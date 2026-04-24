using NinePatch.Core;
using Xunit;

namespace NinePatch.Tests;

public class ReconstructionTests
{
    [Fact]
    public void Compress2D_Reconstruct_Roundtrip_UniformImage()
    {
        byte[] img = CreateImageU8(100, 100, 128, 128, 128, 255);
        SoaImageLinear imgLinear = ColorSpace.DecodeSrgbRgba8ToLinear(img, 100, 100);
        SoaImagePremul imgPremul = ColorSpace.Premultiply(imgLinear);

        var resX = Segmenter.SearchX(imgPremul, threshold: 4f);
        var resY = Segmenter.SearchY(imgPremul, threshold: 4f);
        Assert.NotNull(resX);
        Assert.NotNull(resY);

        var (compressed, meta) = Compressor.Compress2D(imgPremul, resX.Value, resY.Value);

        SoaImagePremul recon = Compressor.ReconstructStretched(compressed, meta);
        Assert.Equal(imgPremul.PixelCount, recon.PixelCount);

        SoaImagePremulSrgb origSrgb = ColorSpace.ToPremulSrgb(imgPremul);
        float err = ErrorMetric.MaxError(origSrgb, recon);
        Assert.True(err <= 4f, $"2D error = {err}");
    }

    [Fact]
    public void Compress2D_Reconstruct_Roundtrip_HorizontalGradient()
    {
        int w = 100, h = 100;
        byte[] img = CreateHGradientU8(w, h);
        SoaImageLinear imgLinear = ColorSpace.DecodeSrgbRgba8ToLinear(img, w, h);
        SoaImagePremul imgPremul = ColorSpace.Premultiply(imgLinear);

        var resX = Segmenter.SearchX(imgPremul, threshold: 4f);
        var resY = Segmenter.SearchY(imgPremul, threshold: 4f);

        // Gradient images may fall back to identity with Segmenter pipeline
        SearchResult1D finalX = resX ?? new SearchResult1D(0, w, w);
        SearchResult1D finalY = resY ?? new SearchResult1D(0, h, h);

        var (compressed, meta) = Compressor.Compress2D(imgPremul, finalX, finalY);
        SoaImagePremul recon = Compressor.ReconstructStretched(compressed, meta);

        SoaImagePremulSrgb origSrgb = ColorSpace.ToPremulSrgb(imgPremul);
        float err = ErrorMetric.MaxError(origSrgb, recon);
        // Gradient images have high reconstruction error when compressed
        Assert.True(err <= 75f, $"2D error = {err} for gradient image");
    }

    [Fact]
    public void Compress2D_Reconstruct_Roundtrip_ManualNinePatch()
    {
        int w = 100, h = 100;
        SoaImageLinear imgLinear = SoaImageLinear.Create(w, h);

        int xb = 20, xe = 80, yb = 20, ye = 80;

        // Corners: solid colors
        SetRect(imgLinear, 0, 0, xb, yb, 1.0f, 0.0f, 0.0f, 1.0f);
        SetRect(imgLinear, xe, 0, w - xe, yb, 0.0f, 1.0f, 0.0f, 1.0f);
        SetRect(imgLinear, 0, ye, xb, h - ye, 0.0f, 0.0f, 1.0f, 1.0f);
        SetRect(imgLinear, xe, ye, w - xe, h - ye, 1.0f, 1.0f, 0.0f, 1.0f);

        // Top/bottom edges: horizontal gradient
        SetHGradient(imgLinear, xb, 0, xe - xb, yb, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f);
        SetHGradient(imgLinear, xb, ye, xe - xb, h - ye, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f, 0.0f);

        // Left/right edges: vertical gradient
        SetVGradient(imgLinear, 0, yb, xb, ye - yb, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f);
        SetVGradient(imgLinear, xe, yb, w - xe, ye - yb, 0.0f, 1.0f, 0.0f, 1.0f, 1.0f, 0.0f);

        // Center: bilinear gradient
        SetBilinear(imgLinear, xb, yb, xe - xb, ye - yb,
            1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f, 0.0f);

        SoaImagePremul imgPremul = ColorSpace.Premultiply(imgLinear);

        // Manually specify the nine-patch split (N=60 = lossless for 60px region)
        var resX = new SearchResult1D(xb, xe, 60);
        var resY = new SearchResult1D(yb, ye, 60);

        var (compressed, meta) = Compressor.Compress2D(imgPremul, resX, resY);
        SoaImagePremul recon = Compressor.ReconstructStretched(compressed, meta);

        SoaImagePremulSrgb origSrgb = ColorSpace.ToPremulSrgb(imgPremul);
        float err = ErrorMetric.MaxError(origSrgb, recon);
        Assert.True(err <= 0.01f, $"2D error = {err} for manual nine-patch");
    }

