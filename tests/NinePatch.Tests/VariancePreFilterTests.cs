using System.Numerics;
using NinePatch.Core;
using Xunit;

namespace NinePatch.Tests;

public class VariancePreFilterTests
{
    // --- 1.2 / 1.3: Variance prefix-sum correctness ---

    [Fact]
    public void VarianceForInterval_MatchesBruteForce_KnownInput()
    {
        // Create a known pattern: 8x1 image, alternating 0/1 in R channel
        SoaImage img = CreateGradientImage(10, 10, 50, 200); // R from 50 to 200 across columns
        int pixelCount = img.Width * img.Height;
        var origSrgb = BuildPrecomputedSrgb(img);

        float[] prefix = Search1D.ComputeAxisVariancePrefixSum(img, axis: 1, origSrgb);

        // Test a specific interval [2, 6)
        int b = 2, e = 6;
        float lookupVar = Search1D.VarianceForInterval(prefix, b, e);

        // Brute-force: compute variance directly from sRGB values
        float bruteForceVar = BruteForceAxisVariance(img, axis: 1, b, e, origSrgb);

        Assert.Equal(bruteForceVar, lookupVar, precision: 5);
    }

    [Fact]
    public void VarianceForInterval_MatchesOnRandomIntervals()
    {
        SoaImage img = CreateNoisePatternImage(32, 32);
        int pixelCount = img.Width * img.Height;
        var origSrgb = BuildPrecomputedSrgb(img);

        float[] prefix = Search1D.ComputeAxisVariancePrefixSum(img, axis: 1, origSrgb);

        // Test multiple random intervals on X axis
        var rng = new Random(42);
        for (int i = 0; i < 20; i++)
        {
            int l = img.Width;
            int b = rng.Next(0, l - 2);
            int e = rng.Next(b + 1, l);

            float lookupVar = Search1D.VarianceForInterval(prefix, b, e);
            float bruteForceVar = BruteForceAxisVariance(img, axis: 1, b, e, origSrgb);

            Assert.Equal(bruteForceVar, lookupVar, precision: 5);
        }

        // Same for Y axis
        prefix = Search1D.ComputeAxisVariancePrefixSum(img, axis: 0, origSrgb);
        for (int i = 0; i < 20; i++)
        {
            int l = img.Height;
            int b = rng.Next(0, l - 2);
            int e = rng.Next(b + 1, l);

            float lookupVar = Search1D.VarianceForInterval(prefix, b, e);
            float bruteForceVar = BruteForceAxisVariance(img, axis: 0, b, e, origSrgb);

            Assert.Equal(bruteForceVar, lookupVar, precision: 5);
        }
    }

    // --- 2.2 / 2.3: Adaptive threshold ---

    [Fact]
    public void ComputeVarianceThreshold_SolidColor_ReturnsFloor()
    {
        SoaImage img = CreateImage(10, 10, 128, 128, 128, 255);
        int pixelCount = img.PixelCount;
        var origSrgb = BuildPrecomputedSrgb(img);

        float globalVar = Search1D.ComputeGlobalVariance(img, origSrgb);
        float threshold = Search1D.ComputeVarianceThreshold(globalVar);

        // Solid color → global variance near zero → threshold = floor
        Assert.Equal(0.01f, threshold, precision: 2);
    }

    [Fact]
    public void ComputeVarianceThreshold_Gradient_ScalesWithVariance()
    {
        SoaImage img = CreateGradientImage(100, 100, 0, 255);
        int pixelCount = img.PixelCount;
        var origSrgb = BuildPrecomputedSrgb(img);

        float globalVar = Search1D.ComputeGlobalVariance(img, origSrgb);
        float threshold = Search1D.ComputeVarianceThreshold(globalVar);

        // Gradient has non-zero variance, threshold should be > floor and scale with global
        Assert.True(globalVar > 0.01f, $"globalVar={globalVar}");
        Assert.True(threshold > 0.01f, $"threshold={threshold}");
        Assert.Equal(3.0f * globalVar, threshold, precision: 5);
    }

    // --- 4.2: Noise image early termination ---

    [Fact]
    public void SearchY_NoiseImage_CompletesQuickly()
    {
        // Large noise image should trigger variance pre-filter pruning and early termination
        SoaImage img = CreateNoisePatternImage(435, 511);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = Search1D.SearchY(img, threshold: 4f, margin: 0);
        sw.Stop();

        // Should complete in under 1 second with pruning active
        Assert.True(sw.ElapsedMilliseconds < 1000, $"SearchY took {sw.ElapsedMilliseconds}ms, expected <1000ms");
        // Result may be null (early termination) or identity fallback
    }

