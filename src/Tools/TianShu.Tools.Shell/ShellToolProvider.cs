using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Tools.Shell;

/// <summary>
/// Shell / Exec 工具域 Provider。
/// Provider for the Shell / Exec tool domain.
/// </summary>
public sealed class ShellToolProvider : ITianShuToolProvider
{
    private static readonly IReadOnlyDictionary<string, ToolDescriptor> Descriptors =
        new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal)
        {
            [ShellToolNames.Shell] = ShellToolDescriptors.BuildDescriptor(
                ShellToolNames.Shell,
                "Shell",
                "Runs a Powershell command (Windows) and returns its output. Arguments to `shell` will be passed to CreateProcessW(). Most commands should be prefixed with [\"powershell.exe\", \"-Command\"].",
                ShellToolSchemas.BuildShellInputSchema(isArrayCommand: true, includeLogin: false, includeAdditionalPermissions: true)),
            [ShellToolNames.LocalShell] = ShellToolDescriptors.BuildDescriptor(
                ShellToolNames.LocalShell,
                "Local Shell",
                "Runs a local shell command and returns its output.",
                ShellToolSchemas.BuildShellInputSchema(isArrayCommand: true, includeLogin: false, includeAdditionalPermissions: true),
                requirements: ShellToolDescriptors.BuildLocalShellRequirements()),
            [ShellToolNames.ShellCommand] = ShellToolDescriptors.BuildDescriptor(
                ShellToolNames.ShellCommand,
                "Shell Command",
                "Runs a Powershell command (Windows) and returns its output.",
                ShellToolSchemas.BuildShellInputSchema(isArrayCommand: false, includeLogin: true, includeAdditionalPermissions: true)),
            [ShellToolNames.ExecCommand] = ShellToolDescriptors.BuildDescriptor(
                ShellToolNames.ExecCommand,
                "Exec Command",
                "Starts a unified exec session and returns a session id with initial output.",
                ShellToolSchemas.UnifiedExecCommandInputSchema,
                outputSchema: ShellToolSchemas.UnifiedExecOutputSchema,
                requirements: ShellToolDescriptors.BuildUnifiedExecRequirements(),
                concurrencyClass: ToolConcurrencyClass.Sequential),
            [ShellToolNames.WriteStdin] = ShellToolDescriptors.BuildDescriptor(
                ShellToolNames.WriteStdin,
                "Write Stdin",
                "Writes text to an existing unified exec session and returns new output.",
                ShellToolSchemas.WriteStdinInputSchema,
                outputSchema: ShellToolSchemas.UnifiedExecOutputSchema,
                requirements: ShellToolDescriptors.BuildUnifiedExecRequirements(),
                concurrencyClass: ToolConcurrencyClass.Sequential),
        };

    public IReadOnlyList<ToolDescriptor> DescribeTools(TianShuToolRegistrationContext context)
    {
        _ = context;
        return Descriptors.Values.ToArray();
    }

    public ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context)
    {
        _ = context;
        return Descriptors.ContainsKey(toolKey)
            ? new ShellToolHandler(Descriptors[toolKey])
            : throw new InvalidOperationException($"Unknown shell tool: {toolKey}");
    }
}

internal static class ShellToolNames
{
    public const string Shell = "shell";
    public const string LocalShell = "local_shell";
    public const string ShellCommand = "shell_command";
    public const string ExecCommand = "exec_command";
    public const string WriteStdin = "write_stdin";
    public const string ImplementationId = "tianshu.tools.shell";
}

internal sealed class ShellToolHandler : ITianShuToolHandler
{
    public ShellToolHandler(ToolDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public ToolDescriptor Descriptor { get; }

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (context.ShellServices is null)
        {
            return ShellToolResultFactory.Failure(request, "shell services unavailable");
        }

        var result = await context.ShellServices
            .InvokeShellToolAsync(new TianShuShellToolRequest(request.ToolKey, request.Input), cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return ShellToolResultFactory.Failure(request, result.OutputText);
        }

        return ShellToolResultFactory.Success(
            request,
            result.StructuredOutput ?? StructuredValue.FromString(result.OutputText));
    }
}

internal static class ShellToolDescriptors
{
    public static ToolDescriptor BuildDescriptor(
        string name,
        string displayName,
        string description,
        JsonElement inputSchema,
        JsonElement? outputSchema = null,
        IReadOnlyList<ToolRuntimeRequirement>? requirements = null,
        ToolConcurrencyClass concurrencyClass = ToolConcurrencyClass.SharedReadOnly)
        => new(
            name,
            displayName,
            description,
            capabilities:
            [
                new ToolCapability("process-exec", "Execute local shell commands through host-governed policy."),
            ],
            approvalRequirement: ToolApprovalRequirement.Required,
            concurrencyClass: concurrencyClass,
            implementationBinding: new ToolImplementationBinding(
                name,
                ToolImplementationKind.ExternalProcess,
                implementationId: ShellToolNames.ImplementationId,
                requirements: requirements ?? [new ToolRuntimeRequirement("powershell", "PowerShell")]),
            inputSchema: inputSchema,
            outputSchema: outputSchema);

