using System.Numerics;

namespace NinePatch.Core;

public readonly record struct SearchResult1D(int Begin, int End, int N);

public static class Search1D
{
    /// <summary>
    /// Scratch buffers allocated once in Run() and reused by TryN.
    /// Tracks partial dirty state: only the portion actually written by the
    /// previous TryN (which may have exited early) needs restoring.
    /// </summary>
    private sealed class ScratchBuffers
    {
        public float[] Region; // max w*h
        public float[] Down;   // max w*h
        public float[] Up;     // max w*h (same size as region)

        // Dirty tracking: [DirtyB..DirtyE) x [0..DirtySliceEnd) was written.
        // For axis=1 (X): slice = row, range [0..H)
        // For axis=0 (Y): slice = row range [b..e), tracked as absolute row indices
        public int DirtyB = -1, DirtyE = -1;
        public int DirtySliceEnd = 0; // exclusive: row index (axis=1) or row index (axis=0)

        // Precomputed weights for current TryN
        public Resampler.BoxWeightsPrecomputed BoxWeights;
        public Resampler.BilinearParamsPrecomputed BilinearArgs;

        // Row-level temp buffers (single row, max 1024)
        public float[] SrcRow;  // source row buffer
        public float[] DstRow;  // downsampled row buffer (max N)
        public float[] UpRow;   // upsampled row buffer (max L)

        public ScratchBuffers(int width, int height)
        {
            int size = width * height;
            Region = new float[size];
            Down = new float[size];
            Up = new float[size];
            int maxDim = Math.Max(width, height);
            SrcRow = new float[maxDim];
            DstRow = new float[maxDim];
            UpRow = new float[maxDim];
        }
    }

    private static bool TryN(
        SoaImage img, int b, int e, int n, float threshold, int axis,
        SoaImage recon, ScratchBuffers scratch, PrecomputedSrgb origSrgb)
    {
        int len = e - b;
        int w = img.Width;
        int h = img.Height;
        float[][] srcChannels = [img.R, img.G, img.B, img.A];
        float[][] dstChannels = [recon.R, recon.G, recon.B, recon.A];

        // Restore previous TryN's dirty region
        if (scratch.DirtyB >= 0)
        {
            int db = scratch.DirtyB;
            int de = scratch.DirtyE;
            int sliceEnd = scratch.DirtySliceEnd;
            if (axis == 1)
            {
                int dirtyLen = de - db;
                for (int y = 0; y < sliceEnd; y++)
                {
                    int rowBytes = dirtyLen * 4;
                    for (int ch = 0; ch < 4; ch++)
                        Buffer.BlockCopy(srcChannels[ch], (y * w + db) * 4, dstChannels[ch], (y * w + db) * 4, rowBytes);
                }
            }
            else
            {
                for (int y = db; y < de; y++)
                {
                    int rowBytes = w * 4;
                    for (int ch = 0; ch < 4; ch++)
                        Buffer.BlockCopy(srcChannels[ch], y * w * 4, dstChannels[ch], y * w * 4, rowBytes);
                }
            }
        }

        if (axis == 1)
        {
            return TryN_X(b, e, len, n, w, h, threshold, srcChannels, dstChannels, recon, scratch, origSrgb);
        }
        else
        {
            return TryN_Y(b, e, len, n, w, h, threshold, srcChannels, dstChannels, recon, scratch, origSrgb);
        }
    }

