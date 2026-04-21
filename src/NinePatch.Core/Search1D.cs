namespace NinePatch.Core;

public readonly record struct SearchResult1D(int Begin, int End, int N);

public static class Search1D
{
    /// <summary>
    /// Downsample region [b..e] along axis to n pixels, upsample back.
    /// Returns full-image-sized SoaImage with unchanged pixels outside [b..e].
    /// </summary>
    private static SoaImage Compress1D(SoaImage img, int b, int e, int n, int axis)
    {
        int len = e - b;
        int w = img.Width;
        int h = img.Height;
        var dst = SoaImage.Create(w, h);

        float[][] srcChannels = [img.R, img.G, img.B, img.A];
        float[][] dstChannels = [dst.R, dst.G, dst.B, dst.A];

        for (int ch = 0; ch < 4; ch++)
        {
            float[] srcCh = srcChannels[ch];
            float[] dstCh = dstChannels[ch];

            if (axis == 1)
            {
                // Extract X region [b, e), row-major: each row is contiguous
                var region = new float[len * h];
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(srcCh, (y * w + b) * 4, region, y * len * 4, len * 4);

                float[] down = Resampler.Downsample1D(region, len, h, n, 1);
                float[] up = Resampler.Upsample1D(down, n, h, len, 1);

                // Left pad
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(srcCh, y * w * 4, dstCh, y * w * 4, b * 4);
                // Upsampled region
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(up, y * len * 4, dstCh, (y * w + b) * 4, len * 4);
                // Right pad
                int rightBytes = (w - e) * 4;
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(srcCh, (y * w + e) * 4, dstCh, (y * w + e) * 4, rightBytes);
            }
            else
            {
                // Extract Y region [b, e), rows are contiguous in row-major
                var region = new float[w * len];
                for (int y = b; y < e; y++)
                    Buffer.BlockCopy(srcCh, y * w * 4, region, (y - b) * w * 4, w * 4);

                float[] down = Resampler.Downsample1D(region, w, len, n, 0);
                float[] up = Resampler.Upsample1D(down, w, n, len, 0);

                for (int y = 0; y < h; y++)
                {
                    int rowBytes = w * 4;
                    if (y < b || y >= e)
                    {
                        // Pad region: copy unchanged
                        Buffer.BlockCopy(srcCh, y * w * 4, dstCh, y * w * 4, rowBytes);
                    }
                    else
                    {
                        // Stretch region: copy from upsampled (row (y-b))
                        Buffer.BlockCopy(up, (y - b) * w * 4, dstCh, y * w * 4, rowBytes);
                    }
                }
            }
        }

        return dst;
    }

    private static (float error, bool passes) TryN(
        SoaImage img, int b, int e, int n, float threshold, int axis)
    {
        SoaImage recon = Compress1D(img, b, e, n, axis);
        float err = ErrorMetric.MaxError(img, recon);
        return (err, err <= threshold);
    }

    public static SearchResult1D? Run(
        SoaImage img, int axis,
        float threshold, int margin = 0, int shrinkStep = 2)
    {
        int l = axis == 1 ? img.Width : img.Height;
        int b = margin;
        int e = l - margin;

        if (e - b < 4) return null;

        int iteration = 0;
        while (e - b >= 4)
        {
            iteration++;
            int intervalLen = e - b;
            int maxN = intervalLen / 2;

            if (maxN < 2) break;

            int loN = 2, hiN = maxN;
            int? foundN = null;

            while (loN <= hiN)
            {
                int midN = (loN + hiN) / 2;
                var (err, passes) = TryN(img, b, e, midN, threshold, axis);
                if (passes)
                {
                    foundN = midN;
                    hiN = midN - 1;
                }
                else
                {
                    loN = midN + 1;
                }
            }

            if (foundN is not null)
                return new SearchResult1D(b, e, foundN.Value);

            int bStep = Math.Min(shrinkStep, (e - b - 4) / 2);
            if (bStep < 1) break;

            int leftLen = e - (b + bStep);
            int rightLen = (e - bStep) - b;
            int leftMaxN = Math.Max(2, leftLen / 2);
            int rightMaxN = Math.Max(2, rightLen / 2);

            float errLeft = TryN(img, b + bStep, e, leftMaxN, threshold, axis).error;
            float errRight = TryN(img, b, e - bStep, rightMaxN, threshold, axis).error;

            if (errLeft < errRight)
                b += bStep;
            else if (errRight < errLeft)
                e -= bStep;
            else
            {
                if (iteration % 2 == 1)
                    b += bStep;
                else
                    e -= bStep;
            }
        }

        return null;
    }

    public static SearchResult1D? SearchX(SoaImage img, float threshold, int margin = 0, int shrinkStep = 2)
        => Run(img, axis: 1, threshold, margin, shrinkStep);

    public static SearchResult1D? SearchY(SoaImage img, float threshold, int margin = 0, int shrinkStep = 2)
        => Run(img, axis: 0, threshold, margin, shrinkStep);
}
