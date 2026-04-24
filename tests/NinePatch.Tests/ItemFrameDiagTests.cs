using NinePatch.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NinePatch.Tests;

public class ItemFrameDiagTests
{
    private static readonly string SamplesDir = Path.Combine(
        Path.GetDirectoryName(typeof(ItemFrameDiagTests).Assembly.Location)!,
        "..", "..", "..", "..", "samples");

    // Regression for a bug where the Y search path returned null on a 90°-symmetric
    // image because ExtractAxisSignals' vertical branch skipped the transpose.
    // After unifying Y onto `Transpose + X path`, both axes must find a split.
    [Fact]
    public void ItemFrame_BothAxesFindCompression()
    {
        using var img = Image.Load<Rgba32>(Path.Combine(SamplesDir, "item_frame.png"));
        var rgba = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(rgba);
        SoaImageLinear lin = ColorSpace.DecodeSrgbRgba8ToLinear(rgba, img.Width, img.Height);
        SoaImagePremul premul = ColorSpace.Premultiply(lin);

        var resX = Segmenter.SearchX(premul, threshold: 4f);
        var resY = Segmenter.SearchY(premul, threshold: 4f);

        Assert.NotNull(resX);
        Assert.NotNull(resY);
    }
}