    /// <summary>X-axis TryN: process row by row with early exit.</summary>
    private static bool TryN_X(
        int b, int e, int len, int n, int w, int h, float threshold,
        float[][] srcChannels, float[][] dstChannels, SoaImage recon, ScratchBuffers scratch, PrecomputedSrgb origSrgb)
    {
        // Precompute weights once
        scratch.BoxWeights = Resampler.BuildRowBoxWeights(len, n);
        scratch.BilinearArgs = Resampler.BuildRowBilinearParams(n, len);
        var boxWeights = scratch.BoxWeights;
        var bilinearArgs = scratch.BilinearArgs;
        var srcRow = scratch.SrcRow;
        var dstRow = scratch.DstRow;
        var upRow = scratch.UpRow;

        for (int y = 0; y < h; y++)
        {
            // Process each channel for this row
            for (int ch = 0; ch < 4; ch++)
            {
                // Extract source row [b..e)
                Buffer.BlockCopy(srcChannels[ch], (y * w + b) * 4, srcRow, 0, len * 4);

                // Downsample + upsample this single row
                Resampler.Downsample1DRow(srcRow.AsSpan(0, len), len, boxWeights, dstRow.AsSpan(0, n));
                Resampler.Upsample1DRow(dstRow.AsSpan(0, n), n, bilinearArgs, upRow.AsSpan(0, len));

                // Write reconstructed row back
                Buffer.BlockCopy(upRow, 0, dstChannels[ch], (y * w + b) * 4, len * 4);
            }

            // Early exit check on this row's [b..e) region
            if (!ErrorMetric.PassesThresholdSliceX(origSrgb, recon, y, b, e, w, threshold))
            {
                scratch.DirtyB = b;
                scratch.DirtyE = e;
                scratch.DirtySliceEnd = y + 1;
                return false;
            }
        }

        scratch.DirtyB = b;
        scratch.DirtyE = e;
        scratch.DirtySliceEnd = h;
        return true;
    }

    /// <summary>Y-axis TryN: process column-block by column-block with early exit.</summary>
    private static bool TryN_Y(
        int b, int e, int len, int n, int w, int h, float threshold,
        float[][] srcChannels, float[][] dstChannels, SoaImage recon, ScratchBuffers scratch, PrecomputedSrgb origSrgb)
    {
        // Precompute weights once — used by column-resample functions
        scratch.BoxWeights = Resampler.BuildRowBoxWeights(len, n);
        scratch.BilinearArgs = Resampler.BuildRowBilinearParams(n, len);
        var boxWeights = scratch.BoxWeights;
        var bilinearArgs = scratch.BilinearArgs;

        int vecLen = Vector<float>.Count;
        int numBlocks = (w + vecLen - 1) / vecLen;

        for (int blockIdx = 0; blockIdx < numBlocks; blockIdx++)
        {
            int x0 = blockIdx * vecLen;
            int blockW = Math.Min(vecLen, w - x0);

            // Extract region [b..e) x [x0..x0+blockW) into contiguous buffer
            int regionW = blockW;
            int regionH = len;
            int regionSize = regionW * regionH;

            for (int ch = 0; ch < 4; ch++)
            {
                for (int y = b; y < e; y++)
                {
                    Buffer.BlockCopy(srcChannels[ch], (y * w + x0) * 4,
                        scratch.Region, ((y - b) * regionW) * 4, blockW * 4);
                }

                int downSize = regionW * n;
                int upSize = regionSize;

                Resampler.Downsample1DCol(scratch.Region.AsSpan(0, regionSize), regionH, regionW,
                    boxWeights, scratch.Down.AsSpan(0, downSize));
                Resampler.Upsample1DCol(scratch.Down.AsSpan(0, downSize), n, regionW,
                    bilinearArgs, scratch.Up.AsSpan(0, upSize));

                for (int y = b; y < e; y++)
                {
                    Buffer.BlockCopy(scratch.Up, ((y - b) * regionW) * 4,
                        dstChannels[ch], (y * w + x0) * 4, blockW * 4);
                }
            }

            if (!ErrorMetric.PassesThresholdSliceY(origSrgb, recon, b, e, x0, blockW, w, threshold))
            {
                scratch.DirtyB = b;
                scratch.DirtyE = e;
                scratch.DirtySliceEnd = e;
                return false;
            }
        }

        scratch.DirtyB = b;
        scratch.DirtyE = e;
        scratch.DirtySliceEnd = e;
        return true;
    }

