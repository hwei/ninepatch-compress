using System.Numerics;

namespace NinePatch.Core;

/// <summary>Result of a 1D nine-patch search: compressible region [Begin,End) at rate N.</summary>
public readonly record struct SearchResult1D(int Begin, int End, int N);

/// <summary>
/// Four-stage pipeline: Segment → Intersect → Squeeze → Optimize.
/// Replaces Search1D with composable 1D operations.
/// </summary>
public static class Segmenter
{
    // ---- Segment: 1D single-channel compressible segment finding ----

    /// <summary>
    /// Find all compressible segments in a 1D single-channel signal.
    /// Phase 1: whole-signal round-trip for fast candidate generation.
    /// Phase 2: per-candidate independent verification.
    /// </summary>
    public static List<(int begin, int end)> Segment(
        ReadOnlySpan<float> signal, int rate, float threshold, int minLength,
        int marginL = 0, int marginR = -1)
    {
        int L = signal.Length;
        if (marginR < 0) marginR = L;
        if (L < minLength) return [];

        int dstLen = Math.Max(1, L / rate);

        // Precompute box weights and bilinear params
        var boxWeights = Resampler.BuildRowBoxWeights(L, dstLen);
        var bilinearArgs = Resampler.BuildRowBilinearParams(dstLen, L);

        // Precompute original sRGB
        var origSrgb = ComputeSrgbArray(signal);

        // Phase 1: whole-signal round-trip
        var down = new float[dstLen];
        var up = new float[L];
        Resampler.Downsample1DRow(signal, L, boxWeights, down);
        Resampler.Upsample1DRow(down, dstLen, bilinearArgs, up);

        // Compute per-pixel sRGB error
        var errors = new float[L];
        ComputeErrorArray(origSrgb, up, errors, L);

        // Find contiguous low-error regions
        var candidates = FindLowErrorRegions(errors, threshold, minLength, marginL, marginR);

        // Compute valid boundary set and shrink endpoints
        var validBoundaries = ComputeValidBoundaries(signal, threshold, L, marginL, marginR);
        candidates = ShrinkToBoundaries(candidates, validBoundaries, minLength);

        if (candidates.Count == 0) return [];

        // Phase 2: per-candidate independent round-trip verification
        var verified = new List<(int begin, int end)>();
        foreach (var (b, e) in candidates)
        {
            if (VerifySegmentIndependent(signal.Slice(b, e - b), rate, threshold, origSrgb.AsSpan(b, e - b)))
                verified.Add((b, e));
        }

        return verified;
    }

    /// <summary>Compute sRGB byte-scale values for a signal.</summary>
    private static float[] ComputeSrgbArray(ReadOnlySpan<float> signal)
    {
        int len = signal.Length;
        var srgb = new float[len];
        int vecLen = Vector<float>.Count;
        int vecEnd = (len / vecLen) * vecLen;
        var v255 = new Vector<float>(255f);
        for (int i = 0; i < vecEnd; i += vecLen)
        {
            var s = ColorSpace.LinearToSrgbSimd(new Vector<float>(signal.Slice(i, vecLen))) * v255;
            s.CopyTo(srgb.AsSpan(i));
        }
        for (int i = vecEnd; i < len; i++)
            srgb[i] = ColorSpace.LinearToSrgbFloat(signal[i]) * 255f;
        return srgb;
    }

    /// <summary>Compute per-pixel sRGB error between original and reconstructed.</summary>
    private static void ComputeErrorArray(float[] origSrgb, float[] reconstructed, float[] errors, int len)
    {
        int vecLen = Vector<float>.Count;
        int vecEnd = (len / vecLen) * vecLen;
        var v255 = new Vector<float>(255f);
        for (int i = 0; i < vecEnd; i += vecLen)
        {
            var reconSrgb = ColorSpace.LinearToSrgbSimd(new Vector<float>(reconstructed, i)) * v255;
            var err = Vector.Abs(new Vector<float>(origSrgb, i) - reconSrgb);
            err.CopyTo(errors.AsSpan(i));
        }
        for (int i = vecEnd; i < len; i++)
        {
            float reconSrgb = ColorSpace.LinearToSrgbFloat(reconstructed[i]) * 255f;
            errors[i] = MathF.Abs(origSrgb[i] - reconSrgb);
        }
    }

