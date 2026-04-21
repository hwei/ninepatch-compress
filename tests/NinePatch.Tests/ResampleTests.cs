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
        // Single channel: 4 pixels downsampled to 2
        float[] src = [100f, 100f, 200f, 200f]; // 4 pixels, 1 channel
        float[] dst = Resampler.Downsample1D(src, 4, 1, 2, 1); // axis=1 (X), srcW=4, srcH=1

        Assert.Equal(2, dst.Length); // 2 pixels, 1 channel
        Assert.Equal(100f, dst[0], 1); // avg of 100,100
        Assert.Equal(200f, dst[1], 1); // avg of 200,200
    }

    [Fact]
    public void Upsample1D_2to4_HalfPixelCenter()
    {
        // 2 pixels [A=100, B=200] upsampled to 4 (single channel)
        float[] src = [100f, 200f]; // 2 pixels, 1 channel
        float[] dst = Resampler.Upsample1D(src, 2, 1, 4, 1);

        Assert.Equal(4, dst.Length);
        // First pixel: closer to A, last pixel: closer to B
        Assert.True(dst[0] < dst[1] && dst[1] < dst[2] && dst[2] < dst[3]);
    }

    [Fact]
    public void DownsampleUpsampleRoundtrip_SameSize()
    {
        float[] src = [50f, 80f, 110f, 140f]; // 4 pixels, 1 channel
        float[] down = Resampler.Downsample1D(src, 4, 1, 4, 1);
        float[] back = Resampler.Upsample1D(down, 4, 1, 4, 1);

        for (int i = 0; i < src.Length; i++)
            Assert.Equal(src[i], back[i], 5);
    }
}
