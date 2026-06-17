using TianShu.Configuration;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using TianShu.Execution.Runtime;
using TianShu.IdentityMemory;
using TianShu.Provider.Abstractions;
using TianShu.Tools.FileSystem;
using TianShu.Tools.FileSystemMutating;
using TianShu.Tools.Memory;
using TianShu.Tools.McpResources;
using TianShu.Tools.Shell;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TianShu.RuntimeComposition;

/// <summary>
/// CLI opt-in Kernel→Runtime turn loop 的运行时绑定组合入口。
/// Runtime binding composition entry point for the CLI opt-in Kernel-to-Runtime turn loop.
/// </summary>
public static class KernelRuntimeTurnLoopComposition
{
    private static readonly HttpClient SharedProviderHttpClient = new();

    private static readonly string[] ReadOnlyToolIds =
    [
        "read_file",
        "list_dir",
        "grep",
        "glob",
    ];

    private static readonly string[] WorkspaceWriteToolIds =
    [
        "write",
        "apply_patch",
    ];

    private static readonly string[] ShellToolIds =
    [
        "shell_command",
    ];

    private static readonly string[] McpResourceToolIds =
    [
        "list_mcp_resources",
        "list_mcp_resource_templates",
        "read_mcp_resource",
    ];

    private static readonly string[] MemoryToolIds =
    [
        "memory_search",
        "memory_explain_overlay",
        "memory_feedback",
    ];

    /// <summary>
    /// 创建带真实 provider module 与只读内置工具的执行运行时。
    /// Creates an execution runtime with the real provider module and read-only built-in tools.
    /// </summary>
    public static TianShuExecutionRuntime CreateRuntime(
        ResolvedTianShuConfig config,
        string providerModuleId = "provider.default",
        bool includeWorkspaceWrite = false,
        bool includeShell = false,
        bool includeMcp = false,
        ITianShuMcpResourceToolServices? mcpResourceServices = null,
        IReadOnlyList<TianShuMcpToolDescriptor>? mcpToolDescriptors = null,
        ITianShuMcpToolServices? mcpToolServices = null,
        bool includeMemory = false,
        ITianShuMemoryToolServices? memoryToolServices = null,
        HttpMessageHandler? providerHttpHandler = null,
        Func<string, string?>? readEnvironmentVariable = null,
        HttpClient? providerHttpClient = null,
        IExecutionRuntimeMetricsSink? metricsSink = null,
        IReadOnlyDictionary<string, ISubAgentModule>? subAgentModules = null,
        IReadOnlyDictionary<string, IMemoryModule>? memoryModules = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (providerHttpHandler is not null && providerHttpClient is not null)
        {
            throw new ArgumentException("不能同时传入 providerHttpHandler 与 providerHttpClient。");
        }

        var memoryBindings = includeMemory
            ? CreateMemoryRuntimeBindings(memoryToolServices, memoryModules)
            : null;
        var tools = CreateTools(
            includeWorkspaceWrite,
            includeShell,
            includeMcp,
            mcpResourceServices,
            mcpToolDescriptors,
            mcpToolServices,
            includeMemory,
            memoryBindings?.ToolServices);
        var providerToolDescriptors = tools.Values.Select(static tool => tool.Descriptor)
            .Concat(subAgentModules is null ? Array.Empty<ToolDescriptor>() : [CreateSubAgentProviderToolDescriptor()])
            .ToArray();
        var provider = new ConfiguredResponsesProviderModule(
            providerModuleId,
            config,
            providerToolDescriptors,
            providerHttpHandler,
            readEnvironmentVariable,
            providerHttpClient ?? (providerHttpHandler is null ? SharedProviderHttpClient : null));

        return new TianShuExecutionRuntime(
            new ExecutionRuntimeStepBindingRegistry(
                providers: new Dictionary<string, IProviderModule>(StringComparer.Ordinal)
                {
                    [providerModuleId] = provider,
                },
                tools: tools,
                subAgentModules: subAgentModules,
                memoryModules: memoryBindings?.Modules),
            metricsSink);
    }

