// Quick profiler for Search1D hot path using small test images.
// Usage: dotnet run --project src/NinePatch.Bench/NinePatch.Bench.csproj

using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using NinePatch.Core;

var images = new (string name, byte[] rgba, int w, int h)[]
{
    Load("tests/samples/hgrad.png"),
    Load("tests/samples/rounded_panel.png"),
    Load("tests/samples/img_hero_pic_201_1.png"), // hard-to-compress noise image
};

const float threshold = 4f;

// High-level timing
foreach (var (name, rgba, w, h) in images)
{
    Console.WriteLine($"=== {name} ({w}x{h}) ===");
    var img = ColorSpace.RgbaU8ToLinear(rgba, w, h);

    var sw = Stopwatch.StartNew();
    var resultX = Search1D.SearchX(img, threshold, margin: 0);
    sw.Stop();
    Console.WriteLine($"  SearchX: {sw.ElapsedMilliseconds}ms, result={resultX}");

    sw.Restart();
    var resultY = Search1D.SearchY(img, threshold, margin: 0);
    sw.Stop();
    Console.WriteLine($"  SearchY: {sw.ElapsedMilliseconds}ms, result={resultY}");
    Console.WriteLine();
}

// Detailed breakdown: instrument individual operations on hgrad
Console.WriteLine("=== Detailed breakdown (hgrad 100x100, X axis) ===");
{
    var (name, rgba, w, h) = images[0];
    var img = ColorSpace.RgbaU8ToLinear(rgba, w, h);
    int len = img.Width, margin = 0;
    int maxN = len / 2;
    int b = 0, e = len;

    // Time the full MeasureError pipeline (Compress1D + ErrorMetric)
    // by running Search1D with a known result and timing individual components
    var recon = SoaImage.Create(w, h);

    // Measure a single Compress1D+Error cycle
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < 100; i++)
    {
        BenchMeasureError(img, b, e, maxN, axis: 1, recon);
    }
    sw.Stop();
    double msPerCall = sw.ElapsedMilliseconds / 100.0;
    Console.WriteLine($"  1x MeasureError (len={len}, N={maxN}): {msPerCall:F3}ms");

    // Time just ErrorMetric
    BenchMeasureError(img, b, e, maxN, axis: 1, recon);
    sw.Restart();
    for (int i = 0; i < 1000; i++)
        ErrorMetric.MaxError(img, recon);
    sw.Stop();
    double errMs = sw.ElapsedMilliseconds / 1000.0;
    Console.WriteLine($"  1x ErrorMetric (full image):    {errMs:F3}ms");
    Console.WriteLine($"  1x Compress1D (est):            {msPerCall - errMs:F3}ms");

    // Time Resampler.Downsample1D alone
    var region = new float[len * h];
    for (int y = 0; y < h; y++)
        Buffer.BlockCopy(img.R, (y * w + b) * 4, region, y * len * 4, len * 4);

    sw.Restart();
    for (int i = 0; i < 100; i++)
        Resampler.Downsample1D(region, len, h, maxN, 1);
    sw.Stop();
    Console.WriteLine($"  1x Downsample1D:                {sw.ElapsedMilliseconds / 100.0:F3}ms");

    // Time Resampler.Upsample1D alone
    var down = Resampler.Downsample1D(region, len, h, maxN, 1);
    sw.Restart();
    for (int i = 0; i < 100; i++)
        Resampler.Upsample1D(down, maxN, h, len, 1);
    sw.Stop();
    Console.WriteLine($"  1x Upsample1D:                  {sw.ElapsedMilliseconds / 100.0:F3}ms");

    // Count how many MeasureError calls the full search makes
    int totalCalls = 0, quickRejectCount = 0, binarySearchCount = 0, totalBsSteps = 0;
    int bestSaving = -1;

    for (int l2 = len - 2 * margin; l2 >= 4; l2--)
    {
        if (l2 - 2 <= bestSaving) break;
        int mN = l2 / 2;
        for (int b2 = margin; b2 + l2 <= len - margin; b2++)
        {
            int e2 = b2 + l2;
            totalCalls++;
            quickRejectCount++;

            var (_, okMax) = BenchTryN(img, b2, e2, mN, threshold, 1);
            if (!okMax) continue;

            binarySearchCount++;
            int lo = 2, hi = mN - 1, foundN = mN;
            while (lo <= hi)
            {
                totalBsSteps++;
                totalCalls++;
                var (_, ok) = BenchTryN(img, b2, e2, (lo + hi) / 2, threshold, 1);
                if (ok) { foundN = (lo + hi) / 2; hi = (lo + hi) / 2 - 1; }
                else lo = (lo + hi) / 2 + 1;
            }

            int saving = l2 - foundN;
            if (saving > bestSaving) { bestSaving = saving; if (foundN == 2) break; }
        }
    }

    Console.WriteLine();
    Console.WriteLine($"  Total MeasureError calls: {totalCalls}");
    Console.WriteLine($"  Quick rejects:            {quickRejectCount}");
    Console.WriteLine($"  Binary searches:          {binarySearchCount}");
    Console.WriteLine($"  Total BS steps:           {totalBsSteps}");
    Console.WriteLine($"  ESTIMATED total:          {totalCalls * msPerCall:F0}ms");

    // Break down time spent by category
    double qrTime = quickRejectCount * msPerCall;
    double bsTime = totalBsSteps * msPerCall;
    Console.WriteLine($"  Time in quick rejects:    {qrTime / 1000.0:F0}ms");
    Console.WriteLine($"  Time in binary search:    {bsTime / 1000.0:F0}ms");
}

