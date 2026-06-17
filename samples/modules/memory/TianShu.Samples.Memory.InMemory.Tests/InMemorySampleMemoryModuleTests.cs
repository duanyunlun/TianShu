using TianShu.Contracts.Kernel;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Samples.Memory.InMemory.Tests;

public sealed class InMemorySampleMemoryModuleTests
{
    [Fact]
    public void Manifest_ShouldPassMemoryAccessValidation()
    {
        var result = MemoryModuleAccessValidator.Validate(
            InMemorySampleMemoryModule.CreateManifest(),
            InMemorySampleMemoryModule.CreateGovernance(),
            InMemorySampleMemoryModule.CreateApprovedContextPolicy());

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.Equal(InMemorySampleMemoryModule.ModuleId, result.Access!.Manifest.ModuleId);
        Assert.Contains(result.Access.Manifest.Capabilities, static capability => capability.Kind == MemoryModuleCapabilityKind.Supersede);
    }

    [Fact]
    public async Task Module_ShouldAddRetrieveAndSupersedeMemory()
    {
        var module = new InMemorySampleMemoryModule();
        var context = InvocationContext();
        var memorySpaceId = new MemorySpaceId("memory:sample:user");
        var source = new MemorySourceRef(MemorySourceKind.Conversation, "sample-test", snippet: "sample evidence");
        var addResult = await module.MutateAsync(
            new MemoryModuleMutationInvocation(
                new AddMemoryModuleMutation(new AddMemory(
                    memorySpaceId,
                    "sample.preference",
                    StructuredValue.FromString("prefer deterministic examples"),
                    Source: source)),
                context),
            CancellationToken.None);

        var beforeSupersede = await module.QueryAsync(
            new MemoryModuleQueryInvocation(new FilterMemoryModuleQuery(new FilterMemory(memorySpaceId)), context),
            CancellationToken.None);
        var oldRecord = Assert.Single(beforeSupersede.Records!.Records);

        var supersedeResult = await module.MutateAsync(
            new MemoryModuleMutationInvocation(
                new SupersedeMemoryModuleMutation(new SupersedeMemory(
                    oldRecord.Id,
                    memorySpaceId,
                    "sample.preference",
                    StructuredValue.FromString("prefer executable examples"),
                    "sample correction",
                    Source: source)),
                context),
            CancellationToken.None);
        var afterSupersede = await module.QueryAsync(
            new MemoryModuleQueryInvocation(new FilterMemoryModuleQuery(new FilterMemory(memorySpaceId)), context),
            CancellationToken.None);

        Assert.True(addResult.Success);
        Assert.True(supersedeResult.Success);
        var activeRecord = Assert.Single(afterSupersede.Records!.Records);
        Assert.Equal("prefer executable examples", activeRecord.Value.StringValue);
        Assert.Equal(MemoryMutationEffect.Superseded, supersedeResult.Effect);
    }

    [Fact]
    public async Task Module_ShouldFailClosedForUnsupportedMutation()
    {
        var module = new InMemorySampleMemoryModule();
        var result = await module.MutateAsync(
            new MemoryModuleMutationInvocation(
                new ForgetMemoryModuleMutation(new ForgetMemory(Key: "sample.preference")),
                InvocationContext()),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(MemoryMutationEffect.Degraded, result.Effect);
        Assert.Equal("sample.memory.unsupported_mutation", result.DegradedReason);
    }

    private static MemoryModuleInvocationContext InvocationContext()
        => new(
            "runtime-sample-memory",
            "intent-sample-memory",
            "graph-sample-memory",
            "stage-sample-memory",
            "operation-sample-memory",
            new PermissionEnvelope(["memory.form"], requiresHumanGate: true),
            new SideEffectProfile(SideEffectLevel.ExternalMutation, ["memory"], reversible: false),
            new MemoryOperationContext("sample-tester"));
}
