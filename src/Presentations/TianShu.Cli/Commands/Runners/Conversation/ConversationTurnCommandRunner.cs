using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using TianShu.ControlPlane;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.Events;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Governance;
using TianShu.RuntimeComposition;

namespace TianShu.Cli;

internal sealed class ConversationTurnCommandRunner
{
    private readonly Func<IExecutionRuntime> runtimeFactory;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public ConversationTurnCommandRunner()
        : this(TianShuAppHostRuntimeClientFactory.Create)
    {
    }

    internal ConversationTurnCommandRunner(Func<IExecutionRuntime> runtimeFactory)
    {
        this.runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
    }

    public async Task<int> RunFollowUpAsync(FollowUpCliCommandOptions options, CancellationToken cancellationToken)
    {
        ProbePermissionRequestScript? permissionScript = null;
        if (!string.IsNullOrWhiteSpace(options.PermissionsJsonPath))
        {
            permissionScript = ProbePermissionRequestScript.Load(options.PermissionsJsonPath);
        }

        ProbeUserInputScript? userInputScript = null;
        if (!string.IsNullOrWhiteSpace(options.UserInputJsonPath))
        {
            userInputScript = ProbeUserInputScript.Load(options.UserInputJsonPath);
        }

        if (options.KernelRuntimeLoop)
        {
            return await RunKernelRuntimeFollowUpAsync(options, cancellationToken).ConfigureAwait(false);
        }

        var bootstrap = CliRuntimeBootstrapper.Prepare(options);
        await using var runtime = runtimeFactory();
        var controlPlane = TianShuControlPlaneClientFactory.Create(runtime);
        var assistantText = new StringBuilder();
        var errors = new ConcurrentQueue<string>();
        var autoResponseTasks = new ConcurrentQueue<Task>();
        var assistantLineOpen = false;
        var sawErrorEvent = false;
        var approvalRequested = false;
        var approvalBlocked = false;
        var approvalAutoResponded = false;
        var permissionRequested = false;
        var permissionBlocked = false;
        var permissionAutoResponded = false;
        var userInputRequested = false;
        var userInputBlocked = false;
        var userInputAutoResponded = false;
        string? autoResponseFailureMessage = null;
        ControlPlaneTurnSubmissionResult? result = null;
        Exception? failure = null;

        using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var generatedCorrelationId = Guid.NewGuid().ToString("N");

        void CaptureAutoResponseFailure(string message)
        {
            if (Interlocked.CompareExchange(ref autoResponseFailureMessage, message, null) is not null)
            {
                return;
            }

            errors.Enqueue(message);
            sendCancellation.Cancel();
        }

        async Task AutoApproveAsync(ControlPlaneConversationStreamEvent streamEvent)
        {
            var pendingApproval = CliInteractiveStateConverters.ToPendingApprovalRequestState(streamEvent);
            if (pendingApproval is null || string.IsNullOrWhiteSpace(pendingApproval.CallId))
            {
                CaptureAutoResponseFailure("收到审批事件但缺少 callId，无法自动批准。");
                return;
            }

            try
            {
                var response = CliApprovalResponseResolver.BuildResolution(
                    pendingApproval.CallId,
                    pendingApproval,
                    options.ApprovalDecision,
                    "CLI 自动审批",
                    out _);
                var responded = await controlPlane.Governance.ResolveApprovalAsync(response, cancellationToken).ConfigureAwait(false);
                if (!responded)
                {
                    CaptureAutoResponseFailure($"自动批准失败：{pendingApproval.CallId}");
                    return;
                }

                approvalAutoResponded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                CaptureAutoResponseFailure($"自动批准异常：{ex.Message}");
            }
        }

        async Task AutoProvidePermissionGrantAsync(ControlPlaneConversationStreamEvent streamEvent)
        {
            if (permissionScript is null)
            {
                CaptureAutoResponseFailure("收到权限申请请求，但未配置 permissions-json。");
                return;
            }

            var callId = streamEvent.CallId?.Value;
            if (string.IsNullOrWhiteSpace(callId))
            {
                CaptureAutoResponseFailure("收到权限申请事件但缺少 callId，无法自动提交授权结果。");
                return;
            }

            if (!permissionScript.TryResolveResponse(callId, out var response))
            {
                CaptureAutoResponseFailure($"权限申请请求未匹配到响应：{callId}");
                return;
            }

            try
            {
                var responded = await controlPlane.Governance.ResolvePermissionRequestAsync(response, cancellationToken).ConfigureAwait(false);
                if (!responded)
                {
                    CaptureAutoResponseFailure($"自动提交权限授权结果失败：{callId}");
                    return;
                }

                permissionAutoResponded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                CaptureAutoResponseFailure($"自动提交权限授权结果异常：{ex.Message}");
            }
        }

        async Task AutoProvideUserInputAsync(ControlPlaneConversationStreamEvent streamEvent)
        {
            if (userInputScript is null)
            {
                CaptureAutoResponseFailure("收到用户补录请求，但未配置 user-input-json。");
                return;
            }

            var callId = streamEvent.CallId?.Value;
            if (string.IsNullOrWhiteSpace(callId))
            {
                CaptureAutoResponseFailure("收到用户补录事件但缺少 callId，无法自动提交答案。");
                return;
            }

            if (!userInputScript.TryResolveAnswers(callId, out var answers))
            {
                CaptureAutoResponseFailure($"用户补录请求未匹配到答案：{callId}");
                return;
            }

            try
            {
                var responded = await controlPlane.Governance.SubmitUserInputAsync(answers, cancellationToken).ConfigureAwait(false);
                if (!responded)
                {
                    CaptureAutoResponseFailure($"自动提交用户补录答案失败：{callId}");
                    return;
                }

                userInputAutoResponded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                CaptureAutoResponseFailure($"自动提交用户补录答案异常：{ex.Message}");
            }
        }

        runtime.StreamEventReceived += (_, args) =>
        {
            ControlPlaneConversationStreamEvent streamEvent = args.StreamEvent;
            switch (streamEvent.Kind)
            {
                case ControlPlaneConversationStreamEventKind.AssistantTextDelta:
                    if (!string.IsNullOrEmpty(streamEvent.Text))
                    {
                        assistantText.Append(streamEvent.Text);
                        if (!options.OutputJson)
                        {
                            Console.Write(streamEvent.Text);
                            assistantLineOpen = true;
                        }
                    }
                    break;
                case ControlPlaneConversationStreamEventKind.AssistantTextCompleted:
                    if (assistantLineOpen && !options.OutputJson)
                    {
                        Console.WriteLine();
                        assistantLineOpen = false;
                    }
                    break;
                case ControlPlaneConversationStreamEventKind.ApprovalRequested:
                    approvalRequested = true;
                    if (options.ApproveAll)
                    {
                        autoResponseTasks.Enqueue(AutoApproveAsync(streamEvent));
                    }
                    else
                    {
                        approvalBlocked = true;
                        errors.Enqueue("收到审批请求，但未启用 --approve-all。");
                        sendCancellation.Cancel();
                    }
                    break;
                case ControlPlaneConversationStreamEventKind.PermissionRequested:
                    permissionRequested = true;
                    if (permissionScript is not null)
                    {
                        autoResponseTasks.Enqueue(AutoProvidePermissionGrantAsync(streamEvent));
                    }
                    else
                    {
                        permissionBlocked = true;
                        errors.Enqueue("收到权限申请请求，但未提供 --permissions-json。");
                        sendCancellation.Cancel();
                    }
                    break;
                case ControlPlaneConversationStreamEventKind.UserInputRequested:
                    userInputRequested = true;
                    if (userInputScript is not null)
                    {
                        autoResponseTasks.Enqueue(AutoProvideUserInputAsync(streamEvent));
                    }
                    else
                    {
                        userInputBlocked = true;
                        errors.Enqueue("收到用户补录请求，但未提供 --user-input-json。");
                        sendCancellation.Cancel();
                    }
                    break;
                case ControlPlaneConversationStreamEventKind.Error:
                    if (streamEvent.WillRetry == true)
                    {
                        if (!options.OutputJson)
                        {
                            Console.Error.WriteLine(streamEvent.Message ?? streamEvent.Text ?? "收到可重试错误事件。");
                        }

                        break;
                    }

                    sawErrorEvent = true;
                    errors.Enqueue(streamEvent.Message ?? streamEvent.Text ?? "收到错误事件。");
                    break;
            }
        };

        try
        {
            await runtime.InitializeAsync(bootstrap.RuntimeOptions, dynamicToolCallHandler: null, cancellationToken).ConfigureAwait(false);
            var followUpResult = await controlPlane.Conversations.SubmitFollowUpAsync(
                    CliConversationEnvelopeFactory.Normalize(new ControlPlaneSubmitFollowUpCommand
                    {
                        Inputs = [new ControlPlaneTextInput(options.Message)],
                        Mode = options.Mode,
                        CorrelationId = generatedCorrelationId,
                    }),
                    sendCancellation.Token)
                .ConfigureAwait(false);
            result = followUpResult;
        }
        catch (OperationCanceledException) when (approvalBlocked || permissionBlocked || userInputBlocked || autoResponseFailureMessage is not null)
        {
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        try
        {
            await AwaitBackgroundTasksAsync(autoResponseTasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }

        failure ??= autoResponseFailureMessage is null ? null : new InvalidOperationException(autoResponseFailureMessage);
        if (failure is not null)
        {
            errors.Enqueue(failure is AppServerRpcException rpcException
                ? CliCommandFailureWriter.FormatText(rpcException)
                : failure.Message);
        }

        var finalAssistantText = assistantText.ToString();
        var hasErrors = sawErrorEvent || !errors.IsEmpty || result?.Accepted != true;
        var resolvedCorrelationId = result?.CorrelationId ?? generatedCorrelationId;
        if (options.OutputJson)
        {
            var sessionSnapshot = await CliSessionSnapshotUtilities.GetSnapshotAsync(runtime, cancellationToken).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                threadId = sessionSnapshot.ActiveThreadId?.Value,
                turnId = result?.TurnId?.Value,
                turnStatus = result?.TurnStatus,
                correlationId = resolvedCorrelationId,
                requestedMode = result?.RequestedMode?.ToString() ?? options.Mode.ToString(),
                effectiveMode = result?.EffectiveMode?.ToString(),
                message = result?.Message ?? failure?.Message,
                assistantText = finalAssistantText,
                approvalRequested,
                approvalBlocked,
                approvalAutoResponded,
                permissionRequested,
                permissionBlocked,
                permissionAutoResponded,
                userInputRequested,
                userInputBlocked,
                userInputAutoResponded,
                appServerError = failure is AppServerRpcException rpcException
                    ? CliCommandFailureWriter.BuildErrorEnvelope(rpcException)
                    : null,
                errors = errors.ToArray(),
            }, jsonOptions));
        }
        else
        {
            if (assistantLineOpen)
            {
                Console.WriteLine();
            }

            if (permissionRequested)
            {
                Console.WriteLine($"权限：{(permissionAutoResponded ? "已自动提交" : permissionBlocked ? "触发但未处理" : "已触发")}");
            }

            Console.WriteLine($"状态：{result?.TurnStatus ?? "failed"}");
            while (errors.TryDequeue(out var error))
            {
                Console.Error.WriteLine(error);
            }
        }

