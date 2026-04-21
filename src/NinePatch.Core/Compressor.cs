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

            // Extract the stretch region as a 2D array (ny rows × width cols, row-major)
            var midSrc2D = new float[ny * width * 4];
            for (int row = 0; row < ny; row++)
            for (int col = 0; col < width; col++)
            {
                // For Y-downsample: each output row is a weighted average of input rows
                // Using box filter: row r averages input rows [r*stretchH/ny .. (r+1)*stretchH/ny)
                float scale = (float)stretchH / ny;
                float lo = row * scale;
                float hi = (row + 1) * scale;
                int i0 = (int)MathF.Floor(lo);
                int i1 = Math.Min((int)MathF.Ceiling(hi), stretchH);
                float count = 0;
                float rSum = 0, gSum = 0, bSum = 0, aSum = 0;
                for (int sy = i0; sy < i1; sy++)
                {
                    float overlap = Math.Min(sy + 1, hi) - Math.Max(sy, lo);
                    int si = (sy * width + col) * 4;
                    rSum += img[((yb + sy) * width + col) * 4] * overlap;
                    gSum += img[((yb + sy) * width + col) * 4 + 1] * overlap;
                    bSum += img[((yb + sy) * width + col) * 4 + 2] * overlap;
                    aSum += img[((yb + sy) * width + col) * 4 + 3] * overlap;
                    count += overlap;
                }
                int di = (row * width + col) * 4;
                float inv = count > 0 ? 1f / count : 0;
                midSrc2D[di] = rSum * inv;
                midSrc2D[di + 1] = gSum * inv;
                midSrc2D[di + 2] = bSum * inv;
                midSrc2D[di + 3] = aSum * inv;
            }

            // Now X-downsample the center region of midSrc2D
            var midCenter2D = new float[ny * nx * 4];
            for (int row = 0; row < ny; row++)
            {
                float scale = (float)(xe - xb) / nx;
                for (int dx = 0; dx < nx; dx++)
                {
                    float lo = dx * scale;
                    float hi = (dx + 1) * scale;
                    int i0 = Math.Max((int)MathF.Floor(lo), 0);
                    int i1 = Math.Min((int)MathF.Ceiling(hi), xe - xb);
                    float count = 0;
                    float rSum = 0, gSum = 0, bSum = 0, aSum = 0;
                    for (int sx = i0; sx < i1; sx++)
                    {
                        float overlap = Math.Min(sx + 1, hi) - Math.Max(sx, lo);
                        int si = (row * width + xb + sx) * 4;
                        rSum += midSrc2D[si] * overlap;
                        gSum += midSrc2D[si + 1] * overlap;
                        bSum += midSrc2D[si + 2] * overlap;
                        aSum += midSrc2D[si + 3] * overlap;
                        count += overlap;
                    }
                    int di = (row * nx + dx) * 4;
                    float inv = count > 0 ? 1f / count : 0;
                    midCenter2D[di] = rSum * inv;
                    midCenter2D[di + 1] = gSum * inv;
                    midCenter2D[di + 2] = bSum * inv;
                    midCenter2D[di + 3] = aSum * inv;
                }
            }

            // Assemble middle row into compressed image
            int dy = chTop;
            for (int row = 0; row < ny; row++)
            {
                int dstRowStart = (dy + row) * w2 * 4;
                // Left columns
                for (int col = 0; col < cwLeft; col++)
                {
                    int si = (row * width + col) * 4;
                    int di = dstRowStart + col * 4;
                    compressed[di] = midSrc2D[si]; compressed[di + 1] = midSrc2D[si + 1];
                    compressed[di + 2] = midSrc2D[si + 2]; compressed[di + 3] = midSrc2D[si + 3];
                }
                // Center columns (X-downsampled)
                for (int col = 0; col < nx; col++)
                {
                    int si = (row * nx + col) * 4;
                    int di = dstRowStart + (cwLeft + col) * 4;
                    compressed[di] = midCenter2D[si]; compressed[di + 1] = midCenter2D[si + 1];
                    compressed[di + 2] = midCenter2D[si + 2]; compressed[di + 3] = midCenter2D[si + 3];
                }
                // Right columns
                for (int col = 0; col < cwRight; col++)
                {
                    int si = (row * width + xe + col) * 4;
                    int di = dstRowStart + (cwLeft + nx + col) * 4;
                    compressed[di] = midSrc2D[si]; compressed[di + 1] = midSrc2D[si + 1];
                    compressed[di + 2] = midSrc2D[si + 2]; compressed[di + 3] = midSrc2D[si + 3];
                }
            }
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
            // Extract middle row from compressed as 2D (ny rows × compW cols, row-major)
            var mid2D = new float[ny * compW * 4];
            for (int row = 0; row < ny; row++)
            for (int col = 0; col < compW; col++)
            {
                int si = ((chTop + row) * compW + col) * 4;
                int di = (row * compW + col) * 4;
                mid2D[di] = compressed[si]; mid2D[di + 1] = compressed[si + 1];
                mid2D[di + 2] = compressed[si + 2]; mid2D[di + 3] = compressed[si + 3];
            }

            // Upsample Y from ny to origStretchH
            var midYUp = new float[origStretchH * compW * 4];
            for (int dy2 = 0; dy2 < origStretchH; dy2++)
            {
                float u = (dy2 + 0.5f) * ny / origStretchH - 0.5f;
                int i0 = (int)MathF.Floor(u);
                int i1 = i0 + 1;
                if (i0 < 0) i0 = 0;
                if (i0 >= ny) i0 = ny - 1;
                if (i1 >= ny) i1 = ny - 1;
                float t = u - MathF.Floor(u);
                float t0 = 1f - t, t1 = t;
                for (int col = 0; col < compW; col++)
                {
                    int si0 = (i0 * compW + col) * 4;
                    int si1 = (i1 * compW + col) * 4;
                    int di = (dy2 * compW + col) * 4;
                    midYUp[di]     = mid2D[si0]     * t0 + mid2D[si1]     * t1;
                    midYUp[di + 1] = mid2D[si0 + 1] * t0 + mid2D[si1 + 1] * t1;
                    midYUp[di + 2] = mid2D[si0 + 2] * t0 + mid2D[si1 + 2] * t1;
                    midYUp[di + 3] = mid2D[si0 + 3] * t0 + mid2D[si1 + 3] * t1;
                }
            }

            // Upsample X from nx to origStretchW in the center region
            var centerUp = new float[origStretchH * origStretchW * 4];
            for (int dy2 = 0; dy2 < origStretchH; dy2++)
            {
                for (int dx2 = 0; dx2 < origStretchW; dx2++)
                {
                    float u = (dx2 + 0.5f) * nx / origStretchW - 0.5f;
                    int i0 = (int)MathF.Floor(u);
                    int i1 = i0 + 1;
                    if (i0 < 0) i0 = 0;
                    if (i0 >= nx) i0 = nx - 1;
                    if (i1 >= nx) i1 = nx - 1;
                    float t = u - MathF.Floor(u);
                    float t0 = 1f - t, t1 = t;
                    int si0 = (dy2 * compW + cwLeft + i0) * 4;
                    int si1 = (dy2 * compW + cwLeft + i1) * 4;
                    int di = (dy2 * origStretchW + dx2) * 4;
                    centerUp[di]     = midYUp[si0]     * t0 + midYUp[si1]     * t1;
                    centerUp[di + 1] = midYUp[si0 + 1] * t0 + midYUp[si1 + 1] * t1;
                    centerUp[di + 2] = midYUp[si0 + 2] * t0 + midYUp[si1 + 2] * t1;
                    centerUp[di + 3] = midYUp[si0 + 3] * t0 + midYUp[si1 + 3] * t1;
                }
            }

            // Write to result
            for (int dy2 = 0; dy2 < origStretchH; dy2++)
            {
                int dstRowStart = ((yb + dy2) * w) * 4;
                // Left
                for (int col = 0; col < cwLeft; col++)
                {
                    int si = (dy2 * compW + col) * 4;
                    int di = dstRowStart + col * 4;
                    result[di] = midYUp[si]; result[di + 1] = midYUp[si + 1];
                    result[di + 2] = midYUp[si + 2]; result[di + 3] = midYUp[si + 3];
                }
                // Center (X-upsampled)
                for (int col = 0; col < origStretchW; col++)
                {
                    int si = (dy2 * origStretchW + col) * 4;
                    int di = dstRowStart + (cwLeft + col) * 4;
                    result[di] = centerUp[si]; result[di + 1] = centerUp[si + 1];
                    result[di + 2] = centerUp[si + 2]; result[di + 3] = centerUp[si + 3];
                }
                // Right
                for (int col = 0; col < cwRight; col++)
                {
                    int si = (dy2 * compW + cwLeft + nx + col) * 4;
                    int di = dstRowStart + (cwLeft + origStretchW + col) * 4;
                    result[di] = midYUp[si]; result[di + 1] = midYUp[si + 1];
                    result[di + 2] = midYUp[si + 2]; result[di + 3] = midYUp[si + 3];
                }
            }
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
        // Downsampled dimensions: axis=0 → (rw cols × 2 rows), axis=1 → (2 cols × rh rows)
        int downW = axis == 1 ? 2 : rw;
        int downH = axis == 1 ? rh : 2;
        float[] up = Resampler.Upsample1D(down, downW, downH, len, axis);
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
