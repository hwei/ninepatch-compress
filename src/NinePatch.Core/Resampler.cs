using System.Numerics;

namespace NinePatch.Core;

/// <summary>
/// 1D box downsample and bilinear upsample.
/// Data layout: each channel is a separate flat float array of size Width × Height (row-major).
/// </summary>
public static class Resampler
{
    /// <summary>
    /// Precomputed box filter weights for a single 1D downsample (L → N).
    /// Reused across rows/columns to avoid recomputing weights per row.
    /// </summary>
    internal readonly struct BoxWeightsPrecomputed
    {
        /// <summary>Flattened weights[dstLen * srcSpan], where srcSpan = i1 - i0 per dst index.</summary>
        public readonly int[] SrcStart;  // srcStart[d] = first src index contributing to dst[d]
        public readonly int[] SrcEnd;    // srcEnd[d] = last src index (exclusive)
        public readonly float[] Weights; // flattened: weight for dst[d], src[s] is Weights[d * maxSpan + (s - SrcStart[d])]
        public readonly int MaxSpan;     // max (SrcEnd[d] - SrcStart[d]) across all d

        public BoxWeightsPrecomputed(int[] srcStart, int[] srcEnd, float[] weights, int maxSpan)
        {
            SrcStart = srcStart;
            SrcEnd = srcEnd;
            Weights = weights;
            MaxSpan = maxSpan;
        }
    }

    /// <summary>
    /// Precomputed bilinear interpolation params for a single 1D upsample (N → L).
    /// Reused across rows/columns.
    /// </summary>
    internal readonly struct BilinearParamsPrecomputed
    {
        public readonly int[] Ix0;  // ix0[dx]
        public readonly int[] Ix1;  // ix1[dx]
        public readonly float[] T;  // t[dx]

        public BilinearParamsPrecomputed(int[] ix0, int[] ix1, float[] t)
        {
            Ix0 = ix0;
            Ix1 = ix1;
            T = t;
        }
    }

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

    /// <summary>Build precomputed box weights for reuse across rows.</summary>
    internal static BoxWeightsPrecomputed BuildRowBoxWeights(int srcLen, int dstLen)
    {
        float scale = (float)srcLen / dstLen;
        var srcStart = new int[dstLen];
        var srcEnd = new int[dstLen];
        int maxSpan = 0;

        for (int d = 0; d < dstLen; d++)
        {
            float lo = d * scale;
            float hi = (d + 1) * scale;
            int i0 = (int)MathF.Floor(lo);
            int i1 = (int)MathF.Ceiling(hi);
            srcStart[d] = i0;
            srcEnd[d] = Math.Min(i1, srcLen);
            int span = srcEnd[d] - srcStart[d];
            if (span > maxSpan) maxSpan = span;
        }

        var weights = new float[dstLen * maxSpan];
        for (int d = 0; d < dstLen; d++)
        {
            float lo = d * scale;
            float hi = (d + 1) * scale;
            float rowSum = 0;
            for (int s = srcStart[d]; s < srcEnd[d]; s++)
                rowSum += Math.Min(s + 1, hi) - Math.Max(s, lo);
            float invRowSum = rowSum > 0 ? 1f / rowSum : 1f;
            int offset = d * maxSpan;
            for (int s = srcStart[d]; s < srcEnd[d]; s++)
                weights[offset + (s - srcStart[d])] = (Math.Min(s + 1, hi) - Math.Max(s, lo)) * invRowSum;
        }

        return new BoxWeightsPrecomputed(srcStart, srcEnd, weights, maxSpan);
    }

    /// <summary>Build precomputed bilinear interpolation params for reuse across rows.</summary>
    internal static BilinearParamsPrecomputed BuildRowBilinearParams(int srcLen, int dstLen)
    {
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
        return new BilinearParamsPrecomputed(ix0, ix1, t);
    }

