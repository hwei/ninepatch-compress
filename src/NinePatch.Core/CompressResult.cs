namespace NinePatch.Core;

public sealed class CompressResult
{
    public CompressStatus Status { get; init; }
    public string? Message { get; init; }
    public byte[]? CompressedRgba { get; init; }
    public NinePatchMeta? Meta { get; init; }

    public static CompressResult Ok(byte[] compressed, NinePatchMeta meta) =>
        new() { Status = CompressStatus.Success, CompressedRgba = compressed, Meta = meta };

    public static CompressResult Fail(CompressStatus status, string message) =>
        new() { Status = status, Message = message };
}
