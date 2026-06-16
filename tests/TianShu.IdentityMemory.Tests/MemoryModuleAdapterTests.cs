using TianShu.Contracts.Kernel;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using TianShu.IdentityMemory;
using TianShu.Tools.Memory;

namespace TianShu.IdentityMemory.Tests;

public sealed class MemoryModuleAdapterTests
{
    [Fact]
    public async Task MemoryServiceModuleAdapter_ShouldRouteFilterQueryThroughModuleSurface()
    {
        var service = new RecordingMemoryService();
        var module = new MemoryServiceModuleAdapter(service);
        var invocation = new MemoryModuleQueryInvocation(
            new FilterMemoryModuleQuery(new FilterMemory(QueryText: "architecture")),
            CreateContext(SideEffectLevel.ReadOnly));

        var result = await module.QueryAsync(invocation, CancellationToken.None);

        Assert.NotNull(result.Records);
        Assert.Equal("architecture", service.LastFilter?.QueryText);
        Assert.Equal("runtime-step", service.LastOperationContext?.CorrelationId);
        Assert.Equal(ModuleKind.MemoryIdentity, module.Descriptor.Kind);
    }

    [Fact]
    public async Task MemoryServiceModuleAdapter_ShouldRouteSupersedeMutationThroughGovernedContext()
    {
        var service = new RecordingMemoryService();
        var module = new MemoryServiceModuleAdapter(service);
        var mutation = new SupersedeMemoryModuleMutation(new SupersedeMemory(
            new MemoryRecordId("memory-old"),
            new MemorySpaceId("memory:user:test"),
            "preference.editor",
            StructuredValue.FromString("rider"),
            "User corrected previous preference.",
            Source: new MemorySourceRef(MemorySourceKind.Conversation, "turn-1")));
        var invocation = new MemoryModuleMutationInvocation(
            mutation,
            CreateContext(SideEffectLevel.ExternalMutation));

        var result = await module.MutateAsync(invocation, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(MemoryMutationEffect.Superseded, result.Effect);
        Assert.Equal("runtime-step", service.LastOperationContext?.CorrelationId);
        Assert.Equal("memory-old", service.LastSupersede?.OldRecordId.Value);
    }

    [Fact]
    public async Task MemoryToolProvider_ShouldBeInvokableThroughUnifiedToolAdapter()
    {
        var provider = new MemoryToolProvider();
        var handler = provider.CreateHandler(
            "memory_search",
            new TianShuToolActivationContext(WorkspacePath: "D:/repo"));
        ITianShuTool tool = new TianShuToolHandlerAdapter(
            handler,
            context => new TianShuToolInvocationContext(
                context.SourceIntentId,
                context.RuntimeStepId,
                context.WorkingDirectory ?? string.Empty,
                MemoryServices: new RecordingMemoryToolServices()));
        var envelope = new ToolInvocationEnvelope(
            new CallId("call-memory"),
            tool.Descriptor.ToolId,
            "invoke",
            StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["query"] = "architecture",
                ["limit"] = 5,
            }),
            new PermissionEnvelope(scopes: ["tool.memory.search"], requiresHumanGate: false),
            new SideEffectProfile(SideEffectLevel.ReadOnly, ["memory"], reversible: true, requiresAudit: true));

        var result = await tool.InvokeAsync(envelope, CreateToolContext(), CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(tool.Descriptor.ToolId, result.ToolKey);
        Assert.NotEmpty(result.StreamItems);
    }

    private static MemoryModuleInvocationContext CreateContext(SideEffectLevel sideEffectLevel)
        => new(
            "runtime-step",
            "intent",
            "graph",
            "stage",
            "operation",
            new PermissionEnvelope(scopes: ["memory.identity"], requiresHumanGate: sideEffectLevel > SideEffectLevel.ReadOnly),
            new SideEffectProfile(sideEffectLevel, ["memory"], reversible: sideEffectLevel <= SideEffectLevel.ReadOnly, requiresAudit: true),
            new MemoryOperationContext("tester", correlationId: "runtime-step"));

    private static ToolInvocationContext CreateToolContext()
        => new("runtime-step", "intent", "graph", "stage", "operation", "D:/repo");

