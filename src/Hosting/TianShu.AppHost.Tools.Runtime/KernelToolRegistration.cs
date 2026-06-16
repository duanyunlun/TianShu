using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using TianShu.AppHost.Configuration;
using TianShu.AppHost.Tools;
using ConfigurationHomePaths = TianShu.Configuration.TianShuHomePathUtilities;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// 工具集注册入口。Tool set registration entry used at composition time.
/// </summary>
internal interface IKernelToolSet
{
    string Id { get; }

    void Register(KernelToolRegistryBuilder builder);
}

internal enum KernelToolRegistrationConflictPolicy
{
    Reject,
    Replace,
}

/// <summary>
/// 工具注册构建器。Tool registry builder that keeps replacement and disable rules outside the raw registry dictionary.
/// </summary>
internal sealed class KernelToolRegistryBuilder
{
    private readonly KernelToolRegistry registry = new();
    private readonly HashSet<string> registeredToolKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> disabledToolKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> aliasTargets = new(StringComparer.Ordinal);

    public KernelToolRegistryBuilder DisableTool(string toolKey)
    {
        if (string.IsNullOrWhiteSpace(toolKey))
        {
            throw new ArgumentException("Tool key must not be empty.", nameof(toolKey));
        }

        disabledToolKeys.Add(toolKey);
        return this;
    }

    public KernelToolRegistryBuilder AddTool(
        IKernelToolHandler handler,
        KernelToolRegistrationConflictPolicy conflictPolicy = KernelToolRegistrationConflictPolicy.Reject)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AddTool(handler.Name, handler, conflictPolicy);
    }

    public KernelToolRegistryBuilder AddAlias(
        string toolKey,
        IKernelToolHandler handler,
        KernelToolRegistrationConflictPolicy conflictPolicy = KernelToolRegistrationConflictPolicy.Reject)
    {
        if (disabledToolKeys.Contains(toolKey) || disabledToolKeys.Contains(handler.Name))
        {
            return this;
        }

        AddTool(toolKey, handler, conflictPolicy);
        aliasTargets[toolKey] = handler.Name;
        return this;
    }

    public KernelToolRegistryBuilder ReplaceTool(string toolKey, IKernelToolHandler handler)
    {
        return AddTool(toolKey, handler, KernelToolRegistrationConflictPolicy.Replace);
    }

    public KernelToolRegistryBuilder AddToolSet(IKernelToolSet toolSet)
    {
        ArgumentNullException.ThrowIfNull(toolSet);
        toolSet.Register(this);
        return this;
    }

    public bool ContainsToolKey(string toolKey)
    {
        return registeredToolKeys.Contains(toolKey);
    }

    public KernelToolRegistryBuilder AddContractToolProvider(
        ITianShuToolProvider provider,
        KernelToolProfileOptions? profileOptions = null,
        string? workspacePath = null,
        bool replaceExisting = false)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var registrationContext = new TianShuToolRegistrationContext(workspacePath, profileOptions?.ToolProfileId);
        var activationContext = new TianShuToolActivationContext(workspacePath, profileOptions?.ToolProfileId);
        foreach (var descriptor in provider.DescribeTools(registrationContext))
        {
            var handler = provider.CreateHandler(descriptor.Key, activationContext);
            var adapter = new KernelContractToolHandlerAdapter(handler);
            var shouldReplace = ContainsToolKey(descriptor.Key)
                                && (replaceExisting
                                    || (profileOptions?.TryGetImplementationSelection(descriptor.Key, out var selection) == true
                                        && MatchesSelection(descriptor, selection)));
            if (ContainsToolKey(descriptor.Key) && !shouldReplace)
            {
                continue;
            }

            AddTool(
                descriptor.Key,
                adapter,
                shouldReplace ? KernelToolRegistrationConflictPolicy.Replace : KernelToolRegistrationConflictPolicy.Reject);
            AddProviderAliases(descriptor.Key, adapter, shouldReplace);
        }

        return this;
    }

    public KernelToolRegistry Build() => registry;

    private KernelToolRegistryBuilder AddProviderAliases(string toolKey, IKernelToolHandler handler, bool replaceExisting)
    {
        if (string.Equals(toolKey, "shell", StringComparison.Ordinal))
        {
            AddAlias(
                "container.exec",
                handler,
                replaceExisting ? KernelToolRegistrationConflictPolicy.Replace : KernelToolRegistrationConflictPolicy.Reject);
        }

        return this;
    }

    private KernelToolRegistryBuilder AddTool(
        string toolKey,
        IKernelToolHandler handler,
        KernelToolRegistrationConflictPolicy conflictPolicy)
    {
        if (string.IsNullOrWhiteSpace(toolKey))
        {
            throw new ArgumentException("Tool key must not be empty.", nameof(toolKey));
        }

        ArgumentNullException.ThrowIfNull(handler);
        if (disabledToolKeys.Contains(toolKey) || disabledToolKeys.Contains(handler.Name))
        {
            return this;
        }

        if (registeredToolKeys.Contains(toolKey))
        {
            if (conflictPolicy == KernelToolRegistrationConflictPolicy.Reject)
            {
                throw new InvalidOperationException($"Tool key '{toolKey}' is already registered.");
            }
        }
        else
        {
            registeredToolKeys.Add(toolKey);
        }

        registry.Register(toolKey, handler);
        RefreshAliasesForTool(toolKey, handler);
        return this;
    }

    private void RefreshAliasesForTool(string toolKey, IKernelToolHandler handler)
    {
        foreach (var aliasKey in aliasTargets
                     .Where(alias => string.Equals(alias.Value, toolKey, StringComparison.Ordinal))
                     .Select(static alias => alias.Key)
                     .ToArray())
        {
            if (disabledToolKeys.Contains(aliasKey))
            {
                continue;
            }

            registry.Register(aliasKey, handler);
        }
    }

    private static bool MatchesSelection(ToolDescriptor descriptor, KernelToolImplementationSelection? selection)
    {
        if (selection is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selection.ImplementationId)
            && string.Equals(selection.ImplementationId, descriptor.ImplementationBinding?.ImplementationId, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(selection.ProviderId)
            && string.Equals(selection.ProviderId, descriptor.ImplementationBinding?.ImplementationId, StringComparison.Ordinal))
        {
            return true;
        }

        return selection.ImplementationKind.HasValue
               && selection.ImplementationKind.Value == descriptor.ImplementationBinding?.ImplementationKind;
    }
}

/// <summary>
/// 内部测试用 Runtime 工具集。Internal-only legacy runtime tool set for focused compatibility tests.
/// </summary>
internal sealed class KernelInternalRuntimeToolSet : IKernelToolSet
{
    public string Id => "tianshu.internal.runtime";

    public void Register(KernelToolRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddTool(new KernelTestSyncRuntimeEndpoint());
    }
}

internal static class KernelToolRegistryFactoryExtensions
{
    public static KernelToolRegistryBuilder AddConfiguredToolPackages(
        this KernelToolRegistryBuilder builder,
        IReadOnlyDictionary<string, string>? configValues,
        KernelToolProfileOptions profileOptions,
        string? workspacePath)
    {
        var manifests = KernelToolPackageManifestLoader.LoadManifests(configValues, workspacePath);
        foreach (var manifest in manifests)
        {
            if (!manifest.Enabled)
            {
                continue;
            }

            if (string.Equals(manifest.Type, KernelToolPackageManifest.BuiltinType, StringComparison.OrdinalIgnoreCase))
            {
                // builtin 父 manifest 只声明官方工具包生命周期；实际工具由其 [[providers]] 子项加载。
                // The builtin parent manifest only owns package lifecycle; provider entries register tools.
                continue;
            }

            foreach (var provider in KernelToolProviderAssemblyLoader.LoadProviders(manifest))
            {
                builder.AddContractToolProvider(provider, profileOptions, workspacePath, manifest.ReplaceExisting);
            }
        }

        return builder;
    }
}

internal sealed record KernelToolPackageManifest(
    string Id,
    string Type,
    bool Enabled,
    int Priority,
    string? AssemblyPath,
    string? ProviderType,
    string? PackageDirectory,
    IReadOnlyList<string> EnabledToolSets,
    IReadOnlyList<string> DisabledToolSets,
    string? Version,
    string? MinTianShuVersion,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Diagnostics,
    string LoadStatus,
    string? UnavailableReason,
    bool ReplaceExisting = false)
{
    public const string BuiltinType = "builtin";
    public const string AssemblyType = "assembly";
    public const string PackageType = "package";
    public const string PluginType = "plugin";
    public const string LoadStatusAvailable = "available";
    public const string LoadStatusUnavailable = "unavailable";
}

