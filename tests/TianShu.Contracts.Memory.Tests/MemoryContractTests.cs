using System.Text.Json;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Memory.Tests;

public sealed class MemoryContractTests
{
    [Fact]
    public void MemoryContractsAssembly_ShouldNotReferenceMemoryImplementationsOrSemanticBackends()
    {
        var referencedAssemblies = typeof(MemorySpace)
            .Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name ?? string.Empty)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        var forbiddenPrefixes = new[]
        {
            "TianShu.IdentityMemory",
            "TianShu.Execution.Runtime",
            "TianShu.AppHost",
            "TianShu.Provider.",
        };
        var forbiddenFragments = new[]
        {
            "SemanticKernel",
            "Qdrant",
            "Pinecone",
            "Milvus",
            "Weaviate",
            "LiteDB",
            "Microsoft.Data.Sqlite",
        };

        var forbiddenReferences = referencedAssemblies
            .Where(name => forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))
                           || forbiddenFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(forbiddenReferences);
    }

    [Fact]
    public void MemoryQueries_PreserveScopeAndOverlayTarget()
    {
        var memorySpaceId = new MemorySpaceId("memory:user:semi");

        var listQuery = new ListMemorySpaces(MemoryScopeKind.User);
        var overlayQuery = new ResolveMemoryOverlay(memorySpaceId);

        Assert.Equal(MemoryScopeKind.User, listQuery.ScopeKind);
        Assert.Equal(memorySpaceId, overlayQuery.MemorySpaceId);
        Assert.Null(overlayQuery.CollaborationSpaceId);
    }

    [Fact]
    public void FactMemoryRecord_RejectsConfidenceOutsideRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FactMemoryRecord(
            "decision",
            StructuredValue.FromString("accept"),
            new MemorySpaceId("memory-001"),
            2m));
    }

    [Fact]
    public void MemoryOverlay_PreservesFactsAndHabitProfile()
    {
        var source = new MemorySourceRef(
            MemorySourceKind.Conversation,
            "thread-001",
            role: "user",
            snippet: "以后默认中文汇报",
            capturedAt: new DateTimeOffset(2026, 4, 30, 0, 0, 0, TimeSpan.Zero));
        var overlay = new MemoryOverlay(
            new[]
            {
                new FactMemoryRecord(
                    "decision",
                    StructuredValue.FromString("accept"),
                    new MemorySpaceId("memory-001"),
                    sources: [source],
                    usageCount: 3,
                    lastUsedAt: new DateTimeOffset(2026, 4, 30, 1, 0, 0, TimeSpan.Zero)),
            },
            new HabitProfile(new AccountId("account-memory"), new[] { "shell" }, "high"));

        var fact = Assert.Single(overlay.Facts);
        Assert.Equal(MemoryLifecycleStatus.Active, fact.LifecycleStatus);
        Assert.Single(fact.Sources);
        Assert.Equal(3, fact.UsageCount);
        Assert.Equal("thread-001", fact.Sources[0].SourceId);
        Assert.StartsWith("memory-record:", fact.Id.Value, StringComparison.Ordinal);
        Assert.Equal("high", overlay.HabitProfile?.PreferredVerbosity);
    }

    [Fact]
    public void ThreadMemoryMode_ShouldExposeSessionLevelReadWriteControls()
    {
        Assert.Equal(0, (int)ThreadMemoryMode.ReadWrite);
        Assert.Equal(1, (int)ThreadMemoryMode.ReadOnly);
        Assert.Equal(2, (int)ThreadMemoryMode.Ephemeral);
        Assert.Equal(3, (int)ThreadMemoryMode.Disabled);
    }

    [Fact]
    public void MemoryMutationEffect_ShouldExposeAuditableMutationOutcomes()
    {
        Assert.Equal(0, (int)MemoryMutationEffect.None);
        Assert.Equal(1, (int)MemoryMutationEffect.Upserted);
        Assert.Equal(2, (int)MemoryMutationEffect.LifecycleChanged);
        Assert.Equal(3, (int)MemoryMutationEffect.SoftDeleted);
        Assert.Equal(4, (int)MemoryMutationEffect.PhysicallyDeleted);
        Assert.Equal(5, (int)MemoryMutationEffect.Degraded);
        Assert.Equal(6, (int)MemoryMutationEffect.Superseded);
    }

    [Fact]
    public void MemoryPhaseAContracts_ShouldRepresentProviderFeedbackCitationAndResults()
    {
        var memorySpaceId = new MemorySpaceId("memory:user:semi");
        var source = new MemorySourceRef(MemorySourceKind.Conversation, "thread-001", role: "user");
        var command = new AddMemory(memorySpaceId, "preference.language", StructuredValue.FromString("zh-CN"), 0.8m, source);
        var import = new ImportMemory(memorySpaceId, source);
        var export = new ExportMemory(memorySpaceId, source, new FilterMemory(memorySpaceId, Tag: "language", ScopeKind: MemoryScopeKind.User));
        var record = new FactMemoryRecord(
            command.Key,
            command.Value,
            command.MemorySpaceId,
            command.Confidence,
            id: new MemoryRecordId("mem-001"),
            sources: [source],
            tags: ["language"]);
        var citation = new MemoryCitation(
        [
            new MemoryCitationEntry(record.Id, record.MemorySpaceId, record.Key, source, "used in answer")
        ]);
        var queryResult = new MemoryQueryResult([record], citation: citation);
        var mutationResult = new MemoryMutationResult(
            true,
            record.Id,
            MemoryLifecycleStatus.Active,
            Effect: MemoryMutationEffect.Upserted);
        var provider = new MemoryProviderDescriptor(
            "local",
            "Local Memory",
            "1.0",
            MemoryProviderCapability.ListSpaces | MemoryProviderCapability.Add | MemoryProviderCapability.Filter,
            [MemoryScopeKind.User],
            RequiresNetwork: false,
            RequiresCredentials: false,
            TrustLevel: MemoryProviderTrustLevel.BuiltIn,
            SupportedLifecycleStatuses: [MemoryLifecycleStatus.Active, MemoryLifecycleStatus.Forgotten],
            DegradationStrategy: MemoryProviderDegradationStrategy.UnsupportedResult,
            Features: MemoryProviderFeature.SourceTracking | MemoryProviderFeature.UsageTelemetry | MemoryProviderFeature.SecretRedaction);
        var binding = new MemoryProviderBinding(
            provider.ProviderId,
            memorySpaceId,
            MemoryProviderBindingMode.ReadWrite,
            MemoryProviderCapability.Add | MemoryProviderCapability.Filter);
        var bindCommand = new BindMemoryProvider(
            provider.ProviderId,
            memorySpaceId,
            MemoryProviderBindingMode.ReadWrite,
            MemoryProviderCapability.Add | MemoryProviderCapability.Filter);
        var context = new MemoryOperationContext("user:semi", timestamp: new DateTimeOffset(2026, 4, 30, 2, 0, 0, TimeSpan.Zero));
        var candidate = new MemoryCandidate("preference.language", StructuredValue.FromString("zh-CN"), memorySpaceId, 0.7m, source, "explicit preference", "remember-rule");
        var extractionJob = MemoryExtractionJob.Start(
            memorySpaceId,
            source,
            "RuleBasedMemoryExtractor",
            context.Timestamp).Complete(1, context.Timestamp);
        var usedRecord = record.WithUsage(1, context.Timestamp, context.Timestamp);

        Assert.Equal("preference.language", command.Key);
        Assert.Equal(memorySpaceId, import.MemorySpaceId);
        Assert.Empty(import.Records);
        Assert.Equal(source.SourceId, export.Destination!.SourceId);
        Assert.Equal("language", export.Filter!.Tag);
        Assert.Equal(MemoryScopeKind.User, export.Filter.ScopeKind);
        Assert.Equal(record.Id, mutationResult.RecordId);
        Assert.Equal(MemoryMutationEffect.Upserted, mutationResult.Effect);
        Assert.Equal(1, queryResult.TotalCount);
        Assert.Single(queryResult.Citation!.Entries);
        Assert.Contains("language", record.Tags);
        Assert.Equal(MemoryProviderBindingMode.ReadWrite, binding.Mode);
        Assert.Equal(provider.ProviderId, bindCommand.ProviderId);
        Assert.Equal(MemoryProviderCapability.Add | MemoryProviderCapability.Filter, bindCommand.AllowedCapabilities);
        Assert.Contains(MemoryScopeKind.User, provider.SupportedScopes);
        Assert.Equal(MemoryProviderTrustLevel.BuiltIn, provider.TrustLevel);
        Assert.Contains(MemoryLifecycleStatus.Forgotten, provider.SupportedLifecycleStatuses);
        Assert.Equal(MemoryProviderDegradationStrategy.UnsupportedResult, provider.DegradationStrategy);
        Assert.True(provider.Features.HasFlag(MemoryProviderFeature.SourceTracking));
        Assert.True(provider.Features.HasFlag(MemoryProviderFeature.SecretRedaction));
        Assert.False(provider.Capabilities.HasFlag(MemoryProviderCapability.SemanticSearch));
        Assert.Equal("user:semi", context.ActorId);
        Assert.Equal(MemoryLifecycleStatus.PendingReview, candidate.LifecycleStatus);
        Assert.Equal(MemoryFormationPath.Unknown, candidate.FormationPath);
        Assert.Equal(MemoryExtractionJobStatus.Succeeded, extractionJob.Status);
        Assert.Equal(1, extractionJob.CandidateCount);
        Assert.StartsWith("memory-extraction:", extractionJob.JobId.Value, StringComparison.Ordinal);
        Assert.Equal(1, usedRecord.UsageCount);
        Assert.Equal(context.Timestamp, usedRecord.LastUsedAt);
    }

    [Fact]
    public void MemoryModuleSurface_ShouldExposeGovernedQueryAndMutationEntrypoints()
    {
        var methods = typeof(IMemoryModule).GetMethods().ToDictionary(static method => method.Name, StringComparer.Ordinal);

        Assert.Equal(typeof(IModuleHealthCheck), typeof(IMemoryModule).GetInterfaces().Single(static type => type == typeof(IModuleHealthCheck)));
        Assert.Equal(typeof(ValueTask<MemoryModuleQueryResult>), methods[nameof(IMemoryModule.QueryAsync)].ReturnType);
        Assert.Equal(typeof(ValueTask<MemoryMutationResult>), methods[nameof(IMemoryModule.MutateAsync)].ReturnType);

        var context = new MemoryModuleInvocationContext(
            "runtime-step",
            "intent",
            "graph",
            "stage",
            "operation",
            new PermissionEnvelope(scopes: ["memory.identity"], requiresHumanGate: true),
            new SideEffectProfile(SideEffectLevel.ExternalMutation, ["memory"], reversible: false, requiresAudit: true),
            new MemoryOperationContext("tester", correlationId: "runtime-step"));

        Assert.Equal("runtime-step", context.RuntimeStepId);
        Assert.Equal("operation", context.SourceKernelOperationId);
        Assert.True(context.Permission.RequiresHumanGate);
        Assert.Equal(SideEffectLevel.ExternalMutation, context.SideEffect.Level);
        Assert.Equal("runtime-step", context.OperationContext.CorrelationId);
    }

    [Fact]
    public void MemoryModuleMutationInvocation_ShouldRejectFullReasoningTracePayloads()
    {
        var mutation = new AddMemoryModuleMutation(new AddMemory(
            new MemorySpaceId("memory:user:test"),
            "unsafe.reasoning",
            StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["chain_of_thought"] = "hidden trace",
            })));

        Assert.Throws<ArgumentException>(() => new MemoryModuleMutationInvocation(
            mutation,
            new MemoryModuleInvocationContext(
                "runtime-step",
                "intent",
                "graph",
                "stage",
                "operation",
                new PermissionEnvelope(scopes: ["memory.identity"], requiresHumanGate: true),
                new SideEffectProfile(SideEffectLevel.ExternalMutation, ["memory"], reversible: false, requiresAudit: true),
                new MemoryOperationContext("tester"))));
    }

    [Fact]
    public void MemoryAdvancedContracts_ShouldRepresentSearchCapabilitiesAndOverlayExplainability()
    {
        var memorySpaceId = new MemorySpaceId("memory:workspace:tianshu");
        var recordId = new MemoryRecordId("memory-record:tianshu:search");
        var descriptor = new MemoryProviderDescriptor(
            "semantic-provider",
            "Semantic Provider",
            "1.0",
            MemoryProviderCapability.ListSpaces
            | MemoryProviderCapability.Filter
            | MemoryProviderCapability.KeywordSearch
            | MemoryProviderCapability.SemanticSearch
            | MemoryProviderCapability.EmbeddingIndexing
            | MemoryProviderCapability.LlmExtraction
            | MemoryProviderCapability.ReadOnlyAccess,
            [MemoryScopeKind.Workspace],
            RequiresNetwork: true,
            RequiresCredentials: true,
            TrustLevel: MemoryProviderTrustLevel.External,
            DegradationStrategy: MemoryProviderDegradationStrategy.DegradedRead,
            Features: MemoryProviderFeature.ExternalIndex | MemoryProviderFeature.ModelInvocation);
        var filter = new FilterMemory(
            memorySpaceId,
            QueryText: "WPF HTTP",
            SearchMode: MemorySearchMode.Semantic);
        var explanation = new MemoryOverlayExplanation(
            recordId,
            memorySpaceId,
            "wpf.http.client",
            1,
            16.5m,
            ["scope-match", "keyword-match"],
            "keyword");
        var result = new MemoryQueryResult(
            [
                new FactMemoryRecord(
                    explanation.Key,
                    StructuredValue.FromString("use HttpClient"),
                    memorySpaceId,
                    id: recordId)
            ],
            degradedProviders: ["semantic_search_degraded:keyword"],
            explanations: [explanation],
            EffectiveSearchMode: MemorySearchMode.Keyword);

        Assert.True(descriptor.Capabilities.HasFlag(MemoryProviderCapability.SemanticSearch));
        Assert.True(descriptor.Capabilities.HasFlag(MemoryProviderCapability.EmbeddingIndexing));
        Assert.True(descriptor.Capabilities.HasFlag(MemoryProviderCapability.LlmExtraction));
        Assert.Equal(MemorySearchMode.Semantic, filter.SearchMode);
        Assert.Equal("WPF HTTP", filter.QueryText);
        Assert.Equal(MemorySearchMode.Keyword, result.EffectiveSearchMode);
        Assert.Contains("semantic_search_degraded:keyword", result.DegradedProviders);
        Assert.Equal("keyword", Assert.Single(result.Explanations).RetrievalMode);
    }

    [Fact]
    public void MemoryPhaseAFormationContracts_ShouldRepresentEvidenceRiskContextAndSupersede()
    {
        var memorySpaceId = new MemorySpaceId("memory:workspace:tianshu");
        var oldRecordId = new MemoryRecordId("memory-record:old");
        var newRecordId = new MemoryRecordId("memory-record:new");
        var source = new MemorySourceRef(
            MemorySourceKind.ToolResult,
            "turn-001",
            path: "tests/TianShu.Contracts.Memory.Tests/MemoryContractTests.cs",
            snippet: "architecture lock passed");
        var validation = new MemoryValidationEvidence(
            "evidence:validation:001",
            MemoryValidationEvidenceKind.TestPassed,
            "contracts memory tests passed",
            source);
        var evidence = new MemoryEvidenceRecord(
            "evidence:001",
            memorySpaceId,
            source,
            MemoryEvidenceKind.UserCorrection,
            "user corrected memory contract boundary",
            MemoryScopeKind.Workspace,
            tags: ["memory", "contracts"],
            validationEvidence: [validation]);
        var contextSignature = new MemoryContextSignature(
            memorySpaceIds: [memorySpaceId],
            scopeKinds: [MemoryScopeKind.Workspace],
            tags: ["memory", "contracts"],
            sources: [source],
            lifecycleStatuses: [MemoryLifecycleStatus.Active, MemoryLifecycleStatus.PendingReview],
            excludeRecordIds: [oldRecordId],
            minimumConfidence: 0.75m,
            recordedAfter: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var candidate = new MemoryCandidate(
            "counterexample.build.vsix",
            StructuredValue.FromString("do not use dotnet build for VSIX"),
            memorySpaceId,
            0.82m,
            source,
            "user correction",
            "explicit-correction",
            MemoryFormationPath.ExploratoryLearning,
            [validation],
            contextSignature,
            isCounterexample: true);
        var fact = new FactMemoryRecord(
            "build.entry",
            StructuredValue.FromString("use VS2026 MSBuild chain for VSIX"),
            memorySpaceId,
            0.91m,
            id: newRecordId,
            sources: [source],
            tags: ["build", "vsix"],
            formationPath: MemoryFormationPath.AnalogicalTransfer,
            contextSignature: contextSignature,
            validationEvidence: [validation]);
        var counterexample = new MemoryCounterexample(
            "TianShu VSIX project",
            "dotnet build src/Presentations/TianShu.VSSDK.VSExtension",
            "VSIX project requires Visual Studio build chain",
            "tools/Build-TianShuVsix.ps1",
            [source],
            MemoryFormationPath.DirectInstruction);
        var transferLink = new MemoryTransferLink(
            [oldRecordId],
            "same repository and VSIX build boundary",
            "existing VSIX build rule applies to this project",
            [validation.EvidenceId],
            "TianShu VSIX only",
            newRecordId);
        var learningTrace = new MemoryLearningTrace(
            "repo=tianshu;area=memory-contracts",
            [new MemoryLearningAttemptSummary("added architecture lock", "passed")],
            ["plain upsert equals correction"],
            "supersede command creates new record and link",
            [validation.EvidenceId],
            [oldRecordId]);
        var supersedeCommand = new SupersedeMemory(
            oldRecordId,
            memorySpaceId,
            fact.Key,
            fact.Value,
            "new evidence supersedes old build advice",
            fact.Confidence,
            source);
        var supersedeLink = new MemorySupersedeLink(
            oldRecordId,
            newRecordId,
            supersedeCommand.Reason,
            "user:semi",
            source);
        var promotionDecision = new MemoryPromotionDecision(
            MemoryPromotionDecisionKind.SupersedeProposal,
            [MemoryRiskReasonCode.ConflictsWithActiveFact],
            MemoryLifecycleStatus.PendingReview,
            "requires review because it replaces an active fact",
            supersedeLink);
        var ingestionDecision = new MemoryIngestionDecision(
            MemoryIngestionDecisionKind.NeedsReview,
            [MemoryRiskReasonCode.LongTermBehaviorChange, MemoryRiskReasonCode.ConflictsWithActiveFact],
            MemoryLifecycleStatus.PendingReview,
            promotionDecision: promotionDecision);
        var filter = new FilterMemory(ContextSignature: contextSignature);
        var mutationResult = new MemoryMutationResult(
            true,
            newRecordId,
            MemoryLifecycleStatus.Active,
            Effect: MemoryMutationEffect.Superseded,
            SupersedeLink: supersedeLink);

        Assert.Equal("evidence:001", evidence.EvidenceId);
        Assert.Equal(MemoryEvidenceKind.UserCorrection, evidence.EvidenceKind);
        Assert.Single(evidence.ValidationEvidence);
        Assert.True(candidate.IsCounterexample);
        Assert.Equal(MemoryFormationPath.ExploratoryLearning, candidate.FormationPath);
        Assert.Equal(contextSignature, candidate.ContextSignature);
        Assert.Equal(MemoryFormationPath.AnalogicalTransfer, fact.FormationPath);
        Assert.Single(fact.ValidationEvidence);
        Assert.Equal("tools/Build-TianShuVsix.ps1", counterexample.PreferredAlternative);
        Assert.Equal(newRecordId, transferLink.TargetRecordId);
        Assert.Single(learningTrace.AttemptSummaries);
        Assert.Equal(fact.Key, supersedeCommand.NewKey);
        Assert.Equal(MemoryPromotionDecisionKind.SupersedeProposal, promotionDecision.Kind);
        Assert.Contains(MemoryRiskReasonCode.ConflictsWithActiveFact, ingestionDecision.ReasonCodes);
        Assert.Equal(contextSignature, filter.ContextSignature);
        Assert.Equal(MemoryMutationEffect.Superseded, mutationResult.Effect);
        Assert.Equal(supersedeLink, mutationResult.SupersedeLink);

        var serialized = JsonSerializer.Serialize(ingestionDecision);
        Assert.Contains(nameof(MemoryIngestionDecision.ReasonCodes), serialized, StringComparison.Ordinal);
    }
}