    // --- Variance pre-filter ---

    /// <summary>
    /// Computes per-position variance prefix-sum for the given axis.
    /// X axis (axis=1): per-column variance across all rows, max across channels.
    /// Y axis (axis=0): per-row variance across all columns, max across channels.
    /// prefixVariance[i] = sum of per-position max-channel-variance for positions [0..i).
    /// </summary>
    internal static float[] ComputeAxisVariancePrefixSum(SoaImage img, int axis, PrecomputedSrgb origSrgb)
    {
        int l = axis == 1 ? img.Width : img.Height;
        int otherLen = axis == 1 ? img.Height : img.Width;
        int w = img.Width;
        float[][] srgbPlanes = [origSrgb.R, origSrgb.G, origSrgb.B, origSrgb.Alpha];
        float[] prefixSum = new float[l + 1];

        // Per-channel accumulators: [channel, position]
        float[,] sum = new float[4, l];
        float[,] sumSq = new float[4, l];

        if (axis == 1)
        {
            // Per column x: accumulate across all rows y
            for (int y = 0; y < otherLen; y++)
            {
                int rowOffset = y * w;
                for (int x = 0; x < l; x++)
                {
                    int idx = rowOffset + x;
                    for (int ch = 0; ch < 4; ch++)
                    {
                        float v = srgbPlanes[ch][idx];
                        sum[ch, x] += v;
                        sumSq[ch, x] += v * v;
                    }
                }
            }
        }
        else
        {
            // Per row y: accumulate across all columns x
            for (int y = 0; y < l; y++)
            {
                int rowOffset = y * w;
                for (int x = 0; x < otherLen; x++)
                {
                    int idx = rowOffset + x;
                    for (int ch = 0; ch < 4; ch++)
                    {
                        float v = srgbPlanes[ch][idx];
                        sum[ch, y] += v;
                        sumSq[ch, y] += v * v;
                    }
                }
            }
        }

        // Build prefix sum of max-per-channel variance
        float invN = 1f / otherLen;
        float running = 0f;
        for (int i = 0; i < l; i++)
        {
            float maxVar = 0f;
            for (int ch = 0; ch < 4; ch++)
            {
                float m = sum[ch, i] * invN;
                float mSq = sumSq[ch, i] * invN;
                float var = mSq - m * m; // Var = E[x^2] - E[x]^2
                if (var > maxVar) maxVar = var;
            }
            running += maxVar;
            prefixSum[i + 1] = running;
        }

        return prefixSum;
    }

    /// <summary>O(1) variance lookup for interval [b, e) using prefix-sum table.</summary>
    internal static float VarianceForInterval(float[] prefixSum, int b, int e)
    {
        int len = e - b;
        if (len <= 0) return 0f;
        return (prefixSum[e] - prefixSum[b]) / len;
    }

    /// <summary>
    /// Computes global variance of the entire image in sRGB space, max across channels.
    /// Used to derive an adaptive variance threshold.
    /// </summary>
    internal static float ComputeGlobalVariance(SoaImage img, PrecomputedSrgb origSrgb)
    {
        int n = img.PixelCount;
        float[][] planes = [origSrgb.R, origSrgb.G, origSrgb.B, origSrgb.Alpha];
        float maxVar = 0f;

        for (int ch = 0; ch < 4; ch++)
        {
            int vecLen = Vector<float>.Count;
            int vecEnd = (n / vecLen) * vecLen;
            double dSum = 0, dSumSq = 0;
            var vSum = Vector<float>.Zero;
            var vSumSq = Vector<float>.Zero;

            for (int i = 0; i < vecEnd; i += vecLen)
            {
                var v = new Vector<float>(planes[ch], i);
                vSum += v;
                vSumSq += v * v;
            }
            for (int j = 0; j < vecLen; j++)
            {
                dSum += vSum[j];
                dSumSq += vSumSq[j];
            }
            for (int i = vecEnd; i < n; i++)
            {
                float v = planes[ch][i];
                dSum += v;
                dSumSq += v * v;
            }

            double mean = dSum / n;
            double meanSq = dSumSq / n;
            float variance = (float)(meanSq - mean * mean);
            if (variance > maxVar) maxVar = variance;
        }

        return maxVar;
    }

