using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Adaptive.Tools;

/// <summary>
/// KernelTool 名称常量。
/// KernelTool name constants.
/// </summary>
public static class KernelToolNames
{
    public const string ProposeStage = "propose_stage";
    public const string ComposeStageGraph = "compose_stage_graph";
    public const string ReviseStageGraph = "revise_stage_graph";
    public const string SelectModelRoute = "select_model_route";
    public const string SelectToolStrategy = "select_tool_strategy";
    public const string RequestCapabilityCall = "request_capability_call";
    public const string UpdateContextPolicy = "update_context_policy";
    public const string ProposeCheckpoint = "propose_checkpoint";
    public const string ProposeRecoveryPlan = "propose_recovery_plan";
    public const string EvaluateRun = "evaluate_run";
    public const string PromoteStrategy = "promote_strategy";
    public const string RollbackStrategy = "rollback_strategy";
    public const string ProposeKernelPolicyChange = "propose_kernel_policy_change";
}

/// <summary>
/// KernelTool typed 调用输入。
/// Typed KernelTool invocation input.
/// </summary>
public sealed record KernelToolInvocation
{
    public KernelToolInvocation(
        CoreIntent intent,
        KernelRunState state,
        KernelRunOptions options,
        StructuredValue? input = null)
        : this(intent?.IntentId ?? throw new ArgumentNullException(nameof(intent)), state, options, input)
    {
        Intent = intent;
    }

    public KernelToolInvocation(
        CoreIntentId sourceIntentId,
        KernelRunState state,
        KernelRunOptions options,
        StructuredValue? input = null)
    {
        SourceIntentId = sourceIntentId;
        State = state ?? throw new ArgumentNullException(nameof(state));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Input = input ?? StructuredValue.Null;
    }

    public CoreIntent? Intent { get; }

    public CoreIntentId SourceIntentId { get; }

    public KernelRunState State { get; }

    public KernelRunOptions Options { get; }

    public StructuredValue Input { get; }
}

/// <summary>
/// KernelTool typed 调用结果，只允许 proposal 或 operation。
/// Typed KernelTool result that only allows proposal or operation.
/// </summary>
public sealed record KernelToolResult
{
    public KernelToolResult(KernelProposal? proposal = null, KernelOperation? operation = null, string? rationaleRef = null)
    {
        if (proposal is null && operation is null)
        {
            throw new ArgumentException("KernelToolResult 必须包含 proposal 或 operation。");
        }

        if (proposal is not null && operation is not null)
        {
            throw new ArgumentException("KernelToolResult 不能同时包含 proposal 和 operation。");
        }

        Proposal = proposal;
        Operation = operation;
        RationaleRef = rationaleRef;
    }

    public KernelProposal? Proposal { get; }

    public KernelOperation? Operation { get; }

    public string? RationaleRef { get; }
}

/// <summary>
/// AI 可调用的 KernelTool 底层接口，禁止返回 RuntimeStep。
/// Base KernelTool interface callable by AI; RuntimeStep return values are forbidden.
/// </summary>
public interface IKernelTool
{
    string ToolName { get; }

    Task<KernelToolResult> InvokeKernelAsync(KernelToolInvocation invocation, CancellationToken cancellationToken = default);
}
