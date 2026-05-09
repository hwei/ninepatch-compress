namespace NinePatch.Core;

/// <summary>
/// Main API entry point for nine-patch compression.
/// </summary>
public static class NinePatchCompressor
{
    public static CompressResult Compress(
        ReadOnlySpan<byte> rgba,    // sRGB RGBA, W*H*4 bytes
        int width,
        int height,
        double threshold = 4.0,     // [0, 255] scale
        int margin = 0,
        int minLength = 8)
    {
        return Compressor.RunFullPipeline(rgba, width, height, threshold, margin, minLength);
    }

    /// <summary>
    /// On-demand analysis that reports per-row X and per-column Y candidate
    /// intervals plus final axis results. Uses the same validation, search,
    /// auto-retry, and identity fallback semantics as <see cref="Compress"/>.
    /// Does NOT produce a compressed output image.
    /// </summary>
    public static DebugAnalyzeResult Analyze(
        ReadOnlySpan<byte> rgba,
        int width,
        int height,
        double threshold = 4.0,
        int margin = 0,
        int minLength = 8)
    {
        if (rgba.Length != width * height * 4)
            return DebugAnalyzeResult.Fail(CompressStatus.InvalidInput,
                $"Buffer size {rgba.Length} != {width}*{height}*4");

        if (width > 1024 || height > 1024)
            return DebugAnalyzeResult.Fail(CompressStatus.InvalidInput,
                $"Image {width}x{height} exceeds 1024x1024 limit");

        // Decode → Linear → Premul
        var imgLinear = ColorSpace.DecodeSrgbRgba8ToLinear(rgba, width, height);
        var imgPremul = ColorSpace.Premultiply(imgLinear);

        float thr = (float)threshold;
        int marginL = margin;
        int marginRX = imgPremul.Width - margin;
        int marginRY = imgPremul.Height - margin;

        // Collect per-row X candidates and per-column Y candidates
        var xCandidates = Segmenter.CollectRowCandidatesX(imgPremul, 2, thr, minLength, marginL, marginRX);
        var yCandidates = Segmenter.CollectColumnCandidatesY(imgPremul, 2, thr, minLength, marginL, marginRY);

        // Run normal search
        SearchResult1D? resX = Segmenter.SearchX(imgPremul, thr, minLength, margin);
        SearchResult1D? resY = Segmenter.SearchY(imgPremul, thr, minLength, margin);

        // Auto-retry with increasing margin (same semantics as Compress)
        int maxMargin = Math.Min(width, height) / 4;
        int curMargin = margin;
        const int marginStep = 4;
        if ((resX is null || resY is null) && margin == 0 && marginStep > 0)
        {
            while (curMargin + marginStep <= maxMargin)
            {
                curMargin += marginStep;
                if (resX is null)
                    resX = Segmenter.SearchX(imgPremul, thr, minLength, curMargin);
                if (resY is null)
                    resY = Segmenter.SearchY(imgPremul, thr, minLength, curMargin);
                if (resX is not null && resY is not null) break;
            }
        }

        // Identity fallback
        DebugAxisResult finalX = resX is { } rx
            ? new DebugAxisResult(rx.Begin, rx.End, rx.N, false)
            : new DebugAxisResult(0, width, width, true);

        DebugAxisResult finalY = resY is { } ry
            ? new DebugAxisResult(ry.Begin, ry.End, ry.N, false)
            : new DebugAxisResult(0, height, height, true);

        return DebugAnalyzeResult.Ok(finalX, finalY, xCandidates, yCandidates);
    }
}
