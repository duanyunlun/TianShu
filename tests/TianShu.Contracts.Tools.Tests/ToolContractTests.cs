using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using TianShu.Contracts.Kernel;
using System.Reflection;

namespace TianShu.Contracts.Tools.Tests;

public sealed class ToolContractTests
{
    [Fact]
    public void ToolDescriptor_RejectsBlankKey()
    {
        Assert.Throws<ArgumentException>(() => new ToolDescriptor(" ", "Shell", "Run commands"));
    }

    [Fact]
    public void ToolInvocationResult_PreservesArtifactAndStreamItems()
    {
        var result = new ToolInvocationResult(
            new CallId("call-tool"),
            "shell",
            new[]
            {
                new ToolStreamItem("stdout", StructuredValue.FromString("hello")),
            },
            new ArtifactRef(new ArtifactId("artifact-tool"), "stdout.txt", "text"));

        Assert.Single(result.StreamItems);
        Assert.Equal("artifact-tool", result.OutputArtifact?.Id.Value);
    }

    [Fact]
    public void ToolInvocationResultProjector_WhenFailure_ShouldPreserveStructuredOutput()
    {
        var result = new ToolInvocationResult(
            new CallId("call-failure"),
            "read_file",
            failure: new ToolInvocationFailure("tool_failed", "read failed"));

        var projection = ToolInvocationResultProjector.Project(result);

        Assert.False(projection.Success);
        Assert.Equal("read failed", projection.OutputText);
        Assert.Equal("read_file", projection.StructuredOutput.GetProperty("ToolKey").GetString());
        Assert.Equal("tool_failed", projection.StructuredOutput.GetProperty("Failure").GetProperty("Code").GetString());
    }

    [Fact]
    public void ToolInvocationResultProjector_WhenTerminalTextTool_ShouldProjectTerminalText()
    {
        var result = new ToolInvocationResult(
            new CallId("call-shell"),
            "shell",
            streamItems:
            [
                new ToolStreamItem("text", StructuredValue.FromPlainObject("done"), isTerminal: true),
            ]);

        var projection = ToolInvocationResultProjector.Project(result);

        Assert.True(projection.Success);
        Assert.Equal("done", projection.OutputText);
        Assert.True(ToolInvocationResultProjector.ShouldProjectTerminalText("memory_search"));
        Assert.False(ToolInvocationResultProjector.ShouldProjectTerminalText("custom_tool"));
    }

