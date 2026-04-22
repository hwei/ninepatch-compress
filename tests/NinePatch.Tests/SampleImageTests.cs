using NinePatch.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NinePatch.Tests;

public class SampleImageTests
{
    private static readonly string SamplesDir = Path.Combine(
        Path.GetDirectoryName(typeof(SampleImageTests).Assembly.Location)!,
        "..", "..", "..", "..", "samples");

    private static (byte[] rgba, int w, int h) LoadPng(string name)
    {
        using var img = Image.Load<Rgba32>(Path.Combine(SamplesDir, name));
        var bytes = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(bytes);
        return (bytes, img.Width, img.Height);
    }

    [Fact]
    public void Compress_ZiyuanIconBg_ShouldFindNonIdentitySplit()
    {
        // Regression: this image has two separately compressible X regions
        // (two octagons with a hard vertical edge between them). The previous
        // greedy shrink algorithm got stuck on the middle edge and returned
        // identity fallback for X. Exhaustive (b, e) search must find the
        // interior of one of the octagons.
        var (rgba, w, h) = LoadPng("img_ziyuan_icon_bg.png");
        var result = NinePatchCompressor.Compress(rgba, w, h, threshold: 4.0);

        Assert.Equal(CompressStatus.Success, result.Status);
        Assert.NotNull(result.Meta);
        var meta = result.Meta.Value;

        // Non-identity on at least X (the failing axis).
        Assert.True(meta.Nx < w, $"Expected Nx < {w}, got {meta.Nx}");
        // Reasonable savings (prior behavior was 0% identity on both axes).
        Assert.True(meta.SavingsPct > 30.0, $"Expected savings > 30%, got {meta.SavingsPct:F1}%");
        Assert.True(meta.Error2d <= 4.0, $"2D error {meta.Error2d} exceeds threshold");
    }
}
