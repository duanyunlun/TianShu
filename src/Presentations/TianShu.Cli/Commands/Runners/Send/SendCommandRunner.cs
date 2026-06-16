using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TianShu.AppHost.Configuration;
using TianShu.Configuration;
using TianShu.ControlPlane;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.Diagnostics;
using TianShu.Execution.Runtime.Events;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;
using TianShu.Provider.Abstractions;
using TianShu.RuntimeComposition;
using TianShu.SubAgent;

namespace TianShu.Cli;

internal sealed class SendCommandRunner
{
    private readonly Func<IExecutionRuntime> runtimeFactory;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public SendCommandRunner()
        : this(TianShuAppHostRuntimeClientFactory.Create)
    {
    }

    internal SendCommandRunner(Func<IExecutionRuntime> runtimeFactory)
    {
        this.runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
    }

    private ControlPlaneInitializeRuntimeCommand BuildRuntimeOptions(SendCommandOptions probeOptions)
        => new()
        {
            ExecutablePath = "dotnet",
            UseDotNetProjectLauncher = true,
            WorkingDirectory = probeOptions.WorkingDirectory,
            ConfigFilePath = probeOptions.ConfigFilePath,
            ProfileName = probeOptions.ProfileName,
            ConfigOverrides = probeOptions.ConfigOverrides,
            ResumeThreadId = probeOptions.ResumeThreadId,
            ResumeLatestThread = probeOptions.ResumeLatestThread,
            ResumeLatestMatchCwd = probeOptions.ResumeLatestMatchCwd,
            CollaborationMode = probeOptions.CollaborationMode,
            SessionSource = ControlPlaneSessionSource.Cli,
            DynamicTools = probeOptions.DynamicTools,
            StartupTimeout = TimeSpan.FromSeconds(45),
            TurnTimeout = TimeSpan.FromSeconds(probeOptions.TurnTimeoutSeconds),
        };

    public async Task<SendCommandRunResult> RunAsync(SendCommandOptions probeOptions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probeOptions);

        var startedAt = DateTimeOffset.Now;
        var events = new ConcurrentQueue<ProbeEventRecord>();
        var assistantText = new StringBuilder();
        var approvalRequested = false;
        var approvalBlocked = false;
        var permissionRequested = false;
        var permissionBlocked = false;
        var userInputRequested = false;
        var userInputBlocked = false;
        var sawErrorEvent = false;
        var sawTerminalErrorEvent = false;
        var resumedExistingThread = false;
        string? autoResponseFailureMessage = null;
        string? completedAssistantText = null;
        string? activeThreadId = null;
        string? activeTurnId = null;
        string? finalTurnStatus = null;
        string? resolvedAppHostProjectPath = null;
        ResolvedTianShuConfig? resolvedConfig = null;
        ProbePermissionRequestScript? permissionScript = null;
        ProbeUserInputScript? userInputScript = null;
        ControlPlaneTurnSubmissionResult? sendResult = null;
        Exception? failure = null;

