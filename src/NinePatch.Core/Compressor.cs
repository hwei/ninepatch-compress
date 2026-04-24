using System.Numerics;

namespace NinePatch.Core;

/// <summary>
/// 2D nine-patch compression assembly and reconstruction.
/// All operations work on SoaImagePremul (premultiplied linear) planes.
/// </summary>
public static class Compressor
{
    private static void CopyRect(SoaImagePremul src, int srcW, SoaImagePremul dst, int dstW,
        int sx, int sy, int dx, int dy, int w, int h)
    {
        for (int ch = 0; ch < 4; ch++)
        {
            var s = GetChannel(src, ch);
            var d = GetChannel(dst, ch);
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(s, ((sy + y) * srcW + sx) * 4, d, ((dy + y) * dstW + dx) * 4, w * 4);
        }
    }

    private static void CopyChannelRect(float[] src, int srcW, float[] dst, int dstW,
        int sx, int sy, int dx, int dy, int w, int h)
    {
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(src, ((sy + y) * srcW + sx) * 4, dst, ((dy + y) * dstW + dx) * 4, w * 4);
    }

    private static float[] ExtractChannel(float[] src, int srcW, int sx, int sy, int w, int h)
    {
        var dst = new float[w * h];
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(src, ((sy + y) * srcW + sx) * 4, dst, y * w * 4, w * 4);
        return dst;
    }

    private static float[] GetChannel(SoaImagePremul img, int ch) => ch switch
    {
        0 => img.R, 1 => img.G, 2 => img.B, 3 => img.A, _ => throw new System.ArgumentOutOfRangeException()
    };

    private static void SetChannel(ref SoaImagePremul img, int ch, float[] data)
    {
        switch (ch) { case 0: img = img with { R = data }; break; case 1: img = img with { G = data }; break; case 2: img = img with { B = data }; break; case 3: img = img with { A = data }; break; }
    }