    [Fact]
    public void ToolInvocationResultProjector_WhenContentItemsExist_ShouldPreserveContentAndRawItems()
    {
        var rawOutput = JsonSerializer.SerializeToElement(new { type = "text", text = "raw" });
        var result = new ToolInvocationResult(
            new CallId("call-content"),
            "custom_tool",
            outputContentItems:
            [
                new ToolOutputContentItem("input_text", Text: "hello"),
                new ToolOutputContentItem("input_image", ImageUrl: "data:image/png;base64,abc", Detail: "high"),
            ],
            rawOutputContentItems: [rawOutput]);

        var projection = ToolInvocationResultProjector.Project(result);

        Assert.True(projection.Success);
        Assert.Equal(2, projection.OutputContentItems.Count);
        Assert.Equal("hello", projection.OutputContentItems[0].Text);
        Assert.Equal("data:image/png;base64,abc", projection.OutputContentItems[1].ImageUrl);
        Assert.Equal("raw", Assert.Single(projection.RawOutputContentItems).GetProperty("text").GetString());
        Assert.Contains("\"ToolKey\":\"custom_tool\"", projection.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolUseFollowUpItemProjector_BuildsProviderToolUseItems()
    {
        var functionCall = ToolUseFollowUpItemProjector.BuildFunctionCallItem(
            name: "spawn_agent",
            arguments: "{\"message\":\"继续处理\"}",
            callId: "call-function");
        var functionOutput = ToolUseFollowUpItemProjector.BuildFunctionCallOutputItem(
            "call-function",
            isCustomToolCall: false,
            output: "done");
        var customOutput = ToolUseFollowUpItemProjector.BuildFunctionCallOutputItem(
            "call-custom",
            isCustomToolCall: true,
            output: "custom done");
        var toolSearchOutput = ToolUseFollowUpItemProjector.BuildToolSearchOutputItem(
            "call-search",
            [JsonSerializer.SerializeToElement(new { name = "read_file" })]);
        var cancelledShellOutput = ToolUseFollowUpItemProjector.BuildCancelledFunctionCallOutputItem(
            "call-shell",
            "shell",
            isCustomToolCall: false,
            elapsedSeconds: 0.02);

        Assert.Equal("function_call", functionCall["type"]);
        Assert.Equal("spawn_agent", functionCall["name"]);
        Assert.Equal("{\"message\":\"继续处理\"}", functionCall["arguments"]);
        Assert.Equal("call-function", functionCall["call_id"]);
        Assert.Equal("function_call_output", functionOutput["type"]);
        Assert.Equal("call-function", functionOutput["call_id"]);
        Assert.Equal("done", functionOutput["output"]);
        Assert.Equal("custom_tool_call_output", customOutput["type"]);
        Assert.Equal("tool_search_output", toolSearchOutput["type"]);
        Assert.Equal("completed", toolSearchOutput["status"]);
        Assert.Equal("client", toolSearchOutput["execution"]);
        var tools = Assert.IsType<JsonElement[]>(toolSearchOutput["tools"]);
        Assert.Equal("read_file", Assert.Single(tools).GetProperty("name").GetString());
        Assert.Equal("Wall time: 0.1 seconds\naborted by user", cancelledShellOutput["output"]);
    }

    [Fact]
    public void ToolUseFollowUpItemProjector_BuildsModelToolCallItemId()
    {
        var itemId = ToolUseFollowUpItemProjector.BuildModelToolCallItemId(
            "call/with unsafe chars and a very very very very very very long suffix",
            "tool.name/with spaces");

        Assert.Equal("tool_tool_name_with_spaces_call_with_unsafe_chars_and_a_very_very_very_very", itemId);
    }

    [Fact]
    public void ToolUseFollowUpItemProjector_ExtractsToolSearchOutputTools()
    {
        var structured = JsonSerializer.SerializeToElement(new
        {
            tools = new[]
            {
                new { name = "structured_tool" },
            },
        });
        var text = JsonSerializer.Serialize(new
        {
            tools = new[]
            {
                new { name = "text_tool" },
            },
        });

        var structuredTools = ToolUseFollowUpItemProjector.ExtractToolSearchOutputTools(
            success: true,
            outputText: null,
            structuredOutput: structured);
        var textTools = ToolUseFollowUpItemProjector.ExtractToolSearchOutputTools(
            success: true,
            outputText: text,
            structuredOutput: null);
        var failedTools = ToolUseFollowUpItemProjector.ExtractToolSearchOutputTools(
            success: false,
            outputText: text,
            structuredOutput: structured);

        Assert.Equal("structured_tool", Assert.Single(structuredTools).GetProperty("name").GetString());
        Assert.Equal("text_tool", Assert.Single(textTools).GetProperty("name").GetString());
        Assert.Empty(failedTools);
    }

    [Fact]
    public void ToolUseFollowUpItemProjector_BuildsFunctionCallOutputPayload()
    {
        var textOnlyPayload = ToolUseFollowUpItemProjector.BuildFunctionCallOutputPayload(
            "plain output",
            null);
        var contentPayload = ToolUseFollowUpItemProjector.BuildFunctionCallOutputPayload(
            "ignored when content items exist",
            [
                new ToolOutputContentItem(" input_text ", Text: " hello "),
                new ToolOutputContentItem("input_image", ImageUrl: "data:image/png;base64,abc", Detail: " high "),
            ]);
        var preview = ToolUseFollowUpItemProjector.BuildTextPreview(
            [
                new ToolOutputContentItem("input_text", Text: " first "),
                new ToolOutputContentItem("input_image", ImageUrl: "data:image/png;base64,abc"),
                new ToolOutputContentItem("input_text", Text: "second"),
            ]);

        Assert.Equal("plain output", textOnlyPayload);
        var contentItems = Assert.IsType<Dictionary<string, object?>[]>(contentPayload);
        Assert.Equal("input_text", contentItems[0]["type"]);
        Assert.Equal(" hello ", contentItems[0]["text"]);
        Assert.Equal("input_image", contentItems[1]["type"]);
        Assert.Equal("data:image/png;base64,abc", contentItems[1]["image_url"]);
        Assert.Equal("high", contentItems[1]["detail"]);
        Assert.Equal($"first{Environment.NewLine}second", preview);
    }

    [Fact]
    public void ToolDescriptor_PreservesImplementationBinding()
    {
        var binding = new ToolImplementationBinding(
            "grep_files",
            ToolImplementationKind.Managed,
            implementationId: "dotnet-managed",
            requirements:
            [
                new ToolRuntimeRequirement("file_system", "File system"),
            ],
            probe: new ToolCapabilityProbe(available: true),
            fallbackPolicy: new ToolFallbackPolicy(
                "managed_default",
                [ToolImplementationKind.Managed, ToolImplementationKind.ExternalProcess]),
            platformProfile: new PlatformToolProfile(
                "windows",
                enabledToolKeys: ["grep_files"],
                defaultImplementationKinds: [ToolImplementationKind.Managed]));

        var descriptor = new ToolDescriptor(
            "grep_files",
            "Grep files",
            "Find files by content.",
            implementationBinding: binding);

        Assert.Equal(ToolImplementationKind.Managed, descriptor.ImplementationBinding?.ImplementationKind);
        Assert.Equal("file_system", Assert.Single(descriptor.ImplementationBinding!.Requirements).Key);
        Assert.Equal("managed_default", descriptor.ImplementationBinding.FallbackPolicy?.Strategy);
        Assert.Equal("windows", descriptor.ImplementationBinding.PlatformProfile?.Platform);
    }

    [Fact]
    public void PublicToolContracts_DoNotExposeAppHostRuntimeTypes()
    {
        var exportedTypes = typeof(ITianShuToolProvider).Assembly.GetExportedTypes()
            .Where(static type => type.Namespace?.StartsWith("TianShu.Contracts.Tools", StringComparison.Ordinal) == true)
            .ToArray();

        foreach (var type in exportedTypes)
        {
            Assert.False(IsForbiddenPublicType(type), $"Forbidden public type surfaced: {type.FullName}");

            foreach (var memberType in EnumeratePublicMemberTypes(type))
            {
                Assert.False(
                    IsForbiddenPublicType(memberType),
                    $"{type.FullName} exposes forbidden runtime type {memberType.FullName}");
            }
        }
    }

    [Fact]
    public void ToolInvocationContext_ExposesGovernedHostServiceInterfaces()
    {
        var constructor = typeof(TianShuToolInvocationContext).GetConstructors().Single();
        var serviceParameterTypes = constructor.GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .Where(static type => type.IsInterface && type.Namespace == typeof(ITianShuToolProvider).Namespace)
            .Select(static type => type.Name)
            .ToArray();

        Assert.Contains(nameof(ITianShuMemoryToolServices), serviceParameterTypes);
        Assert.Contains(nameof(ITianShuMcpResourceToolServices), serviceParameterTypes);
        Assert.Contains(nameof(ITianShuFileMutationToolServices), serviceParameterTypes);
        Assert.Contains(nameof(ITianShuShellToolServices), serviceParameterTypes);
        Assert.Contains(nameof(ITianShuInteractionToolServices), serviceParameterTypes);
        Assert.Contains(nameof(ITianShuCollaborationToolServices), serviceParameterTypes);
        Assert.Contains(nameof(ITianShuFanoutToolServices), serviceParameterTypes);
        Assert.Contains(nameof(ITianShuCodeToolServices), serviceParameterTypes);
        Assert.Contains(nameof(ITianShuArtifactToolServices), serviceParameterTypes);
        Assert.Contains(nameof(ITianShuToolSuggestionServices), serviceParameterTypes);
        Assert.Contains(nameof(ITianShuToolDiagnosticServices), serviceParameterTypes);
    }

    [Fact]
    public void ToolDiagnosticEvent_RejectsBlankIdentityAndCarriesMetadata()
    {
        var metadata = new MetadataBag(new Dictionary<string, StructuredValue>
        {
            ["scope"] = StructuredValue.FromString("provider"),
        });

        var diagnostic = new TianShuToolDiagnosticEvent(
            "contract_echo",
            "provider.ready",
            "provider is ready",
            TianShuToolDiagnosticSeverity.Info,
            metadata);

        Assert.Equal("provider.ready", diagnostic.Code);
        Assert.True(diagnostic.Metadata.TryGetValue("scope", out var scope));
        Assert.Equal("provider", scope.GetString());
        Assert.Throws<ArgumentException>(() => new TianShuToolDiagnosticEvent(" ", "code", "message"));
        Assert.Throws<ArgumentException>(() => new TianShuToolDiagnosticEvent("tool", " ", "message"));
        Assert.Throws<ArgumentException>(() => new TianShuToolDiagnosticEvent("tool", "code", " "));
    }

    [Fact]
    public void ToolDescriptor_ExposesUnifiedToolSurfaceAndGovernanceProfiles()
    {
        var descriptor = new ToolDescriptor(
            "tool.shell",
            "Shell",
            "Run shell commands.",
            kind: ToolKind.Capability,
            inputSchemaRef: new JsonSchemaRef("schema.shell.input", "1"),
            outputSchemaRef: new JsonSchemaRef("schema.shell.output", "1"),
            permissions: new PermissionDeclaration(["workspace.read"], requiresHumanGate: true),
            sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly),
            audit: new AuditProfile(eventKinds: ["tool.invoked"]));

        Assert.Equal("tool.shell", descriptor.ToolId);
        Assert.Equal("Shell", descriptor.Name);
        Assert.Equal(ToolKind.Capability, descriptor.Kind);
        Assert.True(descriptor.Permissions.RequiresHumanGate);
        Assert.Equal(SideEffectLevel.ReadOnly, descriptor.SideEffects.Level);
        Assert.True(descriptor.Audit.Required);
    }

    [Fact]
    public void ToolDescriptor_IsAllowedByGovernanceEnvelopeOnlyInsidePolicyBoundary()
    {
        var descriptor = new ToolDescriptor(
            "tool.search",
            "Search",
            "Read-only search.",
            permissions: new PermissionDeclaration(["tool.search"], requiresHumanGate: false),
            sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly),
            audit: new AuditProfile(eventKinds: ["tool.search.invoked"]));
        var allowed = new GovernanceEnvelope(
            "governance-tool",
            allowedToolIds: ["tool.search"],
            maxSideEffectLevel: SideEffectLevel.ReadOnly,
            requiresHumanGate: false);
        var denied = new GovernanceEnvelope(
            "governance-tool-denied",
            allowedToolIds: ["tool.other"],
            maxSideEffectLevel: SideEffectLevel.ReadOnly,
            requiresHumanGate: false);

        Assert.True(descriptor.IsAllowedBy(allowed));
        Assert.False(descriptor.IsAllowedBy(denied));
    }

