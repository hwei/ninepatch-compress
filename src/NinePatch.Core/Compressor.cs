namespace NinePatch.Core;

/// <summary>
/// 2D nine-patch compression assembly and reconstruction.
/// </summary>
public static class Compressor
{
    /// <summary>Cut 9 regions, downsample stretch zones, assemble compressed texture.</summary>
    public static (float[] compressed, NinePatchMeta meta) Compress2D(
        ReadOnlySpan<float> img, int width, int height,
        SearchResult1D resultX, SearchResult1D resultY)
    {
        int xb = resultX.Begin, xe = resultX.End, nx = resultX.N;
        int yb = resultY.Begin, ye = resultY.End, ny = resultY.N;

        int cwLeft = xb;
        int cwRight = width - xe;
        int cwMid = nx;
        int chTop = yb;
        int chBottom = height - ye;
        int chMid = ny;

        int h2 = chTop + chMid + chBottom;
        int w2 = cwLeft + cwMid + cwRight;
        var compressed = new float[w2 * h2 * 4];

        // Helper: copy a rect from src to dst
        void CopyRect(ReadOnlySpan<float> src, int srcW, Span<float> dst, int dstW,
            int sx, int sy, int dx, int dy, int w, int h)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int si = ((sy + y) * srcW + sx + x) * 4;
                int di = ((dy + y) * dstW + dx + x) * 4;
                dst[di] = src[si]; dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2]; dst[di + 3] = src[si + 3];
            }
        }

        // Top row (y=0..yb): copy left/right unchanged, X-downsample center
        if (chTop > 0)
        {
            // Extract center region and X-downsample
            var topCenter = new float[(xe - xb) * chTop * 4];
            CopyRect(img, width, topCenter, xe - xb, xb, 0, 0, 0, xe - xb, chTop);
            float[] topMid = Resampler.Downsample1D(topCenter, xe - xb, chTop, nx, 1);

            int dy = 0;
            if (cwLeft > 0) { CopyRect(img, width, compressed, w2, 0, 0, 0, dy, cwLeft, chTop); }
            CopyRect(topMid, nx, compressed, w2, 0, 0, cwLeft, dy, cwMid, chTop);
            if (cwRight > 0)
                CopyRect(img, width, compressed, w2, xe, 0, cwLeft + cwMid, dy, cwRight, chTop);
        }

        // Middle row (yb..ye): Y-downsample all, X-downsample center only
        {
            int stretchH = ye - yb;
            var midSrc = new float[width * stretchH * 4];
            CopyRect(img, width, midSrc, width, 0, yb, 0, 0, width, stretchH);

            // Downsample Y first
            float[] midY = Resampler.Downsample1D(midSrc, width, stretchH, ny, 0);
            // midY layout: (row, col, ch) with row < ny, col < width, stored as (col * ny + row) * 4

            // Left: Y-downsample only, X unchanged
            var midLeft = cwLeft > 0 ? ExtractRect(midY, width, ny, 0, 0, cwLeft, ny) : null;
            // Center: Y + X downsample
            var midCenter = Resampler.Downsample1D(
                ExtractRect(midY, width, ny, xb, 0, xe - xb, ny), xe - xb, ny, nx, 1);
            // Right: Y-downsample only, X unchanged
            var midRight = cwRight > 0 ? ExtractRect(midY, width, ny, xe, 0, cwRight, ny) : null;

            int cx = 0;
            if (midLeft != null) { CopyRect(midLeft, cwLeft, compressed, w2, 0, 0, cx, chTop, cwLeft, chMid); cx += cwLeft; }
            CopyRect(midCenter, nx, compressed, w2, 0, 0, cx, chTop, nx, chMid); cx += nx;
            if (midRight != null) { CopyRect(midRight, cwRight, compressed, w2, 0, 0, cx, chTop, cwRight, chMid); }
        }

        // Bottom row (ye..height): copy left/right from compressed, upsample center X
        if (chBottom > 0)
        {
            var botCenter = new float[(xe - xb) * chBottom * 4];
            CopyRect(img, width, botCenter, xe - xb, xb, ye, 0, 0, xe - xb, chBottom);
            float[] botMid = Resampler.Downsample1D(botCenter, xe - xb, chBottom, nx, 1);

            int srcYBot = chTop + chMid; // compressed bottom row Y
            int dxRight = cwLeft + cwMid; // compressed bottom-right X

            // Bottom-left: unchanged from original, placed at compressed bottom-left
            if (cwLeft > 0) { CopyRect(img, width, compressed, w2, 0, ye, 0, srcYBot, cwLeft, chBottom); }
            // Bottom-center: X-downsampled
            CopyRect(botMid, nx, compressed, w2, 0, 0, cwLeft, srcYBot, cwMid, chBottom);
            // Bottom-right: unchanged from original, placed at compressed bottom-right
            if (cwRight > 0)
                CopyRect(img, width, compressed, w2, xe, ye, dxRight, srcYBot, cwRight, chBottom);
        }

        double savingsPct = (1.0 - (double)(w2 * h2) / (width * height)) * 100.0;

        var meta = new NinePatchMeta(
            Xb: xb, Xe: xe, Yb: yb, Ye: ye,
            OriginalW: width, OriginalH: height,
            CompressedW: w2, CompressedH: h2,
            Nx: nx, Ny: ny,
            SavingsPct: savingsPct);

        return (compressed, meta);
    }

    /// <summary>Upsample stretch regions back to original size.</summary>
    public static float[] ReconstructStretched(
        ReadOnlySpan<float> compressed, int compW, int compH,
        NinePatchMeta meta)
    {
        int xb = meta.Xb, xe = meta.Xe, yb = meta.Yb, ye = meta.Ye;
        int nx = meta.Nx, ny = meta.Ny;
        int w = meta.OriginalW, h = meta.OriginalH;
        int origStretchW = xe - xb;
        int origStretchH = ye - yb;

        int cwLeft = xb;
        int cwRight = w - xe;
        int cwMid = nx;
        int chTop = yb;
        int chBottom = h - ye;
        int chMid = ny;

        var result = new float[w * h * 4];

        void CopyRect(Span<float> dst, int dstW, ReadOnlySpan<float> src, int srcW,
            int dx, int dy, int sx, int sy, int w, int h)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int di = ((dy + y) * dstW + dx + x) * 4;
                int si = ((sy + y) * srcW + sx + x) * 4;
                dst[di] = src[si]; dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2]; dst[di + 3] = src[si + 3];
            }
        }

        // Top strip: extract, expand X, write to result
        if (chTop > 0)
        {
            var topStrip = ExtractRect(compressed, compW, compH, 0, 0, compW, chTop);
            if (cwLeft > 0)
                CopyRect(result, w, topStrip, compW, 0, 0, 0, 0, cwLeft, chTop);
            var topCenterSrc = ExtractRect(topStrip, compW, chTop, cwLeft, 0, cwMid, chTop);
            var topCenterUp = Resampler.Upsample1D(topCenterSrc, cwMid, chTop, origStretchW, 1);
            CopyRect(result, w, topCenterUp, origStretchW, cwLeft, 0, 0, 0, origStretchW, chTop);
            if (cwRight > 0)
                CopyRect(result, w, topStrip, compW, cwLeft + origStretchW, 0, cwLeft + cwMid, 0, cwRight, chTop);
        }

        // Middle strip: first upsample Y, then expand X
        {
            var midComp = ExtractRect(compressed, compW, compH, 0, chTop, compW, chMid);
            var midYUp = Resampler.Upsample1D(midComp, compW, chMid, origStretchH, 0);

            // Left: unchanged
            if (cwLeft > 0)
                CopyRect(result, w, midYUp, compW, 0, yb, 0, 0, cwLeft, origStretchH);

            // Center: upsample X
            var centerSrc = ExtractRect(midYUp, compW, origStretchH, cwLeft, 0, cwMid, origStretchH);
            var centerUp = Resampler.Upsample1D(centerSrc, cwMid, origStretchH, origStretchW, 1);
            CopyRect(result, w, centerUp, origStretchW, cwLeft, yb, 0, 0, origStretchW, origStretchH);

            // Right: unchanged
            if (cwRight > 0)
                CopyRect(result, w, midYUp, compW, cwLeft + origStretchW, yb, cwLeft + cwMid, 0, cwRight, origStretchH);
        }

        // Bottom strip: extract, expand X, write to result
        if (chBottom > 0)
        {
            int botSrcRow = chTop + chMid;
            var botStrip = ExtractRect(compressed, compW, compH, 0, botSrcRow, compW, chBottom);
            int dstY = yb + origStretchH;
            if (cwLeft > 0)
                CopyRect(result, w, botStrip, compW, 0, dstY, 0, 0, cwLeft, chBottom);
            var botCenterSrc = ExtractRect(botStrip, compW, chBottom, cwLeft, 0, cwMid, chBottom);
            var botCenterUp = Resampler.Upsample1D(botCenterSrc, cwMid, chBottom, origStretchW, 1);
            CopyRect(result, w, botCenterUp, origStretchW, cwLeft, dstY, 0, 0, origStretchW, chBottom);
            if (cwRight > 0)
                CopyRect(result, w, botStrip, compW, cwLeft + origStretchW, dstY, cwLeft + cwMid, 0, cwRight, chBottom);
        }

        return result;
    }

    /// <summary>Quick error check: downsample to 2 pixels, upsample back, measure.</summary>
    private static float BoundaryError(ReadOnlySpan<float> img, int width, int height, int b, int e, int axis)
    {
        int len = e - b;
        float[] region;
        int rw, rh;

        if (axis == 1)
        {
            region = new float[len * height * 4];
            rw = len; rh = height;
            for (int y = 0; y < height; y++)
            for (int x = b; x < e; x++)
            {
                int si = (y * width + x) * 4;
                int di = (y * len + (x - b)) * 4;
                region[di] = img[si]; region[di + 1] = img[si + 1];
                region[di + 2] = img[si + 2]; region[di + 3] = img[si + 3];
            }
        }
        else
        {
            region = new float[width * len * 4];
            rw = width; rh = len;
            for (int y = b; y < e; y++)
            for (int x = 0; x < width; x++)
            {
                int si = (y * width + x) * 4;
                int di = ((y - b) * width + x) * 4;
                region[di] = img[si]; region[di + 1] = img[si + 1];
                region[di + 2] = img[si + 2]; region[di + 3] = img[si + 3];
            }
        }

        float[] down = Resampler.Downsample1D(region, rw, rh, 2, axis);
        int srcW_up = axis == 1 ? rh : rw;
        float[] up = Resampler.Upsample1D(down, 2, srcW_up, len, axis);
        if (up.Length != region.Length)
            return 999f;
        return ErrorMetric.MaxError(region, up);
    }

    /// <summary>Run full pipeline with margin auto-retry.</summary>
    public static CompressResult RunFullPipeline(
        ReadOnlySpan<byte> imgU8, int width, int height,
        double threshold, int margin = 0, double minSavings = 30.0)
    {
        if (imgU8.Length != width * height * 4)
            return CompressResult.Fail(CompressStatus.InvalidInput,
                $"Buffer size {imgU8.Length} != {width}*{height}*4");

        if (width > 1024 || height > 1024)
            return CompressResult.Fail(CompressStatus.InvalidInput,
                $"Image {width}x{height} exceeds 1024x1024 limit");

        float[] imgLinear = ColorSpace.RgbaU8ToLinear(imgU8);

        SearchResult1D? resX = Search1D.SearchX(imgLinear, width, height, (float)threshold, margin);
        SearchResult1D? resY = Search1D.SearchY(imgLinear, width, height, (float)threshold, margin);

        // Auto-retry with increasing margin
        int maxMargin = Math.Min(width, height) / 4;
        int curMargin = margin;
        int marginStep = 4;
        if ((resX is null || resY is null) && margin == 0 && marginStep > 0)
        {
            while (curMargin + marginStep <= maxMargin)
            {
                curMargin += marginStep;
                resX = Search1D.SearchX(imgLinear, width, height, (float)threshold, curMargin);
                resY = Search1D.SearchY(imgLinear, width, height, (float)threshold, curMargin);
                if (resX is not null && resY is not null) break;
            }
        }

        if (resX is null || resY is null)
            return CompressResult.Fail(CompressStatus.NoValidSplit, "No valid nine-patch split found");

        // Check savings
        int w2 = resX.Value.N + resX.Value.Begin + (width - resX.Value.End);
        int h2 = resY.Value.N + resY.Value.Begin + (height - resY.Value.End);
        double savingsPct = (1.0 - (double)(w2 * h2) / (width * height)) * 100.0;

        if (savingsPct < minSavings)
            return CompressResult.Fail(CompressStatus.SavingsTooLow,
                $"Savings {savingsPct:F1}% below minimum {minSavings}%");

        // Compress
        var (compressed, meta) = Compress2D(imgLinear, width, height, resX.Value, resY.Value);

        // Boundary errors
        meta = meta with
        {
            ErrorX = BoundaryError(imgLinear, width, height, resX.Value.Begin, resX.Value.End, 1),
            ErrorY = BoundaryError(imgLinear, width, height, resY.Value.Begin, resY.Value.End, 0)
        };

        // Reconstruct and measure 2D error
        float[] reconstructed = ReconstructStretched(compressed, w2, h2, meta);
        float err2d = ErrorMetric.MaxError(imgLinear, reconstructed);
        meta = meta with { Error2d = err2d };

        byte[] compressedU8 = ColorSpace.RgbaLinearToU8(compressed);
        return CompressResult.Ok(compressedU8, meta);
    }

    private static float[] ExtractRect(ReadOnlySpan<float> src, int srcW, int srcH, int sx, int sy, int w, int h)
    {
        var dst = new float[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int si = ((sy + y) * srcW + sx + x) * 4;
            int di = (y * w + x) * 4;
            dst[di] = src[si]; dst[di + 1] = src[si + 1];
            dst[di + 2] = src[si + 2]; dst[di + 3] = src[si + 3];
        }
        return dst;
    }
}
