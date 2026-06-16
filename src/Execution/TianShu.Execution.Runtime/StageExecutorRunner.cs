using TianShu.Contracts.Orchestration;

namespace TianShu.Execution.Runtime;

/// <summary>
/// Stage Executor 运行请求，承载一次已分派 Stage 执行所需的宿主上下文。
/// Stage executor run request carrying host context for one dispatched stage execution.
/// </summary>
public sealed record StageExecutorRunRequest<TContext>
{
    /// <summary>
    /// 初始化 Stage Executor 运行请求。
    /// Initializes a stage executor run request.
    /// </summary>
    public StageExecutorRunRequest(
        string threadId,
        string turnId,
        string userText,
        TContext context,
        bool persistExtendedHistory,
        StageExecutorRuntimeContext runtimeContext)
    {
        ThreadId = NormalizeRequired(threadId, nameof(threadId));
        TurnId = NormalizeRequired(turnId, nameof(turnId));
        UserText = userText ?? string.Empty;
        Context = context;
        PersistExtendedHistory = persistExtendedHistory;
        RuntimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
    }

    public string ThreadId { get; }

    public string TurnId { get; }

    public string UserText { get; }

    public TContext Context { get; }

    public bool PersistExtendedHistory { get; }

    public StageExecutorRuntimeContext RuntimeContext { get; }

    private static string NormalizeRequired(string value, string paramName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Stage Executor 运行请求标识不能为空。", paramName)
            : value.Trim();
}

/// <summary>
/// Stage Executor 运行请求工厂，统一创建已绑定运行上下文的执行请求。
/// Stage executor run request factory that creates execution requests bound to runtime context.
/// </summary>
public static class StageExecutorRunRequestFactory
{
    /// <summary>
    /// 创建 Stage Executor 运行请求。
    /// Creates a stage executor run request.
    /// </summary>
    public static StageExecutorRunRequest<TContext> Create<TContext>(
        string threadId,
        string turnId,
        string userText,
        TContext context,
        bool persistExtendedHistory,
        StageExecutorRuntimeContext runtimeContext)
    {
        return new StageExecutorRunRequest<TContext>(
            threadId,
            turnId,
            userText,
            context,
            persistExtendedHistory,
            runtimeContext);
    }
}

/// <summary>
/// Stage Executor implementation 描述符，绑定 implementation id 与实际执行委托。
/// Stage executor implementation descriptor that binds an implementation id to an execution delegate.
/// </summary>
public sealed record StageExecutorImplementation<TContext>
{
    /// <summary>
    /// 初始化 Stage Executor implementation 描述符。
    /// Initializes a stage executor implementation descriptor.
    /// </summary>
    public StageExecutorImplementation(
        string implementationId,
        StageExecutorDispatchKind dispatchKind,
        Func<StageExecutorRunRequest<TContext>, CancellationToken, Task> executeAsync)
    {
        ImplementationId = string.IsNullOrWhiteSpace(implementationId)
            ? throw new ArgumentException("Stage Executor implementation id 不能为空。", nameof(implementationId))
            : implementationId.Trim();
        DispatchKind = dispatchKind;
        ExecuteAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
    }

    public string ImplementationId { get; }

    public StageExecutorDispatchKind DispatchKind { get; }

    public Func<StageExecutorRunRequest<TContext>, CancellationToken, Task> ExecuteAsync { get; }
}

/// <summary>
/// Stage Executor 运行器，按 dispatch plan 固化的 implementation 执行 Stage。
/// Stage executor runner that executes a stage by the implementation pinned in the dispatch plan.
/// </summary>
public sealed class StageExecutorRunner<TContext>
{
    private readonly IReadOnlyDictionary<string, StageExecutorImplementation<TContext>> implementationsById;

    /// <summary>
    /// 使用一组 Stage Executor implementation 初始化运行器。
    /// Initializes the runner with stage executor implementations.
    /// </summary>
    public StageExecutorRunner(IEnumerable<StageExecutorImplementation<TContext>> implementations)
    {
        ArgumentNullException.ThrowIfNull(implementations);

        var mutableImplementations = new Dictionary<string, StageExecutorImplementation<TContext>>(StringComparer.OrdinalIgnoreCase);
        foreach (var implementation in implementations)
        {
            ArgumentNullException.ThrowIfNull(implementation);
            if (!mutableImplementations.TryAdd(implementation.ImplementationId, implementation))
            {
                throw new ArgumentException($"Stage Executor implementation `{implementation.ImplementationId}` 被重复注册。", nameof(implementations));
            }
        }

        implementationsById = mutableImplementations;
    }

    /// <summary>
    /// 执行一次已完成 dispatch 的 Stage。
    /// Executes one dispatched stage.
    /// </summary>
    public Task RunAsync(
        StageExecutorRunRequest<TContext> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var implementationId = request.RuntimeContext.ExecutorImplementationId;
        if (string.IsNullOrWhiteSpace(implementationId))
        {
            throw new InvalidOperationException("Stage Executor dispatch plan 缺少 implementation id。");
        }

        if (!implementationsById.TryGetValue(implementationId, out var implementation))
        {
            throw new InvalidOperationException($"Stage Executor implementation `{implementationId}` 未注册。");
        }

        if (request.RuntimeContext.ExecutorDispatchKind is { } dispatchKind
            && dispatchKind != implementation.DispatchKind)
        {
            throw new InvalidOperationException(
                $"Stage Executor implementation `{implementationId}` 的 dispatch kind 与运行上下文不一致。");
        }

        return implementation.ExecuteAsync(request, cancellationToken);
    }
}

/// <summary>
/// Stage Executor runner 工厂，统一创建内置 implementation 组合。
/// Stage executor runner factory that centralizes built-in implementation composition.
/// </summary>
public static class StageExecutorRunnerFactory
{
    /// <summary>
    /// 创建默认 model-turn Stage Executor runner。
    /// Creates the default model-turn Stage Executor runner.
    /// </summary>
    public static StageExecutorRunner<TContext> CreateDefaultModelTurnRunner<TContext>(
        Func<StageExecutorRunRequest<TContext>, CancellationToken, Task> executeAsync)
    {
        ArgumentNullException.ThrowIfNull(executeAsync);

        return new StageExecutorRunner<TContext>(
        [
            new StageExecutorImplementation<TContext>(
                StageExecutorDispatcher.DefaultModelTurnImplementationId,
                StageExecutorDispatchKind.ModelTurn,
                executeAsync),
        ]);
    }
}