    [Fact]
    public void Compress2D_Reconstruct_Roundtrip_OneWayIdentityY()
    {
        int w = 80, h = 60;
        byte[] img = CreateHGradientU8(w, h);
        SoaImageLinear imgLinear = ColorSpace.DecodeSrgbRgba8ToLinear(img, w, h);
        SoaImagePremul imgPremul = ColorSpace.Premultiply(imgLinear);

        // X compressed, Y identity
        var resX = new SearchResult1D(20, 60, 8);
        var resY = new SearchResult1D(0, h, h);

        var (compressed, meta) = Compressor.Compress2D(imgPremul, resX, resY);
        SoaImagePremul recon = Compressor.ReconstructStretched(compressed, meta);

        SoaImagePremulSrgb origSrgb = ColorSpace.ToPremulSrgb(imgPremul);
        float err = ErrorMetric.MaxError(origSrgb, recon);
        Assert.True(err <= 15f, $"2D error = {err} for one-way Y identity");
    }

    [Fact]
    public void FullPipeline_OneWay_YAxisIdentity()
    {
        // Tall, narrow image: X is compressible (solid), Y has too few rows
        // for Search1D to find a split (h=3 < 4), so Y falls back to identity.
        int w = 100, h = 3;
        byte[] img = CreateImageU8(w, h, 128, 128, 128, 255);

        var result = NinePatchCompressor.Compress(img, w, h, threshold: 4.0);

        Assert.Equal(CompressStatus.Success, result.Status);
        Assert.NotNull(result.Meta);
        var meta = result.Meta.Value;
        // Y should be identity
        Assert.Equal(0, meta.Yb);
        Assert.Equal(h, meta.Ye);
        Assert.Equal(h, meta.Ny);
        // X should have been compressed
        Assert.True(meta.Nx < w, $"Nx={meta.Nx} should be < {w}");
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

    private static void SetRect(SoaImageLinear img, int x, int y, int rw, int rh, float r, float g, float b, float a)
    {
        for (int dy = 0; dy < rh; dy++)
        for (int dx = 0; dx < rw; dx++)
        {
            int i = (y + dy) * img.Width + x + dx;
            img.R[i] = r; img.G[i] = g; img.B[i] = b; img.A[i] = a;
        }
    }

    private static void SetHGradient(SoaImageLinear img, int x, int y, int rw, int rh, float r1, float g1, float b1, float r2, float g2, float b2)
    {
        for (int dy = 0; dy < rh; dy++)
        for (int dx = 0; dx < rw; dx++)
        {
            float t = rw > 1 ? (float)dx / (rw - 1) : 0;
            int i = (y + dy) * img.Width + x + dx;
            img.R[i] = r1 * (1 - t) + r2 * t;
            img.G[i] = g1 * (1 - t) + g2 * t;
            img.B[i] = b1 * (1 - t) + b2 * t;
            img.A[i] = 1.0f;
        }
    }

    private static void SetVGradient(SoaImageLinear img, int x, int y, int rw, int rh, float r1, float g1, float b1, float r2, float g2, float b2)
    {
        for (int dy = 0; dy < rh; dy++)
        for (int dx = 0; dx < rw; dx++)
        {
            float t = rh > 1 ? (float)dy / (rh - 1) : 0;
            int i = (y + dy) * img.Width + x + dx;
            img.R[i] = r1 * (1 - t) + r2 * t;
            img.G[i] = g1 * (1 - t) + g2 * t;
            img.B[i] = b1 * (1 - t) + b2 * t;
            img.A[i] = 1.0f;
        }
    }

    private static void SetBilinear(SoaImageLinear img, int x, int y, int rw, int rh,
        float r00, float g00, float b00, float r10, float g10, float b10, float r01, float g01, float b01, float r11, float g11, float b11)
    {
        for (int dy = 0; dy < rh; dy++)
        for (int dx = 0; dx < rw; dx++)
        {
            float tx = rw > 1 ? (float)dx / (rw - 1) : 0;
            float ty = rh > 1 ? (float)dy / (rh - 1) : 0;
            int i = (y + dy) * img.Width + x + dx;
            img.R[i] = Lerp(Lerp(r00, r10, tx), Lerp(r01, r11, tx), ty);
            img.G[i] = Lerp(Lerp(g00, g10, tx), Lerp(g01, g11, tx), ty);
            img.B[i] = Lerp(Lerp(b00, b10, tx), Lerp(b01, b11, tx), ty);
            img.A[i] = 1.0f;
        }
    }

    private static float Lerp(float a, float b, float t) => a * (1 - t) + b * t;
}
