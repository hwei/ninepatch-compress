using NinePatch.Core;
using Xunit;

namespace NinePatch.Tests;

public class GradientPrefilterTests
{
    // --- ComputeAxisGradient tests (tasks 1.3-1.5) ---

    [Fact]
    public void ComputeAxisGradient_SolidColor_AllZero()
    {
        SoaImage img = CreateImageU8(64, 64, 128, 128, 128, 255);
        var srgb = PrecomputeSrgb(img);

        float[] gX = Search1D.ComputeAxisGradient(img, axis: 1, srgb);
        float[] gY = Search1D.ComputeAxisGradient(img, axis: 0, srgb);

        Assert.All(gX, v => Assert.Equal(0f, v));
        Assert.All(gY, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void ComputeAxisGradient_HorizontalRamp_SmallAndConstantOnX_ZeroOnY()
    {
        int w = 100, h = 50;
        var bytes = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            byte v = (byte)(x * 255 / (w - 1));
            bytes[i] = v; bytes[i + 1] = v; bytes[i + 2] = v; bytes[i + 3] = 255;
        }
        SoaImage img = ColorSpace.RgbaU8ToLinear(bytes, w, h);
        var srgb = PrecomputeSrgb(img);

        float[] gX = Search1D.ComputeAxisGradient(img, axis: 1, srgb);
        float[] gY = Search1D.ComputeAxisGradient(img, axis: 0, srgb);

        // X axis: gradient should be small and roughly constant (smooth ramp)
        Assert.All(gX, v => Assert.True(v > 0 && v < 0.05f));
        var xAvg = gX.Average();
        var xVar = gX.Select(v => (v - xAvg) * (v - xAvg)).Average();
        Assert.True(xVar < 0.0001f, "X gradient should be roughly constant");

        // Y axis: all rows identical, so gradient ~0
        Assert.All(gY, v => Assert.True(v < 0.001f));
    }

    [Fact]
    public void ComputeAxisGradient_RoundedPanel_SpikesAtBorder()
    {
        int w = 200, h = 200;
        var bytes = new byte[w * h * 4];
        // Border at x=10 and x=189, y=10 and y=189; interior uniform
        byte borderR = 255, borderG = 0, borderB = 0;
        byte interiorR = 128, interiorG = 128, interiorB = 128;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            bool isBorder = x <= 10 || x >= 189 || y <= 10 || y >= 189;
            bytes[i] = isBorder ? borderR : interiorR;
            bytes[i + 1] = isBorder ? borderG : interiorG;
            bytes[i + 2] = isBorder ? borderB : interiorB;
            bytes[i + 3] = 255;
        }
        SoaImage img = ColorSpace.RgbaU8ToLinear(bytes, w, h);
        var srgb = PrecomputeSrgb(img);

        float[] gX = Search1D.ComputeAxisGradient(img, axis: 1, srgb);

        // Spikes at border transitions (x=10 and x=189)
        float maxGrad = gX.Max();
        float threshold = maxGrad * 0.5f;
        var spikePositions = gX.Select((v, i) => (v, i)).Where(t => t.v > threshold).Select(t => t.i).ToArray();

        // Border transitions should be near x=10 and x=189
        Assert.Contains(spikePositions, p => p >= 9 && p <= 11);
        Assert.Contains(spikePositions, p => p >= 187 && p <= 189);

        // Interior positions should have very low gradient
        Assert.All(gX.Skip(20).Take(160), v => Assert.True(v < maxGrad * 0.1f));
    }

    // --- ExtractEdgePositions tests (tasks 2.2-2.4) ---

    [Fact]
    public void ExtractEdgePositions_RoundedPanel_ReturnsBorderIndices()
    {
        int w = 200, h = 200;
        var bytes = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            bool isBorder = x <= 10 || x >= 189 || y <= 10 || y >= 189;
            bytes[i] = isBorder ? (byte)255 : (byte)128;
            bytes[i + 1] = isBorder ? (byte)0 : (byte)128;
            bytes[i + 2] = isBorder ? (byte)0 : (byte)128;
            bytes[i + 3] = 255;
        }
        SoaImage img = ColorSpace.RgbaU8ToLinear(bytes, w, h);
        var srgb = PrecomputeSrgb(img);