    [Fact]
    public void ToolDescriptor_IsAllowedByGovernanceEnvelopeRequiresHumanGateWhenDeclared()
    {
        var descriptor = new ToolDescriptor(
            "tool.write",
            "Write",
            "Write workspace files.",
            permissions: new PermissionDeclaration(["tool.write"], requiresHumanGate: true),
            sideEffects: new SideEffectProfile(SideEffectLevel.WorkspaceWrite),
            audit: new AuditProfile(eventKinds: ["tool.write.invoked"]));

        Assert.False(descriptor.IsAllowedBy(new GovernanceEnvelope(
            "governance-tool-without-gate",
            allowedToolIds: ["tool.write"],
            maxSideEffectLevel: SideEffectLevel.WorkspaceWrite,
            requiresHumanGate: false)));
        Assert.True(descriptor.IsAllowedBy(new GovernanceEnvelope(
            "governance-tool-with-gate",
            allowedToolIds: ["tool.write"],
            maxSideEffectLevel: SideEffectLevel.WorkspaceWrite,
            requiresHumanGate: true)));
    }

    [Fact]
    public void ToolInvocationEnvelope_RequiresPermissionAndCarriesSourceContext()
    {
        var permission = new PermissionEnvelope(["workspace.read"]);
        var sideEffect = new SideEffectProfile(SideEffectLevel.ReadOnly);
        var envelope = new ToolInvocationEnvelope(
            new CallId("call-tool-envelope"),
            "tool.search",
            "query",
            StructuredValue.FromString("term"),
            permission,
            sideEffect);
        var context = new ToolInvocationContext(
            "step-tool-envelope",
            "intent-tool-envelope",
            "graph-tool-envelope",
            "stage-tool-envelope",
            "operation-tool-envelope");

        Assert.Equal(permission, envelope.Permission);
        Assert.Equal(sideEffect, envelope.SideEffect);
        Assert.Equal("operation-tool-envelope", context.SourceKernelOperationId);
        Assert.Throws<ArgumentNullException>(() => new ToolInvocationEnvelope(
            new CallId("call-tool-envelope-2"),
            "tool.search",
            "query",
            StructuredValue.FromString("term"),
            null!,
            sideEffect));
    }

