using NinePatch.Core;
using Xunit;

namespace NinePatch.Tests;

public class ColorSpaceTests
{
    [Fact]
    public void SrgbToLinearRoundtrip_ShouldBeWithin1()
    {
        // For every sRGB byte value, convert to linear and back — result must be within 1
        for (int i = 0; i < 256; i++)
        {
            float linear = ColorSpace.SrgbByteToLinear((byte)i);
            byte back = ColorSpace.LinearToSrgbByte(linear);
            Assert.True(Math.Abs(back - i) <= 1, $"Roundtrip failed for {i}: got {back}");
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(128, 128)]
    [InlineData(255, 255)]
    [InlineData(12, 12)]
    [InlineData(200, 200)]
    public void SrgbByteToLinear_KnownValues_ShouldRoundtrip(byte original, byte _)
    {
        float linear = ColorSpace.SrgbByteToLinear(original);
        Assert.InRange(linear, 0f, 1f);

        byte back = ColorSpace.LinearToSrgbByte(linear);
        Assert.True(Math.Abs(back - original) <= 1);
    }

    [Fact]
    public void RgbaU8ToLinear_ShouldConvertAllChannels()
    {
        byte[] input = [255, 128, 0, 200]; // RGBA single pixel
        SoaImage result = ColorSpace.RgbaU8ToLinear(input, 1, 1);

        Assert.Equal(1, result.PixelCount);
        Assert.InRange(result.R[0], 0.9f, 1.0f);  // R=255 → ~1.0
        Assert.InRange(result.G[0], 0.1f, 0.6f);  // G=128 → linear
        Assert.InRange(result.B[0], 0f, 0.01f);    // B=0 → 0.0
        Assert.Equal(200 / 255.0f, result.A[0], 2); // A=200 → 200/255
    }

    [Fact]
    public void RgbaLinearToU8_ShouldRoundtrip()
    {
        byte[] original = [0, 64, 128, 192, 255, 100, 50, 200]; // 2 pixels
        SoaImage linear = ColorSpace.RgbaU8ToLinear(original, 2, 1);
        byte[] back = ColorSpace.RgbaLinearToU8(linear);

        for (int i = 0; i < linear.PixelCount; i++)
        {
            Assert.True(Math.Abs(back[i * 4]     - original[i * 4])     <= 1);
            Assert.True(Math.Abs(back[i * 4 + 1] - original[i * 4 + 1]) <= 1);
            Assert.True(Math.Abs(back[i * 4 + 2] - original[i * 4 + 2]) <= 1);
            Assert.True(Math.Abs(back[i * 4 + 3] - original[i * 4 + 3]) <= 1);
        }
    }
}
