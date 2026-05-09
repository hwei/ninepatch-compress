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
        int minLength = 8)
    {
        try
        {
            var result = NinePatchCompressor.Compress(rgba, width, height, threshold, margin, minLength);
            return SerializeResult(result);
        }
        catch (Exception ex)
        {
            return SerializeError(CompressStatus.InvalidInput, ex.Message);
        }
    }

    [JSExport]
    public static string Analyze(
        byte[] rgba,
        int width,
        int height,
        double threshold = 4.0,
        int margin = 0,
        int minLength = 8)
    {
        try
        {
            var result = NinePatchCompressor.Analyze(rgba, width, height, threshold, margin, minLength);
            return SerializeAnalyzeResult(result);
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

    private static string SerializeAnalyzeResult(DebugAnalyzeResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"status\":").Append((int)result.Status);
        sb.Append(",\"message\":").Append(JsonString(result.Message));

        if (result.Status == CompressStatus.Success)
        {
            sb.Append(",\"final_x\":{");
            sb.Append("\"begin\":").Append(result.FinalX.Begin).Append(',');
            sb.Append("\"end\":").Append(result.FinalX.End).Append(',');
            sb.Append("\"n\":").Append(result.FinalX.N).Append(',');
            sb.Append("\"is_identity_fallback\":").Append(result.FinalX.IsIdentityFallback ? "true" : "false");
            sb.Append('}');

            sb.Append(",\"final_y\":{");
            sb.Append("\"begin\":").Append(result.FinalY.Begin).Append(',');
            sb.Append("\"end\":").Append(result.FinalY.End).Append(',');
            sb.Append("\"n\":").Append(result.FinalY.N).Append(',');
            sb.Append("\"is_identity_fallback\":").Append(result.FinalY.IsIdentityFallback ? "true" : "false");
            sb.Append('}');

            sb.Append(",\"x_candidates\":");
            SerializeCandidatesArray(sb, result.XCandidates);

            sb.Append(",\"y_candidates\":");
            SerializeCandidatesArray(sb, result.YCandidates);
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static void SerializeCandidatesArray(System.Text.StringBuilder sb, List<DebugLineCandidates> candidates)
    {
        sb.Append('[');
        for (int i = 0; i < candidates.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var lc = candidates[i];
            sb.Append("{\"line\":").Append(lc.LineIndex);
            sb.Append(",\"intervals\":[");
            for (int j = 0; j < lc.Intervals.Count; j++)
            {
                if (j > 0) sb.Append(',');
                var (begin, end) = lc.Intervals[j];
                sb.Append('[').Append(begin).Append(',').Append(end).Append(']');
            }
            sb.Append("]}");
        }
        sb.Append(']');
    }

    private static string JsonString(string? s)
    {
        if (s is null) return "null";
        var escaped = s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        return $"\"{escaped}\"";
    }
}
