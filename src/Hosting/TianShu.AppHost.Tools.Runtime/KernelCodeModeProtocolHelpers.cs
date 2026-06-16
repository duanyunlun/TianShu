using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelCodeModeProtocolHelpers
{
    private const string RunningPrefix = "Script running with cell ID ";

    public static object BuildCodeModeProtocolPayload(
        string threadId,
        string turnId,
        KernelCodeModeOperationResult result,
        string? fallbackCellId)
    {
        var status = InferCodeModeStatus(result.ContentItems, result.Success);
        var cellId = fallbackCellId;
        if (string.Equals(status, "running", StringComparison.Ordinal))
        {
            cellId ??= TryExtractCodeModeCellId(result.ContentItems);
        }

        return new
        {
            success = result.Success,
            status,
            threadId,
            turnId,
            cellId,
            output = result.Output,
            contentItems = result.ContentItems.Select(static item => new
            {
                type = item.Type,
                text = item.Text,
                imageUrl = item.ImageUrl,
                detail = item.Detail,
            }).ToArray(),
        };
    }

    public static string InferCodeModeStatus(IReadOnlyList<KernelToolOutputContentItem> contentItems, bool success)
    {
        var header = contentItems
            .FirstOrDefault(static item => string.Equals(item.Type, "input_text", StringComparison.OrdinalIgnoreCase))
            ?.Text;
        if (!string.IsNullOrWhiteSpace(header))
        {
            if (header.StartsWith(RunningPrefix, StringComparison.Ordinal))
            {
                return "running";
            }

            if (header.StartsWith("Script completed", StringComparison.Ordinal))
            {
                return "completed";
            }

            if (header.StartsWith("Script terminated", StringComparison.Ordinal))
            {
                return "terminated";
            }

            if (header.StartsWith("Script failed", StringComparison.Ordinal))
            {
                return "failed";
            }
        }

        return success ? "completed" : "failed";
    }

    public static string? TryExtractCodeModeCellId(IReadOnlyList<KernelToolOutputContentItem> contentItems)
    {
        var header = contentItems
            .FirstOrDefault(static item => string.Equals(item.Type, "input_text", StringComparison.OrdinalIgnoreCase))
            ?.Text;
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(RunningPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var remainder = header[RunningPrefix.Length..];
        var lineBreakIndex = remainder.IndexOf('\n');
        var cellId = lineBreakIndex >= 0 ? remainder[..lineBreakIndex] : remainder;
        return KernelToolJsonHelpers.Normalize(cellId);
    }
}
