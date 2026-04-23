using NinePatch.Core;
using Xunit;

namespace NinePatch.Tests;

public class SearchTests
{
    [Fact]
    public void SearchX_UniformImage_ShouldFindSplit()
    {
        SoaImage img = CreateImage(100, 100, 128, 128, 128, 255);
        var result = Segmenter.SearchX(img, threshold: 4f, margin: 0);
        Assert.NotNull(result);
        Assert.Equal(0, result.Value.Begin);
        Assert.Equal(100, result.Value.End);
        Assert.True(result.Value.N < 50);
    }

    [Fact]
    public void SearchY_UniformImage_ShouldFindSplit()
    {
        SoaImage img = CreateImage(100, 100, 128, 128, 128, 255);
        var result = Segmenter.SearchY(img, threshold: 4f, margin: 0);
        Assert.NotNull(result);
        Assert.Equal(0, result.Value.Begin);
        Assert.Equal(100, result.Value.End);
    }

    [Fact]
    public void SearchX_NoiseImage_ShouldFailOrReturnSmallInterval()
    {
        SoaImage img = CreateNoiseImage(20, 20);
        var result = Segmenter.SearchX(img, threshold: 4f, margin: 0);
        if (result is not null)
        {
            Assert.True(result.Value.N < 20);
        }
    }

    [Fact]
    public void SearchX_WithMargin_ShouldRespectMargin()
    {
        SoaImage img = CreateImage(100, 100, 128, 128, 128, 255);
        var result = Segmenter.SearchX(img, threshold: 4f, margin: 10);
        Assert.NotNull(result);
        Assert.True(result.Value.Begin >= 10);
        Assert.True(result.Value.End <= 90);
    }

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

    private static SoaImage CreateNoiseImage(int w, int h)
    {
        var bytes = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            byte v = (byte)(i % 2 * 255);
            bytes[i * 4] = v;
            bytes[i * 4 + 1] = v;
            bytes[i * 4 + 2] = v;
            bytes[i * 4 + 3] = 255;
        }
        return ColorSpace.RgbaU8ToLinear(bytes, w, h);
    }
}
