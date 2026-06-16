using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelToolCallAppHostRuntimeContext(
    string ThreadId,
    string TurnId,
    string ItemId,
    string ToolName,
    JsonElement Arguments,
    string ResolvedCwd,
    Dictionary<string, object?> RuntimeConfig,
    KernelResponsesNativeToolOptions NativeToolOptions,
    JsonElement? SandboxPolicy,
    string? SandboxMode,
    IReadOnlyList<KernelDynamicToolDescriptor>? DynamicTools,
    KernelApprovalPolicy? ApprovalPolicy,
    bool AllowLoginShell,
    KernelShellEnvironmentPolicy? ShellEnvironmentPolicy,
    KernelCollaborationModeState? CollaborationMode,
    bool DefaultModeRequestUserInputEnabled,
    KernelWindowsSandboxLevel WindowsSandboxLevel,
    KernelToolRuntimeServices RuntimeServices,
    Func<McpServerElicitationRequest, CancellationToken, Task<McpServerElicitationResponse>> McpServerElicitationRequester,
    Func<KernelRequestUserInputRequest, CancellationToken, Task<KernelRequestUserInputResponse>> UserInputRequester,
    Func<KernelRequestPermissionsRequest, CancellationToken, Task<KernelRequestPermissionsResponse>> PermissionRequester,
    string? ExternalCallId);

internal sealed class KernelToolCallAppHostRuntime
{
    private readonly KernelToolRegistry toolRegistry;
    private readonly IReadOnlyList<IKernelToolExecutionHook> toolExecutionHooks;
    private readonly KernelExecPolicyManager execPolicyManager;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> commandApprovalSessionKeysByThread;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> fileChangeApprovalSessionPathsByThread;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> mcpToolApprovalSessionKeysByThread;
    private readonly ConcurrentDictionary<string, KernelPermissionGrantProfile> grantedPermissionSessionByThread;
    private readonly ConcurrentDictionary<string, KernelPermissionGrantProfile> grantedPermissionTurnByTurn;
    private readonly Func<KernelDynamicToolDescriptor, string?, CancellationToken, Task<bool>> tryPersistDynamicToolApprovalAsync;
    private readonly Func<string, object, string, CancellationToken, TimeSpan?, Task<JsonElement>> sendServerRequestAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Func<string, string, string, string, JsonElement, CancellationToken, Task> emitCollabToolCallStartedNotificationAsync;
    private readonly Func<string, string, string, string, JsonElement, KernelToolResult, CancellationToken, Task> emitCollabToolCallCompletedNotificationAsync;
    private readonly Func<string, string, string, string, JsonElement, IReadOnlyList<KernelDynamicToolDescriptor>?, CancellationToken, Task> emitMcpToolCallStartedNotificationAsync;
    private readonly Func<string, string, string, string, JsonElement, IReadOnlyList<KernelDynamicToolDescriptor>?, KernelToolResult, TimeSpan, CancellationToken, Task> emitMcpToolCallCompletedNotificationAsync;
    private readonly Func<string, string, string, string, JsonElement, CancellationToken, Task> emitDynamicToolCallStartedNotificationAsync;
    private readonly Func<string, string, string, string, JsonElement, KernelToolResult, TimeSpan, CancellationToken, Task> emitDynamicToolCallCompletedNotificationAsync;
    private readonly Func<string, string, string, string, JsonElement, string, CancellationToken, Task> emitFileChangeStartedNotificationAsync;
    private readonly Func<string, string, string, string, JsonElement, string, string, CancellationToken, Task> emitFileChangeCompletedNotificationAsync;
    private readonly Func<string, string, string, JsonElement, string, CancellationToken, Task> emitImageViewLifecycleNotificationsAsync;