    private static FactMemoryRecord CreateFactRecord()
        => new(
            new MemoryRecordId("memory-1"),
            "architecture",
            StructuredValue.FromString("module-plane"),
            new MemorySpaceId("memory:user:test"),
            1m,
            DateTimeOffset.UtcNow,
            MemoryLifecycleStatus.Active,
            [new MemorySourceRef(MemorySourceKind.Conversation, "turn-1")],
            ["architecture"],
            0,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            MemoryFormationPath.DirectInstruction,
            null,
            [],
            false);

    private sealed class RecordingMemoryService : IMemoryService
    {
        public FilterMemory? LastFilter { get; private set; }

        public SupersedeMemory? LastSupersede { get; private set; }

        public MemoryOperationContext? LastOperationContext { get; private set; }

        public IReadOnlyList<MemoryProviderDescriptor> ListProviders()
            => [CreateProvider()];

        public IReadOnlyList<MemoryProviderDescriptor> ListProviders(ListMemoryProviders query)
            => [CreateProvider()];

        public Task<IReadOnlyList<MemorySpace>> ListSpacesAsync(ListMemorySpaces query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MemorySpace>>(
                [new MemorySpace(new MemorySpaceId("memory:user:test"), MemoryScopeKind.User, "test", "Test")]);

        public Task<MemoryMutationResult> AddAsync(AddMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true, Effect: MemoryMutationEffect.Upserted));

        public Task<IReadOnlyList<MemoryCandidate>> ExtractAsync(ExtractMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<MemoryMutationResult> ImportAsync(ImportMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true, Effect: MemoryMutationEffect.Upserted));

        public Task<MemoryQueryResult> ExportAsync(ExportMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryQueryResult());

        public Task<MemoryMutationResult> BindProviderAsync(BindMemoryProvider command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true, Effect: MemoryMutationEffect.None));

        public Task<MemoryQueryResult> FilterAsync(FilterMemory query, MemoryOperationContext context, CancellationToken cancellationToken)
        {
            LastFilter = query;
            LastOperationContext = context;
            return Task.FromResult(new MemoryQueryResult([CreateFactRecord()]));
        }

        public Task<MemoryReviewQueryResult> ListReviewsAsync(ListMemoryReviews query, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryReviewQueryResult());

        public Task<MemoryMutationResult> ForgetAsync(ForgetMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true, Effect: MemoryMutationEffect.LifecycleChanged));

        public Task<MemoryMutationResult> DeleteAsync(DeleteMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true, Effect: MemoryMutationEffect.SoftDeleted));

        public Task<MemoryMutationResult> SupersedeAsync(SupersedeMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
        {
            LastSupersede = command;
            LastOperationContext = context;
            return Task.FromResult(new MemoryMutationResult(true, Effect: MemoryMutationEffect.Superseded));
        }

        public Task<MemoryMutationResult> ApproveReviewAsync(ApproveMemoryReview command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true, Effect: MemoryMutationEffect.Upserted));

        public Task<MemoryMutationResult> DemoteReviewAsync(DemoteMemoryReview command, MemoryOperationContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<MemoryMutationResult> MergeReviewAsync(MergeMemoryReview command, MemoryOperationContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<MemoryMutationResult> RestoreReviewAsync(RestoreMemoryReview command, MemoryOperationContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<MemoryMutationResult> RecordFeedbackAsync(RecordMemoryFeedback command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true, Effect: MemoryMutationEffect.None));

        public Task<MemoryMutationResult> RecordCitationAsync(RecordMemoryCitation command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true, Effect: MemoryMutationEffect.None));

        private static MemoryProviderDescriptor CreateProvider()
            => new(
                "local",
                "Local Memory",
                "1.0",
                MemoryProviderCapability.ReadOnlyAccess | MemoryProviderCapability.ReadWriteAccess | MemoryProviderCapability.Supersede,
                [MemoryScopeKind.User]);

    }

    private sealed class RecordingMemoryToolServices : ITianShuMemoryToolServices
    {
        public Task<MemoryQueryResult> FilterMemoryAsync(FilterMemory command, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryQueryResult([CreateFactRecord()]));

        public Task<MemoryOverlay> ResolveMemoryOverlayAsync(ResolveMemoryOverlay command, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryOverlay());

        public Task<MemoryMutationResult> RecordMemoryFeedbackAsync(RecordMemoryFeedback command, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true, Effect: MemoryMutationEffect.None));
    }
}
