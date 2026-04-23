using NinePatch.Core;
using Xunit;
using System.Text;

namespace NinePatch.Tests;

public class DebugTests
{
    [Fact]
    public void CheckBoundaryErrorSizes()
    {
        byte[] img = CreateImageU8(100, 100, 128, 128, 128, 255);
        SoaImage imgLinear = ColorSpace.RgbaU8ToLinear(img, 100, 100);

        var resX = Segmenter.SearchX(imgLinear, threshold: 4f);
        Assert.NotNull(resX);

        var resY = Segmenter.SearchY(imgLinear, threshold: 4f);
        Assert.NotNull(resY);

        // Now run Compress2D
        var (compressed, meta) = Compressor.Compress2D(imgLinear, resX.Value, resY.Value);
        Assert.NotNull(compressed);
        Assert.True(meta.Nx >= 2, $"Expected Nx >= 2, got {meta.Nx}");
        Assert.True(meta.Ny >= 2, $"Expected Ny >= 2, got {meta.Ny}");

        // Now reconstruct
        SoaImage recon = Compressor.ReconstructStretched(compressed, meta);
        Assert.Equal(imgLinear.PixelCount, recon.PixelCount);

        float err = ErrorMetric.MaxError(imgLinear, recon);
        Assert.True(err <= 4f);

        // Now try BoundaryError via the full pipeline
        var result = NinePatchCompressor.Compress(img, 100, 100);
        Assert.Equal(CompressStatus.Success, result.Status);
    }

    [Fact]
    public void DebugBoundaryError()
    {
        // Test the BoundaryError guard that returns 999f
        int w = 128, h = 96;
        SoaImage img = SoaImage.Create(w, h);
        // Simple gradient
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            img.R[i] = x / (float)w;
            img.G[i] = y / (float)h;
            img.B[i] = 0.5f;
            img.A[i] = 1.0f;
        }

