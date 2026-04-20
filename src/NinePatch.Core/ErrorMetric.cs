namespace NinePatch.Core;

/// <summary>
/// Max per-channel error in sRGB space [0,255] scale.
/// RGB error is alpha-weighted when alphaWeighted is true.
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

            float rgbErr = 0;
            for (int c = 0; c < 3; c++)
            {
                float oSrgb = ColorSpace.LinearToSrgbByte(Math.Clamp(original[i + c], 0f, 1f));
                float rSrgb = ColorSpace.LinearToSrgbByte(Math.Clamp(reconstructed[i + c], 0f, 1f));
                float err = MathF.Abs(oSrgb - rSrgb);
                if (err > rgbErr) rgbErr = err;
            }

            if (alphaWeighted)
            {
                float vis = MathF.Max(aOrig, aRecon);
                rgbErr *= vis;
            }

            float alphaErr = MathF.Abs(aOrig * 255f - aRecon * 255f);
            float pixelErr = MathF.Max(rgbErr, alphaErr);
            if (pixelErr > maxErr) maxErr = pixelErr;
        }

        return maxErr;
    }
}
