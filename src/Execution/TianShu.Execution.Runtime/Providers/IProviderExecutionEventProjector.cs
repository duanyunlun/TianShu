using TianShu.Contracts.Provider;
using TianShu.Contracts.Conversations;

namespace TianShu.Execution.Runtime.Providers;

/// <summary>
/// Provider 执行事件投影器，将 southbound typed provider 事件转换为 runtime 可见事件。
/// Projects southbound typed provider events into runtime-visible stream events.
/// </summary>
internal interface IProviderExecutionEventProjector
{
    /// <summary>
    /// 将单个 Provider 语义事件投影为一个或多个 runtime 事件。
    /// Projects one provider-semantic event into one or more runtime events.
    /// </summary>
    IReadOnlyList<ControlPlaneConversationStreamEvent> Project(ProviderEventProjectionContext context, ProviderStreamEvent providerEvent);
}