    /// <summary>Cut 9 regions, downsample stretch zones, assemble compressed texture.</summary>
    public static (SoaImagePremul compressed, NinePatchMeta meta) Compress2D(
        SoaImagePremul img, SearchResult1D resultX, SearchResult1D resultY)
    {
        int xb = resultX.Begin, xe = resultX.End, nx = resultX.N;
        int yb = resultY.Begin, ye = resultY.End, ny = resultY.N;

        int cwLeft = xb;
        int cwRight = img.Width - xe;
        int cwMid = nx;
        int chTop = yb;
        int chBottom = img.Height - ye;
        int chMid = ny;

        int h2 = chTop + chMid + chBottom;
        int w2 = cwLeft + cwMid + cwRight;

        var compressed = SoaImagePremul.Create(w2, h2);

        // Top row (y=0..yb): copy left/right unchanged, X-downsample center
        if (chTop > 0)
        {
            for (int ch = 0; ch < 4; ch++)
            {
                var topCenter = ExtractChannel(GetChannel(img, ch), img.Width, xb, 0, xe - xb, chTop);
                float[] topMid = Resampler.Downsample1D(topCenter, xe - xb, chTop, nx, 1);
                for (int y = 0; y < chTop; y++)
                    Buffer.BlockCopy(topMid, y * nx * 4, GetChannel(compressed, ch), (y * w2 + cwLeft) * 4, nx * 4);
            }
            CopyRect(img, img.Width, compressed, w2, 0, 0, 0, 0, cwLeft, chTop);
            CopyRect(img, img.Width, compressed, w2, xe, 0, cwLeft + cwMid, 0, cwRight, chTop);
        }

        // Middle row (yb..ye): Y-downsample left/right, Y+X-downsample center
        {
            int stretchH = ye - yb;

            // Y-downsample the entire stretch region to ny rows
            float[][] midSrc2D = new float[4][];
            for (int ch = 0; ch < 4; ch++)
            {
                var region = new float[img.Width * stretchH];
                for (int y = 0; y < stretchH; y++)
                    Buffer.BlockCopy(GetChannel(img, ch), ((yb + y) * img.Width) * 4, region, y * img.Width * 4, img.Width * 4);

                midSrc2D[ch] = Resampler.Downsample1D(region, img.Width, stretchH, ny, 0);
            }

            // X-downsample the center region of midSrc2D
            float[][] midCenter2D = new float[4][];
            for (int ch = 0; ch < 4; ch++)
            {
                var centerRegion = new float[ny * (xe - xb)];
                for (int y = 0; y < ny; y++)
                    Buffer.BlockCopy(midSrc2D[ch], (y * img.Width + xb) * 4, centerRegion, y * (xe - xb) * 4, (xe - xb) * 4);

                midCenter2D[ch] = Resampler.Downsample1D(centerRegion, xe - xb, ny, nx, 1);
            }

            // Assemble middle row
            int dstRow = chTop;
            for (int ch = 0; ch < 4; ch++)
            for (int row = 0; row < ny; row++)
            {
                var dstCh = GetChannel(compressed, ch);
                // Left: from midSrc2D
                Buffer.BlockCopy(midSrc2D[ch], (row * img.Width) * 4, dstCh, ((dstRow + row) * w2) * 4, cwLeft * 4);
                // Center: from midCenter2D
                Buffer.BlockCopy(midCenter2D[ch], (row * nx) * 4, dstCh, ((dstRow + row) * w2 + cwLeft) * 4, nx * 4);
                // Right: from midSrc2D
                Buffer.BlockCopy(midSrc2D[ch], (row * img.Width + xe) * 4, dstCh, ((dstRow + row) * w2 + cwLeft + nx) * 4, cwRight * 4);
            }
        }

        // Bottom row (ye..height): copy left/right, X-downsample center
        if (chBottom > 0)
        {
            for (int ch = 0; ch < 4; ch++)
            {
                var botCenter = ExtractChannel(GetChannel(img, ch), img.Width, xb, ye, xe - xb, chBottom);
                float[] botMid = Resampler.Downsample1D(botCenter, xe - xb, chBottom, nx, 1);
                for (int y = 0; y < chBottom; y++)
                    Buffer.BlockCopy(botMid, y * nx * 4, GetChannel(compressed, ch), ((chTop + chMid + y) * w2 + cwLeft) * 4, nx * 4);
            }
            int dstYBot = chTop + chMid;
            CopyRect(img, img.Width, compressed, w2, 0, ye, 0, dstYBot, cwLeft, chBottom);
            CopyRect(img, img.Width, compressed, w2, xe, ye, cwLeft + cwMid, dstYBot, cwRight, chBottom);
        }

        double savingsPct = (1.0 - (double)(w2 * h2) / (img.Width * img.Height)) * 100.0;

        var meta = new NinePatchMeta(
            Xb: xb, Xe: xe, Yb: yb, Ye: ye,
            OriginalW: img.Width, OriginalH: img.Height,
            CompressedW: w2, CompressedH: h2,
            Nx: nx, Ny: ny,
            SavingsPct: savingsPct);

        return (compressed, meta);
    }

    /// <summary>Upsample stretch regions back to original size.</summary>
    public static SoaImagePremul ReconstructStretched(
        SoaImagePremul compressed, NinePatchMeta meta)
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

        var result = SoaImagePremul.Create(w, h);

        // Top strip: extract, expand X, write to result
        if (chTop > 0)
        {
            for (int ch = 0; ch < 4; ch++)
            {
                var srcCh = GetChannel(compressed, ch);
                var dstCh = GetChannel(result, ch);
                // Left
                for (int y = 0; y < chTop; y++)
                    Buffer.BlockCopy(srcCh, y * compressed.Width * 4, dstCh, y * w * 4, cwLeft * 4);
                // Center: upsample X
                var topCenter = new float[chTop * cwMid];
                for (int y = 0; y < chTop; y++)
                    Buffer.BlockCopy(srcCh, (y * compressed.Width + cwLeft) * 4, topCenter, y * cwMid * 4, cwMid * 4);
                var topUp = Resampler.Upsample1D(topCenter, cwMid, chTop, origStretchW, 1);
                for (int y = 0; y < chTop; y++)
                    Buffer.BlockCopy(topUp, y * origStretchW * 4, dstCh, (y * w + cwLeft) * 4, origStretchW * 4);
                // Right
                for (int y = 0; y < chTop; y++)
                    Buffer.BlockCopy(srcCh, (y * compressed.Width + cwLeft + cwMid) * 4, dstCh, (y * w + cwLeft + origStretchW) * 4, cwRight * 4);
            }
        }

