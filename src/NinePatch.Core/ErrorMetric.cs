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
        int len = original.Length;
        float maxErr = 0;

        for (int i = 0; i < len; i += 4)
        {
            float aOrig = Math.Clamp(original[i + 3], 0f, 1f);
            float aRecon = Math.Clamp(reconstructed[i + 3], 0f, 1f);

            // RGB: linear→sRGB byte, integer-level absolute diff, max across channels
            float rgbErr = 0;
            for (int c = 0; c < 3; c++)
            {
                byte oSrgb = ColorSpace.LinearToSrgbByte(Math.Clamp(original[i + c], 0f, 1f));
                byte rSrgb = ColorSpace.LinearToSrgbByte(Math.Clamp(reconstructed[i + c], 0f, 1f));
                float err = MathF.Abs(oSrgb - rSrgb);
                if (err > rgbErr) rgbErr = err;
            }

            if (alphaWeighted)
                rgbErr *= MathF.Max(aOrig, aRecon);

            // Alpha: direct float diff in [0,255] scale — no round-first
            float alphaErr = MathF.Abs(aOrig - aRecon) * 255f;

            float pixelErr = MathF.Max(rgbErr, alphaErr);
            if (pixelErr > maxErr) maxErr = pixelErr;
        }

        return maxErr;
    }
}
