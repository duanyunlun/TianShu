namespace TianShu.Execution.Runtime;

/// <summary>
/// Turn execution run request，承载一次 turn 执行所需的宿主上下文和通用分派上下文。
/// Turn execution run request carrying host context and generic dispatch context for one turn execution.
/// </summary>
public sealed record TurnExecutionRunRequest<TContext>
{
    /// <summary>
    /// 初始化 turn execution run request。
    /// Initializes the turn execution run request.
    /// </summary>
    public TurnExecutionRunRequest(
        string threadId,
        string turnId,
        string userText,
        TContext context,
        bool persistExtendedHistory,
        TurnExecutionDispatchContext dispatchContext)
    {
        ThreadId = NormalizeRequired(threadId, nameof(threadId));
        TurnId = NormalizeRequired(turnId, nameof(turnId));
        UserText = userText ?? string.Empty;
        Context = context;
        PersistExtendedHistory = persistExtendedHistory;
        DispatchContext = dispatchContext ?? throw new ArgumentNullException(nameof(dispatchContext));
    }

    /// <summary>
    /// 线程标识。
    /// Thread identifier.
    /// </summary>
    public string ThreadId { get; }

    /// <summary>
    /// Turn 标识。
    /// Turn identifier.
    /// </summary>
    public string TurnId { get; }

    /// <summary>
    /// 用户输入文本。
    /// User input text.
    /// </summary>
    public string UserText { get; }

    /// <summary>
    /// 宿主上下文。
    /// Host context.
    /// </summary>
    public TContext Context { get; }

    /// <summary>
    /// 是否持久化扩展历史。
    /// Whether extended history should be persisted.
    /// </summary>
    public bool PersistExtendedHistory { get; }

    /// <summary>
    /// 通用执行分派上下文。
    /// Generic execution dispatch context.
    /// </summary>
    public TurnExecutionDispatchContext DispatchContext { get; }

    private static string NormalizeRequired(string value, string paramName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Turn execution run request 标识不能为空。", paramName)
            : value.Trim();
}

/// <summary>
/// Turn execution run request 工厂，统一创建绑定分派上下文的 turn 执行请求。
/// Turn execution run request factory that creates turn execution requests bound to dispatch context.
/// </summary>
public static class TurnExecutionRunRequestFactory
{
    /// <summary>
    /// 创建 turn execution run request。
    /// Creates a turn execution run request.
    /// </summary>
    public static TurnExecutionRunRequest<TContext> Create<TContext>(
        string threadId,
        string turnId,
        string userText,
        TContext context,
        bool persistExtendedHistory,
        TurnExecutionDispatchContext dispatchContext)
        => new(
            threadId,
            turnId,
            userText,
            context,
            persistExtendedHistory,
            dispatchContext);
}

/// <summary>
/// Turn execution runner，按通用 turn 分派上下文执行 turn。
/// Turn execution runner that runs a turn by the generic turn dispatch context.
/// </summary>
public sealed class TurnExecutionRunner<TContext>
{
    private readonly StageExecutorRunner<TContext> innerRunner;

    /// <summary>
    /// 使用底层执行 runner 初始化通用 turn runner。
    /// Initializes the generic turn runner with the underlying execution runner.
    /// </summary>
    public TurnExecutionRunner(StageExecutorRunner<TContext> innerRunner)
    {
        this.innerRunner = innerRunner ?? throw new ArgumentNullException(nameof(innerRunner));
    }

    /// <summary>
    /// 执行一次已完成分派的 turn。
    /// Runs one dispatched turn.
    /// </summary>
    public Task RunAsync(
        TurnExecutionRunRequest<TContext> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var innerRequest = StageExecutorRunRequestFactory.Create(
            request.ThreadId,
            request.TurnId,
            request.UserText,
            request.Context,
            request.PersistExtendedHistory,
            request.DispatchContext.RuntimeContext);
        return innerRunner.RunAsync(innerRequest, cancellationToken);
    }
}

/// <summary>
/// Turn execution runner 工厂，统一创建内置 turn 执行组合。
/// Turn execution runner factory that centralizes built-in turn execution composition.
/// </summary>
public static class TurnExecutionRunnerFactory
{
    /// <summary>
    /// 创建默认模型 turn runner。
    /// Creates the default model-turn runner.
    /// </summary>
    public static TurnExecutionRunner<TContext> CreateDefaultModelTurnRunner<TContext>(
        Func<TurnExecutionRunRequest<TContext>, CancellationToken, Task> executeAsync)
    {
        ArgumentNullException.ThrowIfNull(executeAsync);

        return new TurnExecutionRunner<TContext>(
            StageExecutorRunnerFactory.CreateDefaultModelTurnRunner<TContext>(
                (request, cancellationToken) => executeAsync(
                    new TurnExecutionRunRequest<TContext>(
                        request.ThreadId,
                        request.TurnId,
                        request.UserText,
                        request.Context,
                        request.PersistExtendedHistory,
                        new TurnExecutionDispatchContext(request.RuntimeContext)),
                    cancellationToken)));
    }
}