    // --- 4.3: Gradient image still finds optimal result with pruning ---

    [Fact]
    public void SearchX_GradientImage_FindsResultWithPruningActive()
    {
        SoaImage img = CreateGradientImage(100, 100, 50, 200);
        var result = Search1D.SearchX(img, threshold: 4f, margin: 0);

        Assert.NotNull(result);
        Assert.True(result.Value.Begin >= 0);
        Assert.True(result.Value.End <= img.Width);
        Assert.True(result.Value.N < result.Value.End - result.Value.Begin);
    }

    [Fact]
    public void SearchY_GradientImage_FindsResultWithPruningActive()
    {
        SoaImage img = CreateGradientImage(100, 100, 50, 200);
        var result = Search1D.SearchY(img, threshold: 4f, margin: 0);

        Assert.NotNull(result);
        Assert.True(result.Value.Begin >= 0);
        Assert.True(result.Value.End <= img.Height);
    }

    // --- Helper methods ---

    private static SoaImage CreateImage(int w, int h, byte r, byte g, byte b, byte a)
    {
        var bytes = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            bytes[i * 4] = r;
            bytes[i * 4 + 1] = g;
            bytes[i * 4 + 2] = b;
            bytes[i * 4 + 3] = a;
        }
        return ColorSpace.RgbaU8ToLinear(bytes, w, h);
    }

    private static SoaImage CreateGradientImage(int w, int h, byte rStart, byte rEnd)
    {
        var bytes = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bytes[i] = (byte)(rStart + (rEnd - rStart) * x / Math.Max(w - 1, 1));
                bytes[i + 1] = 128;
                bytes[i + 2] = 128;
                bytes[i + 3] = 255;
            }
        }
        return ColorSpace.RgbaU8ToLinear(bytes, w, h);
    }

    private static SoaImage CreateNoisePatternImage(int w, int h)
    {
        var bytes = new byte[w * h * 4];
        var rng = new Random(123);
        for (int i = 0; i < w * h; i++)
        {
            bytes[i * 4] = (byte)rng.Next(0, 256);
            bytes[i * 4 + 1] = (byte)rng.Next(0, 256);
            bytes[i * 4 + 2] = (byte)rng.Next(0, 256);
            bytes[i * 4 + 3] = 255;
        }
        return ColorSpace.RgbaU8ToLinear(bytes, w, h);
    }

    private static PrecomputedSrgb BuildPrecomputedSrgb(SoaImage img)
    {
        int pixelCount = img.PixelCount;
        var origSrgb = new PrecomputedSrgb(
            R: new float[pixelCount],
            G: new float[pixelCount],
            B: new float[pixelCount],
            Alpha: (float[])img.A.Clone());

        int vLen = Vector<float>.Count;
        int vEnd = (pixelCount / vLen) * vLen;
        for (int i = 0; i < vEnd; i += vLen)
        {
            var vr = new Vector<float>(img.R, i);
            var vg = new Vector<float>(img.G, i);
            var vb = new Vector<float>(img.B, i);
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

        return origSrgb;
    }

    /// <summary>
    /// Brute-force computation of per-position variance for interval [b, e).
    /// For each position: compute per-channel variance (across orthogonal axis), take max.
    /// Then average the max-variances across positions.
    /// </summary>
    private static float BruteForceAxisVariance(SoaImage img, int axis, int b, int e, PrecomputedSrgb origSrgb)
    {
        int w = img.Width;
        int h = img.Height;
        float[][] planes = [origSrgb.R, origSrgb.G, origSrgb.B, origSrgb.Alpha];
        int otherLen = axis == 1 ? h : w;

        double totalMaxVar = 0;
        for (int pos = b; pos < e; pos++)
        {
            float maxVar = 0f;
            for (int ch = 0; ch < 4; ch++)
            {
                double sum = 0, sumSq = 0;
                for (int o = 0; o < otherLen; o++)
                {
                    int idx = axis == 1 ? o * w + pos : pos * w + o;
                    float v = planes[ch][idx];
                    sum += v;
                    sumSq += v * v;
                }
                double mean = sum / otherLen;
                double meanSq = sumSq / otherLen;
                float var = (float)(meanSq - mean * mean);
                if (var > maxVar) maxVar = var;
            }
            totalMaxVar += maxVar;
        }

        return (float)(totalMaxVar / (e - b));
    }
}