internal sealed class KernelContractToolHandlerAdapter : IKernelToolHandler
{
    private static readonly KernelCommittedUnifiedExecProcessManager ShellUnifiedExecManager = new();

    private static readonly JsonElement EmptyObjectSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { },
        additionalProperties = false,
    });

    private readonly ITianShuToolHandler handler;

    public KernelContractToolHandlerAdapter(ITianShuToolHandler handler)
    {
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        Name = handler.Descriptor.Key;
        Description = handler.Descriptor.Description;
        IsMutating = handler.Descriptor.ApprovalRequirement == ToolApprovalRequirement.Required
                     || handler.Descriptor.ConcurrencyClass == ToolConcurrencyClass.Exclusive;
        SupportsParallelToolCalls = handler.Descriptor.ConcurrencyClass == ToolConcurrencyClass.SharedReadOnly;
        InputSchema = handler.Descriptor.InputSchema?.Clone() ?? EmptyObjectSchema.Clone();
        OutputSchema = handler.Descriptor.OutputSchema?.Clone();
        ImplementationBinding = handler.Descriptor.ImplementationBinding
                                ?? new ToolImplementationBinding(Name, ToolImplementationKind.Managed);
    }

    public string Name { get; }

    public string Description { get; }

    public bool IsMutating { get; }

    public bool SupportsParallelToolCalls { get; }

    public JsonElement InputSchema { get; }

    public JsonElement? OutputSchema { get; }

    public ToolImplementationBinding ImplementationBinding { get; }

    public ProviderResponsesToolDefinition BuildProviderToolDefinition()
    {
        if (string.Equals(Name, KernelToolDiscoveryToolNames.Search, StringComparison.Ordinal))
        {
            return KernelToolDiscoveryRuntimeSupport.BuildSearchProviderToolDefinition(connectorNames: null);
        }

        return new ProviderResponsesFunctionToolDefinition(
            Name,
            Description,
            InputSchema,
            OutputSchema,
            strict: false);
    }

    public async Task<KernelToolResult> ExecuteAsync(JsonElement arguments, KernelToolCallContext context, CancellationToken cancellationToken)
    {
        var result = await handler.InvokeAsync(
                new ToolInvocationRequest(
                    new CallId(context.ExternalCallId ?? context.ItemId ?? $"{context.TurnId}:{Name}"),
                    Name,
                    "invoke",
                    StructuredValue.FromJsonElement(arguments)),
                new TianShuToolInvocationContext(
                    context.ThreadId,
                    context.TurnId,
                    context.Cwd,
                    DynamicTools: BuildDiscoveryDescriptors(context.DynamicTools),
                    MemoryServices: BuildMemoryServices(context.RuntimeServices),
                    McpResourceServices: BuildMcpResourceServices(context.RuntimeServices),
                    FileMutationServices: BuildFileMutationServices(context),
                    ToolSuggestionServices: BuildToolSuggestionServices(context),
                    ShellServices: BuildShellServices(context),
                    InteractionServices: BuildInteractionServices(context),
                    CollaborationServices: BuildCollaborationServices(context),
                    FanoutServices: BuildFanoutServices(context),
                    CodeServices: BuildCodeServices(context),
                    ArtifactServices: BuildArtifactServices(context),
                    DiagnosticServices: BuildDiagnosticServices(context.RuntimeServices)),
                cancellationToken)
            .ConfigureAwait(false);

        return KernelToolInvocationResultMapper.FromInvocationResult(result);
    }

    public Task<KernelToolResult> ExecuteCustomAsync(string input, KernelToolCallContext context, CancellationToken cancellationToken)
    {
        return ExecuteAsync(JsonSerializer.SerializeToElement(new { input }), context, cancellationToken);
    }

    public ProviderResponsesToolDefinition? BuildCustomProviderToolDefinition()
    {
        var customInput = handler.Descriptor.CustomInputDefinition;
        return customInput is null
            ? null
            : new ProviderResponsesCustomToolDefinition(Name, customInput.Description, customInput.Format, OutputSchema);
    }

    private static IReadOnlyList<TianShuToolDiscoveryDescriptor>? BuildDiscoveryDescriptors(
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools)
    {
        var descriptors = KernelDynamicToolResolver.Describe(dynamicTools);
        if (descriptors.Count == 0)
        {
            return null;
        }

        return descriptors
            .Select(static descriptor => new TianShuToolDiscoveryDescriptor(
                descriptor.FullName,
                descriptor.ShortName,
                descriptor.Namespace,
                descriptor.Server,
                descriptor.Title,
                descriptor.Description,
                descriptor.ConnectorName,
                descriptor.ConnectorDescription,
                descriptor.InputSchema?.Clone()))
            .ToArray();
    }

    private static ITianShuMemoryToolServices? BuildMemoryServices(KernelToolRuntimeServices? runtimeServices)
    {
        if (runtimeServices is null
            || runtimeServices.FilterMemory is null
            || runtimeServices.ResolveMemoryOverlay is null
            || runtimeServices.RecordMemoryFeedback is null)
        {
            return null;
        }

        return new KernelMemoryToolServicesAdapter(runtimeServices);
    }

    private static ITianShuMcpResourceToolServices? BuildMcpResourceServices(KernelToolRuntimeServices? runtimeServices)
    {
        if (runtimeServices is null
            || runtimeServices.ListMcpResources is null
            || runtimeServices.ListMcpResourceTemplates is null
            || runtimeServices.ReadMcpResource is null)
        {
            return null;
        }

        return new KernelMcpResourceToolServicesAdapter(runtimeServices);
    }

    private static ITianShuFileMutationToolServices BuildFileMutationServices(KernelToolCallContext context)
        => new KernelFileMutationToolServicesAdapter(context);

    private static ITianShuShellToolServices BuildShellServices(KernelToolCallContext context)
        => new KernelShellToolServicesAdapter(context);

    private static ITianShuInteractionToolServices BuildInteractionServices(KernelToolCallContext context)
        => new KernelInteractionToolServicesAdapter(context);

    private static ITianShuCollaborationToolServices BuildCollaborationServices(KernelToolCallContext context)
        => new KernelCollaborationToolServicesAdapter(context);

    private static ITianShuFanoutToolServices BuildFanoutServices(KernelToolCallContext context)
        => new KernelFanoutToolServicesAdapter(context);

    private static ITianShuCodeToolServices BuildCodeServices(KernelToolCallContext context)
        => new KernelCodeToolServicesAdapter(context);

    private static ITianShuArtifactToolServices BuildArtifactServices(KernelToolCallContext context)
        => new KernelArtifactToolServicesAdapter(context);

    private static ITianShuToolSuggestionServices? BuildToolSuggestionServices(KernelToolCallContext context)
    {
        if (context.RuntimeServices?.ListToolSuggestDiscoverableConnectors is null)
        {
            return null;
        }

        return new KernelToolSuggestionServicesAdapter(context);
    }

    private static ITianShuToolDiagnosticServices? BuildDiagnosticServices(KernelToolRuntimeServices? runtimeServices)
        => runtimeServices?.ReportToolDiagnostic is null
            ? null
            : new KernelToolDiagnosticServicesAdapter(runtimeServices);

    private sealed class KernelToolDiagnosticServicesAdapter : ITianShuToolDiagnosticServices
    {
        private readonly KernelToolRuntimeServices runtimeServices;

        public KernelToolDiagnosticServicesAdapter(KernelToolRuntimeServices runtimeServices)
        {
            this.runtimeServices = runtimeServices;
        }

        public Task ReportDiagnosticAsync(TianShuToolDiagnosticEvent diagnostic, CancellationToken cancellationToken)
            => runtimeServices.ReportToolDiagnostic!(diagnostic, cancellationToken);
    }

    private sealed class KernelMemoryToolServicesAdapter : ITianShuMemoryToolServices
    {
        private readonly KernelToolRuntimeServices runtimeServices;

        public KernelMemoryToolServicesAdapter(KernelToolRuntimeServices runtimeServices)
        {
            this.runtimeServices = runtimeServices;
        }

        public Task<MemoryQueryResult> FilterMemoryAsync(FilterMemory command, CancellationToken cancellationToken)
            => runtimeServices.FilterMemory!(command, cancellationToken);

        public Task<MemoryOverlay> ResolveMemoryOverlayAsync(ResolveMemoryOverlay command, CancellationToken cancellationToken)
            => runtimeServices.ResolveMemoryOverlay!(command, cancellationToken);

        public Task<MemoryMutationResult> RecordMemoryFeedbackAsync(RecordMemoryFeedback command, CancellationToken cancellationToken)
            => runtimeServices.RecordMemoryFeedback!(command, cancellationToken);
    }

    private sealed class KernelMcpResourceToolServicesAdapter : ITianShuMcpResourceToolServices
    {
        private readonly KernelToolRuntimeServices runtimeServices;

        public KernelMcpResourceToolServicesAdapter(KernelToolRuntimeServices runtimeServices)
        {
            this.runtimeServices = runtimeServices;
        }

        public async Task<TianShuMcpListResourcesResult> ListResourcesAsync(string? server, string? cursor, CancellationToken cancellationToken)
        {
            var result = await runtimeServices.ListMcpResources!(server, cursor, cancellationToken).ConfigureAwait(false);
            return new TianShuMcpListResourcesResult(
                result.Server,
                result.Resources.Select(static entry => new TianShuMcpResourceEntry(entry.Server, entry.Resource.Clone())).ToArray(),
                result.NextCursor);
        }

        public async Task<TianShuMcpListResourceTemplatesResult> ListResourceTemplatesAsync(string? server, string? cursor, CancellationToken cancellationToken)
        {
            var result = await runtimeServices.ListMcpResourceTemplates!(server, cursor, cancellationToken).ConfigureAwait(false);
            return new TianShuMcpListResourceTemplatesResult(
                result.Server,
                result.ResourceTemplates.Select(static entry => new TianShuMcpResourceTemplateEntry(entry.Server, entry.Template.Clone())).ToArray(),
                result.NextCursor);
        }

        public async Task<TianShuMcpReadResourceResult> ReadResourceAsync(string server, string uri, CancellationToken cancellationToken)
        {
            var result = await runtimeServices.ReadMcpResource!(server, uri, cancellationToken).ConfigureAwait(false);
            return new TianShuMcpReadResourceResult(result.Server, result.Uri, result.Result.Clone());
        }
    }

    private sealed class KernelFileMutationToolServicesAdapter : ITianShuFileMutationToolServices
    {
        private readonly KernelToolCallContext context;

        public KernelFileMutationToolServicesAdapter(KernelToolCallContext context)
        {
            this.context = context;
        }

        public bool IsWritePathAllowed(string fullPath)
            => KernelSandboxEnforcer.EnsureWritePathAllowed(
                    fullPath,
                    context.Cwd,
                    context.SandboxPolicy,
                    context.SandboxMode)
                .Allowed;

        public bool IsFileChangeApproved(string fullPath)
            => KernelFileChangeApprovalHelpers.IsApproved(fullPath, context.ApprovedFileChangePaths);
    }

    private sealed class KernelShellToolServicesAdapter : ITianShuShellToolServices
    {
        private readonly KernelToolCallContext context;

        public KernelShellToolServicesAdapter(KernelToolCallContext context)
        {
            this.context = context;
        }

        public async Task<TianShuShellToolResult> InvokeShellToolAsync(
            TianShuShellToolRequest request,
            CancellationToken cancellationToken)
        {
            var arguments = ToJsonElement(request.Arguments);
            var result = request.ToolKey switch
            {
                "shell" or "local_shell" => await ShellToolExecutor.ExecuteShellAsync(
                    arguments,
                    context,
                    KernelExecRunner.ExecuteAsync,
                    cancellationToken).ConfigureAwait(false),
                "shell_command" => await ShellToolExecutor.ExecuteShellCommandAsync(
                    arguments,
                    context,
                    KernelExecRunner.ExecuteAsync,
                    cancellationToken).ConfigureAwait(false),
                "exec_command" => await KernelCommittedUnifiedExecExecutor.ExecuteCommandAsync(
                    arguments,
                    context,
                    ShellUnifiedExecManager,
                    cancellationToken).ConfigureAwait(false),
                "write_stdin" => await KernelCommittedUnifiedExecExecutor.WriteStdinAsync(
                    arguments,
                    context,
                    ShellUnifiedExecManager,
                    cancellationToken).ConfigureAwait(false),
                _ => new KernelToolResult(false, $"unsupported shell tool: {request.ToolKey}"),
            };

            var structuredOutput = result.StructuredOutput.HasValue
                ? StructuredValue.FromJsonElement(result.StructuredOutput.Value)
                : null;
            return new TianShuShellToolResult(result.Success, result.OutputText, structuredOutput);
        }

        private static JsonElement ToJsonElement(StructuredValue value)
            => JsonSerializer.SerializeToElement(value);
    }

    private sealed class KernelInteractionToolServicesAdapter : ITianShuInteractionToolServices
    {
        private readonly KernelToolCallContext context;

        public KernelInteractionToolServicesAdapter(KernelToolCallContext context)
        {
            this.context = context;
        }

        public async Task<TianShuInteractionToolResult> InvokeInteractionToolAsync(
            TianShuInteractionToolRequest request,
            CancellationToken cancellationToken)
        {
            return request.ToolKey switch
            {
                "request_user_input" => await InvokeRequestUserInputAsync(request, cancellationToken).ConfigureAwait(false),
                "request_permissions" => await InvokeRequestPermissionsAsync(request, cancellationToken).ConfigureAwait(false),
                _ => new TianShuInteractionToolResult(false, $"unsupported interaction tool: {request.ToolKey}"),
            };
        }

        private async Task<TianShuInteractionToolResult> InvokeRequestUserInputAsync(
            TianShuInteractionToolRequest request,
            CancellationToken cancellationToken)
        {
            var collaborationMode = context.CollaborationMode ?? KernelCollaborationModeState.CreateDefault("unknown");
            if (!collaborationMode.AllowsRequestUserInput(context.DefaultModeRequestUserInputEnabled))
            {
                return new TianShuInteractionToolResult(
                    false,
                    $"request_user_input is unavailable in {FormatModeName(collaborationMode.Mode)} mode");
            }

            var parsedRequest = ParseRequestUserInput(ToJsonElement(request.Arguments), context.ItemId, out var error);
            if (parsedRequest is null)
            {
                return new TianShuInteractionToolResult(false, error ?? "request_user_input handler received unsupported payload");
            }

            if (context.UserInputRequester is null)
            {
                return new TianShuInteractionToolResult(false, "request_user_input runtime bridge is unavailable");
            }

            var response = await context.UserInputRequester(parsedRequest, cancellationToken).ConfigureAwait(false);
            var payload = new
            {
                answers = response.Answers.ToDictionary(
                    static entry => entry.Key,
                    static entry => new { answers = entry.Value.Answers })
            };

            return new TianShuInteractionToolResult(true, JsonSerializer.Serialize(payload));
        }

        private async Task<TianShuInteractionToolResult> InvokeRequestPermissionsAsync(
            TianShuInteractionToolRequest request,
            CancellationToken cancellationToken)
        {
            if (context.PermissionRequester is null)
            {
                return new TianShuInteractionToolResult(false, "request_permissions runtime bridge is unavailable");
            }

            if (!context.RequestPermissionsEnabled)
            {
                return new TianShuInteractionToolResult(false, "request_permissions is disabled by current policy");
            }

            var arguments = ToJsonElement(request.Arguments);
            if (!KernelPermissionGrantProfile.TryParseRequestPermissions(
                    arguments,
                    context.Cwd,
                    out var reason,
                    out var permissions,
                    out var errorMessage))
            {
                return new TianShuInteractionToolResult(
                    false,
                    errorMessage ?? "request_permissions handler received unsupported payload");
            }

            var parsedRequest = new KernelRequestPermissionsRequest(
                KernelToolJsonHelpers.Normalize(context.ItemId) ?? $"request_permissions_{Guid.NewGuid():N}",
                context.Cwd,
                reason,
                permissions);
            var response = await context.PermissionRequester(parsedRequest, cancellationToken).ConfigureAwait(false);
            return new TianShuInteractionToolResult(
                true,
                JsonSerializer.Serialize(response.Permissions.BuildResponsePayload(response.Scope)));
        }

        private static KernelRequestUserInputRequest? ParseRequestUserInput(
            JsonElement arguments,
            string? itemId,
            out string? error)
        {
            error = null;
            if (arguments.ValueKind != JsonValueKind.Object
                || !arguments.TryGetProperty("questions", out var questionsElement)
                || questionsElement.ValueKind != JsonValueKind.Array)
            {
                error = "questions must be a non-empty array";
                return null;
            }

            var questions = new List<KernelRequestUserInputQuestion>();
            foreach (var questionElement in questionsElement.EnumerateArray())
            {
                if (questionElement.ValueKind != JsonValueKind.Object)
                {
                    error = "questions must contain objects";
                    return null;
                }

                var id = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(questionElement, "id"));
                var header = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(questionElement, "header"));
                var question = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(questionElement, "question"));
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(question))
                {
                    error = "each question must contain id, header, and question";
                    return null;
                }

                if (!questionElement.TryGetProperty("options", out var optionsElement)
                    || optionsElement.ValueKind != JsonValueKind.Array)
                {
                    error = "request_user_input requires non-empty options for every question";
                    return null;
                }

                var parsedOptions = new List<KernelRequestUserInputOption>();
                foreach (var optionElement in optionsElement.EnumerateArray())
                {
                    if (optionElement.ValueKind != JsonValueKind.Object)
                    {
                        error = "question options must contain objects";
                        return null;
                    }

                    var label = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(optionElement, "label"));
                    var description = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(optionElement, "description"));
                    if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(description))
                    {
                        error = "each option must contain label and description";
                        return null;
                    }

                    parsedOptions.Add(new KernelRequestUserInputOption(label, description));
                }

                if (parsedOptions.Count == 0)
                {
                    error = "request_user_input requires non-empty options for every question";
                    return null;
                }

                var isSecret = KernelToolJsonHelpers.ReadBool(questionElement, "is_secret")
                    ?? KernelToolJsonHelpers.ReadBool(questionElement, "isSecret")
                    ?? false;

                questions.Add(new KernelRequestUserInputQuestion(id, header, question, parsedOptions, IsOther: true, isSecret));
            }

            if (questions.Count == 0)
            {
                error = "questions must be a non-empty array";
                return null;
            }

            return new KernelRequestUserInputRequest(
                KernelToolJsonHelpers.Normalize(itemId) ?? $"request_user_input_{Guid.NewGuid():N}",
                questions);
        }

        private static string FormatModeName(string mode)
        {
            var normalized = KernelToolJsonHelpers.Normalize(mode) ?? KernelCollaborationModeState.DefaultMode;
            if (normalized.Length == 0)
            {
                return "Default";
            }

            return char.ToUpperInvariant(normalized[0]) + normalized[1..];
        }

        private static JsonElement ToJsonElement(StructuredValue value)
            => JsonSerializer.SerializeToElement(value);
    }

    private sealed class KernelCollaborationToolServicesAdapter : ITianShuCollaborationToolServices
    {
        private readonly KernelToolCallContext context;

        public KernelCollaborationToolServicesAdapter(KernelToolCallContext context)
        {
            this.context = context;
        }

        public async Task<TianShuCollaborationToolResult> InvokeCollaborationToolAsync(
            TianShuCollaborationToolRequest request,
            CancellationToken cancellationToken)
        {
            return request.ToolKey switch
            {
                "update_plan" => await InvokeUpdatePlanAsync(request, cancellationToken).ConfigureAwait(false),
                "spawn_agent" => await InvokeSpawnAgentAsync(request, cancellationToken).ConfigureAwait(false),
                "send_input" => await InvokeSendInputAsync(request, cancellationToken).ConfigureAwait(false),
                "resume_agent" => await InvokeResumeAgentAsync(request, cancellationToken).ConfigureAwait(false),
                "wait" => await InvokeWaitAsync(request, cancellationToken).ConfigureAwait(false),
                "close_agent" => await InvokeCloseAgentAsync(request, cancellationToken).ConfigureAwait(false),
                _ => new TianShuCollaborationToolResult(false, $"unsupported collaboration tool: {request.ToolKey}"),
            };
        }

        private async Task<TianShuCollaborationToolResult> InvokeUpdatePlanAsync(
            TianShuCollaborationToolRequest request,
            CancellationToken cancellationToken)
        {
            var collaborationMode = context.CollaborationMode ?? KernelCollaborationModeState.CreateDefault("unknown");
            if (string.Equals(collaborationMode.Mode, KernelCollaborationModeState.PlanMode, StringComparison.OrdinalIgnoreCase))
            {
                return new TianShuCollaborationToolResult(false, "update_plan is a TODO/checklist tool and is not allowed in Plan mode");
            }

            if (context.RuntimeServices?.UpdatePlan is null)
            {
                return new TianShuCollaborationToolResult(false, "update_plan is unavailable");
            }

            var arguments = ToJsonElement(request.Arguments);
            var plan = new List<KernelPlanStep>();
            if (arguments.TryGetProperty("plan", out var planElement) && planElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in planElement.EnumerateArray())
                {
                    var step = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "step"));
                    var status = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "status"));
                    if (string.IsNullOrWhiteSpace(step) || string.IsNullOrWhiteSpace(status))
                    {
                        return new TianShuCollaborationToolResult(false, "plan items require step and status");
                    }

                    plan.Add(new KernelPlanStep(step!, status!));
                }
            }

            if (plan.Count == 0)
            {
                return new TianShuCollaborationToolResult(false, "plan must contain at least one step");
            }

            await context.RuntimeServices.UpdatePlan(
                new KernelPlanUpdateRequest(
                    KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "explanation")),
                    plan),
                cancellationToken).ConfigureAwait(false);
            return new TianShuCollaborationToolResult(true, "Plan updated");
        }

        private async Task<TianShuCollaborationToolResult> InvokeSpawnAgentAsync(
            TianShuCollaborationToolRequest request,
            CancellationToken cancellationToken)
        {
            if (context.RuntimeServices?.SpawnAgent is null)
            {
                return new TianShuCollaborationToolResult(false, "spawn_agent is unavailable");
            }

            var spawnRequest = ToolSchemaHelpers.ParseSpawnAgentRequest(ToJsonElement(request.Arguments), out var error);
            if (spawnRequest is null)
            {
                return new TianShuCollaborationToolResult(false, error ?? "invalid spawn_agent arguments");
            }

            spawnRequest = spawnRequest with
            {
                ParentCallId = context.ExternalCallId ?? context.ItemId,
            };

            var response = await context.RuntimeServices.SpawnAgent(spawnRequest, cancellationToken).ConfigureAwait(false);
            return new TianShuCollaborationToolResult(true, JsonSerializer.Serialize(new
            {
                agent_id = response.AgentId,
                nickname = response.Nickname,
            }));
        }

        private async Task<TianShuCollaborationToolResult> InvokeSendInputAsync(
            TianShuCollaborationToolRequest request,
            CancellationToken cancellationToken)
        {
            if (context.RuntimeServices?.SendInputToAgent is null)
            {
                return new TianShuCollaborationToolResult(false, "send_input is unavailable");
            }

            var arguments = ToJsonElement(request.Arguments);
            var id = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "id"));
            if (string.IsNullOrWhiteSpace(id))
            {
                return new TianShuCollaborationToolResult(false, "id is required");
            }

            var input = ToolSchemaHelpers.ParseSharedInput(arguments, out var error);
            if (input is null)
            {
                return new TianShuCollaborationToolResult(false, error ?? "invalid send_input arguments");
            }

            var response = await context.RuntimeServices.SendInputToAgent(
                new KernelSendInputRequest(
                    id!,
                    input.Value.Message,
                    input.Value.Items,
                    KernelToolJsonHelpers.ReadBool(arguments, "interrupt") ?? false),
                cancellationToken).ConfigureAwait(false);
            return new TianShuCollaborationToolResult(true, JsonSerializer.Serialize(new { submission_id = response.SubmissionId }));
        }

        private async Task<TianShuCollaborationToolResult> InvokeResumeAgentAsync(
            TianShuCollaborationToolRequest request,
            CancellationToken cancellationToken)
        {
            if (context.RuntimeServices?.ResumeAgent is null)
            {
                return new TianShuCollaborationToolResult(false, "resume_agent is unavailable");
            }

            var arguments = ToJsonElement(request.Arguments);
            var id = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "id"));
            if (string.IsNullOrWhiteSpace(id))
            {
                return new TianShuCollaborationToolResult(false, "id is required");
            }

            var status = await context.RuntimeServices.ResumeAgent(id!, cancellationToken).ConfigureAwait(false);
            return new TianShuCollaborationToolResult(true, JsonSerializer.Serialize(new JsonObject
            {
                ["status"] = status?.DeepClone(),
            }));
        }

        private async Task<TianShuCollaborationToolResult> InvokeWaitAsync(
            TianShuCollaborationToolRequest request,
            CancellationToken cancellationToken)
        {
            if (context.RuntimeServices?.WaitOnAgents is null)
            {
                return new TianShuCollaborationToolResult(false, "wait is unavailable");
            }

            var arguments = ToJsonElement(request.Arguments);
            var ids = ToolSchemaHelpers.ReadStringArray(arguments, "ids");
            if (ids.Count == 0)
            {
                return new TianShuCollaborationToolResult(false, "ids must contain at least one agent id");
            }

            var timeoutMs = KernelToolJsonHelpers.ReadInt(arguments, "timeout_ms");
            if (timeoutMs is <= 0)
            {
                return new TianShuCollaborationToolResult(false, "timeout_ms must be greater than zero");
            }

            var response = await context.RuntimeServices.WaitOnAgents(ids, timeoutMs, cancellationToken).ConfigureAwait(false);
            var status = new JsonObject();
            foreach (var pair in response.Status)
            {
                status[pair.Key] = pair.Value?.DeepClone();
            }

            return new TianShuCollaborationToolResult(true, JsonSerializer.Serialize(new JsonObject
            {
                ["status"] = status,
                ["timed_out"] = response.TimedOut,
            }));
        }

        private async Task<TianShuCollaborationToolResult> InvokeCloseAgentAsync(
            TianShuCollaborationToolRequest request,
            CancellationToken cancellationToken)
        {
            if (context.RuntimeServices?.CloseAgent is null)
            {
                return new TianShuCollaborationToolResult(false, "close_agent is unavailable");
            }

            var arguments = ToJsonElement(request.Arguments);
            var id = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "id"));
            if (string.IsNullOrWhiteSpace(id))
            {
                return new TianShuCollaborationToolResult(false, "id is required");
            }

            var status = await context.RuntimeServices.CloseAgent(id!, cancellationToken).ConfigureAwait(false);
            return new TianShuCollaborationToolResult(true, JsonSerializer.Serialize(new JsonObject
            {
                ["status"] = status?.DeepClone(),
            }));
        }

        private static JsonElement ToJsonElement(StructuredValue value)
            => JsonSerializer.SerializeToElement(value);
    }

    private sealed class KernelFanoutToolServicesAdapter : ITianShuFanoutToolServices
    {
        private readonly KernelToolCallContext context;

        public KernelFanoutToolServicesAdapter(KernelToolCallContext context)
        {
            this.context = context;
        }

        public async Task<TianShuFanoutToolResult> InvokeFanoutToolAsync(
            TianShuFanoutToolRequest request,
            CancellationToken cancellationToken)
        {
            return request.ToolKey switch
            {
                "spawn_agents_on_csv" => await InvokeSpawnAgentsOnCsvAsync(request, cancellationToken).ConfigureAwait(false),
                "report_agent_job_result" => await InvokeReportAgentJobResultAsync(request, cancellationToken).ConfigureAwait(false),
                _ => new TianShuFanoutToolResult(false, $"unsupported fanout tool: {request.ToolKey}"),
            };
        }

        private async Task<TianShuFanoutToolResult> InvokeSpawnAgentsOnCsvAsync(
            TianShuFanoutToolRequest request,
            CancellationToken cancellationToken)
        {
            if (context.RuntimeServices?.SpawnAgentsOnCsv is null)
            {
                return new TianShuFanoutToolResult(false, "spawn_agents_on_csv is unavailable");
            }

            var arguments = ToJsonElement(request.Arguments);
            var csvPath = KernelToolJsonHelpers.Normalize(
                KernelToolJsonHelpers.ReadString(arguments, "csv_path")
                ?? KernelToolJsonHelpers.ReadString(arguments, "csvPath"));
            var instruction = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "instruction"));
            if (string.IsNullOrWhiteSpace(csvPath) || string.IsNullOrWhiteSpace(instruction))
            {
                return new TianShuFanoutToolResult(false, "csv_path and instruction are required");
            }

            JsonElement? outputSchema = null;
            if (arguments.TryGetProperty("output_schema", out var outputSchemaElement))
            {
                if (outputSchemaElement.ValueKind != JsonValueKind.Object)
                {
                    return new TianShuFanoutToolResult(false, "output_schema must be a JSON object");
                }

                outputSchema = outputSchemaElement.Clone();
            }

            try
            {
                var response = await context.RuntimeServices.SpawnAgentsOnCsv(
                    new KernelSpawnAgentsOnCsvRequest(
                        csvPath!,
                        instruction!,
                        KernelToolJsonHelpers.Normalize(
                            KernelToolJsonHelpers.ReadString(arguments, "id_column")
                            ?? KernelToolJsonHelpers.ReadString(arguments, "idColumn")),
                        KernelToolJsonHelpers.Normalize(
                            KernelToolJsonHelpers.ReadString(arguments, "output_csv_path")
                            ?? KernelToolJsonHelpers.ReadString(arguments, "outputCsvPath")),
                        KernelToolJsonHelpers.ReadInt(arguments, "max_concurrency"),
                        KernelToolJsonHelpers.ReadInt(arguments, "max_workers"),
                        KernelToolJsonHelpers.ReadInt(arguments, "max_runtime_seconds"),
                        outputSchema),
                    cancellationToken).ConfigureAwait(false);
                return new TianShuFanoutToolResult(true, JsonSerializer.Serialize(new
                {
                    job_id = response.JobId,
                    status = response.Status,
                    output_csv_path = response.OutputCsvPath,
                    total_items = response.TotalItems,
                    completed_items = response.CompletedItems,
                    failed_items = response.FailedItems,
                    job_error = response.JobError,
                    failed_item_errors = response.FailedItemErrors?.Select(static item => new
                    {
                        item_id = item.ItemId,
                        source_id = item.SourceId,
                        last_error = item.LastError,
                    }),
                }));
            }
            catch (Exception ex)
            {
                return new TianShuFanoutToolResult(false, $"failed to process csv job: {ex.Message}");
            }
        }

        private async Task<TianShuFanoutToolResult> InvokeReportAgentJobResultAsync(
            TianShuFanoutToolRequest request,
            CancellationToken cancellationToken)
        {
            if (context.RuntimeServices?.ReportAgentJobResult is null)
            {
                return new TianShuFanoutToolResult(false, "report_agent_job_result is unavailable");
            }

            var arguments = ToJsonElement(request.Arguments);
            var jobId = KernelToolJsonHelpers.Normalize(
                KernelToolJsonHelpers.ReadString(arguments, "job_id")
                ?? KernelToolJsonHelpers.ReadString(arguments, "jobId"));
            var itemId = KernelToolJsonHelpers.Normalize(
                KernelToolJsonHelpers.ReadString(arguments, "item_id")
                ?? KernelToolJsonHelpers.ReadString(arguments, "itemId"));
            if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(itemId))
            {
                return new TianShuFanoutToolResult(false, "job_id and item_id are required");
            }

            if (!arguments.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            {
                return new TianShuFanoutToolResult(false, "result must be a JSON object");
            }

            var stop = KernelToolJsonHelpers.ReadBool(arguments, "stop") ?? false;
            try
            {
                var accepted = await context.RuntimeServices.ReportAgentJobResult(
                    jobId!,
                    itemId!,
                    result.Clone(),
                    stop,
                    cancellationToken).ConfigureAwait(false);
                return new TianShuFanoutToolResult(true, JsonSerializer.Serialize(new
                {
                    accepted,
                }));
            }
            catch (Exception ex)
            {
                return new TianShuFanoutToolResult(false, $"failed to record agent job result for {jobId} / {itemId}: {ex.Message}");
            }
        }

        private static JsonElement ToJsonElement(StructuredValue value)
            => JsonSerializer.SerializeToElement(value);
    }

    private sealed class KernelCodeToolServicesAdapter : ITianShuCodeToolServices
    {
        private readonly KernelToolCallContext context;

        public KernelCodeToolServicesAdapter(KernelToolCallContext context)
        {
            this.context = context;
        }

        public async Task<TianShuCodeToolResult> InvokeCodeToolAsync(
            TianShuCodeToolRequest request,
            CancellationToken cancellationToken)
        {
            return request.ToolKey switch
            {
                "exec" => await InvokeExecAsync(request, cancellationToken).ConfigureAwait(false),
                "exec_wait" => await InvokeExecWaitAsync(request, cancellationToken).ConfigureAwait(false),
                "js_repl" => await InvokeJsReplAsync(request, cancellationToken).ConfigureAwait(false),
                "js_repl_reset" => await InvokeJsReplResetAsync(request, cancellationToken).ConfigureAwait(false),
                _ => new TianShuCodeToolResult(false, $"unsupported code tool: {request.ToolKey}"),
            };
        }

        private async Task<TianShuCodeToolResult> InvokeExecAsync(
            TianShuCodeToolRequest request,
            CancellationToken cancellationToken)
        {
            var parseResult = KernelCodeModeRuntimeSupport.ParseExecFreeformInput(request.CustomInput ?? string.Empty);
            if (!parseResult.Success || parseResult.Request is null)
            {
                return new TianShuCodeToolResult(false, parseResult.Error ?? "exec 输入无效。");
            }

            if (context.RuntimeServices?.ExecuteCodeMode is null)
            {
                return new TianShuCodeToolResult(false, "exec is unavailable");
            }

            var result = await context.RuntimeServices.ExecuteCodeMode(parseResult.Request, cancellationToken).ConfigureAwait(false);
            return BuildCodeResult(result.Success, result.Output, result.ContentItems);
        }

        private async Task<TianShuCodeToolResult> InvokeExecWaitAsync(
            TianShuCodeToolRequest request,
            CancellationToken cancellationToken)
        {
            if (context.RuntimeServices?.WaitOnCodeMode is null)
            {
                return new TianShuCodeToolResult(false, "exec_wait is unavailable");
            }

            var arguments = ToJsonElement(request.Arguments);
            var cellId = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "cell_id"));
            if (string.IsNullOrWhiteSpace(cellId))
            {
                return new TianShuCodeToolResult(false, "cell_id is required");
            }

            var waitRequest = new KernelCodeModeWaitRequest(
                cellId!,
                KernelToolJsonHelpers.ReadInt(arguments, "yield_time_ms") ?? KernelCodeModeManager.DefaultWaitYieldTimeMs,
                KernelToolJsonHelpers.ReadInt(arguments, "max_tokens"),
                KernelToolJsonHelpers.ReadBool(arguments, "terminate") ?? false);
            var result = await context.RuntimeServices.WaitOnCodeMode(waitRequest, cancellationToken).ConfigureAwait(false);
            return BuildCodeResult(result.Success, result.Output, result.ContentItems);
        }

        private async Task<TianShuCodeToolResult> InvokeJsReplAsync(
            TianShuCodeToolRequest request,
            CancellationToken cancellationToken)
        {
            KernelJsReplExecutionRequest jsRequest;
            if (request.CustomInput is { } customInput)
            {
                var parseResult = KernelJsReplRuntimeSupport.ParseFreeformInput(customInput);
                if (!parseResult.Success || parseResult.Request is null)
                {
                    return new TianShuCodeToolResult(false, parseResult.Error ?? "js_repl 输入无效。");
                }

                jsRequest = parseResult.Request;
            }
            else
            {
                var arguments = ToJsonElement(request.Arguments);
                var code = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "code"));
                if (string.IsNullOrWhiteSpace(code))
                {
                    return new TianShuCodeToolResult(false, "js_repl expects raw JavaScript tool input (non-empty). Provide JS source text, optionally with first-line `// tianshu-js-repl: ...`.");
                }

                jsRequest = new KernelJsReplExecutionRequest(code!, KernelToolJsonHelpers.ReadInt(arguments, "timeout_ms"));
            }

            if (context.RuntimeServices?.ExecuteJsRepl is null)
            {
                return new TianShuCodeToolResult(false, "js_repl is unavailable");
            }

            var result = await context.RuntimeServices.ExecuteJsRepl(jsRequest, cancellationToken).ConfigureAwait(false);
            return BuildCodeResult(result.Success, result.Output, result.ContentItems);
        }

        private async Task<TianShuCodeToolResult> InvokeJsReplResetAsync(
            TianShuCodeToolRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            if (context.RuntimeServices?.ResetJsRepl is null)
            {
                return new TianShuCodeToolResult(false, "js_repl_reset is unavailable");
            }

            await context.RuntimeServices.ResetJsRepl(cancellationToken).ConfigureAwait(false);
            return new TianShuCodeToolResult(true, "js_repl kernel reset");
        }

        private static TianShuCodeToolResult BuildCodeResult(
            bool success,
            string output,
            IReadOnlyList<KernelToolOutputContentItem> contentItems)
            => new(
                success,
                output,
                OutputContentItems: contentItems.Select(static item =>
                    new ToolOutputContentItem(item.Type, item.Text, item.ImageUrl, item.Detail)).ToArray());

        private static JsonElement ToJsonElement(StructuredValue value)
            => JsonSerializer.SerializeToElement(value);
    }

    private sealed class KernelArtifactToolServicesAdapter : ITianShuArtifactToolServices
    {
        private readonly KernelToolCallContext context;

        public KernelArtifactToolServicesAdapter(KernelToolCallContext context)
        {
            this.context = context;
        }

        public async Task<TianShuArtifactToolResult> InvokeArtifactToolAsync(
            TianShuArtifactToolRequest request,
            CancellationToken cancellationToken)
        {
            return request.ToolKey switch
            {
                "artifacts" => await InvokeArtifactsAsync(request, cancellationToken).ConfigureAwait(false),
                "view_image" => await InvokeViewImageAsync(request, cancellationToken).ConfigureAwait(false),
                _ => new TianShuArtifactToolResult(false, $"unsupported artifact tool: {request.ToolKey}"),
            };
        }

        private async Task<TianShuArtifactToolResult> InvokeArtifactsAsync(
            TianShuArtifactToolRequest request,
            CancellationToken cancellationToken)
        {
            KernelArtifactsExecutionRequest artifactRequest;
            if (request.CustomInput is { } customInput)
            {
                var parseResult = KernelArtifactsRuntimeSupport.ParseFreeformInput(customInput);
                if (!parseResult.Success || parseResult.Request is null)
                {
                    return new TianShuArtifactToolResult(false, parseResult.Error ?? "artifacts 输入无效。");
                }

                artifactRequest = parseResult.Request;
            }
            else
            {
                var arguments = ToJsonElement(request.Arguments);
                var code = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "code"));
                if (string.IsNullOrWhiteSpace(code))
                {
                    return new TianShuArtifactToolResult(false, "artifacts expects raw JavaScript source text (non-empty) authored against the preloaded TianShu artifact surface. Provide JS only, optionally with first-line `// tianshu-artifacts: timeout_ms=15000` or `// tianshu-artifact-tool: timeout_ms=15000`.");
                }

                artifactRequest = new KernelArtifactsExecutionRequest(code!, KernelToolJsonHelpers.ReadInt(arguments, "timeout_ms"));
            }

            if (context.RuntimeServices?.ExecuteArtifacts is null)
            {
                return new TianShuArtifactToolResult(false, "artifacts is unavailable");
            }

            var result = await context.RuntimeServices.ExecuteArtifacts(artifactRequest, cancellationToken).ConfigureAwait(false);
            return new TianShuArtifactToolResult(result.Success, result.Output);
        }

        private async Task<TianShuArtifactToolResult> InvokeViewImageAsync(
            TianShuArtifactToolRequest request,
            CancellationToken cancellationToken)
        {
            var result = await ViewImageToolExecutor.ExecuteAsync(
                ToJsonElement(request.Arguments),
                context,
                cancellationToken).ConfigureAwait(false);
            return FromKernelToolResult(result);
        }

        private static TianShuArtifactToolResult FromKernelToolResult(KernelToolResult result)
        {
            var structuredOutput = result.StructuredOutput.HasValue
                ? StructuredValue.FromJsonElement(result.StructuredOutput.Value)
                : null;
            return new TianShuArtifactToolResult(
                result.Success,
                result.OutputText,
                structuredOutput,
                result.OutputContentItems?.Select(static item =>
                    new ToolOutputContentItem(item.Type, item.Text, item.ImageUrl, item.Detail)).ToArray(),
                result.RawOutputContentItems?.Select(static item => item.Clone()).ToArray());
        }

        private static JsonElement ToJsonElement(StructuredValue value)
            => JsonSerializer.SerializeToElement(value);
    }

    private sealed class KernelToolSuggestionServicesAdapter : ITianShuToolSuggestionServices
    {
        private readonly KernelToolCallContext context;

        public KernelToolSuggestionServicesAdapter(KernelToolCallContext context)
        {
            this.context = context;
        }

        public async Task<IReadOnlyList<TianShuToolSuggestConnectorInfo>> ListDiscoverableConnectorsAsync(CancellationToken cancellationToken)
        {
            var connectors = await context.RuntimeServices!.ListToolSuggestDiscoverableConnectors!(cancellationToken).ConfigureAwait(false);
            return connectors
                .Select(static connector => new TianShuToolSuggestConnectorInfo(
                    connector.Id,
                    connector.Name,
                    connector.Description,
                    connector.InstallUrl))
                .ToArray();
        }

        public async Task<TianShuToolSuggestionResult> SuggestConnectorAsync(
            TianShuToolSuggestionRequest request,
            CancellationToken cancellationToken)
        {
            var connectors = await context.RuntimeServices!.ListToolSuggestDiscoverableConnectors!(cancellationToken).ConfigureAwait(false);
            var connector = connectors.FirstOrDefault(
                candidate => string.Equals(candidate.Id, request.ToolId, StringComparison.OrdinalIgnoreCase));
            if (connector is null)
            {
                throw new InvalidOperationException($"tool_id must match one of the discoverable tools exposed by {KernelToolDiscoveryToolNames.Suggest}");
            }

            if (context.McpServerElicitationRequester is null)
            {
                throw new InvalidOperationException("mcpServer/elicitation/request is unavailable");
            }

            var elicitationResponse = await context.McpServerElicitationRequester(
                KernelToolDiscoveryRuntimeSupport.BuildToolSuggestionElicitationRequest(
                    connector,
                    request.SuggestReason,
                    request.ToolType,
                    request.ActionType),
                cancellationToken).ConfigureAwait(false);

            var userConfirmed = string.Equals(elicitationResponse.Action, "accept", StringComparison.OrdinalIgnoreCase);
            var completed = false;
            if (userConfirmed && context.RuntimeServices.RefreshOpenAiAppsToolSnapshot is not null)
            {
                var snapshot = await context.RuntimeServices.RefreshOpenAiAppsToolSnapshot(cancellationToken).ConfigureAwait(false);
                completed = snapshot?.AccessibleConnectors.Any(
                    accessibleConnector => string.Equals(accessibleConnector.Id, connector.Id, StringComparison.OrdinalIgnoreCase)) == true;
            }

            return new TianShuToolSuggestionResult(
                completed,
                userConfirmed,
                "connector",
                "install",
                connector.Id,
                connector.Name,
                request.SuggestReason);
        }
    }

}

