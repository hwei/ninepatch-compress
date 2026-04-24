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
        SoaImageLinear result = ColorSpace.DecodeSrgbRgba8ToLinear(input, 1, 1);

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
        SoaImageLinear linear = ColorSpace.DecodeSrgbRgba8ToLinear(original, 2, 1);
        byte[] back = ColorSpace.EncodeLinearToSrgbRgba8(linear);

        for (int i = 0; i < linear.PixelCount; i++)
        {
            Assert.True(Math.Abs(back[i * 4]     - original[i * 4])     <= 1);
            Assert.True(Math.Abs(back[i * 4 + 1] - original[i * 4 + 1]) <= 1);
            Assert.True(Math.Abs(back[i * 4 + 2] - original[i * 4 + 2]) <= 1);
            Assert.True(Math.Abs(back[i * 4 + 3] - original[i * 4 + 3]) <= 1);
        }
    }

    [Theory]
    [InlineData(1.0f, 0.5f, 0.0f, 1.0f)]   // fully opaque
    [InlineData(0.8f, 0.4f, 0.2f, 0.5f)]   // semi-transparent
    public void PremultiplyUnpremultiply_Roundtrip(float r, float g, float b, float a)
    {
        var linear = SoaImageLinear.Create(1, 1);
        linear.R[0] = r; linear.G[0] = g; linear.B[0] = b; linear.A[0] = a;

        var premul = ColorSpace.Premultiply(linear);
        var roundtrip = ColorSpace.Unpremultiply(premul);

        // Verify premultiplied values
        Assert.Equal(r * a, premul.R[0], 6);
        Assert.Equal(g * a, premul.G[0], 6);
        Assert.Equal(b * a, premul.B[0], 6);

        // Verify roundtrip back to original
        Assert.Equal(r, roundtrip.R[0], 5);
        Assert.Equal(g, roundtrip.G[0], 5);
        Assert.Equal(b, roundtrip.B[0], 5);
        Assert.Equal(a, roundtrip.A[0], 5);
    }

    [Fact]
    public void Unpremultiply_AlphaZero_RgbIsExactlyZero()
    {
        var linear = SoaImageLinear.Create(1, 1);
        linear.R[0] = 1f; linear.G[0] = 0.5f; linear.B[0] = 0.25f; linear.A[0] = 0f;

        var premul = ColorSpace.Premultiply(linear);
        // After premultiply: R=G=B=0 (because α=0), A=0
        Assert.Equal(0f, premul.R[0]);
        Assert.Equal(0f, premul.G[0]);
        Assert.Equal(0f, premul.B[0]);

        var roundtrip = ColorSpace.Unpremultiply(premul);
        Assert.Equal(0f, roundtrip.R[0]);
        Assert.Equal(0f, roundtrip.G[0]);
        Assert.Equal(0f, roundtrip.B[0]);
        Assert.Equal(0f, roundtrip.A[0]);
    }

    [Fact]
    public void Premultiply_VerifyPremulValues()
    {
        var linear = SoaImageLinear.Create(1, 1);
        linear.R[0] = 0.8f; linear.G[0] = 0.6f; linear.B[0] = 0.4f; linear.A[0] = 0.5f;

        var premul = ColorSpace.Premultiply(linear);

        Assert.Equal(0.8f * 0.5f, premul.R[0]);
        Assert.Equal(0.6f * 0.5f, premul.G[0]);
        Assert.Equal(0.4f * 0.5f, premul.B[0]);
        Assert.Equal(0.5f, premul.A[0]);
    }

    [Fact]
    public void ToPremulSrgb_EncodesRgbCopiesAlpha()
    {
        var premul = SoaImagePremul.Create(1, 1);
        premul.R[0] = 0.5f; premul.G[0] = 0.25f; premul.B[0] = 0.0f; premul.A[0] = 0.75f;

        var srgb = ColorSpace.ToPremulSrgb(premul);

        float expectedR = ColorSpace.LinearToSrgbFloat(0.5f) * 255f;
        float expectedG = ColorSpace.LinearToSrgbFloat(0.25f) * 255f;
        float expectedB = ColorSpace.LinearToSrgbFloat(0.0f) * 255f;

        Assert.Equal(expectedR, srgb.R[0], 3);
        Assert.Equal(expectedG, srgb.G[0], 3);
        Assert.Equal(expectedB, srgb.B[0], 3);
        Assert.Equal(0.75f, srgb.A[0]);
    }
}
