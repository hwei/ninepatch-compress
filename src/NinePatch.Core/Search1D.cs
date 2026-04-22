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
        // Precompute weights once
        scratch.BoxWeights = Resampler.BuildRowBoxWeights(len, n);
        scratch.BilinearArgs = Resampler.BuildRowBilinearParams(n, len);
        var boxWeights = scratch.BoxWeights;
        var bilinearArgs = scratch.BilinearArgs;

        // For Y axis: box-down is along rows (each row independently), but the
        // data is non-contiguous. We extract a column region per row, downsample
        // along the column axis, then upsample back.
        //
        // Strategy: process the full column [x0..x0+vecLen) for all rows at once
        // using existing Downsample1D/Upsample1D with srcW=vecLen, srcH=len.
        // This is the simplest approach that keeps SIMD working on the "other" dimension.

        int vecLen = Vector<float>.Count;
        int numBlocks = (w + vecLen - 1) / vecLen;

        for (int blockIdx = 0; blockIdx < numBlocks; blockIdx++)
        {
            int x0 = blockIdx * vecLen;
            int blockW = Math.Min(vecLen, w - x0);

            // Extract region [b..e) x [x0..x0+blockW) for each channel
            int regionW = blockW;
            int regionH = len;
            int regionSize = regionW * regionH;

            for (int ch = 0; ch < 4; ch++)
            {
                // Extract: src row y, cols x0..x0+blockW -> region row (y-b), cols 0..blockW
                for (int y = b; y < e; y++)
                {
                    Buffer.BlockCopy(srcChannels[ch], (y * w + x0) * 4,
                        scratch.Region, ((y - b) * regionW) * 4, blockW * 4);
                }

                int downSize = regionW * n;
                int upSize = regionSize;

                System.Array.Clear(scratch.Down, 0, downSize);
                Resampler.Downsample1D(scratch.Region.AsSpan(0, regionSize), regionW, regionH, n, 0,
                    scratch.Down.AsSpan(0, downSize));
                Resampler.Upsample1D(scratch.Down.AsSpan(0, downSize), regionW, n, len, 0,
                    scratch.Up.AsSpan(0, upSize));

                // Write back to recon
                for (int y = b; y < e; y++)
                {
                    Buffer.BlockCopy(scratch.Up, ((y - b) * regionW) * 4,
                        dstChannels[ch], (y * w + x0) * 4, blockW * 4);
                }
            }

            // Early exit: check this column block across all rows [b..e)
            if (!ErrorMetric.PassesThresholdSliceY(origSrgb, recon, b, e, x0, blockW, w, threshold))
            {
                scratch.DirtyB = b;
                scratch.DirtyE = e;
                scratch.DirtySliceEnd = e; // all rows [b..e) were written
                return false;
            }
        }

        scratch.DirtyB = b;
        scratch.DirtyE = e;
        scratch.DirtySliceEnd = e;
        return true;
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

        // Allocate recon once, pre-filled with original data
        var recon = SoaImage.Create(w, h);
        Buffer.BlockCopy(img.R, 0, recon.R, 0, pixelCount * sizeof(float));
        Buffer.BlockCopy(img.G, 0, recon.G, 0, pixelCount * sizeof(float));
        Buffer.BlockCopy(img.B, 0, recon.B, 0, pixelCount * sizeof(float));
        Buffer.BlockCopy(img.A, 0, recon.A, 0, pixelCount * sizeof(float));

        var scratch = new ScratchBuffers(w, h);

        int bestSaving = -1;
        SearchResult1D? best = null;

        int maxLen = hiBound - loBound;
        for (int len = maxLen; len >= 4; len--)
        {
            if (len - 2 <= bestSaving) break;
            int maxN = len / 2;

            for (int b = loBound; b + len <= hiBound; b++)
            {
                int e = b + len;

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
        }

        return best;
    }

    public static SearchResult1D? SearchX(SoaImage img, float threshold, int margin = 0)
        => Run(img, axis: 1, threshold, margin);

    public static SearchResult1D? SearchY(SoaImage img, float threshold, int margin = 0)
        => Run(img, axis: 0, threshold, margin);
}