    public KernelToolCallAppHostRuntime(
        KernelToolRegistry toolRegistry,
        IReadOnlyList<IKernelToolExecutionHook> toolExecutionHooks,
        KernelExecPolicyManager execPolicyManager,
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> commandApprovalSessionKeysByThread,
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> fileChangeApprovalSessionPathsByThread,
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> mcpToolApprovalSessionKeysByThread,
        ConcurrentDictionary<string, KernelPermissionGrantProfile> grantedPermissionSessionByThread,
        ConcurrentDictionary<string, KernelPermissionGrantProfile> grantedPermissionTurnByTurn,
        Func<KernelDynamicToolDescriptor, string?, CancellationToken, Task<bool>> tryPersistDynamicToolApprovalAsync,
        Func<string, object, string, CancellationToken, TimeSpan?, Task<JsonElement>> sendServerRequestAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Func<string, string, string, string, JsonElement, CancellationToken, Task> emitCollabToolCallStartedNotificationAsync,
        Func<string, string, string, string, JsonElement, KernelToolResult, CancellationToken, Task> emitCollabToolCallCompletedNotificationAsync,
        Func<string, string, string, string, JsonElement, IReadOnlyList<KernelDynamicToolDescriptor>?, CancellationToken, Task> emitMcpToolCallStartedNotificationAsync,
        Func<string, string, string, string, JsonElement, IReadOnlyList<KernelDynamicToolDescriptor>?, KernelToolResult, TimeSpan, CancellationToken, Task> emitMcpToolCallCompletedNotificationAsync,
        Func<string, string, string, string, JsonElement, CancellationToken, Task> emitDynamicToolCallStartedNotificationAsync,
        Func<string, string, string, string, JsonElement, KernelToolResult, TimeSpan, CancellationToken, Task> emitDynamicToolCallCompletedNotificationAsync,
        Func<string, string, string, string, JsonElement, string, CancellationToken, Task> emitFileChangeStartedNotificationAsync,
        Func<string, string, string, string, JsonElement, string, string, CancellationToken, Task> emitFileChangeCompletedNotificationAsync,
        Func<string, string, string, JsonElement, string, CancellationToken, Task> emitImageViewLifecycleNotificationsAsync)
    {
        this.toolRegistry = toolRegistry;
        this.toolExecutionHooks = toolExecutionHooks;
        this.execPolicyManager = execPolicyManager;
        this.commandApprovalSessionKeysByThread = commandApprovalSessionKeysByThread;
        this.fileChangeApprovalSessionPathsByThread = fileChangeApprovalSessionPathsByThread;
        this.mcpToolApprovalSessionKeysByThread = mcpToolApprovalSessionKeysByThread;
        this.grantedPermissionSessionByThread = grantedPermissionSessionByThread;
        this.grantedPermissionTurnByTurn = grantedPermissionTurnByTurn;
        this.tryPersistDynamicToolApprovalAsync = tryPersistDynamicToolApprovalAsync;
        this.sendServerRequestAsync = sendServerRequestAsync;
        this.writeNotificationAsync = writeNotificationAsync;
        this.emitCollabToolCallStartedNotificationAsync = emitCollabToolCallStartedNotificationAsync;
        this.emitCollabToolCallCompletedNotificationAsync = emitCollabToolCallCompletedNotificationAsync;
        this.emitMcpToolCallStartedNotificationAsync = emitMcpToolCallStartedNotificationAsync;
        this.emitMcpToolCallCompletedNotificationAsync = emitMcpToolCallCompletedNotificationAsync;
        this.emitDynamicToolCallStartedNotificationAsync = emitDynamicToolCallStartedNotificationAsync;
        this.emitDynamicToolCallCompletedNotificationAsync = emitDynamicToolCallCompletedNotificationAsync;
        this.emitFileChangeStartedNotificationAsync = emitFileChangeStartedNotificationAsync;
        this.emitFileChangeCompletedNotificationAsync = emitFileChangeCompletedNotificationAsync;
        this.emitImageViewLifecycleNotificationsAsync = emitImageViewLifecycleNotificationsAsync;
    }

