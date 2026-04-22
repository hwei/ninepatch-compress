using System.Numerics;

namespace NinePatch.Core;

/// <summary>
/// Precomputed sRGB planes for the original image. Used by Search1D to avoid
/// repeated LinearToSrgbSimd calls on the original (which never changes).
/// </summary>
public readonly record struct PrecomputedSrgb(
    float[] R, float[] G, float[] B, float[] Alpha);

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

    /// <summary>
    /// Check if max error is within threshold. Returns early on first exceeding pixel.
    /// Much faster than MaxError when the reconstruction has large errors.
    /// </summary>
    public static bool PassesThreshold(SoaImage original, SoaImage reconstructed, float threshold, bool alphaWeighted = true)
    {
        // Delegate to the precomputed version by computing sRGB on the fly
        // (this path is only used by tests/non-hot paths)
        int n = original.PixelCount;
        int vecLen = Vector<float>.Count;
        int vecEnd = (n / vecLen) * vecLen;
        var vZero = Vector<float>.Zero;

        // --- Main SIMD loop: compute error per vector chunk, early-exit if any exceeds ---
        for (int i = 0; i < vecEnd; i += vecLen)
        {
            var oA = new Vector<float>(original.A, i);
            var rA = new Vector<float>(reconstructed.A, i);
            var alphaWeight = alphaWeighted ? Vector.Max(oA, rA) : Vector<float>.One;

            var rgbErr = ComputeRgbError(original.R, reconstructed.R, i, vecLen);
            rgbErr = Vector.Max(rgbErr, ComputeRgbError(original.G, reconstructed.G, i, vecLen));
            rgbErr = Vector.Max(rgbErr, ComputeRgbError(original.B, reconstructed.B, i, vecLen));
            rgbErr *= alphaWeight;
            rgbErr = Vector.ConditionalSelect(Vector.LessThan(rgbErr, vZero), vZero, rgbErr);

            // Unroll the vector to check each element
            for (int j = 0; j < vecLen; j++)
                if (rgbErr[j] > threshold) return false;
        }

        // --- Scalar tail ---
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
            if (rErr > threshold || gErr > threshold || bErr > threshold)
                return false;
        }

        // --- Alpha error ---
        for (int i = 0; i < vecEnd; i += vecLen)
        {
            var oA = new Vector<float>(original.A, i);
            var rA = new Vector<float>(reconstructed.A, i);
            var aErr = Vector.Abs(oA - rA) * new Vector<float>(255f);
            for (int j = 0; j < vecLen; j++)
                if (aErr[j] > threshold) return false;
        }
        for (int i = vecEnd; i < n; i++)
        {
            float aErr = MathF.Abs(original.A[i] - reconstructed.A[i]) * 255f;
            if (aErr > threshold) return false;
        }

        return true;
    }

    /// <summary>
    /// Overload accepting precomputed sRGB planes for the original image.
    /// RGB + Alpha are merged into a single early-exit pass.
    /// </summary>
    public static bool PassesThreshold(
        PrecomputedSrgb origSrgb, SoaImage reconstructed, float threshold, bool alphaWeighted = true)
    {
        int n = reconstructed.PixelCount;
        int vecLen = Vector<float>.Count;
        int vecEnd = (n / vecLen) * vecLen;
        var vZero = Vector<float>.Zero;

        // --- Main SIMD loop: merged RGB + Alpha early-exit ---
        for (int i = 0; i < vecEnd; i += vecLen)
        {
            var oA = new Vector<float>(origSrgb.Alpha, i);
            var rA = new Vector<float>(reconstructed.A, i);
            var alphaWeight = alphaWeighted ? Vector.Max(oA, rA) : Vector<float>.One;

            var rgbErr = ComputeRgbErrorPrecomputed(origSrgb.R, reconstructed.R, i);
            rgbErr = Vector.Max(rgbErr, ComputeRgbErrorPrecomputed(origSrgb.G, reconstructed.G, i));
            rgbErr = Vector.Max(rgbErr, ComputeRgbErrorPrecomputed(origSrgb.B, reconstructed.B, i));
            rgbErr *= alphaWeight;
            rgbErr = Vector.ConditionalSelect(Vector.LessThan(rgbErr, vZero), vZero, rgbErr);

            // Check RGB elements
            for (int j = 0; j < vecLen; j++)
                if (rgbErr[j] > threshold) return false;

            // Check alpha elements
            var aErr = Vector.Abs(oA - rA) * new Vector<float>(255f);
            for (int j = 0; j < vecLen; j++)
                if (aErr[j] > threshold) return false;
        }

        // --- Scalar tail ---
        for (int i = vecEnd; i < n; i++)
        {
            float rErr = MathF.Abs(origSrgb.R[i] - ColorSpace.LinearToSrgbByte(reconstructed.R[i]) / 255f);
            float gErr = MathF.Abs(origSrgb.G[i] - ColorSpace.LinearToSrgbByte(reconstructed.G[i]) / 255f);
            float bErr = MathF.Abs(origSrgb.B[i] - ColorSpace.LinearToSrgbByte(reconstructed.B[i]) / 255f);
            float aErr = MathF.Abs(origSrgb.Alpha[i] - reconstructed.A[i]) * 255f;
            if (alphaWeighted)
            {
                float w = MathF.Max(origSrgb.Alpha[i], reconstructed.A[i]);
                rErr *= w; gErr *= w; bErr *= w;
            }
            if (rErr > threshold || gErr > threshold || bErr > threshold || aErr > threshold)
                return false;
        }

        return true;
    }

    private static Vector<float> ComputeRgbError(
        float[] orig, float[] recon, int offset, int vecLen)
    {
        var oCh = new Vector<float>(orig, offset);
        var rCh = new Vector<float>(recon, offset);
        var oSrgb = ColorSpace.LinearToSrgbSimd(oCh);
        var rSrgb = ColorSpace.LinearToSrgbSimd(rCh);
        return Vector.Abs(oSrgb - rSrgb) * new Vector<float>(255f);
    }

    private static Vector<float> ComputeRgbErrorPrecomputed(
        float[] origSrgb, float[] recon, int offset)
    {
        var oSrgb = new Vector<float>(origSrgb, offset);
        var rCh = new Vector<float>(recon, offset);
        var rSrgb = ColorSpace.LinearToSrgbSimd(rCh);
        return Vector.Abs(oSrgb - rSrgb) * new Vector<float>(255f);
    }

    private static float SrgbDiffScalar(float o, float r)
    {
        float fo = ColorSpace.LinearToSrgbByte(o);
        float fr = ColorSpace.LinearToSrgbByte(r);
        return MathF.Abs(fo - fr);
    }
}
