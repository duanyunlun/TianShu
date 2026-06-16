using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TianShu.AppHost.Tools;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelShellRuntimeSupport
{
    private const string ShellDescription =
        "Runs a Powershell command (Windows) and returns its output. Arguments to `shell` will be passed to CreateProcessW(). Most commands should be prefixed with [\"powershell.exe\", \"-Command\"].";

    private const string LocalShellDescription = "Runs a local shell command and returns its output.";

    private const string ShellCommandDescription = "Runs a Powershell command (Windows) and returns its output.";

    internal static JsonElement BuildShellInternalInputSchema()
        => BuildInputSchema(isArrayCommand: true, includeLogin: false, includeAdditionalPermissions: true, includeMacOs: true);

    internal static JsonElement BuildShellModelInputSchema(bool execPermissionApprovalsEnabled)
        => BuildInputSchema(isArrayCommand: true, includeLogin: false, includeAdditionalPermissions: execPermissionApprovalsEnabled, includeMacOs: false);

    internal static ProviderResponsesToolDefinition BuildShellProviderToolDefinition(bool execPermissionApprovalsEnabled)
        => BuildFunctionToolDefinition("shell", ShellDescription, BuildShellModelInputSchema(execPermissionApprovalsEnabled));

    internal static JsonElement BuildLocalShellInternalInputSchema()
        => BuildInputSchema(isArrayCommand: true, includeLogin: false, includeAdditionalPermissions: true, includeMacOs: true);

    internal static JsonElement BuildLocalShellModelInputSchema(bool execPermissionApprovalsEnabled)
        => BuildInputSchema(isArrayCommand: true, includeLogin: false, includeAdditionalPermissions: execPermissionApprovalsEnabled, includeMacOs: false);

    internal static ProviderResponsesToolDefinition BuildLocalShellProviderToolDefinition(bool execPermissionApprovalsEnabled)
        => BuildFunctionToolDefinition("local_shell", LocalShellDescription, BuildLocalShellModelInputSchema(execPermissionApprovalsEnabled));

    internal static JsonElement BuildShellCommandInternalInputSchema()
        => BuildInputSchema(isArrayCommand: false, includeLogin: true, includeAdditionalPermissions: true, includeMacOs: true);

    internal static JsonElement BuildShellCommandModelInputSchema(bool execPermissionApprovalsEnabled)
        => BuildInputSchema(isArrayCommand: false, includeLogin: true, includeAdditionalPermissions: execPermissionApprovalsEnabled, includeMacOs: false);

    internal static ProviderResponsesToolDefinition BuildShellCommandProviderToolDefinition(bool execPermissionApprovalsEnabled)
        => BuildFunctionToolDefinition("shell_command", ShellCommandDescription, BuildShellCommandModelInputSchema(execPermissionApprovalsEnabled));

    private static JsonElement BuildInputSchema(
        bool isArrayCommand,
        bool includeLogin,
        bool includeAdditionalPermissions,
        bool includeMacOs)
    {
        var schema = KernelShellToolSchemaFactory.BuildShellInputSchema(
            includeLogin: includeLogin,
            includeAdditionalPermissions: includeAdditionalPermissions,
            includeMacOs: includeMacOs);
        return InsertCommandProperty(schema, isArrayCommand);
    }

    private static JsonElement InsertCommandProperty(JsonElement schema, bool isArrayCommand)
    {
        using var document = JsonDocument.Parse(schema.GetRawText());
        var properties = document.RootElement.GetProperty("properties").EnumerateObject().ToDictionary(
            static property => property.Name,
            static property => (object?)JsonSerializer.Deserialize<object>(property.Value.GetRawText()));
        properties["command"] = isArrayCommand
            ? new
            {
                description = "The command to execute",
                oneOf = new object[]
                {
                    new
                    {
                        type = "array",
                        items = new { type = "string" },
                    },
                    new
                    {
                        type = "string",
                        description = "JSON 字符串化后的字符串数组，用于兼容会把工具数组参数字符串化的 Anthropic-compatible 网关",
                    },
                },
            }
            : new
            {
                type = "string",
                description = "The shell script to execute in the user's default shell",
            };
        return JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties,
            required = new[] { "command" },
            additionalProperties = false,
        });
    }

    private static ProviderResponsesToolDefinition BuildFunctionToolDefinition(string name, string description, JsonElement inputSchema)
        => new ProviderResponsesFunctionToolDefinition(
            name,
            description,
            inputSchema,
            strict: false);
}