    [Fact]
    public void ToolDescriptor_DefaultsGovernanceProfilesWhenOmitted()
    {
        var descriptor = new ToolDescriptor(
            "exec_command",
            "Exec Command",
            "Execute a governed command.",
            approvalRequirement: ToolApprovalRequirement.Required,
            concurrencyClass: ToolConcurrencyClass.Exclusive);

        Assert.Equal(ToolKind.Capability, descriptor.Kind);
        Assert.NotEmpty(descriptor.Permissions.RequiredScopes);
        Assert.True(descriptor.Permissions.RequiresHumanGate);
        Assert.Equal(SideEffectLevel.HostMutation, descriptor.SideEffects.Level);
        Assert.Contains("command", descriptor.SideEffects.AffectedResources);
        Assert.True(descriptor.Audit.Required);
        Assert.NotEmpty(descriptor.Audit.EventKinds);
    }

    [Fact]
    public async Task TianShuToolHandlerAdapter_ShouldInvokeHandlerThroughUnifiedToolSurface()
    {
        var handler = new EchoToolHandler();
        var tool = new TianShuToolHandlerAdapter(handler);
        var invocation = new ToolInvocationEnvelope(
            new CallId("call-unified-tool"),
            "echo",
            "invoke",
            StructuredValue.FromString("hello"),
            new PermissionEnvelope(["tool.echo"], requiresHumanGate: false),
            new SideEffectProfile(SideEffectLevel.ReadOnly));
        var context = new ToolInvocationContext(
            "step-unified-tool",
            "intent-unified-tool",
            "graph-unified-tool",
            "stage-unified-tool",
            "operation-unified-tool");

        var result = await tool.InvokeAsync(invocation, context, CancellationToken.None);

        Assert.Equal("echo", tool.Descriptor.ToolId);
        Assert.Equal("echo", result.ToolKey);
        Assert.Equal("hello", Assert.Single(result.StreamItems).Payload.GetString());
    }

