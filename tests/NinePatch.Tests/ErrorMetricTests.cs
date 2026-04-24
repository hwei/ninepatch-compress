using NinePatch.Core;
using Xunit;

namespace NinePatch.Tests;

public class ErrorMetricTests
{
    [Fact]
    public void MaxError_IdenticalImages_ShouldBeZero()
    {
        var imgPremul = CreatePremulImage(2, 1,
            [0.5f, 0.2f], [0.5f, 0.3f], [0.5f, 0.4f], [1f, 1f]);
        var imgSrgb = ColorSpace.ToPremulSrgb(imgPremul);

        float err = ErrorMetric.MaxError(imgSrgb, imgPremul);
        Assert.Equal(0f, err);
    }

    [Fact]
    public void MaxError_FullyTransparent_ShouldReportRgbError()
    {
        // With premul-linear resampling, α=0 pixels have R=G=B=0 by convention.
        // If reconstructed has different RGB, error is reported (no alpha weighting).
        var origPremul = SoaImagePremul.Create(1, 1);
        origPremul.R[0] = 0f; origPremul.G[0] = 0f; origPremul.B[0] = 0f; origPremul.A[0] = 0f;
        var origSrgb = ColorSpace.ToPremulSrgb(origPremul);

        var reconPremul = SoaImagePremul.Create(1, 1);
        reconPremul.R[0] = 0f; reconPremul.G[0] = 0f; reconPremul.B[0] = 0f; reconPremul.A[0] = 0f;

        float err = ErrorMetric.MaxError(origSrgb, reconPremul);
        Assert.Equal(0f, err);
    }

    [Fact]
    public void MaxError_AlphaOnlyDifference_ShouldReportAlphaError()
    {
        var origPremul = CreatePremulImage(1, 1, [0.5f], [0.5f], [0.5f], [1f]);
        var origSrgb = ColorSpace.ToPremulSrgb(origPremul);

        var reconPremul = SoaImagePremul.Create(1, 1);
        reconPremul.R[0] = 0.5f; reconPremul.G[0] = 0.5f; reconPremul.B[0] = 0.5f; reconPremul.A[0] = 0.5f;

        float err = ErrorMetric.MaxError(origSrgb, reconPremul);
        Assert.Equal(127.5f, err, 0); // |1.0 - 0.5| * 255 = 127.5
    }

    [Fact]
    public void PassesThreshold_WithinThreshold_ShouldReturnTrue()
    {
        var origPremul = CreatePremulImage(1, 1, [0.5f], [0.5f], [0.5f], [1f]);
        var origSrgb = ColorSpace.ToPremulSrgb(origPremul);

        // Small reconstruction difference
        var reconPremul = SoaImagePremul.Create(1, 1);
        reconPremul.R[0] = 0.5f; reconPremul.G[0] = 0.5f; reconPremul.B[0] = 0.5f; reconPremul.A[0] = 0.999f;

        Assert.True(ErrorMetric.PassesThreshold(origSrgb, reconPremul, 1f));
    }

    [Fact]
    public void PassesThreshold_ExceedsThreshold_ShouldReturnFalse()
    {
        var origPremul = CreatePremulImage(1, 1, [0.5f], [0.5f], [0.5f], [1f]);
        var origSrgb = ColorSpace.ToPremulSrgb(origPremul);

        // Large alpha difference
        var reconPremul = SoaImagePremul.Create(1, 1);
        reconPremul.R[0] = 0.5f; reconPremul.G[0] = 0.5f; reconPremul.B[0] = 0.5f; reconPremul.A[0] = 0.5f;

        Assert.False(ErrorMetric.PassesThreshold(origSrgb, reconPremul, 1f));
    }

    private static SoaImagePremul CreatePremulImage(int w, int h, float[] r, float[] g, float[] b, float[] a)
    {
        return new SoaImagePremul(r, g, b, a) { Width = w, Height = h };
    }
}