    /// <summary>Find contiguous regions where all per-pixel errors <= threshold.</summary>
    private static List<(int, int)> FindLowErrorRegions(float[] errors, float threshold, int minLength, int marginL, int marginR)
    {
        var regions = new List<(int, int)>();
        int start = -1;
        for (int i = marginL; i < marginR; i++)
        {
            if (errors[i] <= threshold)
            {
                if (start < 0) start = i;
            }
            else
            {
                if (start >= 0 && i - start >= minLength)
                    regions.Add((start, i));
                start = -1;
            }
        }
        if (start >= 0 && marginR - start >= minLength)
            regions.Add((start, marginR));
        return regions;
    }

    /// <summary>Compute valid boundary positions where adjacent-pixel diff <= threshold.</summary>
    private static bool[] ComputeValidBoundaries(ReadOnlySpan<float> signal, float threshold, int len, int marginL, int marginR)
    {
        var valid = new bool[len + 1];
        valid[0] = true;
        valid[len] = true;

        var srgb = ComputeSrgbArray(signal);
        for (int i = marginL + 1; i < marginR; i++)
        {
            if (MathF.Abs(srgb[i] - srgb[i - 1]) <= threshold)
                valid[i] = true;
        }
        if (marginL > 0) valid[marginL] = true;
        if (marginR < len) valid[marginR] = true;

        return valid;
    }

    /// <summary>Shrink candidate endpoints to nearest valid boundary.</summary>
    private static List<(int, int)> ShrinkToBoundaries(List<(int, int)> candidates, bool[] validBoundaries, int minLength)
    {
        var result = new List<(int, int)>();
        foreach (var (b, e) in candidates)
        {
            int newB = b;
            while (newB < e && !validBoundaries[newB]) newB++;
            int newE = e;
            while (newE > newB && !validBoundaries[newE]) newE--;
            if (newE - newB >= minLength)
                result.Add((newB, newE));
        }
        return result;
    }

    /// <summary>Verify a single segment independently at the given rate.</summary>
    private static bool VerifySegmentIndependent(ReadOnlySpan<float> seg, int rate, float threshold, ReadOnlySpan<float> origSrgb)
    {
        int segLen = seg.Length;
        int dstLen = Math.Max(1, segLen / rate);
        var segWeights = Resampler.BuildRowBoxWeights(segLen, dstLen);
        var segBilinear = Resampler.BuildRowBilinearParams(dstLen, segLen);

        var down = new float[dstLen];
        var up = new float[segLen];
        Resampler.Downsample1DRow(seg, segLen, segWeights, down);
        Resampler.Upsample1DRow(down, dstLen, segBilinear, up);

        int vecLen = Vector<float>.Count;
        int vecEnd = (segLen / vecLen) * vecLen;
        var v255 = new Vector<float>(255f);
        for (int i = 0; i < vecEnd; i += vecLen)
        {
            var orig = new Vector<float>(origSrgb.Slice(i, vecLen));
            var recon = ColorSpace.LinearToSrgbSimd(new Vector<float>(up, i)) * v255;
            var err = Vector.Abs(orig - recon);
            for (int j = 0; j < vecLen; j++)
                if (err[j] > threshold) return false;
        }
        for (int i = vecEnd; i < segLen; i++)
        {
            if (MathF.Abs(origSrgb[i] - ColorSpace.LinearToSrgbFloat(up[i]) * 255f) > threshold)
                return false;
        }
        return true;
    }

    // ---- Intersect: multi-channel segment set intersection ----

