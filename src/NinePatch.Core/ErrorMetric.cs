using System.Numerics;

namespace NinePatch.Core;

/// <summary>
/// Max per-channel error in sRGB space, [0,255] scale.
/// RGB: convert linear→sRGB float, take max absolute diff across R/G/B.
/// Alpha: direct linear-float diff scaled to [0,255].
/// When alphaWeighted, RGB error multiplied by max(a_orig, a_recon).
/// </summary>
public static class ErrorMetric
{
    /// <summary>
    /// Compute max error between original and reconstructed SoaImage.
    /// </summary>
    /// <param name="original">SoaImage with linear float planes</param>
    /// <param name="reconstructed">SoaImage with linear float planes</param>
    /// <param name="alphaWeighted">If true, RGB error multiplied by max(a_orig, a_recon)</param>
    /// <returns>Max error in [0,255] scale</returns>
    public static float MaxError(SoaImage original, SoaImage reconstructed, bool alphaWeighted = true)
    {
        int n = original.PixelCount;
        int vecLen = Vector<float>.Count;
        int vecEnd = (n / vecLen) * vecLen;
        float maxErr = 0;

        var vMax = Vector<float>.Zero;
        var vZero = Vector<float>.Zero;

        // --- Main SIMD loop: process all channels sequentially per pixel vector ---
        for (int i = 0; i < vecEnd; i += vecLen)
        {
            var oA = new Vector<float>(original.A, i);
            var rA = new Vector<float>(reconstructed.A, i);

            // Alpha weight: max(oA, rA)
            var alphaWeight = alphaWeighted
                ? Vector.Max(oA, rA)
                : Vector<float>.One;

            // RGB error
            var rgbErr = ComputeRgbError(original.R, reconstructed.R, i, vecLen);
            rgbErr = Vector.Max(rgbErr, ComputeRgbError(original.G, reconstructed.G, i, vecLen));
            rgbErr = Vector.Max(rgbErr, ComputeRgbError(original.B, reconstructed.B, i, vecLen));

            // Apply alpha weight
            rgbErr *= alphaWeight;

            // Clamp to avoid negative/NaN artifacts
            rgbErr = Vector.ConditionalSelect(Vector.LessThan(rgbErr, vZero), vZero, rgbErr);

            vMax = Vector.Max(vMax, rgbErr);
        }

        // Reduce vector max
        for (int j = 0; j < vecLen; j++)
        {
            float v = vMax[j];
            if (v > maxErr) maxErr = v;
        }

        // --- Scalar tail for RGB ---
        for (int i = vecEnd; i < n; i++)
        {
            float rErr = SrgbDiffScalar(original.R[i], reconstructed.R[i]);
            float gErr = SrgbDiffScalar(original.G[i], reconstructed.G[i]);
            float bErr = SrgbDiffScalar(original.B[i], reconstructed.B[i]);
            if (alphaWeighted)
            {
                float w = MathF.Max(original.A[i], reconstructed.A[i]);
                rErr *= w; gErr *= w; bErr *= w;
            }
            if (rErr > maxErr) maxErr = rErr;
            if (gErr > maxErr) maxErr = gErr;
            if (bErr > maxErr) maxErr = bErr;
        }

        // --- Alpha error (always |a_orig - a_recon| * 255, never weighted) ---
        var vaMax = Vector<float>.Zero;
        var vScale = new Vector<float>(255f);
        for (int i = 0; i < vecEnd; i += vecLen)
        {
            var oA = new Vector<float>(original.A, i);
            var rA = new Vector<float>(reconstructed.A, i);
            var aErr = Vector.Abs(oA - rA) * vScale;
            vaMax = Vector.Max(vaMax, aErr);
        }
        for (int j = 0; j < vecLen; j++)
        {
            float v = vaMax[j];
            if (v > maxErr) maxErr = v;
        }
        for (int i = vecEnd; i < n; i++)
        {
            float aErr = MathF.Abs(original.A[i] - reconstructed.A[i]) * 255f;
            if (aErr > maxErr) maxErr = aErr;
        }

        return maxErr;
    }

    private static Vector<float> ComputeRgbError(
        float[] orig, float[] recon, int offset, int vecLen)
    {
        var oCh = new Vector<float>(orig, offset);
        var rCh = new Vector<float>(recon, offset);
        var oSrgb = ColorSpace.LinearToSrgbSimd(oCh);
        var rSrgb = ColorSpace.LinearToSrgbSimd(rCh);
        return Vector.Abs(oSrgb - rSrgb);
    }

    private static float SrgbDiffScalar(float o, float r)
    {
        float fo = ColorSpace.LinearToSrgbByte(o);
        float fr = ColorSpace.LinearToSrgbByte(r);
        return MathF.Abs(fo - fr);
    }
}
