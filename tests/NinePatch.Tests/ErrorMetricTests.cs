using NinePatch.Core;
using Xunit;

namespace NinePatch.Tests;

public class ErrorMetricTests
{
    [Fact]
    public void MaxError_IdenticalImages_ShouldBeZero()
    {
        SoaImage img = SoaImage.Create(2, 1);
        img.R[0] = 0.5f; img.G[0] = 0.5f; img.B[0] = 0.5f; img.A[0] = 1f;
        img.R[1] = 0.2f; img.G[1] = 0.3f; img.B[1] = 0.4f; img.A[1] = 1f;
        float err = ErrorMetric.MaxError(img, img);
        Assert.Equal(0f, err);
    }

    [Fact]
    public void MaxError_FullyTransparent_ShouldBeZero()
    {
        // Even if RGB differs, alpha=0 means error should be suppressed
        SoaImage orig = SoaImage.Create(1, 1);
        orig.R[0] = 0f; orig.G[0] = 0f; orig.B[0] = 0f; orig.A[0] = 0f;
        SoaImage recon = SoaImage.Create(1, 1);
        recon.R[0] = 1f; recon.G[0] = 1f; recon.B[0] = 1f; recon.A[0] = 0f;
        float err = ErrorMetric.MaxError(orig, recon, alphaWeighted: true);
        Assert.Equal(0f, err);
    }

    [Fact]
    public void MaxError_AlphaOnlyDifference_ShouldReportAlphaError()
    {
        SoaImage orig = SoaImage.Create(1, 1);
        orig.R[0] = 0.5f; orig.G[0] = 0.5f; orig.B[0] = 0.5f; orig.A[0] = 1f;
        SoaImage recon = SoaImage.Create(1, 1);
        recon.R[0] = 0.5f; recon.G[0] = 0.5f; recon.B[0] = 0.5f; recon.A[0] = 0.5f;
        float err = ErrorMetric.MaxError(orig, recon);
        Assert.Equal(127.5f, err, 0); // |255 - 127.5| ≈ 128
    }
}
