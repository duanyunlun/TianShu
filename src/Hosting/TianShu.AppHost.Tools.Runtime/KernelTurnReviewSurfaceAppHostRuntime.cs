using System.Collections.Concurrent;
using System.Text.Json;
using TianShu.AppHost.Configuration;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;
using TianShu.Execution.Runtime;
using TianShuPromptConfigUtilities = TianShu.Configuration.TianShuPromptConfigUtilities;
using static TianShu.AppHost.Tools.KernelReviewAppHostUtilities;
using static TianShu.AppHost.Tools.KernelToolJsonHelpers;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// `turn/steer` 与 `review/start` northbound surface 宿主运行时。
/// Host runtime for the `turn/steer` and `review/start` northbound surfaces.
/// </summary>
internal sealed class KernelTurnReviewSurfaceAppHostRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly KernelThreadManager threadManager;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> runningTurns;
    private readonly ConcurrentDictionary<string, Task> runningTurnTasks;
    private readonly Func<string> nextThreadId;
    private readonly Func<string> nextTurnId;
    private readonly Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync;
    private readonly Func<JsonElement?, int, string, object?, CancellationToken, Task> writeErrorWithDataAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Func<IReadOnlyList<JsonElement>, int> countInputTextChars;
    private readonly Func<IEnumerable<JsonElement>, string> extractUserTextFromInputItems;
    private readonly Action<string, string> enqueueSteerInput;
    private readonly Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession;
    private readonly Func<string, KernelThreadSessionState, string?, string, CancellationToken, Task<TurnRequestContext>> buildReviewTurnRequestContext;
    private readonly Func<string?, CancellationToken, Task<Dictionary<string, string>>> loadEffectiveConfigValuesAsync;
    private readonly Func<string?, Dictionary<string, object?>> readRuntimeConfig;
    private readonly Func<IReadOnlyList<string>, string, CancellationToken, Task<KernelReviewCommandResult>> executeReviewCommandAsync;
    private readonly Func<string, string, string, TurnRequestContext, bool, CancellationTokenSource, Task> runTurnAsync;
    private readonly Func<KernelThreadRecord, CancellationToken, Task> writeThreadStatusChangedAsync;
    private readonly Func<KernelThreadRecord, bool, object> toThreadPayload;
    private readonly int maxUserInputTextChars;
    private readonly string inputTooLargeErrorCode;

    public KernelTurnReviewSurfaceAppHostRuntime(
        KernelThreadStore threadStore,
        KernelThreadManager threadManager,
        ConcurrentDictionary<string, CancellationTokenSource> runningTurns,
        ConcurrentDictionary<string, Task> runningTurnTasks,
        Func<string> nextThreadId,
        Func<string> nextTurnId,
        Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync,
        Func<JsonElement?, int, string, object?, CancellationToken, Task> writeErrorWithDataAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Func<IReadOnlyList<JsonElement>, int> countInputTextChars,
        Func<IEnumerable<JsonElement>, string> extractUserTextFromInputItems,
        Action<string, string> enqueueSteerInput,
        Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession,
        Func<string, KernelThreadSessionState, string?, string, CancellationToken, Task<TurnRequestContext>> buildReviewTurnRequestContext,
        Func<string?, CancellationToken, Task<Dictionary<string, string>>> loadEffectiveConfigValuesAsync,
        Func<string?, Dictionary<string, object?>> readRuntimeConfig,
        Func<IReadOnlyList<string>, string, CancellationToken, Task<KernelReviewCommandResult>> executeReviewCommandAsync,
        Func<string, string, string, TurnRequestContext, bool, CancellationTokenSource, Task> runTurnAsync,
        Func<KernelThreadRecord, CancellationToken, Task> writeThreadStatusChangedAsync,
        Func<KernelThreadRecord, bool, object> toThreadPayload,
        int maxUserInputTextChars,
        string inputTooLargeErrorCode)
    {
        this.threadStore = threadStore;
        this.threadManager = threadManager;
        this.runningTurns = runningTurns;
        this.runningTurnTasks = runningTurnTasks;
        this.nextThreadId = nextThreadId;
        this.nextTurnId = nextTurnId;
        this.writeErrorAsync = writeErrorAsync;
        this.writeErrorWithDataAsync = writeErrorWithDataAsync;
        this.writeResultAsync = writeResultAsync;
        this.writeNotificationAsync = writeNotificationAsync;
        this.countInputTextChars = countInputTextChars;
        this.extractUserTextFromInputItems = extractUserTextFromInputItems;
        this.enqueueSteerInput = enqueueSteerInput;
        this.buildDefaultThreadSession = buildDefaultThreadSession;
        this.buildReviewTurnRequestContext = buildReviewTurnRequestContext;
        this.loadEffectiveConfigValuesAsync = loadEffectiveConfigValuesAsync;
        this.readRuntimeConfig = readRuntimeConfig;
        this.executeReviewCommandAsync = executeReviewCommandAsync;
        this.runTurnAsync = runTurnAsync;
        this.writeThreadStatusChangedAsync = writeThreadStatusChangedAsync;
        this.toThreadPayload = toThreadPayload;
        this.maxUserInputTextChars = maxUserInputTextChars;
        this.inputTooLargeErrorCode = inputTooLargeErrorCode;
    }

    public async Task HandleTurnSteerAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var threadId = ReadString(@params, "threadId");
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await writeErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var expectedTurnId = ReadString(@params, "expectedTurnId") ?? ReadString(@params, "turnId");
        if (string.IsNullOrWhiteSpace(expectedTurnId))
        {
            await writeErrorAsync(id, -32600, "expectedTurnId must not be empty", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TryReadInputArray(@params, out var inputItems) || inputItems.Count == 0)
        {
            await writeErrorAsync(id, -32600, "input must not be empty", cancellationToken).ConfigureAwait(false);
            return;
        }

        var inputChars = countInputTextChars(inputItems);
        if (inputChars > maxUserInputTextChars)
        {
            await writeErrorWithDataAsync(
                id,
                -32602,
                $"Input exceeds the maximum length of {maxUserInputTextChars} characters.",
                new
                {
                    input_error_code = inputTooLargeErrorCode,
                    max_chars = maxUserInputTextChars,
                    actual_chars = inputChars,
                },
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var activeTurnId = threadManager.TryGetThread(threadId, out var thread)
            ? thread?.ActiveTurnId
            : null;
        if (string.IsNullOrWhiteSpace(activeTurnId))
        {
            await writeErrorAsync(id, -32600, "no active turn to steer", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(activeTurnId, expectedTurnId, StringComparison.Ordinal))
        {
            await writeErrorAsync(
                id,
                -32600,
                $"expected active turn id `{expectedTurnId}` but found `{activeTurnId}`",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!runningTurns.ContainsKey(activeTurnId))
        {
            await writeErrorAsync(id, -32600, "no active turn to steer", cancellationToken).ConfigureAwait(false);
            return;
        }

        var steerText = extractUserTextFromInputItems(inputItems);
        if (!string.IsNullOrWhiteSpace(steerText))
        {
            enqueueSteerInput(activeTurnId, steerText);
        }

        await writeResultAsync(id, new
        {
            turnId = activeTurnId,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleReviewStartAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var sourceThreadId = ReadString(@params, "threadId");
        if (string.IsNullOrWhiteSpace(sourceThreadId))
        {
            await writeErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var sourceThread = await threadStore.GetThreadAsync(sourceThreadId, cancellationToken).ConfigureAwait(false);
        if (sourceThread is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{sourceThreadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var reviewCwd = Normalize(sourceThread.Cwd) ?? Environment.CurrentDirectory;
        var promptConfiguration = TianShuPromptConfigUtilities.FromConfig(readRuntimeConfig(reviewCwd));
        if (!TryBuildReviewPrompt(@params, promptConfiguration, out var reviewPrompt, out var reviewDisplayText, out var error))
        {
            await writeErrorAsync(id, -32600, error ?? "review target 无效。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var delivery = Normalize(ReadString(@params, "delivery")) ?? "inline";
        var reviewThreadId = sourceThreadId;
        KernelThreadRecord reviewThreadRecord = sourceThread;
        var isDetachedReview = string.Equals(delivery, "detached", StringComparison.OrdinalIgnoreCase);
        if (isDetachedReview)
        {
            var detachedThreadId = nextThreadId();
            var forked = await threadStore
                .ForkThreadAsync(
                    sourceThreadId,
                    detachedThreadId,
                    sourceThread.Cwd,
                    cancellationToken,
                    sourceThread.ConfigSnapshot?.Ephemeral == true)
                .ConfigureAwait(false);
            if (forked is null)
            {
                await writeErrorAsync(id, -32004, $"线程不存在：{sourceThreadId}", cancellationToken).ConfigureAwait(false);
                return;
            }

            reviewThreadId = forked.Id;
            reviewThreadRecord = forked;
            var detachedActiveRecord = await threadStore
                .SetThreadStatusAsync(reviewThreadId, "active", Array.Empty<string>(), cancellationToken)
                .ConfigureAwait(false);
            if (detachedActiveRecord is not null)
            {
                reviewThreadRecord = detachedActiveRecord;
            }

            var detachedSession = threadManager.TryGetThread(sourceThreadId, out var sourceRuntimeThread)
                && sourceRuntimeThread is not null
                ? sourceRuntimeThread.Session with
                {
                    Cwd = Normalize(reviewThreadRecord.Cwd) ?? sourceRuntimeThread.Session.Cwd,
                    SessionSource = KernelSessionSource.SubAgent(KernelSubAgentSource.Review),
                }
                : buildDefaultThreadSession(reviewThreadRecord) with
                {
                    SessionSource = KernelSessionSource.SubAgent(KernelSubAgentSource.Review),
                };
            _ = threadManager.AttachThread(reviewThreadRecord, detachedSession, loaded: true, publishCreated: true);
            await writeNotificationAsync("thread/started", new
            {
                thread = toThreadPayload(reviewThreadRecord, false),
            }, cancellationToken).ConfigureAwait(false);
        }
        else if (!string.Equals(delivery, "inline", StringComparison.OrdinalIgnoreCase))
        {
            await writeErrorAsync(id, -32600, $"invalid delivery: {delivery}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (reviewThreadRecord.IsArchived)
        {
            await writeErrorAsync(id, -32006, $"线程已归档，无法开始 review：{reviewThreadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        reviewCwd = Normalize(reviewThreadRecord.Cwd)
            ?? Normalize(sourceThread.Cwd)
            ?? Environment.CurrentDirectory;
        reviewPrompt = await EnrichReviewPromptWithTargetContextAsync(
                @params,
                reviewPrompt,
                promptConfiguration,
                reviewCwd!,
                executeReviewCommandAsync,
                cancellationToken)
            .ConfigureAwait(false);

        var turnId = nextTurnId();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runningTurns[turnId] = cts;
        var reviewThread = threadManager.GetOrAttachThread(reviewThreadRecord, buildDefaultThreadSession, loaded: true);
        reviewThread.SetActiveTurn(turnId);
        if (!isDetachedReview)
        {
            var activeRecord = await threadStore
                .SetThreadStatusAsync(reviewThreadId, "active", Array.Empty<string>(), cancellationToken)
                .ConfigureAwait(false);
            if (activeRecord is not null)
            {
                await writeThreadStatusChangedAsync(activeRecord, cancellationToken).ConfigureAwait(false);
            }
        }

        string? detachedReviewModel = null;
        if (isDetachedReview)
        {
            detachedReviewModel = await ResolveDetachedReviewModelAsync(
                    reviewCwd,
                    loadEffectiveConfigValuesAsync,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var turnContext = await buildReviewTurnRequestContext(
            reviewThreadId,
            reviewThread.Session,
            detachedReviewModel,
            reviewDisplayText,
            cancellationToken).ConfigureAwait(false);
        var task = Task.Run(() => runTurnAsync(
            reviewThreadId,
            turnId,
            reviewPrompt,
            turnContext,
            reviewThread.Session.PersistExtendedHistory,
            cts), CancellationToken.None);
        runningTurnTasks[turnId] = task;

        await writeResultAsync(id, new
        {
            turn = new
            {
                id = turnId,
                status = "inProgress",
                items = BuildReviewTurnItems(turnId, reviewDisplayText),
                error = (object?)null,
            },
            reviewThreadId,
        }, cancellationToken).ConfigureAwait(false);
    }
}