internal static class KernelToolProviderAssemblyLoader
{
    public static IReadOnlyList<ITianShuToolProvider> LoadProviders(KernelToolPackageManifest manifest)
    {
        if (!IsAssemblyBacked(manifest.Type)
            || string.IsNullOrWhiteSpace(manifest.AssemblyPath)
            || string.IsNullOrWhiteSpace(manifest.ProviderType))
        {
            return [];
        }

        var type = LoadProviderType(manifest);
        if (type is null || !typeof(ITianShuToolProvider).IsAssignableFrom(type))
        {
            return [];
        }

        return Activator.CreateInstance(type) is ITianShuToolProvider provider ? [provider] : [];
    }

    private static bool IsAssemblyBacked(string type)
        => string.Equals(type, KernelToolPackageManifest.AssemblyType, StringComparison.OrdinalIgnoreCase)
           || string.Equals(type, KernelToolPackageManifest.PackageType, StringComparison.OrdinalIgnoreCase)
           || string.Equals(type, KernelToolPackageManifest.PluginType, StringComparison.OrdinalIgnoreCase);

    private static Type? LoadProviderType(KernelToolPackageManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.AssemblyPath)
            || string.IsNullOrWhiteSpace(manifest.ProviderType))
        {
            return null;
        }

        var resolvedPath = ResolvePath(manifest.AssemblyPath, manifest.PackageDirectory);
        if (!File.Exists(resolvedPath))
        {
            resolvedPath = ProbeBundledProviderPath(manifest.AssemblyPath);
            if (resolvedPath is null)
            {
                return null;
            }
        }

        var assemblyName = AssemblyName.GetAssemblyName(resolvedPath);
        var assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(existing =>
            AssemblyName.ReferenceMatchesDefinition(existing.GetName(), assemblyName))
            ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(resolvedPath);
        return assembly.GetType(manifest.ProviderType, throwOnError: false, ignoreCase: false);
    }

    private static string ResolvePath(string path, string? packageDirectory)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }

        var root = string.IsNullOrWhiteSpace(packageDirectory) ? Environment.CurrentDirectory : packageDirectory;
        return Path.GetFullPath(Path.Combine(root, path));
    }

    private static string? ProbeBundledProviderPath(string path)
    {
        var fileName = Path.GetFileName(path);
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, fileName)),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}