    /// <summary>
    /// Adaptive variance threshold: K * globalVariance, with a floor of 0.01.
    /// K = 3.0 by design.
    /// </summary>
    internal static float ComputeVarianceThreshold(float globalVariance, float k = 3.0f, float floor = 0.01f)
    {
        float threshold = k * globalVariance;
        return Math.Max(threshold, floor);
    }

    /// <summary>
    /// Computes per-axis gradient magnitude: for each position i, the L1-mean
    /// absolute difference between adjacent positions along the axis, averaged
    /// over the orthogonal axis, max over RGBA channels.
    /// Returns array of length L-1 (gradient between position i and i+1).
    /// </summary>
    internal static float[] ComputeAxisGradient(SoaImage img, int axis, PrecomputedSrgb origSrgb)
    {
        int l = axis == 1 ? img.Width : img.Height;
        int otherLen = axis == 1 ? img.Height : img.Width;
        int w = img.Width;
        float[][] planes = [origSrgb.R, origSrgb.G, origSrgb.B, origSrgb.Alpha];
        int gradientLen = l - 1;
        float[] gradient = new float[gradientLen];
        int vecLen = Vector<float>.Count;
        float invOther = 1f / otherLen;

        for (int ch = 0; ch < 4; ch++)
        {
            if (axis == 1)
            {
                // X axis: vecLen gradient positions can be processed in parallel
                // because columns are contiguous in memory within a row.
                for (int i = 0; i < gradientLen;)
                {
                    int remaining = gradientLen - i;
                    if (remaining >= vecLen)
                    {
                        var acc = Vector<float>.Zero;
                        for (int o = 0; o < otherLen; o++)
                        {
                            int idx1 = o * w + i;
                            int idx2 = o * w + i + 1;
                            var v1 = new Vector<float>(planes[ch], idx1);
                            var v2 = new Vector<float>(planes[ch], idx2);
                            acc += Vector.Abs(v2 - v1);
                        }
                        acc *= invOther;
                        for (int j = 0; j < vecLen; j++)
                        {
                            float val = acc[j];
                            if (val > gradient[i + j]) gradient[i + j] = val;
                        }
                        i += vecLen;
                    }
                    else
                    {
                        float sumDiff = 0f;
                        for (int o = 0; o < otherLen; o++)
                        {
                            int idx1 = o * w + i;
                            int idx2 = o * w + i + 1;
                            sumDiff += MathF.Abs(planes[ch][idx2] - planes[ch][idx1]);
                        }
                        float avgDiff = sumDiff * invOther;
                        if (avgDiff > gradient[i]) gradient[i] = avgDiff;
                        i++;
                    }
                }
            }
            else
            {
                // Y axis: rows are separated by stride W, so SIMD must
                // traverse the orthogonal dimension (columns) instead.
                for (int i = 0; i < gradientLen; i++)
                {
                    int offset1 = i * w;
                    int offset2 = (i + 1) * w;
                    int o = 0;
                    float sumDiff = 0f;
                    for (; o < otherLen - vecLen + 1; o += vecLen)
                    {
                        var v1 = new Vector<float>(planes[ch], offset1 + o);
                        var v2 = new Vector<float>(planes[ch], offset2 + o);
                        var diff = Vector.Abs(v2 - v1);
                        for (int j = 0; j < vecLen; j++)
                            sumDiff += diff[j];
                    }
                    for (; o < otherLen; o++)
                        sumDiff += MathF.Abs(planes[ch][offset2 + o] - planes[ch][offset1 + o]);
                    float avgDiff = sumDiff * invOther;
                    if (avgDiff > gradient[i]) gradient[i] = avgDiff;
                }
            }
        }

        return gradient;
    }