    /// <summary>
    /// Compute geometric intersection of segment sets from multiple channels,
    /// filtered by minLength.
    /// </summary>
    public static List<(int begin, int end)> Intersect(
        IReadOnlyList<List<(int begin, int end)>> channelSegments, int minLength)
    {
        if (channelSegments.Count == 0) return [];
        if (channelSegments.Count == 1)
            return channelSegments[0].Where(s => s.end - s.begin >= minLength).ToList();

        var result = channelSegments[0];
        for (int ch = 1; ch < channelSegments.Count; ch++)
        {
            result = IntersectTwoSets(result, channelSegments[ch]);
            if (result.Count == 0) return [];
        }

        return result.Where(s => s.Item2 - s.Item1 >= minLength).ToList();
    }

    /// <summary>Intersect two sorted segment lists, returning overlapping regions.</summary>
    private static List<(int, int)> IntersectTwoSets(List<(int, int)> a, List<(int, int)> b)
    {
        var result = new List<(int, int)>();
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            int lo = Math.Max(a[i].Item1, b[j].Item1);
            int hi = Math.Min(a[i].Item2, b[j].Item2);
            if (lo < hi)
                result.Add((lo, hi));
            if (a[i].Item2 <= b[j].Item2) i++;
            else j++;
        }
        return result;
    }

    // ---- Squeeze: 2D segment finding ----

    /// <summary>
    /// Find horizontal compressible segments: per-row Segment per channel →
    /// Intersect → intersect all rows' segment sets → minLength filter.
    /// </summary>
    public static List<(int begin, int end)> SqueezeHorizontal(
        SoaImage img, int rate, float threshold, int minLength,
        int marginL = 0, int marginR = -1)
    {
        if (marginR < 0) marginR = img.Width;
        int width = img.Width;
        int height = img.Height;

        var channels = new[] { img.R, img.G, img.B, img.A };

        if (height == 0) return [];

        // Intersect first row's channels as the initial result
        var result = IntersectChannelsForRow(img, 0, rate, threshold, minLength, marginL, marginR, channels);
        if (result.Count == 0) return [];

        // Intersect with each subsequent row
        for (int y = 1; y < height; y++)
        {
            var rowSegs = IntersectChannelsForRow(img, y, rate, threshold, minLength, marginL, marginR, channels);
            result = IntersectTwoSets(result, rowSegs);
            if (result.Count == 0) return [];
        }

        return result.Where(s => s.Item2 - s.Item1 >= minLength).ToList();
    }

    private static List<(int, int)> IntersectChannelsForRow(
        SoaImage img, int y, int rate, float threshold, int minLength,
        int marginL, int marginR, float[][] channels)
    {
        int width = img.Width;
        var chSegments = new List<List<(int begin, int end)>>(4);
        foreach (var ch in channels)
        {
            var row = ch.AsSpan(y * width, width);
            var segs = Segment(row, rate, threshold, minLength, marginL, marginR);
            chSegments.Add(segs);
        }
        return Intersect(chSegments, minLength);
    }

    /// <summary>
    /// Find vertical compressible segments: per-column Segment per channel →
    /// Intersect → intersect all columns' segment sets → minLength filter.
    /// </summary>
    public static List<(int begin, int end)> SqueezeVertical(
        SoaImage img, int rate, float threshold, int minLength,
        int marginL = 0, int marginR = -1)
    {
        if (marginR < 0) marginR = img.Height;
        int width = img.Width;
        int height = img.Height;

        var channels = new[] { img.R, img.G, img.B, img.A };

        if (width == 0) return [];

        // Intersect first column's channels as the initial result
        var result = IntersectChannelsForColumn(img, 0, rate, threshold, minLength, marginL, marginR, channels);
        if (result.Count == 0) return [];

        // Intersect with each subsequent column
        for (int x = 1; x < width; x++)
        {
            var colSegs = IntersectChannelsForColumn(img, x, rate, threshold, minLength, marginL, marginR, channels);
            result = IntersectTwoSets(result, colSegs);
            if (result.Count == 0) return [];
        }

        return result.Where(s => s.Item2 - s.Item1 >= minLength).ToList();
    }

    private static List<(int, int)> IntersectChannelsForColumn(
        SoaImage img, int x, int rate, float threshold, int minLength,
        int marginL, int marginR, float[][] channels)
    {
        int height = img.Height;
        int width = img.Width;
        var chSegments = new List<List<(int begin, int end)>>(4);
        foreach (var ch in channels)
        {
            var col = new float[height];
            for (int y = 0; y < height; y++)
                col[y] = ch[y * width + x];
            var segs = Segment(col, rate, threshold, minLength, marginL, marginR);
            chSegments.Add(segs);
        }
        return Intersect(chSegments, minLength);
    }

    // ---- Optimize: max compression rate search + best segment selection ----

    /// <summary>
    /// For a given axis, find the best compressible segment with maximum rate.
    /// Returns null if no segment passes at any rate (identity fallback).
    /// </summary>
    public static SearchResult1D? OptimizeHorizontal(
        SoaImage img, float threshold, int minLength = 8,
        int margin = 0, int maxRate = 16)
    {
        return OptimizeAxis(img, threshold, minLength, margin, maxRate, isHorizontal: true);
    }

    public static SearchResult1D? OptimizeVertical(
        SoaImage img, float threshold, int minLength = 8,
        int margin = 0, int maxRate = 16)
    {
        return OptimizeAxis(img, threshold, minLength, margin, maxRate, isHorizontal: false);
    }

    private static SearchResult1D? OptimizeAxis(
        SoaImage img, float threshold, int minLength, int margin, int maxRate, bool isHorizontal)
    {
        int len = isHorizontal ? img.Width : img.Height;
        int marginL = margin;
        int marginR = len - margin;

        if (marginR - marginL < minLength) return null;

        // Get candidate segments at rate=2
        var segments = isHorizontal
            ? SqueezeHorizontal(img, 2, threshold, minLength, marginL, marginR)
            : SqueezeVertical(img, 2, threshold, minLength, marginL, marginR);

        if (segments.Count == 0) return null;

        SearchResult1D? best = null;
        int bestSavings = 0;

        foreach (var (b, e) in segments)
        {
            int sLen = e - b;
            var (signals, _, _) = ExtractAxisSignals(img, b, e, isHorizontal);
            var origSrgb = signals.Select(s => ComputeSrgbArray(s)).ToArray();
            int maxPassingRate = SearchRateForSegment(signals, origSrgb, sLen, threshold, maxRate);
            if (maxPassingRate < 2) continue;

            int savings = sLen - (int)MathF.Ceiling((float)sLen / maxPassingRate);
            if (savings > bestSavings)
            {
                bestSavings = savings;
                best = new SearchResult1D(b, e, maxPassingRate);
            }
        }

        return best;
    }

    /// <summary>
    /// Extract 1D signals for all channels along an axis segment.
    /// Horizontal: signals[ch][y * segLen + x] for x in [b,e), y in [0,height)
    /// Vertical: signals[ch][(y-b) * width + x] for y in [b,e), x in [0,width)
    /// </summary>
    private static (float[][] signals, int segLen, int orthoLen) ExtractAxisSignals(
        SoaImage img, int b, int e, bool isHorizontal)
    {
        int segLen = e - b;
        var channels = new[] { img.R, img.G, img.B, img.A };
        var signals = new float[4][];

        if (isHorizontal)
        {
            int orthoLen = img.Height;
            for (int ch = 0; ch < 4; ch++)
            {
                signals[ch] = new float[segLen * orthoLen];
                var src = channels[ch];
                int w = img.Width;
                for (int y = 0; y < orthoLen; y++)
                    Buffer.BlockCopy(src, (y * w + b) * 4, signals[ch], y * segLen * 4, segLen * 4);
            }
            return (signals, segLen, orthoLen);
        }
        else
        {
            int orthoLen = img.Width;
            for (int ch = 0; ch < 4; ch++)
            {
                signals[ch] = new float[segLen * orthoLen];
                var src = channels[ch];
                int w = img.Width;
                for (int y = b; y < e; y++)
                    Buffer.BlockCopy(src, y * w * 4, signals[ch], (y - b) * w * 4, w * 4);
            }
            return (signals, segLen, orthoLen);
        }
    }

    /// <summary>
    /// Search maximum compression rate for a specific segment.
    /// Coarse stepping (rate=2,3,4,...) then fine binary search.
    /// </summary>
    private static int SearchRateForSegment(
        float[][] signals, float[][] origSrgb, int segLen, float threshold, int maxRate)
    {
        // Coarse search
        int lastPassing = 1;
        int firstFailing = maxRate + 1;

        for (int rate = 2; rate <= maxRate; rate++)
        {
            if (VerifyRate1D(signals, origSrgb, rate, threshold, segLen))
                lastPassing = rate;
            else
            {
                firstFailing = rate;
                break;
            }
        }

        if (lastPassing == 1) return 1;

        // Fine binary search
        int lo = lastPassing, hi = firstFailing;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (VerifyRate1D(signals, origSrgb, mid, threshold, segLen))
                lo = mid;
            else
                hi = mid;
        }

        return lo;
    }

    /// <summary>
    /// Verify a 1D round-trip at a given rate across all channels and orthogonal dimension.
    /// signals[ch] is laid out as [ortho][seg] row-major.
    /// </summary>
    private static bool VerifyRate1D(
        float[][] signals, float[][] origSrgb, int rate, float threshold,
        int segLen)
    {
        int orthoLen = signals[0].Length / segLen;
        int dstLen = Math.Max(1, segLen / rate);
        var boxWeights = Resampler.BuildRowBoxWeights(segLen, dstLen);
        var bilinearArgs = Resampler.BuildRowBilinearParams(dstLen, segLen);

        var down = new float[dstLen];
        var up = new float[segLen];

        for (int ch = 0; ch < 4; ch++)
        {
            var signal = signals[ch];
            var srgb = origSrgb[ch];

            for (int o = 0; o < orthoLen; o++)
            {
                int offset = o * segLen;
                Resampler.Downsample1DRow(signal.AsSpan(offset, segLen), segLen, boxWeights, down);
                Resampler.Upsample1DRow(down, dstLen, bilinearArgs, up);

                // Check error per pixel
                if (!CheckErrorVectorized(srgb.AsSpan(offset, segLen), up, threshold))
                    return false;
            }
        }
        return true;
    }

    /// <summary>Check per-pixel sRGB error against threshold with SIMD + early exit.</summary>
    private static bool CheckErrorVectorized(ReadOnlySpan<float> origSrgb, float[] reconstructed, float threshold)
    {
        int len = origSrgb.Length;
        int vecLen = Vector<float>.Count;
        int vecEnd = (len / vecLen) * vecLen;
        var v255 = new Vector<float>(255f);

        for (int i = 0; i < vecEnd; i += vecLen)
        {
            var orig = new Vector<float>(origSrgb.Slice(i, vecLen));
            var recon = ColorSpace.LinearToSrgbSimd(new Vector<float>(reconstructed, i)) * v255;
            var err = Vector.Abs(orig - recon);
            for (int j = 0; j < vecLen; j++)
                if (err[j] > threshold) return false;
        }
        for (int i = vecEnd; i < len; i++)
        {
            if (MathF.Abs(origSrgb[i] - ColorSpace.LinearToSrgbFloat(reconstructed[i]) * 255f) > threshold)
                return false;
        }
        return true;
    }

    // ---- Convenience wrappers (replace Search1D.SearchX/SearchY) ----

    public static SearchResult1D? SearchX(SoaImage img, float threshold, int minLength = 8, int margin = 0)
    {
        return OptimizeHorizontal(img, threshold, minLength, margin);
    }

    public static SearchResult1D? SearchY(SoaImage img, float threshold, int minLength = 8, int margin = 0)
    {
        return OptimizeVertical(img, threshold, minLength, margin);
    }
}