internal static class KernelToolPackageManifestLoader
{
    private const string BuiltinManifestResourceName = "TianShu.AppHost.Tools.Runtime.Resources.tools.builtin.tool.toml";
    public static IReadOnlyList<KernelToolPackageManifest> LoadManifests(
        IReadOnlyDictionary<string, string>? configValues,
        string? workspacePath)
    {
        var manifests = new List<KernelToolPackageManifest>();
        var userManifests = LoadUserToolPackageManifests();
        if (userManifests.Any(static manifest => string.Equals(manifest.Id, "builtin", StringComparison.OrdinalIgnoreCase)))
        {
            manifests.AddRange(userManifests);
        }
        else
        {
            manifests.AddRange(ParseManifestBundle(ReadEmbeddedBuiltinManifest(), manifestPath: null));
            manifests.AddRange(userManifests);
        }

        manifests.AddRange(LoadConfiguredProviderManifests(configValues, workspacePath));
        return manifests
            .GroupBy(static manifest => manifest.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderBy(static manifest => string.Equals(manifest.Type, KernelToolPackageManifest.BuiltinType, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenByDescending(static manifest => manifest.Priority)
                .First())
            .OrderBy(static manifest => string.Equals(manifest.Type, KernelToolPackageManifest.BuiltinType, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(static manifest => manifest.Priority)
            .ThenBy(static manifest => manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<KernelToolPackageManifest> LoadUserToolPackageManifests()
    {
        var manifests = new List<KernelToolPackageManifest>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var toolRoot in ResolveToolRootPaths())
        {
            foreach (var manifestPath in Directory.EnumerateDirectories(toolRoot)
                         .Select(static directory => Path.Combine(directory, "tool.toml"))
                         .Where(File.Exists))
            {
                var packageManifests = ParseManifestBundle(File.ReadAllText(manifestPath), manifestPath);
                var manifest = packageManifests[0];
                if (seenIds.Add(manifest.Id))
                {
                    manifests.AddRange(packageManifests);
                }
            }
        }

        return manifests
            .ToArray();
    }

    private static IEnumerable<KernelToolPackageManifest> LoadConfiguredProviderManifests(
        IReadOnlyDictionary<string, string>? configValues,
        string? workspacePath)
    {
        if (configValues is null || configValues.Count == 0)
        {
            yield break;
        }

        foreach (var providerId in EnumerateProviderIds(configValues))
        {
            var enabled = ReadBool(configValues, $"tool_providers.{providerId}.enabled") ?? true;
            var kind = Normalize(ReadString(configValues, $"tool_providers.{providerId}.type")) ?? KernelToolPackageManifest.AssemblyType;
            var assemblyPath = ReadString(configValues, $"tool_providers.{providerId}.assembly_path", $"tool_providers.{providerId}.path");
            var providerType = ReadString(configValues, $"tool_providers.{providerId}.provider_type", $"tool_providers.{providerId}.type_name");
            var minTianShuVersion = ReadString(configValues, $"tool_providers.{providerId}.min_tianshu_version");
            var compatible = IsCompatible(minTianShuVersion, out var unavailableReason);
            yield return new KernelToolPackageManifest(
                providerId,
                kind,
                enabled && compatible,
                ReadInt(configValues, $"tool_providers.{providerId}.priority") ?? 0,
                assemblyPath,
                providerType,
                workspacePath,
                EnabledToolSets: [],
                DisabledToolSets: [],
                Version: ReadString(configValues, $"tool_providers.{providerId}.version"),
                MinTianShuVersion: minTianShuVersion,
                Capabilities: [],
                Diagnostics: [],
                LoadStatus: compatible ? KernelToolPackageManifest.LoadStatusAvailable : KernelToolPackageManifest.LoadStatusUnavailable,
                UnavailableReason: unavailableReason,
                ReplaceExisting: ReadBool(configValues, $"tool_providers.{providerId}.replace_existing") ?? false);
        }
    }

    private static IEnumerable<string> EnumerateProviderIds(IReadOnlyDictionary<string, string> configValues)
    {
        return configValues.Keys
            .Where(static key => key.StartsWith("tool_providers.", StringComparison.OrdinalIgnoreCase))
            .Select(static key => key["tool_providers.".Length..])
            .Select(static rest => rest.Split('.', 2)[0])
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal);
    }

    private static IReadOnlyList<KernelToolPackageManifest> ParseManifestBundle(string text, string? manifestPath)
    {
        var parent = ParseManifest(text, manifestPath);
        var providers = ParseProviderManifests(text, manifestPath, parent);
        if (providers.Count == 0)
        {
            return [parent];
        }

        var manifests = new List<KernelToolPackageManifest>(providers.Count + 1) { parent };
        manifests.AddRange(providers);
        return manifests;
    }

    private static KernelToolPackageManifest ParseManifest(string text, string? manifestPath)
    {
        var sections = ParseTomlSections(text);
        sections.TryGetValue(string.Empty, out var root);
        root ??= [];
        var packageDirectory = manifestPath is null ? null : Path.GetDirectoryName(Path.GetFullPath(manifestPath));
        var id = ReadTomlScalar(root, "id")
                 ?? (string.IsNullOrWhiteSpace(packageDirectory) ? "builtin" : Path.GetFileName(packageDirectory));
        var type = ReadTomlScalar(root, "type") ?? KernelToolPackageManifest.AssemblyType;
        var toolSets = GetSection(sections, "tool_sets");
        var minTianShuVersion = ReadTomlScalar(root, "min_tianshu_version");
        var compatible = IsCompatible(minTianShuVersion, out var unavailableReason);
        return new KernelToolPackageManifest(
            id,
            type,
            (ReadTomlBoolean(root, "enabled") ?? true) && compatible,
            ReadTomlInt(root, "priority") ?? 0,
            ReadTomlScalar(root, "assembly_path") ?? ReadTomlScalar(root, "path"),
            ReadTomlScalar(root, "provider_type") ?? ReadTomlScalar(root, "type_name"),
            packageDirectory,
            ReadTomlStringArray(toolSets, "enabled") ?? [],
            ReadTomlStringArray(toolSets, "disabled") ?? [],
            ReadTomlScalar(root, "version"),
            minTianShuVersion,
            ReadTomlStringArray(root, "capabilities") ?? [],
            ReadTomlStringArray(root, "diagnostics") ?? [],
            compatible ? KernelToolPackageManifest.LoadStatusAvailable : KernelToolPackageManifest.LoadStatusUnavailable,
            unavailableReason,
            ReadTomlBoolean(root, "replace_existing") ?? false);
    }

    private static IReadOnlyList<KernelToolPackageManifest> ParseProviderManifests(
        string text,
        string? manifestPath,
        KernelToolPackageManifest parent)
    {
        var packageDirectory = manifestPath is null ? null : Path.GetDirectoryName(Path.GetFullPath(manifestPath));
        var providerSections = ParseTomlArraySections(text, "providers");
        if (providerSections.Count == 0)
        {
            return [];
        }

        var providers = new List<KernelToolPackageManifest>(providerSections.Count);
        foreach (var section in providerSections)
        {
            var providerId = ReadTomlScalar(section, "id");
            if (string.IsNullOrWhiteSpace(providerId))
            {
                continue;
            }

            var minTianShuVersion = ReadTomlScalar(section, "min_tianshu_version") ?? parent.MinTianShuVersion;
            var providerCompatible = IsCompatible(minTianShuVersion, out var providerUnavailableReason);
            var parentUnavailable = string.Equals(parent.LoadStatus, KernelToolPackageManifest.LoadStatusUnavailable, StringComparison.OrdinalIgnoreCase);
            providers.Add(new KernelToolPackageManifest(
                $"{parent.Id}.{providerId}",
                ReadTomlScalar(section, "type") ?? KernelToolPackageManifest.AssemblyType,
                parent.Enabled && (ReadTomlBoolean(section, "enabled") ?? true) && providerCompatible,
                ReadTomlInt(section, "priority") ?? parent.Priority,
                ReadTomlScalar(section, "assembly_path") ?? ReadTomlScalar(section, "path"),
                ReadTomlScalar(section, "provider_type") ?? ReadTomlScalar(section, "type_name"),
                packageDirectory,
                EnabledToolSets: [],
                DisabledToolSets: [],
                Version: ReadTomlScalar(section, "version") ?? parent.Version,
                MinTianShuVersion: minTianShuVersion,
                Capabilities: ReadTomlStringArray(section, "capabilities") ?? parent.Capabilities,
                Diagnostics: ReadTomlStringArray(section, "diagnostics") ?? parent.Diagnostics,
                LoadStatus: parentUnavailable || !providerCompatible
                    ? KernelToolPackageManifest.LoadStatusUnavailable
                    : KernelToolPackageManifest.LoadStatusAvailable,
                UnavailableReason: parentUnavailable ? parent.UnavailableReason : providerUnavailableReason,
                ReplaceExisting: ReadTomlBoolean(section, "replace_existing") ?? false));
        }

        return providers;
    }

    private static Dictionary<string, string> GetSection(
        IReadOnlyDictionary<string, Dictionary<string, string>> sections,
        string name)
        => sections.TryGetValue(name, out var section) ? section : [];

    private static Dictionary<string, Dictionary<string, string>> ParseTomlSections(string text)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = new(StringComparer.OrdinalIgnoreCase),
        };
        var currentSection = string.Empty;
        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (line.StartsWith("[[", StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal))
                {
                    currentSection = line[2..^2].Trim();
                    continue;
                }

                currentSection = line[1..^1].Trim();
                if (!sections.ContainsKey(currentSection))
                {
                    sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                continue;
            }

            var equalIndex = line.IndexOf('=');
            if (equalIndex <= 0)
            {
                continue;
            }

            if (sections.TryGetValue(currentSection, out var section))
            {
                section[line[..equalIndex].Trim()] = line[(equalIndex + 1)..].Trim();
            }
        }

        return sections;
    }

    private static IReadOnlyList<Dictionary<string, string>> ParseTomlArraySections(string text, string sectionName)
    {
        var sections = new List<Dictionary<string, string>>();
        Dictionary<string, string>? currentSection = null;
        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[[", StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal))
            {
                var currentName = line[2..^2].Trim();
                currentSection = string.Equals(currentName, sectionName, StringComparison.OrdinalIgnoreCase)
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : null;
                if (currentSection is not null)
                {
                    sections.Add(currentSection);
                }

                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = null;
                continue;
            }

            if (currentSection is null)
            {
                continue;
            }

            var equalIndex = line.IndexOf('=');
            if (equalIndex <= 0)
            {
                continue;
            }

            currentSection[line[..equalIndex].Trim()] = line[(equalIndex + 1)..].Trim();
        }

        return sections;
    }

    private static string StripComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (line[i] == '#' && !inString)
            {
                return line[..i];
            }
        }

        return line;
    }