    public static IReadOnlyList<ToolRuntimeRequirement> BuildLocalShellRequirements()
        =>
        [
            new ToolRuntimeRequirement("bash", "bash", "POSIX shell runtime.", required: !OperatingSystem.IsWindows()),
            new ToolRuntimeRequirement("powershell", "PowerShell", "Windows shell runtime.", required: OperatingSystem.IsWindows()),
        ];

    public static IReadOnlyList<ToolRuntimeRequirement> BuildUnifiedExecRequirements()
        =>
        [
            new ToolRuntimeRequirement("powershell", "PowerShell", "Windows unified exec runtime.", required: OperatingSystem.IsWindows()),
            new ToolRuntimeRequirement("bash", "bash", "POSIX unified exec runtime.", required: !OperatingSystem.IsWindows()),
        ];
}

internal static class ShellToolResultFactory
{
    public static ToolInvocationResult Success(ToolInvocationRequest request, StructuredValue payload)
        => new(
            request.CallId,
            request.ToolKey,
            [new ToolStreamItem("text", payload, isTerminal: true)]);

    public static ToolInvocationResult Failure(ToolInvocationRequest request, string message)
        => new(
            request.CallId,
            request.ToolKey,
            failure: new ToolInvocationFailure($"{request.ToolKey}.invalid_request", message));
}

internal static class ShellToolSchemas
{
    public static readonly JsonElement UnifiedExecOutputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            chunk_id = new { type = "string" },
            wall_time_seconds = new { type = "number" },
            exit_code = new { type = "number" },
            session_id = new { type = "number" },
            original_token_count = new { type = "number" },
            output = new { type = "string" },
        },
        required = new[] { "wall_time_seconds", "output" },
        additionalProperties = false,
    });

    public static readonly JsonElement UnifiedExecCommandInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            command = new { oneOf = new object[] { new { type = "string" }, new { type = "array", items = new { type = "string" } } } },
            cmd = new { type = "string" },
            cwd = new { type = "string" },
            login = new { type = "boolean" },
            max_output_tokens = new { type = "number" },
        },
        required = Array.Empty<string>(),
    });

    public static readonly JsonElement WriteStdinInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            session_id = new { type = "number" },
            sessionId = new { type = "number" },
            text = new { type = "string" },
            chars = new { type = "string" },
            close = new { type = "boolean" },
            yield_time_ms = new { type = "number" },
            max_output_tokens = new { type = "number" },
        },
        required = Array.Empty<string>(),
    });

    public static JsonElement BuildShellInputSchema(bool isArrayCommand, bool includeLogin, bool includeAdditionalPermissions)
    {
        var properties = new Dictionary<string, object?>
        {
            ["workdir"] = new
            {
                type = "string",
                description = "The working directory to execute the command in",
            },
            ["timeout_ms"] = new
            {
                type = "number",
                description = "The timeout for the command in milliseconds",
            },
            ["sandbox_permissions"] = new
            {
                type = "string",
                description = includeAdditionalPermissions
                    ? "Sandbox permissions for the command. Use \"with_additional_permissions\" to request additional sandboxed filesystem or network permissions (preferred), or \"require_escalated\" to request running without sandbox restrictions; defaults to \"use_default\"."
                    : "Sandbox permissions for the command. Set to \"require_escalated\" to request running without sandbox restrictions; defaults to \"use_default\".",
            },
            ["justification"] = new
            {
                type = "string",
                description = "Only set if sandbox_permissions is \"require_escalated\". Request approval from the user to run this command outside the sandbox.",
            },
            ["prefix_rule"] = new
            {
                type = "array",
                description = "Only specify when sandbox_permissions is `require_escalated`. Suggest a prefix command pattern that will allow similar requests in the future.",
                items = new { type = "string" },
            },
        };

        if (includeLogin)
        {
            properties["login"] = new
            {
                type = "boolean",
                description = "Whether to run the shell with login shell semantics. Defaults to the configured allow_login_shell setting.",
            };
        }

        if (includeAdditionalPermissions)
        {
            properties["additional_permissions"] = BuildAdditionalPermissionsSchema();
        }

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

    private static object BuildAdditionalPermissionsSchema()
        => new
        {
            type = "object",
            additionalProperties = false,
            properties = new Dictionary<string, object?>
            {
                ["network"] = new
                {
                    type = "object",
                    additionalProperties = false,
                    properties = new Dictionary<string, object?>
                    {
                        ["enabled"] = new
                        {
                            type = "boolean",
                            description = "Set to true to enable network access for this command.",
                        },
                    },
                },
                ["file_system"] = new
                {
                    type = "object",
                    additionalProperties = false,
                    properties = new Dictionary<string, object?>
                    {
                        ["read"] = new
                        {
                            type = "array",
                            description = "Additional filesystem paths to grant read access for this command.",
                            items = new { type = "string" },
                        },
                        ["write"] = new
                        {
                            type = "array",
                            description = "Additional filesystem paths to grant write access for this command.",
                            items = new { type = "string" },
                        },
                    },
                },
            },
        };
}