    /// <summary>
    /// Extracts edge positions from a gradient array using a hybrid absolute + relative threshold.
    /// A position i is an edge position iff g[i] >= max(absThreshold, P90(g)).
    /// Returns sorted array of edge position indices.
    /// </summary>
    internal static int[] ExtractEdgePositions(float[] gradient, float absThreshold = 8f / 255f)
    {
        if (gradient.Length == 0) return [];

        // Compute 90th percentile
        var sorted = (float[])gradient.Clone();
        Array.Sort(sorted);
        int p90Index = (int)Math.Floor(sorted.Length * 0.9);
        p90Index = Math.Max(0, Math.Min(p90Index, sorted.Length - 1));
        float percentileThreshold = sorted[p90Index];

        float effectiveThreshold = MathF.Max(absThreshold, percentileThreshold);

        var positions = new List<int>();
        for (int i = 0; i < gradient.Length; i++)
        {
            if (gradient[i] >= effectiveThreshold)
                positions.Add(i);
        }
        return positions.ToArray();
    }

    /// <summary>
    /// Builds restricted candidate sets B and E from detected edge positions.
    /// B = {margin} U {edge, edge+1, edge+2 : edge in edges, clamped to [margin, hiBound)}
    /// E = {hiBound} U {edge-1, edge, edge+1 : edge in edges, clamped to (margin, hiBound]}
    /// When edges is empty, falls back to stride-sampled positions every L/16.
    /// Returns sorted, deduplicated arrays.
    /// </summary>
    internal static (int[] B, int[] E) BuildCandidateSets(int[] edges, int margin, int hiBound)
    {
        var bSet = new HashSet<int>();
        var eSet = new HashSet<int>();

        if (edges.Length == 0)
        {
            // Stride-sampled fallback: every L/16 positions
            int stride = Math.Max(1, (hiBound - margin) / 16);
            bSet.Add(margin);
            eSet.Add(hiBound);
            for (int pos = margin + stride; pos < hiBound; pos += stride)
            {
                bSet.Add(pos);
                eSet.Add(pos);
            }
        }
        else
        {
            bSet.Add(margin);
            eSet.Add(hiBound);

            foreach (int edge in edges)
            {
                // B candidates: edge, edge+1, edge+2
                for (int offset = 0; offset <= 2; offset++)
                {
                    int pos = edge + offset;
                    if (pos >= margin && pos < hiBound) bSet.Add(pos);
                }

                // E candidates: edge-1, edge, edge+1 (left-leaning from edge position)
                for (int offset = -1; offset <= 1; offset++)
                {
                    int pos = edge + offset;
                    if (pos > margin && pos <= hiBound) eSet.Add(pos);
                }
            }
        }

        var bArray = bSet.OrderBy(x => x).ToArray();
        var eArray = eSet.OrderBy(x => x).ToArray();
        return (bArray, eArray);
    }

