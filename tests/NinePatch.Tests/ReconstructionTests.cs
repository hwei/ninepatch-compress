using NinePatch.Core;
using Xunit;

namespace NinePatch.Tests;

public class ReconstructionTests
{
    [Fact]
    public void Compress2D_Reconstruct_Roundtrip_UniformImage()
    {
        byte[] img = CreateImageU8(100, 100, 128, 128, 128, 255);
        float[] imgLinear = ColorSpace.RgbaU8ToLinear(img);

        var resX = Search1D.SearchX(imgLinear, 100, 100, 4f);
        var resY = Search1D.SearchY(imgLinear, 100, 100, 4f);
        Assert.NotNull(resX);
        Assert.NotNull(resY);

        var (compressed, meta) = Compressor.Compress2D(imgLinear, 100, 100, resX.Value, resY.Value);

        int w2 = meta.CompressedW;
        int h2 = meta.CompressedH;

        float[] recon = Compressor.ReconstructStretched(compressed, w2, h2, meta);
        Assert.Equal(imgLinear.Length, recon.Length);

        float err = ErrorMetric.MaxError(imgLinear, recon);
        Assert.True(err <= 4f, $"2D error = {err}");
    }

    [Fact]
    public void Compress2D_Reconstruct_Roundtrip_HorizontalGradient()
    {
        int w = 100, h = 100;
        byte[] img = CreateHGradientU8(w, h);
        float[] imgLinear = ColorSpace.RgbaU8ToLinear(img);

        var resX = Search1D.SearchX(imgLinear, w, h, 4f);
        var resY = Search1D.SearchY(imgLinear, w, h, 4f);
        Assert.NotNull(resX);
        Assert.NotNull(resY);

        var (compressed, meta) = Compressor.Compress2D(imgLinear, w, h, resX.Value, resY.Value);
        float[] recon = Compressor.ReconstructStretched(compressed, meta.CompressedW, meta.CompressedH, meta);

        float err = ErrorMetric.MaxError(imgLinear, recon);
        Assert.True(err <= 5f, $"2D error = {err} for gradient image");
    }

    [Fact]
    public void Compress2D_Reconstruct_Roundtrip_ManualNinePatch()
    {
        // Build an image with a proper nine-patch structure:
        // 20px solid corners, 60px gradient stretch region, 20px solid borders.
        // Then manually specify the nine-patch split and verify roundtrip.
        int w = 100, h = 100;
        var img = new float[w * h * 4];

        int xb = 20, xe = 80, yb = 20, ye = 80;

        // Corners: solid colors
        SetRect(img, w, 0, 0, xb, yb, 1.0f, 0.0f, 0.0f, 1.0f);       // top-left: red
        SetRect(img, w, xe, 0, w - xe, yb, 0.0f, 1.0f, 0.0f, 1.0f);  // top-right: green
        SetRect(img, w, 0, ye, xb, h - ye, 0.0f, 0.0f, 1.0f, 1.0f);  // bottom-left: blue
        SetRect(img, w, xe, ye, w - xe, h - ye, 1.0f, 1.0f, 0.0f, 1.0f); // bottom-right: yellow

        // Top/bottom edges: horizontal gradient
        SetHGradient(img, w, xb, 0, xe - xb, yb, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f);
        SetHGradient(img, w, xb, ye, xe - xb, h - ye, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f, 0.0f);

        // Left/right edges: vertical gradient
        SetVGradient(img, w, 0, yb, xb, ye - yb, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f);
        SetVGradient(img, w, xe, yb, w - xe, ye - yb, 0.0f, 1.0f, 0.0f, 1.0f, 1.0f, 0.0f);

        // Center: bilinear gradient
        SetBilinear(img, w, xb, yb, xe - xb, ye - yb,
            1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f, 0.0f);

        byte[] imgU8 = ColorSpace.RgbaLinearToU8(img);

        // Manually specify the nine-patch split (N=60 = lossless for 60px region)
        var resX = new SearchResult1D(xb, xe, 60);
        var resY = new SearchResult1D(yb, ye, 60);

        var (compressed, meta) = Compressor.Compress2D(img, w, h, resX, resY);
        float[] recon = Compressor.ReconstructStretched(compressed, meta.CompressedW, meta.CompressedH, meta);

        float err = ErrorMetric.MaxError(img, recon);
        Assert.True(err <= 0.01f, $"2D error = {err} for manual nine-patch");
    }

    private static byte[] CreateImageU8(int w, int h, byte r, byte g, byte b, byte a)
    {
        var img = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            img[i * 4] = r;
            img[i * 4 + 1] = g;
            img[i * 4 + 2] = b;
            img[i * 4 + 3] = a;
        }
        return img;
    }

    private static byte[] CreateHGradientU8(int w, int h)
    {
        var img = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            byte v = (byte)(x * 255 / (w - 1));
            img[i] = v; img[i + 1] = v; img[i + 2] = v; img[i + 3] = 255;
        }
        return img;
    }

    private static void SetRect(float[] img, int w, int x, int y, int rw, int rh, float r, float g, float b, float a)
    {
        for (int dy = 0; dy < rh; dy++)
        for (int dx = 0; dx < rw; dx++)
        {
            int i = ((y + dy) * w + x + dx) * 4;
            img[i] = r; img[i + 1] = g; img[i + 2] = b; img[i + 3] = a;
        }
    }

    private static void SetHGradient(float[] img, int w, int x, int y, int rw, int rh, float r1, float g1, float b1, float r2, float g2, float b2)
    {
        for (int dy = 0; dy < rh; dy++)
        for (int dx = 0; dx < rw; dx++)
        {
            float t = rw > 1 ? (float)dx / (rw - 1) : 0;
            int i = ((y + dy) * w + x + dx) * 4;
            img[i] = r1 * (1 - t) + r2 * t;
            img[i + 1] = g1 * (1 - t) + g2 * t;
            img[i + 2] = b1 * (1 - t) + b2 * t;
            img[i + 3] = 1.0f;
        }
    }

    private static void SetVGradient(float[] img, int w, int x, int y, int rw, int rh, float r1, float g1, float b1, float r2, float g2, float b2)
    {
        for (int dy = 0; dy < rh; dy++)
        for (int dx = 0; dx < rw; dx++)
        {
            float t = rh > 1 ? (float)dy / (rh - 1) : 0;
            int i = ((y + dy) * w + x + dx) * 4;
            img[i] = r1 * (1 - t) + r2 * t;
            img[i + 1] = g1 * (1 - t) + g2 * t;
            img[i + 2] = b1 * (1 - t) + b2 * t;
            img[i + 3] = 1.0f;
        }
    }

    private static void SetBilinear(float[] img, int w, int x, int y, int rw, int rh,
        float r00, float g00, float b00, float r10, float g10, float b10, float r01, float g01, float b01, float r11, float g11, float b11)
    {
        for (int dy = 0; dy < rh; dy++)
        for (int dx = 0; dx < rw; dx++)
        {
            float tx = rw > 1 ? (float)dx / (rw - 1) : 0;
            float ty = rh > 1 ? (float)dy / (rh - 1) : 0;
            int i = ((y + dy) * w + x + dx) * 4;
            img[i]     = Lerp(Lerp(r00, r10, tx), Lerp(r01, r11, tx), ty);
            img[i + 1] = Lerp(Lerp(g00, g10, tx), Lerp(g01, g11, tx), ty);
            img[i + 2] = Lerp(Lerp(b00, b10, tx), Lerp(b01, b11, tx), ty);
            img[i + 3] = 1.0f;
        }
    }

    private static float Lerp(float a, float b, float t) => a * (1 - t) + b * t;
}
