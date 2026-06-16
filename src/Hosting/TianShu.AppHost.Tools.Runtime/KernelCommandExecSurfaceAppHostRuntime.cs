using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using TianShu.AppHost.Tools;
using TianShu.Execution.Runtime;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelCommandExecThreadSessionSnapshot(
    string? Cwd,
    KernelApprovalPolicy? ApprovalPolicy,
    JsonElement? SandboxPolicy,
    string? SandboxMode,
    KernelShellEnvironmentPolicy? ShellEnvironmentPolicy);

/// <summary>
/// command/exec northbound surface 宿主运行时。
/// Host runtime for the command/exec northbound surface.
/// </summary>
internal sealed class KernelCommandExecSurfaceAppHostRuntime
{
    private readonly Func<string, KernelCommandExecThreadSessionSnapshot?> tryGetThreadSessionSnapshot;
    private readonly Func<string?, KernelResolvedPermissionRuntimeSettings> resolveConfiguredPermissionSettings;
    private readonly KernelExecPolicyManager execPolicyManager;
    private readonly KernelManagedNetworkAppHostRuntime managedNetworkAppHostRuntime;
    private readonly KernelCommandExecAppHostRuntime commandExecAppHostRuntime;
    private readonly KernelToolItemLifecycleAppHostRuntime toolItemLifecycleAppHostRuntime;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> commandApprovalSessionKeysByThread;
    private readonly Func<string, object, string, CancellationToken, Task<JsonElement>> sendServerRequestAsync;
    private readonly Func<IReadOnlyList<string>, string?, int?, IReadOnlyDictionary<string, string>?, CancellationToken, Task<KernelCommandRunResult>> executeCommandAsync;
    private readonly Func<IReadOnlyList<string>, string?, string, string, IReadOnlyDictionary<string, string>?, KernelManagedNetworkExecutionLease?, Func<Process, Task>?, string?, string?, string?, Task<int>> startBackgroundCommandAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;

    public KernelCommandExecSurfaceAppHostRuntime(
        Func<string, KernelCommandExecThreadSessionSnapshot?> tryGetThreadSessionSnapshot,
        Func<string?, KernelResolvedPermissionRuntimeSettings> resolveConfiguredPermissionSettings,
        KernelExecPolicyManager execPolicyManager,
        KernelManagedNetworkAppHostRuntime managedNetworkAppHostRuntime,
        KernelCommandExecAppHostRuntime commandExecAppHostRuntime,
        KernelToolItemLifecycleAppHostRuntime toolItemLifecycleAppHostRuntime,
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> commandApprovalSessionKeysByThread,
        Func<string, object, string, CancellationToken, Task<JsonElement>> sendServerRequestAsync,
        Func<IReadOnlyList<string>, string?, int?, IReadOnlyDictionary<string, string>?, CancellationToken, Task<KernelCommandRunResult>> executeCommandAsync,
        Func<IReadOnlyList<string>, string?, string, string, IReadOnlyDictionary<string, string>?, KernelManagedNetworkExecutionLease?, Func<Process, Task>?, string?, string?, string?, Task<int>> startBackgroundCommandAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync)
    {
        this.tryGetThreadSessionSnapshot = tryGetThreadSessionSnapshot;
        this.resolveConfiguredPermissionSettings = resolveConfiguredPermissionSettings;
        this.execPolicyManager = execPolicyManager;
        this.managedNetworkAppHostRuntime = managedNetworkAppHostRuntime;
        this.commandExecAppHostRuntime = commandExecAppHostRuntime;
        this.toolItemLifecycleAppHostRuntime = toolItemLifecycleAppHostRuntime;
        this.commandApprovalSessionKeysByThread = commandApprovalSessionKeysByThread;
        this.sendServerRequestAsync = sendServerRequestAsync;
        this.executeCommandAsync = executeCommandAsync;
        this.startBackgroundCommandAsync = startBackgroundCommandAsync;
        this.writeResultAsync = writeResultAsync;
        this.writeErrorAsync = writeErrorAsync;
        this.writeNotificationAsync = writeNotificationAsync;
    }

    public async Task HandleCommandExecAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var commandArgs = KernelToolJsonHelpers.ReadStringArray(@params, "command");
        string commandPreview;
        string? commandText = null;
        if (commandArgs.Count > 0)
        {
            commandPreview = string.Join(' ', commandArgs);
        }
        else
        {
            commandText = ReadString(@params, "command");
            if (string.IsNullOrWhiteSpace(commandText))
            {
                await writeErrorAsync(id, -32600, "command must not be empty", cancellationToken).ConfigureAwait(false);
                return;
            }

            commandPreview = commandText!;
        }

