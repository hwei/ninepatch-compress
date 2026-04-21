using System.Numerics;

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
                var vw = new Vector4(w, w, w, w);
                for (int o = 0; o < (axis == 1 ? srcH : srcW); o++)
                {
                    int si, di;
                    if (axis == 1)
                    {
                        si = (s * srcH + o) * channels;
                        di = (d * dstH + o) * channels;
                    }
                    else
                    {
                        si = (s * srcW + o) * channels;
                        di = (d * dstW + o) * channels;
                    }
                    var srcPx = new Vector4(src[si], src[si + 1], src[si + 2], src[si + 3]);
                    var dstPx = new Vector4(dst[di], dst[di + 1], dst[di + 2], dst[di + 3]);
                    var acc = dstPx + srcPx * vw;
                    dst[di]     = acc.X;
                    dst[di + 1] = acc.Y;
                    dst[di + 2] = acc.Z;
                    dst[di + 3] = acc.W;
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
            var vt0 = new Vector4(t0, t0, t0, t0);
            var vt1 = new Vector4(t1, t1, t1, t1);
            for (int o = 0; o < otherLen; o++)
            {
                int si0, si1, di;
                if (axis == 1)
                {
                    si0 = (s0 * srcH + o) * channels;
                    si1 = (s1 * srcH + o) * channels;
                    di = (dx * dstH + o) * channels;
                }
                else
                {
                    si0 = (s0 * srcW + o) * channels;
                    si1 = (s1 * srcW + o) * channels;
                    di = (dx * dstW + o) * channels;
                }
                var px0 = new Vector4(src[si0], src[si0 + 1], src[si0 + 2], src[si0 + 3]);
                var px1 = new Vector4(src[si1], src[si1 + 1], src[si1 + 2], src[si1 + 3]);
                var result = px0 * vt0 + px1 * vt1;
                dst[di]     = result.X;
                dst[di + 1] = result.Y;
                dst[di + 2] = result.Z;
                dst[di + 3] = result.W;
            }
        }
        return dst;
    }
}