internal static class ShellToolExecutor
{
    public static async Task<KernelToolResult> ExecuteShellAsync(
        JsonElement arguments,
        KernelToolCallContext context,
        KernelExecRunnerDelegate execRunner,
        CancellationToken cancellationToken)
    {
        if (!KernelToolArgumentParser.TryParse(arguments, out KernelShellToolCallArguments? parsedArguments, out var parseError))
        {
            return new KernelToolResult(false, parseError ?? "shell received invalid arguments");
        }

        var parsed = parsedArguments!;
        var command = KernelShellCommandBuilder.NormalizeExplicitCommand(
            NormalizeStringArray(parsed.Command));
        if (command.Count == 0)
        {
            return new KernelToolResult(false, "shell requires a non-empty command.");
        }

        var workdir = KernelToolJsonHelpers.Normalize(parsed.Workdir);
        var timeoutMs = parsed.ResolveTimeoutMs() ?? 30_000;
        var cwd = ResolveWorkdir(context.Cwd, workdir);
        if (!KernelToolSandboxResolver.TryResolve(
                parsed.SandboxPermissions,
                parsed.AdditionalPermissions,
                context,
                cwd,
                out var sandboxPolicy,
                out var sandboxMode,
                out var sandboxError))
        {
            return new KernelToolResult(false, sandboxError ?? "tool sandbox override rejected");
        }

        IKernelManagedNetworkExecutionLease managedNetworkLease = KernelManagedNetworkExecutionLeaseDefaults.Inactive;
        try
        {
            var skillMetadata = KernelSkillMetadataResolver.TryResolveForCommand(command, cwd);
            managedNetworkLease = context.RuntimeServices?.BeginManagedNetworkExecution is not null && !string.IsNullOrWhiteSpace(context.ItemId)
                ? await context.RuntimeServices.BeginManagedNetworkExecution(
                    new KernelManagedNetworkExecutionRequest(
                        context.ThreadId,
                        context.TurnId,
                        context.ItemId!,
                        string.Join(" ", command),
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

        try
        {
            var sandboxDecision = KernelSandboxEnforcer.EvaluateCommand(
                command,
                string.Join(" ", command),
                cwd,
                sandboxPolicy,
                sandboxMode,
                bypassSandbox: false,
                allowManagedNetwork: managedNetworkLease.IsActive);
            if (!sandboxDecision.Allowed)
            {
                return new KernelToolResult(false, $"命令被沙箱策略阻止：{sandboxDecision.Reason ?? sandboxMode ?? "unknown"}");
            }

            var environment = managedNetworkLease.ApplyToEnvironment(KernelShellEnvironmentBuilder.CreateEnvironment(context.ShellEnvironmentPolicy, context.ThreadId));
            var output = await execRunner(command, cwd, timeoutMs, environment, cancellationToken).ConfigureAwait(false);
            output = managedNetworkLease.ApplyOutcome(output);
            var formatted = KernelExecOutputFormatting.FormatStructured(output);
            return output.ExitCode == 0 && !output.TimedOut && !managedNetworkLease.HasRejectedOutcome
                ? new KernelToolResult(true, formatted)
                : new KernelToolResult(false, formatted);
        }
        finally
        {
            await managedNetworkLease.DisposeAsync().ConfigureAwait(false);
        }
    }

    public static async Task<KernelToolResult> ExecuteShellCommandAsync(
        JsonElement arguments,
        KernelToolCallContext context,
        KernelExecRunnerDelegate execRunner,
        CancellationToken cancellationToken)
    {
        if (!KernelToolArgumentParser.TryParse(arguments, out KernelShellCommandToolCallArguments? parsedArguments, out var parseError))
        {
            return new KernelToolResult(false, parseError ?? "shell_command received invalid arguments");
        }

        var parsed = parsedArguments!;
        var commandText = KernelToolJsonHelpers.Normalize(parsed.Command) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return new KernelToolResult(false, "shell_command requires a non-empty command.");
        }

        var workdir = KernelToolJsonHelpers.Normalize(parsed.Workdir);
        var timeoutMs = parsed.ResolveTimeoutMs() ?? 30_000;
        var login = parsed.Login;
        if (!KernelShellCommandBuilder.TryResolveUseLoginShell(login, context.AllowLoginShell, out var useLoginShell, out var loginError))
        {
            return new KernelToolResult(false, loginError!);
        }

        var cwd = ResolveWorkdir(context.Cwd, workdir);
        if (!KernelToolSandboxResolver.TryResolve(
                parsed.SandboxPermissions,
                parsed.AdditionalPermissions,
                context,
                cwd,
                out var sandboxPolicy,
                out var sandboxMode,
                out var sandboxError))
        {
            return new KernelToolResult(false, sandboxError ?? "tool sandbox override rejected");
        }

        var execCommand = KernelShellCommandBuilder.BuildDefaultCommand(commandText, useLoginShell);

        IKernelManagedNetworkExecutionLease managedNetworkLease = KernelManagedNetworkExecutionLeaseDefaults.Inactive;
        try
        {
            var skillMetadata = KernelSkillMetadataResolver.TryResolveForCommand(execCommand, cwd);
            managedNetworkLease = context.RuntimeServices?.BeginManagedNetworkExecution is not null && !string.IsNullOrWhiteSpace(context.ItemId)
                ? await context.RuntimeServices.BeginManagedNetworkExecution(
                    new KernelManagedNetworkExecutionRequest(
                        context.ThreadId,
                        context.TurnId,
                        context.ItemId!,
                        commandText,
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

        try
        {
            var sandboxDecision = KernelSandboxEnforcer.EvaluateCommand(
                execCommand,
                commandText,
                cwd,
                sandboxPolicy,
                sandboxMode,
                bypassSandbox: false,
                allowManagedNetwork: managedNetworkLease.IsActive);
            if (!sandboxDecision.Allowed)
            {
                return new KernelToolResult(false, $"命令被沙箱策略阻止：{sandboxDecision.Reason ?? sandboxMode ?? "unknown"}");
            }

            var environment = managedNetworkLease.ApplyToEnvironment(KernelShellEnvironmentBuilder.CreateEnvironment(context.ShellEnvironmentPolicy, context.ThreadId));
            var output = await execRunner(execCommand, cwd, timeoutMs, environment, cancellationToken).ConfigureAwait(false);
            output = managedNetworkLease.ApplyOutcome(output);
            var formatted = KernelExecOutputFormatting.FormatFreeform(output);
            return output.ExitCode == 0 && !output.TimedOut && !managedNetworkLease.HasRejectedOutcome
                ? new KernelToolResult(true, formatted)
                : new KernelToolResult(false, formatted);
        }
        finally
        {
            await managedNetworkLease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string ResolveWorkdir(string contextCwd, string? workdir)
    {
        var baseDir = string.IsNullOrWhiteSpace(contextCwd) ? Environment.CurrentDirectory : contextCwd;
        if (string.IsNullOrWhiteSpace(workdir))
        {
            return baseDir;
        }

        return Path.GetFullPath(Path.IsPathRooted(workdir) ? workdir : Path.Combine(baseDir, workdir));
    }

    private static List<string> NormalizeStringArray(IEnumerable<string>? values)
    {
        var list = new List<string>();
        if (values is null)
        {
            return list;
        }

        foreach (var value in values)
        {
            var normalized = KernelToolJsonHelpers.Normalize(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                list.Add(normalized!);
            }
        }

        return list;
    }
}