    /// <summary>
    /// Detects if an axis is noisy (incompressible) by checking adjacent-position
    /// squared-differences. For noise, adjacent positions differ significantly.
    /// For smooth/gradient images, adjacent positions are similar.
    /// </summary>
    private static bool DetectNoisyAxis(SoaImage img, int axis, PrecomputedSrgb origSrgb,
        int l, int w, float varianceThreshold)
    {
        // For axis=1 (X): compare adjacent columns x and x+1, averaged over all rows
        // For axis=0 (Y): compare adjacent rows y and y+1, averaged over all columns
        float[][] planes = [origSrgb.R, origSrgb.G, origSrgb.B, origSrgb.Alpha];
        int otherLen = axis == 1 ? img.Height : img.Width;
        float invOther = 1f / otherLen;

        // Compute mean squared-difference between adjacent positions for each channel
        float maxAdjDiff = 0f;
        for (int ch = 0; ch < 4; ch++)
        {
            float sumSqDiff = 0f;
            int count = 0;
            for (int pos = 0; pos < l - 1; pos++)
            {
                for (int o = 0; o < otherLen; o++)
                {
                    int idx1 = axis == 1 ? o * w + pos : pos * w + o;
                    int idx2 = axis == 1 ? o * w + pos + 1 : (pos + 1) * w + o;
                    float diff = planes[ch][idx1] - planes[ch][idx2];
                    sumSqDiff += diff * diff;
                    count++;
                }
            }
            float avgSqDiff = count > 0 ? sumSqDiff / count : 0f;
            if (avgSqDiff > maxAdjDiff) maxAdjDiff = avgSqDiff;
        }

        // If max adjacent squared-diff is below 50% of variance threshold, the axis is smooth
        // (For gradient: adjacent rows are identical → adjDiff ≈ 0)
        // (For noise: adjacent rows differ a lot → adjDiff ≈ 2 * per-position variance)
        return maxAdjDiff > varianceThreshold * 0.5f;
    }

