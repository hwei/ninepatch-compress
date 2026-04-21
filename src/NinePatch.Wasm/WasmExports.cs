using System.Runtime.InteropServices.JavaScript;
using NinePatch.Core;

namespace NinePatch.Wasm;

/// <summary>
/// JavaScript-exported API for nine-patch compression.
/// </summary>
public static partial class WasmExports
{
    [JSExport]
    public static string Compress(
        byte[] rgba,
        int width,
        int height,
        double threshold = 4.0,
        int margin = 0,
        double minSavings = 30.0)
    {
        try
        {
            var result = NinePatchCompressor.Compress(rgba, width, height, threshold, margin, minSavings);
            return SerializeResult(result);
        }
        catch (Exception ex)
        {
            return SerializeError(CompressStatus.InvalidInput, ex.Message);
        }
    }

    [JSExport]
    public static string GetVersion() => "1.0.0-wasm";

    private static string SerializeResult(CompressResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"status\":").Append((int)result.Status);
        sb.Append(",\"message\":").Append(JsonString(result.Message));

        if (result.Status == CompressStatus.Success && result.Meta is { } meta)
        {
            sb.Append(",\"metadata\":{");
            sb.Append("\"xb\":").Append(meta.Xb).Append(',');
            sb.Append("\"xe\":").Append(meta.Xe).Append(',');
            sb.Append("\"yb\":").Append(meta.Yb).Append(',');
            sb.Append("\"ye\":").Append(meta.Ye).Append(',');
            sb.Append("\"original_width\":").Append(meta.OriginalW).Append(',');
            sb.Append("\"original_height\":").Append(meta.OriginalH).Append(',');
            sb.Append("\"compressed_width\":").Append(meta.CompressedW).Append(',');
            sb.Append("\"compressed_height\":").Append(meta.CompressedH).Append(',');
            sb.Append("\"nx\":").Append(meta.Nx).Append(',');
            sb.Append("\"ny\":").Append(meta.Ny).Append(',');
            sb.Append("\"error_x\":").Append(meta.ErrorX.ToString("G17")).Append(',');
            sb.Append("\"error_y\":").Append(meta.ErrorY.ToString("G17")).Append(',');
            sb.Append("\"error_2d\":").Append(meta.Error2d.ToString("G17")).Append(',');
            sb.Append("\"savings_pct\":").Append(meta.SavingsPct.ToString("G17"));
            sb.Append('}');

            sb.Append(",\"compressed_rgba_b64\":").Append(JsonString(Convert.ToBase64String(result.CompressedRgba!)));
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static string SerializeError(CompressStatus status, string message)
    {
        return $"{{\"status\":{(int)status},\"message\":{JsonString(message)}}}";
    }

    private static string JsonString(string? s)
    {
        if (s is null) return "null";
        var escaped = s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        return $"\"{escaped}\"";
    }
}
