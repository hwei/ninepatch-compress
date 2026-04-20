using NinePatch.Core;
using Xunit;

namespace NinePatch.Tests;

public class ErrorMetricTests
{
    [Fact]
    public void MaxError_IdenticalImages_ShouldBeZero()
    {
        float[] img = [0.5f, 0.5f, 0.5f, 1f, 0.2f, 0.3f, 0.4f, 1f];
        float err = ErrorMetric.MaxError(img, img);
        Assert.Equal(0f, err);
    }

    [Fact]
    public void MaxError_FullyTransparent_ShouldBeZero()
    {
        // Even if RGB differs, alpha=0 means error should be suppressed
        float[] orig = [0f, 0f, 0f, 0f];
        float[] recon = [1f, 1f, 1f, 0f];
        float err = ErrorMetric.MaxError(orig, recon, alphaWeighted: true);
        Assert.Equal(0f, err);
    }

    [Fact]
    public void MaxError_AlphaOnlyDifference_ShouldReportAlphaError()
    {
        float[] orig = [0.5f, 0.5f, 0.5f, 1f];
        float[] recon = [0.5f, 0.5f, 0.5f, 0.5f];
        float err = ErrorMetric.MaxError(orig, recon);
        Assert.Equal(127.5f, err, 0); // |255 - 127.5| ≈ 128
    }
}
