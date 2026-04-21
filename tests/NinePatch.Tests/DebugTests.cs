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
        float[] imgLinear = ColorSpace.RgbaU8ToLinear(img);

        var resX = Search1D.SearchX(imgLinear, 100, 100, 4f);
        Assert.NotNull(resX);

        var resY = Search1D.SearchY(imgLinear, 100, 100, 4f);
        Assert.NotNull(resY);

        // Now run Compress2D
        var (compressed, meta) = Compressor.Compress2D(imgLinear, 100, 100, resX.Value, resY.Value);
        Assert.NotNull(compressed);
        Assert.Equal(2, meta.Nx);
        Assert.Equal(2, meta.Ny);

        int w2 = meta.CompressedW;
        int h2 = meta.CompressedH;

        // Now reconstruct
        float[] recon = Compressor.ReconstructStretched(compressed, w2, h2, meta);
        Assert.Equal(imgLinear.Length, recon.Length);

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
        var img = new float[w * h * 4];
        // Simple gradient
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            img[i] = x / (float)w;
            img[i + 1] = y / (float)h;
            img[i + 2] = 0.5f;
            img[i + 3] = 1.0f;
        }

        // Test both b=20,e=74 (len=54) and b=20,e=76 (len=56)
        TestBoundary(img, w, h, 20, 74, 0);
        TestBoundary(img, w, h, 20, 76, 0);
        TestBoundary(img, w, h, 20, 108, 1); // X axis
    }

    private static void TestBoundary(float[] img, int w, int h, int b, int e, int axis)
    {
        int len = e - b;
        int rw, rh;
        float[] region;

        if (axis == 1)
        {
            region = new float[len * h * 4];
            rw = len; rh = h;
            for (int y = 0; y < h; y++)
            for (int x = b; x < e; x++)
            {
                int si = (y * w + x) * 4;
                int di = (y * len + (x - b)) * 4;
                region[di] = img[si]; region[di + 1] = img[si + 1];
                region[di + 2] = img[si + 2]; region[di + 3] = img[si + 3];
            }
        }
        else
        {
            region = new float[w * len * 4];
            rw = w; rh = len;
            for (int y = b; y < e; y++)
            for (int x = 0; x < w; x++)
            {
                int si = (y * w + x) * 4;
                int di = ((y - b) * w + x) * 4;
                region[di] = img[si]; region[di + 1] = img[si + 1];
                region[di + 2] = img[si + 2]; region[di + 3] = img[si + 3];
            }
        }

        float[] down = Resampler.Downsample1D(region, rw, rh, 2, axis);
        // After downsample to 2:
        // axis=0 (Y): down has rw cols × 2 rows → (srcW=rw, srcH=2)
        // axis=1 (X): down has 2 cols × rh rows → (srcW=2, srcH=rh)
        int downW = axis == 1 ? 2 : rw;
        int downH = axis == 1 ? rh : 2;
        float[] up = Resampler.Upsample1D(down, downW, downH, len, axis);

        var sb = new StringBuilder();
        sb.AppendLine($"BoundaryError(axis={axis}, b={b}, e={e}, len={len})");
        sb.AppendLine($"  Region: {region.Length} ({rw}x{rh}x4)");
        sb.AppendLine($"  Down: {down.Length}");
        sb.AppendLine($"  Up: {up.Length} (expected: {region.Length})");
        sb.AppendLine($"  Match: {up.Length == region.Length}");
        if (up.Length != region.Length)
        {
            sb.AppendLine($"  MISMATCH! downW={downW}, downH={downH}");
        }
        else
        {
            float err = ErrorMetric.MaxError(region, up);
            sb.AppendLine($"  Error: {err}");
        }

        Console.WriteLine(sb.ToString());
        Assert.True(up.Length == region.Length, sb.ToString());
    }

    [Fact]
    public void DebugHGradientRoundtrip()
    {
        int w = 100, h = 100;
        byte[] imgU8 = CreateHGradientU8(w, h);
        float[] imgLinear = ColorSpace.RgbaU8ToLinear(imgU8);

        var resX = Search1D.SearchX(imgLinear, w, h, 4f);
        var resY = Search1D.SearchY(imgLinear, w, h, 4f);

        var sb = new StringBuilder();
        sb.AppendLine($"Search X: {resX}");
        sb.AppendLine($"Search Y: {resY}");

        var (compressed, meta) = Compressor.Compress2D(imgLinear, w, h, resX.Value, resY.Value);
        sb.AppendLine($"Compressed: {meta.CompressedW}x{meta.CompressedH}");

        float[] recon = Compressor.ReconstructStretched(compressed, meta.CompressedW, meta.CompressedH, meta);

        // Check a few pixel values
        for (int y = 0; y < h; y += 25)
        for (int x = 0; x < w; x += 25)
        {
            int idx = (y * w + x) * 4;
            float origSrgb = ColorSpace.LinearToSrgbByte(imgLinear[idx]);
            float reconSrgb = ColorSpace.LinearToSrgbByte(recon[idx]);
            sb.AppendLine($"  ({x},{y}) orig={origSrgb:F1} recon={reconSrgb:F1} diff={MathF.Abs(origSrgb - reconSrgb):F1}");
        }

        float err = ErrorMetric.MaxError(imgLinear, recon);
        sb.AppendLine($"MaxError = {err}");

        Console.WriteLine(sb.ToString());
        Assert.True(err <= 5f, sb.ToString());
    }

    [Fact]
    public void DebugManualNinePatch()
    {
        int w = 100, h = 100;
        var img = new float[w * h * 4];

        int xb = 20, xe = 80, yb = 20, ye = 80;

        // Corners: solid colors
        SetRect(img, w, 0, 0, xb, yb, 1.0f, 0.0f, 0.0f, 1.0f);
        SetRect(img, w, xe, 0, w - xe, yb, 0.0f, 1.0f, 0.0f, 1.0f);
        SetRect(img, w, 0, ye, xb, h - ye, 0.0f, 0.0f, 1.0f, 1.0f);
        SetRect(img, w, xe, ye, w - xe, h - ye, 1.0f, 1.0f, 0.0f, 1.0f);

        // Top/bottom edges: horizontal gradient
        SetHGradient(img, w, xb, 0, xe - xb, yb, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f);
        SetHGradient(img, w, xb, ye, xe - xb, h - ye, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f, 0.0f);

        // Left/right edges: vertical gradient
        SetVGradient(img, w, 0, yb, xb, ye - yb, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f);
        SetVGradient(img, w, xe, yb, w - xe, ye - yb, 0.0f, 1.0f, 0.0f, 1.0f, 1.0f, 0.0f);

        // Center: bilinear gradient
        SetBilinear(img, w, xb, yb, xe - xb, ye - yb,
            1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f, 0.0f);

        var resX = new SearchResult1D(xb, xe, 60);
        var resY = new SearchResult1D(yb, ye, 60);

        var (compressed, meta) = Compressor.Compress2D(img, w, h, resX, resY);

        var sb = new StringBuilder();
        sb.AppendLine($"Compressed: {meta.CompressedW}x{meta.CompressedH}");
        sb.AppendLine($"cwLeft={xb} cwMid={resX.N} cwRight={w - xe}");
        sb.AppendLine($"chTop={yb} chMid={resY.N} chBottom={h - ye}");

        // Print compressed layout: top row, middle row, bottom row
        sb.AppendLine($"Top row: rows 0..{yb-1} in compressed");
        sb.AppendLine($"Middle row: rows {yb}..{yb+resY.N-1} in compressed");
        sb.AppendLine($"Bottom row: rows {yb+resY.N}..{meta.CompressedH-1} in compressed");

        // Print some compressed pixel values
        sb.AppendLine("Compressed middle row pixels (row 20):");
        int midRowStart = yb * meta.CompressedW;
        for (int x = 0; x < meta.CompressedW; x++)
        {
            int idx = midRowStart * 4 + x * 4;
            sb.AppendLine($"  [{x}] R={compressed[idx]:F3} G={compressed[idx+1]:F3} B={compressed[idx+2]:F3}");
        }

        // Print compressed bottom row first/last pixel
        sb.AppendLine("Compressed bottom row (row 24):");
        int botRowStart = (yb + resY.N) * meta.CompressedW;
        sb.AppendLine($"  [0]  R={compressed[botRowStart*4]:F3} G={compressed[botRowStart*4+1]:F3} B={compressed[botRowStart*4+2]:F3}");
        sb.AppendLine($"  [19] R={compressed[(botRowStart+19)*4]:F3} G={compressed[(botRowStart+19)*4+1]:F3} B={compressed[(botRowStart+19)*4+2]:F3}");
        sb.AppendLine($"  [20] R={compressed[(botRowStart+20)*4]:F3} G={compressed[(botRowStart+20)*4+1]:F3} B={compressed[(botRowStart+20)*4+2]:F3}");
        sb.AppendLine($"  [23] R={compressed[(botRowStart+23)*4]:F3} G={compressed[(botRowStart+23)*4+1]:F3} B={compressed[(botRowStart+23)*4+2]:F3}");
        sb.AppendLine($"  [24] R={compressed[(botRowStart+24)*4]:F3} G={compressed[(botRowStart+24)*4+1]:F3} B={compressed[(botRowStart+24)*4+2]:F3}");
        sb.AppendLine($"  [43] R={compressed[(botRowStart+43)*4]:F3} G={compressed[(botRowStart+43)*4+1]:F3} B={compressed[(botRowStart+43)*4+2]:F3}");

        // Reconstruct
        float[] recon = Compressor.ReconstructStretched(compressed, meta.CompressedW, meta.CompressedH, meta);

        // Verify compressed bottom-right corner is yellow
        int compressedBR = ((meta.CompressedH - 1) * meta.CompressedW + (meta.CompressedW - 1)) * 4;
        sb.AppendLine($"Compressed bottom-right pixel: R={compressed[compressedBR]:F3} G={compressed[compressedBR+1]:F3} B={compressed[compressedBR+2]:F3}");

        // Check recon bottom-right
        sb.AppendLine($"Recon array size = {recon.Length}, expected = {w * h * 4}");
        int reconBR = (99 * w + 99) * 4;
        sb.AppendLine($"recon[99,99] raw: R={recon[reconBR]:F3} G={recon[reconBR+1]:F3} B={recon[reconBR+2]:F3}");

        // Check specific original vs reconstructed pixels
        sb.AppendLine("Pixel checks (orig -> recon):");
        int[] checkX = { 0, 50, 99 };
        int[] checkY = { 0, 50, 99 };
        foreach (var cy in checkY)
        foreach (var cx in checkX)
        {
            int idx = (cy * w + cx) * 4;
            sb.AppendLine($"  [{cx},{cy}] orig=({img[idx]:F2},{img[idx+1]:F2},{img[idx+2]:F2}) recon=({recon[idx]:F2},{recon[idx+1]:F2},{recon[idx+2]:F2})");
        }

        float err = ErrorMetric.MaxError(img, recon);
        sb.AppendLine($"  MaxError = {err}");

        System.Diagnostics.Debug.WriteLine(sb.ToString());
        Console.WriteLine(sb.ToString());
        Assert.True(err <= 0.01f, sb.ToString());
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