        return string.Equals(result?.TurnStatus, "completed", StringComparison.OrdinalIgnoreCase) && !hasErrors ? 0 : 1;
    }

    private static async Task AwaitBackgroundTasksAsync(ConcurrentQueue<Task> tasks)
    {
        if (tasks.IsEmpty)
        {
            return;
        }

        var snapshot = tasks.ToArray();
        if (snapshot.Length == 0)
        {
            return;
        }

        await Task.WhenAll(snapshot).ConfigureAwait(false);
    }

    private async Task<int> RunKernelRuntimeFollowUpAsync(FollowUpCliCommandOptions options, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(options.WorkingDirectory))
        {
            return WriteKernelRuntimeFollowUpResult(
                options,
                operation: "follow-up",
                status: "failed",
                threadId: options.ResumeThreadId,
                turnId: options.TurnId,
                failureCode: "working_directory_not_found",
                failureMessage: $"工作目录不存在：{options.WorkingDirectory}",
                payload: null);
        }

        if (string.IsNullOrWhiteSpace(options.ResumeThreadId))
        {
            return WriteKernelRuntimeFollowUpResult(
                options,
                operation: "follow-up",
                status: "failed",
                threadId: null,
                turnId: options.TurnId,
                failureCode: "kernel_runtime_followup_thread_missing",
                failureMessage: "Kernel runtime follow-up 需要 --resume-thread-id 指定目标线程。",
                payload: null);
        }

        try
        {
            var loader = new RuntimeConfigurationComposition();
            var resolvedConfig = loader.Load(
                options.ConfigFilePath,
                options.ProfileName,
                options.ConfigOverrides,
                options.WorkingDirectory);
            var runtimeWriteCheck = CliRuntimeWriteGuard.CheckKernelRuntimeWorkspace(
                resolvedConfig.UserConfigPath,
                options.WorkingDirectory);
            if (!runtimeWriteCheck.Available)
            {
                return WriteKernelRuntimeFollowUpResult(
                    options,
                    operation: "follow-up",
                    status: "failed",
                    threadId: options.ResumeThreadId,
                    turnId: options.TurnId,
                    failureCode: runtimeWriteCheck.FailureCode,
                    failureMessage: runtimeWriteCheck.FailureMessage,
                    payload: null);
            }

            if (!string.IsNullOrWhiteSpace(options.CheckpointRef))
            {
                var resume = await KernelRuntimeTurnLoopBridge.RunResumeAsync(
                        new KernelRuntimeResumeRequest(
                            options.ResumeThreadId,
                            options.TurnId,
                            string.IsNullOrWhiteSpace(options.ResumeToken) ? $"resume-token-cli-{Guid.NewGuid():N}" : options.ResumeToken!,
                            options.CheckpointRef!,
                            options.WorkingDirectory,
                            resolvedConfig),
                        cancellationToken)
                    .ConfigureAwait(false);

                return WriteKernelRuntimeFollowUpResult(
                    options,
                    resume.Operation,
                    resume.Status,
                    resume.TerminalProjection.ThreadId,
                    resume.TerminalProjection.TurnId,
                    resume.FailureCode,
                    resume.FailureMessage,
                    new
                    {
                        resume.Operation,
                        resume.Status,
                        resume.FailureCode,
                        resume.FailureMessage,
                        resume.TerminalProjection,
                        resume.ReplaySummary,
                        resume.DiagnosticsProjection,
                    });
            }

            if (options.Mode == ControlPlaneFollowUpMode.Interrupt)
            {
                var interrupt = await KernelRuntimeTurnLoopBridge.RunInterruptAsync(
                        new KernelRuntimeInterruptRequest(
                            options.ResumeThreadId,
                            options.TurnId,
                            string.IsNullOrWhiteSpace(options.Message) ? "user.cancel" : options.Message,
                            options.WorkingDirectory,
                            resolvedConfig),
                        cancellationToken)
                    .ConfigureAwait(false);

                return WriteKernelRuntimeFollowUpResult(
                    options,
                    interrupt.Operation,
                    interrupt.Status,
                    interrupt.TerminalProjection.ThreadId,
                    interrupt.TerminalProjection.TurnId,
                    interrupt.FailureCode,
                    interrupt.FailureMessage,
                    new
                    {
                        interrupt.Operation,
                        interrupt.Status,
                        interrupt.FailureCode,
                        interrupt.FailureMessage,
                        interrupt.TerminalProjection,
                        interrupt.ReplaySummary,
                        interrupt.DiagnosticsProjection,
                    });
            }

            if (options.Mode == ControlPlaneFollowUpMode.Steer)
            {
                var enableWorkspaceWrite = options.ApproveAll && options.ApprovalDecision == ControlPlaneApprovalDecision.Approve;
                var enableShell = options.EnableShell && enableWorkspaceWrite;
                var steer = await KernelRuntimeTurnLoopBridge.RunSteerAsync(
                        new KernelRuntimeSteerRequest(
                            options.ResumeThreadId,
                            options.TurnId,
                            options.Message,
                            [options.Message],
                            options.WorkingDirectory,
                            options.TurnTimeoutSeconds,
                            resolvedConfig,
                            EnableWorkspaceWrite: enableWorkspaceWrite,
                            WorkspaceWriteApprovalId: enableWorkspaceWrite
                                ? new ApprovalId($"approval-cli-kernel-followup-workspace-write-{Guid.NewGuid():N}")
                                : null,
                            EnableShell: enableShell,
                            EnableMcp: options.EnableMcp,
                            EnableMemory: options.EnableMemory),
                        cancellationToken)
                    .ConfigureAwait(false);

                return WriteKernelRuntimeFollowUpResult(
                    options,
                    operation: "steer",
                    status: steer.TurnStatus,
                    threadId: steer.ThreadId,
                    turnId: steer.TurnId,
                    failureCode: steer.TurnStatus == "completed" ? null : "kernel_runtime_steer_failed",
                    failureMessage: steer.TurnStatus == "completed" ? null : steer.ResultText,
                    payload: new
                    {
                        operation = "steer",
                        status = steer.TurnStatus,
                        steer.SendResult,
                        steer.ResultText,
                        steer.TerminalProjection,
                        steer.ReplaySummary,
                        steer.DiagnosticsProjection,
                    });
            }

            return WriteKernelRuntimeFollowUpResult(
                options,
                operation: "follow-up",
                status: "failed",
                threadId: options.ResumeThreadId,
                turnId: options.TurnId,
                failureCode: "kernel_runtime_followup_mode_unsupported",
                failureMessage: "Kernel runtime follow-up 当前只支持 --mode steer、--mode interrupt，或通过 --checkpoint-ref 执行 resume。",
                payload: null);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or FormatException)
        {
            return WriteKernelRuntimeFollowUpResult(
                options,
                operation: "follow-up",
                status: "failed",
                threadId: options.ResumeThreadId,
                turnId: options.TurnId,
                failureCode: "kernel_runtime_followup_failed",
                failureMessage: ex.Message,
                payload: null);
        }
    }

    private int WriteKernelRuntimeFollowUpResult(
        FollowUpCliCommandOptions options,
        string operation,
        string status,
        string? threadId,
        string? turnId,
        string? failureCode,
        string? failureMessage,
        object? payload)
    {
        var success = string.IsNullOrWhiteSpace(failureCode)
            && !string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);
        if (options.OutputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                executionPath = "kernel-runtime-loop",
                operation,
                status,
                threadId,
                turnId,
                turnStatus = status,
                requestedMode = options.Mode.ToString(),
                message = failureMessage,
                failureCode,
                kernelRuntime = payload,
                errors = string.IsNullOrWhiteSpace(failureMessage)
                    ? Array.Empty<string>()
                    : [failureMessage],
            }, jsonOptions));
        }
        else
        {
            Console.WriteLine($"路径：kernel-runtime-loop");
            Console.WriteLine($"操作：{operation}");
            Console.WriteLine($"状态：{status}");
            if (!string.IsNullOrWhiteSpace(failureMessage))
            {
                Console.Error.WriteLine(failureMessage);
            }
        }

        return success ? 0 : 1;
    }
}
