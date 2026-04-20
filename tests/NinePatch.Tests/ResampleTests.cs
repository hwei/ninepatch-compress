using NinePatch.Core;
using Xunit;

namespace NinePatch.Tests;

public class ResampleTests
{
    [Fact]
    public void BuildBoxWeights_UniformRegion_ShouldPreserveEnergy()
    {
        // 4 pixels downsampled to 2: weights should average correctly
        var w = Resampler.BuildBoxWeights(4, 2);
        // Each dst pixel covers 2 src pixels, weights should be 0.5
        Assert.Equal(0.5f, w[0, 0]);
        Assert.Equal(0.5f, w[0, 1]);
        Assert.Equal(0.5f, w[1, 2]);
        Assert.Equal(0.5f, w[1, 3]);
    }

    [Fact]
    public void Downsample1D_4to2_ShouldAverage()
    {
        // 1D: 4 pixels, single channel conceptually (we test all 4 channels)
        // RGBA: [R0,G0,B0,A0, R1,G1,B1,A1, ...]
        float[] src = [100, 0, 0, 1, 100, 0, 0, 1, 200, 0, 0, 1, 200, 0, 0, 1]; // 4 px, 4 ch
        float[] dst = Resampler.Downsample1D(src, 4, 1, 2, 1); // axis=1 (X), srcW=4, srcH=1

        Assert.Equal(8, dst.Length); // 2 pixels * 4 channels
        Assert.Equal(100f, dst[0], 1); // avg of 100,100
        Assert.Equal(200f, dst[4], 1); // avg of 200,200
    }

    [Fact]
    public void Upsample1D_2to4_HalfPixelCenter()
    {
        // 2 pixels [A=100, B=200] upsampled to 4
        // With half-pixel center: positions map to ~0.25, 0.75 interpolation
        float[] src = [100f, 0, 0, 1f, 200f, 0, 0, 1f]; // 2 pixels
        float[] dst = Resampler.Upsample1D(src, 2, 1, 4, 1);

        Assert.Equal(16, dst.Length);
        // First pixel: closer to A, last pixel: closer to B
        Assert.True(dst[0] < dst[4] && dst[4] < dst[8] && dst[8] < dst[12]);
        // Alpha should remain 1.0
        Assert.Equal(1f, dst[3], 3);
        Assert.Equal(1f, dst[7], 3);
        Assert.Equal(1f, dst[11], 3);
        Assert.Equal(1f, dst[15], 3);
    }

    [Fact]
    public void DownsampleUpsampleRoundtrip_SameSize()
    {
        float[] src = [50, 100, 150, 1, 80, 120, 160, 1, 110, 140, 170, 1, 140, 160, 180, 1]; // 4 pixels
        float[] down = Resampler.Downsample1D(src, 4, 1, 4, 1);
        float[] back = Resampler.Upsample1D(down, 4, 1, 4, 1);

        for (int i = 0; i < src.Length; i++)
            Assert.Equal(src[i], back[i], 5);
    }
}