    /// <summary>Single-row box downsample using precomputed weights.</summary>
    internal static void Downsample1DRow(
        ReadOnlySpan<float> srcRow, int srcLen,
        BoxWeightsPrecomputed weights, Span<float> dstRow)
    {
        int dstLen = weights.SrcStart.Length;
        int maxSpan = weights.MaxSpan;
        var w = weights.Weights;
        var starts = weights.SrcStart;
        var ends = weights.SrcEnd;

        for (int d = 0; d < dstLen; d++)
        {
            float sum = 0;
            int offset = d * maxSpan;
            int s0 = starts[d];
            int s1 = ends[d];
            for (int s = s0; s < s1; s++)
                sum += srcRow[s] * w[offset + (s - s0)];
            dstRow[d] = sum;
        }
    }

    /// <summary>Single-row bilinear upsample using precomputed params.</summary>
    internal static void Upsample1DRow(
        ReadOnlySpan<float> srcRow, int srcLen,
        BilinearParamsPrecomputed args, Span<float> dstRow)
    {
        int dstLen = args.Ix0.Length;
        var ix0 = args.Ix0;
        var ix1 = args.Ix1;
        var t = args.T;

        for (int dx = 0; dx < dstLen; dx++)
        {
            float t0 = 1f - t[dx];
            float t1 = t[dx];
            dstRow[dx] = srcRow[ix0[dx]] * t0 + srcRow[ix1[dx]] * t1;
        }
    }

    /// <summary>Box-filter downsample a single channel along axis (0=Y, 1=X).</summary>
    public static float[] Downsample1D(ReadOnlySpan<float> src, int srcW, int srcH, int dstLen, int axis)
    {
        int srcLen = axis == 1 ? srcW : srcH;
        if (dstLen == srcLen)
            return src.ToArray();

        var weights = BuildBoxWeights(srcLen, dstLen);
        int dstW = axis == 1 ? dstLen : srcW;
        int dstH = axis == 0 ? dstLen : srcH;
        var dst = new float[dstW * dstH];

        ApplyBoxFilter(weights, src, srcW, srcH, dst, dstW, dstH, axis);
        return dst;
    }

    /// <summary>Box-filter downsample writing into a pre-allocated destination buffer.</summary>
    public static void Downsample1D(ReadOnlySpan<float> src, int srcW, int srcH, int dstLen, int axis, Span<float> dst)
    {
        int srcLen = axis == 1 ? srcW : srcH;
        if (dstLen == srcLen)
        {
            src.CopyTo(dst);
            return;
        }

        int dstW = axis == 1 ? dstLen : srcW;
        int dstH = axis == 0 ? dstLen : srcH;
        if (dst.Length < dstW * dstH)
            throw new ArgumentException("Destination buffer too small");

        ApplyBoxFilterInline(src, srcW, srcH, dst, dstW, dstH, axis);
    }

    /// <summary>Box filter with on-the-fly weight computation — eliminates 2D array allocation.</summary>
    private static void ApplyBoxFilterInline(
        ReadOnlySpan<float> src, int srcW, int srcH,
        Span<float> dst, int dstW, int dstH, int axis)
    {
        int srcLen = axis == 1 ? srcW : srcH;
        int dstLen = axis == 1 ? dstW : dstH;
        int otherLen = axis == 1 ? srcH : srcW;
        float scale = (float)srcLen / dstLen;

        for (int d = 0; d < dstLen; d++)
        {
            float lo = d * scale;
            float hi = (d + 1) * scale;
            int i0 = (int)MathF.Floor(lo);
            int i1 = (int)MathF.Ceiling(hi);
            int clampedI1 = Math.Min(i1, srcLen);

            // Compute rowSum first for normalization
            float rowSum = 0;
            for (int s = i0; s < clampedI1; s++)
                rowSum += Math.Min(s + 1, hi) - Math.Max(s, lo);
            float invRowSum = rowSum > 0 ? 1f / rowSum : 1f;

            // Apply normalized weights
            for (int s = i0; s < clampedI1; s++)
            {
                float w = (Math.Min(s + 1, hi) - Math.Max(s, lo)) * invRowSum;
                if (w == 0) continue;
                for (int o = 0; o < otherLen; o++)
                {
                    int si = axis == 1 ? o * srcW + s : s * srcW + o;
                    int di = axis == 1 ? o * dstW + d : d * dstW + o;
                    dst[di] += src[si] * w;
                }
            }
        }
    }

