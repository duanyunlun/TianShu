using TianShu.Configuration;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Tools;
using TianShu.Execution.Runtime;
using TianShu.Provider.Abstractions;
using TianShu.Tools.FileSystem;
using TianShu.Tools.FileSystemMutating;
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
    ];

    /// <summary>
    /// 创建带真实 provider module 与只读内置工具的执行运行时。
    /// Creates an execution runtime with the real provider module and read-only built-in tools.
    /// </summary>
    public static TianShuExecutionRuntime CreateRuntime(
        ResolvedTianShuConfig config,
        string providerModuleId = "provider.default",
        bool includeWorkspaceWrite = false,
        HttpMessageHandler? providerHttpHandler = null,
        Func<string, string?>? readEnvironmentVariable = null,
        HttpClient? providerHttpClient = null,
        IExecutionRuntimeMetricsSink? metricsSink = null,
        IReadOnlyDictionary<string, ISubAgentModule>? subAgentModules = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (providerHttpHandler is not null && providerHttpClient is not null)
        {
            throw new ArgumentException("不能同时传入 providerHttpHandler 与 providerHttpClient。");
        }

        var tools = CreateTools(includeWorkspaceWrite);
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
                subAgentModules: subAgentModules),
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
        => CreateTools(includeWorkspaceWrite: false);

    /// <summary>
    /// 创建 CLI 新 loop 可注册的工具绑定表；默认只读，显式审批态才包含 workspace write。
    /// Creates tool bindings for the new CLI loop; defaults to read-only and includes workspace write only for explicit approval mode.
    /// </summary>
    public static IReadOnlyDictionary<string, ITianShuTool> CreateTools(bool includeWorkspaceWrite = false)
    {
        var readOnlyProvider = new FileSystemToolProvider();
        var registrationContext = new TianShuToolRegistrationContext();
        var activationContext = new TianShuToolActivationContext();
        var toolIds = includeWorkspaceWrite
            ? ReadOnlyToolIds.Concat(WorkspaceWriteToolIds).ToArray()
            : ReadOnlyToolIds;
        var readOnlyDescriptorsById = readOnlyProvider.DescribeTools(registrationContext)
            .Where(descriptor => toolIds.Contains(descriptor.ToolId, StringComparer.Ordinal))
            .ToDictionary(static descriptor => descriptor.ToolId, StringComparer.Ordinal);
        var mutatingProvider = includeWorkspaceWrite ? new MutatingFileSystemToolProvider() : null;
        var mutatingDescriptorsById = mutatingProvider?.DescribeTools(registrationContext)
            .Where(descriptor => WorkspaceWriteToolIds.Contains(descriptor.ToolId, StringComparer.Ordinal))
            .ToDictionary(static descriptor => descriptor.ToolId, StringComparer.Ordinal)
            ?? new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal);

        return toolIds
            .Where(toolId => readOnlyDescriptorsById.ContainsKey(toolId) || mutatingDescriptorsById.ContainsKey(toolId))
            .ToDictionary(
                static toolId => toolId,
                toolId => (ITianShuTool)(readOnlyDescriptorsById.ContainsKey(toolId)
                    ? new TianShuToolHandlerAdapter(readOnlyProvider.CreateHandler(toolId, activationContext))
                    : new TianShuToolHandlerAdapter(
                        mutatingProvider!.CreateHandler(toolId, activationContext),
                        CreateWorkspaceMutationInvocationContext)),
                StringComparer.Ordinal);
    }

    private static TianShuToolInvocationContext CreateWorkspaceMutationInvocationContext(ToolInvocationContext context)
        => new(
            context.SourceIntentId,
            context.RuntimeStepId,
            context.WorkingDirectory ?? string.Empty,
            FileMutationServices: new WorkspaceOnlyFileMutationServices(context.WorkingDirectory),
            Metadata: context.Metadata);

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

        private static bool IsPathInsideRoot(string rootPath, string candidatePath)
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
