using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Memory;

/// <summary>
/// Memory Module 统一入口，供 Execution Runtime 通过 ModuleCapabilityStep 调用。
/// Unified Memory Module entry point invoked by Execution Runtime through ModuleCapabilityStep.
/// </summary>
public interface IMemoryModule : IModuleHealthCheck
{
    ValueTask<MemoryModuleQueryResult> QueryAsync(
        MemoryModuleQueryInvocation invocation,
        CancellationToken cancellationToken);

    ValueTask<MemoryMutationResult> MutateAsync(
        MemoryModuleMutationInvocation invocation,
        CancellationToken cancellationToken);
}

/// <summary>
/// Memory Module 调用上下文，承载 RuntimeStep 来源追踪和治理边界。
/// Memory Module invocation context carrying RuntimeStep source tracing and governance boundary.
/// </summary>
public sealed record MemoryModuleInvocationContext
{
    public MemoryModuleInvocationContext(
        string runtimeStepId,
        string sourceIntentId,
        string sourceGraphId,
        string sourceStageId,
        string sourceKernelOperationId,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        MemoryOperationContext operationContext,
        MetadataBag? metadata = null)
    {
        RuntimeStepId = IdentifierGuard.AgainstNullOrWhiteSpace(runtimeStepId, nameof(runtimeStepId));
        SourceIntentId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceIntentId, nameof(sourceIntentId));
        SourceGraphId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceGraphId, nameof(sourceGraphId));
        SourceStageId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceStageId, nameof(sourceStageId));
        SourceKernelOperationId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceKernelOperationId, nameof(sourceKernelOperationId));
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
        SideEffect = sideEffect ?? throw new ArgumentNullException(nameof(sideEffect));
        OperationContext = operationContext ?? throw new ArgumentNullException(nameof(operationContext));
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string RuntimeStepId { get; }

    public string SourceIntentId { get; }

    public string SourceGraphId { get; }

    public string SourceStageId { get; }

    public string SourceKernelOperationId { get; }

    public PermissionEnvelope Permission { get; }

    public SideEffectProfile SideEffect { get; }

    public MemoryOperationContext OperationContext { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Memory Module 查询调用。
/// Memory Module query invocation.
/// </summary>
public sealed record MemoryModuleQueryInvocation(MemoryModuleQuery Query, MemoryModuleInvocationContext Context)
{
    public MemoryModuleQuery Query { get; } = Query ?? throw new ArgumentNullException(nameof(Query));

    public MemoryModuleInvocationContext Context { get; } = Context ?? throw new ArgumentNullException(nameof(Context));
}

/// <summary>
/// Memory Module 写入调用。
/// Memory Module mutation invocation.
/// </summary>
public sealed record MemoryModuleMutationInvocation(MemoryModuleMutation Mutation, MemoryModuleInvocationContext Context)
{
    public MemoryModuleMutation Mutation { get; } = MemoryModulePayloadGuard.RejectReasoningTrace(Mutation ?? throw new ArgumentNullException(nameof(Mutation)));

    public MemoryModuleInvocationContext Context { get; } = Context ?? throw new ArgumentNullException(nameof(Context));
}

/// <summary>
/// Memory Module 查询结果，保留不同查询形态的 typed projection。
/// Memory Module query result preserving typed projections for different query shapes.
/// </summary>
public sealed record MemoryModuleQueryResult
{
    public MemoryModuleQueryResult(
        IReadOnlyList<MemoryProviderDescriptor>? providers = null,
        IReadOnlyList<MemorySpace>? spaces = null,
        MemoryOverlay? overlay = null,
        MemoryQueryResult? records = null,
        MemoryReviewQueryResult? reviews = null,
        MemoryQueryResult? exported = null,
        IReadOnlyList<string>? degradedProviders = null)
    {
        Providers = providers ?? Array.Empty<MemoryProviderDescriptor>();
        Spaces = spaces ?? Array.Empty<MemorySpace>();
        Overlay = overlay;
        Records = records;
        Reviews = reviews;
        Exported = exported;
        DegradedProviders = degradedProviders ?? Array.Empty<string>();
    }

    public IReadOnlyList<MemoryProviderDescriptor> Providers { get; }

    public IReadOnlyList<MemorySpace> Spaces { get; }

    public MemoryOverlay? Overlay { get; }

    public MemoryQueryResult? Records { get; }

    public MemoryReviewQueryResult? Reviews { get; }

    public MemoryQueryResult? Exported { get; }

    public IReadOnlyList<string> DegradedProviders { get; }
}

public abstract record MemoryModuleQuery;

public sealed record ListMemoryProvidersModuleQuery(ListMemoryProviders Query) : MemoryModuleQuery
{
    public ListMemoryProviders Query { get; } = Query ?? throw new ArgumentNullException(nameof(Query));
}

public sealed record ListMemorySpacesModuleQuery(ListMemorySpaces Query) : MemoryModuleQuery
{
    public ListMemorySpaces Query { get; } = Query ?? throw new ArgumentNullException(nameof(Query));
}

public sealed record ResolveMemoryOverlayModuleQuery(ResolveMemoryOverlay Query) : MemoryModuleQuery
{
    public ResolveMemoryOverlay Query { get; } = Query ?? throw new ArgumentNullException(nameof(Query));
}

public sealed record FilterMemoryModuleQuery(FilterMemory Query) : MemoryModuleQuery
{
    public FilterMemory Query { get; } = Query ?? throw new ArgumentNullException(nameof(Query));
}

public sealed record ListMemoryReviewsModuleQuery(ListMemoryReviews Query) : MemoryModuleQuery
{
    public ListMemoryReviews Query { get; } = Query ?? throw new ArgumentNullException(nameof(Query));
}

public sealed record ExportMemoryModuleQuery(ExportMemory Query) : MemoryModuleQuery
{
    public ExportMemory Query { get; } = Query ?? throw new ArgumentNullException(nameof(Query));
}

public abstract record MemoryModuleMutation;

public sealed record AddMemoryModuleMutation(AddMemory Command) : MemoryModuleMutation
{
    public AddMemory Command { get; } = Command ?? throw new ArgumentNullException(nameof(Command));
}

public sealed record ImportMemoryModuleMutation(ImportMemory Command) : MemoryModuleMutation
{
    public ImportMemory Command { get; } = Command ?? throw new ArgumentNullException(nameof(Command));
}

public sealed record BindMemoryProviderModuleMutation(BindMemoryProvider Command) : MemoryModuleMutation
{
    public BindMemoryProvider Command { get; } = Command ?? throw new ArgumentNullException(nameof(Command));
}

public sealed record ForgetMemoryModuleMutation(ForgetMemory Command) : MemoryModuleMutation
{
    public ForgetMemory Command { get; } = Command ?? throw new ArgumentNullException(nameof(Command));
}

public sealed record DeleteMemoryModuleMutation(DeleteMemory Command) : MemoryModuleMutation
{
    public DeleteMemory Command { get; } = Command ?? throw new ArgumentNullException(nameof(Command));
}

public sealed record SupersedeMemoryModuleMutation(SupersedeMemory Command) : MemoryModuleMutation
{
    public SupersedeMemory Command { get; } = Command ?? throw new ArgumentNullException(nameof(Command));
}

public sealed record ApproveMemoryReviewModuleMutation(ApproveMemoryReview Command) : MemoryModuleMutation
{
    public ApproveMemoryReview Command { get; } = Command ?? throw new ArgumentNullException(nameof(Command));
}

public sealed record RecordMemoryFeedbackModuleMutation(RecordMemoryFeedback Command) : MemoryModuleMutation
{
    public RecordMemoryFeedback Command { get; } = Command ?? throw new ArgumentNullException(nameof(Command));
}

public sealed record RecordMemoryCitationModuleMutation(RecordMemoryCitation Command) : MemoryModuleMutation
{
    public RecordMemoryCitation Command { get; } = Command ?? throw new ArgumentNullException(nameof(Command));
}

internal static class MemoryModulePayloadGuard
{
    private static readonly string[] ProhibitedKeys =
    [
        "chain_of_thought",
        "chainOfThought",
        "cot",
        "reasoning_trace",
        "reasoningTrace",
        "full_model_thoughts",
        "fullModelThoughts",
        "internal_reasoning",
        "internalReasoning",
    ];

    private static readonly string[] ProhibitedSnippetMarkers =
    [
        "chain_of_thought",
        "chain of thought",
        "full model thoughts",
        "internal reasoning trace",
        "hidden reasoning",
    ];

    public static MemoryModuleMutation RejectReasoningTrace(MemoryModuleMutation mutation)
    {
        switch (mutation)
        {
            case AddMemoryModuleMutation add:
                RejectValue(add.Command.Value);
                RejectSource(add.Command.Source);
                break;
            case ImportMemoryModuleMutation import:
                RejectSource(import.Command.Source);
                foreach (var record in import.Command.Records)
                {
                    RejectValue(record.Value);
                    foreach (var source in record.Sources)
                    {
                        RejectSource(source);
                    }
                }

                break;
            case SupersedeMemoryModuleMutation supersede:
                RejectValue(supersede.Command.NewValue);
                RejectSource(supersede.Command.Source);
                break;
            case RecordMemoryFeedbackModuleMutation feedback:
                RejectText(feedback.Command.Feedback, nameof(RecordMemoryFeedback.Feedback));
                RejectSource(feedback.Command.Source);
                break;
        }

        return mutation;
    }

    private static void RejectValue(StructuredValue value)
    {
        foreach (var pair in value.Properties)
        {
            if (ProhibitedKeys.Any(key => string.Equals(key, pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Memory Module 不允许保存完整模型思考链或内部推理轨迹。", nameof(value));
            }

            RejectValue(pair.Value);
        }

        foreach (var item in value.Items)
        {
            RejectValue(item);
        }
    }

    private static void RejectSource(MemorySourceRef? source)
    {
        if (source?.Snippet is { } snippet)
        {
            RejectText(snippet, nameof(MemorySourceRef.Snippet));
        }
    }

    private static void RejectText(string text, string paramName)
    {
        if (ProhibitedSnippetMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Memory Module 不允许保存完整模型思考链或内部推理轨迹。", paramName);
        }
    }
}
