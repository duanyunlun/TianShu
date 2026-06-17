using TianShu.Contracts.Kernel;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Template.MemoryModule.Tests;

public sealed class TemplateMemoryModuleTests
{
    [Fact]
    public void Manifest_ShouldPassMemoryAccessValidation()
    {
        var result = MemoryModuleAccessValidator.Validate(
            TemplateMemoryModule.CreateManifest(),
            TemplateMemoryModule.CreateGovernance(),
            TemplateMemoryModule.CreateApprovedContextPolicy());

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.Equal(TemplateMemoryModule.ModuleId, result.Access!.Manifest.ModuleId);
        Assert.Contains(result.Access.Manifest.Capabilities, static capability => capability.Kind == MemoryModuleCapabilityKind.CompressReserved);
    }

    [Fact]
    public async Task Module_ShouldSupportHealthQueryAndAddMemoryMutation()
    {
        var module = new TemplateMemoryModule();
        var health = await module.CheckAsync(CancellationToken.None);
        var context = InvocationContext();
        var memorySpaceId = new MemorySpaceId("memory:template:user");
        var source = new MemorySourceRef(MemorySourceKind.ToolResult, "tool-template", snippet: "template evidence");
        var mutation = new AddMemoryModuleMutation(new AddMemory(
            memorySpaceId,
            "template.preference",
            StructuredValue.FromString("中文优先"),
            Source: source));

        var mutationResult = await module.MutateAsync(
            new MemoryModuleMutationInvocation(mutation, context),
            CancellationToken.None);
        var queryResult = await module.QueryAsync(
            new MemoryModuleQueryInvocation(
                new FilterMemoryModuleQuery(new FilterMemory(memorySpaceId)),
                context),
            CancellationToken.None);

        Assert.Equal(ModuleHealthStatus.Healthy, health.Status);
        Assert.True(mutationResult.Success);
        var record = Assert.Single(queryResult.Records!.Records);
        Assert.Equal("template.preference", record.Key);
        Assert.Equal("template evidence", Assert.Single(record.Sources).Snippet);
    }

    private static MemoryModuleInvocationContext InvocationContext()
        => new(
            "runtime-template-memory",
            "intent-template-memory",
            "graph-template-memory",
            "stage-template-memory",
            "operation-template-memory",
            new PermissionEnvelope(["memory.form"], requiresHumanGate: true),
            new SideEffectProfile(SideEffectLevel.ExternalMutation, ["memory"], reversible: false),
            new MemoryOperationContext("template-tester"));
}
