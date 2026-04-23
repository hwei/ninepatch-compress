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
        Assert.True(meta.SavingsPct > 0);
        Assert.True(meta.Error2d <= 4.0);
    }

    [Fact]
    public void FullPipeline_LowSavings_StillReturnsSuccess()
    {
        // Gradient image where nine-patch compression yields minimal savings.
        // Core algorithm should still return Success regardless of savings amount.
        int w = 100, h = 100;
        byte[] img = CreateHGradientU8(w, h);
        var result = NinePatchCompressor.Compress(img, w, h, threshold: 4.0);

        Assert.Equal(CompressStatus.Success, result.Status);
        Assert.NotNull(result.Meta);
        Assert.NotNull(result.CompressedRgba);

        var meta = result.Meta.Value;
        // Gradient images may fall back to identity with the new Segmenter pipeline;
        // the key is we still get a valid result
        Assert.True(meta.CompressedW > 0 && meta.CompressedH > 0);
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

    private static byte[] CreateHGradientU8(int w, int h)
    {
        var img = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            byte v = (byte)(x * 255 / (w - 1));
            img[i] = v; img[i + 1] = v; img[i + 2] = v; img[i + 3] = 255;
        }
        return img;
    }
}
