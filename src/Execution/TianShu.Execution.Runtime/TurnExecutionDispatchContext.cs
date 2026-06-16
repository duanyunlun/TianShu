using TianShu.Contracts.Orchestration;

namespace TianShu.Execution.Runtime;

/// <summary>
/// Turn execution dispatch context，封装一次已绑定执行入口的运行时上下文。
/// Turn execution dispatch context wrapping the runtime context for one bound execution entry.
/// </summary>
public sealed record TurnExecutionDispatchContext
{
    /// <summary>
    /// 初始化 turn execution dispatch context。
    /// Initializes the turn execution dispatch context.
    /// </summary>
    public TurnExecutionDispatchContext(StageExecutorRuntimeContext runtimeContext)
    {
        RuntimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
    }

    /// <summary>
    /// 当前执行入口所属 Stage 标识。
    /// Stage identifier for the current execution entry.
    /// </summary>
    public string StageId => RuntimeContext.StageId;

    /// <summary>
    /// 生成当前执行入口的编排决策标识。
    /// Orchestration decision identifier that produced the current execution entry.
    /// </summary>
    public string? DecisionId => RuntimeContext.DecisionId;

    /// <summary>
    /// 当前执行入口使用的上下文包标识。
    /// Context package identifier used by the current execution entry.
    /// </summary>
    public string? ContextPackageId => RuntimeContext.ContextPackageId;

    /// <summary>
    /// 当前执行入口的稳定执行请求标识。
    /// Stable execution request identifier for the current execution entry.
    /// </summary>
    public string ExecutionId => RuntimeContext.ExecutionId;

    /// <summary>
    /// 已解析的执行绑定名称。
    /// Resolved execution binding name.
    /// </summary>
    public string? Binding => RuntimeContext.ExecutorBinding;

    /// <summary>
    /// 已选中的执行实现标识。
    /// Selected execution implementation identifier.
    /// </summary>
    public string? ImplementationId => RuntimeContext.ExecutorImplementationId;

    /// <summary>
    /// 已选中的执行分派类型。
    /// Selected execution dispatch kind.
    /// </summary>
    public string? DispatchKind => RuntimeContext.ExecutorDispatchKind?.ToString();

    /// <summary>
    /// 当前执行入口开始时间。
    /// Start time of the current execution entry.
    /// </summary>
    public DateTimeOffset StartedAt => RuntimeContext.StartedAt;

    /// <summary>
    /// Execution Runtime 内部使用的底层运行时上下文。
    /// Underlying runtime context used internally by Execution Runtime.
    /// </summary>
    public StageExecutorRuntimeContext RuntimeContext { get; }

    /// <summary>
    /// 从 Stage Executor dispatch plan 创建通用 turn dispatch context。
    /// Creates the generic turn dispatch context from a stage-executor dispatch plan.
    /// </summary>
    public static TurnExecutionDispatchContext FromDispatchPlan(StageExecutorDispatchPlan dispatchPlan)
    {
        ArgumentNullException.ThrowIfNull(dispatchPlan);

        return new TurnExecutionDispatchContext(dispatchPlan.RuntimeContext);
    }
}
