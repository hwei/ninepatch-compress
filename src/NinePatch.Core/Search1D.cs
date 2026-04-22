namespace NinePatch.Core;

public readonly record struct SearchResult1D(int Begin, int End, int N);

public static class Search1D
{
    /// <summary>
    /// Scratch buffers allocated once in Run() and reused by TryN.
    /// </summary>
    private sealed class ScratchBuffers
    {
        public float[] Region; // max w*h
        public float[] Down;   // max w*h
        public float[] Up;     // max w*h (same size as region)

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
    /// Uses pre-allocated scratch buffers (no internal allocations for working buffers).
    /// </summary>
    private static bool TryN(
        SoaImage img, int b, int e, int n, float threshold, int axis,
        SoaImage recon, ScratchBuffers scratch)
    {
        int len = e - b;
        int w = img.Width;
        int h = img.Height;

        int regionSize = axis == 1 ? len * h : w * len;
        int downSize = axis == 1 ? n * h : w * n;
        int upSize = regionSize; // upsample back to original region size

        float[] region = scratch.Region;
        float[] down = scratch.Down;
        float[] up = scratch.Up;

        float[][] srcChannels = [img.R, img.G, img.B, img.A];
        float[][] dstChannels = [recon.R, recon.G, recon.B, recon.A];

        for (int ch = 0; ch < 4; ch++)
        {
            float[] srcCh = srcChannels[ch];
            float[] dstCh = dstChannels[ch];

            if (axis == 1)
            {
                // Extract X region [b, e)
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(srcCh, (y * w + b) * 4, region, y * len * 4, len * 4);

                // Clear down buffer (ApplyBoxFilter accumulates with +=)
                System.Array.Clear(down, 0, downSize);
                Resampler.Downsample1D(region.AsSpan(0, regionSize), len, h, n, 1, down.AsSpan(0, downSize));
                Resampler.Upsample1D(down.AsSpan(0, downSize), n, h, len, 1, up.AsSpan(0, upSize));

                // Left pad: copy unchanged
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(srcCh, y * w * 4, dstCh, y * w * 4, b * 4);
                // Upsampled region
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(up, y * len * 4, dstCh, (y * w + b) * 4, len * 4);
                // Right pad: copy unchanged
                int rightBytes = (w - e) * 4;
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(srcCh, (y * w + e) * 4, dstCh, (y * w + e) * 4, rightBytes);
            }
            else
            {
                // Extract Y region [b, e)
                for (int y = b; y < e; y++)
                    Buffer.BlockCopy(srcCh, y * w * 4, region, (y - b) * w * 4, w * 4);

                // Clear down buffer (ApplyBoxFilter accumulates with +=)
                System.Array.Clear(down, 0, downSize);
                Resampler.Downsample1D(region.AsSpan(0, regionSize), w, len, n, 0, down.AsSpan(0, downSize));
                Resampler.Upsample1D(down.AsSpan(0, downSize), w, n, len, 0, up.AsSpan(0, upSize));

                for (int y = 0; y < h; y++)
                {
                    int rowBytes = w * 4;
                    if (y < b || y >= e)
                        Buffer.BlockCopy(srcCh, y * w * 4, dstCh, y * w * 4, rowBytes);
                    else
                        Buffer.BlockCopy(up, (y - b) * w * 4, dstCh, y * w * 4, rowBytes);
                }
            }
        }

        return ErrorMetric.PassesThreshold(img, recon, threshold);
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

        // Allocate recon SoaImage once and all scratch buffers for TryN reuse.
        var recon = SoaImage.Create(w, h);
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
                if (!TryN(img, b, e, maxN, threshold, axis, recon, scratch)) continue;

                int lo = 2, hi = maxN - 1;
                int foundN = maxN;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (TryN(img, b, e, mid, threshold, axis, recon, scratch))
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