    private static string? ReadTomlScalar(IReadOnlyDictionary<string, string> section, string key)
        => section.TryGetValue(key, out var raw) ? Normalize(Unquote(raw.Trim())) : null;

    private static bool? ReadTomlBoolean(IReadOnlyDictionary<string, string> section, string key)
        => section.TryGetValue(key, out var raw) && bool.TryParse(Unquote(raw.Trim()), out var value) ? value : null;

    private static int? ReadTomlInt(IReadOnlyDictionary<string, string> section, string key)
        => section.TryGetValue(key, out var raw) && int.TryParse(Unquote(raw.Trim()), out var value) ? value : null;

    private static IReadOnlyList<string>? ReadTomlStringArray(IReadOnlyDictionary<string, string> section, string key)
    {
        if (!section.TryGetValue(key, out var raw))
        {
            return null;
        }

        raw = raw.Trim();
        if (!raw.StartsWith('[') || !raw.EndsWith(']'))
        {
            return null;
        }

        var values = raw[1..^1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static value => Normalize(Unquote(value)))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();
        return values;
    }

    private static bool IsCompatible(string? minTianShuVersion, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(minTianShuVersion))
        {
            return true;
        }

        if (!Version.TryParse(minTianShuVersion, out var minimum))
        {
            reason = $"min_tianshu_version 不是有效版本：{minTianShuVersion}";
            return false;
        }

