using System.Diagnostics;
using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelCommittedUnifiedExecExecutor
{
    public static async Task<KernelToolResult> ExecuteCommandAsync(
        JsonElement arguments,
        KernelToolCallContext context,
        KernelCommittedUnifiedExecProcessManager manager,
        CancellationToken cancellationToken)
    {
        if (!KernelUnifiedExecToolHelpers.TryResolveCommand(arguments, context.AllowLoginShell, out var command, out var commandError))
        {
            return new KernelToolResult(false, commandError ?? "exec_command requires a non-empty command.");
        }

        var commandPreview = string.Join(' ', command);
        var cwd = KernelUnifiedExecToolHelpers.ResolveCwd(
            context.Cwd,
            KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "cwd")));
        if (!KernelToolSandboxResolver.TryResolve(arguments, context, cwd, out var sandboxPolicy, out var sandboxMode, out var sandboxError))
        {
            return new KernelToolResult(false, sandboxError ?? "tool sandbox override rejected");
        }

        if (!KernelUnifiedExecAvailability.IsAllowed(sandboxPolicy, sandboxMode, context.WindowsSandboxLevel))
        {
            return new KernelToolResult(false, KernelUnifiedExecAvailability.BuildBlockedMessage());
        }

        IKernelManagedNetworkExecutionLease managedNetworkLease = KernelManagedNetworkExecutionLeaseDefaults.Inactive;
        try
        {
            try
            {
                var skillMetadata = KernelSkillMetadataResolver.TryResolveForCommand(command, cwd);
                managedNetworkLease = context.RuntimeServices?.BeginManagedNetworkExecution is not null && !string.IsNullOrWhiteSpace(context.ItemId)
                    ? await context.RuntimeServices.BeginManagedNetworkExecution(
                        new KernelManagedNetworkExecutionRequest(
                            context.ThreadId,
                            context.TurnId,
                            context.ItemId!,
                            commandPreview,
                            cwd,
                            sandboxPolicy,
                            sandboxMode,
                            context.ApprovalPolicy,
                            skillMetadata?.ManagedNetworkOverride?.AllowedDomains,
                            skillMetadata?.ManagedNetworkOverride?.DeniedDomains),
                        cancellationToken).ConfigureAwait(false)
                    : KernelManagedNetworkExecutionLeaseDefaults.Inactive;
            }
            catch (Exception ex)
            {
                return new KernelToolResult(false, $"managed network proxy failed: {ex.Message}");
            }

            var sandboxDecision = KernelSandboxEnforcer.EvaluateCommand(
                command,
                commandPreview,
                cwd,
                sandboxPolicy,
                sandboxMode,
                bypassSandbox: false,
                allowManagedNetwork: managedNetworkLease.IsActive);
            if (!sandboxDecision.Allowed)
            {
                return new KernelToolResult(false, $"exec_command blocked by sandbox: {sandboxDecision.Reason}");
            }

            var sessionId = manager.NextSessionId();
            KernelCommittedUnifiedExecSession session;
            try
            {
                var environment = managedNetworkLease.ApplyToEnvironment(KernelShellEnvironmentBuilder.CreateEnvironment(context.ShellEnvironmentPolicy, context.ThreadId));
                session = KernelCommittedUnifiedExecSession.Start(command, cwd, environment, managedNetworkLease);
                managedNetworkLease = KernelManagedNetworkExecutionLeaseDefaults.Inactive;
            }
            catch (Exception ex)
            {
                return new KernelToolResult(false, $"exec_command failed: {ex.Message}");
            }

            manager.StoreSession(sessionId, session);
            var waitStopwatch = Stopwatch.StartNew();
            await session.WaitForOutputOrExitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            var output = session.ReadNewOutput(KernelToolJsonHelpers.ReadInt(arguments, "max_output_tokens"), out var originalTokenCount);
            var payload = KernelUnifiedExecToolHelpers.BuildPayload(
                sessionId,
                session.HasExited,
                session.ExitCode,
                output,
                originalTokenCount,
                waitStopwatch.Elapsed);
            return new KernelToolResult(true, payload);
        }
        finally
        {
            await managedNetworkLease.DisposeAsync().ConfigureAwait(false);
        }
    }

    public static async Task<KernelToolResult> WriteStdinAsync(
        JsonElement arguments,
        KernelToolCallContext context,
        KernelCommittedUnifiedExecProcessManager manager,
        CancellationToken cancellationToken)
    {
        if (!KernelUnifiedExecAvailability.IsAllowed(context.SandboxPolicy, context.SandboxMode, context.WindowsSandboxLevel))
        {
            return new KernelToolResult(false, KernelUnifiedExecAvailability.BuildBlockedMessage());
        }

        var sessionId = KernelToolJsonHelpers.ReadInt(arguments, "session_id") ?? KernelToolJsonHelpers.ReadInt(arguments, "sessionId");
        var text = KernelToolJsonHelpers.ReadString(arguments, "chars") ?? KernelToolJsonHelpers.ReadString(arguments, "text");
        var close = KernelToolJsonHelpers.ReadBool(arguments, "close") ?? false;
        if (sessionId is null || string.IsNullOrEmpty(text))
        {
            return new KernelToolResult(false, "write_stdin requires session_id/sessionId and chars/text.");
        }

        if (!manager.TryGetSession(sessionId.Value, out var session) || session is null)
        {
            return new KernelToolResult(false, $"write_stdin session not found: {sessionId}");
        }

        var waitTimeMs = Math.Clamp(KernelToolJsonHelpers.ReadInt(arguments, "yield_time_ms") ?? 1_000, 0, 300_000);
        await session.WriteStdinAsync(text!, close, cancellationToken).ConfigureAwait(false);
        var waitStopwatch = Stopwatch.StartNew();
        await session.WaitForOutputOrExitAsync(TimeSpan.FromMilliseconds(waitTimeMs), cancellationToken).ConfigureAwait(false);
        if (close)
        {
            await session.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }

        var output = session.ReadNewOutput(KernelToolJsonHelpers.ReadInt(arguments, "max_output_tokens"), out var originalTokenCount);
        var payload = KernelUnifiedExecToolHelpers.BuildPayload(
            sessionId.Value,
            session.HasExited,
            session.ExitCode,
            output,
            originalTokenCount,
            waitStopwatch.Elapsed);
        return new KernelToolResult(true, payload);
    }
}

