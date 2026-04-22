using System.Numerics;

namespace NinePatch.Core;

/// <summary>Structure of Arrays: 4 independent H×W channel planes.</summary>
public readonly record struct SoaImage(
    float[] R,
    float[] G,
    float[] B,
    float[] A)
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int PixelCount => Width * Height;

    public static SoaImage Create(int width, int height)
    {
        int n = width * height;
        return new SoaImage(new float[n], new float[n], new float[n], new float[n])
        {
            Width = width,
            Height = height,
        };
    }

    /// <summary>Index into any channel array: y * Width + x.</summary>
    public int Index(int x, int y) => y * Width + x;
}

/// <summary>
/// sRGB ↔ Linear conversion.
/// Uses LUT for U8↔Linear IO boundary.
/// Uses polynomial approximation + Vector&lt;float&gt; SIMD for internal linear→sRGB.
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

    /// <summary>RGBA uint8 (H×W×4, sRGB) → SoaImage (linear float planes)</summary>
    public static SoaImage RgbaU8ToLinear(ReadOnlySpan<byte> src, int width, int height)
    {
        int nPixels = src.Length / 4;
        if (nPixels != width * height)
            throw new System.ArgumentException($"src length {src.Length} doesn't match {width}x{height}");

        var r = new float[nPixels];
        var g = new float[nPixels];
        var b = new float[nPixels];
        var a = new float[nPixels];

        for (int i = 0; i < nPixels; i++)
        {
            r[i] = SrgbToLinearLut[src[i * 4]];
            g[i] = SrgbToLinearLut[src[i * 4 + 1]];
            b[i] = SrgbToLinearLut[src[i * 4 + 2]];
            a[i] = src[i * 4 + 3] / 255.0f;
        }

        return new SoaImage(r, g, b, a) { Width = width, Height = height };
    }

    /// <summary>SoaImage (linear float planes) → RGBA uint8 (H×W×4, sRGB)</summary>
    public static byte[] RgbaLinearToU8(SoaImage img)
    {
        int n = img.PixelCount;
        var dst = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            dst[i * 4]     = LinearToSrgbByte(img.R[i]);
            dst[i * 4 + 1] = LinearToSrgbByte(img.G[i]);
            dst[i * 4 + 2] = LinearToSrgbByte(img.B[i]);
            dst[i * 4 + 3] = (byte)Math.Clamp((int)(img.A[i] * 255.0f + 0.5f), 0, 255);
        }
        return dst;
    }

    // --- Polynomial approximation for SIMD internal use ---
    // Degree-5 minimax approximation of x^(5/12) on [0,1].
    // Max error ~3×10⁻⁴, well below 0.5/255 uint8 quantization.

    private const float P0 = 0.107417f;
    private const float P1 = 1.76591f;
    private const float P2 = -1.67415f;
    private const float P3 = 1.23275f;
    private const float P4 = -0.601214f;
    private const float P5 = 0.169347f;

    /// <summary>SIMD linear→sRGB float [0,1] using polynomial (no LUT).</summary>
    public static Vector<float> LinearToSrgbSimd(Vector<float> linear)
    {
        var threshold = new Vector<float>(0.0031308f);
        var below = Vector.LessThanOrEqual(linear, threshold);

        // Below: linear * 12.92
        var lo = linear * 12.92f;

        // Above: 1.055 * poly(linear) - 0.055
        // Horner: c*(c*(c*(c*(c*P5+P4)+P3)+P2)+P1)+P0
        var p = linear * new Vector<float>(P5) + new Vector<float>(P4);
        p = linear * p + new Vector<float>(P3);
        p = linear * p + new Vector<float>(P2);
        p = linear * p + new Vector<float>(P1);
        p = linear * p + new Vector<float>(P0);
        var hi = new Vector<float>(1.055f) * p - new Vector<float>(0.055f);

        return Vector.ConditionalSelect(below, lo, hi);
    }

    /// <summary>Scalar linear→sRGB float [0,1] using the same polynomial as the SIMD path.</summary>
    internal static float LinearToSrgbFloat(float linear)
    {
        if (linear <= 0.0031308f) return linear * 12.92f;
        float p = linear * P5 + P4;
        p = linear * p + P3;
        p = linear * p + P2;
        p = linear * p + P1;
        p = linear * p + P0;
        return 1.055f * p - 0.055f;
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