        if (!Version.TryParse("0.1.0", out var current))
        {
            reason = "当前 TianShu 版本不是有效版本：0.1.0";
            return false;
        }

        if (current < minimum)
        {
            reason = $"需要 TianShu >= {minimum}，当前为 {current}";
            return false;
        }

        return true;
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return value;
    }

    private static IReadOnlyList<string> ResolveToolRootPaths()
    {
        var root = ConfigurationHomePaths.ResolveTianShuModulePath("tools", "packages");
        return new[] { root }
            .Where(Directory.Exists)
            .ToArray();
    }

    private static string ReadEmbeddedBuiltinManifest()
    {
        var assembly = typeof(KernelToolPackageManifestLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(BuiltinManifestResourceName)
            ?? throw new InvalidOperationException($"Embedded builtin tool manifest not found: {BuiltinManifestResourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? ReadString(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!values.TryGetValue(key, out var raw))
            {
                continue;
            }

            var value = ReadJsonScalar(raw);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var raw) && bool.TryParse(ReadJsonScalar(raw), out var value) ? value : null;

    private static int? ReadInt(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var raw) && int.TryParse(ReadJsonScalar(raw), out var value) ? value : null;

    private static string? ReadJsonScalar(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.String => Normalize(document.RootElement.GetString()),
                JsonValueKind.Number => document.RootElement.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return Normalize(raw.Trim().Trim('"', '\''));
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
