using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Adaptive.Tools;

/// <summary>
/// KernelTool 基类，同时暴露统一 ITianShuTool 外壳。
/// Base KernelTool that also exposes the unified ITianShuTool shell.
/// </summary>
public abstract class KernelToolBase : IKernelTool, ITianShuTool
{
    protected KernelToolBase(string toolName, string description)
    {
        ToolName = toolName;
        Descriptor = new ToolDescriptor(
            $"kernel.{toolName}",
            toolName,
            description,
            kind: ToolKind.Kernel,
            inputSchemaRef: new JsonSchemaRef($"schema.kernel.{toolName}.input", "1"),
            outputSchemaRef: new JsonSchemaRef("schema.kernel.tool_result", "1"),
            permissions: new PermissionDeclaration(requiresHumanGate: true, rationale: "KernelTool only proposes Kernel changes."),
            sideEffects: new SideEffectProfile(SideEffectLevel.None),
            audit: new AuditProfile(eventKinds: new[] { "kernel.tool.invoked" }));
    }

    public string ToolName { get; }

    public ToolDescriptor Descriptor { get; }

    public abstract Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default);

    public async ValueTask<ToolInvocationResult> InvokeAsync(ToolInvocationEnvelope invocation, ToolInvocationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(context);

        var sourceIntentId = new CoreIntentId(context.SourceIntentId);
        var state = new KernelRunState(new KernelRunId($"run-{context.SourceIntentId}"), sourceIntentId);
        var result = await InvokeKernelAsync(new KernelToolInvocation(sourceIntentId, state, new KernelRunOptions(), invocation.Input), cancellationToken).ConfigureAwait(false);

        return new ToolInvocationResult(
            invocation.CallId,
            invocation.ToolId,
            new[]
            {
                new ToolStreamItem(
                    result.Proposal is null ? "kernel_operation" : "kernel_proposal",
                    StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["tool"] = ToolName,
                        ["proposalKind"] = result.Proposal?.ProposalKind.ToString(),
                        ["operationKind"] = result.Operation?.OperationKind.ToString(),
                        ["rationaleRef"] = result.RationaleRef,
                    }),
                    isTerminal: true),
            });
    }

    protected static string ReadString(StructuredValue input, string propertyName, string fallback)
        => input.Kind == StructuredValueKind.Object
           && input.TryGetProperty(propertyName, out var value)
           && value is not null
           && value.Kind != StructuredValueKind.Null
            ? value.GetString() ?? fallback
            : fallback;

    internal static KernelProposalId ProposalId(string toolName) => new($"proposal-{toolName}-{Guid.NewGuid():N}");

    internal static KernelOperationId OperationId(string toolName) => new($"operation-{toolName}-{Guid.NewGuid():N}");

    internal static RiskProfile LowRisk(bool requiresHumanGate = false) => new("low", requiresHumanGate: requiresHumanGate);

    internal static KernelBudgetImpact BudgetImpact(string reason) => new(new KernelBudget(tokenBudget: 128, timeBudgetMs: 1_000, toolCallBudget: 1), reason);

    internal static RollbackPlan Rollback(string toolName, bool reversible = true) => new($"rollback.{toolName}", reversible);

    internal static EvaluationPlan Evaluation(string toolName) => new($"evaluation.{toolName}", new[] { "proposal_validity" });
}
