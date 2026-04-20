namespace NinePatch.Core;

/// <summary>
/// 1D box downsample and bilinear upsample.
/// Data layout: (height, width, 4) interleaved RGBA float array, row-major.
/// </summary>
public static class Resampler
{
    /// <summary>Build (dstLen, srcLen) weight matrix for box filter.</summary>
    public static float[,] BuildBoxWeights(int srcLen, int dstLen)
    {
        var weights = new float[dstLen, srcLen];
        float scale = (float)srcLen / dstLen;
        for (int d = 0; d < dstLen; d++)
        {
            float lo = d * scale;
            float hi = (d + 1) * scale;
            int i0 = (int)MathF.Floor(lo);
            int i1 = (int)MathF.Ceiling(hi);
            float rowSum = 0;
            for (int s = i0; s < Math.Min(i1, srcLen); s++)
            {
                float overlap = Math.Min(s + 1, hi) - Math.Max(s, lo);
                weights[d, s] = overlap;
                rowSum += overlap;
            }
            if (rowSum > 0)
            {
                float inv = 1f / rowSum;
                for (int s = i0; s < Math.Min(i1, srcLen); s++)
                    weights[d, s] *= inv;
            }
        }
        return weights;
    }

    /// <summary>Box-filter downsample along axis (0=Y, 1=X).</summary>
    public static float[] Downsample1D(ReadOnlySpan<float> src, int srcW, int srcH, int dstLen, int axis)
    {
        int srcLen = axis == 1 ? srcW : srcH;
        if (dstLen == srcLen)
            return src.ToArray();

        var weights = BuildBoxWeights(srcLen, dstLen);
        int channels = 4;
        int dstW = axis == 1 ? dstLen : srcW;
        int dstH = axis == 0 ? dstLen : srcH;
        var dst = new float[dstW * dstH * channels];

        for (int d = 0; d < dstLen; d++)
        {
            for (int s = 0; s < srcLen; s++)
            {
                float w = weights[d, s];
                if (w == 0) continue;
                for (int o = 0; o < (axis == 1 ? srcH : srcW); o++)
                {
                    int si = axis == 1 ? (s * srcH + o) * channels : (o * srcLen + s) * channels;
                    int di = axis == 1 ? (d * dstH + o) * channels : (o * dstLen + d) * channels;
                    dst[di]     += src[si]     * w;
                    dst[di + 1] += src[si + 1] * w;
                    dst[di + 2] += src[si + 2] * w;
                    dst[di + 3] += src[si + 3] * w;
                }
            }
        }
        return dst;
    }

    /// <summary>Bilinear upsample along axis (0=Y, 1=X) with half-pixel center.</summary>
    public static float[] Upsample1D(ReadOnlySpan<float> src, int srcW, int srcH, int dstLen, int axis)
    {
        int srcLen = axis == 1 ? srcW : srcH;
        if (dstLen == srcLen)
            return src.ToArray();

        int channels = 4;
        int otherLen = axis == 1 ? srcH : srcW;
        int dstW = axis == 1 ? dstLen : srcW;
        int dstH = axis == 0 ? dstLen : srcH;
        var dst = new float[dstW * dstH * channels];

        // Precompute interpolation params
        var ix0 = new int[dstLen];
        var ix1 = new int[dstLen];
        var t = new float[dstLen];
        for (int dx = 0; dx < dstLen; dx++)
        {
            float u = (dx + 0.5f) * srcLen / dstLen - 0.5f;
            int i0 = (int)MathF.Floor(u);
            int i1 = i0 + 1;
            if (i0 < 0) i0 = 0;
            if (i0 >= srcLen) i0 = srcLen - 1;
            if (i1 >= srcLen) i1 = srcLen - 1;
            ix0[dx] = i0;
            ix1[dx] = i1;
            t[dx] = u - MathF.Floor(u);
        }

        for (int dx = 0; dx < dstLen; dx++)
        {
            float t0 = 1f - t[dx];
            float t1 = t[dx];
            int s0 = ix0[dx];
            int s1 = ix1[dx];
            for (int o = 0; o < otherLen; o++)
            {
                int si0 = (axis == 1 ? (s0 * srcH + o) : (o * srcLen + s0)) * channels;
                int si1 = (axis == 1 ? (s1 * srcH + o) : (o * srcLen + s1)) * channels;
                int di = (axis == 1 ? (dx * dstH + o) : (o * dstLen + dx)) * channels;
                dst[di]     = src[si0]     * t0 + src[si1]     * t1;
                dst[di + 1] = src[si0 + 1] * t0 + src[si1 + 1] * t1;
                dst[di + 2] = src[si0 + 2] * t0 + src[si1 + 2] * t1;
                dst[di + 3] = src[si0 + 3] * t0 + src[si1 + 3] * t1;
            }
        }
        return dst;
    }
}