    /// <summary>
    /// Exhaustive search over all (b, e) intervals within [margin, L-margin).
    /// </summary>
    public static SearchResult1D? Run(
        SoaImage img, int axis, float threshold, int margin = 0)
    {
        int l = axis == 1 ? img.Width : img.Height;
        int loBound = margin;
        int hiBound = l - margin;
        if (hiBound - loBound < 4) return null;

        int w = img.Width;
        int h = img.Height;

        // Precompute original sRGB planes
        int pixelCount = w * h;
        var origSrgb = new PrecomputedSrgb(
            R: new float[pixelCount],
            G: new float[pixelCount],
            B: new float[pixelCount],
            Alpha: (float[])img.A.Clone());

        var vLen = System.Numerics.Vector<float>.Count;
        var vEnd = (pixelCount / vLen) * vLen;
        for (int i = 0; i < vEnd; i += vLen)
        {
            var vr = new System.Numerics.Vector<float>(img.R, i);
            var vg = new System.Numerics.Vector<float>(img.G, i);
            var vb = new System.Numerics.Vector<float>(img.B, i);
            var sr = ColorSpace.LinearToSrgbSimd(vr);
            var sg = ColorSpace.LinearToSrgbSimd(vg);
            var sb = ColorSpace.LinearToSrgbSimd(vb);
            for (int j = 0; j < vLen; j++)
            {
                origSrgb.R[i + j] = sr[j];
                origSrgb.G[i + j] = sg[j];
                origSrgb.B[i + j] = sb[j];
            }
        }
        for (int i = vEnd; i < pixelCount; i++)
        {
            origSrgb.R[i] = ColorSpace.LinearToSrgbByte(img.R[i]) / 255f;
            origSrgb.G[i] = ColorSpace.LinearToSrgbByte(img.G[i]) / 255f;
            origSrgb.B[i] = ColorSpace.LinearToSrgbByte(img.B[i]) / 255f;
        }

        // Precompute variance prefix-sum table for this axis
        float[] prefixVariance = ComputeAxisVariancePrefixSum(img, axis, origSrgb);
        float globalVariance = ComputeGlobalVariance(img, origSrgb);
        float varianceThreshold = ComputeVarianceThreshold(globalVariance);

        // Detect noisy axis: if adjacent positions have high squared-difference
        // consistently (noise pattern), the axis is incompressible via box-downsampling.
        // This checks between-position variation (not within-position variance),
        // which correctly identifies gradients (low adjacent-diff) vs noise (high adjacent-diff).
        if (DetectNoisyAxis(img, axis, origSrgb, l, w, varianceThreshold))
            return null;

        // Gradient-derived candidate restriction
        float[] axisGradient = ComputeAxisGradient(img, axis, origSrgb);
        int[] edgePositions = ExtractEdgePositions(axisGradient);
        var (bCandidates, eCandidates) = BuildCandidateSets(edgePositions, loBound, hiBound);

        // Build (b, e) pair list from cartesian product, filtered and sorted by len descending
        var pairs = new List<(int b, int e, int len)>();
        foreach (int b in bCandidates)
        {
            foreach (int e in eCandidates)
            {
                if (e - b >= 4 && e <= hiBound)
                    pairs.Add((b, e, e - b));
            }
        }
        pairs.Sort((a, b2) => b2.len.CompareTo(a.len));

        // Allocate recon once, pre-filled with original data
        var recon = SoaImage.Create(w, h);
        Buffer.BlockCopy(img.R, 0, recon.R, 0, pixelCount * sizeof(float));
        Buffer.BlockCopy(img.G, 0, recon.G, 0, pixelCount * sizeof(float));
        Buffer.BlockCopy(img.B, 0, recon.B, 0, pixelCount * sizeof(float));
        Buffer.BlockCopy(img.A, 0, recon.A, 0, pixelCount * sizeof(float));

        var scratch = new ScratchBuffers(w, h);

        int bestSaving = -1;
        SearchResult1D? best = null;

        foreach (var (b, e, len) in pairs)
        {
            if (len - 2 <= bestSaving) break;
            int maxN = len / 2;

            // Variance pre-filter: skip intervals with too much high-frequency content
            float intervalVariance = VarianceForInterval(prefixVariance, b, e);
            if (intervalVariance > varianceThreshold) continue;

            // Quick reject
            if (!TryN(img, b, e, maxN, threshold, axis, recon, scratch, origSrgb)) continue;
            int lo = 2, hi = maxN - 1;
            int foundN = maxN;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (TryN(img, b, e, mid, threshold, axis, recon, scratch, origSrgb))
                {
                    foundN = mid;
                    hi = mid - 1;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            int saving = len - foundN;
            if (saving > bestSaving)
            {
                bestSaving = saving;
                best = new SearchResult1D(b, e, foundN);
                if (foundN == 2) break;
            }
        }

        // Safety-net fallback: if restricted enumeration found no valid split,
        // fall back to full exhaustive enumeration (Decision 5, design.md).
        if (best is null)
        {
            int maxLen = hiBound - loBound;
            for (int len = maxLen; len >= 4; len--)
            {
                if (len - 2 <= bestSaving) break;
                int maxN = len / 2;

                for (int b = loBound; b + len <= hiBound; b++)
                {
                    int e = b + len;

                    float intervalVariance = VarianceForInterval(prefixVariance, b, e);
                    if (intervalVariance > varianceThreshold) continue;

                    if (!TryN(img, b, e, maxN, threshold, axis, recon, scratch, origSrgb)) continue;

                    int lo = 2, hi = maxN - 1;
                    int foundN = maxN;
                    while (lo <= hi)
                    {
                        int mid = (lo + hi) / 2;
                        if (TryN(img, b, e, mid, threshold, axis, recon, scratch, origSrgb))
                        {
                            foundN = mid;
                            hi = mid - 1;
                        }
                        else
                        {
                            lo = mid + 1;
                        }
                    }

                    int saving = len - foundN;
                    if (saving > bestSaving)
                    {
                        bestSaving = saving;
                        best = new SearchResult1D(b, e, foundN);
                        if (foundN == 2) break;
                    }
                }

                if (len == 4 && bestSaving == -1) return null;
            }
        }

        return best;
    }

    public static SearchResult1D? SearchX(SoaImage img, float threshold, int margin = 0)
        => Run(img, axis: 1, threshold, margin);

    public static SearchResult1D? SearchY(SoaImage img, float threshold, int margin = 0)
        => Run(img, axis: 0, threshold, margin);
}