static (float error, bool passes) BenchTryN(SoaImage img, int b, int e, int n, float threshold, int axis)
{
    var recon = SoaImage.Create(img.Width, img.Height);
    BenchMeasureError(img, b, e, n, axis, recon);
    float err = ErrorMetric.MaxError(img, recon);
    return (err, err <= threshold);
}

/// <summary>Mirrors Search1D.MeasureError so we can benchmark components.</summary>
static void BenchMeasureError(SoaImage img, int b, int e, int n, int axis, SoaImage recon)
{
    int len = e - b;
    int w = img.Width;
    int h = img.Height;

    float[][] srcChannels = [img.R, img.G, img.B, img.A];
    float[][] dstChannels = [recon.R, recon.G, recon.B, recon.A];

    for (int ch = 0; ch < 4; ch++)
    {
        float[] srcCh = srcChannels[ch];
        float[] dstCh = dstChannels[ch];

        if (axis == 1)
        {
            var region = new float[len * h];
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(srcCh, (y * w + b) * 4, region, y * len * 4, len * 4);

            float[] down = Resampler.Downsample1D(region, len, h, n, 1);
            float[] up = Resampler.Upsample1D(down, n, h, len, 1);

            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(srcCh, y * w * 4, dstCh, y * w * 4, b * 4);
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(up, y * len * 4, dstCh, (y * w + b) * 4, len * 4);
            int rightBytes = (w - e) * 4;
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(srcCh, (y * w + e) * 4, dstCh, (y * w + e) * 4, rightBytes);
        }
        else
        {
            var region = new float[w * len];
            for (int y = b; y < e; y++)
                Buffer.BlockCopy(srcCh, y * w * 4, region, (y - b) * w * 4, w * 4);

            float[] down = Resampler.Downsample1D(region, w, len, n, 0);
            float[] up = Resampler.Upsample1D(down, w, n, len, 0);

            for (int y = 0; y < h; y++)
            {
                int rowBytes = w * 4;
                if (y < b || y >= e)
                    Buffer.BlockCopy(srcCh, y * w * 4, dstCh, y * w * 4, rowBytes);
                else
                    Buffer.BlockCopy(up, (y - b) * w * 4, dstCh, y * w * 4, rowBytes);
            }
        }
    }
}

static (string name, byte[] rgba, int w, int h) Load(string path)
{
    using var image = Image.Load<Rgba32>(path);
    var rgba = new byte[image.Width * image.Height * 4];
    image.CopyPixelDataTo(rgba);
    return (Path.GetFileName(path), rgba, image.Width, image.Height);
}
