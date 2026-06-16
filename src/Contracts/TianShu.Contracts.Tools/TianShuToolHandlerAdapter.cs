namespace TianShu.Contracts.Tools;

/// <summary>
/// 将既有 handler 适配为新架构统一工具入口。
/// Adapts the existing handler surface to the unified tool entry point.
/// </summary>
public sealed class TianShuToolHandlerAdapter : ITianShuTool
{
    private readonly ITianShuToolHandler handler;
    private readonly Func<ToolInvocationContext, TianShuToolInvocationContext> contextFactory;

    public TianShuToolHandlerAdapter(
        ITianShuToolHandler handler,
        Func<ToolInvocationContext, TianShuToolInvocationContext>? contextFactory = null)
    {
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        this.contextFactory = contextFactory ?? CreateDefaultInvocationContext;
    }

    public ToolDescriptor Descriptor => handler.Descriptor;

    public ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationEnvelope invocation,
        ToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(context);

        if (!string.Equals(invocation.ToolId, Descriptor.ToolId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ToolInvocationEnvelope 的 ToolId 与 ToolDescriptor 不一致。");
        }

        return handler.InvokeAsync(
            new ToolInvocationRequest(
                invocation.CallId,
                invocation.ToolId,
                invocation.Operation,
                invocation.Input,
                invocation.Metadata),
            contextFactory(context),
            cancellationToken);
    }

    private static TianShuToolInvocationContext CreateDefaultInvocationContext(ToolInvocationContext context)
        => new(
            context.SourceIntentId,
            context.RuntimeStepId,
            context.WorkingDirectory ?? string.Empty,
            Metadata: context.Metadata);
}
