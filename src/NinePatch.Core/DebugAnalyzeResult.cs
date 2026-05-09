namespace NinePatch.Core;

/// <summary>Per-line candidate intervals after channel intersection.</summary>
public readonly record struct DebugLineCandidates(
    int LineIndex,
    List<(int begin, int end)> Intervals);

/// <summary>Final axis result reported with its computation status.</summary>
public readonly record struct DebugAxisResult(
    int Begin,
    int End,
    int N,
    bool IsIdentityFallback);

/// <summary>
/// On-demand analysis result containing per-row X candidates,
/// per-column Y candidates, and final selected axis results.
/// </summary>
public sealed class DebugAnalyzeResult
{
    public CompressStatus Status { get; init; }
    public string? Message { get; init; }

    public DebugAxisResult FinalX { get; init; }
    public DebugAxisResult FinalY { get; init; }

    /// <summary>Per-row X candidate intervals (one entry per source row).</summary>
    public List<DebugLineCandidates> XCandidates { get; init; } = [];

    /// <summary>Per-column Y candidate intervals (one entry per source column).</summary>
    public List<DebugLineCandidates> YCandidates { get; init; } = [];

    public static DebugAnalyzeResult Ok(
        DebugAxisResult finalX,
        DebugAxisResult finalY,
        List<DebugLineCandidates> xCandidates,
        List<DebugLineCandidates> yCandidates) =>
        new()
        {
            Status = CompressStatus.Success,
            FinalX = finalX,
            FinalY = finalY,
            XCandidates = xCandidates,
            YCandidates = yCandidates,
        };

    public static DebugAnalyzeResult Fail(CompressStatus status, string message) =>
        new() { Status = status, Message = message };
}
