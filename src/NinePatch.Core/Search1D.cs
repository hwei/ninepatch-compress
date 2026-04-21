namespace NinePatch.Core;

public readonly record struct SearchResult1D(int Begin, int End, int N);

public static class Search1D
{
    /// <summary>
    /// Downsample region [b..e] along axis to n pixels, upsample back.
    /// Returns full-image-sized reconstructed array with unchanged pixels outside [b..e].
    /// </summary>
    private static float[] Compress1D(float[] img, int srcW, int srcH, int b, int e, int n, int axis)
    {
        int len = e - b;
        var dst = new float[srcW * srcH * 4];

        if (axis == 1)
        {
            // Extract X region [b, e) in column-major order: (col * srcH + row) * 4
            var region = new float[len * srcH * 4];
            for (int y = 0; y < srcH; y++)
            for (int x = b; x < e; x++)
            {
                int si = (y * srcW + x) * 4;
                int di = ((x - b) * srcH + y) * 4;
                Buffer.BlockCopy(img, si * 4, region, di * 4, 16);
            }

            // Downsample + upsample (axis=1 uses column-major-like indexing)
            float[] down = Resampler.Downsample1D(region, len, srcH, n, 1);
            float[] up = Resampler.Upsample1D(down, n, srcH, len, 1);

            // Copy back: left pad, upsampled region, right pad
            for (int y = 0; y < srcH; y++)
            {
                // Left pad (unchanged)
                for (int x = 0; x < b; x++)
                {
                    int si = (y * srcW + x) * 4;
                    Buffer.BlockCopy(img, si * 4, dst, si * 4, 16);
                }
                // Upsampled region — up output is column-major-like: (col * srcH + row) * 4
                for (int x = 0; x < len; x++)
                {
                    int si = (x * srcH + y) * 4;
                    int di = (y * srcW + b + x) * 4;
                    Buffer.BlockCopy(up, si * 4, dst, di * 4, 16);
                }
                // Right pad (unchanged)
                for (int x = e; x < srcW; x++)
                {
                    int si = (y * srcW + x) * 4;
                    Buffer.BlockCopy(img, si * 4, dst, si * 4, 16);
                }
            }
        }
        else
        {
            // Extract Y region [b, e)
            var region = new float[srcW * len * 4];
            for (int y = b; y < e; y++)
            for (int x = 0; x < srcW; x++)
            {
                int si = (y * srcW + x) * 4;
                int di = ((y - b) * srcW + x) * 4;
                Buffer.BlockCopy(img, si * 4, region, di * 4, 16);
            }

            float[] down = Resampler.Downsample1D(region, srcW, len, n, 0);
            float[] up = Resampler.Upsample1D(down, srcW, n, len, 0);

            for (int y = 0; y < srcH; y++)
            for (int x = 0; x < srcW; x++)
            {
                int di = (y * srcW + x) * 4;
                if (y < b || y >= e)
                {
                    // Pad region: copy unchanged
                    Buffer.BlockCopy(img, di * 4, dst, di * 4, 16);
                }
                else
                {
                    // Stretch region: copy from upsampled (row-major: (y-b) * srcW + x)
                    int si = ((y - b) * srcW + x) * 4;
                    Buffer.BlockCopy(up, si * 4, dst, di * 4, 16);
                }
            }
        }

        return dst;
    }

    private static (float error, bool passes) TryN(
        float[] img, int srcW, int srcH, int b, int e, int n, float threshold, int axis)
    {
        float[] recon = Compress1D(img, srcW, srcH, b, e, n, axis);
        float err = ErrorMetric.MaxError(img, recon);
        return (err, err <= threshold);
    }

    public static SearchResult1D? Run(
        ReadOnlySpan<float> img, int width, int height, int axis,
        float threshold, int margin = 0, int shrinkStep = 2)
    {
        // Copy once — all TryN calls need a mutable array
        float[] imgArr = img.ToArray();

        int l = axis == 1 ? width : height;
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
                var (err, passes) = TryN(imgArr, width, height, b, e, midN, threshold, axis);
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

            float errLeft = TryN(imgArr, width, height, b + bStep, e, maxN, threshold, axis).error;
            float errRight = TryN(imgArr, width, height, b, e - bStep, maxN, threshold, axis).error;

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

    public static SearchResult1D? SearchX(ReadOnlySpan<float> img, int width, int height, float threshold, int margin = 0, int shrinkStep = 2)
        => Run(img, width, height, axis: 1, threshold, margin, shrinkStep);

    public static SearchResult1D? SearchY(ReadOnlySpan<float> img, int width, int height, float threshold, int margin = 0, int shrinkStep = 2)
        => Run(img, width, height, axis: 0, threshold, margin, shrinkStep);
}
