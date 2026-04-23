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
}