    public async Task<KernelToolResult> ExecuteToolCallAsync(
        KernelToolCallAppHostRuntimeContext context,
        KernelReadinessFlag? toolCallGate,
        CancellationToken cancellationToken,
        string? customInput = null,
        bool isCustomToolCall = false)
    {
        _ = toolRegistry.TryGet(context.ToolName, out var handler);
        JsonElement? dynamicToolSchema = null;
        var hasDynamicTool = !isCustomToolCall
            && KernelToolRuntimeParsingHelpers.TryResolveDynamicToolSchema(context.DynamicTools, context.ToolName, out dynamicToolSchema);
        KernelDynamicToolDescriptor? dynamicToolDescriptor = null;
        _ = !isCustomToolCall
            && KernelToolRuntimeParsingHelpers.TryResolveDynamicToolDescriptor(context.DynamicTools, context.ToolName, out dynamicToolDescriptor);
        var dynamicToolCallId = Normalize(context.ExternalCallId) ?? context.ItemId;
        var providerCallId = Normalize(context.ExternalCallId) ?? context.ItemId;

        await writeNotificationAsync("item/tool/call", new
        {
            threadId = context.ThreadId,
            turnId = context.TurnId,
            callId = providerCallId,
            item = new
            {
                id = context.ItemId,
                type = "tool_call",
                call_id = providerCallId,
                toolName = context.ToolName,
                status = "inProgress",
            },
        }, CancellationToken.None).ConfigureAwait(false);

        await emitCollabToolCallStartedNotificationAsync(
            context.ThreadId,
            context.TurnId,
            context.ItemId,
            context.ToolName,
            context.Arguments,
            CancellationToken.None).ConfigureAwait(false);
        await emitMcpToolCallStartedNotificationAsync(
            context.ThreadId,
            context.TurnId,
            context.ItemId,
            context.ToolName,
            context.Arguments,
            context.DynamicTools,
            CancellationToken.None).ConfigureAwait(false);

        var shouldEmitDynamicToolLifecycle = handler is null && hasDynamicTool;
        var shouldEmitFileChangeLifecycle = KernelToolRuntimeApprovalHelpers.IsFileChangeApprovalTool(context.ToolName);
        var isImageViewTool = string.Equals(context.ToolName, "view_image", StringComparison.Ordinal);
        if (shouldEmitDynamicToolLifecycle)
        {
            await emitDynamicToolCallStartedNotificationAsync(
                context.ThreadId,
                context.TurnId,
                dynamicToolCallId,
                context.ToolName,
                context.Arguments,
                CancellationToken.None).ConfigureAwait(false);
        }

        if (shouldEmitFileChangeLifecycle)
        {
            await emitFileChangeStartedNotificationAsync(
                context.ThreadId,
                context.TurnId,
                context.ItemId,
                context.ToolName,
                context.Arguments,
                context.ResolvedCwd,
                CancellationToken.None).ConfigureAwait(false);
        }

        KernelToolCallContext CreateCallContext(IReadOnlyList<string>? approvedFileChangePaths = null)
        {
            var grantedPermissions = KernelToolRuntimeApprovalHelpers.GetGrantedPermissions(
                grantedPermissionSessionByThread,
                grantedPermissionTurnByTurn,
                context.ThreadId,
                context.TurnId);
            JsonElement? effectiveSandboxPolicy = context.SandboxPolicy;
            string? effectiveSandboxMode = context.SandboxMode;
            if (approvedFileChangePaths is { Count: > 0 })
            {
                effectiveSandboxMode = "danger-full-access";
            }
            else
            {
                effectiveSandboxPolicy = KernelToolSandboxResolver.ApplyGrantedPermissions(
                    context.SandboxPolicy,
                    context.SandboxMode ?? "workspaceWrite",
                    grantedPermissions,
                    out var grantedSandboxMode);
                effectiveSandboxMode = grantedSandboxMode;
            }

            return new KernelToolCallContext(
                ThreadId: context.ThreadId,
                TurnId: context.TurnId,
                Cwd: context.ResolvedCwd,
                SandboxPolicy: effectiveSandboxPolicy,
                SandboxMode: effectiveSandboxMode,
                GrantedPermissions: grantedPermissions,
                ApprovedFileChangePaths: approvedFileChangePaths,
                McpServerElicitationRequester: context.McpServerElicitationRequester,
                RuntimeServices: context.RuntimeServices,
                DynamicTools: context.DynamicTools,
                ApprovalPolicy: context.ApprovalPolicy,
                AllowLoginShell: context.AllowLoginShell,
                ShellEnvironmentPolicy: context.ShellEnvironmentPolicy,
                ItemId: context.ItemId,
                CollaborationMode: context.CollaborationMode,
                DefaultModeRequestUserInputEnabled: context.DefaultModeRequestUserInputEnabled,
                UserInputRequester: context.UserInputRequester,
                ExecPermissionApprovalsEnabled: context.NativeToolOptions.ExecPermissionApprovalsEnabled,
                RequestPermissionsToolEnabled: context.NativeToolOptions.RequestPermissionsToolEnabled,
                RequestPermissionsEnabled: KernelToolRuntimeApprovalHelpers.ResolveRequestPermissionsEnabled(context.RuntimeConfig, context.ApprovalPolicy),
                PermissionRequester: context.PermissionRequester,
                SupportsImageInput: context.NativeToolOptions.ViewImageModelSupportsImageInput,
                CanRequestOriginalImageDetail: context.NativeToolOptions.ViewImageCanRequestOriginalDetail,
                WindowsSandboxLevel: context.WindowsSandboxLevel,
                ExternalCallId: context.ExternalCallId);
        }

        async Task<KernelToolResult> InvokeHandlerAsync(IReadOnlyList<string>? approvedFileChangePaths = null)
        {
            var callContext = CreateCallContext(approvedFileChangePaths);
            return isCustomToolCall
                ? await handler!.ExecuteCustomAsync(customInput ?? string.Empty, callContext, cancellationToken).ConfigureAwait(false)
                : await handler!.ExecuteAsync(context.Arguments, callContext, cancellationToken).ConfigureAwait(false);
        }

        TimeSpan? executionDuration = null;
        async Task<KernelToolResult> InvokeHandlerWithDurationAsync(IReadOnlyList<string>? approvedFileChangePaths = null)
        {
            var handlerStopwatch = Stopwatch.StartNew();
            var invokeResult = await InvokeHandlerAsync(approvedFileChangePaths).ConfigureAwait(false);
            executionDuration = handlerStopwatch.Elapsed;
            return invokeResult;
        }

        var hookContext = new KernelToolExecutionHookContext(
            ThreadId: context.ThreadId,
            TurnId: context.TurnId,
            ItemId: context.ItemId,
            ExternalCallId: context.ExternalCallId,
            ToolName: context.ToolName,
            Arguments: context.Arguments);
        await InvokeToolHooksBeforeAsync(hookContext, cancellationToken).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        KernelToolResult result;
        string? fileChangeCompletionStatus = null;
        try
        {
            if (handler is null && !hasDynamicTool && isCustomToolCall)
            {
                result = new KernelToolResult(false, $"unsupported custom tool call: {context.ToolName}");
            }
            else if (handler is null && !hasDynamicTool)
            {
                result = new KernelToolResult(false, $"未知工具：{context.ToolName}");
            }
            else if (handler is not null
                     && !KernelToolRuntimeApprovalHelpers.IsBuiltInToolExecutionEnabled(context.ToolName, context.NativeToolOptions))
            {
                result = new KernelToolResult(false, $"工具当前未启用：{context.ToolName}");
            }
            else if (handler is null
                     && hasDynamicTool
                     && !KernelToolSchemaValidator.TryValidate(dynamicToolSchema!.Value, context.Arguments, out var dynamicSchemaError))
            {
                result = new KernelToolResult(false, $"工具参数无效：{dynamicSchemaError}");
            }
            else if (handler is null && hasDynamicTool)
            {
                var requiresApproval = dynamicToolDescriptor is not null
                                       && KernelToolRuntimeApprovalHelpers.DynamicToolRequiresApproval(dynamicToolDescriptor);
                var sessionApprovalKey = dynamicToolDescriptor is null
                    ? null
                    : KernelToolRuntimeApprovalHelpers.BuildDynamicToolApprovalSessionKey(dynamicToolDescriptor);
                var approvedForSession = dynamicToolDescriptor is not null
                    && KernelToolRuntimeApprovalHelpers.IsDynamicToolApprovalAcceptedForSession(
                        mcpToolApprovalSessionKeysByThread,
                        context.ThreadId,
                        sessionApprovalKey);
                var approvedPersistently = dynamicToolDescriptor is not null
                    && KernelToolRuntimeApprovalHelpers.IsDynamicToolApprovalRememberedPersistently(
                        context.RuntimeConfig,
                        dynamicToolDescriptor);

                if (requiresApproval && !approvedForSession && !approvedPersistently)
                {
                    var availableDecisions = dynamicToolDescriptor is null
                        ? ["accept", "decline", "cancel"]
                        : KernelToolRuntimeApprovalHelpers.BuildDynamicToolApprovalAvailableDecisions(dynamicToolDescriptor);
                    var approvalResponse = await sendServerRequestAsync(
                        "item/tool/requestApproval",
                        new
                        {
                            threadId = context.ThreadId,
                            turnId = context.TurnId,
                            itemId = context.ItemId,
                            callId = providerCallId,
                            toolName = context.ToolName,
                            serverName = dynamicToolDescriptor?.ApprovalServerName,
                            arguments = context.Arguments,
                            reason = "mcp_tool_call_requires_approval",
                            approvalKind = "mcp_tool_call",
                            availableDecisions,
                            _meta = dynamicToolDescriptor is null
                                ? null
                                : KernelToolRuntimeApprovalHelpers.BuildDynamicToolApprovalMetadata(dynamicToolDescriptor, context.Arguments),
                        },
                        context.ThreadId,
                        cancellationToken,
                        null).ConfigureAwait(false);

                    var decision = KernelToolRuntimeApprovalHelpers.ResolveDynamicToolApprovalDecision(approvalResponse);
                    if (string.Equals(decision, "acceptForSession", StringComparison.OrdinalIgnoreCase))
                    {
                        KernelToolRuntimeApprovalHelpers.MarkDynamicToolApprovalAcceptedForSession(
                            mcpToolApprovalSessionKeysByThread,
                            context.ThreadId,
                            sessionApprovalKey);
                    }
                    else if (string.Equals(decision, "acceptAndRemember", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = dynamicToolDescriptor is not null
                            && await tryPersistDynamicToolApprovalAsync(dynamicToolDescriptor, context.ResolvedCwd, cancellationToken).ConfigureAwait(false);
                        KernelToolRuntimeApprovalHelpers.MarkDynamicToolApprovalAcceptedForSession(
                            mcpToolApprovalSessionKeysByThread,
                            context.ThreadId,
                            sessionApprovalKey);
                    }
                    else if (!string.Equals(decision, "accept", StringComparison.OrdinalIgnoreCase))
                    {
                        result = new KernelToolResult(false, "工具调用未获批准。");
                        goto DynamicToolExecutionCompleted;
                    }
                }

                executionDuration = TimeSpan.Zero;
                result = await ExecuteDynamicToolAsync(
                    context.ThreadId,
                    context.TurnId,
                    dynamicToolCallId,
                    context.ToolName,
                    context.Arguments,
                    cancellationToken).ConfigureAwait(false);

DynamicToolExecutionCompleted:
                ;
            }
            else if (!isCustomToolCall
                     && handler is not null
                     && !KernelToolSchemaValidator.TryValidate(handler.InputSchema, context.Arguments, out var schemaError))
            {
                result = new KernelToolResult(false, $"工具参数无效：{schemaError}");
            }
            else if (handler is not null && handler.IsMutating)
            {
                toolCallGate ??= new KernelReadinessFlag();
                await toolCallGate.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
                var grantedPermissions = KernelToolRuntimeApprovalHelpers.GetGrantedPermissions(
                    grantedPermissionSessionByThread,
                    grantedPermissionTurnByTurn,
                    context.ThreadId,
                    context.TurnId);
                _ = KernelToolSandboxResolver.ApplyGrantedPermissions(
                    context.SandboxPolicy,
                    context.SandboxMode ?? "workspaceWrite",
                    grantedPermissions,
                    out var effectiveGrantedSandboxMode);

                IReadOnlyList<string>? resolvedFileChangePaths = null;
                var supportsFileChangeApproval = KernelToolRuntimeApprovalHelpers.IsFileChangeApprovalTool(context.ToolName);
                if (supportsFileChangeApproval)
                {
                    resolvedFileChangePaths = KernelToolRuntimeApprovalHelpers.TryResolveFileChangePaths(
                        context.ToolName,
                        context.Arguments,
                        context.ResolvedCwd);
                }

                var alreadyApprovedBySessionApproval = supportsFileChangeApproval
                    && KernelToolRuntimeApprovalHelpers.AreFileChangesApprovedForSession(
                        fileChangeApprovalSessionPathsByThread,
                        context.ThreadId,
                        resolvedFileChangePaths);
                var alreadyApprovedByGrantedPermissions = supportsFileChangeApproval
                    && KernelToolRuntimeApprovalHelpers.AreGrantedPermissionsApproved(grantedPermissions, resolvedFileChangePaths);
                var alreadyApprovedForSession = alreadyApprovedBySessionApproval || alreadyApprovedByGrantedPermissions;
                var isCommandApprovalTool = KernelToolCommandApprovalResolver.IsSupportedTool(context.ToolName);
                KernelCommandApprovalRequest? commandApprovalRequest = null;
                string? commandApprovalSessionKey = null;
                KernelExecPolicyDecision toolPolicyDecision;
                if (isCommandApprovalTool
                    && KernelToolCommandApprovalResolver.TryResolve(
                        context.ToolName,
                        context.Arguments,
                        context.AllowLoginShell,
                        context.ResolvedCwd,
                        out commandApprovalRequest,
                        out _)
                    && commandApprovalRequest is not null)
                {
                    commandApprovalSessionKey = KernelCommandApprovalUtilities.BuildCommandApprovalSessionKey(commandApprovalRequest);
                    var alreadyApprovedByCommandSession = IsCommandApprovalAcceptedForSession(context.ThreadId, commandApprovalSessionKey);
                    alreadyApprovedForSession = alreadyApprovedForSession || alreadyApprovedByCommandSession;
                    toolPolicyDecision = execPolicyManager.EvaluateCommand(
                        commandApprovalRequest.Command,
                        commandApprovalRequest.CommandPreview,
                        context.ApprovalPolicy,
                        effectiveGrantedSandboxMode,
                        alreadyApproved: alreadyApprovedForSession,
                        requestsSandboxOverride: commandApprovalRequest.RequestsFreshSandboxOverride(grantedPermissions));
                }
                else if (isCommandApprovalTool)
                {
                    toolPolicyDecision = new KernelExecPolicyDecision(
                        KernelExecPolicyDecisionKind.Allow,
                        "exec_policy_defer_to_tool_handler",
                        BypassSandbox: false,
                        ProposedAmendment: null);
                }
                else
                {
                    toolPolicyDecision = execPolicyManager.EvaluateMutatingTool(
                        context.ToolName,
                        context.ApprovalPolicy,
                        effectiveGrantedSandboxMode,
                        alreadyApproved: alreadyApprovedForSession);
                }

                IReadOnlyList<string>? approvedFileChangePaths = alreadyApprovedBySessionApproval
                    ? resolvedFileChangePaths
                    : null;
                if (toolPolicyDecision.Kind == KernelExecPolicyDecisionKind.Forbidden)
                {
                    result = new KernelToolResult(
                        false,
                        KernelCommandApprovalUtilities.BuildCommandPolicyDeniedMessage(
                            toolPolicyDecision.Reason,
                            effectiveGrantedSandboxMode));
                    if (shouldEmitFileChangeLifecycle)
                    {
                        fileChangeCompletionStatus = "failed";
                    }
                }
                else if (toolPolicyDecision.Kind == KernelExecPolicyDecisionKind.NeedsApproval)
                {
                    if (supportsFileChangeApproval)
                    {
                        var approvalResponse = await sendServerRequestAsync(
                            "item/fileChange/requestApproval",
                            new
                            {
                                threadId = context.ThreadId,
                                turnId = context.TurnId,
                                itemId = context.ItemId,
                                callId = providerCallId,
                                toolName = context.ToolName,
                                arguments = context.Arguments,
                                reason = toolPolicyDecision.Reason,
                                grantRoot = (string?)null,
                            },
                            context.ThreadId,
                            cancellationToken,
                            null).ConfigureAwait(false);

                        var decision = Normalize(ReadString(approvalResponse, "decision"));
                        if (!KernelToolRuntimeApprovalHelpers.TryResolveFileChangeApprovalDecision(decision, out var approvedForSession))
                        {
                            result = new KernelToolResult(false, "文件变更未获批准。");
                            fileChangeCompletionStatus = "declined";
                        }
                        else
                        {
                            if (approvedForSession)
                            {
                                KernelToolRuntimeApprovalHelpers.MarkFileChangesApprovedForSession(
                                    fileChangeApprovalSessionPathsByThread,
                                    context.ThreadId,
                                    resolvedFileChangePaths);
                            }

                            approvedFileChangePaths = resolvedFileChangePaths;
                            result = await InvokeHandlerWithDurationAsync(approvedFileChangePaths).ConfigureAwait(false);
                            fileChangeCompletionStatus = result.Success ? "completed" : "failed";
                        }
                    }
                    else if (isCommandApprovalTool && commandApprovalRequest is not null)
                    {
                        var (decision, requestError, applyAmendment) = await RequestCommandExecutionApprovalAsync(
                            context.ThreadId,
                            context.TurnId,
                            context.ItemId,
                            commandApprovalRequest.Command,
                            commandApprovalRequest.CommandPreview,
                            context.ResolvedCwd,
                            toolPolicyDecision.Reason,
                            KernelCommandApprovalUtilities.BuildCommandExecutionAvailableDecisions(toolPolicyDecision.ProposedAmendment),
                            toolPolicyDecision.ProposedAmendment,
                            approvalId: null,
                            additionalPermissions: commandApprovalRequest.RequestedAdditionalPermissions,
                            commandActions: null,
                            cancellationToken).ConfigureAwait(false);

                        if (!string.IsNullOrWhiteSpace(requestError))
                        {
                            result = new KernelToolResult(false, $"审批请求失败：{requestError}");
                        }
                        else if (!KernelCommandApprovalUtilities.TryResolveCommandApprovalDecision(decision, out var approvedForSession))
                        {
                            result = new KernelToolResult(
                                false,
                                KernelCommandApprovalUtilities.BuildCommandApprovalDeclinedMessage(
                                    toolPolicyDecision.Reason,
                                    effectiveGrantedSandboxMode));
                        }
                        else
                        {
                            if (approvedForSession)
                            {
                                MarkCommandApprovalAcceptedForSession(context.ThreadId, commandApprovalSessionKey);
                            }

                            if (applyAmendment is not null)
                            {
                                await execPolicyManager.AppendAmendmentAndUpdateAsync(applyAmendment, cancellationToken)
                                    .ConfigureAwait(false);
                            }

                            result = await InvokeHandlerWithDurationAsync().ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        var approvalResponse = await sendServerRequestAsync(
                            "item/tool/requestApproval",
                            new
                            {
                                threadId = context.ThreadId,
                                turnId = context.TurnId,
                                itemId = context.ItemId,
                                callId = providerCallId,
                                toolName = context.ToolName,
                                arguments = context.Arguments,
                                reason = toolPolicyDecision.Reason,
                            },
                            context.ThreadId,
                            cancellationToken,
                            null).ConfigureAwait(false);

                        var decision = Normalize(ReadString(approvalResponse, "decision"));
                        if (!string.Equals(decision, "accept", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(decision, "acceptForSession", StringComparison.OrdinalIgnoreCase))
                        {
                            result = new KernelToolResult(false, "工具调用未获批准。");
                        }
                        else
                        {
                            result = await InvokeHandlerWithDurationAsync().ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    result = await InvokeHandlerWithDurationAsync(approvedFileChangePaths).ConfigureAwait(false);
                    if (shouldEmitFileChangeLifecycle && fileChangeCompletionStatus is null)
                    {
                        fileChangeCompletionStatus = result.Success ? "completed" : "failed";
                    }
                }
            }
            else
            {
                result = await InvokeHandlerWithDurationAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            var message = Normalize(ex.Message) ?? "tool_execute_failed";
            result = new KernelToolResult(false, message);
            if (shouldEmitFileChangeLifecycle && fileChangeCompletionStatus is null)
            {
                fileChangeCompletionStatus = "failed";
            }

            await InvokeToolHooksErrorAsync(
                hookContext,
                message,
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }

        var afterHookDecision = await InvokeToolHooksAfterAsync(hookContext, result, stopwatch.Elapsed, cancellationToken)
            .ConfigureAwait(false);
        if (afterHookDecision.ShouldAbort)
        {
            throw new KernelToolExecutionHookAbortException(
                afterHookDecision.ErrorMessage ?? "after_tool_use hook aborted operation");
        }

        if (shouldEmitDynamicToolLifecycle)
        {
            await emitDynamicToolCallCompletedNotificationAsync(
                context.ThreadId,
                context.TurnId,
                dynamicToolCallId,
                context.ToolName,
                context.Arguments,
                result,
                executionDuration ?? stopwatch.Elapsed,
                CancellationToken.None).ConfigureAwait(false);
        }

        if (shouldEmitFileChangeLifecycle)
        {
            await emitFileChangeCompletedNotificationAsync(
                context.ThreadId,
                context.TurnId,
                context.ItemId,
                context.ToolName,
                context.Arguments,
                context.ResolvedCwd,
                fileChangeCompletionStatus ?? (result.Success ? "completed" : "failed"),
                CancellationToken.None).ConfigureAwait(false);
        }

        if (isImageViewTool && result.Success)
        {
            await emitImageViewLifecycleNotificationsAsync(
                context.ThreadId,
                context.TurnId,
                context.ItemId,
                context.Arguments,
                context.ResolvedCwd,
                CancellationToken.None).ConfigureAwait(false);
        }

        if (string.Equals(context.ToolName, "apply_patch", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(result.OutputText))
        {
            await writeNotificationAsync("item/fileChange/outputDelta", new
            {
                threadId = context.ThreadId,
                turnId = context.TurnId,
                itemId = context.ItemId,
                delta = result.OutputText,
            }, CancellationToken.None).ConfigureAwait(false);
        }

        await emitCollabToolCallCompletedNotificationAsync(
            context.ThreadId,
            context.TurnId,
            context.ItemId,
            context.ToolName,
            context.Arguments,
            result,
            CancellationToken.None).ConfigureAwait(false);
        await emitMcpToolCallCompletedNotificationAsync(
            context.ThreadId,
            context.TurnId,
            context.ItemId,
            context.ToolName,
            context.Arguments,
            context.DynamicTools,
            result,
            executionDuration ?? stopwatch.Elapsed,
            CancellationToken.None).ConfigureAwait(false);
        await writeNotificationAsync("item/tool/call", new
        {
            threadId = context.ThreadId,
            turnId = context.TurnId,
            callId = providerCallId,
            item = new
            {
                id = context.ItemId,
                type = "tool_call",
                call_id = providerCallId,
                toolName = context.ToolName,
                status = result.Success ? "completed" : "failed",
                arguments = context.Arguments.GetRawText(),
                output = result.OutputText,
            },
        }, CancellationToken.None).ConfigureAwait(false);

        return result;
    }

    private async Task<KernelToolResult> ExecuteDynamicToolAsync(
        string threadId,
        string turnId,
        string callId,
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await sendServerRequestAsync(
                "item/tool/call",
                new
                {
                    threadId,
                    turnId,
                    callId,
                    tool = toolName,
                    arguments,
                },
                threadId,
                cancellationToken,
                null).ConfigureAwait(false);
            var success = ReadBool(response, "success");
            if (!success.HasValue)
            {
                return new KernelToolResult(
                    false,
                    "dynamic tool response was invalid",
                    outputContentItems:
                    [
                        new KernelToolOutputContentItem("input_text", Text: "dynamic tool response was invalid"),
                    ]);
            }

            var outputText = KernelToolRuntimeParsingHelpers.ExtractDynamicToolOutput(response);
            var outputContentItems = KernelToolRuntimeParsingHelpers.ReadDynamicToolOutputContentItems(response);
            var rawOutputContentItems = KernelToolRuntimeParsingHelpers.ReadDynamicToolRawContentItems(response);
            var structuredOutput = KernelToolRuntimeParsingHelpers.ReadDynamicToolStructuredOutput(response);
            var metadata = KernelToolRuntimeParsingHelpers.ReadDynamicToolMetadata(response);
            return new KernelToolResult(success.Value, outputText, outputContentItems, rawOutputContentItems, structuredOutput, metadata);
        }
        catch (Exception ex)
        {
            return new KernelToolResult(false, Normalize(ex.Message) ?? "dynamic_tool_failed");
        }
    }

    private bool IsCommandApprovalAcceptedForSession(string threadId, string? approvalKey)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedApprovalKey = Normalize(approvalKey);
        if (string.IsNullOrWhiteSpace(normalizedThreadId)
            || string.IsNullOrWhiteSpace(normalizedApprovalKey))
        {
            return false;
        }

        return commandApprovalSessionKeysByThread.TryGetValue(normalizedThreadId!, out var approvals)
               && approvals.ContainsKey(normalizedApprovalKey!);
    }

    private void MarkCommandApprovalAcceptedForSession(string threadId, string? approvalKey)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedApprovalKey = Normalize(approvalKey);
        if (string.IsNullOrWhiteSpace(normalizedThreadId)
            || string.IsNullOrWhiteSpace(normalizedApprovalKey))
        {
            return;
        }

        var approvals = commandApprovalSessionKeysByThread.GetOrAdd(
            normalizedThreadId!,
            static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        approvals[normalizedApprovalKey!] = 0;
    }

    private async Task<(string? Decision, string? Error, KernelExecPolicyAmendment? ApplyAmendment)> RequestCommandExecutionApprovalAsync(
        string threadId,
        string turnId,
        string itemId,
        IReadOnlyList<string> commandArgs,
        string command,
        string? cwd,
        string reason,
        IReadOnlyList<object?> availableDecisions,
        KernelExecPolicyAmendment? proposedAmendment,
        string? approvalId,
        KernelPermissionGrantProfile? additionalPermissions,
        IReadOnlyList<object?>? commandActions,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await sendServerRequestAsync(
                "item/commandExecution/requestApproval",
                new
                {
                    threadId,
                    turnId,
                    itemId,
                    approvalId,
                    command,
                    cwd = cwd ?? Environment.CurrentDirectory,
                    commandActions = commandActions?.ToArray(),
                    additionalPermissions = additionalPermissions?.BuildServerPayload(),
                    reason,
                    skillMetadata = KernelCommandApprovalUtilities.TryResolveCommandExecutionApprovalSkillMetadata(
                        commandArgs,
                        cwd ?? Environment.CurrentDirectory),
                    availableDecisions = availableDecisions.ToArray(),
                    proposedExecpolicyAmendment = proposedAmendment?.CommandPrefix.ToArray(),
                },
                threadId,
                cancellationToken,
                null).ConfigureAwait(false);
            return (
                Normalize(KernelManagedNetworkAppHostUtilities.ExtractApprovalDecision(response)),
                null,
                KernelExecPolicyApprovalResponseReader.TryReadAppliedAmendment(response, proposedAmendment));
        }
        catch (Exception ex)
        {
            return (null, Normalize(ex.Message) ?? "unknown", null);
        }
    }

    private async Task InvokeToolHooksBeforeAsync(
        KernelToolExecutionHookContext context,
        CancellationToken cancellationToken)
    {
        foreach (var hook in toolExecutionHooks)
        {
            try
            {
                await hook.OnBeforeExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // hook 不应影响主链路。
            }
        }
    }

    private async Task<KernelToolExecutionHookAfterDecision> InvokeToolHooksAfterAsync(
        KernelToolExecutionHookContext context,
        KernelToolResult result,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        foreach (var hook in toolExecutionHooks)
        {
            try
            {
                var decision = await hook.OnAfterExecuteAsync(context, result, duration, cancellationToken)
                    .ConfigureAwait(false);
                if (decision.ShouldAbort)
                {
                    var detail = Normalize(decision.ErrorMessage) ?? "after_tool_use hook aborted operation";
                    return KernelToolExecutionHookAfterDecision.Abort(
                        $"after_tool_use hook '{hook.Name}' failed and aborted operation: {detail}");
                }
            }
            catch
            {
                // hook 不应影响主链路。
            }
        }

        return KernelToolExecutionHookAfterDecision.Continue;
    }

    private async Task InvokeToolHooksErrorAsync(
        KernelToolExecutionHookContext context,
        string error,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        foreach (var hook in toolExecutionHooks)
        {
            try
            {
                await hook.OnExecuteErrorAsync(context, error, duration, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // hook 不应影响主链路。
            }
        }
    }

    private static string? ReadString(JsonElement json, string propertyName)
        => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(propertyName, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                JsonValueKind.Null => null,
                _ => null,
            }
            : null;

    private static bool? ReadBool(JsonElement json, string propertyName)
        => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(propertyName, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
                _ => null,
            }
            : null;

    private static string? Normalize(string? value) => KernelToolJsonHelpers.Normalize(value);
}
