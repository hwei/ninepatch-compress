namespace NinePatch.Core;

/// <summary>
/// sRGB ↔ Linear conversion using LUTs for SIMD-friendly indexed lookup.
/// Alpha is always linear; only RGB channels go through the gamma curve.
/// </summary>
public static class ColorSpace
{
    // 256-entry LUT: index is sRGB byte (0-255), value is linear float [0,1]
    private static readonly float[] SrgbToLinearLut = new float[256];

    // 4096-entry LUT: index = round(linear * 4095), value = sRGB byte (0-255)
    private static readonly byte[] LinearToSrgbLut = new byte[4096];

    static ColorSpace()
    {
        for (int i = 0; i < 256; i++)
            SrgbToLinearLut[i] = SrgbToLinearScalar(i / 255.0f);

        for (int i = 0; i < 4096; i++)
        {
            float linear = i / 4095.0f;
            LinearToSrgbLut[i] = LinearToSrgbScalar(linear);
        }
    }

    /// <summary>sRGB byte [0,255] → linear float [0,1]</summary>
    public static float SrgbByteToLinear(byte srgb) => SrgbToLinearLut[srgb];

    /// <summary>Linear float [0,1] → sRGB byte [0,255]</summary>
    public static byte LinearToSrgbByte(float linear)
    {
        int idx = (int)(linear * 4095.0f + 0.5f);
        return LinearToSrgbLut[Math.Clamp(idx, 0, 4095)];
    }

    /// <summary>RGBA uint8 (H×W×4, sRGB) → linear float array (H×W×4, float)</summary>
    public static float[] RgbaU8ToLinear(ReadOnlySpan<byte> src)
    {
        var dst = new float[src.Length];
        for (int i = 0; i < src.Length; i += 4)
        {
            dst[i]     = SrgbByteToLinear(src[i]);
            dst[i + 1] = SrgbByteToLinear(src[i + 1]);
            dst[i + 2] = SrgbByteToLinear(src[i + 2]);
            dst[i + 3] = src[i + 3] / 255.0f;
        }
        return dst;
    }

    /// <summary>Linear float array (H×W×4) → RGBA uint8 (H×W×4, sRGB)</summary>
    public static byte[] RgbaLinearToU8(ReadOnlySpan<float> src)
    {
        var dst = new byte[src.Length];
        for (int i = 0; i < src.Length; i += 4)
        {
            dst[i]     = LinearToSrgbByte(src[i]);
            dst[i + 1] = LinearToSrgbByte(src[i + 1]);
            dst[i + 2] = LinearToSrgbByte(src[i + 2]);
            dst[i + 3] = (byte)Math.Clamp((int)(src[i + 3] * 255.0f + 0.5f), 0, 255);
        }
        return dst;
    }

    // --- Scalar implementations for LUT init ---

    private static float SrgbToLinearScalar(float c)
    {
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    private static byte LinearToSrgbScalar(float c)
    {
        float s = c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1.0f / 2.4f) - 0.055f;
        return (byte)Math.Clamp((int)(s * 255.0f + 0.5f), 0, 255);
    }
}