        var threadId = ReadString(@params, "threadId") ?? "thread_command_exec";
        var turnId = ReadString(@params, "turnId") ?? "turn_command_exec";
        var itemId = ReadString(@params, "itemId") ?? $"cmd_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}";
        var clientProcessId = Normalize(ReadString(@params, "processId"));
        var tty = ReadBool(@params, "tty") ?? false;
        var streamStdin = ReadBool(@params, "streamStdin") ?? false;
        var streamStdoutStderr = ReadBool(@params, "streamStdoutStderr") ?? false;
        var outputBytesCap = ReadInt(@params, "outputBytesCap");
        var disableOutputCap = ReadBool(@params, "disableOutputCap") ?? false;
        var disableTimeout = ReadBool(@params, "disableTimeout") ?? false;
        var timeoutMs = ReadInt(@params, "timeoutMs");
        var background = ReadBool(@params, "background") ?? false;
        var hasSize = @params.TryGetProperty("size", out var sizeElement)
                      && sizeElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
        if (!KernelCommandExecRequestHelpers.TryValidateCommandExecV2Params(
                clientProcessId,
                tty,
                streamStdin,
                streamStdoutStderr,
                background,
                disableTimeout,
                timeoutMs,
                disableOutputCap,
                outputBytesCap,
                hasSize,
                out var validationErrorCode,
                out var validationErrorMessage))
        {
            await writeErrorAsync(id, validationErrorCode, validationErrorMessage ?? "command/exec request is invalid", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!KernelCommandExecRequestHelpers.TryReadCommandExecEnvOverrides(@params, out var envOverrides, out var envError))
        {
            await writeErrorAsync(id, -32602, envError ?? "command/exec env is invalid", cancellationToken).ConfigureAwait(false);
            return;
        }

        KernelCommandExecTerminalSize? terminalSize = null;
        if (hasSize && !KernelCommandExecRequestHelpers.TryReadCommandExecTerminalSize(sizeElement, out terminalSize, out var sizeError))
        {
            await writeErrorAsync(id, -32602, sizeError ?? "command/exec size is invalid", cancellationToken).ConfigureAwait(false);
            return;
        }

        var effectiveStreamStdin = tty || streamStdin;
        var effectiveStreamStdoutStderr = tty || streamStdoutStderr;
        var trackedCommandExec = KernelCommandExecRequestHelpers.ShouldUseTrackedCommandExec(clientProcessId, tty, effectiveStreamStdin, effectiveStreamStdoutStderr);
        var notificationProcessId = clientProcessId ?? $"proc_{Guid.NewGuid():N}";
        var threadSession = tryGetThreadSessionSnapshot(threadId);
        var cwd = ReadString(@params, "cwd") ?? threadSession?.Cwd;
        var resolvedCwd = string.IsNullOrWhiteSpace(cwd) ? Environment.CurrentDirectory : cwd!;
        var configuredPermissions = resolveConfiguredPermissionSettings(resolvedCwd);
        if (commandArgs.Count == 0)
        {
            var requestedLogin = ReadBool(@params, "login");
            if (!KernelShellCommandBuilder.TryResolveUseLoginShell(requestedLogin, configuredPermissions.AllowLoginShell, out var useLoginShell, out var loginError))
            {
                await writeErrorAsync(id, -32600, loginError ?? "invalid login shell configuration", cancellationToken).ConfigureAwait(false);
                return;
            }

            commandArgs = KernelShellCommandBuilder.BuildDefaultCommand(commandText!, useLoginShell);
        }

        var approvalPolicy = Normalize(ReadString(@params, "approvalPolicy"))
            ?? threadSession?.ApprovalPolicy
            ?? configuredPermissions.ApprovalPolicy;
        var approved = ReadBool(@params, "approved") ?? false;
        JsonElement? configuredSandboxPolicy = configuredPermissions.SandboxPolicy;
        var sandboxPolicy = TryReadSandboxPolicy(@params, "sandboxPolicy", "sandbox")
            ?? threadSession?.SandboxPolicy
            ?? configuredSandboxPolicy;
        var sandboxMode = sandboxPolicy.HasValue
            ? ResolveSandboxMode(sandboxPolicy.Value)
            : threadSession?.SandboxMode ?? configuredPermissions.SandboxMode;
        var commandApprovalSessionKey = KernelCommandApprovalUtilities.BuildCommandApprovalSessionKey(
            commandArgs,
            resolvedCwd,
            tty,
            sandboxPermissionsValue: null,
            additionalPermissions: null);
        var isSessionApproved = IsCommandApprovalAcceptedForSession(threadId, commandApprovalSessionKey);
        var effectiveApproved = approved || isSessionApproved;

        await writeNotificationAsync("item/commandExecution/terminalInteraction", new
        {
            threadId,
            turnId,
            itemId,
            processId = notificationProcessId,
            stdin = commandPreview,
        }, cancellationToken).ConfigureAwait(false);

        async Task WriteCommandExecutionFailureAsync(string stderr, string status)
        {
            await toolItemLifecycleAppHostRuntime.EmitCommandExecutionCompletedNotificationAsync(
                threadId,
                turnId,
                itemId,
                commandPreview,
                resolvedCwd,
                processId: null,
                status,
                aggregatedOutput: null,
                exitCode: null,
                durationMs: null,
                CancellationToken.None).ConfigureAwait(false);

            await writeResultAsync(id, new
            {
                exitCode = -1,
                stdout = string.Empty,
                stderr,
            }, cancellationToken).ConfigureAwait(false);
        }

        var execPolicyDecision = execPolicyManager.EvaluateCommand(
            commandArgs,
            commandPreview,
            approvalPolicy,
            sandboxMode,
            effectiveApproved);

        if (execPolicyDecision.Kind == KernelExecPolicyDecisionKind.Forbidden)
        {
            await WriteCommandExecutionFailureAsync(
                KernelCommandApprovalUtilities.BuildCommandPolicyDeniedMessage(execPolicyDecision.Reason, sandboxMode),
                status: "declined").ConfigureAwait(false);
            return;
        }

        if (execPolicyDecision.Kind == KernelExecPolicyDecisionKind.NeedsApproval)
        {
            var (decision, requestError, applyAmendment) = await RequestCommandExecutionApprovalAsync(
                threadId,
                turnId,
                itemId,
                commandArgs,
                commandPreview,
                resolvedCwd,
                reason: execPolicyDecision.Reason,
                availableDecisions: KernelCommandApprovalUtilities.BuildCommandExecutionAvailableDecisions(execPolicyDecision.ProposedAmendment),
                proposedAmendment: execPolicyDecision.ProposedAmendment,
                approvalId: null,
                additionalPermissions: null,
                commandActions: null,
                cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(requestError))
            {
                await WriteCommandExecutionFailureAsync($"审批请求失败：{requestError}", status: "failed").ConfigureAwait(false);
                return;
            }

            if (!KernelCommandApprovalUtilities.TryResolveCommandApprovalDecision(decision, out var approvedForSession))
            {
                await WriteCommandExecutionFailureAsync(
                    KernelCommandApprovalUtilities.BuildCommandApprovalDeclinedMessage(execPolicyDecision.Reason, sandboxMode),
                    status: "declined").ConfigureAwait(false);
                return;
            }

            if (approvedForSession)
            {
                MarkCommandApprovalAcceptedForSession(threadId, commandApprovalSessionKey);
            }

            if (applyAmendment is not null)
            {
                await execPolicyManager.AppendAmendmentAndUpdateAsync(applyAmendment, cancellationToken).ConfigureAwait(false);
            }

            effectiveApproved = true;
        }

        var managedNetworkLease = KernelManagedNetworkExecutionLease.Inactive();
        try
        {
            try
            {
                var skillMetadata = KernelSkillMetadataResolver.TryResolveForCommand(commandArgs, resolvedCwd);
                managedNetworkLease = await managedNetworkAppHostRuntime.BeginExecutionAsync(
                    new KernelManagedNetworkExecutionRequest(
                        threadId,
                        turnId,
                        itemId,
                        commandPreview,
                        resolvedCwd,
                        sandboxPolicy,
                        sandboxMode,
                        approvalPolicy,
                        skillMetadata?.ManagedNetworkOverride?.AllowedDomains,
                        skillMetadata?.ManagedNetworkOverride?.DeniedDomains),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await WriteCommandExecutionFailureAsync($"managed network proxy failed: {ex.Message}", status: "failed").ConfigureAwait(false);
                return;
            }

            var sandboxDecision = KernelSandboxEnforcer.EvaluateCommand(
                commandArgs,
                commandPreview,
                resolvedCwd,
                sandboxPolicy,
                sandboxMode,
                bypassSandbox: effectiveApproved || execPolicyDecision.BypassSandbox,
                allowManagedNetwork: managedNetworkLease.IsActive);
            if (!sandboxDecision.Allowed)
            {
                if (KernelApprovalPolicyHelpers.IsNever(approvalPolicy))
                {
                    await WriteCommandExecutionFailureAsync(
                        KernelCommandApprovalUtilities.BuildCommandPolicyDeniedMessage(sandboxDecision.Reason ?? "sandbox_policy_denied", sandboxMode),
                        status: "declined").ConfigureAwait(false);
                    return;
                }

                var (decision, requestError, _) = await RequestCommandExecutionApprovalAsync(
                    threadId,
                    turnId,
                    itemId,
                    commandArgs,
                    commandPreview,
                    resolvedCwd,
                    reason: sandboxDecision.Reason ?? "sandbox_policy_denied",
                    availableDecisions: KernelCommandApprovalUtilities.BuildCommandExecutionAvailableDecisions(proposedAmendment: null),
                    proposedAmendment: null,
                    approvalId: null,
                    additionalPermissions: null,
                    commandActions: null,
                    cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(requestError))
                {
                    await WriteCommandExecutionFailureAsync($"审批请求失败：{requestError}", status: "failed").ConfigureAwait(false);
                    return;
                }

                if (!KernelCommandApprovalUtilities.TryResolveCommandApprovalDecision(decision, out var approvedForSession))
                {
                    await WriteCommandExecutionFailureAsync(
                        KernelCommandApprovalUtilities.BuildCommandPolicyDeniedMessage(sandboxDecision.Reason ?? "sandbox_policy_denied", sandboxMode),
                        status: "declined").ConfigureAwait(false);
                    return;
                }

                if (approvedForSession)
                {
                    MarkCommandApprovalAcceptedForSession(threadId, commandApprovalSessionKey);
                }
            }

            var baseEnvironment = KernelShellEnvironmentBuilder.CreateEnvironment(threadSession?.ShellEnvironmentPolicy ?? configuredPermissions.ShellEnvironmentPolicy, threadId);
            var environment = KernelCommandExecRequestHelpers.MergeCommandExecEnvironment(managedNetworkLease.ApplyToEnvironment(baseEnvironment), envOverrides);

            if (background)
            {
                var commandExecutionStopwatch = Stopwatch.StartNew();
                var backgroundPid = await startBackgroundCommandAsync(
                    commandArgs,
                    resolvedCwd,
                    threadId,
                    notificationProcessId,
                    environment,
                    managedNetworkLease,
                    async process =>
                    {
                        var status = KernelToolItemLifecycleHelpers.TryGetCommandExecutionStatusFromExitCode(process.ExitCode);
                        await toolItemLifecycleAppHostRuntime.EmitCommandExecutionCompletedNotificationAsync(
                            threadId,
                            turnId,
                            itemId,
                            commandPreview,
                            resolvedCwd,
                            notificationProcessId,
                            status,
                            aggregatedOutput: null,
                            exitCode: process.ExitCode,
                            durationMs: (long)Math.Max(0, commandExecutionStopwatch.Elapsed.TotalMilliseconds),
                            CancellationToken.None).ConfigureAwait(false);
                    },
                    turnId,
                    itemId,
                    commandPreview).ConfigureAwait(false);
                managedNetworkLease = KernelManagedNetworkExecutionLease.Inactive();
                await writeResultAsync(id, new
                {
                    started = true,
                    processId = notificationProcessId,
                    pid = backgroundPid,
                    exitCode = -1,
                    stdout = string.Empty,
                    stderr = string.Empty,
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (trackedCommandExec)
            {
                await commandExecAppHostRuntime.StartTrackedCommandExecAsync(
                    id,
                    clientProcessId!,
                    commandArgs,
                    resolvedCwd,
                    environment,
                    tty,
                    terminalSize,
                    effectiveStreamStdin,
                    effectiveStreamStdoutStderr,
                    timeoutMs,
                    disableTimeout,
                    disableOutputCap ? null : outputBytesCap,
                    managedNetworkLease,
                    threadId,
                    turnId,
                    itemId,
                    commandPreview,
                    cancellationToken).ConfigureAwait(false);
                managedNetworkLease = KernelManagedNetworkExecutionLease.Inactive();
                return;
            }

            var effectiveTimeoutMs = disableTimeout ? (int?)null : timeoutMs ?? 30000;
            var commandExecutionStartedAt = Stopwatch.StartNew();
            await toolItemLifecycleAppHostRuntime.EmitCommandExecutionStartedNotificationAsync(
                threadId,
                turnId,
                itemId,
                commandPreview,
                resolvedCwd,
                processId: null,
                CancellationToken.None).ConfigureAwait(false);

            var runResult = await executeCommandAsync(commandArgs, resolvedCwd, effectiveTimeoutMs, environment, cancellationToken).ConfigureAwait(false);
            runResult = KernelCommandExecAppHostRuntime.ApplyCommandExecOutputCap(runResult, outputBytesCap, disableOutputCap);
            if (managedNetworkLease.HasRejectedOutcome)
            {
                var outcomeMessage = managedNetworkLease.ConsumeOutcomeMessage();
                if (!string.IsNullOrWhiteSpace(outcomeMessage))
                {
                    var stdErr = string.IsNullOrWhiteSpace(runResult.StdErr)
                        ? outcomeMessage
                        : runResult.StdErr + Environment.NewLine + outcomeMessage;
                    var exitCode = runResult.ExitCode == 0 ? -1 : runResult.ExitCode;
                    runResult = runResult with
                    {
                        ExitCode = exitCode,
                        StdErr = stdErr,
                    };
                }
            }

            if (!string.IsNullOrWhiteSpace(runResult.StdOut))
            {
                await writeNotificationAsync("item/commandExecution/outputDelta", new
                {
                    threadId,
                    turnId,
                    itemId,
                    stream = "stdout",
                    delta = runResult.StdOut,
                }, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(runResult.StdErr))
            {
                await writeNotificationAsync("item/commandExecution/outputDelta", new
                {
                    threadId,
                    turnId,
                    itemId,
                    stream = "stderr",
                    delta = runResult.StdErr,
                }, cancellationToken).ConfigureAwait(false);
            }

            await toolItemLifecycleAppHostRuntime.EmitCommandExecutionCompletedNotificationAsync(
                threadId,
                turnId,
                itemId,
                commandPreview,
                resolvedCwd,
                processId: null,
                KernelToolItemLifecycleHelpers.TryGetCommandExecutionStatusFromExitCode(runResult.ExitCode),
                KernelToolItemLifecycleHelpers.BuildCommandExecutionAggregatedOutput(runResult.StdOut, runResult.StdErr),
                runResult.ExitCode,
                (long)Math.Max(0, commandExecutionStartedAt.Elapsed.TotalMilliseconds),
                CancellationToken.None).ConfigureAwait(false);

            await writeResultAsync(id, new
            {
                exitCode = runResult.ExitCode,
                stdout = runResult.StdOut,
                stderr = runResult.StdErr,
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await managedNetworkLease.DisposeAsync().ConfigureAwait(false);
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
                    skillMetadata = KernelCommandApprovalUtilities.TryResolveCommandExecutionApprovalSkillMetadata(commandArgs, cwd ?? Environment.CurrentDirectory),
                    availableDecisions = availableDecisions.ToArray(),
                    proposedExecpolicyAmendment = proposedAmendment?.CommandPrefix.ToArray(),
                },
                threadId,
                cancellationToken).ConfigureAwait(false);
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

    private static JsonElement? TryReadSandboxPolicy(JsonElement @params, params string[] candidateNames)
    {
        foreach (var name in candidateNames)
        {
            if (!TryReadJsonProperty(@params, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var mode = Normalize(value.GetString()) ?? "workspaceWrite";
                return JsonSerializer.SerializeToElement(new
                {
                    type = mode,
                });
            }

            return value;
        }

        return null;
    }

    private static bool TryReadJsonProperty(JsonElement json, string propertyName, out JsonElement value)
    {
        value = default;
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        value = property.Clone();
        return true;
    }

    private static string? ResolveSandboxMode(JsonElement policy)
    {
        if (policy.ValueKind == JsonValueKind.String)
        {
            return Normalize(policy.GetString());
        }

        return Normalize(ReadString(policy, "type"));
    }

    private static string? ReadString(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.Number when current.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(current.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static bool? ReadBool(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(current.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
