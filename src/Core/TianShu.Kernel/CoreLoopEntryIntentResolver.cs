namespace TianShu.Kernel;

/// <summary>
/// Core loop 入口意图解析器，负责把内核入口意图解析为默认内置 stage。
/// Core-loop entry intent resolver that maps Kernel entry intent to the default built-in stage.
/// </summary>
public sealed class CoreLoopEntryIntentResolver
{
    /// <summary>
    /// 解析入口意图对应的请求 stage id。
    /// Resolves the requested stage id for an entry intent.
    /// </summary>
    public string? ResolveRequestedStageId(
        string? collaborationMode,
        CoreLoopEntryIntent entryIntent)
    {
        if (entryIntent == CoreLoopEntryIntent.ReviewTurn)
        {
            return KernelBuiltInStageIds.Review;
        }

        return string.Equals(
            Normalize(collaborationMode),
            KernelCollaborationModes.Plan,
            StringComparison.OrdinalIgnoreCase)
            ? KernelBuiltInStageIds.Planning
            : null;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Core loop 入口意图。
/// Core-loop entry intent.
/// </summary>
public enum CoreLoopEntryIntent
{
    DefaultTurn,
    ReviewTurn,
}

/// <summary>
/// Kernel 内置 stage id。
/// Built-in Kernel stage identifiers.
/// </summary>
public static class KernelBuiltInStageIds
{
    public const string Planning = "planning";
    public const string Review = "review";
}

/// <summary>
/// Kernel 协作模式常量。
/// Kernel collaboration mode constants.
/// </summary>
public static class KernelCollaborationModes
{
    public const string Plan = "plan";
}
