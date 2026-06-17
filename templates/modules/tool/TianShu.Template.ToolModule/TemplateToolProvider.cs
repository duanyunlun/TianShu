using System.Text.Json;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Template.ToolModule;

/// <summary>
/// 自定义 Tool 模块模板：提供一个只读 echo 工具，并声明治理边界。
/// Custom Tool module template that provides a read-only echo tool and declares its governance boundary.
/// </summary>
public sealed class TemplateToolProvider : ITianShuToolProvider
{
    public const string ModuleId = "template.tool";
    public const string ToolKey = "template.echo";

    public IReadOnlyList<ToolDescriptor> DescribeTools(TianShuToolRegistrationContext context)
        => [TemplateEchoToolHandler.DescriptorValue];

    public ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context)
    {
        if (!string.Equals(toolKey, ToolKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unknown template tool: {toolKey}");
        }

        return new TemplateEchoToolHandler();
    }

    public static ToolModuleManifest CreateManifest()
        => new(
            ModuleId,
            "Template Tool Module",
            "1.0.0",
            "0.6.0",
            tools:
            [
                new ToolModuleToolBinding(
                    ToolKey,
                    "Template Echo",
                    "Echoes governed input for template validation.",
                    inputSchema: TemplateSchemas.EchoInput,
                    outputSchema: TemplateSchemas.EchoOutput,
                    permission: new PermissionDeclaration(["tool.template.echo"], requiresHumanGate: false),
                    sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly, ["runtime"], reversible: true),
                    requiresHumanGate: false),
            ],
            diagnostics: ["template.tool.access"]);

    public static GovernanceEnvelope CreateGovernance()
        => new(
            "template-tool-governance",
            allowedToolIds: [ToolKey],
            allowedModuleIds: [ModuleId],
            maxSideEffectLevel: SideEffectLevel.ReadOnly,
            requiresHumanGate: false);
}

public sealed class TemplateEchoToolHandler : ITianShuToolHandler
{
    public static ToolDescriptor DescriptorValue { get; } = new(
        TemplateToolProvider.ToolKey,
        "Template Echo",
        "Echoes governed input for template validation.",
        inputSchema: TemplateSchemas.EchoInput,
        outputSchema: TemplateSchemas.EchoOutput,
        permissions: new PermissionDeclaration(["tool.template.echo"], requiresHumanGate: false),
        sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly, ["runtime"], reversible: true),
        audit: new AuditProfile(eventKinds: ["tool.template.echo.invoked"]));

    public ToolDescriptor Descriptor => DescriptorValue;

    public ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(new ToolInvocationResult(
            request.CallId,
            request.ToolKey,
            streamItems:
            [
                new ToolStreamItem("text", request.Input, isTerminal: true),
            ]));
    }
}

internal static class TemplateSchemas
{
    public static JsonElement EchoInput { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "text" },
        properties = new
        {
            text = new { type = "string" },
        },
    });

    public static JsonElement EchoOutput { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            text = new { type = "string" },
        },
    });
}