    private static void ApplyBoxFilter(
        float[,] weights, ReadOnlySpan<float> src, int srcW, int srcH,
        Span<float> dst, int dstW, int dstH, int axis)
    {
        int srcLen = axis == 1 ? srcW : srcH;
        int dstLen = axis == 1 ? dstW : dstH;
        int otherLen = axis == 1 ? srcH : srcW;

        for (int d = 0; d < dstLen; d++)
        {
            for (int s = 0; s < srcLen; s++)
            {
                float w = weights[d, s];
                if (w == 0) continue;
                for (int o = 0; o < otherLen; o++)
                {
                    int si = axis == 1 ? o * srcW + s : s * srcW + o;
                    int di = axis == 1 ? o * dstW + d : d * dstW + o;
                    dst[di] += src[si] * w;
                }
            }
        }
    }

    /// <summary>Bilinear upsample a single channel along axis (0=Y, 1=X) with half-pixel center.</summary>
    public static float[] Upsample1D(ReadOnlySpan<float> src, int srcW, int srcH, int dstLen, int axis)
    {
        int srcLen = axis == 1 ? srcW : srcH;
        if (dstLen == srcLen)
            return src.ToArray();

        int dstW = axis == 1 ? dstLen : srcW;
        int dstH = axis == 0 ? dstLen : srcH;
        var dst = new float[dstW * dstH];

        ApplyBilinearUpsample(src, srcW, srcH, dst, dstW, dstH, axis, dstLen);
        return dst;
    }

    /// <summary>Bilinear upsample writing into a pre-allocated destination buffer.</summary>
    public static void Upsample1D(ReadOnlySpan<float> src, int srcW, int srcH, int dstLen, int axis, Span<float> dst)
    {
        int srcLen = axis == 1 ? srcW : srcH;
        if (dstLen == srcLen)
        {
            src.CopyTo(dst);
            return;
        }

        int dstW = axis == 1 ? dstLen : srcW;
        int dstH = axis == 0 ? dstLen : srcH;
        if (dst.Length < dstW * dstH)
            throw new ArgumentException("Destination buffer too small");

        ApplyBilinearUpsample(src, srcW, srcH, dst, dstW, dstH, axis, dstLen);
    }

    private static void ApplyBilinearUpsample(
        ReadOnlySpan<float> src, int srcW, int srcH,
        Span<float> dst, int dstW, int dstH, int axis, int dstLen)
    {
        int srcLen = axis == 1 ? srcW : srcH;
        int otherLen = axis == 1 ? srcH : srcW;

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

        // SIMD only works for axis=0 (Y) where adjacent pixels are contiguous.
        // axis=1 (X) would need gather; fall back to scalar.
        bool useSimd = axis == 0;
        int vecLen = useSimd ? Vector<float>.Count : 1;

        for (int dx = 0; dx < dstLen; dx++)
        {
            float t0 = 1f - t[dx];
            float t1 = t[dx];
            int s0 = ix0[dx];
            int s1 = ix1[dx];
            var vt0 = new Vector<float>(t0);
            var vt1 = new Vector<float>(t1);
            for (int o = 0; o < otherLen;)
            {
                int remain = otherLen - o;
                if (useSimd && remain >= vecLen)
                {
                    int si0 = s0 * srcW + o;
                    int si1 = s1 * srcW + o;
                    int di = dx * dstW + o;

                    var px0 = new Vector<float>(src.Slice(si0, vecLen));
                    var px1 = new Vector<float>(src.Slice(si1, vecLen));
                    var result = px0 * vt0 + px1 * vt1;
                    for (int j = 0; j < vecLen; j++)
                        dst[di + j] = result[j];
                    o += vecLen;
                }
                else
                {
                    int si0 = axis == 1 ? o * srcW + s0 : s0 * srcW + o;
                    int si1 = axis == 1 ? o * srcW + s1 : s1 * srcW + o;
                    int di = axis == 1 ? o * dstW + dx : dx * dstW + o;
                    int batch = useSimd ? remain : 1;
                    for (int j = 0; j < batch; j++)
                        dst[di + j] = src[si0 + j] * t0 + src[si1 + j] * t1;
                    o += batch;
                }
            }
        }
    }
}