    /// <summary>
    /// 创建产品入口可复用的 Kernel/Runtime 执行循环。
    /// Creates the Kernel/Runtime execution loop used by product entry points.
    /// </summary>
    public static IKernelRuntimeExecutionLoop CreateExecutionLoop(IExecutionRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return new AdaptiveRuntimeExecutionLoop(new TianShu.Kernel.StableKernelCore(), runtime);
    }

    /// <summary>
    /// 创建 CLI 新 loop 默认允许的只读工具绑定表。
    /// Creates the default read-only tool bindings allowed by the new CLI loop.
    /// </summary>
    public static IReadOnlyDictionary<string, ITianShuTool> CreateReadOnlyTools()
        => CreateTools(includeWorkspaceWrite: false, includeShell: false, includeMcp: false);

    /// <summary>
    /// 创建 CLI 新 loop 可注册的工具绑定表；默认只读，显式审批态才包含 workspace write 或 shell。
    /// Creates tool bindings for the new CLI loop; defaults to read-only and includes workspace write or shell only for explicit approval mode.
    /// </summary>
    public static IReadOnlyDictionary<string, ITianShuTool> CreateTools(
        bool includeWorkspaceWrite = false,
        bool includeShell = false,
        bool includeMcp = false,
        ITianShuMcpResourceToolServices? mcpResourceServices = null,
        IReadOnlyList<TianShuMcpToolDescriptor>? mcpToolDescriptors = null,
        ITianShuMcpToolServices? mcpToolServices = null,
        bool includeMemory = false,
        ITianShuMemoryToolServices? memoryToolServices = null)
    {
        var readOnlyProvider = new FileSystemToolProvider();
        var registrationContext = new TianShuToolRegistrationContext();
        var activationContext = new TianShuToolActivationContext();
        var mcpProvider = includeMcp ? new McpResourceToolProvider(mcpToolDescriptors) : null;
        var memoryProvider = includeMemory ? new MemoryToolProvider() : null;
        var mcpDescriptorsById = mcpProvider?.DescribeTools(registrationContext)
            .Where(descriptor => McpResourceToolIds.Contains(descriptor.ToolId, StringComparer.Ordinal)
                                 || descriptor.ToolId.StartsWith("mcp.", StringComparison.Ordinal))
            .ToDictionary(static descriptor => descriptor.ToolId, StringComparer.Ordinal)
            ?? new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal);
        var toolIds = ReadOnlyToolIds
            .Concat(includeWorkspaceWrite ? WorkspaceWriteToolIds : Array.Empty<string>())
            .Concat(includeShell ? ShellToolIds : Array.Empty<string>())
            .Concat(includeMcp ? mcpDescriptorsById.Keys : Array.Empty<string>())
            .Concat(includeMemory ? MemoryToolIds : Array.Empty<string>())
            .ToArray();
        var readOnlyDescriptorsById = readOnlyProvider.DescribeTools(registrationContext)
            .Where(descriptor => toolIds.Contains(descriptor.ToolId, StringComparer.Ordinal))
            .ToDictionary(static descriptor => descriptor.ToolId, StringComparer.Ordinal);
        var mutatingProvider = includeWorkspaceWrite ? new MutatingFileSystemToolProvider() : null;
        var mutatingDescriptorsById = mutatingProvider?.DescribeTools(registrationContext)
            .Where(descriptor => WorkspaceWriteToolIds.Contains(descriptor.ToolId, StringComparer.Ordinal))
            .ToDictionary(static descriptor => descriptor.ToolId, StringComparer.Ordinal)
            ?? new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal);
        var shellProvider = includeShell ? new ShellToolProvider() : null;
        var shellDescriptorsById = shellProvider?.DescribeTools(registrationContext)
            .Where(descriptor => ShellToolIds.Contains(descriptor.ToolId, StringComparer.Ordinal))
            .ToDictionary(static descriptor => descriptor.ToolId, StringComparer.Ordinal)
            ?? new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal);
        var memoryDescriptorsById = memoryProvider?.DescribeTools(registrationContext)
            .Where(descriptor => MemoryToolIds.Contains(descriptor.ToolId, StringComparer.Ordinal))
            .ToDictionary(static descriptor => descriptor.ToolId, StringComparer.Ordinal)
            ?? new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal);

        return toolIds
            .Where(toolId => readOnlyDescriptorsById.ContainsKey(toolId)
                             || mutatingDescriptorsById.ContainsKey(toolId)
                             || shellDescriptorsById.ContainsKey(toolId)
                             || mcpDescriptorsById.ContainsKey(toolId)
                             || memoryDescriptorsById.ContainsKey(toolId))
            .ToDictionary(
                static toolId => toolId,
                toolId =>
                {
                    if (readOnlyDescriptorsById.ContainsKey(toolId))
                    {
                        return (ITianShuTool)new TianShuToolHandlerAdapter(readOnlyProvider.CreateHandler(toolId, activationContext));
                    }

                    if (mutatingDescriptorsById.ContainsKey(toolId))
                    {
                        return new TianShuToolHandlerAdapter(
                            mutatingProvider!.CreateHandler(toolId, activationContext),
                            CreateWorkspaceMutationInvocationContext);
                    }

                    if (shellDescriptorsById.ContainsKey(toolId))
                    {
                        return new TianShuToolHandlerAdapter(
                            shellProvider!.CreateHandler(toolId, activationContext),
                            CreateShellInvocationContext);
                    }

                    if (memoryDescriptorsById.ContainsKey(toolId))
                    {
                        return new TianShuToolHandlerAdapter(
                            memoryProvider!.CreateHandler(toolId, activationContext),
                            context => CreateMemoryInvocationContext(context, memoryToolServices));
                    }

                    return new TianShuToolHandlerAdapter(
                        mcpProvider!.CreateHandler(toolId, activationContext),
                        context => CreateMcpInvocationContext(context, mcpResourceServices, mcpToolServices));
                },
                StringComparer.Ordinal);
    }

    private static TianShuToolInvocationContext CreateWorkspaceMutationInvocationContext(ToolInvocationContext context)
        => new(
            context.SourceIntentId,
            context.RuntimeStepId,
            context.WorkingDirectory ?? string.Empty,
            FileMutationServices: new WorkspaceOnlyFileMutationServices(context.WorkingDirectory),
            Metadata: context.Metadata);

    private static TianShuToolInvocationContext CreateShellInvocationContext(ToolInvocationContext context)
        => new(
            context.SourceIntentId,
            context.RuntimeStepId,
            context.WorkingDirectory ?? string.Empty,
            ShellServices: new WorkspaceShellToolServices(context.WorkingDirectory),
            Metadata: context.Metadata);

    private static TianShuToolInvocationContext CreateMcpInvocationContext(
        ToolInvocationContext context,
        ITianShuMcpResourceToolServices? mcpResourceServices,
        ITianShuMcpToolServices? mcpToolServices)
        => new(
            context.SourceIntentId,
            context.RuntimeStepId,
            context.WorkingDirectory ?? string.Empty,
            McpResourceServices: mcpResourceServices,
            McpToolServices: mcpToolServices,
            Metadata: context.Metadata);

    private static TianShuToolInvocationContext CreateMemoryInvocationContext(
        ToolInvocationContext context,
        ITianShuMemoryToolServices? memoryToolServices)
        => new(
            context.SourceIntentId,
            context.RuntimeStepId,
            context.WorkingDirectory ?? string.Empty,
            MemoryServices: memoryToolServices,
            Metadata: context.Metadata);

    private static MemoryRuntimeBindings CreateMemoryRuntimeBindings(
        ITianShuMemoryToolServices? memoryToolServices,
        IReadOnlyDictionary<string, IMemoryModule>? memoryModules)
    {
        if (memoryToolServices is not null && memoryModules is not null)
        {
            return new MemoryRuntimeBindings(memoryToolServices, memoryModules);
        }

        var service = CreateDefaultEphemeralMemoryService();
        return new MemoryRuntimeBindings(
            memoryToolServices ?? new KernelRuntimeMemoryToolServices(service),
            memoryModules ?? new Dictionary<string, IMemoryModule>(StringComparer.Ordinal)
            {
                ["memory.identity"] = new MemoryServiceModuleAdapter(service),
            });
    }

    private static DefaultMemoryService CreateDefaultEphemeralMemoryService()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var memorySpace = new MemorySpace(
            new MemorySpaceId("memory:workspace:kernel-runtime"),
            MemoryScopeKind.Workspace,
            "kernel-runtime",
            "Kernel Runtime Ephemeral Memory");
        var provider = new TianShuLocalMemoryProvider(store, [memorySpace]);
        var registry = new MemoryProviderRegistry(
            [provider],
            [
                new MemoryProviderBinding(
                    TianShuLocalMemoryProvider.DefaultProviderId,
                    memorySpace.Id,
                    MemoryProviderBindingMode.ReadWrite,
                    provider.Descriptor.Capabilities)
            ]);
        return new DefaultMemoryService(registry);
    }

    private sealed record MemoryRuntimeBindings(
        ITianShuMemoryToolServices ToolServices,
        IReadOnlyDictionary<string, IMemoryModule> Modules);

    private sealed class KernelRuntimeMemoryToolServices(DefaultMemoryService memoryService) : ITianShuMemoryToolServices
    {
        public Task<MemoryQueryResult> FilterMemoryAsync(FilterMemory command, CancellationToken cancellationToken)
            => memoryService.FilterAsync(command, CreateOperationContext(), cancellationToken);

        public Task<MemoryOverlay> ResolveMemoryOverlayAsync(ResolveMemoryOverlay command, CancellationToken cancellationToken)
            => memoryService.ResolveOverlayAsync(command, null, null, CreateOperationContext(), cancellationToken);

        public Task<MemoryMutationResult> RecordMemoryFeedbackAsync(RecordMemoryFeedback command, CancellationToken cancellationToken)
            => memoryService.RecordFeedbackAsync(command, CreateOperationContext(), cancellationToken);

        private static MemoryOperationContext CreateOperationContext()
            => new("kernel-runtime-loop", correlationId: "cli-kernel-runtime-memory");
    }

    private static ToolDescriptor CreateSubAgentProviderToolDescriptor()
        => new(
            "spawn_agent",
            "Spawn Sub-Agent",
            "Use when the current task has a separate evidence domain, comparison target, risk review, or deliverable that can be handled from a standalone brief without the full parent history. Starts one governed child run, waits for it to finish, and returns the child result to this parent turn. Good fits include isolated repo/document inspection, independent verification of a claim, or a bounded review that would otherwise distract from the parent synthesis. Do not use for a single directory listing, a single file read, simple sequential inspection, follow-up conversation with an existing child, or work that needs the full parent history.",
            capabilities: [new ToolCapability("sub_agent.spawn", "Start one governed child run for a bounded standalone work item.")],
            approvalRequirement: ToolApprovalRequirement.Required,
            concurrencyClass: ToolConcurrencyClass.Sequential,
            inputSchema: JsonSerializer.SerializeToElement(new
            {
                type = "object",
                additionalProperties = false,
                properties = new
                {
                    operation = new
                    {
                        type = "string",
                        @enum = new[] { "spawn" },
                        description = "Must be spawn.",
                    },
                    taskBrief = new
                    {
                        type = "string",
                        description = "A concise standalone task brief. Include enough scope, expected output, constraints, and success criteria for the child run to work without the full parent history.",
                    },
                    evidenceRefs = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Optional explicit evidence references, paths, or stable identifiers the child should inspect or use. Prefer passing only the evidence needed by the child task.",
                    },
                    requiresHumanGate = new
                    {
                        type = "boolean",
                        description = "Whether this child task should require an additional human gate. Omit or false for read-only analysis.",
                    },
                },
                required = new[] { "operation", "taskBrief" },
            }),
            permissions: new PermissionDeclaration(["module.sub_agent"], requiresHumanGate: true),
            sideEffects: new SideEffectProfile(SideEffectLevel.HostMutation, ["subagent", "kernel-run"], reversible: false, requiresAudit: true));

    private sealed class WorkspaceShellToolServices : ITianShuShellToolServices
    {
        private const int DefaultTimeoutMs = 30_000;
        private const int MaxTimeoutMs = 30_000;
        private const int MaxOutputChars = 12_000;
        private readonly string? workspaceRoot;

        public WorkspaceShellToolServices(string? workspaceRoot)
        {
            this.workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot) ? null : Path.GetFullPath(workspaceRoot);
        }

        public async Task<TianShuShellToolResult> InvokeShellToolAsync(
            TianShuShellToolRequest request,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(request.ToolKey, "shell_command", StringComparison.Ordinal))
            {
                return Failure(request, "unsupported shell tool in kernel runtime loop.", "shell_tool_not_opened");
            }

            var commandText = ReadString(request.Arguments, "command");
            if (string.IsNullOrWhiteSpace(commandText))
            {
                return Failure(request, "shell_command requires a non-empty command.", "shell_command_empty");
            }

            if (ReadBool(request.Arguments, "login") == true)
            {
                return Failure(request, "login shell is not allowed in kernel runtime shell execution.", "shell_login_not_allowed");
            }

            if (!TryResolveCwd(ReadString(request.Arguments, "workdir") ?? ReadString(request.Arguments, "cwd"), out var cwd, out var cwdFailure))
            {
                return Failure(request, cwdFailure ?? "shell cwd is not allowed.", "shell_cwd_not_allowed");
            }

            if (IsDangerousCommand(commandText))
            {
                return Failure(request, "shell command matches a high-risk command pattern and was rejected before execution.", "shell_dangerous_command_rejected");
            }

            var timeoutMs = Math.Clamp(ReadInt(request.Arguments, "timeout_ms") ?? DefaultTimeoutMs, 1, MaxTimeoutMs);
            var planId = $"{request.ToolKey}-{Guid.NewGuid():N}.shell-plan";
            var commandHash = ComputeHash(commandText);
            var startedAt = DateTimeOffset.UtcNow;
            var command = BuildShellCommand(commandText);
            var environment = CreateMinimalEnvironment();
            var rejectedEnvironmentKeys = ApplyAllowedEnvironmentOverrides(environment, request.Arguments);
            var transcriptRef = $"artifact://shell-transcript/{planId}";
            var auditRef = $"audit://shell/{planId}";
            var traceRef = $"trace://shell/{planId}";

            KernelShellRunResult runResult;
            try
            {
                runResult = await RunProcessAsync(command.FileName, command.Arguments, cwd!, timeoutMs, environment, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var cancelled = BuildProjection(
                    request.ToolKey,
                    planId,
                    commandHash,
                    RedactText(commandText),
                    cwd!,
                    timeoutMs,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    exitCode: null,
                    timedOut: false,
                    cancelled: true,
                    stdout: string.Empty,
                    stderr: "shell execution cancelled.",
                    outputTruncated: false,
                    transcriptRef,
                    auditRef,
                    traceRef,
                    failureCode: "shell_cancelled",
                    rejectedEnvironmentKeys);
                return new TianShuShellToolResult(false, "shell execution cancelled.", cancelled, "shell_cancelled");
            }
            catch (Exception ex)
            {
                var failed = BuildProjection(
                    request.ToolKey,
                    planId,
                    commandHash,
                    RedactText(commandText),
                    cwd!,
                    timeoutMs,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    exitCode: null,
                    timedOut: false,
                    cancelled: false,
                    stdout: string.Empty,
                    stderr: RedactText(ex.Message),
                    outputTruncated: false,
                    transcriptRef,
                    auditRef,
                    traceRef,
                    failureCode: "shell_execution_failed",
                    rejectedEnvironmentKeys);
                return new TianShuShellToolResult(false, RedactText(ex.Message), failed, "shell_execution_failed");
            }

            var failureCode = runResult.TimedOut
                ? "shell_timeout"
                : runResult.ExitCode == 0 ? null : "shell_nonzero_exit";
            var projection = BuildProjection(
                request.ToolKey,
                planId,
                commandHash,
                RedactText(commandText),
                cwd!,
                timeoutMs,
                startedAt,
                DateTimeOffset.UtcNow,
                runResult.ExitCode,
                runResult.TimedOut,
                cancelled: false,
                runResult.Stdout,
                runResult.Stderr,
                runResult.OutputTruncated,
                transcriptRef,
                auditRef,
                traceRef,
                failureCode,
                rejectedEnvironmentKeys);
            var outputText = BuildOutputText(runResult);
            return new TianShuShellToolResult(failureCode is null, outputText, projection, failureCode);
        }

        private static (string FileName, string Arguments) BuildShellCommand(string commandText)
            => OperatingSystem.IsWindows()
                ? ("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command " + QuoteArgument(commandText))
                : ("/bin/sh", "-c " + QuoteArgument(commandText));

        private static async Task<KernelShellRunResult> RunProcessAsync(
            string fileName,
            string arguments,
            string cwd,
            int timeoutMs,
            IReadOnlyDictionary<string, string> environment,
            CancellationToken cancellationToken)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = cwd,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.Environment.Clear();
            foreach (var pair in environment)
            {
                process.StartInfo.Environment[pair.Key] = pair.Value;
            }

            if (!process.Start())
            {
                throw new InvalidOperationException("failed to start shell process");
            }

            var stdoutTask = ReadCappedAsync(process.StandardOutput, cancellationToken);
            var stderrTask = ReadCappedAsync(process.StandardError, cancellationToken);
            var timedOut = false;
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var exitCode = process.HasExited ? process.ExitCode : -1;
            return new KernelShellRunResult(
                exitCode,
                RedactText(stdout.Text),
                RedactText(stderr.Text),
                timedOut,
                stdout.Truncated || stderr.Truncated);
        }

        private bool TryResolveCwd(string? requestedCwd, out string? cwd, out string? failure)
        {
            cwd = null;
            failure = null;
            if (workspaceRoot is null)
            {
                failure = "shell execution requires a working directory.";
                return false;
            }

            var candidate = string.IsNullOrWhiteSpace(requestedCwd)
                ? workspaceRoot
                : Path.GetFullPath(Path.IsPathRooted(requestedCwd) ? requestedCwd : Path.Combine(workspaceRoot, requestedCwd));
            if (!WorkspaceOnlyFileMutationServices.IsPathInsideRoot(workspaceRoot, candidate))
            {
                failure = "shell cwd is outside the workspace root.";
                return false;
            }

            if (!Directory.Exists(candidate))
            {
                failure = "shell cwd does not exist.";
                return false;
            }

            cwd = candidate;
            return true;
        }

        private static async Task<(string Text, bool Truncated)> ReadCappedAsync(StreamReader reader, CancellationToken cancellationToken)
        {
            var buffer = new char[4096];
            var builder = new StringBuilder();
            var truncated = false;
            while (true)
            {
                var read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                if (builder.Length + read > MaxOutputChars)
                {
                    builder.Append(buffer, 0, Math.Max(0, MaxOutputChars - builder.Length));
                    truncated = true;
                    continue;
                }

                builder.Append(buffer, 0, read);
            }

            return (builder.ToString(), truncated);
        }

        private static StructuredValue BuildProjection(
            string toolId,
            string planId,
            string commandHash,
            string commandDisplayRedacted,
            string cwd,
            int timeoutMs,
            DateTimeOffset startedAt,
            DateTimeOffset endedAt,
            int? exitCode,
            bool timedOut,
            bool cancelled,
            string stdout,
            string stderr,
            bool outputTruncated,
            string transcriptRef,
            string auditRef,
            string traceRef,
            string? failureCode,
            IReadOnlyList<string>? rejectedEnvironmentKeys = null)
            => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["runtimeBoundary"] = "tool.shell_execution",
                ["status"] = failureCode is null ? "succeeded" : timedOut ? "timeout" : cancelled ? "cancelled" : "failed",
                ["planId"] = planId,
                ["toolId"] = toolId,
                ["commandHash"] = commandHash,
                ["commandDisplayRedacted"] = commandDisplayRedacted,
                ["workingDirectoryRef"] = CreateWorkspaceRef(cwd),
                ["effectiveTimeoutMs"] = timeoutMs,
                ["startedAt"] = startedAt.ToString("O"),
                ["endedAt"] = endedAt.ToString("O"),
                ["durationMs"] = Math.Max(0, (endedAt - startedAt).TotalMilliseconds),
                ["exitCode"] = exitCode,
                ["timedOut"] = timedOut,
                ["cancelled"] = cancelled,
                ["outputTruncated"] = outputTruncated,
                ["stdoutRef"] = $"{transcriptRef}/stdout",
                ["stderrRef"] = $"{transcriptRef}/stderr",
                ["transcriptRef"] = transcriptRef,
                ["diagnosticsRef"] = $"diagnostics://shell/{planId}",
                ["auditRef"] = auditRef,
                ["traceRef"] = traceRef,
                ["redactionStatus"] = "sanitized",
                ["redactedEnvironmentKeys"] = RedactedEnvironmentKeyPatterns,
                ["rejectedEnvironmentKeys"] = rejectedEnvironmentKeys ?? Array.Empty<string>(),
                ["stdoutPreview"] = stdout,
                ["stderrPreview"] = stderr,
                ["failureCode"] = failureCode,
            });

        private static string BuildOutputText(KernelShellRunResult result)
        {
            var builder = new StringBuilder();
            if (result.TimedOut)
            {
                builder.AppendLine("command timed out.");
            }

            builder.Append("Exit code: ");
            builder.AppendLine(result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(result.Stdout))
            {
                builder.AppendLine("Stdout:");
                builder.AppendLine(result.Stdout);
            }

            if (!string.IsNullOrWhiteSpace(result.Stderr))
            {
                builder.AppendLine("Stderr:");
                builder.AppendLine(result.Stderr);
            }

            if (result.OutputTruncated)
            {
                builder.AppendLine("[output truncated]");
            }

            return builder.ToString();
        }

        private static Dictionary<string, string> CreateMinimalEnvironment()
        {
            var environment = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var key in new[] { "PATH", "Path", "SystemRoot", "WINDIR", "TEMP", "TMP", "HOME", "USERPROFILE", "COMSPEC", "PSModulePath" })
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(value) && !IsSensitiveKey(key))
                {
                    environment[key] = value;
                }
            }

            return environment;
        }

        private static IReadOnlyList<string> ApplyAllowedEnvironmentOverrides(
            Dictionary<string, string> environment,
            StructuredValue arguments)
        {
            if (!arguments.TryGetProperty("env", out var env)
                || env is null
                || env.Kind != StructuredValueKind.Object)
            {
                return Array.Empty<string>();
            }

            var rejected = new List<string>();
            foreach (var pair in env.Properties)
            {
                if (IsSensitiveKey(pair.Key))
                {
                    rejected.Add(pair.Key);
                    continue;
                }

                var value = pair.Value.GetString();
                if (value is not null)
                {
                    environment[pair.Key] = value;
                }
            }

            return rejected;
        }

        private static TianShuShellToolResult Failure(TianShuShellToolRequest request, string message, string failureCode)
        {
            var projection = BuildProjection(
                request.ToolKey,
                $"{request.ToolKey}-{Guid.NewGuid():N}.shell-plan",
                "unavailable",
                string.Empty,
                Environment.CurrentDirectory,
                DefaultTimeoutMs,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                exitCode: null,
                timedOut: false,
                cancelled: false,
                stdout: string.Empty,
                stderr: message,
                outputTruncated: false,
                $"artifact://shell-transcript/{request.ToolKey}-unavailable",
                $"audit://shell/{request.ToolKey}-unavailable",
                $"trace://shell/{request.ToolKey}-unavailable",
                failureCode);
            return new TianShuShellToolResult(false, message, projection, failureCode);
        }

        private static string? ReadString(StructuredValue value, string propertyName)
            => value.TryGetProperty(propertyName, out var property) ? property?.GetString() : null;

        private static bool? ReadBool(StructuredValue value, string propertyName)
            => value.TryGetProperty(propertyName, out var property)
                ? property?.Kind == StructuredValueKind.Boolean
                    ? property.GetBoolean()
                    : bool.TryParse(property?.GetString(), out var parsed) ? parsed : null
                : null;

        private static int? ReadInt(StructuredValue value, string propertyName)
            => value.TryGetProperty(propertyName, out var property)
                && int.TryParse(property?.GetString(), out var parsed)
                ? parsed
                : null;

        private static bool IsDangerousCommand(string commandText)
        {
            var normalized = commandText.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (normalized.Contains("remove-item", StringComparison.Ordinal)
                && normalized.Contains("-recurse", StringComparison.Ordinal)
                && normalized.Contains("-force", StringComparison.Ordinal))
            {
                return true;
            }

            if (normalized.Contains("rm -rf /", StringComparison.Ordinal)
                || normalized.Contains("rm -fr /", StringComparison.Ordinal)
                || normalized.Contains("rm -rf .", StringComparison.Ordinal)
                || normalized.Contains("rm -fr .", StringComparison.Ordinal)
                || normalized.Contains("rm -rf *", StringComparison.Ordinal)
                || normalized.Contains("rm -fr *", StringComparison.Ordinal)
                || normalized.Contains("del /s /q", StringComparison.Ordinal)
                || normalized.Contains("rmdir /s /q", StringComparison.Ordinal)
                || normalized.Contains("rd /s /q", StringComparison.Ordinal)
                || normalized.Contains("format ", StringComparison.Ordinal)
                || normalized.Contains("diskpart", StringComparison.Ordinal)
                || normalized.Contains("shutdown ", StringComparison.Ordinal)
                || normalized.Contains("restart-computer", StringComparison.Ordinal)
                || normalized.Contains("stop-computer", StringComparison.Ordinal)
                || normalized.Contains("bcdedit", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private static string QuoteArgument(string value)
            => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

        private static string ComputeHash(string value)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        private static string CreateWorkspaceRef(string fullPath)
            => "workspace://" + Path.GetFileName(fullPath);

        private static string RedactText(string value)
        {
            var redacted = value;
            foreach (var key in RedactedEnvironmentKeyPatterns)
            {
                redacted = redacted.Replace(key, $"<redacted:{key}>", StringComparison.OrdinalIgnoreCase);
            }

            return redacted;
        }

        private static bool IsSensitiveKey(string key)
            => RedactedEnvironmentKeyPatterns.Any(pattern => key.Contains(pattern, StringComparison.OrdinalIgnoreCase));

        private static readonly string[] RedactedEnvironmentKeyPatterns =
        [
            "API_KEY",
            "TOKEN",
            "SECRET",
            "PASSWORD",
            "CREDENTIAL",
            "AUTHORIZATION",
            "COOKIE",
        ];

        private sealed record KernelShellRunResult(
            int ExitCode,
            string Stdout,
            string Stderr,
            bool TimedOut,
            bool OutputTruncated);
    }

    private sealed class WorkspaceOnlyFileMutationServices : ITianShuFileMutationToolServices
    {
        private readonly string? workspaceRoot;

        public WorkspaceOnlyFileMutationServices(string? workspaceRoot)
        {
            this.workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot) ? null : Path.GetFullPath(workspaceRoot);
        }

        public bool IsWritePathAllowed(string fullPath)
            => workspaceRoot is not null && IsPathInsideRoot(workspaceRoot, fullPath);

        public bool IsFileChangeApproved(string fullPath)
            => IsWritePathAllowed(fullPath);

        public static bool IsPathInsideRoot(string rootPath, string candidatePath)
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(candidatePath);
            return string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, comparison)
                   || candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison)
                   || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, comparison);
        }
    }
}
