using TianShu.Contracts.Orchestration;

namespace TianShu.Execution.Runtime;

/// <summary>
/// Stage Executor binding 描述符，声明一个 binding 可由哪个执行器实现承接。
/// Stage executor binding descriptor that declares which executor implementation handles a binding.
/// </summary>
public sealed record StageExecutorBindingDescriptor
{
    /// <summary>
    /// 初始化 Stage Executor binding 描述符。
    /// Initializes a stage executor binding descriptor.
    /// </summary>
    public StageExecutorBindingDescriptor(
        string binding,
        string implementationId,
        StageExecutorDispatchKind dispatchKind = StageExecutorDispatchKind.ModelTurn,
        IReadOnlyList<string>? stageIds = null)
    {
        Binding = NormalizeRequired(binding, nameof(binding));
        ImplementationId = NormalizeRequired(implementationId, nameof(implementationId));
        DispatchKind = dispatchKind;
        StageIds = NormalizeStageIds(stageIds, nameof(stageIds));
    }

    public string Binding { get; }

    public string ImplementationId { get; }

    public StageExecutorDispatchKind DispatchKind { get; }

    public IReadOnlyList<string> StageIds { get; }

    public bool MatchesStage(string stageId)
    {
        var normalizedStageId = NormalizeRequired(stageId, nameof(stageId));
        return StageIds.Count == 0
               || StageIds.Contains(normalizedStageId, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> NormalizeStageIds(IReadOnlyList<string>? values, string paramName)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<string>();
        }

        var normalized = values
            .Select(value => NormalizeRequired(value, paramName))
            .ToArray();
        if (normalized.Length != normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new ArgumentException("Stage Executor binding 的 Stage 列表不能包含重复项。", paramName);
        }

        return normalized;
    }

    private static string NormalizeRequired(string value, string paramName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Stage Executor binding 描述符标识不能为空。", paramName)
            : value.Trim();
}

/// <summary>
/// Stage Executor 分派计划，固定一次 Stage 执行应使用的 executor 实现。
/// Stage executor dispatch plan that pins the executor implementation for one stage execution.
/// </summary>
public sealed record StageExecutorDispatchPlan
{
    /// <summary>
    /// 初始化 Stage Executor 分派计划。
    /// Initializes a stage executor dispatch plan.
    /// </summary>
    public StageExecutorDispatchPlan(
        StageExecutionRequest executionRequest,
        StageExecutorRuntimeContext runtimeContext,
        StageExecutorBindingDescriptor descriptor)
    {
        ExecutionRequest = executionRequest ?? throw new ArgumentNullException(nameof(executionRequest));
        RuntimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public StageExecutionRequest ExecutionRequest { get; }

    public StageExecutorRuntimeContext RuntimeContext { get; }

    public StageExecutorBindingDescriptor Descriptor { get; }

    public string Binding => Descriptor.Binding;

    public string ImplementationId => Descriptor.ImplementationId;

    public StageExecutorDispatchKind DispatchKind => Descriptor.DispatchKind;
}

/// <summary>
/// Stage Executor 分派器，按 StageExecutionRequest.executor_binding 解析执行器实现。
/// Stage executor dispatcher that resolves executor implementations by StageExecutionRequest.executor_binding.
/// </summary>
public sealed class StageExecutorDispatcher
{
    public const string DefaultModelTurnImplementationId = "apphost.turn-runtime";

    private readonly IReadOnlyDictionary<string, StageExecutorBindingDescriptor> descriptorsByBinding;

    /// <summary>
    /// 使用一组 executor binding 描述符初始化分派器。
    /// Initializes the dispatcher with executor binding descriptors.
    /// </summary>
    public StageExecutorDispatcher(IEnumerable<StageExecutorBindingDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var mutableDescriptors = new Dictionary<string, StageExecutorBindingDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in descriptors)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            if (!mutableDescriptors.TryAdd(descriptor.Binding, descriptor))
            {
                throw new ArgumentException($"Stage Executor binding `{descriptor.Binding}` 被重复注册。", nameof(descriptors));
            }
        }

        descriptorsByBinding = mutableDescriptors;
    }

    /// <summary>
    /// 从当前可见 Stage 定义创建默认 dispatcher。
    /// Creates the default dispatcher from currently visible stage definitions.
    /// </summary>
    public static StageExecutorDispatcher FromStageDefinitions(
        IEnumerable<StageDefinition> stageDefinitions,
        string implementationId = DefaultModelTurnImplementationId)
    {
        ArgumentNullException.ThrowIfNull(stageDefinitions);

        var descriptors = stageDefinitions
            .GroupBy(static stage => stage.ExecutorBinding, StringComparer.OrdinalIgnoreCase)
            .Select(group => new StageExecutorBindingDescriptor(
                group.Key,
                implementationId,
                StageExecutorDispatchKind.ModelTurn,
                group.Select(static stage => stage.Id).ToArray()))
            .ToArray();
        return new StageExecutorDispatcher(descriptors);
    }

    /// <summary>
    /// 解析一次 Stage 执行的 executor binding，并返回固定的 dispatch plan。
    /// Resolves the executor binding for one stage execution and returns a pinned dispatch plan.
    /// </summary>
    public StageExecutorDispatchPlan Dispatch(
        StageExecutionRequest executionRequest,
        StageExecutorRuntimeContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(executionRequest);
        ArgumentNullException.ThrowIfNull(runtimeContext);

        if (!string.Equals(executionRequest.ExecutionId, runtimeContext.ExecutionId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Stage Executor runtime context 必须匹配 execution request。", nameof(runtimeContext));
        }

        if (!string.Equals(executionRequest.StageId, runtimeContext.StageId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(executionRequest.ExecutorBinding, runtimeContext.ExecutorBinding, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Stage Executor runtime context 的 Stage 或 binding 与 execution request 不一致。", nameof(runtimeContext));
        }

        if (!descriptorsByBinding.TryGetValue(executionRequest.ExecutorBinding, out var descriptor))
        {
            throw new InvalidOperationException($"Stage Executor binding `{executionRequest.ExecutorBinding}` 未注册。");
        }

        if (!descriptor.MatchesStage(executionRequest.StageId))
        {
            throw new InvalidOperationException(
                $"Stage Executor binding `{executionRequest.ExecutorBinding}` 未授权 Stage `{executionRequest.StageId}` 使用。");
        }

        var dispatchedContext = runtimeContext.WithExecutorDispatch(
            descriptor.ImplementationId,
            descriptor.DispatchKind);
        return new StageExecutorDispatchPlan(
            executionRequest,
            dispatchedContext,
            descriptor);
    }
}