    private static IEnumerable<Type> EnumeratePublicMemberTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        foreach (var constructor in type.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return Unwrap(parameter.ParameterType);
            }
        }

        foreach (var property in type.GetProperties(flags))
        {
            yield return Unwrap(property.PropertyType);
        }

        foreach (var method in type.GetMethods(flags))
        {
            if (method.IsSpecialName)
            {
                continue;
            }

            yield return Unwrap(method.ReturnType);
            foreach (var parameter in method.GetParameters())
            {
                yield return Unwrap(parameter.ParameterType);
            }
        }
    }

    private static Type Unwrap(Type type)
    {
        if (type.IsGenericType)
        {
            return type.GetGenericArguments().Select(Unwrap).FirstOrDefault(IsForbiddenPublicType) ?? type.GetGenericTypeDefinition();
        }

        if (type.HasElementType)
        {
            return Unwrap(type.GetElementType()!);
        }

        return type;
    }

    private static bool IsForbiddenPublicType(Type type)
    {
        var fullName = type.FullName ?? type.Name;
        return fullName.StartsWith("TianShu.AppHost", StringComparison.Ordinal)
               || fullName.StartsWith("TianShu.Provider", StringComparison.Ordinal)
               || type.Name.StartsWith("Kernel", StringComparison.Ordinal);
    }

    private sealed class EchoToolHandler : ITianShuToolHandler
    {
        public ToolDescriptor Descriptor { get; } = new(
            "echo",
            "Echo",
            "Echo input.",
            inputSchemaRef: new JsonSchemaRef("schema.echo.input"),
            outputSchemaRef: new JsonSchemaRef("schema.echo.output"),
            permissions: new PermissionDeclaration(["tool.echo"], requiresHumanGate: false),
            sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly),
            audit: new AuditProfile(eventKinds: ["tool.echo.invoked"]));

        public ValueTask<ToolInvocationResult> InvokeAsync(
            ToolInvocationRequest request,
            TianShuToolInvocationContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ToolInvocationResult(
                request.CallId,
                request.ToolKey,
                [new ToolStreamItem("text", request.Input, isTerminal: true)]));
    }
}