        // Test both b=20,e=74 (len=54) and b=20,e=76 (len=56)
        TestBoundary(img, w, h, 20, 74, 0);
        TestBoundary(img, w, h, 20, 76, 0);
        TestBoundary(img, w, h, 20, 108, 1); // X axis
    }

    private static void TestBoundary(SoaImage img, int w, int h, int b, int e, int axis)
    {
        int len = e - b;
        int rw, rh;

        // Extract region per channel
        float[][] region = new float[4][];
        if (axis == 1)
        {
            rw = len; rh = h;
            for (int ch = 0; ch < 4; ch++)
            {
                var chData = GetChannel(img, ch);
                region[ch] = new float[len * h];
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(chData, (y * w + b) * 4, region[ch], y * len * 4, len * 4);
            }
        }
        else
        {
            rw = w; rh = len;
            for (int ch = 0; ch < 4; ch++)
            {
                var chData = GetChannel(img, ch);
                region[ch] = new float[w * len];
                for (int y = b; y < e; y++)
                    Buffer.BlockCopy(chData, y * w * 4, region[ch], (y - b) * w * 4, w * 4);
            }
        }

        var regionSoa = new SoaImage(region[0], region[1], region[2], region[3]) { Width = rw, Height = rh };

        float[][] down = new float[4][];
        for (int ch = 0; ch < 4; ch++)
            down[ch] = Resampler.Downsample1D(region[ch], rw, rh, 2, axis);

        int downW = axis == 1 ? 2 : rw;
        int downH = axis == 1 ? rh : 2;

        var up = SoaImage.Create(rw, rh);
        for (int ch = 0; ch < 4; ch++)
        {
            var upCh = Resampler.Upsample1D(down[ch], downW, downH, len, axis);
            SetChannel(ref up, ch, upCh);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"BoundaryError(axis={axis}, b={b}, e={e}, len={len})");
        sb.AppendLine($"  Region: {len * rw * rh} ({rw}x{rh}x4)");
        sb.AppendLine($"  Down: {down[0].Length}");
        sb.AppendLine($"  Up: {up.PixelCount * 4} (expected: {len * rw * rh})");
        sb.AppendLine($"  Match: {up.PixelCount * 4 == len * rw * rh}");
        if (up.PixelCount * 4 != len * rw * rh)
        {
            sb.AppendLine($"  MISMATCH! downW={downW}, downH={downH}");
        }
        else
        {
            float err = ErrorMetric.MaxError(regionSoa, up);
            sb.AppendLine($"  Error: {err}");
        }

        Console.WriteLine(sb.ToString());
        Assert.True(up.PixelCount == rw * rh, sb.ToString());
    }

    private static float[] GetChannel(SoaImage img, int ch) => ch switch
    {
        0 => img.R, 1 => img.G, 2 => img.B, 3 => img.A, _ => throw new System.ArgumentOutOfRangeException()
    };

    private static void SetChannel(ref SoaImage img, int ch, float[] data)
    {
        switch (ch) { case 0: img = img with { R = data }; break; case 1: img = img with { G = data }; break; case 2: img = img with { B = data }; break; case 3: img = img with { A = data }; break; }
    }

    [Fact]
    public void DebugHGradientRoundtrip()
    {
        int w = 100, h = 100;
        byte[] imgU8 = CreateHGradientU8(w, h);
        SoaImage imgLinear = ColorSpace.RgbaU8ToLinear(imgU8, w, h);

        var resX = Segmenter.SearchX(imgLinear, threshold: 4f);
        var resY = Segmenter.SearchY(imgLinear, threshold: 4f);

        // Gradient images may return null from Segmenter; use identity fallback
        SearchResult1D finalX = resX ?? new SearchResult1D(0, w, w);
        SearchResult1D finalY = resY ?? new SearchResult1D(0, h, h);

        var sb = new StringBuilder();
        sb.AppendLine($"Search X: {resX}");
        sb.AppendLine($"Search Y: {resY}");

        var (compressed, meta) = Compressor.Compress2D(imgLinear, finalX, finalY);
        sb.AppendLine($"Compressed: {meta.CompressedW}x{meta.CompressedH}");

        SoaImage recon = Compressor.ReconstructStretched(compressed, meta);

        // Check a few pixel values
        for (int y = 0; y < h; y += 25)
        for (int x = 0; x < w; x += 25)
        {
            int idx = y * w + x;
            float origSrgb = ColorSpace.LinearToSrgbByte(imgLinear.R[idx]);
            float reconSrgb = ColorSpace.LinearToSrgbByte(recon.R[idx]);
            sb.AppendLine($"  ({x},{y}) orig={origSrgb:F1} recon={reconSrgb:F1} diff={MathF.Abs(origSrgb - reconSrgb):F1}");
        }

        float err = ErrorMetric.MaxError(imgLinear, recon);
        sb.AppendLine($"MaxError = {err}");

        Console.WriteLine(sb.ToString());
        Assert.True(err <= 75f, sb.ToString());
    }

    [Fact]
    public void DebugManualNinePatch()
    {
        int w = 100, h = 100;
        SoaImage img = SoaImage.Create(w, h);

        int xb = 20, xe = 80, yb = 20, ye = 80;

        // Corners: solid colors
        SetRect(img, 0, 0, xb, yb, 1.0f, 0.0f, 0.0f, 1.0f);
        SetRect(img, xe, 0, w - xe, yb, 0.0f, 1.0f, 0.0f, 1.0f);
        SetRect(img, 0, ye, xb, h - ye, 0.0f, 0.0f, 1.0f, 1.0f);
        SetRect(img, xe, ye, w - xe, h - ye, 1.0f, 1.0f, 0.0f, 1.0f);

        // Top/bottom edges: horizontal gradient
        SetHGradient(img, xb, 0, xe - xb, yb, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f);
        SetHGradient(img, xb, ye, xe - xb, h - ye, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f, 0.0f);

        // Left/right edges: vertical gradient
        SetVGradient(img, 0, yb, xb, ye - yb, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f);
        SetVGradient(img, xe, yb, w - xe, ye - yb, 0.0f, 1.0f, 0.0f, 1.0f, 1.0f, 0.0f);

        // Center: bilinear gradient
        SetBilinear(img, xb, yb, xe - xb, ye - yb,
            1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f, 0.0f);

        var resX = new SearchResult1D(xb, xe, 60);
        var resY = new SearchResult1D(yb, ye, 60);

        var (compressed, meta) = Compressor.Compress2D(img, resX, resY);

        var sb = new StringBuilder();
        sb.AppendLine($"Compressed: {meta.CompressedW}x{meta.CompressedH}");
        sb.AppendLine($"cwLeft={xb} cwMid={resX.N} cwRight={w - xe}");
        sb.AppendLine($"chTop={yb} chMid={resY.N} chBottom={h - ye}");

        // Print compressed layout: top row, middle row, bottom row
        sb.AppendLine($"Top row: rows 0..{yb - 1} in compressed");
        sb.AppendLine($"Middle row: rows {yb}..{yb + resY.N - 1} in compressed");
        sb.AppendLine($"Bottom row: rows {yb + resY.N}..{meta.CompressedH - 1} in compressed");

        // Print some compressed pixel values
        sb.AppendLine("Compressed middle row pixels (row 20):");
        int midRowStart = yb * meta.CompressedW;
        for (int x = 0; x < meta.CompressedW; x++)
        {
            int idx = midRowStart + x; // row-major: row * width + col
            sb.AppendLine($"  [{x}] R={compressed.R[idx]:F3} G={compressed.G[idx]:F3} B={compressed.B[idx]:F3}");
        }

        // Reconstruct
        SoaImage recon = Compressor.ReconstructStretched(compressed, meta);

        // Verify compressed bottom-right corner is yellow
        int compressedBR = (meta.CompressedH - 1) * meta.CompressedW + (meta.CompressedW - 1);
        sb.AppendLine($"Compressed bottom-right pixel: R={compressed.R[compressedBR]:F3} G={compressed.G[compressedBR]:F3} B={compressed.B[compressedBR]:F3}");

        // Check recon bottom-right
        sb.AppendLine($"Recon array size = {recon.PixelCount}, expected = {w * h}");
        int reconBR = 99 * w + 99;
        sb.AppendLine($"recon[99,99] raw: R={recon.R[reconBR]:F3} G={recon.G[reconBR]:F3} B={recon.B[reconBR]:F3}");

        // Check specific original vs reconstructed pixels
        sb.AppendLine("Pixel checks (orig -> recon):");
        int[] checkX = { 0, 50, 99 };
        int[] checkY = { 0, 50, 99 };
        foreach (var cy in checkY)
        foreach (var cx in checkX)
        {
            int idx = cy * w + cx;
            sb.AppendLine($"  [{cx},{cy}] orig=({img.R[idx]:F2},{img.G[idx]:F2},{img.B[idx]:F2}) recon=({recon.R[idx]:F2},{recon.G[idx]:F2},{recon.B[idx]:F2})");
        }

        float err = ErrorMetric.MaxError(img, recon);
        sb.AppendLine($"  MaxError = {err}");

        System.Diagnostics.Debug.WriteLine(sb.ToString());
        Console.WriteLine(sb.ToString());
        Assert.True(err <= 0.01f, sb.ToString());
    }

    private static void SetRect(SoaImage img, int x, int y, int rw, int rh, float r, float g, float b, float a)
    {
        for (int dy = 0; dy < rh; dy++)
        for (int dx = 0; dx < rw; dx++)
        {
            int i = (y + dy) * img.Width + x + dx;
            img.R[i] = r; img.G[i] = g; img.B[i] = b; img.A[i] = a;
        }
    }

    private static void SetHGradient(SoaImage img, int x, int y, int rw, int rh, float r1, float g1, float b1, float r2, float g2, float b2)
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

    private static void SetVGradient(SoaImage img, int x, int y, int rw, int rh, float r1, float g1, float b1, float r2, float g2, float b2)
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

    private static void SetBilinear(SoaImage img, int x, int y, int rw, int rh,
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
}
