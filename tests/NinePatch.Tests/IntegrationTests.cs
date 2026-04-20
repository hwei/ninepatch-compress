using NinePatch.Core;
using Xunit;

namespace NinePatch.Tests;

public class IntegrationTests
{
    [Fact]
    public void FullPipeline_UniformImage_ShouldSucceed()
    {
        byte[] img = CreateImageU8(100, 100, 128, 128, 128, 255);
        var result = NinePatchCompressor.Compress(img, 100, 100);

        Assert.Equal(CompressStatus.Success, result.Status);
        Assert.NotNull(result.Meta);
        Assert.NotNull(result.CompressedRgba);

        var meta = result.Meta.Value;
        Assert.True(meta.SavingsPct >= 30.0);
        Assert.True(meta.Error2d <= 4.0);
    }

    [Fact]
    public void FullPipeline_InvalidDimensions_ShouldFail()
    {
        byte[] small = new byte[10];
        var result = NinePatchCompressor.Compress(small, 3, 3);
        Assert.Equal(CompressStatus.InvalidInput, result.Status);
    }

    [Fact]
    public void FullPipeline_HighThreshold_ShouldCompressMore()
    {
        byte[] img = CreateImageU8(100, 100, 128, 128, 128, 255);
        var result1 = NinePatchCompressor.Compress(img, 100, 100, threshold: 4.0);
        var result2 = NinePatchCompressor.Compress(img, 100, 100, threshold: 20.0);

        Assert.NotNull(result1.Meta);
        Assert.NotNull(result2.Meta);
        Assert.True(result2.Meta.Value.CompressedW * result2.Meta.Value.CompressedH
            <= result1.Meta.Value.CompressedW * result1.Meta.Value.CompressedH);
    }

    [Fact]
    public void FullPipeline_TransparentImage_ShouldHandleAlpha()
    {
        byte[] img = CreateImageU8(50, 50, 255, 0, 0, 0); // fully transparent
        var result = NinePatchCompressor.Compress(img, 50, 50, threshold: 4.0);
        // Should succeed since all pixels are transparent (error suppressed)
        Assert.Equal(CompressStatus.Success, result.Status);
    }

    private static byte[] CreateImageU8(int w, int h, byte r, byte g, byte b, byte a)
    {
        var img = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            img[i * 4] = r;
            img[i * 4 + 1] = g;
            img[i * 4 + 2] = b;
            img[i * 4 + 3] = a;
        }
        return img;
    }
}