        // Middle strip: upsample Y, then expand X
        {
            // Upsample Y from ny to origStretchH (per channel)
            float[][] midYUp = new float[4][];
            for (int ch = 0; ch < 4; ch++)
            {
                var upY = new float[origStretchH * compressed.Width];
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
                    for (int col = 0; col < compressed.Width; col++)
                        upY[dy2 * compressed.Width + col] = GetChannel(compressed, ch)[(chTop + i0) * compressed.Width + col] * t0 + GetChannel(compressed, ch)[(chTop + i1) * compressed.Width + col] * t1;
                }
                midYUp[ch] = upY;
            }

            // Upsample X from nx to origStretchW in the center region
            float[][] centerUp = new float[4][];
            for (int ch = 0; ch < 4; ch++)
            {
                var upX = new float[origStretchH * origStretchW];
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
                        upX[dy2 * origStretchW + dx2] = midYUp[ch][dy2 * compressed.Width + cwLeft + i0] * t0 + midYUp[ch][dy2 * compressed.Width + cwLeft + i1] * t1;
                    }
                }
                centerUp[ch] = upX;
            }

            // Write to result
            for (int ch = 0; ch < 4; ch++)
            for (int dy2 = 0; dy2 < origStretchH; dy2++)
            {
                var dstCh = GetChannel(result, ch);
                // Left
                Buffer.BlockCopy(midYUp[ch], dy2 * compressed.Width * 4, dstCh, ((yb + dy2) * w) * 4, cwLeft * 4);
                // Center (X-upsampled)
                Buffer.BlockCopy(centerUp[ch], dy2 * origStretchW * 4, dstCh, ((yb + dy2) * w + cwLeft) * 4, origStretchW * 4);
                // Right
                Buffer.BlockCopy(midYUp[ch], (dy2 * compressed.Width + cwLeft + nx) * 4, dstCh, ((yb + dy2) * w + cwLeft + origStretchW) * 4, cwRight * 4);
            }
        }

        // Bottom strip: extract, expand X, write to result
        if (chBottom > 0)
        {
            int botSrcRow = chTop + chMid;
            int dstY = yb + origStretchH;
            for (int ch = 0; ch < 4; ch++)
            {
                var srcCh = GetChannel(compressed, ch);
                var dstCh = GetChannel(result, ch);
                // Left
                for (int y = 0; y < chBottom; y++)
                    Buffer.BlockCopy(srcCh, ((botSrcRow + y) * compressed.Width) * 4, dstCh, ((dstY + y) * w) * 4, cwLeft * 4);
                // Center: upsample X
                var botCenter = new float[chBottom * cwMid];
                for (int y = 0; y < chBottom; y++)
                    Buffer.BlockCopy(srcCh, ((botSrcRow + y) * compressed.Width + cwLeft) * 4, botCenter, y * cwMid * 4, cwMid * 4);
                var botUp = Resampler.Upsample1D(botCenter, cwMid, chBottom, origStretchW, 1);
                for (int y = 0; y < chBottom; y++)
                    Buffer.BlockCopy(botUp, y * origStretchW * 4, dstCh, ((dstY + y) * w + cwLeft) * 4, origStretchW * 4);
                // Right
                for (int y = 0; y < chBottom; y++)
                    Buffer.BlockCopy(srcCh, ((botSrcRow + y) * compressed.Width + cwLeft + cwMid) * 4, dstCh, ((dstY + y) * w + cwLeft + origStretchW) * 4, cwRight * 4);
            }
        }

        return result;
    }

    /// <summary>
    /// Quick X-axis error check: extract [b,e) columns × full height, downsample X to 2,
    /// upsample back, measure in premul-sRGB space.
    /// </summary>
    private static float BoundaryErrorX(SoaImagePremul img, int b, int e, SoaImagePremulSrgb origSrgb)
    {
        int len = e - b;
        int h = img.Height;

        float[][] region = new float[4][];
        for (int ch = 0; ch < 4; ch++)
        {
            region[ch] = new float[len * h];
            var chData = GetChannel(img, ch);
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(chData, (y * img.Width + b) * 4, region[ch], y * len * 4, len * 4);
        }

        var regionSoa = new SoaImagePremul(region[0], region[1], region[2], region[3])
        {
            Width = len,
            Height = h,
        };

        var upSoa = SoaImagePremul.Create(len, h);
        for (int ch = 0; ch < 4; ch++)
        {
            var down = Resampler.Downsample1D(region[ch], len, h, 2, axis: 1);
            var up = Resampler.Upsample1D(down, 2, h, len, axis: 1);
            SetChannel(ref upSoa, ch, up);
        }

        // Extract the same region from origSrgb for comparison
        var origRegion = SoaImagePremulSrgb.Create(len, h);
        for (int ch = 0; ch < 4; ch++)
        {
            var src = ch switch { 0 => origSrgb.R, 1 => origSrgb.G, 2 => origSrgb.B, 3 => origSrgb.A, _ => null! };
            var dst = ch switch { 0 => origRegion.R, 1 => origRegion.G, 2 => origRegion.B, 3 => origRegion.A };
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(src, (y * origSrgb.Width + b) * 4, dst, y * len * 4, len * 4);
        }

        return ErrorMetric.MaxError(origRegion, upSoa);
    }

    /// <summary>Run full pipeline with margin auto-retry.</summary>
    public static CompressResult RunFullPipeline(
        ReadOnlySpan<byte> imgU8, int width, int height,
        double threshold, int margin = 0, int minLength = 8)
    {
        if (imgU8.Length != width * height * 4)
            return CompressResult.Fail(CompressStatus.InvalidInput,
                $"Buffer size {imgU8.Length} != {width}*{height}*4");

        if (width > 1024 || height > 1024)
            return CompressResult.Fail(CompressStatus.InvalidInput,
                $"Image {width}x{height} exceeds 1024x1024 limit");

        // Decode → Linear → Premul → PremulSrgb
        SoaImageLinear imgLinear = ColorSpace.DecodeSrgbRgba8ToLinear(imgU8, width, height);
        SoaImagePremul imgPremul = ColorSpace.Premultiply(imgLinear);
        SoaImagePremulSrgb origSrgb = ColorSpace.ToPremulSrgb(imgPremul);

        SearchResult1D? resX = Segmenter.SearchX(imgPremul, (float)threshold, minLength, margin);
        SearchResult1D? resY = Segmenter.SearchY(imgPremul, (float)threshold, minLength, margin);

        System.Console.Error.WriteLine($"DEBUG: resX={resX}, resY={resY}");

        // Auto-retry with increasing margin
        int maxMargin = Math.Min(width, height) / 4;
        int curMargin = margin;
        int marginStep = 4;
        if ((resX is null || resY is null) && margin == 0 && marginStep > 0)
        {
            while (curMargin + marginStep <= maxMargin)
            {
                curMargin += marginStep;
                if (resX is null)
                    resX = Segmenter.SearchX(imgPremul, (float)threshold, minLength, curMargin);
                if (resY is null)
                    resY = Segmenter.SearchY(imgPremul, (float)threshold, minLength, curMargin);
                if (resX is not null && resY is not null) break;
            }
        }

        // Identity fallback: allow one-way compression when only one axis is stretchable
        SearchResult1D finalX = resX ?? new SearchResult1D(0, width, width);
        SearchResult1D finalY = resY ?? new SearchResult1D(0, height, height);

        System.Console.Error.WriteLine($"DEBUG: finalX={finalX.Begin}-{finalX.End} N={finalX.N}, finalY={finalY.Begin}-{finalY.End} N={finalY.N}");

        // Compress
        var (compressed, meta) = Compress2D(imgPremul, finalX, finalY);

        System.Console.WriteLine($"DEBUG: compressed={meta.CompressedW}x{meta.CompressedH}, savings={meta.SavingsPct:F1}%");

        // Boundary errors
        meta = meta with
        {
            ErrorX = BoundaryErrorX(imgPremul, finalX.Begin, finalX.End, origSrgb),
            ErrorY = BoundaryErrorX(imgPremul.Transpose(), finalY.Begin, finalY.End, origSrgb.Transpose())
        };

        // Reconstruct and measure 2D error
        SoaImagePremul reconstructedPremul = ReconstructStretched(compressed, meta);
        float err2d = ErrorMetric.MaxError(origSrgb, reconstructedPremul);
        meta = meta with { Error2d = err2d };

        // Output: Unpremultiply → Encode
        SoaImageLinear compressedLinear = ColorSpace.Unpremultiply(compressed);
        byte[] compressedU8 = ColorSpace.EncodeLinearToSrgbRgba8(compressedLinear);
        return CompressResult.Ok(compressedU8, meta);
    }
}
