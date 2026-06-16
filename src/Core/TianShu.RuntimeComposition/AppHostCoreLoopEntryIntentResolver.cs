using TianShu.AppHost.State;
using TianShu.Kernel;

namespace TianShu.RuntimeComposition;

/// <summary>
/// AppHost Core Loop 入口意图解析器，负责把宿主入口意图投影为请求的 Stage。
/// AppHost core-loop entry intent resolver that projects host entry intent into the requested stage.
/// </summary>
internal sealed class AppHostCoreLoopEntryIntentResolver
{
    private readonly CoreLoopEntryIntentResolver kernelResolver = new();

    public AppHostCoreLoopEntryIntentResolver(Func<string?, string?> normalize)
        => ArgumentNullException.ThrowIfNull(normalize);

    public string? ResolveRequestedStageId(
        KernelThreadSessionState session,
        AppHostCoreLoopRoutingEntryIntent entryIntent)
    {
        ArgumentNullException.ThrowIfNull(session);

        return kernelResolver.ResolveRequestedStageId(
            session.CollaborationMode?.Mode,
            entryIntent == AppHostCoreLoopRoutingEntryIntent.ReviewTurn
                ? CoreLoopEntryIntent.ReviewTurn
                : CoreLoopEntryIntent.DefaultTurn);
    }
}

internal enum AppHostCoreLoopRoutingEntryIntent
{
    DefaultTurn,
    ReviewTurn,
}
