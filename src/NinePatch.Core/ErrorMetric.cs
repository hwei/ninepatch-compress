using System.Numerics;

namespace NinePatch.Core;

/// <summary>
/// Max per-channel error in premul-sRGB (RGB) / linear-α (A), [0,255] scale.
/// 4-channel L∞: dR/dG/dB = |orig_sRGB - sRGB(recon_premul)|, dA = |orig_α - recon_α| * 255.
/// </summary>
public static class ErrorMetric
{
    /// <summary>
    /// Compute max error between original (premul-sRGB) and reconstructed (premul-linear).
    /// RGB channels: compare in sRGB space. Alpha: compare in linear space.
    /// </summary>
    public static float MaxError(SoaImagePremulSrgb original, SoaImagePremul reconstructed)
    {
        int n = original.PixelCount;
        int vecLen = Vector<float>.Count;
        int vecEnd = (n / vecLen) * vecLen;
        float maxErr = 0;

        var vMax = Vector<float>.Zero;
        var v255 = new Vector<float>(255f);

        // --- SIMD loop: 4 channels merged ---
        for (int i = 0; i < vecEnd; i += vecLen)
        {
            var dR = (Vector.Abs(new Vector<float>(original.R, i) - ColorSpace.LinearToSrgbSimd(new Vector<float>(reconstructed.R, i)) * v255));
            var dG = (Vector.Abs(new Vector<float>(original.G, i) - ColorSpace.LinearToSrgbSimd(new Vector<float>(reconstructed.G, i)) * v255));
            var dB = (Vector.Abs(new Vector<float>(original.B, i) - ColorSpace.LinearToSrgbSimd(new Vector<float>(reconstructed.B, i)) * v255));
            var dA = Vector.Abs(new Vector<float>(original.A, i) - new Vector<float>(reconstructed.A, i)) * v255;

            var vErr = Vector.Max(dR, Vector.Max(dG, Vector.Max(dB, dA)));
            vMax = Vector.Max(vMax, vErr);
        }

        // Reduce vector max
        for (int j = 0; j < vecLen; j++)
        {
            float v = vMax[j];
            if (v > maxErr) maxErr = v;
        }

        // --- Scalar tail ---
        for (int i = vecEnd; i < n; i++)
        {
            float rErr = MathF.Abs(original.R[i] - ColorSpace.LinearToSrgbFloat(reconstructed.R[i]) * 255f);
            float gErr = MathF.Abs(original.G[i] - ColorSpace.LinearToSrgbFloat(reconstructed.G[i]) * 255f);
            float bErr = MathF.Abs(original.B[i] - ColorSpace.LinearToSrgbFloat(reconstructed.B[i]) * 255f);
            float aErr = MathF.Abs(original.A[i] - reconstructed.A[i]) * 255f;
            float chMax = MathF.Max(rErr, MathF.Max(gErr, MathF.Max(bErr, aErr)));
            if (chMax > maxErr) maxErr = chMax;
        }

        return maxErr;
    }

    /// <summary>
    /// Check if max error is within threshold. Returns early on first exceeding pixel.
    /// </summary>
    public static bool PassesThreshold(SoaImagePremulSrgb original, SoaImagePremul reconstructed, float threshold)
    {
        int n = original.PixelCount;
        int vecLen = Vector<float>.Count;
        int vecEnd = (n / vecLen) * vecLen;
        var v255 = new Vector<float>(255f);
        var vThresh = new Vector<float>(threshold);

        // --- SIMD loop with early exit ---
        for (int i = 0; i < vecEnd; i += vecLen)
        {
            var dR = (Vector.Abs(new Vector<float>(original.R, i) - ColorSpace.LinearToSrgbSimd(new Vector<float>(reconstructed.R, i)) * v255));
            var dG = (Vector.Abs(new Vector<float>(original.G, i) - ColorSpace.LinearToSrgbSimd(new Vector<float>(reconstructed.G, i)) * v255));
            var dB = (Vector.Abs(new Vector<float>(original.B, i) - ColorSpace.LinearToSrgbSimd(new Vector<float>(reconstructed.B, i)) * v255));
            var dA = Vector.Abs(new Vector<float>(original.A, i) - new Vector<float>(reconstructed.A, i)) * v255;

            var vErr = Vector.Max(dR, Vector.Max(dG, Vector.Max(dB, dA)));
            if (Vector.GreaterThanAny(vErr, vThresh)) return false;
        }

        // --- Scalar tail ---
        for (int i = vecEnd; i < n; i++)
        {
            float rErr = MathF.Abs(original.R[i] - ColorSpace.LinearToSrgbFloat(reconstructed.R[i]) * 255f);
            float gErr = MathF.Abs(original.G[i] - ColorSpace.LinearToSrgbFloat(reconstructed.G[i]) * 255f);
            float bErr = MathF.Abs(original.B[i] - ColorSpace.LinearToSrgbFloat(reconstructed.B[i]) * 255f);
            float aErr = MathF.Abs(original.A[i] - reconstructed.A[i]) * 255f;
            if (rErr > threshold || gErr > threshold || bErr > threshold || aErr > threshold)
                return false;
        }

        return true;
    }
}
