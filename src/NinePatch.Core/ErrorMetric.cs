namespace NinePatch.Core;

/// <summary>
/// Max per-channel error in sRGB space, [0,255] scale.
/// RGB: convert linear→sRGB byte (uint8 level), take max absolute diff across R/G/B.
/// Alpha: direct linear-float diff scaled to [0,255] (alpha is a blending coefficient,
/// not a perceptual value — no round-first, just |a_orig - a_recon| * 255).
/// When alphaWeighted, RGB error is multiplied by max(a_orig, a_recon) to suppress
/// invisible pixels.
/// </summary>
public static class ErrorMetric
{
    /// <summary>
    /// Compute max error between original and reconstructed linear RGBA.
    /// </summary>
    /// <param name="original">Linear float array (H×W×4)</param>
    /// <param name="reconstructed">Linear float array (H×W×4)</param>
    /// <param name="alphaWeighted">If true, RGB error multiplied by max(a_orig, a_recon)</param>
    /// <returns>Max error in [0,255] scale</returns>
    public static float MaxError(ReadOnlySpan<float> original, ReadOnlySpan<float> reconstructed, bool alphaWeighted = true)
    {
        // Pass 1: LUT conversion + byte→float promotion (scatter-gather, not vectorizable)
        // All byte→float conversion happens here so the core loop only sees float arrays.
        int nPixels = original.Length / 4;
        var origR = new float[nPixels];
        var origG = new float[nPixels];
        var origB = new float[nPixels];
        var origA = new float[nPixels];
        var reconR = new float[nPixels];
        var reconG = new float[nPixels];
        var reconB = new float[nPixels];
        var reconA = new float[nPixels];

        for (int i = 0; i < nPixels; i++)
        {
            origR[i] = ColorSpace.LinearToSrgbByte(original[i * 4]);
            origG[i] = ColorSpace.LinearToSrgbByte(original[i * 4 + 1]);
            origB[i] = ColorSpace.LinearToSrgbByte(original[i * 4 + 2]);
            origA[i] = Math.Clamp(original[i * 4 + 3], 0f, 1f);
            reconR[i] = ColorSpace.LinearToSrgbByte(reconstructed[i * 4]);
            reconG[i] = ColorSpace.LinearToSrgbByte(reconstructed[i * 4 + 1]);
            reconB[i] = ColorSpace.LinearToSrgbByte(reconstructed[i * 4 + 2]);
            reconA[i] = Math.Clamp(reconstructed[i * 4 + 3], 0f, 1f);
        }

        // Pass 2: pure float arithmetic, stride-1, no branches — JIT-friendly
        return alphaWeighted
            ? MaxErrorWeighted(origR, origG, origB, origA, reconR, reconG, reconB, reconA)
            : MaxErrorUnweighted(origR, origG, origB, origA, reconR, reconG, reconB, reconA);
    }

    /// <summary>Pure float max-reduce with alpha weighting. No branches in loop body.</summary>
    private static float MaxErrorWeighted(
        ReadOnlySpan<float> origR, ReadOnlySpan<float> origG, ReadOnlySpan<float> origB, ReadOnlySpan<float> origA,
        ReadOnlySpan<float> reconR, ReadOnlySpan<float> reconG, ReadOnlySpan<float> reconB, ReadOnlySpan<float> reconA)
    {
        int n = origR.Length;
        float maxErr = 0;
        for (int i = 0; i < n; i++)
        {
            float rgbErr = MathF.Max(MathF.Abs(origR[i] - reconR[i]),
                         MathF.Max(MathF.Abs(origG[i] - reconG[i]),
                                   MathF.Abs(origB[i] - reconB[i])));
            rgbErr *= MathF.Max(origA[i], reconA[i]);
            float alphaErr = MathF.Abs(origA[i] - reconA[i]) * 255f;
            float pixelErr = MathF.Max(rgbErr, alphaErr);
            if (pixelErr > maxErr) maxErr = pixelErr;
        }
        return maxErr;
    }

    /// <summary>Pure float max-reduce without alpha weighting. No branches in loop body.</summary>
    private static float MaxErrorUnweighted(
        ReadOnlySpan<float> origR, ReadOnlySpan<float> origG, ReadOnlySpan<float> origB, ReadOnlySpan<float> origA,
        ReadOnlySpan<float> reconR, ReadOnlySpan<float> reconG, ReadOnlySpan<float> reconB, ReadOnlySpan<float> reconA)
    {
        int n = origR.Length;
        float maxErr = 0;
        for (int i = 0; i < n; i++)
        {
            float rgbErr = MathF.Max(MathF.Abs(origR[i] - reconR[i]),
                         MathF.Max(MathF.Abs(origG[i] - reconG[i]),
                                   MathF.Abs(origB[i] - reconB[i])));
            float alphaErr = MathF.Abs(origA[i] - reconA[i]) * 255f;
            float pixelErr = MathF.Max(rgbErr, alphaErr);
            if (pixelErr > maxErr) maxErr = pixelErr;
        }
        return maxErr;
    }
}