        float[] gX = Search1D.ComputeAxisGradient(img, axis: 1, srgb);
        int[] edges = Search1D.ExtractEdgePositions(gX);

        // Edge positions should be near x=10 and x=189 (within +/-1)
        Assert.All(edges, e =>
            Assert.True((e >= 9 && e <= 12) || (e >= 187 && e <= 191),
                $"Edge position {e} should be near border"));
        Assert.True(edges.Length > 0);
    }

    [Fact]
    public void ExtractEdgePositions_PureGradient_ReturnsEmptyOrFew()
    {
        int w = 100, h = 50;
        var bytes = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            byte v = (byte)(x * 255 / (w - 1));
            bytes[i] = v; bytes[i + 1] = v; bytes[i + 2] = v; bytes[i + 3] = 255;
        }
        SoaImage img = ColorSpace.RgbaU8ToLinear(bytes, w, h);
        var srgb = PrecomputeSrgb(img);

        float[] gX = Search1D.ComputeAxisGradient(img, axis: 1, srgb);
        int[] edges = Search1D.ExtractEdgePositions(gX);

        // Pure gradient should return empty or very few edges
        Assert.True(edges.Length < 5, $"Expected few edges for pure gradient, got {edges.Length}");
    }

    [Fact]
    public void ExtractEdgePositions_NoiseImage_ReturnsManyPositions()
    {
        int w = 32, h = 32;
        var bytes = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            byte v = (byte)(i % 2 * 255);
            bytes[i * 4] = v; bytes[i + 1] = v; bytes[i + 2] = v; bytes[i + 3] = 255;
        }
        SoaImage img = ColorSpace.RgbaU8ToLinear(bytes, w, h);
        var srgb = PrecomputeSrgb(img);

        float[] gX = Search1D.ComputeAxisGradient(img, axis: 1, srgb);
        int[] edges = Search1D.ExtractEdgePositions(gX);

        // Noise image should return many positions (but DetectNoisyAxis should fire first)
        Assert.True(edges.Length > gX.Length * 0.5f,
            $"Expected many edges for noise, got {edges.Length}/{gX.Length}");
    }

    // --- BuildCandidateSets tests (tasks 3.3-3.4) ---

    [Fact]
    public void BuildCandidateSets_WithEdges_ReturnsExpectedNeighbors()
    {
        var (b, e) = Search1D.BuildCandidateSets([10, 50, 100], margin: 0, hiBound: 200);

        // B should include 0 and each edge +/- neighborhood
        Assert.Contains(0, b);
        Assert.Contains(10, b); Assert.Contains(11, b); Assert.Contains(12, b);
        Assert.Contains(50, b); Assert.Contains(51, b); Assert.Contains(52, b);
        Assert.Contains(100, b); Assert.Contains(101, b); Assert.Contains(102, b);

        // E should include hiBound and each edge +/- neighborhood
        Assert.Contains(200, e);
        Assert.Contains(9, e); Assert.Contains(10, e); Assert.Contains(11, e);
        Assert.Contains(49, e); Assert.Contains(50, e); Assert.Contains(51, e);
        Assert.Contains(99, e); Assert.Contains(100, e); Assert.Contains(101, e);

        // Both should be sorted
        for (int i = 1; i < b.Length; i++) Assert.True(b[i] > b[i - 1], "B should be sorted");
        for (int i = 1; i < e.Length; i++) Assert.True(e[i] > e[i - 1], "E should be sorted");
    }

    [Fact]
    public void BuildCandidateSets_EmptyEdges_ReturnsStrideSampled()
    {
        var (b, e) = Search1D.BuildCandidateSets([], margin: 0, hiBound: 200);

        Assert.True(b.Length > 0, "B should have stride-sampled positions");
        Assert.True(e.Length > 0, "E should have stride-sampled positions");

        // Should cover the full range
        Assert.Contains(0, b);
        Assert.Contains(200, e);
    }

    [Fact]
    public void BuildCandidateSets_EdgeAtBoundary_ClampsCorrectly()
    {
        // Edge at position 0: B should include 0,1,2; E should NOT include -1 or 0
        // (E clamps to (margin, hiBound], and 0 > margin=0 is false)
        var (b0, e0) = Search1D.BuildCandidateSets([0], margin: 0, hiBound: 200);
        Assert.Contains(0, b0); Assert.Contains(1, b0); Assert.Contains(2, b0);
        Assert.DoesNotContain(-1, e0); Assert.DoesNotContain(0, e0); // 0 not > margin
        Assert.Contains(1, e0); Assert.Contains(200, e0);

        // Edge at hiBound-1: B should include 199 (200 is >= hiBound, clamped out)
        // E should include 198,199,200 (all <= hiBound and > margin)
        var (b1, e1) = Search1D.BuildCandidateSets([199], margin: 0, hiBound: 200);
        Assert.Contains(0, b1); // margin always in B
        Assert.Contains(199, b1);
        Assert.DoesNotContain(200, b1); // 200 >= hiBound, clamped out of B

        Assert.Contains(200, e1); Assert.Contains(199, e1); Assert.Contains(198, e1);
    }

    [Fact]
    public void ComputeAxisGradient_VerticalRamp_SmallOnY_ZeroOnX()
    {
        // Vertical ramp: gradient along Y axis, uniform along X
        // This exercises the axis=0 SIMD path
        int w = 64, h = 100; // wide enough to trigger SIMD
        var bytes = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            byte v = (byte)(y * 255 / (h - 1));
            bytes[i] = v; bytes[i + 1] = v; bytes[i + 2] = v; bytes[i + 3] = 255;
        }
        SoaImage img = ColorSpace.RgbaU8ToLinear(bytes, w, h);
        var srgb = PrecomputeSrgb(img);

        float[] gY = Search1D.ComputeAxisGradient(img, axis: 0, srgb);
        float[] gX = Search1D.ComputeAxisGradient(img, axis: 1, srgb);

        // Y axis: small and roughly constant (smooth vertical ramp)
        Assert.All(gY, v => Assert.True(v > 0 && v < 0.05f));
        var yAvg = gY.Average();
        var yVar = gY.Select(v => (v - yAvg) * (v - yAvg)).Average();
        Assert.True(yVar < 0.0001f, "Y gradient should be roughly constant");

        // X axis: all columns identical, so gradient ~0
        Assert.All(gX, v => Assert.True(v < 0.001f));
    }

    [Fact]
    public void ExtractEdgePositions_EmptyGradient_ReturnsEmpty()
    {
        int[] edges = Search1D.ExtractEdgePositions([]);
        Assert.Empty(edges);
    }

    // --- Helpers ---

    private static SoaImage CreateImageU8(int w, int h, byte r, byte g, byte b, byte a)
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

    private static PrecomputedSrgb PrecomputeSrgb(SoaImage img)
    {
        int pixelCount = img.PixelCount;
        var srgb = new PrecomputedSrgb(
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
                srgb.R[i + j] = sr[j];
                srgb.G[i + j] = sg[j];
                srgb.B[i + j] = sb[j];
            }
        }
        for (int i = vEnd; i < pixelCount; i++)
        {
            srgb.R[i] = ColorSpace.LinearToSrgbByte(img.R[i]) / 255f;
            srgb.G[i] = ColorSpace.LinearToSrgbByte(img.G[i]) / 255f;
            srgb.B[i] = ColorSpace.LinearToSrgbByte(img.B[i]) / 255f;
        }

        return srgb;
    }
}
