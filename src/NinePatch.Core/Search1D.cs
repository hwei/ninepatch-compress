namespace NinePatch.Core;

public readonly record struct SearchResult1D(int Begin, int End, int N);

public static class Search1D
{
    /// <summary>
    /// Scratch buffers allocated once in Run() and reused by TryN.
    /// Also tracks the dirty region from the previous TryN call so we can
    /// restore it to original data before the next TryN.
    /// </summary>
    private sealed class ScratchBuffers
    {
        public float[] Region; // max w*h
        public float[] Down;   // max w*h
        public float[] Up;     // max w*h (same size as region)
        public int DirtyB = -1, DirtyE = -1; // previous TryN's region

        public ScratchBuffers(int width, int height)
        {
            int size = width * height;
            Region = new float[size];
            Down = new float[size];
            Up = new float[size];
        }
    }

    /// <summary>
    /// Downsample region [b..e] along axis to n pixels, upsample back, measure error.
    /// Before writing, restores the previous TryN's dirty region to original.
    /// </summary>
    private static bool TryN(
        SoaImage img, int b, int e, int n, float threshold, int axis,
        SoaImage recon, ScratchBuffers scratch, PrecomputedSrgb origSrgb)
    {
        int len = e - b;
        int w = img.Width;
        int h = img.Height;

        int regionSize = axis == 1 ? len * h : w * len;
        int downSize = axis == 1 ? n * h : w * n;
        int upSize = regionSize;

        float[] region = scratch.Region;
        float[] down = scratch.Down;
        float[] up = scratch.Up;

        float[][] srcChannels = [img.R, img.G, img.B, img.A];
        float[][] dstChannels = [recon.R, recon.G, recon.B, recon.A];

        // Restore previous TryN's dirty region to original data, so only the
        // current TryN's region differs from original when PassesThreshold runs.
        if (scratch.DirtyB >= 0)
        {
            int db = scratch.DirtyB;
            int de = scratch.DirtyE;
            if (axis == 1)
            {
                int dirtyLen = de - db;
                for (int ch = 0; ch < 4; ch++)
                    for (int y = 0; y < h; y++)
                        Buffer.BlockCopy(srcChannels[ch], (y * w + db) * 4, dstChannels[ch], (y * w + db) * 4, dirtyLen * 4);
            }
            else
            {
                for (int ch = 0; ch < 4; ch++)
                    for (int y = db; y < de; y++)
                        Buffer.BlockCopy(srcChannels[ch], y * w * 4, dstChannels[ch], y * w * 4, w * 4);
            }
        }

        for (int ch = 0; ch < 4; ch++)
        {
            float[] srcCh = srcChannels[ch];

            if (axis == 1)
            {
                // Extract X region [b, e)
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(srcCh, (y * w + b) * 4, region, y * len * 4, len * 4);

                System.Array.Clear(down, 0, downSize);
                Resampler.Downsample1D(region.AsSpan(0, regionSize), len, h, n, 1, down.AsSpan(0, downSize));
                Resampler.Upsample1D(down.AsSpan(0, downSize), n, h, len, 1, up.AsSpan(0, upSize));

                // Write upsampled region only
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(up, y * len * 4, dstChannels[ch], (y * w + b) * 4, len * 4);
            }
            else
            {
                // Extract Y region [b, e)
                for (int y = b; y < e; y++)
                    Buffer.BlockCopy(srcCh, y * w * 4, region, (y - b) * w * 4, w * 4);

                System.Array.Clear(down, 0, downSize);
                Resampler.Downsample1D(region.AsSpan(0, regionSize), w, len, n, 0, down.AsSpan(0, downSize));
                Resampler.Upsample1D(down.AsSpan(0, downSize), w, n, len, 0, up.AsSpan(0, upSize));

                // Write upsampled region rows only
                for (int y = b; y < e; y++)
                    Buffer.BlockCopy(up, (y - b) * w * 4, dstChannels[ch], y * w * 4, w * 4);
            }
        }

        scratch.DirtyB = b;
        scratch.DirtyE = e;

        return ErrorMetric.PassesThreshold(origSrgb, recon, threshold);
    }

    /// <summary>
    /// Exhaustive search over all (b, e) intervals within [margin, L-margin).
    /// For each interval, binary-search the smallest N in [2, (e-b)/2] that passes
    /// the threshold. Return the interval with maximum saving = (e-b) - N.
    ///
    /// Pruning:
    ///   1. Outer loop iterates length L from max down. Since max possible saving
    ///      at length L is L - 2, stop once L - 2 <= bestSaving.
    ///   2. For each (b, e), first probe N = L/2 (least aggressive). If that fails,
    ///      no smaller N can pass, so skip the binary search entirely.
    ///   3. If N = 2 is found (saving = L - 2, the max for this L), break the
    ///      inner b-loop immediately and re-check the outer termination.
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

        // Precompute original sRGB planes — reused across all TryN calls.
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

        // Allocate recon once, pre-filled with original data.
        // TryN only overwrites the region portion; pad regions remain from original.
        // Before each TryN, the previous TryN's region is restored to original.
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

                // Quick reject: if even the least aggressive N fails, skip.
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
                    if (foundN == 2) break; // max saving for this L; no sibling can beat it
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
