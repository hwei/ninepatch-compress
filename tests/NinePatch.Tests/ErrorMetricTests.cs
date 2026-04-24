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
    public void MaxError_IdenticalFullyTransparent_ShouldBeZero()
    {
        // Both original and reconstructed are fully transparent (α=0, RGB=0),
        // so error should be exactly zero.
        var origPremul = SoaImagePremul.Create(1, 1);
        origPremul.R[0] = 0f; origPremul.G[0] = 0f; origPremul.B[0] = 0f; origPremul.A[0] = 0f;
        var origSrgb = ColorSpace.ToPremulSrgb(origPremul);

        var reconPremul = SoaImagePremul.Create(1, 1);
        reconPremul.R[0] = 0f; reconPremul.G[0] = 0f; reconPremul.B[0] = 0f; reconPremul.A[0] = 0f;

        float err = ErrorMetric.MaxError(origSrgb, reconPremul);
        Assert.Equal(0f, err);
    }

    [Fact]
    public void MaxError_TransparentOriginal_NonzeroReconRgb_ShouldReportError()
    {
        // α=0 original (RGB=0 by convention) vs non-zero premul RGB in reconstructed.
        // The 4-channel kernel should report this RGB error (no alpha suppression).
        var origPremul = SoaImagePremul.Create(1, 1);
        origPremul.R[0] = 0f; origPremul.G[0] = 0f; origPremul.B[0] = 0f; origPremul.A[0] = 0f;
        var origSrgb = ColorSpace.ToPremulSrgb(origPremul);

        var reconPremul = SoaImagePremul.Create(1, 1);
        reconPremul.R[0] = 0.1f; reconPremul.G[0] = 0.05f; reconPremul.B[0] = 0f; reconPremul.A[0] = 0f;

        float err = ErrorMetric.MaxError(origSrgb, reconPremul);
        // sRGB(0.1) ≈ 0.37 → ×255 ≈ 94, but polynomial approx gives slightly less
        Assert.True(err > 50f, $"Expected RGB error > 50, got {err}");
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