        if (!Directory.Exists(probeOptions.WorkingDirectory))
        {
            return await BuildTerminalResultAsync(
                    probeOptions,
                    startedAt,
                    events,
                    assistantText: null,
                    exitCode: SendCommandExitCode.InvalidArguments,
                    runtimeOptions: null,
                    resolvedConfig: null,
                    resolvedAppHostProjectPath: null,
                    threadId: null,
                    turnId: null,
                    turnStatus: null,
                    approvalRequested: false,
                    permissionRequested: false,
                    userInputRequested: false,
                    sawErrorEvent: false,
                    resumedExistingThread: false,
                    sendResult: null,
                    failureMessage: $"工作目录不存在：{probeOptions.WorkingDirectory}",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(probeOptions.PermissionsJsonPath))
        {
            try
            {
                permissionScript = ProbePermissionRequestScript.Load(probeOptions.PermissionsJsonPath);
            }
            catch (Exception ex) when (ex is FileNotFoundException or FormatException or JsonException)
            {
                return await BuildTerminalResultAsync(
                        probeOptions,
                        startedAt,
                        events,
                        assistantText: null,
                        exitCode: SendCommandExitCode.InvalidArguments,
                        runtimeOptions: null,
                        resolvedConfig: null,
                        resolvedAppHostProjectPath: null,
                        threadId: null,
                        turnId: null,
                        turnStatus: null,
                        approvalRequested: false,
                        permissionRequested: false,
                        userInputRequested: false,
                        sawErrorEvent: false,
                        resumedExistingThread: false,
                        sendResult: null,
                        failureMessage: ex.Message,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (!string.IsNullOrWhiteSpace(probeOptions.UserInputJsonPath))
        {
            try
            {
                userInputScript = ProbeUserInputScript.Load(probeOptions.UserInputJsonPath);
            }
            catch (Exception ex) when (ex is FileNotFoundException or FormatException or JsonException)
            {
                return await BuildTerminalResultAsync(
                        probeOptions,
                        startedAt,
                        events,
                        assistantText: null,
                        exitCode: SendCommandExitCode.InvalidArguments,
                        runtimeOptions: null,
                        resolvedConfig: null,
                        resolvedAppHostProjectPath: null,
                        threadId: null,
                        turnId: null,
                        turnStatus: null,
                        approvalRequested: false,
                        permissionRequested: false,
                        userInputRequested: false,
                        sawErrorEvent: false,
                        resumedExistingThread: false,
                        sendResult: null,
                        failureMessage: ex.Message,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var runtimeOptions = BuildRuntimeOptions(probeOptions);
        var turnPathDecision = KernelRuntimeTurnPathSelector.Decide(
            KernelRuntimeTurnPathRequest.ForCliSend(
                explicitKernelRuntimeLoopRequested: probeOptions.KernelRuntimeLoop,
                defaultKernelRuntimeLoopEnabled: true));

        try
        {
            if (turnPathDecision.PathKind == KernelRuntimeTurnPathKind.FailClosed)
            {
                return await BuildTerminalResultAsync(
                        probeOptions,
                        startedAt,
                        events,
                        assistantText: null,
                        exitCode: SendCommandExitCode.SendFailed,
                        runtimeOptions: runtimeOptions,
                        resolvedConfig: null,
                        resolvedAppHostProjectPath: null,
                        threadId: null,
                        turnId: null,
                        turnStatus: null,
                        approvalRequested: false,
                        permissionRequested: false,
                        userInputRequested: false,
                        sawErrorEvent: false,
                        resumedExistingThread: false,
                        sendResult: null,
                        failureMessage: turnPathDecision.FailureCode,
                        cancellationToken: cancellationToken,
                        turnPathDecision: turnPathDecision)
                    .ConfigureAwait(false);
            }

            var loader = new RuntimeConfigurationComposition();
            _ = CliFirstRunBootstrapper.EnsureDefaultConfiguration(runtimeOptions.ConfigFilePath);
            resolvedConfig = loader.Load(runtimeOptions.ConfigFilePath, runtimeOptions.ProfileName, runtimeOptions.ConfigOverrides, runtimeOptions.WorkingDirectory);
            RuntimeConfigurationComposition.ApplyToOptions(runtimeOptions, resolvedConfig);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or FormatException)
        {
            return await BuildTerminalResultAsync(
                    probeOptions,
                    startedAt,
                    events,
                    assistantText: null,
                    exitCode: SendCommandExitCode.InvalidConfig,
                    runtimeOptions: runtimeOptions,
                    resolvedConfig: resolvedConfig,
                    resolvedAppHostProjectPath: resolvedAppHostProjectPath,
                    threadId: null,
                    turnId: null,
                    turnStatus: null,
                    approvalRequested: false,
                    permissionRequested: false,
                    userInputRequested: false,
                    sawErrorEvent: false,
                    resumedExistingThread: false,
                    sendResult: null,
                    failureMessage: ex.Message,
                    cancellationToken: cancellationToken,
                    turnPathDecision: turnPathDecision)
                .ConfigureAwait(false);
        }

        if (turnPathDecision.UseKernelRuntimeLoop)
        {
            KernelRuntimeTurnLoopResult? kernelRuntimeLoopResult = null;
            var enableWorkspaceWrite = probeOptions.ApproveAll && IsApprovalDecisionGranted(probeOptions.ApprovalDecision);
            var enableSubAgents = probeOptions.EnableSubAgents && enableWorkspaceWrite;
            try
            {
                await using var subAgentChildRuntime = enableSubAgents
                    ? KernelRuntimeTurnLoopComposition.CreateRuntime(
                        resolvedConfig!,
                        includeWorkspaceWrite: enableWorkspaceWrite)
                    : null;
                var subAgentModules = enableSubAgents
                    ? new Dictionary<string, ISubAgentModule>(StringComparer.Ordinal)
                    {
                        ["module.sub_agent"] = new SubAgentOrchestrationModule(
                            KernelRuntimeTurnLoopComposition.CreateExecutionLoop(subAgentChildRuntime!)),
                    }
                    : null;
                kernelRuntimeLoopResult = await KernelRuntimeTurnLoopBridge.RunAsync(
                        new KernelRuntimeTurnLoopRequest(
                            probeOptions.Message,
                            runtimeOptions.WorkingDirectory,
                            runtimeOptions.ResumeThreadId,
                            probeOptions.TurnTimeoutSeconds,
                            resolvedConfig!,
                            enableWorkspaceWrite,
                            enableWorkspaceWrite
                                ? new ApprovalId($"approval-cli-kernel-workspace-write-{Guid.NewGuid():N}")
                                : null,
                            EnableSubAgents: enableSubAgents,
                            SubAgentApprovalId: enableSubAgents
                                ? new ApprovalId($"approval-cli-kernel-subagent-{Guid.NewGuid():N}")
                                : null,
                            SubAgentModules: subAgentModules),
                        cancellationToken)
                    .ConfigureAwait(false);
                sendResult = kernelRuntimeLoopResult.SendResult;
                activeThreadId = kernelRuntimeLoopResult.ThreadId;
                activeTurnId = kernelRuntimeLoopResult.TurnId;
                finalTurnStatus = kernelRuntimeLoopResult.TurnStatus;
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            var kernelRuntimeExitCode = ResolveExitCode(
                permissionBlocked,
                approvalBlocked,
                userInputBlocked,
                sawTerminalErrorEvent,
                sendResult,
                failure,
                completedAssistantText,
                assistantText,
                autoResponseFailureMessage);
            var kernelRuntimeFailureMessage = failure?.Message
                ?? ResolveTurnFailureMessage(kernelRuntimeExitCode, finalTurnStatus, sendResult);

            return await BuildTerminalResultAsync(
                    probeOptions,
                    startedAt,
                    events,
                    kernelRuntimeLoopResult?.ResultText,
                    kernelRuntimeExitCode,
                    runtimeOptions,
                    resolvedConfig,
                    resolvedAppHostProjectPath,
                    activeThreadId,
                    activeTurnId,
                    finalTurnStatus,
                    approvalRequested,
                    permissionRequested,
                    userInputRequested,
                    sawErrorEvent,
                    resumedExistingThread,
                    sendResult,
                    kernelRuntimeFailureMessage,
                    cancellationToken,
                    kernelRuntimeLoopResult: kernelRuntimeLoopResult,
                    failureException: failure,
                    turnPathDecision: turnPathDecision)
                .ConfigureAwait(false);
        }

        return await BuildTerminalResultAsync(
                probeOptions,
                startedAt,
                events,
                assistantText: null,
                exitCode: SendCommandExitCode.SendFailed,
                runtimeOptions: runtimeOptions,
                resolvedConfig: resolvedConfig,
                resolvedAppHostProjectPath: resolvedAppHostProjectPath,
                threadId: null,
                turnId: null,
                turnStatus: null,
                approvalRequested: false,
                permissionRequested: false,
                userInputRequested: false,
                sawErrorEvent: false,
                resumedExistingThread: false,
                sendResult: null,
                failureMessage: turnPathDecision.FailureCode ?? "kernel_runtime_path_unavailable",
                cancellationToken: cancellationToken,
                turnPathDecision: turnPathDecision)
            .ConfigureAwait(false);
    }
    private static bool IsApprovalDecisionGranted(ControlPlaneApprovalDecision decision)
        => decision is ControlPlaneApprovalDecision.Approve
            or ControlPlaneApprovalDecision.ApproveForSession
            or ControlPlaneApprovalDecision.ApproveAndRemember
            or ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment;

    private async Task<SendCommandRunResult> BuildTerminalResultAsync(
        SendCommandOptions probeOptions,
        DateTimeOffset startedAt,
        IEnumerable<ProbeEventRecord> events,
        string? assistantText,
        SendCommandExitCode exitCode,
        ControlPlaneInitializeRuntimeCommand? runtimeOptions,
        ResolvedTianShuConfig? resolvedConfig,
        string? resolvedAppHostProjectPath,
        string? threadId,
        string? turnId,
        string? turnStatus,
        bool approvalRequested,
        bool permissionRequested,
        bool userInputRequested,
        bool sawErrorEvent,
        bool resumedExistingThread,
        ControlPlaneTurnSubmissionResult? sendResult,
        string? failureMessage,
        CancellationToken cancellationToken,
        bool approvalBlocked = false,
        bool approvalAutoResponded = false,
        bool permissionBlocked = false,
        bool permissionAutoResponded = false,
        bool userInputBlocked = false,
        bool userInputAutoResponded = false,
        string? autoResponseFailureMessage = null,
        KernelRuntimeTurnLoopResult? kernelRuntimeLoopResult = null,
        Exception? failureException = null,
        KernelRuntimeTurnPathDecision? turnPathDecision = null)
    {
        var completedAt = DateTimeOffset.Now;
        var eventList = events.ToArray();
        var finalResultText = string.IsNullOrWhiteSpace(assistantText)
            ? failureMessage ?? sendResult?.Message ?? string.Empty
            : assistantText;

        var summary = new ProbeSummary
        {
            Success = exitCode == SendCommandExitCode.Success,
            ExitCode = (int)exitCode,
            ExitCodeName = exitCode.ToString(),
            StartedAt = startedAt,
            CompletedAt = completedAt,
            DurationMs = Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds),
            WorkingDirectory = probeOptions.WorkingDirectory,
            Message = probeOptions.Message,
            ConfigFilePath = runtimeOptions?.ConfigFilePath ?? probeOptions.ConfigFilePath,
            ProfileName = runtimeOptions?.ProfileName ?? probeOptions.ProfileName,
            ApproveAll = probeOptions.ApproveAll,
            PermissionsJsonPath = probeOptions.PermissionsJsonPath,
            UserInputJsonPath = probeOptions.UserInputJsonPath,
            CollaborationMode = runtimeOptions?.CollaborationMode ?? probeOptions.CollaborationMode,
            RequestedResumeThreadId = runtimeOptions?.ResumeThreadId,
            ResumeLatestThread = runtimeOptions?.ResumeLatestThread ?? false,
            ResumeLatestMatchCwd = runtimeOptions?.ResumeLatestMatchCwd ?? true,
            ReusedExistingThread = resumedExistingThread,
            AppHostProjectPath = resolvedAppHostProjectPath,
            ThreadId = threadId,
            TurnId = turnId ?? sendResult?.TurnId?.Value,
            TurnStatus = turnStatus ?? sendResult?.TurnStatus,
            ApprovalRequested = approvalRequested,
            ApprovalBlocked = approvalBlocked,
            ApprovalAutoResponded = approvalAutoResponded,
            PermissionRequested = permissionRequested,
            PermissionBlocked = permissionBlocked,
            PermissionAutoResponded = permissionAutoResponded,
            UserInputRequested = userInputRequested,
            UserInputBlocked = userInputBlocked,
            UserInputAutoResponded = userInputAutoResponded,
            ErrorEventObserved = sawErrorEvent,
            AutoResponseFailureMessage = autoResponseFailureMessage,
            EventCount = eventList.Length,
            Model = runtimeOptions?.Model ?? resolvedConfig?.Model,
            ModelProvider = runtimeOptions?.ModelProvider ?? resolvedConfig?.ModelProvider,
            ProtocolAdapter = runtimeOptions?.ProtocolAdapter ?? resolvedConfig?.ProtocolAdapter,
            ResultText = finalResultText,
            FailureMessage = exitCode == SendCommandExitCode.Success ? null : failureMessage,
            ExecutionPath = turnPathDecision?.ExecutionPath ?? "kernel-runtime-loop",
            FallbackReason = turnPathDecision?.FallbackReason,
            FailureCode = turnPathDecision?.FailureCode,
            RequiredCapabilities = turnPathDecision?.RequiredCapabilities.Select(static item => item.ToString()).ToArray(),
            LegacyFallbackCapabilities = turnPathDecision?.LegacyFallbackCapabilities.Select(static item => item.ToString()).ToArray(),
            KernelRuntimeDisposition = kernelRuntimeLoopResult?.ExecutionResult.Disposition.ToString(),
            KernelRunId = kernelRuntimeLoopResult?.ExecutionResult.KernelResult.RunId.Value,
            KernelTraceId = kernelRuntimeLoopResult?.ExecutionResult.KernelTraceId?.Value,
            RuntimeExecutionId = kernelRuntimeLoopResult?.ExecutionResult.RuntimeResult?.ExecutionId.Value,
            RuntimeTraceRef = kernelRuntimeLoopResult?.ExecutionResult.RuntimeTraceRef,
            RuntimeDiagnosticsRef = kernelRuntimeLoopResult?.ExecutionResult.DiagnosticsRef,
            ReplayCompleteness = kernelRuntimeLoopResult?.ReplaySummary.Completeness,
            StagePath = kernelRuntimeLoopResult?.ReplaySummary.StagePath,
            RuntimeExecutionStepCount = kernelRuntimeLoopResult?.ReplaySummary.Steps.Count,
            ReplaySummary = kernelRuntimeLoopResult?.ReplaySummary,
            RuntimeDiagnosticsProjection = kernelRuntimeLoopResult?.DiagnosticsProjection,
            KernelRuntimeTerminalProjection = kernelRuntimeLoopResult?.TerminalProjection,
            AppServerError = failureException is AppServerRpcException rpcException
                ? JsonSerializer.SerializeToElement(CliCommandFailureWriter.BuildErrorEnvelope(rpcException), jsonOptions)
                : null,
        };

        var resolvedOptions = ProbeResolvedOptions.FromRuntimeOptions(
            runtimeOptions,
            resolvedConfig,
            resolvedAppHostProjectPath,
            probeOptions.ArtifactsRoot,
            probeOptions.ApproveAll,
            probeOptions.PermissionsJsonPath,
            probeOptions.UserInputJsonPath);
        var writer = new SendCommandArtifactsWriter(jsonOptions);
        var artifactResult = await writer.WriteAsync(probeOptions, summary, resolvedOptions, eventList, probeOptions.Message, finalResultText, cancellationToken).ConfigureAwait(false);
        summary.ArtifactsDirectory = artifactResult.RunDirectory;
        await writer.RewriteSummaryAsync(artifactResult.RunDirectory, summary, cancellationToken).ConfigureAwait(false);

        return new SendCommandRunResult(summary, jsonOptions);
    }

    private static SendCommandExitCode ResolveExitCode(
        bool permissionBlocked,
        bool approvalBlocked,
        bool userInputBlocked,
        bool sawErrorEvent,
        ControlPlaneTurnSubmissionResult? sendResult,
        Exception? failure,
        string? completedAssistantText,
        StringBuilder assistantText,
        string? autoResponseFailureMessage)
    {
        if (!string.IsNullOrWhiteSpace(autoResponseFailureMessage))
        {
            return SendCommandExitCode.SendFailed;
        }

        if (approvalBlocked || permissionBlocked || userInputBlocked)
        {
            return SendCommandExitCode.ApprovalOrInputRequired;
        }

        if (failure is not null)
        {
            return SendCommandExitCode.SendFailed;
        }

        if (sendResult is null)
        {
            return SendCommandExitCode.SendFailed;
        }

        if (!sendResult.Accepted)
        {
            if (!string.IsNullOrWhiteSpace(sendResult.TurnStatus))
            {
                return SendCommandExitCode.TurnFailed;
            }

            return sawErrorEvent ? SendCommandExitCode.TurnFailed : SendCommandExitCode.SendFailed;
        }

        if (sawErrorEvent)
        {
            return SendCommandExitCode.TurnFailed;
        }

        if (IsCompletedTurnStatus(sendResult.TurnStatus))
        {
            return SendCommandExitCode.Success;
        }

        if (!string.IsNullOrWhiteSpace(sendResult.TurnStatus))
        {
            return SendCommandExitCode.TurnFailed;
        }

        var finalText = ResolveFinalAssistantText(completedAssistantText, assistantText, sendResult);
        return !string.IsNullOrWhiteSpace(finalText)
            ? SendCommandExitCode.Success
            : SendCommandExitCode.TurnFailed;
    }

    private static string? ResolveTurnFailureMessage(
        SendCommandExitCode exitCode,
        string? finalTurnStatus,
        ControlPlaneTurnSubmissionResult? sendResult)
    {
        if (sendResult is null)
        {
            return null;
        }

        if (exitCode == SendCommandExitCode.TurnFailed)
        {
            var status = sendResult.TurnStatus ?? finalTurnStatus;
            if (string.Equals(status, "inProgress", StringComparison.OrdinalIgnoreCase))
            {
                return $"回合未在 CLI 等待窗口内完成，当前状态：{status}。";
            }

            if (!string.IsNullOrWhiteSpace(status) && !IsCompletedTurnStatus(status))
            {
                return $"回合未成功完成，当前状态：{status}。";
            }
        }

        return sendResult.Message;
    }

    private static bool IsCompletedTurnStatus(string? status)
        => string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

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

    private static string ResolveFinalAssistantText(string? completedAssistantText, StringBuilder assistantText, ControlPlaneTurnSubmissionResult? sendResult)
    {
        if (!string.IsNullOrWhiteSpace(completedAssistantText))
        {
            return completedAssistantText;
        }

        lock (assistantText)
        {
            if (assistantText.Length > 0)
            {
                return assistantText.ToString();
            }
        }

        return sendResult?.Message ?? string.Empty;
    }
}

internal sealed class SendCommandRunResult
{
    public SendCommandRunResult(ProbeSummary summary, JsonSerializerOptions jsonOptions)
    {
        Summary = summary;
        SummaryJson = JsonSerializer.Serialize(summary, jsonOptions);
        ConsoleSummary = BuildConsoleSummary(summary);
    }

    public ProbeSummary Summary { get; }

    public SendCommandExitCode ExitCode => (SendCommandExitCode)Summary.ExitCode;

    public string SummaryJson { get; }

    public string ConsoleSummary { get; }

    private static string BuildConsoleSummary(ProbeSummary summary)
        => string.Join(
            Environment.NewLine,
            [
                $"结果：{(summary.Success ? "成功" : "失败")}",
                $"退出码：{summary.ExitCode} ({summary.ExitCodeName})",
                $"线程：{summary.ThreadId ?? "<none>"}",
                $"回合：{summary.TurnId ?? "<none>"}",
                $"状态：{summary.TurnStatus ?? "<none>"}",
                $"恢复：{(summary.ReusedExistingThread ? "已续接旧线程" : "新线程")}",
                $"审批：{BuildApprovalSummary(summary)}",
                $"权限：{BuildPermissionSummary(summary)}",
                $"补录：{BuildUserInputSummary(summary)}",
                $"协作模式：{summary.CollaborationMode ?? "<none>"}",
                $"模型：{summary.Model ?? "<unknown>"}",
                $"产物目录：{summary.ArtifactsDirectory}",
                string.Empty,
                summary.Success ? summary.ResultText : (summary.FailureMessage ?? summary.ResultText),
            ]);

    private static string BuildApprovalSummary(ProbeSummary summary)
    {
        if (!summary.ApprovalRequested)
        {
            return summary.ApproveAll ? "未触发（自动批准已开启）" : "未触发";
        }

        if (summary.ApprovalAutoResponded)
        {
            return "已自动批准";
        }

        if (summary.ApprovalBlocked)
        {
            return "触发但未处理";
        }

        return summary.ApproveAll ? "已触发（无需人工响应或已由内核运行时处理）" : "已触发";
    }

    private static string BuildPermissionSummary(ProbeSummary summary)
    {
        if (!summary.PermissionRequested)
        {
            return string.IsNullOrWhiteSpace(summary.PermissionsJsonPath) ? "未触发" : "未触发（自动权限响应已配置）";
        }

        if (summary.PermissionAutoResponded)
        {
            return "已自动提交";
        }

        if (summary.PermissionBlocked)
        {
            return "触发但未处理";
        }

        return "已触发";
    }

    private static string BuildUserInputSummary(ProbeSummary summary)
    {
        if (!summary.UserInputRequested)
        {
            return string.IsNullOrWhiteSpace(summary.UserInputJsonPath) ? "未触发" : "未触发（自动补录已配置）";
        }

        if (summary.UserInputAutoResponded)
        {
            return "已自动提交";
        }

        if (summary.UserInputBlocked)
        {
            return "触发但未处理";
        }

        return "已触发";
    }
}

internal sealed class ProbeSummary
{
    public bool Success { get; set; }

    public int ExitCode { get; set; }

    public string ExitCodeName { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public long DurationMs { get; set; }

    public string WorkingDirectory { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string ConfigFilePath { get; set; } = string.Empty;

    public string? ProfileName { get; set; }

    public bool ApproveAll { get; set; }

    public string? PermissionsJsonPath { get; set; }

    public string? UserInputJsonPath { get; set; }

    public string? CollaborationMode { get; set; }

    public string? RequestedResumeThreadId { get; set; }

    public bool ResumeLatestThread { get; set; }

    public bool ResumeLatestMatchCwd { get; set; } = true;

    public bool ReusedExistingThread { get; set; }

    public string? AppHostProjectPath { get; set; }

    public string? ThreadId { get; set; }

    public string? TurnId { get; set; }

    public string? TurnStatus { get; set; }

    public bool ApprovalRequested { get; set; }

    public bool ApprovalBlocked { get; set; }

    public bool ApprovalAutoResponded { get; set; }

    public bool PermissionRequested { get; set; }

    public bool PermissionBlocked { get; set; }

    public bool PermissionAutoResponded { get; set; }

    public bool UserInputRequested { get; set; }

    public bool UserInputBlocked { get; set; }

    public bool UserInputAutoResponded { get; set; }

    public bool ErrorEventObserved { get; set; }

    public string? AutoResponseFailureMessage { get; set; }

    public int EventCount { get; set; }

    public string? Model { get; set; }

    public string? ModelProvider { get; set; }

    public string? ProtocolAdapter { get; set; }

    public string ResultText { get; set; } = string.Empty;

    public string? FailureMessage { get; set; }

    public string ExecutionPath { get; set; } = "kernel-runtime-loop";

    public string? FallbackReason { get; set; }

    public string? FailureCode { get; set; }

    public IReadOnlyList<string>? RequiredCapabilities { get; set; }

    public IReadOnlyList<string>? LegacyFallbackCapabilities { get; set; }

    public string? KernelRuntimeDisposition { get; set; }

    public string? KernelRunId { get; set; }

    public string? KernelTraceId { get; set; }

    public string? RuntimeExecutionId { get; set; }

    public string? RuntimeTraceRef { get; set; }

    public string? RuntimeDiagnosticsRef { get; set; }

    public string? ReplayCompleteness { get; set; }

    public IReadOnlyList<string>? StagePath { get; set; }

    public int? RuntimeExecutionStepCount { get; set; }

    public KernelRuntimeReplaySummary? ReplaySummary { get; set; }

    public KernelRuntimeDiagnosticsProjection? RuntimeDiagnosticsProjection { get; set; }

    public KernelRuntimeProductTerminalProjection? KernelRuntimeTerminalProjection { get; set; }

    public JsonElement? AppServerError { get; set; }

    public string ArtifactsDirectory { get; set; } = string.Empty;
}

internal sealed class ProbeResolvedOptions
{
    public string? ExecutablePath { get; init; }

    public bool UseDotNetProjectLauncher { get; init; }

    public string? AppHostProjectPath { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? ConfigFilePath { get; init; }

    public string? ProfileName { get; init; }

    public IReadOnlyDictionary<string, string>? ConfigOverrides { get; init; }

    public string? ResumeThreadId { get; init; }

    public bool ResumeLatestThread { get; init; }

    public bool ResumeLatestMatchCwd { get; init; } = true;

    public bool ApproveAll { get; init; }

    public string? PermissionsJsonPath { get; init; }

    public string? UserInputJsonPath { get; init; }

    public string? CollaborationMode { get; init; }

    public string? Model { get; init; }

    public string? ModelProvider { get; init; }

    public string? ApprovalPolicy { get; init; }

    public string? SandboxMode { get; init; }

    public string? WebSearchMode { get; init; }

    public string? ServiceTier { get; init; }

    public string? ProviderBaseUrl { get; init; }

    public string? ProviderApiKeyEnvironmentVariable { get; init; }

    public string? ProviderWireApi { get; init; }

    public long? ProviderRequestMaxRetries { get; init; }

    public long? ProviderStreamMaxRetries { get; init; }

    public long? ProviderStreamIdleTimeoutMs { get; init; }

    public bool? ProviderSupportsWebsockets { get; init; }

    public string? ProtocolAdapter { get; init; }

    public string? ActiveProfile { get; init; }

    public IReadOnlyList<ControlPlaneDynamicToolSpec>? DynamicTools { get; init; }

    public string ArtifactsRoot { get; init; } = string.Empty;

    public static ProbeResolvedOptions FromRuntimeOptions(
        ControlPlaneInitializeRuntimeCommand? options,
        ResolvedTianShuConfig? config,
        string? appHostProjectPath,
        string artifactsRoot,
        bool approveAll,
        string? permissionsJsonPath,
        string? userInputJsonPath)
        => new()
        {
            ExecutablePath = options?.ExecutablePath,
            UseDotNetProjectLauncher = options?.UseDotNetProjectLauncher ?? true,
            AppHostProjectPath = appHostProjectPath ?? options?.AppHostProjectPath,
            WorkingDirectory = options?.WorkingDirectory,
            ConfigFilePath = options?.ConfigFilePath,
            ProfileName = options?.ProfileName,
            ConfigOverrides = options?.ConfigOverrides,
            ResumeThreadId = options?.ResumeThreadId,
            ResumeLatestThread = options?.ResumeLatestThread ?? false,
            ResumeLatestMatchCwd = options?.ResumeLatestMatchCwd ?? true,
            ApproveAll = approveAll,
            PermissionsJsonPath = permissionsJsonPath,
            UserInputJsonPath = userInputJsonPath,
            CollaborationMode = options?.CollaborationMode,
            Model = options?.Model,
            ModelProvider = options?.ModelProvider,
            ApprovalPolicy = options?.ApprovalPolicy,
            SandboxMode = options?.SandboxMode,
            WebSearchMode = options?.WebSearchMode,
            ServiceTier = string.IsNullOrWhiteSpace(options?.ServiceTier)
                ? null
                : options.ServiceTier,
            ProviderBaseUrl = options?.ProviderBaseUrl,
            ProviderApiKeyEnvironmentVariable = options?.ProviderApiKeyEnvironmentVariable,
            ProviderWireApi = options?.ProviderWireApi,
            ProviderRequestMaxRetries = options?.ProviderRequestMaxRetries,
            ProviderStreamMaxRetries = options?.ProviderStreamMaxRetries,
            ProviderStreamIdleTimeoutMs = options?.ProviderStreamIdleTimeoutMs,
            ProviderSupportsWebsockets = options?.ProviderSupportsWebsockets,
            ProtocolAdapter = options?.ProtocolAdapter ?? config?.ProtocolAdapter,
            ActiveProfile = config?.ActiveProfile,
            DynamicTools = options?.DynamicTools,
            ArtifactsRoot = artifactsRoot,
        };
}

internal sealed class ProbeEventRecord
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public DateTimeOffset Timestamp { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string? ThreadId { get; init; }

    public string? TurnId { get; init; }

    public string? ItemId { get; init; }

    public string? CallId { get; init; }

    public string? ToolName { get; init; }

    public string? Text { get; init; }

    public string? Status { get; init; }

    public string? Message { get; init; }

    public bool? WillRetry { get; init; }

    public bool? RequiresApproval { get; init; }

    public PlanEventPayload? Plan { get; init; }

    public ToolCallEventPayload? ToolCall { get; init; }

    public CliApprovalRequestPayload? ApprovalRequest { get; init; }

    public CliPermissionRequestPayload? PermissionRequest { get; init; }

    public CliUserInputRequestPayload? UserInputRequest { get; init; }

    public TaskEventPayload? Task { get; init; }

    public OperationEventPayload? Operation { get; init; }

    public ReasoningEventPayload? Reasoning { get; init; }

    public McpServerStatusPayload? McpServerStatus { get; init; }

    public WindowsSandboxSetupPayload? WindowsSandboxSetup { get; init; }

    public McpServerOauthLoginPayload? McpServerOauthLogin { get; init; }

    public RealtimeSessionPayload? RealtimeSession { get; init; }

    public FuzzyFileSearchSessionPayload? FuzzyFileSearchSession { get; init; }

    public CommittedUserMessagePayload? CommittedUserMessage { get; init; }

    public CliPendingFollowUpPayload? PendingFollowUp { get; init; }

    public CliPendingInputStatePayload? PendingInputState { get; init; }

    public CliJobProgressPayload? AgentJobProgress { get; init; }

    public DeprecationNoticePayload? DeprecationNotice { get; init; }

    public ConfigWarningPayload? ConfigWarning { get; init; }

    public ThreadStatusChangedPayload? ThreadStatusChanged { get; init; }

    public ThreadNameUpdatedPayload? ThreadNameUpdated { get; init; }

    public ThreadTokenUsagePayload? ThreadTokenUsage { get; init; }

    public CommandExecOutputDeltaPayload? CommandExecOutputDelta { get; init; }

    public AppListUpdatedPayload? AppListUpdated { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProbeEventDiagnostics? Diagnostics { get; init; }

    public ThreadRealtimeItemAddedPayload? ThreadRealtimeItemAdded { get; init; }

    public ThreadRealtimeOutputAudioDeltaPayload? ThreadRealtimeOutputAudioDelta { get; init; }

    public ThreadRealtimeErrorPayload? ThreadRealtimeError { get; init; }

    public ThreadRealtimeClosedPayload? ThreadRealtimeClosed { get; init; }

    public static ProbeEventRecord FromStreamEvent(ControlPlaneConversationStreamEvent streamEvent)
        => new()
        {
            Timestamp = streamEvent.Timestamp,
            Kind = streamEvent.Kind.ToString(),
            ThreadId = streamEvent.ThreadId?.Value,
            TurnId = streamEvent.TurnId?.Value,
            ItemId = streamEvent.ItemId,
            CallId = streamEvent.CallId?.Value,
            ToolName = streamEvent.ToolName,
            Text = streamEvent.Text,
            Status = streamEvent.Status,
            Message = streamEvent.Message,
            WillRetry = streamEvent.WillRetry,
            RequiresApproval = streamEvent.RequiresApproval,
            Plan = ReadPayload<PlanEventPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.Plan),
            ToolCall = ReadPayload<ToolCallEventPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.ToolCall),
            ApprovalRequest = ReadPayload<CliApprovalRequestPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.ApprovalRequest),
            PermissionRequest = ReadPayload<CliPermissionRequestPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.PermissionRequest),
            UserInputRequest = ReadPayload<CliUserInputRequestPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.UserInputRequest),
            Task = ReadPayload<TaskEventPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.Task),
            Operation = ReadPayload<OperationEventPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.Operation),
            Reasoning = ReadPayload<ReasoningEventPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.Reasoning),
            McpServerStatus = ReadPayload<McpServerStatusPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.McpServerStatus),
            WindowsSandboxSetup = ReadPayload<WindowsSandboxSetupPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.WindowsSandboxSetup),
            McpServerOauthLogin = ReadPayload<McpServerOauthLoginPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.McpServerOauthLogin),
            RealtimeSession = ReadPayload<RealtimeSessionPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.RealtimeSession),
            FuzzyFileSearchSession = ReadPayload<FuzzyFileSearchSessionPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.FuzzyFileSearchSession),
            CommittedUserMessage = ReadPayload<CommittedUserMessagePayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.CommittedUserMessage),
            PendingFollowUp = CliInteractiveStateConverters.ReadPendingFollowUpPayload(streamEvent),
            PendingInputState = CliInteractiveStateConverters.ReadPendingInputStatePayload(streamEvent),
            AgentJobProgress = ReadPayload<CliJobProgressPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.AgentJobProgress),
            DeprecationNotice = ReadPayload<DeprecationNoticePayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.DeprecationNotice),
            ConfigWarning = ReadPayload<ConfigWarningPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.ConfigWarning),
            ThreadStatusChanged = ReadPayload<ThreadStatusChangedPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadStatusChanged),
            ThreadNameUpdated = ReadPayload<ThreadNameUpdatedPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadNameUpdated),
            ThreadTokenUsage = ReadPayload<ThreadTokenUsagePayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadTokenUsage),
            CommandExecOutputDelta = ReadPayload<CommandExecOutputDeltaPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.CommandExecOutputDelta),
            AppListUpdated = ReadPayload<AppListUpdatedPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.AppListUpdated),
            ThreadRealtimeItemAdded = ReadPayload<ThreadRealtimeItemAddedPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadRealtimeItemAdded),
            ThreadRealtimeOutputAudioDelta = ReadPayload<ThreadRealtimeOutputAudioDeltaPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadRealtimeOutputAudioDelta),
            ThreadRealtimeError = ReadPayload<ThreadRealtimeErrorPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadRealtimeError),
            ThreadRealtimeClosed = ReadPayload<ThreadRealtimeClosedPayload>(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadRealtimeClosed),
            Diagnostics = ProbeEventDiagnostics.CreateFromStreamEvent(streamEvent),
        };

    public static ProbeEventRecord CreateSynthetic(
        string kind,
        string? threadId,
        string? turnId,
        string? callId,
        string? toolName,
        string? message,
        string? text = null,
        string? status = null)
        => new()
        {
            Timestamp = DateTimeOffset.Now,
            Kind = kind,
            ThreadId = threadId,
            TurnId = turnId,
            CallId = callId,
            ToolName = toolName,
            Text = text,
            Status = status,
            Message = message,
        };

    private static TPayload? ReadPayload<TPayload>(
        ControlPlaneConversationStreamEvent streamEvent,
        ControlPlaneConversationStreamPayloadKind payloadKind)
        where TPayload : class
    {
        if (streamEvent.PayloadKind != payloadKind || streamEvent.Payload is null)
        {
            return null;
        }

        var payloadElement = JsonSerializer.SerializeToElement(streamEvent.Payload.ToPlainObject(), PayloadJsonOptions);
        return JsonSerializer.Deserialize<TPayload>(payloadElement, PayloadJsonOptions);
    }
}

internal sealed class ProbeEventDiagnostics
{
    // 仅用于验收取证和排障，不得反向驱动 CLI 的业务展示或状态机。
    public string? DataJson { get; init; }

    public string? MetadataJson { get; init; }

    public string? RawJson { get; init; }

    [DiagnosticsJsonAccessAllowed]
    public static ProbeEventDiagnostics? CreateFromStreamEvent(ControlPlaneConversationStreamEvent streamEvent)
        => Create(streamEvent.Diagnostics?.DataJson, streamEvent.Diagnostics?.MetadataJson, streamEvent.Diagnostics?.RawJson);

    private static ProbeEventDiagnostics? Create(string? dataJson, string? metadataJson, string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson)
            && string.IsNullOrWhiteSpace(metadataJson)
            && string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        return new ProbeEventDiagnostics
        {
            DataJson = dataJson,
            MetadataJson = metadataJson,
            RawJson = rawJson,
        };
    }
}
