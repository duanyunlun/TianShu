using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI;

/// <summary>
/// OpenAI provider 工具事件工厂。
/// Provider tool-event factory for the OpenAI provider.
/// </summary>
public sealed class OpenAiProviderToolEventFactory : IProviderToolEventFactory
{
    /// <inheritdoc />
    public ProviderToolDirectiveEvent CreateToolDirective(ProviderToolDirectiveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ProviderToolDirectiveEvent(
            new ProviderToolDirective(
                ResolveCallId(request.CallId, request.ItemId, "工具指令"),
                request.ToolName ?? "tool",
                StructuredValue.FromPlainObject(request.InputText),
                request.RequiresApproval));
    }

    /// <inheritdoc />
    public ProviderToolOutputDeltaEvent CreateToolOutputDelta(ProviderToolOutputDeltaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ProviderToolOutputDeltaEvent(
            new ProviderToolOutputDelta(
                ResolveCallId(request.CallId, request.ItemId, "工具输出增量"),
                request.ToolName ?? request.ItemType ?? "tool",
                request.OutputText,
                StructuredValue.FromPlainObject(request.InputText),
                request.RequiresApproval ?? false));
    }

    /// <inheritdoc />
    public ProviderToolResultEvent CreateToolResult(ProviderToolResultRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ProviderToolResultEvent(
            new ProviderToolResult(
                ResolveCallId(request.CallId, request.ItemId, "工具终态"),
                request.ToolName ?? request.ItemType ?? "tool",
                StructuredValue.FromPlainObject(request.InputText),
                StructuredValue.FromPlainObject(request.OutputText),
                request.OutputText,
                request.RequiresApproval ?? false));
    }

    private static CallId ResolveCallId(string? callId, string? itemId, string eventName)
        => new(callId ?? itemId ?? throw new InvalidOperationException($"{eventName}缺少 callId/itemId。"));
}
