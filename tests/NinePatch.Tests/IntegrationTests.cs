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
        var dbg = $"compressed={meta.CompressedW}x{meta.CompressedH} orig={meta.OriginalW}x{meta.OriginalH} savings={meta.SavingsPct:F1}% err2d={meta.Error2d:F2}";
        // Check first few compressed pixel values
        var firstPixelR = result.CompressedRgba[0];
        var firstPixelG = result.CompressedRgba[1];
        var firstPixelB = result.CompressedRgba[2];
        var firstPixelA = result.CompressedRgba[3];
        dbg += $" firstPixel=({firstPixelR},{firstPixelG},{firstPixelB},{firstPixelA}) expected=(128,128,128,255)";
        Assert.True(meta.SavingsPct > 0, $"No savings: {dbg}");
        Assert.True(meta.Error2d <= 4.0, $"Error too high: {dbg}");
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
    public void FullPipeline_NonUniformOptimizable_ShouldRespectErrorThreshold()
    {
        // Regression: SearchResult1D.N must be interpreted as target pixel count, not rate.
        // Prior to the fix, Optimize stored rate (e.g. 2) but Compressor read it as target
        // length, producing wildly over-compressed output with error far above threshold.
        // This test uses an image whose interior is uniform but whose edges carry a hard
        // frame — Optimize finds a non-trivial compressible interior segment on both axes
        // with rate >= 2, so N_stored_as_rate would collapse the center to ~2 pixels and
        // blow up Error2d. A correct N (target length) keeps reconstruction within threshold.
        int w = 64, h = 64;
        var img = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            bool frame = x < 4 || x >= w - 4 || y < 4 || y >= h - 4;
            byte v = (byte)(frame ? 40 : 200);
            int i = (y * w + x) * 4;
            img[i] = v; img[i + 1] = v; img[i + 2] = v; img[i + 3] = 255;
        }

        const double threshold = 4.0;
        var result = NinePatchCompressor.Compress(img, w, h, threshold);

        Assert.Equal(CompressStatus.Success, result.Status);
        Assert.NotNull(result.Meta);
        var meta = result.Meta!.Value;

        // Meaningful compression happened on both axes (not identity fallback).
        Assert.True(meta.CompressedW < w, $"X axis did not compress: compressedW={meta.CompressedW}");
        Assert.True(meta.CompressedH < h, $"Y axis did not compress: compressedH={meta.CompressedH}");

        // And the reconstruction error stays within the threshold.
        Assert.True(meta.Error2d <= threshold, $"Error2d={meta.Error2d} exceeds threshold={threshold}");
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
