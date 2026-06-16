using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Identity;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;
using TianShu.IdentityMemory;

namespace TianShu.Execution.Runtime.Tests;

public sealed class IdentityMemoryPlaneTests
{
    [Fact]
    public async Task DefaultTianShuIdentityMemoryPlane_ShouldResolveLocalIdentityAndDevices()
    {
        var sut = new DefaultTianShuIdentityMemoryPlane();
        var context = CreateContext();

        var account = await sut.GetAccountProfileAsync(new GetAccountProfile(context.AccountId), context, CancellationToken.None);
        var devices = await sut.ListBoundDevicesAsync(new ListBoundDevices(context.AccountId), context, CancellationToken.None);

        Assert.NotNull(account);
        Assert.Equal(context.AccountId.Value, account!.Id.Value);
        Assert.Equal("Example", account.DisplayName);
        Assert.Equal("tianshu", account.Metadata.Entries["runtimeName"].StringValue);
        Assert.Equal("platform", account.Metadata.Entries["teamKey"].StringValue);
        Assert.Equal(@"D:\Work\TianShu", account.Metadata.Entries["workspacePath"].StringValue);
        Assert.Equal("space-platform", account.Metadata.Entries["collaborationSpaceId"].StringValue);
        var device = Assert.Single(devices);
        Assert.Equal(context.AccountId.Value, device.AccountId.Value);
        Assert.Equal("Example-PC", device.DeviceName);
        Assert.Equal("Windows", device.Platform);
    }

    [Fact]
    public async Task DefaultTianShuIdentityMemoryPlane_ShouldExposeMemorySpacesAndContextualOverlay()
    {
        var sut = new DefaultTianShuIdentityMemoryPlane();
        var context = CreateContext();

        var spaces = await sut.ListMemorySpacesAsync(new ListMemorySpaces(), context, CancellationToken.None);
        var workspaceOverlay = await sut.ResolveMemoryOverlayAsync(
            new ResolveMemoryOverlay(new MemorySpaceId("memory:workspace:d/gitrepos/personal/tianshu")),
            context,
            CancellationToken.None);
        var collaborationOverlay = await sut.ResolveMemoryOverlayAsync(
            new ResolveMemoryOverlay(CollaborationSpaceId: new CollaborationSpaceId("space-platform")),
            context,
            CancellationToken.None);

        Assert.Contains(spaces, static space => space.ScopeKind == MemoryScopeKind.User);
        Assert.Contains(spaces, static space => space.Id.Value == "memory:workspace:d/gitrepos/personal/tianshu");
        Assert.Contains(spaces, static space => space.ScopeKind == MemoryScopeKind.Workspace);
        Assert.Contains(spaces, static space => space.ScopeKind == MemoryScopeKind.Team);
        Assert.Contains(spaces, static space => space.ScopeKind == MemoryScopeKind.Session);
        Assert.Contains(spaces, static space => space.ScopeKind == MemoryScopeKind.Agent);
        Assert.Contains(spaces, static space => space.ScopeKind == MemoryScopeKind.Collaboration);
        Assert.Contains(spaces, static space => space.ScopeKind == MemoryScopeKind.User && !space.IsReadOnly);
        Assert.Contains(spaces, static space => space.ScopeKind == MemoryScopeKind.Workspace && !space.IsReadOnly);
        Assert.Contains(spaces, static space => space.ScopeKind == MemoryScopeKind.Session && !space.IsReadOnly);
        Assert.Contains(spaces, static space => space.ScopeKind == MemoryScopeKind.Team && space.IsReadOnly);
        Assert.Contains(spaces, static space => space.ScopeKind == MemoryScopeKind.Agent && space.IsReadOnly);
        Assert.Contains(spaces, static space => space.ScopeKind == MemoryScopeKind.Collaboration && space.IsReadOnly);
        Assert.Contains(workspaceOverlay.Facts, static fact => fact.Key == "workspace.cwd");
        Assert.Contains(collaborationOverlay.Facts, static fact => fact.Key == "collaboration.space_id");
        Assert.Equal("high", workspaceOverlay.HabitProfile?.PreferredVerbosity);
        Assert.Contains("shell_command", workspaceOverlay.HabitProfile?.PreferredTools ?? Array.Empty<string>());
        Assert.Contains("team:platform", workspaceOverlay.HabitProfile?.Labels.Values ?? Array.Empty<string>());
        Assert.Contains("collaboration:space-platform", collaborationOverlay.HabitProfile?.Labels.Values ?? Array.Empty<string>());
    }

    [Fact]
    public async Task DefaultTianShuIdentityMemoryPlane_ShouldRespectRuntimeMemoryOptions()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var sut = new DefaultTianShuIdentityMemoryPlane(
            store,
            memoryOptionsResolver: _ => new TianShuMemoryRuntimeOptions
            {
                Profiles =
                [
                    new TianShuMemoryProfileOptions(
                        "workspace",
                        DefaultSpace: "workspace",
                        Overlay: false,
                        Extract: TianShuMemoryExtractMode.Off),
                ],
                Spaces =
                [
                    new TianShuMemorySpaceOptions(
                        "workspace",
                        MemoryScopeKind.Workspace,
                        ProviderId: "local",
                        ReadOnly: true,
                        DisplayName: "配置工作区记忆"),
                ],
                Bindings =
                [
                    new TianShuMemoryProviderBindingOptions(
                        "workspace",
                        "workspace",
                        "local",
                        "read-only",
                        ["filter"]),
                ],
            });
        var context = CreateContext();

        var workspace = Assert.Single(
            await sut.ListMemorySpacesAsync(new ListMemorySpaces(MemoryScopeKind.Workspace), context, CancellationToken.None));
        var addResult = await sut.AddMemoryAsync(
            new AddMemory(workspace.Id, "workspace.configured", StructuredValue.FromString("enabled")),
            context,
            CancellationToken.None);
        var extracted = await sut.ExtractMemoryAsync(
            new ExtractMemory(
                workspace.Id,
                new MemorySourceRef(MemorySourceKind.Conversation, "turn-001", snippet: "请记住：偏好中文"),
                StructuredValue.FromString("请记住：偏好中文")),
            context,
            CancellationToken.None);
        var overlay = await sut.ResolveMemoryOverlayAsync(
            new ResolveMemoryOverlay(workspace.Id),
            context,
            CancellationToken.None);

        Assert.Equal("配置工作区记忆", workspace.DisplayName);
        Assert.True(workspace.IsReadOnly);
        Assert.False(addResult.Success);
        Assert.Equal(MemoryMutationEffect.Degraded, addResult.Effect);
        Assert.Empty(extracted);
        Assert.Empty(overlay.Facts);
        Assert.Equal(MemoryMergeDecision.Ignored, overlay.MergeDecision);
    }

    [Fact]
    public async Task DefaultTianShuIdentityMemoryPlane_ShouldDisableMemoryWhenConfiguredOff()
    {
        var sut = new DefaultTianShuIdentityMemoryPlane(
            new InMemoryTianShuLocalMemoryStore(),
            memoryOptionsResolver: _ => new TianShuMemoryRuntimeOptions { Enabled = false });
        var context = CreateContext();

        var spaces = await sut.ListMemorySpacesAsync(new ListMemorySpaces(), context, CancellationToken.None);
        var overlay = await sut.ResolveMemoryOverlayAsync(new ResolveMemoryOverlay(), context, CancellationToken.None);
        var result = await sut.AddMemoryAsync(
            new AddMemory(new MemorySpaceId("memory:workspace:d/gitrepos/personal/tianshu"), "workspace.disabled", StructuredValue.FromString("ignored")),
            context,
            CancellationToken.None);

        Assert.Empty(spaces);
        Assert.Empty(overlay.Facts);
        Assert.Equal(MemoryMergeDecision.Ignored, overlay.MergeDecision);
        Assert.False(result.Success);
        Assert.Equal("memory_disabled", result.DegradedReason);
    }

    [Fact]
    public async Task DefaultTianShuIdentityMemoryPlane_ShouldWriteToDefaultWorkspaceSpace()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var sut = new DefaultTianShuIdentityMemoryPlane(store);
        var context = CreateContext();
        var workspace = Assert.Single(
            await sut.ListMemorySpacesAsync(new ListMemorySpaces(MemoryScopeKind.Workspace), context, CancellationToken.None));

        var result = await sut.AddMemoryAsync(
            new AddMemory(workspace.Id, "workspace.default_write", StructuredValue.FromString("enabled")),
            context,
            CancellationToken.None);
        var query = await sut.FilterMemoryAsync(new FilterMemory(workspace.Id, "workspace.default_write"), context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(workspace.IsReadOnly);
        var fact = Assert.Single(query.Records);
        Assert.Equal("enabled", fact.Value.StringValue);
    }

    [Fact]
    public async Task DefaultTianShuIdentityMemoryPlane_ShouldMergeAuditedLocalFactsIntoOverlay()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var sut = new DefaultTianShuIdentityMemoryPlane(store);
        var context = CreateContext();
        var workspace = Assert.Single(
            await sut.ListMemorySpacesAsync(new ListMemorySpaces(MemoryScopeKind.Workspace), context, CancellationToken.None));

        await store.UpsertFactAsync(
            workspace,
            "workspace.cwd",
            StructuredValue.FromString(@"D:\override"),
            context,
            "unit-test",
            0.9m,
            CancellationToken.None);
        await store.UpsertFactAsync(
            workspace,
            "workspace.language",
            StructuredValue.FromString("csharp"),
            context,
            "unit-test",
            1m,
            CancellationToken.None);

        var overlay = await sut.ResolveMemoryOverlayAsync(new ResolveMemoryOverlay(workspace.Id), context, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(workspace.Id, CancellationToken.None);

        var cwdFact = Assert.Single(overlay.Facts, static fact => fact.Key == "workspace.cwd");
        Assert.Equal(@"D:\override", cwdFact.Value.StringValue);
        Assert.Contains(overlay.Facts, static fact => fact.Key == "workspace.language" && fact.Value.StringValue == "csharp");
        Assert.Equal(2, audit.Count);
        Assert.All(audit, record =>
        {
            Assert.Equal("upsert_fact", record.Operation);
            Assert.Equal("local-account:semi", record.Actor);
            Assert.Equal("unit-test", record.Source);
            Assert.Equal(StructuredValueKind.String, record.ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(record.ValueHash));
            Assert.NotNull(record.Confidence);
        });
    }

    [Fact]
    public async Task DefaultTianShuIdentityMemoryPlane_ShouldFilterForgottenFactsThroughMemoryService()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var sut = new DefaultTianShuIdentityMemoryPlane(store);
        var context = CreateContext();
        var workspace = Assert.Single(
            await sut.ListMemorySpacesAsync(new ListMemorySpaces(MemoryScopeKind.Workspace), context, CancellationToken.None));

        await store.UpsertFactAsync(
            workspace,
            "workspace.language",
            StructuredValue.FromString("csharp"),
            context,
            "unit-test",
            1m,
            CancellationToken.None);
        await store.ChangeFactLifecycleAsync(
            workspace,
            memoryRecordId: null,
            key: "workspace.language",
            MemoryLifecycleStatus.Forgotten,
            context.AccountId.Value,
            "unit-test",
            context.SnapshotTime,
            reason: null,
            CancellationToken.None);

        var overlay = await sut.ResolveMemoryOverlayAsync(new ResolveMemoryOverlay(workspace.Id), context, CancellationToken.None);

        Assert.DoesNotContain(overlay.Facts, static fact => fact.Key == "workspace.language");
        Assert.Contains(overlay.Facts, static fact => fact.Key == "workspace.cwd");
    }

    [Fact]
    public async Task FileSystemTianShuLocalMemoryStore_ShouldPersistFactsAndAuditRecords()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "identity-memory-store",
            Guid.NewGuid().ToString("N"));
        var context = CreateContext();
        var space = new MemorySpace(
            new MemorySpaceId("memory:workspace:d/gitrepos/personal/TianShu"),
            MemoryScopeKind.Workspace,
            @"D:\Work\TianShu",
            "TianShu Workspace Memory");

        try
        {
            var store = new FileSystemTianShuLocalMemoryStore(root);
            await store.UpsertFactAsync(
                space,
                "workspace.preference",
                StructuredValue.FromString("cli-first"),
                context,
                "unit-test",
                0.8m,
                CancellationToken.None);

            var spacesPath = Path.Combine(root, "memory", "spaces.json");
            var factsPath = Path.Combine(root, "memory", "facts", $"{Uri.EscapeDataString(space.Id.Value)}.jsonl");
            var auditPath = Path.Combine(root, "memory", "audit", "audit-log.jsonl");

            Assert.True(File.Exists(spacesPath));
            Assert.True(File.Exists(factsPath));
            Assert.True(File.Exists(auditPath));

            await File.AppendAllTextAsync(factsPath, $"{Environment.NewLine}not-json", CancellationToken.None);
            var auditJson = await File.ReadAllTextAsync(auditPath, CancellationToken.None);

            var reloadedStore = new FileSystemTianShuLocalMemoryStore(root);
            var facts = await reloadedStore.ListFactsAsync(space, CancellationToken.None);
            var audit = await reloadedStore.ListAuditRecordsAsync(space.Id, CancellationToken.None);

            var fact = Assert.Single(facts);
            Assert.Equal("workspace.preference", fact.Key);
            Assert.Equal("cli-first", fact.Value.StringValue);
            Assert.Equal(MemoryLifecycleStatus.Active, fact.LifecycleStatus);
            Assert.Single(fact.Sources);
            Assert.Equal("unit-test", fact.Sources[0].SourceId);
            Assert.StartsWith("memory-record:", fact.Id.Value, StringComparison.Ordinal);
            var auditRecord = Assert.Single(audit);
            Assert.Equal("workspace.preference", auditRecord.Key);
            Assert.Equal("upsert_fact", auditRecord.Operation);
            Assert.Equal(StructuredValueKind.String, auditRecord.ValueKind);
            Assert.Equal(0.8m, auditRecord.Confidence);
            Assert.False(string.IsNullOrWhiteSpace(auditRecord.ValueHash));
            Assert.Contains("valueHash", auditJson, StringComparison.Ordinal);
            Assert.DoesNotContain("cli-first", auditJson, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileSystemTianShuLocalMemoryStore_ShouldPersistPhaseAStateRecords()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "identity-memory-store",
            Guid.NewGuid().ToString("N"));
        var context = CreateContext();
        var space = CreateWritableWorkspaceMemorySpace();
        var source = new MemorySourceRef(
            MemorySourceKind.ToolResult,
            "turn-phase-a2",
            path: "tests/TianShu.Execution.Runtime.Tests/IdentityMemoryPlaneTests.cs",
            snippet: "phase a2 state persistence");
        var validation = new MemoryValidationEvidence(
            "evidence:validation:a2",
            MemoryValidationEvidenceKind.TestPassed,
            "identity memory tests passed",
            source,
            context.SnapshotTime);
        var contextSignature = new MemoryContextSignature(
            memorySpaceIds: [space.Id],
            scopeKinds: [MemoryScopeKind.Workspace],
            tags: ["phase-a2"],
            sources: [source],
            lifecycleStatuses: [MemoryLifecycleStatus.Active],
            minimumConfidence: 0.7m);
        var fact = new FactMemoryRecord(
            "phase-a2.fact",
            StructuredValue.FromString("state persisted"),
            space.Id,
            0.88m,
            context.SnapshotTime,
            sources: [source],
            tags: ["phase-a2"],
            formationPath: MemoryFormationPath.ExploratoryLearning,
            contextSignature: contextSignature,
            validationEvidence: [validation],
            isCounterexample: true);
        var evidence = new MemoryEvidenceRecord(
            "evidence:a2",
            space.Id,
            source,
            MemoryEvidenceKind.TestResult,
            "safe test summary",
            MemoryScopeKind.Workspace,
            context.SnapshotTime,
            tags: ["phase-a2"],
            validationEvidence: [validation]);
        var candidate = new MemoryCandidate(
            "phase-a2.candidate",
            StructuredValue.FromString("candidate persisted"),
            space.Id,
            0.74m,
            source,
            "phase a2 test",
            "phase-a2-rule",
            MemoryFormationPath.DirectInstruction,
            [validation],
            contextSignature);
        var supersedeLink = new MemorySupersedeLink(
            new MemoryRecordId("memory-record:old-a2"),
            fact.Id,
            "phase a2 persistence",
            context.AccountId.Value,
            source,
            context.SnapshotTime);

        try
        {
            var store = new FileSystemTianShuLocalMemoryStore(root);
            await store.UpsertFactAsync(space, fact, context.AccountId.Value, "unit-test", context.SnapshotTime, CancellationToken.None);
            await store.AppendEvidenceRecordAsync(evidence, CancellationToken.None);
            await store.UpsertCandidateAsync(candidate, CancellationToken.None);
            await store.AppendSupersedeLinkAsync(space.Id, supersedeLink, CancellationToken.None);

            var escapedSpaceId = Uri.EscapeDataString(space.Id.Value);
            await File.AppendAllTextAsync(Path.Combine(root, "memory", "evidence", $"{escapedSpaceId}.jsonl"), $"{Environment.NewLine}not-json", CancellationToken.None);
            await File.AppendAllTextAsync(Path.Combine(root, "memory", "candidates", $"{escapedSpaceId}.jsonl"), $"{Environment.NewLine}not-json", CancellationToken.None);
            await File.AppendAllTextAsync(Path.Combine(root, "memory", "links", $"{escapedSpaceId}.jsonl"), $"{Environment.NewLine}not-json", CancellationToken.None);

            var reloadedStore = new FileSystemTianShuLocalMemoryStore(root);
            var facts = await reloadedStore.ListFactsAsync(space, CancellationToken.None);
            var evidenceRecords = await reloadedStore.ListEvidenceRecordsAsync(space.Id, CancellationToken.None);
            var candidates = await reloadedStore.ListCandidatesAsync(space.Id, CancellationToken.None);
            var supersedeLinks = await reloadedStore.ListSupersedeLinksAsync(space.Id, CancellationToken.None);

            var reloadedFact = Assert.Single(facts);
            Assert.Equal(MemoryFormationPath.ExploratoryLearning, reloadedFact.FormationPath);
            Assert.True(reloadedFact.IsCounterexample);
            Assert.Equal(contextSignature.MinimumConfidence, reloadedFact.ContextSignature?.MinimumConfidence);
            Assert.Single(reloadedFact.ValidationEvidence);
            Assert.Equal("evidence:a2", Assert.Single(evidenceRecords).EvidenceId);
            Assert.Equal(MemoryFormationPath.DirectInstruction, Assert.Single(candidates).FormationPath);
            Assert.Equal(fact.Id, Assert.Single(supersedeLinks).NewRecordId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void TianShuMemoryAuditRecord_ShouldRedactSecretLikeFields()
    {
        var space = CreateWritableWorkspaceMemorySpace();
        var audit = new TianShuMemoryAuditRecord(
            "upsert_fact",
            space.Id,
            "sk-memory-audit-secret",
            "user-password=super-secret",
            "Bearer sk-source-secret",
            new DateTimeOffset(2026, 4, 30, 9, 0, 0, TimeSpan.Zero),
            StructuredValue.FromString("sk-audit-value-secret"),
            0.7m,
            effect: MemoryMutationEffect.Upserted,
            reasonCodes: [MemoryRiskReasonCode.SecretLikeContent],
            reason: "password=super-secret",
            snippet: "Bearer sk-snippet-secret",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["api_key"] = "sk-metadata-secret",
                ["normal"] = "visible",
            });
        var normalAudit = new TianShuMemoryAuditRecord(
            "upsert_fact",
            space.Id,
            "workspace.preference",
            "local-account:semi",
            "unit-test",
            new DateTimeOffset(2026, 4, 30, 9, 0, 0, TimeSpan.Zero),
            StructuredValue.FromString("cli-first"),
            0.9m);
        var secretKeyAudit = new TianShuMemoryAuditRecord(
            "upsert_fact",
            space.Id,
            "workspace.api_key",
            "local-account:semi",
            "unit-test",
            new DateTimeOffset(2026, 4, 30, 9, 0, 0, TimeSpan.Zero),
            StructuredValue.FromString("value"),
            0.9m);

        Assert.Equal("[redacted]", audit.Key);
        Assert.Equal("[redacted]", audit.Actor);
        Assert.Equal("[redacted]", audit.Source);
        Assert.Equal(StructuredValueKind.String, audit.ValueKind);
        Assert.Equal(0.7m, audit.Confidence);
        Assert.Equal(MemoryMutationEffect.Upserted, audit.Effect);
        Assert.Contains(MemoryRiskReasonCode.SecretLikeContent, audit.ReasonCodes);
        Assert.Equal("[redacted]", audit.Reason);
        Assert.Equal("[redacted]", audit.Snippet);
        Assert.Contains("[redacted]", audit.Metadata.Keys);
        Assert.Contains("[redacted]", audit.Metadata.Values);
        Assert.Equal("visible", audit.Metadata["normal"]);
        Assert.False(string.IsNullOrWhiteSpace(audit.ValueHash));
        Assert.DoesNotContain("sk-audit-value-secret", audit.ValueHash, StringComparison.Ordinal);
        Assert.Equal("workspace.preference", normalAudit.Key);
        Assert.Equal("local-account:semi", normalAudit.Actor);
        Assert.Equal("unit-test", normalAudit.Source);
        Assert.Equal(StructuredValueKind.String, normalAudit.ValueKind);
        Assert.Equal(0.9m, normalAudit.Confidence);
        Assert.False(string.IsNullOrWhiteSpace(normalAudit.ValueHash));
        Assert.Equal("[redacted]", secretKeyAudit.Key);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldEmitUnifiedAuditForPhaseA3Operations()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var secretSource = new MemorySourceRef(
            MemorySourceKind.Conversation,
            "thread-memory-phase-a3",
            role: "user",
            snippet: "Bearer sk-phase-a3-snippet",
            capturedAt: operationContext.Timestamp,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["api_key"] = "sk-phase-a3-metadata",
                ["safe"] = "kept",
            });

        var addResult = await service.AddAsync(
            new AddMemory(space.Id, "workspace.preference", StructuredValue.FromString("cli-first"), 0.9m, secretSource),
            operationContext,
            CancellationToken.None);
        var recordId = Assert.NotNull(addResult.RecordId);

        _ = await service.ExtractAsync(
            new ExtractMemory(space.Id, secretSource, StructuredValue.FromString("普通内容")),
            operationContext,
            CancellationToken.None);
        _ = await service.ImportAsync(new ImportMemory(space.Id, secretSource), operationContext, CancellationToken.None);
        _ = await service.FilterAsync(
            new FilterMemory(space.Id, Key: "workspace.api_key"),
            operationContext,
            CancellationToken.None);
        _ = await service.RecordFeedbackAsync(
            new RecordMemoryFeedback(recordId, MemoryMergeDecision.NeedsReview, "password=不要落盘", secretSource),
            operationContext,
            CancellationToken.None);
        _ = await service.RecordCitationAsync(
            new RecordMemoryCitation(
                new MemoryCitation(
                [
                    new MemoryCitationEntry(
                        recordId,
                        space.Id,
                        "workspace.preference",
                        secretSource,
                        "Bearer sk-citation-note")
                ])),
            operationContext,
            CancellationToken.None);
        _ = await service.ForgetAsync(
            new ForgetMemory(recordId, space.Id, "workspace.preference"),
            operationContext,
            CancellationToken.None);
        _ = await service.DeleteAsync(
            new DeleteMemory(recordId, space.Id, "workspace.preference", "password=delete-secret"),
            operationContext,
            CancellationToken.None);

        var supersedeAudit = MemoryAuditRecords.Supersede(
            new SupersedeMemory(
                recordId,
                space.Id,
                "workspace.preference.corrected",
                StructuredValue.FromString("中文优先"),
                "Bearer sk-supersede-reason",
                0.95m,
                secretSource),
            operationContext);
        await store.AppendAuditRecordAsync(supersedeAudit, CancellationToken.None);

        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        Assert.Contains(audit, static record =>
            record.Operation == "upsert_fact"
            && record.Effect == MemoryMutationEffect.Upserted
            && record.ValueKind == StructuredValueKind.String
            && !string.IsNullOrWhiteSpace(record.ValueHash)
            && record.Key == "workspace.preference");
        Assert.Contains(audit, static record =>
            record.Operation == "extract_memory"
            && record.Snippet == "[redacted]"
            && record.Metadata.ContainsKey("[redacted]")
            && record.Metadata.Values.Contains("[redacted]")
            && record.Metadata["safe"] == "kept");
        Assert.Contains(audit, static record =>
            record.Operation == "import_memory"
            && record.ReasonCodes.Contains(MemoryRiskReasonCode.ProviderCapabilityMissing));
        Assert.Contains(audit, static record =>
            record.Operation == "filter_memory"
            && record.Key == "[redacted]");
        Assert.Contains(audit, static record =>
            record.Operation == "record_feedback"
            && record.Reason == "[redacted]");
        Assert.Contains(audit, static record =>
            record.Operation == "record_citation"
            && record.Snippet == "[redacted]"
            && record.Reason == "[redacted]"
            && record.Effect == MemoryMutationEffect.LifecycleChanged);
        Assert.Contains(audit, static record =>
            record.Operation == "forget_fact"
            && record.Effect == MemoryMutationEffect.LifecycleChanged);
        Assert.Contains(audit, static record =>
            record.Operation == "delete_fact"
            && record.Effect == MemoryMutationEffect.SoftDeleted
            && record.Reason == "[redacted]");
        Assert.Contains(audit, static record =>
            record.Operation == "supersede_memory"
            && record.Effect == MemoryMutationEffect.Superseded
            && record.Reason == "[redacted]"
            && record.ReasonCodes.Contains(MemoryRiskReasonCode.ConflictsWithActiveFact));
        Assert.DoesNotContain(audit, static record =>
            string.Equals(record.Reason, "Bearer sk-supersede-reason", StringComparison.Ordinal)
            || string.Equals(record.Snippet, "Bearer sk-phase-a3-snippet", StringComparison.Ordinal)
            || record.Metadata.Values.Contains("sk-phase-a3-metadata"));
    }

    [Fact]
    public void MemoryPolicies_ShouldEvaluateReadIngestionAndPromotionBoundaries()
    {
        var policy = new MemoryPolicyEngine();
        var space = CreateWritableWorkspaceMemorySpace();
        var context = CreateMemoryOperationContext();
        var provider = new TianShuLocalMemoryProvider(new InMemoryTianShuLocalMemoryStore(), [space]);
        var source = new MemorySourceRef(
            MemorySourceKind.Conversation,
            "thread-policy",
            role: "user",
            snippet: "以后默认使用中文回复",
            capturedAt: context.Timestamp);
        var lowRiskCandidate = new MemoryCandidate(
            "preference.default",
            StructuredValue.FromString("中文优先"),
            space.Id,
            0.91m,
            source,
            ruleId: "rule.explicit-default");
        var promotionContext = new MemoryPromotionPolicyContext(
            context,
            provider.Descriptor,
            SourceScopeKind: MemoryScopeKind.Workspace,
            TargetScopeKind: MemoryScopeKind.Workspace,
            SourceStrength: MemorySourceStrength.Strong);

        var activeFact = new FactMemoryRecord(
            "preference.default",
            StructuredValue.FromString("中文优先"),
            space.Id,
            recordedAt: context.Timestamp);
        var pendingFact = new FactMemoryRecord(
            "preference.pending",
            StructuredValue.FromString("待确认"),
            space.Id,
            lifecycleStatus: MemoryLifecycleStatus.PendingReview,
            recordedAt: context.Timestamp);
        var lowRiskDecision = policy.Promotion.EvaluateCandidate(lowRiskCandidate, promotionContext);
        var ingestionDecision = policy.Ingestion.EvaluateEvidence(
            new MemoryEvidenceRecord(
                "evidence:policy:success",
                space.Id,
                source,
                MemoryEvidenceKind.UserCorrection,
                "用户确认中文优先",
                MemoryScopeKind.Workspace,
                context.Timestamp),
            new MemoryIngestionPolicyContext(
                context,
                provider.Descriptor,
                SourceScopeKind: MemoryScopeKind.Workspace,
                TargetScopeKind: MemoryScopeKind.Workspace,
                SourceStrength: MemorySourceStrength.Strong));

        Assert.True(policy.Supports(provider.Descriptor, MemoryProviderCapability.Add));
        Assert.True(policy.CanRead(activeFact));
        Assert.False(policy.CanRead(pendingFact));
        Assert.Equal(MemoryPromotionDecisionKind.AutoPromote, lowRiskDecision.Kind);
        Assert.Equal(MemoryLifecycleStatus.Active, lowRiskDecision.TargetLifecycleStatus);
        Assert.Empty(lowRiskDecision.ReasonCodes);
        Assert.Equal(MemoryIngestionDecisionKind.AcceptEvidence, ingestionDecision.Kind);
        Assert.Empty(ingestionDecision.ReasonCodes);
    }

    [Fact]
    public void MemoryPromotionPolicy_ShouldAutoPromoteExplicitUserInstructionsButGateHardRisks()
    {
        var space = CreateWritableWorkspaceMemorySpace();
        var userSpace = new MemorySpace(
            new MemorySpaceId("memory:user:semi"),
            MemoryScopeKind.User,
            "semi",
            "User Memory");
        var context = CreateMemoryOperationContext();
        var provider = new TianShuLocalMemoryProvider(new InMemoryTianShuLocalMemoryStore(), [space, userSpace]);
        var policy = new MemoryPromotionPolicy();
        var source = new MemorySourceRef(
            MemorySourceKind.Conversation,
            "thread-risk",
            role: "user",
            snippet: "以后默认使用中文回复",
            capturedAt: context.Timestamp);
        var crossScopeCandidate = new MemoryCandidate(
            "preference.default",
            StructuredValue.FromString("中文优先"),
            userSpace.Id,
            0.93m,
            source,
            ruleId: "rule.explicit-default");
        var existing = new FactMemoryRecord(
            "preference.default",
            StructuredValue.FromString("英文优先"),
            space.Id,
            0.9m,
            context.Timestamp);
        var conflictingCandidate = new MemoryCandidate(
            "preference.default",
            StructuredValue.FromString("中文优先"),
            space.Id,
            0.93m,
            source,
            ruleId: "rule.explicit-default");

        var crossScopeDecision = policy.EvaluateCandidate(
            crossScopeCandidate,
            new MemoryPromotionPolicyContext(
                context,
                provider.Descriptor,
                SourceScopeKind: MemoryScopeKind.Session,
                TargetScopeKind: MemoryScopeKind.User,
                SourceStrength: MemorySourceStrength.Strong));
        var weakCrossScopeDecision = policy.EvaluateCandidate(
            crossScopeCandidate,
            new MemoryPromotionPolicyContext(
                context,
                provider.Descriptor,
                SourceScopeKind: MemoryScopeKind.Session,
                TargetScopeKind: MemoryScopeKind.User,
                SourceStrength: MemorySourceStrength.Weak));
        var conflictDecision = policy.EvaluateCandidate(
            conflictingCandidate,
            new MemoryPromotionPolicyContext(
                context,
                provider.Descriptor,
                [existing],
                SourceScopeKind: MemoryScopeKind.Workspace,
                TargetScopeKind: MemoryScopeKind.Workspace,
                SourceStrength: MemorySourceStrength.Strong));
        var secretDecision = policy.EvaluateCandidate(
            new MemoryCandidate(
                "workspace.api_key",
                StructuredValue.FromString("sk-secret"),
                space.Id,
                0.99m,
                source),
            new MemoryPromotionPolicyContext(
                context,
                provider.Descriptor,
                SourceScopeKind: MemoryScopeKind.Workspace,
                TargetScopeKind: MemoryScopeKind.Workspace,
                SourceStrength: MemorySourceStrength.Strong));

        Assert.Equal(MemoryPromotionDecisionKind.AutoPromote, crossScopeDecision.Kind);
        Assert.Equal(MemoryLifecycleStatus.Active, crossScopeDecision.TargetLifecycleStatus);
        Assert.Empty(crossScopeDecision.ReasonCodes);
        Assert.Equal(MemoryPromotionDecisionKind.NeedsReview, weakCrossScopeDecision.Kind);
        Assert.Contains(MemoryRiskReasonCode.WeakSource, weakCrossScopeDecision.ReasonCodes);
        Assert.Equal(MemoryPromotionDecisionKind.SupersedeProposal, conflictDecision.Kind);
        Assert.Contains(MemoryRiskReasonCode.ConflictsWithActiveFact, conflictDecision.ReasonCodes);
        Assert.Equal(MemoryPromotionDecisionKind.Reject, secretDecision.Kind);
        Assert.Contains(MemoryRiskReasonCode.SecretLikeContent, secretDecision.ReasonCodes);
    }

    [Fact]
    public void MemoryIngestionPolicy_ShouldRejectSecretsAndRetainSingleFailuresAsEvidence()
    {
        var space = CreateWritableWorkspaceMemorySpace();
        var context = CreateMemoryOperationContext();
        var provider = new TianShuLocalMemoryProvider(new InMemoryTianShuLocalMemoryStore(), [space]);
        var policy = new MemoryIngestionPolicy();
        var source = new MemorySourceRef(
            MemorySourceKind.ToolResult,
            "tool:shell",
            role: "tool",
            capturedAt: context.Timestamp);
        var secretEvidence = new MemoryEvidenceRecord(
            "evidence:secret",
            space.Id,
            source,
            MemoryEvidenceKind.ExternalFact,
            "password=super-secret",
            MemoryScopeKind.Workspace,
            context.Timestamp);
        var singleFailureEvidence = new MemoryEvidenceRecord(
            "evidence:failure",
            space.Id,
            source,
            MemoryEvidenceKind.CommandFailure,
            "dotnet build failed once",
            MemoryScopeKind.Workspace,
            context.Timestamp);

        var secretDecision = policy.EvaluateEvidence(
            secretEvidence,
            new MemoryIngestionPolicyContext(
                context,
                provider.Descriptor,
                SourceScopeKind: MemoryScopeKind.Workspace,
                TargetScopeKind: MemoryScopeKind.Workspace,
                SourceStrength: MemorySourceStrength.Strong));
        var singleFailureDecision = policy.EvaluateEvidence(
            singleFailureEvidence,
            new MemoryIngestionPolicyContext(
                context,
                provider.Descriptor,
                SourceScopeKind: MemoryScopeKind.Workspace,
                TargetScopeKind: MemoryScopeKind.Workspace,
                SourceStrength: MemorySourceStrength.Strong));
        var providerMissingDecision = policy.EvaluateEvidence(
            singleFailureEvidence,
            new MemoryIngestionPolicyContext(
                context,
                provider.Descriptor with { Features = MemoryProviderFeature.None },
                SourceScopeKind: MemoryScopeKind.Workspace,
                TargetScopeKind: MemoryScopeKind.Workspace,
                SourceStrength: MemorySourceStrength.Strong));

        Assert.Equal(MemoryIngestionDecisionKind.Reject, secretDecision.Kind);
        Assert.Contains(MemoryRiskReasonCode.SecretLikeContent, secretDecision.ReasonCodes);
        Assert.Equal(MemoryIngestionDecisionKind.AcceptEvidence, singleFailureDecision.Kind);
        Assert.Contains(MemoryRiskReasonCode.SingleUnverifiedFailure, singleFailureDecision.ReasonCodes);
        Assert.Equal(MemoryLifecycleStatus.PendingReview, singleFailureDecision.TargetLifecycleStatus);
        Assert.Equal(MemoryIngestionDecisionKind.Reject, providerMissingDecision.Kind);
        Assert.Contains(MemoryRiskReasonCode.ProviderCapabilityMissing, providerMissingDecision.ReasonCodes);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldAddFilterForgetAndDeleteSoft()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var source = new MemorySourceRef(
            MemorySourceKind.Conversation,
            "thread-delete-soft",
            role: "user",
            snippet: "以后默认使用 CLI 优先",
            capturedAt: operationContext.Timestamp);

        var addResult = await service.AddAsync(
            new AddMemory(space.Id, "workspace.preference", StructuredValue.FromString("cli-first"), 0.9m, source),
            operationContext,
            CancellationToken.None);
        var activeFacts = await service.FilterAsync(new FilterMemory(space.Id), operationContext, CancellationToken.None);

        var forgetResult = await service.ForgetAsync(
            new ForgetMemory(addResult.RecordId, space.Id, "workspace.preference"),
            operationContext,
            CancellationToken.None);
        var visibleAfterForget = await service.FilterAsync(new FilterMemory(space.Id), operationContext, CancellationToken.None);
        var forgottenFacts = await service.FilterAsync(
            new FilterMemory(space.Id, LifecycleStatus: MemoryLifecycleStatus.Forgotten),
            operationContext,
            CancellationToken.None);

        var deleteResult = await service.DeleteAsync(
            new DeleteMemory(addResult.RecordId, space.Id, "workspace.preference"),
            operationContext,
            CancellationToken.None);
        var deletedFacts = await service.FilterAsync(
            new FilterMemory(space.Id, LifecycleStatus: MemoryLifecycleStatus.Deleted),
            operationContext,
            CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        Assert.True(addResult.Success);
        Assert.Equal(MemoryMutationEffect.Upserted, addResult.Effect);
        var activeFact = Assert.Single(activeFacts.Records);
        Assert.Equal("cli-first", activeFact.Value.StringValue);
        Assert.Single(activeFact.Sources);
        Assert.True(forgetResult.Success);
        Assert.Equal(MemoryLifecycleStatus.Forgotten, forgetResult.LifecycleStatus);
        Assert.Equal(MemoryMutationEffect.LifecycleChanged, forgetResult.Effect);
        Assert.Empty(visibleAfterForget.Records);
        var forgottenFact = Assert.Single(forgottenFacts.Records);
        Assert.Equal(source.SourceId, forgottenFact.Sources.Single().SourceId);
        Assert.True(deleteResult.Success);
        Assert.Equal(MemoryLifecycleStatus.Deleted, deleteResult.LifecycleStatus);
        Assert.Equal(MemoryMutationEffect.SoftDeleted, deleteResult.Effect);
        var deletedFact = Assert.Single(deletedFacts.Records);
        Assert.Equal(addResult.RecordId, deletedFact.Id);
        Assert.Equal(source.SourceId, deletedFact.Sources.Single().SourceId);
        Assert.Contains(audit, static record => record.Operation == "upsert_fact");
        Assert.Contains(audit, static record => record.Operation == "forget_fact");
        Assert.Contains(audit, static record => record.Operation == "delete_fact");
        Assert.Contains(audit, static record => record.Operation == "filter_memory");
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldLazyCreateMemorySpaceOnFirstAdd()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var service = CreateMemoryService(store);
        var operationContext = CreateMemoryOperationContext();
        var lazySpaceId = new MemorySpaceId("memory:workspace:d/gitrepos/personal/TianShu-lazy");

        var addResult = await service.AddAsync(
            new AddMemory(lazySpaceId, "workspace.preference", StructuredValue.FromString("lazy-create"), 0.9m),
            operationContext,
            CancellationToken.None);
        var spaces = await service.ListSpacesAsync(new ListMemorySpaces(MemoryScopeKind.Workspace), CancellationToken.None);
        var facts = await service.FilterAsync(new FilterMemory(lazySpaceId), operationContext, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(lazySpaceId, CancellationToken.None);

        Assert.True(addResult.Success);
        var space = Assert.Single(spaces, item => string.Equals(item.Id.Value, lazySpaceId.Value, StringComparison.Ordinal));
        Assert.Equal(MemoryScopeKind.Workspace, space.ScopeKind);
        Assert.Equal("d/gitrepos/personal/TianShu-lazy", space.ScopeKey);
        var fact = Assert.Single(facts.Records);
        Assert.Equal("workspace.preference", fact.Key);
        Assert.Equal("lazy-create", fact.Value.StringValue);
        Assert.Contains(audit, static record => record.Operation == "upsert_fact");
        Assert.Contains(audit, static record => record.Operation == "filter_memory");
    }

    [Fact]
    public async Task FileSystemMemoryStore_ShouldReloadLazyCreatedSpaceAndFact()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tianshu-memory-{Guid.NewGuid():N}");
        try
        {
            var operationContext = CreateMemoryOperationContext();
            var lazySpaceId = new MemorySpaceId("memory:workspace:d/gitrepos/personal/TianShu-lazy-file");
            var store = new FileSystemTianShuLocalMemoryStore(root);
            var service = CreateMemoryService(store);

            var addResult = await service.AddAsync(
                new AddMemory(lazySpaceId, "workspace.language", StructuredValue.FromString("csharp"), 0.95m),
                operationContext,
                CancellationToken.None);

            var reloadedStore = new FileSystemTianShuLocalMemoryStore(root);
            var reloadedService = CreateMemoryService(reloadedStore);
            var spaces = await reloadedService.ListSpacesAsync(new ListMemorySpaces(MemoryScopeKind.Workspace), CancellationToken.None);
            var facts = await reloadedService.FilterAsync(new FilterMemory(lazySpaceId), operationContext, CancellationToken.None);

            Assert.True(addResult.Success);
            Assert.True(File.Exists(Path.Combine(root, "memory", "spaces.json")));
            Assert.Single(spaces, item => string.Equals(item.Id.Value, lazySpaceId.Value, StringComparison.Ordinal));
            var fact = Assert.Single(facts.Records);
            Assert.Equal("workspace.language", fact.Key);
            Assert.Equal("csharp", fact.Value.StringValue);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldResolveOverlayWithLifecycleFilteringAndLocalOverride()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var defaultFact = new FactMemoryRecord(
            "workspace.preference",
            StructuredValue.FromString("default"),
            space.Id,
            recordedAt: operationContext.Timestamp);
        var activeOverride = new FactMemoryRecord(
            "workspace.preference",
            StructuredValue.FromString("local"),
            space.Id,
            recordedAt: operationContext.Timestamp);
        var pendingFact = new FactMemoryRecord(
            "workspace.pending",
            StructuredValue.FromString("candidate"),
            space.Id,
            recordedAt: operationContext.Timestamp,
            lifecycleStatus: MemoryLifecycleStatus.PendingReview);

        await store.UpsertFactAsync(space, activeOverride, operationContext.ActorId, "unit-test", operationContext.Timestamp, CancellationToken.None);
        await store.UpsertFactAsync(space, pendingFact, operationContext.ActorId, "unit-test", operationContext.Timestamp, CancellationToken.None);

        var overlay = await service.ResolveOverlayAsync(
            new ResolveMemoryOverlay(space.Id),
            new HabitProfile(PreferredTools: ["shell"], PreferredVerbosity: "high"),
            [defaultFact],
            operationContext,
            CancellationToken.None);

        var fact = Assert.Single(overlay.Facts);
        Assert.Equal("workspace.preference", fact.Key);
        Assert.Equal("local", fact.Value.StringValue);
        Assert.Equal(MemoryMergeDecision.Applied, overlay.MergeDecision);
        Assert.Equal("high", overlay.HabitProfile?.PreferredVerbosity);
    }

    [Fact]
    public void MemoryOverlayResolver_ShouldMergeSameKeyByScopePriority()
    {
        var resolver = new MemoryOverlayResolver();
        var timestamp = new DateTimeOffset(2026, 4, 30, 9, 0, 0, TimeSpan.Zero);
        var workspaceDefault = CreateFact(
            "preference.language",
            "default-workspace",
            new MemorySpaceId("memory:workspace:d/gitrepos/personal/TianShu"),
            timestamp);
        var userFact = CreateFact(
            "preference.language",
            "user",
            new MemorySpaceId("memory:user:semi"),
            timestamp);
        var workspaceFact = CreateFact(
            "preference.language",
            "workspace",
            new MemorySpaceId("memory:workspace:d/gitrepos/personal/TianShu"),
            timestamp);
        var sessionFact = CreateFact(
            "preference.language",
            "session",
            new MemorySpaceId("memory:session:thread-001"),
            timestamp);

        var sameScopeOverlay = resolver.Resolve([workspaceDefault], [workspaceFact]);
        var prioritizedOverlay = resolver.Resolve([workspaceDefault], [userFact, workspaceFact, sessionFact]);

        var sameScopeFact = Assert.Single(sameScopeOverlay.Facts);
        Assert.Equal("workspace", sameScopeFact.Value.StringValue);
        var prioritizedFact = Assert.Single(prioritizedOverlay.Facts);
        Assert.Equal("session", prioritizedFact.Value.StringValue);
    }

    [Fact]
    public void MemoryOverlayResolver_ShouldPrioritizeCurrentWorkspaceAndGateTransferCandidates()
    {
        var resolver = new MemoryOverlayResolver();
        var timestamp = new DateTimeOffset(2026, 5, 19, 9, 0, 0, TimeSpan.Zero);
        var currentWorkspace = new MemorySpaceId("memory:workspace:d/gitrepos/personal/TianShu");
        var otherWorkspace = new MemorySpaceId("memory:workspace:d/gitrepos/work/legacy-wpf");
        var profile = MemoryOverlayResolutionProfile.Create([currentWorkspace], "WPF HTTP", maxFacts: null);

        var current = CreateFact("workspace.preference", "tianshu current rule", currentWorkspace, timestamp);
        var transferable = CreateFact("wpf.http.client", "WPF HTTP tester pattern", otherWorkspace, timestamp);
        var unrelated = CreateFact("legacy.sql.rule", "SQL Server archive", otherWorkspace, timestamp);
        var global = CreateFact("user.language", "中文优先", new MemorySpaceId("memory:user:semi"), timestamp);

        var overlay = resolver.Resolve([], [unrelated, transferable, global, current], profile: profile);

        Assert.Equal(
            ["workspace.preference", "user.language", "wpf.http.client"],
            overlay.Facts.Select(static fact => fact.Key).ToArray());
        Assert.DoesNotContain(overlay.Facts, static fact => fact.Key == "legacy.sql.rule");
    }

    [Fact]
    public void MemoryOverlayResolver_ShouldReturnCitationAndSelectionExplanations()
    {
        var resolver = new MemoryOverlayResolver();
        var timestamp = DateTimeOffset.UtcNow;
        var currentWorkspace = new MemorySpaceId("memory:workspace:d/gitrepos/personal/TianShu");
        var source = new MemorySourceRef(
            MemorySourceKind.Conversation,
            "thread-overlay-explain",
            snippet: "WPF HTTP tester pattern",
            capturedAt: timestamp);
        var fact = new FactMemoryRecord(
            "wpf.http.client",
            StructuredValue.FromString("WPF HTTP tester pattern"),
            currentWorkspace,
            0.93m,
            timestamp,
            sources: [source],
            tags: ["wpf", "http"],
            usageCount: 2);

        var overlay = resolver.Resolve(
            [],
            [fact],
            profile: MemoryOverlayResolutionProfile.Create([currentWorkspace], "HTTP", maxFacts: 4));

        var selected = Assert.Single(overlay.Facts);
        Assert.Equal(fact.Id, selected.Id);
        Assert.NotNull(overlay.Citation);
        Assert.Equal(fact.Id, Assert.Single(overlay.Citation!.Entries).MemoryRecordId);
        var explanation = Assert.Single(overlay.Explanations);
        Assert.Equal(fact.Id, explanation.MemoryRecordId);
        Assert.Contains("scope-match", explanation.Factors);
        Assert.Contains("keyword-match", explanation.Factors);
        Assert.Equal("keyword", explanation.RetrievalMode);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldDegradeSemanticSearchToKeywordWhenSemanticProviderMissing()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        await service.AddAsync(
            new AddMemory(space.Id, "wpf.http.client", StructuredValue.FromString("WPF GET POST tester"), 0.9m),
            operationContext,
            CancellationToken.None);

        var result = await service.FilterAsync(
            new FilterMemory(
                space.Id,
                QueryText: "HTTP POST",
                SearchMode: MemorySearchMode.Semantic),
            operationContext,
            CancellationToken.None);

        var fact = Assert.Single(result.Records);
        Assert.Equal("wpf.http.client", fact.Key);
        Assert.Equal(MemorySearchMode.Keyword, result.EffectiveSearchMode);
        Assert.Contains("semantic_search_degraded:keyword", result.DegradedProviders);
    }

    [Fact]
    public async Task DefaultTianShuIdentityMemoryPlane_ShouldExposeConfiguredExternalSemanticProvider()
    {
        var sut = new DefaultTianShuIdentityMemoryPlane(
            externalMemoryProviders:
            [
                new TianShuExternalMemoryProviderOptions
                {
                    ProviderId = "local_vector",
                    Kind = "qdrant",
                    DisplayName = "Local Vector Memory",
                    Host = "203.0.113.10",
                    Port = 8231,
                    GrpcPort = 50059,
                    Capabilities = ["semantic-search", "embedding-indexing", "llm-extraction"],
                }
            ]);
        var context = CreateContext();

        var providers = await sut.ListMemoryProvidersAsync(new ListMemoryProviders(), context, CancellationToken.None);

        var provider = Assert.Single(providers, static item => item.ProviderId == "local_vector");
        Assert.Equal("Local Vector Memory", provider.DisplayName);
        Assert.True(provider.RequiresNetwork);
        Assert.Equal(MemoryProviderTrustLevel.External, provider.TrustLevel);
        Assert.True(provider.Capabilities.HasFlag(MemoryProviderCapability.SemanticSearch));
        Assert.True(provider.Capabilities.HasFlag(MemoryProviderCapability.EmbeddingIndexing));
        Assert.True(provider.Capabilities.HasFlag(MemoryProviderCapability.LlmExtraction));
        Assert.False(provider.Capabilities.HasFlag(MemoryProviderCapability.Add));
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldReportUnreachableSemanticProviderAndFallbackToKeyword()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var policy = new MemoryPolicyEngine();
        var localProvider = new TianShuLocalMemoryProvider(store, [space], policy);
        var semanticProvider = new TianShuExternalSemanticMemoryProvider(
            new TianShuExternalMemoryProviderOptions
            {
                ProviderId = "local_vector",
                Kind = "qdrant",
                Host = "127.0.0.1",
                Port = 1,
                Capabilities = ["semantic-search", "embedding-indexing"],
                ConnectTimeout = TimeSpan.FromMilliseconds(25),
            },
            [space]);
        var service = new DefaultMemoryService(
            new MemoryProviderRegistry([localProvider, semanticProvider]),
            new MemoryOverlayResolver(policy),
            auditSink: new TianShuLocalMemoryAuditSink(store),
            policy: policy);
        var operationContext = CreateMemoryOperationContext();
        await service.AddAsync(
            new AddMemory(space.Id, "wpf.http.client", StructuredValue.FromString("WPF GET POST tester"), 0.9m),
            operationContext,
            CancellationToken.None);

        var result = await service.FilterAsync(
            new FilterMemory(
                space.Id,
                QueryText: "HTTP POST",
                SearchMode: MemorySearchMode.Semantic),
            operationContext,
            CancellationToken.None);

        var fact = Assert.Single(result.Records);
        Assert.Equal("wpf.http.client", fact.Key);
        Assert.Equal(MemorySearchMode.Keyword, result.EffectiveSearchMode);
        Assert.Contains("memory_provider_unreachable:local_vector", result.DegradedProviders);
        Assert.Contains("semantic_search_degraded:keyword", result.DegradedProviders);
    }

    [Fact]
    public async Task ExternalSemanticProvider_ShouldKeepLlmExtractionAsReadOnlyEnhancement()
    {
        var space = CreateWritableWorkspaceMemorySpace();
        var provider = new TianShuExternalSemanticMemoryProvider(
            new TianShuExternalMemoryProviderOptions
            {
                ProviderId = "local_vector",
                Kind = "qdrant",
                Host = "203.0.113.10",
                Port = 8231,
                GrpcPort = 50059,
                Capabilities = ["semantic-search", "embedding-indexing", "llm-extraction", "read-only"],
            },
            [space]);

        var addResult = await provider.AddAsync(
            new AddMemory(space.Id, "preference.language", StructuredValue.FromString("中文"), 0.8m),
            CreateMemoryOperationContext(),
            CancellationToken.None);

        Assert.True(provider.Descriptor.Capabilities.HasFlag(MemoryProviderCapability.LlmExtraction));
        Assert.True(provider.Descriptor.Capabilities.HasFlag(MemoryProviderCapability.ReadOnlyAccess));
        Assert.False(provider.Descriptor.Capabilities.HasFlag(MemoryProviderCapability.Add));
        Assert.False(addResult.Success);
        Assert.Equal(MemoryMutationEffect.Degraded, addResult.Effect);
        Assert.Equal(MemoryProviderCapability.Add, addResult.UnsupportedCapability);
    }

    [Fact]
    public async Task ExternalSemanticProvider_ShouldQueryHttpAdapterAndRedactExternalIds()
    {
        var space = CreateWritableWorkspaceMemorySpace();
        await using var server = await LoopbackMemoryProviderServer.StartAsync(
            (path, _) =>
            {
                return new MemoryQueryResult(
                    [
                        new FactMemoryRecord(
                            "workspace.preference",
                            StructuredValue.FromString("prefer TianShu native memory"),
                            space.Id,
                            0.91m,
                            id: new MemoryRecordId("qdrant:collection-a:point-42"),
                            sources:
                            [
                                new MemorySourceRef(
                                    MemorySourceKind.ExternalProvider,
                                    "qdrant-point-42",
                                    metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                                    {
                                        ["database_id"] = "collection-a",
                                        ["embedding_id"] = "vector-42",
                                        ["safe_note"] = "semantic match",
                                    }),
                            ])
                    ],
                    citation: new MemoryCitation(
                    [
                        new MemoryCitationEntry(
                            new MemoryRecordId("qdrant:collection-a:point-42"),
                            space.Id,
                            "workspace.preference",
                            new MemorySourceRef(
                                MemorySourceKind.ExternalProvider,
                                "qdrant-point-42",
                                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                                {
                                    ["database_id"] = "collection-a",
                                    ["safe_note"] = "citation source",
                                }),
                            "semantic match used")
                    ]),
                    explanations:
                    [
                        new MemoryOverlayExplanation(
                            new MemoryRecordId("qdrant:collection-a:point-42"),
                            space.Id,
                            "workspace.preference",
                            Rank: 1,
                            Score: 0.87m,
                            Factors: ["scope:workspace", "embedding_id=vector-42", "semantic:0.87"],
                            RetrievalMode: "semantic")
                    ],
                    EffectiveSearchMode: MemorySearchMode.Semantic);
            });
        var provider = new TianShuExternalSemanticMemoryProvider(
            new TianShuExternalMemoryProviderOptions
            {
                ProviderId = "local_vector",
                Kind = "tianshu-http",
                Host = "127.0.0.1",
                Port = server.Port,
                Capabilities = ["semantic-search", "read-only"],
            },
            [space]);

        var result = await provider.FilterAsync(
            new FilterMemory(space.Id, QueryText: "native memory", SearchMode: MemorySearchMode.Semantic),
            CreateMemoryOperationContext(),
            CancellationToken.None);

        Assert.Contains("/v1/memory/filter", server.RequestPaths);
        Assert.True(result.DegradedProviders.Count == 0, string.Join(", ", result.DegradedProviders));
        var fact = Assert.Single(result.Records);
        Assert.Equal("workspace.preference", fact.Key);
        Assert.StartsWith("memory-record:external:local_vector:", fact.Id.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("qdrant", fact.Id.Value, StringComparison.OrdinalIgnoreCase);
        var source = Assert.Single(fact.Sources);
        Assert.Equal("local_vector", source.SourceId);
        Assert.DoesNotContain(source.Metadata.Keys, key => key.Contains("database", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(source.Metadata.Keys, key => key.Contains("embedding", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("semantic match", source.Metadata["safe_note"]);
        Assert.NotNull(result.Citation);
        var citation = result.Citation!;
        var citationEntry = Assert.Single(citation.Entries);
        Assert.Equal(fact.Id, citationEntry.MemoryRecordId);
        Assert.NotNull(citationEntry.Source);
        Assert.Equal("local_vector", citationEntry.Source!.SourceId);
        Assert.Equal("citation source", citationEntry.Source.Metadata["safe_note"]);
        Assert.DoesNotContain(citationEntry.Source.Metadata.Keys, key => key.Contains("database", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("semantic match used", citationEntry.Note);
        var explanation = Assert.Single(result.Explanations);
        Assert.Equal(fact.Id, explanation.MemoryRecordId);
        Assert.Equal(1, explanation.Rank);
        Assert.Equal(0.87m, explanation.Score);
        Assert.Equal("semantic", explanation.RetrievalMode);
        Assert.Contains("scope:workspace", explanation.Factors);
        Assert.Contains("semantic:0.87", explanation.Factors);
        Assert.DoesNotContain(explanation.Factors, factor => factor.Contains("embedding", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExternalSemanticProvider_ShouldUseDiscoverySpacesAndSecretReferences()
    {
        var authEnv = $"TIANSHU_MEMORY_AUTH_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(authEnv, "Bearer unit-test-memory-secret");
        try
        {
            await using var server = await LoopbackMemoryProviderServer.StartAsync(
                (path, _) =>
                {
                    Assert.Equal("/v1/memory/provider", path);
                    return new
                    {
                        schemaVersion = "1.0",
                        health = "healthy",
                        capabilities = new[] { "semantic-search" },
                        spaces = new[]
                        {
                            new
                            {
                                id = "memory:workspace:remote",
                                scope = "workspace",
                                scopeKey = "remote-workspace",
                                displayName = "Remote Workspace Memory",
                                isReadOnly = true,
                            },
                        },
                        collections = new[]
                        {
                            new
                            {
                                name = "workspace_remote",
                                displayName = "Workspace Remote",
                            },
                        },
                    };
                });
            var provider = new TianShuExternalSemanticMemoryProvider(
                new TianShuExternalMemoryProviderOptions
                {
                    ProviderId = "remote_vector",
                    Kind = "tianshu-http",
                    Host = "127.0.0.1",
                    Port = server.Port,
                    GrpcPort = 1,
                    AuthorizationEnvironmentVariable = authEnv,
                    Capabilities = ["semantic-search", "read-only"],
                },
                [CreateWritableWorkspaceMemorySpace()]);

            var spaces = await provider.ListSpacesAsync(null, CancellationToken.None);

            var space = Assert.Single(spaces);
            Assert.Equal("memory:workspace:remote", space.Id.Value);
            Assert.Equal(MemoryScopeKind.Workspace, space.ScopeKind);
            Assert.Equal("remote-workspace", space.ScopeKey);
            Assert.Equal("Remote Workspace Memory", space.DisplayName);
            Assert.True(space.IsReadOnly);
            Assert.True(provider.Descriptor.RequiresCredentials);
            var headers = Assert.Single(server.RequestHeaders);
            Assert.Equal("Bearer unit-test-memory-secret", headers["Authorization"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(authEnv, null);
        }
    }

    [Fact]
    public async Task ExternalSemanticProvider_ShouldRejectUnsupportedDiscoverySchemaVersion()
    {
        await using var server = await LoopbackMemoryProviderServer.StartAsync(
            (path, _) =>
            {
                Assert.Equal("/v1/memory/provider", path);
                return new
                {
                    schemaVersion = "2.0",
                    health = "healthy",
                    spaces = new[]
                    {
                        new
                        {
                            id = "memory:workspace:remote",
                            scope = "workspace",
                            scopeKey = "remote-workspace",
                            displayName = "Remote Workspace Memory",
                        },
                    },
                };
            });
        var provider = new TianShuExternalSemanticMemoryProvider(
            new TianShuExternalMemoryProviderOptions
            {
                ProviderId = "remote_vector",
                Kind = "tianshu-http",
                Host = "127.0.0.1",
                Port = server.Port,
                Capabilities = ["semantic-search", "read-only"],
            },
            [CreateWritableWorkspaceMemorySpace()]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ListSpacesAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task ExternalSemanticProvider_ShouldNegotiateReadWriteModeAndCallMutationAdapter()
    {
        var space = CreateWritableWorkspaceMemorySpace();
        await using var server = await LoopbackMemoryProviderServer.StartAsync(
            (path, _) =>
            {
                Assert.Equal("/v1/memory/add", path);
                return new MemoryMutationResult(
                    true,
                    new MemoryRecordId("external-record-1"),
                    MemoryLifecycleStatus.Active,
                    Effect: MemoryMutationEffect.Upserted);
            });
        var provider = new TianShuExternalSemanticMemoryProvider(
            new TianShuExternalMemoryProviderOptions
            {
                ProviderId = "external_rw",
                Kind = "tianshu-http",
                Host = "127.0.0.1",
                Port = server.Port,
                Mode = MemoryProviderBindingMode.ReadWrite,
                Capabilities = ["add", "filter", "semantic-search", "read-write"],
            },
            [space]);

        var result = await provider.AddAsync(
            new AddMemory(space.Id, "workspace.language", StructuredValue.FromString("zh-CN")),
            CreateMemoryOperationContext(),
            CancellationToken.None);

        Assert.True(provider.Descriptor.Capabilities.HasFlag(MemoryProviderCapability.Add));
        Assert.True(provider.Descriptor.Capabilities.HasFlag(MemoryProviderCapability.ReadWriteAccess));
        Assert.True(result.Success);
        Assert.Equal(MemoryMutationEffect.Upserted, result.Effect);
    }

    [Fact]
    public async Task ExternalSemanticProvider_ShouldDegradeMutationWhenCapabilityIsMissing()
    {
        var space = CreateWritableWorkspaceMemorySpace();
        var provider = new TianShuExternalSemanticMemoryProvider(
            new TianShuExternalMemoryProviderOptions
            {
                ProviderId = "external_partial",
                Kind = "tianshu-http",
                Host = "127.0.0.1",
                Port = 9,
                Mode = MemoryProviderBindingMode.ReadWrite,
                Capabilities = ["filter", "semantic-search", "read-write"],
            },
            [space]);

        var result = await provider.AddAsync(
            new AddMemory(space.Id, "workspace.language", StructuredValue.FromString("zh-CN")),
            CreateMemoryOperationContext(),
            CancellationToken.None);

        Assert.True(provider.Descriptor.Capabilities.HasFlag(MemoryProviderCapability.Filter));
        Assert.False(provider.Descriptor.Capabilities.HasFlag(MemoryProviderCapability.Add));
        Assert.False(result.Success);
        Assert.Equal(MemoryMutationEffect.Degraded, result.Effect);
        Assert.Equal(MemoryProviderCapability.Add, result.UnsupportedCapability);
        Assert.Equal("unsupported_capability", result.DegradedReason);
    }

    [Fact]
    public async Task ExternalSemanticProvider_ShouldListReviewsThroughHttpAdapterAndRedactPrivateFields()
    {
        var space = CreateWritableWorkspaceMemorySpace();
        await using var server = await LoopbackMemoryProviderServer.StartAsync(
            (path, _) =>
            {
                Assert.Equal("/v1/memory/review/list", path);
                var record = new FactMemoryRecord(
                    "workspace.pending",
                    StructuredValue.FromString("needs review"),
                    space.Id,
                    0.73m,
                    id: new MemoryRecordId("qdrant:collection-a:review-42"),
                    lifecycleStatus: MemoryLifecycleStatus.PendingReview,
                    sources:
                    [
                        new MemorySourceRef(
                            MemorySourceKind.ExternalProvider,
                            "qdrant-review-42",
                            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["namespace"] = "workspace-alpha",
                                ["safe_note"] = "review candidate",
                            }),
                    ]);
                var candidate = new MemoryCandidate(
                    "workspace.pending",
                    StructuredValue.FromString("needs review"),
                    space.Id,
                    0.73m,
                    new MemorySourceRef(
                        MemorySourceKind.ExternalProvider,
                        "qdrant-candidate-42",
                        metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["query_dsl"] = "private",
                            ["safe_note"] = "candidate source",
                        }));
                return new MemoryReviewQueryResult([new MemoryReviewItem(record, candidate)]);
            });
        var provider = new TianShuExternalSemanticMemoryProvider(
            new TianShuExternalMemoryProviderOptions
            {
                ProviderId = "review_vector",
                Kind = "tianshu-http",
                Host = "127.0.0.1",
                Port = server.Port,
                Mode = MemoryProviderBindingMode.ReadWrite,
                Capabilities = ["review", "read-write"],
            },
            [space]);

        var result = await provider.ListReviewsAsync(
            new ListMemoryReviews(space.Id),
            CreateMemoryOperationContext(),
            CancellationToken.None);

        Assert.Empty(result.DegradedProviders);
        var item = Assert.Single(result.Items);
        Assert.StartsWith("memory-record:external:review_vector:", item.Record.Id.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("qdrant", item.Record.Id.Value, StringComparison.OrdinalIgnoreCase);
        var recordSource = Assert.Single(item.Record.Sources);
        Assert.Equal("review_vector", recordSource.SourceId);
        Assert.DoesNotContain("namespace", recordSource.Metadata.Keys);
        Assert.Equal("review candidate", recordSource.Metadata["safe_note"]);
        Assert.NotNull(item.Candidate);
        var sanitizedCandidate = item.Candidate!;
        Assert.NotNull(sanitizedCandidate.Source);
        var candidateSource = sanitizedCandidate.Source!;
        Assert.Equal("review_vector", candidateSource.SourceId);
        Assert.DoesNotContain(candidateSource.Metadata.Keys, key => key.Contains("query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("candidate source", candidateSource.Metadata["safe_note"]);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldExcludeArchivedByDefaultButAllowExplicitLifecycleFilter()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var archivedFact = new FactMemoryRecord(
            "workspace.archived",
            StructuredValue.FromString("old-preference"),
            space.Id,
            recordedAt: operationContext.Timestamp,
            lifecycleStatus: MemoryLifecycleStatus.Archived);
        await store.UpsertFactAsync(space, archivedFact, operationContext.ActorId, "unit-test", operationContext.Timestamp, CancellationToken.None);

        var defaultFilter = await service.FilterAsync(new FilterMemory(space.Id), operationContext, CancellationToken.None);
        var explicitFilter = await service.FilterAsync(
            new FilterMemory(space.Id, LifecycleStatus: MemoryLifecycleStatus.Archived),
            operationContext,
            CancellationToken.None);
        var overlay = await service.ResolveOverlayAsync(
            new ResolveMemoryOverlay(space.Id),
            habitProfile: null,
            defaultFacts: null,
            operationContext,
            CancellationToken.None);

        Assert.Empty(defaultFilter.Records);
        var archived = Assert.Single(explicitFilter.Records);
        Assert.Equal("workspace.archived", archived.Key);
        Assert.Empty(overlay.Facts);
        Assert.Equal(MemoryMergeDecision.Ignored, overlay.MergeDecision);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldExcludePendingReviewByDefaultButAllowExplicitLifecycleFilter()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var pendingFact = new FactMemoryRecord(
            "workspace.pending-review",
            StructuredValue.FromString("candidate-preference"),
            space.Id,
            recordedAt: operationContext.Timestamp,
            lifecycleStatus: MemoryLifecycleStatus.PendingReview);
        await store.UpsertFactAsync(space, pendingFact, operationContext.ActorId, "unit-test", operationContext.Timestamp, CancellationToken.None);

        var defaultFilter = await service.FilterAsync(new FilterMemory(space.Id), operationContext, CancellationToken.None);
        var explicitFilter = await service.FilterAsync(
            new FilterMemory(space.Id, LifecycleStatus: MemoryLifecycleStatus.PendingReview),
            operationContext,
            CancellationToken.None);
        var overlay = await service.ResolveOverlayAsync(
            new ResolveMemoryOverlay(space.Id),
            habitProfile: null,
            defaultFacts: null,
            operationContext,
            CancellationToken.None);

        Assert.Empty(defaultFilter.Records);
        var pending = Assert.Single(explicitFilter.Records);
        Assert.Equal("workspace.pending-review", pending.Key);
        Assert.Empty(overlay.Facts);
        Assert.Equal(MemoryMergeDecision.Ignored, overlay.MergeDecision);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldPromotePendingReviewCandidateWhenApproved()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var source = new MemorySourceRef(
            MemorySourceKind.Conversation,
            "turn-review-approve",
            role: "user",
            snippet: "记住我默认使用天枢命令",
            capturedAt: operationContext.Timestamp);
        var candidate = new MemoryCandidate(
            "workspace.cli",
            StructuredValue.FromString("tianshu"),
            space.Id,
            0.72m,
            source,
            "用户要求沉淀为工作区偏好",
            "rule.manual-review",
            MemoryFormationPath.DirectInstruction);
        await store.UpsertCandidateAsync(candidate, CancellationToken.None);

        var pending = await service.FilterAsync(
            new FilterMemory(space.Id, LifecycleStatus: MemoryLifecycleStatus.PendingReview),
            operationContext,
            CancellationToken.None);
        var approveResult = await service.ApproveReviewAsync(
            new ApproveMemoryReview(MemorySpaceId: space.Id, Key: "workspace.cli", Reason: "人工确认"),
            operationContext,
            CancellationToken.None);
        var active = await service.FilterAsync(new FilterMemory(space.Id), operationContext, CancellationToken.None);
        var pendingAfterApprove = await service.FilterAsync(
            new FilterMemory(space.Id, LifecycleStatus: MemoryLifecycleStatus.PendingReview),
            operationContext,
            CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        var pendingRecord = Assert.Single(pending.Records);
        Assert.Equal(MemoryLifecycleStatus.PendingReview, pendingRecord.LifecycleStatus);
        Assert.Equal("workspace.cli", pendingRecord.Key);
        Assert.True(approveResult.Success);
        Assert.Equal(MemoryLifecycleStatus.Active, approveResult.LifecycleStatus);
        Assert.Equal(MemoryMutationEffect.Upserted, approveResult.Effect);
        var activeRecord = Assert.Single(active.Records);
        Assert.Equal(approveResult.RecordId, activeRecord.Id);
        Assert.Equal("tianshu", activeRecord.Value.StringValue);
        Assert.Empty(pendingAfterApprove.Records);
        Assert.Contains(audit, static record => record.Operation == "approve_memory_review");
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldRejectPendingReviewCandidateWithAudit()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var candidate = new MemoryCandidate(
            "workspace.rejected",
            StructuredValue.FromString("不要采用"),
            space.Id,
            0.66m,
            new MemorySourceRef(MemorySourceKind.Conversation, "turn-review-reject", capturedAt: operationContext.Timestamp),
            "用户要求候选待审",
            "rule.manual-review");
        await store.UpsertCandidateAsync(candidate, CancellationToken.None);
        var pending = await service.FilterAsync(
            new FilterMemory(space.Id, LifecycleStatus: MemoryLifecycleStatus.PendingReview),
            operationContext,
            CancellationToken.None);
        var pendingRecord = Assert.Single(pending.Records);

        var reject = await service.ForgetAsync(
            new ForgetMemory(pendingRecord.Id, space.Id, pendingRecord.Key),
            operationContext,
            CancellationToken.None);
        var pendingAfterReject = await service.FilterAsync(
            new FilterMemory(space.Id, LifecycleStatus: MemoryLifecycleStatus.PendingReview),
            operationContext,
            CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        Assert.True(reject.Success);
        Assert.Equal(MemoryLifecycleStatus.Forgotten, reject.LifecycleStatus);
        Assert.Empty(pendingAfterReject.Records);
        Assert.Contains(audit, static record =>
            record.Operation == "reject_memory_review"
            && record.Key == "workspace.rejected"
            && record.Effect == MemoryMutationEffect.LifecycleChanged);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldListReviewItemsWithCandidateEvidenceLinksAndAudit()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var source = new MemorySourceRef(MemorySourceKind.Conversation, "turn-review-list", capturedAt: operationContext.Timestamp);
        var candidate = new MemoryCandidate(
            "workspace.review-list",
            StructuredValue.FromString("需要审核"),
            space.Id,
            0.64m,
            source,
            "用户要求候选待审",
            "rule.manual-review");
        await store.UpsertCandidateAsync(candidate, CancellationToken.None);
        var pending = await service.FilterAsync(
            new FilterMemory(space.Id, LifecycleStatus: MemoryLifecycleStatus.PendingReview),
            operationContext,
            CancellationToken.None);
        var pendingRecord = Assert.Single(pending.Records);
        await store.AppendEvidenceRecordAsync(
            new MemoryEvidenceRecord(
                "evidence-review-list",
                space.Id,
                source,
                MemoryEvidenceKind.ConversationObservation,
                "用户明确要求记录",
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["key"] = pendingRecord.Key,
                }),
            CancellationToken.None);
        await store.AppendAuditRecordAsync(
            MemoryAuditRecords.Create(
                "extract_memory",
                space.Id,
                pendingRecord.Key,
                operationContext.ActorId,
                "unit-test",
                operationContext.Timestamp,
                effect: MemoryMutationEffect.Upserted,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reviewRecordId"] = pendingRecord.Id.Value,
                }),
            CancellationToken.None);

        var result = await service.ListReviewsAsync(new ListMemoryReviews(space.Id), operationContext, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(pendingRecord.Id, item.Record.Id);
        Assert.NotNull(item.Candidate);
        Assert.Single(item.Evidence);
        Assert.Contains(item.Audit, static audit => audit.Operation == "extract_memory");
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldDemoteRestoreAndMergeReviewThroughFormalCommands()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var demoteFact = new FactMemoryRecord(
            "workspace.review-demote",
            StructuredValue.FromString("候选"),
            space.Id,
            recordedAt: operationContext.Timestamp,
            lifecycleStatus: MemoryLifecycleStatus.PendingReview);
        var mergeFact = new FactMemoryRecord(
            "workspace.review-merge",
            StructuredValue.FromString("合并候选"),
            space.Id,
            recordedAt: operationContext.Timestamp,
            lifecycleStatus: MemoryLifecycleStatus.PendingReview);
        var activeTarget = new FactMemoryRecord(
            "workspace.active",
            StructuredValue.FromString("已有事实"),
            space.Id,
            recordedAt: operationContext.Timestamp,
            lifecycleStatus: MemoryLifecycleStatus.Active);
        await store.UpsertFactAsync(space, demoteFact, operationContext.ActorId, "unit-test", operationContext.Timestamp, CancellationToken.None);
        await store.UpsertFactAsync(space, mergeFact, operationContext.ActorId, "unit-test", operationContext.Timestamp, CancellationToken.None);
        await store.UpsertFactAsync(space, activeTarget, operationContext.ActorId, "unit-test", operationContext.Timestamp, CancellationToken.None);

        var demote = await service.DemoteReviewAsync(
            new DemoteMemoryReview(demoteFact.Id, space.Id, demoteFact.Key, "证据不足"),
            operationContext,
            CancellationToken.None);
        var restore = await service.RestoreReviewAsync(
            new RestoreMemoryReview(demoteFact.Id, space.Id, demoteFact.Key, "重新审核"),
            operationContext,
            CancellationToken.None);
        var merge = await service.MergeReviewAsync(
            new MergeMemoryReview(mergeFact.Id, activeTarget.Id, space.Id, "合并重复事实"),
            operationContext,
            CancellationToken.None);
        var reviewAfterMerge = await service.FilterAsync(
            new FilterMemory(space.Id, Key: mergeFact.Key, LifecycleStatus: MemoryLifecycleStatus.Archived),
            operationContext,
            CancellationToken.None);
        var links = await store.ListSupersedeLinksAsync(space.Id, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        Assert.True(demote.Success);
        Assert.Equal(MemoryLifecycleStatus.Archived, demote.LifecycleStatus);
        Assert.True(restore.Success);
        Assert.Equal(MemoryLifecycleStatus.PendingReview, restore.LifecycleStatus);
        Assert.True(merge.Success);
        Assert.Equal(MemoryMutationEffect.Superseded, merge.Effect);
        Assert.Single(reviewAfterMerge.Records);
        Assert.Contains(links, link => link.OldRecordId == activeTarget.Id);
        Assert.Contains(audit, static record => record.Operation == "merge_memory_review");
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldActivatePendingReviewFactWhenApproved()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var pendingFact = new FactMemoryRecord(
            "workspace.review-fact",
            StructuredValue.FromString("ready"),
            space.Id,
            recordedAt: operationContext.Timestamp,
            lifecycleStatus: MemoryLifecycleStatus.PendingReview);
        await store.UpsertFactAsync(space, pendingFact, operationContext.ActorId, "unit-test", operationContext.Timestamp, CancellationToken.None);

        var approveResult = await service.ApproveReviewAsync(
            new ApproveMemoryReview(pendingFact.Id, space.Id, pendingFact.Key, "人工确认"),
            operationContext,
            CancellationToken.None);
        var active = await service.FilterAsync(new FilterMemory(space.Id), operationContext, CancellationToken.None);

        Assert.True(approveResult.Success);
        Assert.Equal(pendingFact.Id, approveResult.RecordId);
        Assert.Equal(MemoryLifecycleStatus.Active, approveResult.LifecycleStatus);
        Assert.Equal(MemoryMutationEffect.LifecycleChanged, approveResult.Effect);
        var activeRecord = Assert.Single(active.Records);
        Assert.Equal("workspace.review-fact", activeRecord.Key);
        Assert.Equal(MemoryLifecycleStatus.Active, activeRecord.LifecycleStatus);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldFilterBySourceTagsUsageAndTimeWindow()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var source = new MemorySourceRef(
            MemorySourceKind.Conversation,
            "thread-001",
            role: "user",
            capturedAt: operationContext.Timestamp);
        var fact = new FactMemoryRecord(
            "workspace.language",
            StructuredValue.FromString("zh-CN"),
            space.Id,
            confidence: 0.9m,
            recordedAt: operationContext.Timestamp,
            sources: [source],
            tags: ["language"],
            usageCount: 2,
            lastUsedAt: operationContext.Timestamp.AddMinutes(1),
            updatedAt: operationContext.Timestamp.AddMinutes(1));
        await store.UpsertFactAsync(space, fact, operationContext.ActorId, "unit-test", operationContext.Timestamp, CancellationToken.None);

        var matching = await service.FilterAsync(
            new FilterMemory(
                space.Id,
                SourceKind: MemorySourceKind.Conversation,
                SourceId: "thread-001",
                Tag: "language",
                MinimumUsageCount: 2,
                RecordedAfter: operationContext.Timestamp.AddMinutes(-1),
                RecordedBefore: operationContext.Timestamp.AddMinutes(1),
                UpdatedAfter: operationContext.Timestamp,
                UsedBefore: operationContext.Timestamp.AddMinutes(2)),
            operationContext,
            CancellationToken.None);
        var nonMatching = await service.FilterAsync(
            new FilterMemory(space.Id, Tag: "runtime"),
            operationContext,
            CancellationToken.None);

        Assert.Single(matching.Records);
        Assert.Empty(nonMatching.Records);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldAuditFilterMemory()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();

        await service.AddAsync(
            new AddMemory(space.Id, "workspace.language", StructuredValue.FromString("zh-CN")),
            operationContext,
            CancellationToken.None);

        var result = await service.FilterAsync(
            new FilterMemory(space.Id, Key: "workspace.language"),
            operationContext,
            CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        Assert.Single(result.Records);
        Assert.Contains(audit, record =>
            record.Operation == "filter_memory"
            && record.Key == "workspace.language"
            && record.Actor == operationContext.ActorId
            && record.Source == "memory-filter");
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldFilterByScopeKind()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var workspaceSpace = CreateWritableWorkspaceMemorySpace();
        var sessionSpace = new MemorySpace(
            new MemorySpaceId("memory:session:thread-001"),
            MemoryScopeKind.Session,
            "thread-001",
            "Thread Memory");
        var service = CreateMemoryService(store, workspaceSpace, sessionSpace);
        var operationContext = CreateMemoryOperationContext();

        await service.AddAsync(
            new AddMemory(workspaceSpace.Id, "workspace.preference", StructuredValue.FromString("workspace")),
            operationContext,
            CancellationToken.None);
        await service.AddAsync(
            new AddMemory(sessionSpace.Id, "session.preference", StructuredValue.FromString("session")),
            operationContext,
            CancellationToken.None);

        var workspaceFacts = await service.FilterAsync(
            new FilterMemory(ScopeKind: MemoryScopeKind.Workspace),
            operationContext,
            CancellationToken.None);
        var sessionFacts = await service.FilterAsync(
            new FilterMemory(ScopeKind: MemoryScopeKind.Session),
            operationContext,
            CancellationToken.None);

        var workspaceFact = Assert.Single(workspaceFacts.Records);
        Assert.Equal("workspace.preference", workspaceFact.Key);
        var sessionFact = Assert.Single(sessionFacts.Records);
        Assert.Equal("session.preference", sessionFact.Key);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldReportFailure_WhenReadOnlyProviderCannotMutate()
    {
        var space = CreateWritableWorkspaceMemorySpace();
        var provider = new ReadOnlyMemoryProvider(space);
        var service = new DefaultMemoryService(new MemoryProviderRegistry([provider]));
        var context = CreateMemoryOperationContext();
        var addCommand = new AddMemory(space.Id, "workspace.preference", StructuredValue.FromString("cli-first"));
        var forgetCommand = new ForgetMemory(new MemoryRecordId("memory-record:readonly"), space.Id, "workspace.preference");

        var serviceAddResult = await service.AddAsync(
            addCommand,
            context,
            CancellationToken.None);
        var serviceForgetResult = await service.ForgetAsync(
            forgetCommand,
            context,
            CancellationToken.None);
        var providerAddResult = await provider.AddAsync(addCommand, context, CancellationToken.None);
        var providerForgetResult = await provider.ForgetAsync(forgetCommand, context, CancellationToken.None);

        Assert.False(serviceAddResult.Success);
        Assert.Equal("memory_provider_not_found", serviceAddResult.DegradedReason);
        Assert.Equal(MemoryMutationEffect.Degraded, serviceAddResult.Effect);
        Assert.False(serviceForgetResult.Success);
        Assert.Equal("memory_provider_not_found", serviceForgetResult.DegradedReason);
        Assert.Equal(MemoryMutationEffect.Degraded, serviceForgetResult.Effect);
        Assert.False(providerAddResult.Success);
        Assert.Equal(MemoryProviderCapability.Add, providerAddResult.UnsupportedCapability);
        Assert.Equal(MemoryMutationEffect.Degraded, providerAddResult.Effect);
        Assert.False(providerForgetResult.Success);
        Assert.Equal(MemoryProviderCapability.Forget, providerForgetResult.UnsupportedCapability);
        Assert.Equal(MemoryMutationEffect.Degraded, providerForgetResult.Effect);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldRejectMutationsAgainstReadOnlyMemorySpace()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var readOnlySpace = new MemorySpace(
            new MemorySpaceId("memory:workspace:readonly"),
            MemoryScopeKind.Workspace,
            "readonly",
            "Read-only Workspace Memory",
            isReadOnly: true);
        var service = CreateMemoryService(store, readOnlySpace);
        var context = CreateMemoryOperationContext();
        var fact = new FactMemoryRecord(
            "workspace.preference",
            StructuredValue.FromString("cli-first"),
            readOnlySpace.Id,
            recordedAt: context.Timestamp);
        await store.UpsertFactAsync(readOnlySpace, fact, context.ActorId, "unit-test", context.Timestamp, CancellationToken.None);

        var addResult = await service.AddAsync(
            new AddMemory(readOnlySpace.Id, "workspace.language", StructuredValue.FromString("zh-CN")),
            context,
            CancellationToken.None);
        var forgetResult = await service.ForgetAsync(
            new ForgetMemory(fact.Id, readOnlySpace.Id, fact.Key),
            context,
            CancellationToken.None);
        var deleteResult = await service.DeleteAsync(
            new DeleteMemory(fact.Id, readOnlySpace.Id, fact.Key),
            context,
            CancellationToken.None);
        var feedbackResult = await service.RecordFeedbackAsync(
            new RecordMemoryFeedback(fact.Id, MemoryMergeDecision.NeedsReview, "保持只读"),
            context,
            CancellationToken.None);
        var citationResult = await service.RecordCitationAsync(
            new RecordMemoryCitation(new MemoryCitation([new MemoryCitationEntry(fact.Id, readOnlySpace.Id, fact.Key)])),
            context,
            CancellationToken.None);
        var visible = await service.FilterAsync(new FilterMemory(readOnlySpace.Id), context, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(readOnlySpace.Id, CancellationToken.None);

        Assert.False(addResult.Success);
        Assert.False(forgetResult.Success);
        Assert.False(deleteResult.Success);
        Assert.False(feedbackResult.Success);
        Assert.False(citationResult.Success);
        Assert.All(
            [addResult, forgetResult, deleteResult, feedbackResult, citationResult],
            result =>
            {
                Assert.Equal("memory_space_read_only", result.DegradedReason);
                Assert.Equal(MemoryMutationEffect.Degraded, result.Effect);
            });
        var visibleFact = Assert.Single(visible.Records);
        Assert.Equal(MemoryLifecycleStatus.Active, visibleFact.LifecycleStatus);
        Assert.Equal(0, visibleFact.UsageCount);
        Assert.DoesNotContain(audit, static record => record.Operation == "forget_fact");
        Assert.DoesNotContain(audit, static record => record.Operation == "delete_fact");
        Assert.DoesNotContain(audit, static record => record.Operation == "record_feedback");
        Assert.DoesNotContain(audit, static record => record.Operation == "record_citation");
    }

    [Fact]
    public async Task DefaultMemoryServiceAndLocalProvider_ShouldExposeImportDegradedBoundaryAndLocalExport()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var localProvider = new TianShuLocalMemoryProvider(store, spaces: [space]);
        var service = new DefaultMemoryService(
            new MemoryProviderRegistry([localProvider]),
            auditSink: new TianShuLocalMemoryAuditSink(store));
        var context = CreateMemoryOperationContext();
        var source = new MemorySourceRef(MemorySourceKind.ExternalProvider, "external-memory");
        await store.UpsertFactAsync(
            space,
            new FactMemoryRecord(
                "workspace.preference",
                StructuredValue.FromString("cli-first"),
                space.Id,
                recordedAt: context.Timestamp,
                sources: [source],
                tags: ["exportable"]),
            context.ActorId,
            "unit-test",
            context.Timestamp,
            CancellationToken.None);
        await store.UpsertFactAsync(
            space,
            new FactMemoryRecord(
                "workspace.private-note",
                StructuredValue.FromString("do-not-export"),
                space.Id,
                recordedAt: context.Timestamp,
                tags: ["private"]),
            context.ActorId,
            "unit-test",
            context.Timestamp,
            CancellationToken.None);

        var serviceImport = await service.ImportAsync(
            new ImportMemory(space.Id, source),
            context,
            CancellationToken.None);
        var serviceExport = await service.ExportAsync(
            new ExportMemory(space.Id, source, new FilterMemory(Tag: "exportable")),
            context,
            CancellationToken.None);
        var providerImport = await localProvider.ImportAsync(
            new ImportMemory(space.Id, source),
            context,
            CancellationToken.None);
        var providerExport = await localProvider.ExportAsync(
            new ExportMemory(space.Id, source, new FilterMemory(Tag: "exportable")),
            context,
            CancellationToken.None);

        Assert.False(serviceImport.Success);
        Assert.Equal("memory_provider_not_found", serviceImport.DegradedReason);
        Assert.Equal(MemoryProviderCapability.Import, serviceImport.UnsupportedCapability);
        Assert.False(providerImport.Success);
        Assert.Equal("unsupported_capability", providerImport.DegradedReason);
        Assert.Equal(MemoryProviderCapability.Import, providerImport.UnsupportedCapability);
        Assert.Empty(serviceExport.DegradedProviders);
        Assert.Empty(providerExport.DegradedProviders);
        var serviceRecord = Assert.Single(serviceExport.Records);
        var providerRecord = Assert.Single(providerExport.Records);
        Assert.Equal("workspace.preference", serviceRecord.Key);
        Assert.Equal("workspace.preference", providerRecord.Key);
        Assert.Equal(MemorySearchMode.Structured, serviceExport.EffectiveSearchMode);
        Assert.Equal(MemorySearchMode.Structured, providerExport.EffectiveSearchMode);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);
        var importAudit = Assert.Single(audit, static record => record.Operation == "import_memory");
        Assert.Equal("records:0", importAudit.Key);
        Assert.Equal(context.ActorId, importAudit.Actor);
        Assert.Equal("external-memory", importAudit.Source);
    }

    [Fact]
    public async Task MemoryProviderRegistry_ShouldConstrainCapabilitiesByBindingMode()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var provider = new TianShuLocalMemoryProvider(store, [space]);
        var service = new DefaultMemoryService(
            new MemoryProviderRegistry([provider]),
            auditSink: new TianShuLocalMemoryAuditSink(store));
        var context = CreateMemoryOperationContext();
        await store.UpsertFactAsync(
            space,
            new FactMemoryRecord("workspace.preference", StructuredValue.FromString("cli-first"), space.Id, recordedAt: context.Timestamp),
            context.ActorId,
            "unit-test",
            context.Timestamp,
            CancellationToken.None);

        var bindReadOnly = await service.BindProviderAsync(
            new BindMemoryProvider(
                TianShuLocalMemoryProvider.DefaultProviderId,
                space.Id,
                MemoryProviderBindingMode.ReadOnly,
                MemoryProviderCapability.ListSpaces | MemoryProviderCapability.Filter),
            context,
            CancellationToken.None);
        var readable = await service.FilterAsync(new FilterMemory(space.Id), context, CancellationToken.None);
        var blockedWrite = await service.AddAsync(
            new AddMemory(space.Id, "workspace.language", StructuredValue.FromString("zh-CN")),
            context,
            CancellationToken.None);
        var mismatchedBinding = await service.BindProviderAsync(
            new BindMemoryProvider(
                TianShuLocalMemoryProvider.DefaultProviderId,
                space.Id,
                MemoryProviderBindingMode.ReadOnly,
                MemoryProviderCapability.Add | MemoryProviderCapability.Filter),
            context,
            CancellationToken.None);
        var exportBinding = await service.BindProviderAsync(
            new BindMemoryProvider(
                TianShuLocalMemoryProvider.DefaultProviderId,
                space.Id,
                MemoryProviderBindingMode.ReadOnly,
                MemoryProviderCapability.ListSpaces | MemoryProviderCapability.Export),
            context,
            CancellationToken.None);

        Assert.True(bindReadOnly.Success);
        Assert.Single(readable.Records);
        Assert.False(blockedWrite.Success);
        Assert.Equal("memory_provider_not_found", blockedWrite.DegradedReason);
        Assert.Equal(MemoryMutationEffect.Degraded, blockedWrite.Effect);
        Assert.False(mismatchedBinding.Success);
        Assert.Equal("memory_provider_binding_mode_capability_mismatch", mismatchedBinding.DegradedReason);
        Assert.Equal(MemoryProviderCapability.Add, mismatchedBinding.UnsupportedCapability);
        Assert.Equal(MemoryMutationEffect.Degraded, mismatchedBinding.Effect);
        Assert.False(exportBinding.Success);
        Assert.Equal("memory_provider_binding_mode_capability_mismatch", exportBinding.DegradedReason);
        Assert.Equal(MemoryProviderCapability.Export, exportBinding.UnsupportedCapability);
        Assert.Equal(MemoryMutationEffect.Degraded, exportBinding.Effect);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldAutoPromoteExplicitWorkspaceInstruction()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var source = new MemorySourceRef(
            MemorySourceKind.Conversation,
            "thread-001",
            role: "user",
            snippet: "以后默认用中文汇报");

        var candidates = await service.ExtractAsync(
            new ExtractMemory(space.Id, source),
            CreateMemoryOperationContext(),
            CancellationToken.None);
        var facts = await store.ListFactsAsync(space, CancellationToken.None);
        var evidence = await store.ListEvidenceRecordsAsync(space.Id, CancellationToken.None);
        var persistedCandidates = await store.ListCandidatesAsync(space.Id, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal("preference.default", candidate.Key);
        Assert.Equal("用中文汇报", candidate.Value.StringValue);
        Assert.Equal(MemoryLifecycleStatus.PendingReview, candidate.LifecycleStatus);
        Assert.Equal("rule.explicit-default", candidate.RuleId);
        var fact = Assert.Single(facts);
        Assert.Equal("preference.default", fact.Key);
        Assert.Equal("用中文汇报", fact.Value.StringValue);
        Assert.Equal(MemoryLifecycleStatus.Active, fact.LifecycleStatus);
        Assert.Single(evidence);
        Assert.Single(persistedCandidates);
        Assert.Contains(audit, static record =>
            record.Operation == "promote_memory_candidate"
            && record.Effect == MemoryMutationEffect.Upserted
            && record.Metadata["decisionKind"] == MemoryPromotionDecisionKind.AutoPromote.ToString()
            && !record.Metadata.ContainsKey("approvalQueueProjection"));
        Assert.Contains(audit, static record =>
            record.Operation == "extract_memory"
            && record.Key == "preference.default"
            && record.Source == "thread-001");
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldAutoPromoteImplicitUserPreference()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var source = new MemorySourceRef(
            MemorySourceKind.Conversation,
            "thread-adaptive-preference",
            role: "user",
            snippet: "我一般喜欢你先给结论，再给细节");

        var candidates = await service.ExtractAsync(
            new ExtractMemory(space.Id, source),
            CreateMemoryOperationContext(),
            CancellationToken.None);
        var facts = await store.ListFactsAsync(space, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal("preference.user", candidate.Key);
        Assert.Equal("rule.semantic-preference", candidate.RuleId);
        Assert.Contains("先给结论", candidate.Value.StringValue, StringComparison.Ordinal);
        var fact = Assert.Single(facts);
        Assert.Equal("preference.user", fact.Key);
        Assert.Equal(MemoryLifecycleStatus.Active, fact.LifecycleStatus);
        Assert.Contains("先给结论", fact.Value.StringValue, StringComparison.Ordinal);
        Assert.Contains(audit, static record =>
            record.Operation == "promote_memory_candidate"
            && record.Effect == MemoryMutationEffect.Upserted
            && record.Metadata["decisionKind"] == MemoryPromotionDecisionKind.AutoPromote.ToString());
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldAutoPromoteImplicitWorkspaceRule()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var source = new MemorySourceRef(
            MemorySourceKind.Conversation,
            "thread-adaptive-rule",
            role: "user",
            snippet: "这个仓库里 VSIX 不要用 dotnet build");

        var candidates = await service.ExtractAsync(
            new ExtractMemory(space.Id, source),
            CreateMemoryOperationContext(),
            CancellationToken.None);
        var facts = await store.ListFactsAsync(space, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal("workspace.rule.vsix", candidate.Key);
        Assert.Equal("rule.semantic-project-rule", candidate.RuleId);
        var fact = Assert.Single(facts);
        Assert.Equal("workspace.rule.vsix", fact.Key);
        Assert.Equal(MemoryLifecycleStatus.Active, fact.LifecycleStatus);
        Assert.Contains("dotnet build", fact.Value.StringValue, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldKeepConflictingAdaptiveRuleInGovernancePipeline()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var add = await service.AddAsync(
            new AddMemory(space.Id, "workspace.rule.vsix", StructuredValue.FromString("VSIX 可以用 dotnet build"), 0.9m),
            operationContext,
            CancellationToken.None);
        var source = new MemorySourceRef(
            MemorySourceKind.Conversation,
            "thread-adaptive-conflict",
            role: "user",
            snippet: "这个仓库里 VSIX 不要用 dotnet build");

        var candidates = await service.ExtractAsync(
            new ExtractMemory(space.Id, source),
            operationContext,
            CancellationToken.None);
        var facts = await store.ListFactsAsync(space, CancellationToken.None);
        var persistedCandidates = await store.ListCandidatesAsync(space.Id, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        Assert.True(add.Success);
        Assert.Single(candidates);
        Assert.Single(persistedCandidates);
        var fact = Assert.Single(facts);
        Assert.Equal("VSIX 可以用 dotnet build", fact.Value.StringValue);
        Assert.Contains(audit, static record =>
            record.Operation == "promote_memory_candidate"
            && record.Metadata["decisionKind"] == MemoryPromotionDecisionKind.SupersedeProposal.ToString()
            && record.Metadata["approvalQueueProjection"] == "memory.review"
            && record.ReasonCodes.Contains(MemoryRiskReasonCode.ConflictsWithActiveFact));
    }

    [Fact]
    public async Task MemoryConsolidationWorker_ShouldCreateIdempotentReviewProposalAudit()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var context = CreateMemoryOperationContext();
        var source = new MemorySourceRef(
            MemorySourceKind.Conversation,
            "thread-consolidation",
            role: "user",
            snippet: "以后默认用天枢命令",
            capturedAt: context.Timestamp);
        await store.UpsertCandidateAsync(
            new MemoryCandidate(
                "workspace.cli",
                StructuredValue.FromString("tianshu"),
                space.Id,
                0.77m,
                source,
                "candidate requires review",
                "rule.consolidation"),
            CancellationToken.None);
        var worker = new MemoryConsolidationWorker(store);

        var first = await worker.RunOnceAsync(space.Id, context, CancellationToken.None);
        var second = await worker.RunOnceAsync(space.Id, context, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        Assert.Equal(1, first.ProposalsCreated);
        Assert.Equal(0, second.ProposalsCreated);
        var proposal = Assert.Single(audit, static record =>
            record.Operation == MemoryConsolidationWorker.ProposalOperationName);
        Assert.Equal("review_candidate", proposal.Metadata["proposalKind"]);
        Assert.False(string.IsNullOrWhiteSpace(proposal.Metadata["idempotencyKey"]));
        Assert.Equal("audit-only", proposal.Metadata["permissionBoundary"]);
        Assert.Equal(MemoryMutationEffect.None, proposal.Effect);
    }

    [Fact]
    public async Task MemoryConsolidationWorker_ShouldPersistLeaseAndSkipConcurrentRun()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var context = CreateMemoryOperationContext();
        var worker = new MemoryConsolidationWorker(store);

        var first = await worker.RunOnceAsync(space.Id, context, CancellationToken.None);
        var second = await worker.RunOnceAsync(space.Id, context, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        Assert.True(first.LeaseAcquired);
        Assert.False(second.LeaseAcquired);
        Assert.True(second.SkippedByLease);
        var lease = Assert.Single(audit, static record =>
            record.Operation == MemoryConsolidationWorker.LeaseOperationName);
        Assert.Equal("audit-only", lease.Metadata["permissionBoundary"]);
        Assert.False(string.IsNullOrWhiteSpace(lease.Metadata["leaseExpiresAt"]));
    }

    [Fact]
    public async Task MemoryConsolidationWorker_ShouldHonorCooldownWindow()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var context = CreateMemoryOperationContext();
        await store.AppendAuditRecordAsync(
            MemoryAuditRecords.Create(
                MemoryConsolidationWorker.ProposalOperationName,
                space.Id,
                "workspace.cli",
                context.ActorId,
                "unit-test",
                context.Timestamp,
                effect: MemoryMutationEffect.None,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["idempotencyKey"] = "previous",
                    ["proposalKind"] = "review_candidate",
                    ["cooldownKey"] = $"{space.Id.Value}:workspace.cli",
                }),
            CancellationToken.None);
        await store.UpsertCandidateAsync(
            new MemoryCandidate(
                "workspace.cli",
                StructuredValue.FromString("tianshu"),
                space.Id,
                0.9m,
                new MemorySourceRef(MemorySourceKind.Conversation, "thread-cooldown", capturedAt: context.Timestamp)),
            CancellationToken.None);
        var worker = new MemoryConsolidationWorker(store);

        var result = await worker.RunOnceAsync(
            space.Id,
            context,
            CancellationToken.None,
            new MemoryConsolidationOptions(enableLease: false));
        var proposals = (await store.ListAuditRecordsAsync(space.Id, CancellationToken.None))
            .Where(static record => record.Operation == MemoryConsolidationWorker.ProposalOperationName)
            .ToArray();

        Assert.Equal(1, result.CandidatesSkippedByCooldown);
        Assert.Single(proposals);
    }

    [Fact]
    public async Task MemoryConsolidationWorker_ShouldRecordFailureDiagnosticsAndDeferRetry()
    {
        var innerStore = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var context = CreateMemoryOperationContext();
        await innerStore.UpsertCandidateAsync(
            new MemoryCandidate(
                "workspace.cli",
                StructuredValue.FromString("tianshu"),
                space.Id,
                0.9m,
                new MemorySourceRef(MemorySourceKind.Conversation, "thread-failure", capturedAt: context.Timestamp)),
            CancellationToken.None);
        var failingWorker = new MemoryConsolidationWorker(new ProposalFailingMemoryStore(innerStore));
        var options = new MemoryConsolidationOptions(enableLease: false, retryDelay: TimeSpan.FromMinutes(10));

        var failed = await failingWorker.RunOnceAsync(space.Id, context, CancellationToken.None, options);
        var deferred = await new MemoryConsolidationWorker(innerStore)
            .RunOnceAsync(space.Id, context, CancellationToken.None, options);
        var audit = await innerStore.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        Assert.Equal(1, failed.FailuresRecorded);
        Assert.Equal(0, failed.ProposalsCreated);
        Assert.Equal(1, deferred.RetriesDeferred);
        Assert.Equal(0, deferred.ProposalsCreated);
        var failure = Assert.Single(audit, static record =>
            record.Operation == MemoryConsolidationWorker.FailureOperationName);
        Assert.Equal("audit-only", failure.Metadata["permissionBoundary"]);
        Assert.Equal(nameof(InvalidOperationException), failure.Metadata["errorType"]);
        Assert.False(string.IsNullOrWhiteSpace(failure.Metadata["nextRetryAt"]));
        Assert.DoesNotContain(audit, static record =>
            record.Operation == MemoryConsolidationWorker.ProposalOperationName);
    }

    [Fact]
    public async Task MemoryConsolidationWorker_ShouldProposeSupersedeWhenCandidateConflictsWithActiveFact()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var context = CreateMemoryOperationContext();
        await store.UpsertFactAsync(
            space,
            "workspace.cli",
            StructuredValue.FromString("legacy-cli"),
            CreateContext(),
            "unit-test",
            0.9m,
            CancellationToken.None);
        await store.UpsertCandidateAsync(
            new MemoryCandidate(
                "workspace.cli",
                StructuredValue.FromString("tianshu"),
                space.Id,
                0.91m,
                new MemorySourceRef(MemorySourceKind.Conversation, "thread-conflict", capturedAt: context.Timestamp),
                "newer correction"),
            CancellationToken.None);
        var worker = new MemoryConsolidationWorker(store);

        var result = await worker.RunOnceAsync(space.Id, context, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        Assert.Equal(1, result.ProposalsCreated);
        var proposal = Assert.Single(audit, static record =>
            record.Operation == MemoryConsolidationWorker.ProposalOperationName);
        Assert.Equal("supersede_proposal", proposal.Metadata["proposalKind"]);
        Assert.Contains(MemoryRiskReasonCode.ConflictsWithActiveFact, proposal.ReasonCodes);
        var activeFacts = await store.ListFactsAsync(space, CancellationToken.None);
        Assert.Equal("legacy-cli", Assert.Single(activeFacts).Value.StringValue);
    }

    [Fact]
    public async Task MemoryConsolidationWorker_ShouldUseLatestDuplicateCandidateProjection()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var context = CreateMemoryOperationContext();
        var worker = new MemoryConsolidationWorker(store);
        await store.UpsertCandidateAsync(
            new MemoryCandidate(
                "workspace.cli",
                StructuredValue.FromString("tianshu"),
                space.Id,
                0.7m,
                new MemorySourceRef(MemorySourceKind.Conversation, "thread-duplicate-a", capturedAt: context.Timestamp)),
            CancellationToken.None);
        await store.UpsertCandidateAsync(
            new MemoryCandidate(
                "workspace.cli",
                StructuredValue.FromString("tianshu"),
                space.Id,
                0.9m,
                new MemorySourceRef(MemorySourceKind.Conversation, "thread-duplicate-b", capturedAt: context.Timestamp.AddSeconds(1))),
            CancellationToken.None);

        var result = await worker.RunOnceAsync(space.Id, context, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        Assert.Equal(1, result.CandidatesScanned);
        Assert.Equal(1, result.ProposalsCreated);
        var proposal = Assert.Single(audit, static record =>
            record.Operation == MemoryConsolidationWorker.ProposalOperationName);
        Assert.Equal("review_candidate", proposal.Metadata["proposalKind"]);
        Assert.Equal("thread-duplicate-b", proposal.Source);
    }

    [Fact]
    public async Task MemoryConsolidationWorker_ShouldProposeArchiveAndOverlayCacheRebuildWithoutMutatingFacts()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var context = CreateMemoryOperationContext();
        var staleAt = context.Timestamp.AddDays(-120);
        var staleFact = new FactMemoryRecord(
            "workspace.old.preference",
            StructuredValue.FromString("旧偏好"),
            space.Id,
            0.8m,
            staleAt,
            usageCount: 0,
            updatedAt: staleAt);
        await store.UpsertFactAsync(
            space,
            staleFact,
            context.ActorId,
            "unit-test",
            staleAt,
            CancellationToken.None);
        var worker = new MemoryConsolidationWorker(store);

        var result = await worker.RunOnceAsync(
            space.Id,
            context,
            CancellationToken.None,
            new MemoryConsolidationOptions(
                includeArchiveProposals: true,
                includeOverlayCacheRebuildProposals: true));
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);
        var facts = await store.ListFactsAsync(space, CancellationToken.None);

        Assert.Equal(2, result.ProposalsCreated);
        Assert.Contains(audit, static record =>
            record.Operation == MemoryConsolidationWorker.ProposalOperationName
            && record.Metadata["proposalKind"] == "archive_proposal");
        Assert.Contains(audit, static record =>
            record.Operation == MemoryConsolidationWorker.ProposalOperationName
            && record.Metadata["proposalKind"] == "overlay_cache_rebuild_proposal");
        Assert.Contains(audit, static record =>
            record.Operation == MemoryConsolidationWorker.MaintenanceOperationName
            && record.Metadata["maintenanceKind"] == "overlay_cache_snapshot"
            && record.Metadata["activeFactCount"] == "1");
        Assert.Equal(MemoryLifecycleStatus.Active, Assert.Single(facts).LifecycleStatus);
    }

    [Fact]
    public async Task MemoryConsolidationWorker_ShouldProposeForgetWithoutMutatingFacts()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var context = CreateMemoryOperationContext();
        var staleAt = context.Timestamp.AddDays(-120);
        var staleFact = new FactMemoryRecord(
            "workspace.old.preference",
            StructuredValue.FromString("旧偏好"),
            space.Id,
            0.8m,
            staleAt,
            usageCount: 0,
            updatedAt: staleAt);
        await store.UpsertFactAsync(
            space,
            staleFact,
            context.ActorId,
            "unit-test",
            staleAt,
            CancellationToken.None);
        var worker = new MemoryConsolidationWorker(store);

        var result = await worker.RunOnceAsync(
            space.Id,
            context,
            CancellationToken.None,
            new MemoryConsolidationOptions(includeForgetProposals: true));
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);
        var facts = await store.ListFactsAsync(space, CancellationToken.None);

        Assert.Equal(1, result.ProposalsCreated);
        Assert.Contains(audit, static record =>
            record.Operation == MemoryConsolidationWorker.ProposalOperationName
            && record.Metadata["proposalKind"] == "forget_proposal"
            && record.Metadata["permissionBoundary"] == "audit-only");
        Assert.Equal(MemoryLifecycleStatus.Active, Assert.Single(facts).LifecycleStatus);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldAutoPromoteLowRiskSessionCandidateWithTraceableEvidence()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = new MemorySpace(
            new MemorySpaceId("memory:session:thread-001"),
            MemoryScopeKind.Session,
            "thread-001",
            "Thread Session Memory");
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var source = new MemorySourceRef(
            MemorySourceKind.Conversation,
            "thread-001",
            role: "user",
            snippet: "以后默认用中文汇报");

        var candidates = await service.ExtractAsync(
            new ExtractMemory(space.Id, source),
            operationContext,
            CancellationToken.None);
        var facts = await store.ListFactsAsync(space, CancellationToken.None);
        var evidence = await store.ListEvidenceRecordsAsync(space.Id, CancellationToken.None);
        var persistedCandidates = await store.ListCandidatesAsync(space.Id, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal(MemoryFormationPath.ExploratoryLearning, candidate.FormationPath);
        var fact = Assert.Single(facts);
        Assert.Equal("preference.default", fact.Key);
        Assert.Equal(MemoryLifecycleStatus.Active, fact.LifecycleStatus);
        Assert.Equal(MemoryFormationPath.ExploratoryLearning, fact.FormationPath);
        Assert.Single(evidence);
        Assert.Single(persistedCandidates);
        Assert.Contains(audit, static record =>
            record.Operation == "promote_memory_candidate"
            && record.Effect == MemoryMutationEffect.Upserted
            && record.Metadata["decisionKind"] == MemoryPromotionDecisionKind.AutoPromote.ToString());
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldAuditFeedback()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var addResult = await service.AddAsync(
            new AddMemory(space.Id, "workspace.preference", StructuredValue.FromString("cli-first"), 0.9m),
            operationContext,
            CancellationToken.None);
        var recordId = Assert.NotNull(addResult.RecordId);

        var feedback = await service.RecordFeedbackAsync(
            new RecordMemoryFeedback(recordId, MemoryMergeDecision.NeedsReview, "应该改成中文优先"),
            operationContext,
            CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        Assert.True(feedback.Success);
        Assert.Equal(addResult.RecordId, feedback.RecordId);
        Assert.Contains(audit, static record => record.Operation == "record_feedback" && record.Key == "workspace.preference");
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldRecordCitationUsageAndAudit()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var addResult = await service.AddAsync(
            new AddMemory(space.Id, "workspace.preference", StructuredValue.FromString("cli-first"), 0.9m),
            operationContext,
            CancellationToken.None);
        var recordId = Assert.NotNull(addResult.RecordId);

        var citation = new MemoryCitation(
        [
            new MemoryCitationEntry(recordId, space.Id, "workspace.preference")
        ]);
        var result = await service.RecordCitationAsync(
            new RecordMemoryCitation(citation),
            operationContext,
            CancellationToken.None);
        var visible = await service.FilterAsync(new FilterMemory(space.Id), operationContext, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        Assert.True(result.Success);
        var fact = Assert.Single(visible.Records);
        Assert.Equal(1, fact.UsageCount);
        Assert.Equal(operationContext.Timestamp, fact.LastUsedAt);
        Assert.Contains(audit, static record => record.Operation == "record_citation" && record.Key == "workspace.preference");
    }

    [Fact]
    public void MemoryFormationTracker_ShouldCreateAnalogicalTransferAndSafeLearningTrace()
    {
        var timestamp = new DateTimeOffset(2026, 4, 30, 9, 0, 0, TimeSpan.Zero);
        var spaceId = new MemorySpaceId("memory:workspace:d/gitrepos/personal/TianShu");
        var source = new MemorySourceRef(MemorySourceKind.ToolResult, "shell", snippet: "dotnet test passed");
        var previous = new FactMemoryRecord(
            "workspace.test.command",
            StructuredValue.FromString("dotnet test"),
            spaceId,
            tags: ["dotnet", "test"],
            sources: [source],
            recordedAt: timestamp);
        var candidate = new MemoryCandidate(
            "workspace.test.command",
            StructuredValue.FromString("dotnet test -m:1"),
            spaceId,
            0.9m,
            source,
            "validation passed",
            contextSignature: new MemoryContextSignature(
                memorySpaceIds: [spaceId],
                tags: ["dotnet"]));
        var evidence = new MemoryEvidenceRecord(
            "evidence:test-pass",
            spaceId,
            source,
            MemoryEvidenceKind.TestResult,
            "dotnet test passed",
            MemoryScopeKind.Workspace,
            timestamp);

        var snapshot = new MemoryFormationTracker().Track(candidate, [previous], [evidence], timestamp);

        Assert.Equal(MemoryFormationPath.AnalogicalTransfer, snapshot.FormationPath);
        Assert.NotNull(snapshot.TransferLink);
        Assert.Contains("same-key", snapshot.TransferLink!.SimilarityBasis);
        Assert.Equal("memory:workspace:d/gitrepos/personal/TianShu:workspace.test.command", snapshot.LearningTrace.ProblemSignature);
        Assert.DoesNotContain("secret", snapshot.Metadata.Values, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldSupersedeWithoutOverwritingOldFact()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();
        var add = await service.AddAsync(
            new AddMemory(space.Id, "workspace.preference", StructuredValue.FromString("old")),
            operationContext,
            CancellationToken.None);

        var result = await service.SupersedeAsync(
            new SupersedeMemory(
                Assert.NotNull(add.RecordId),
                space.Id,
                "workspace.preference",
                StructuredValue.FromString("new"),
                "corrected by user"),
            operationContext,
            CancellationToken.None);
        var active = await service.FilterAsync(new FilterMemory(space.Id), operationContext, CancellationToken.None);
        var archived = await service.FilterAsync(
            new FilterMemory(space.Id, LifecycleStatus: MemoryLifecycleStatus.Archived),
            operationContext,
            CancellationToken.None);
        var links = await store.ListSupersedeLinksAsync(space.Id, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(MemoryMutationEffect.Superseded, result.Effect);
        Assert.Single(active.Records);
        Assert.Equal("new", active.Records[0].Value.StringValue);
        Assert.Single(archived.Records);
        Assert.Equal("old", archived.Records[0].Value.StringValue);
        var link = Assert.Single(links);
        Assert.Equal(add.RecordId, link.OldRecordId);
        Assert.Equal(result.RecordId, link.NewRecordId);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldPassOfficialMemoryAcceptanceMatrix()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var service = CreateMemoryService(store, space);
        var operationContext = CreateMemoryOperationContext();

        var preferenceCandidates = await service.ExtractAsync(
            new ExtractMemory(
                space.Id,
                new MemorySourceRef(
                    MemorySourceKind.Conversation,
                    "turn-preference",
                    role: "user",
                    snippet: "我一般喜欢你先给结论，再给细节",
                    capturedAt: operationContext.Timestamp)),
            operationContext,
            CancellationToken.None);
        var preferenceCandidate = Assert.Single(preferenceCandidates);
        Assert.Equal("preference.user", preferenceCandidate.Key);
        Assert.Equal("rule.semantic-preference", preferenceCandidate.RuleId);
        var activePreference = Assert.Single(
            (await service.FilterAsync(new FilterMemory(space.Id, Key: "preference.user"), operationContext, CancellationToken.None)).Records);
        Assert.Equal(MemoryLifecycleStatus.Active, activePreference.LifecycleStatus);

        var overlayAfterPreference = await service.ResolveOverlayAsync(
            new ResolveMemoryOverlay(space.Id),
            habitProfile: null,
            defaultFacts: null,
            operationContext,
            CancellationToken.None);
        Assert.Contains(
            overlayAfterPreference.Facts,
            static fact => fact.Key == "preference.user"
                && fact.Value.StringValue is { } value
                && value.Contains("先给结论", StringComparison.Ordinal));

        var pendingCandidate = new MemoryCandidate(
            "workspace.reviewed.rule",
            StructuredValue.FromString("提交前必须运行最小验证"),
            space.Id,
            0.72m,
            new MemorySourceRef(
                MemorySourceKind.Conversation,
                "turn-pending-review",
                role: "user",
                snippet: "这个规则需要先进入待审",
                capturedAt: operationContext.Timestamp),
            "置信度不足，需要治理后提升",
            "rule.acceptance-review",
            MemoryFormationPath.DirectInstruction);
        await store.UpsertCandidateAsync(pendingCandidate, CancellationToken.None);
        var pendingBeforeApprove = Assert.Single(
            (await service.FilterAsync(
                new FilterMemory(space.Id, Key: pendingCandidate.Key, LifecycleStatus: MemoryLifecycleStatus.PendingReview),
                operationContext,
                CancellationToken.None)).Records);
        Assert.Equal(MemoryLifecycleStatus.PendingReview, pendingBeforeApprove.LifecycleStatus);
        var approvePending = await service.ApproveReviewAsync(
            new ApproveMemoryReview(MemorySpaceId: space.Id, Key: pendingCandidate.Key, Reason: "验收中确认可提升"),
            operationContext,
            CancellationToken.None);
        Assert.True(approvePending.Success);
        Assert.Equal(MemoryLifecycleStatus.Active, approvePending.LifecycleStatus);

        var addLegacyRule = await service.AddAsync(
            new AddMemory(space.Id, "workspace.rule.vsix", StructuredValue.FromString("VSIX 可以用 dotnet build"), 0.9m),
            operationContext,
            CancellationToken.None);
        Assert.True(addLegacyRule.Success);
        var correctionCandidates = await service.ExtractAsync(
            new ExtractMemory(
                space.Id,
                new MemorySourceRef(
                    MemorySourceKind.Conversation,
                    "turn-correction",
                    role: "user",
                    snippet: "这个仓库里 VSIX 不要用 dotnet build",
                    capturedAt: operationContext.Timestamp)),
            operationContext,
            CancellationToken.None);
        var correctionCandidate = Assert.Single(correctionCandidates);
        Assert.Equal("workspace.rule.vsix", correctionCandidate.Key);
        Assert.Equal("rule.semantic-project-rule", correctionCandidate.RuleId);
        var legacyRule = Assert.Single(
            (await service.FilterAsync(new FilterMemory(space.Id, Key: "workspace.rule.vsix"), operationContext, CancellationToken.None)).Records);
        Assert.Equal("VSIX 可以用 dotnet build", legacyRule.Value.StringValue);

        var feedback = await service.RecordFeedbackAsync(
            new RecordMemoryFeedback(activePreference.Id, MemoryMergeDecision.NeedsReview, "偏好应该改成全程中文并先给结论"),
            operationContext,
            CancellationToken.None);
        var supersede = await service.SupersedeAsync(
            new SupersedeMemory(
                activePreference.Id,
                space.Id,
                "preference.user",
                StructuredValue.FromString("默认全程中文并先给结论"),
                "用户纠正偏好"),
            operationContext,
            CancellationToken.None);
        Assert.True(feedback.Success);
        Assert.True(supersede.Success);
        Assert.Equal(MemoryMutationEffect.Superseded, supersede.Effect);

        var overlayAfterSupersede = await service.ResolveOverlayAsync(
            new ResolveMemoryOverlay(space.Id),
            habitProfile: null,
            defaultFacts: null,
            operationContext,
            CancellationToken.None);
        Assert.Contains(
            overlayAfterSupersede.Facts,
            static fact => fact.Key == "preference.user" && fact.Value.StringValue == "默认全程中文并先给结论");

        var forgetPreference = await service.ForgetAsync(
            new ForgetMemory(supersede.RecordId, space.Id, "preference.user"),
            operationContext,
            CancellationToken.None);
        var overlayAfterForget = await service.ResolveOverlayAsync(
            new ResolveMemoryOverlay(space.Id),
            habitProfile: null,
            defaultFacts: null,
            operationContext,
            CancellationToken.None);
        Assert.True(forgetPreference.Success);
        Assert.DoesNotContain(overlayAfterForget.Facts, static fact => fact.Key == "preference.user");

        var staleAt = operationContext.Timestamp.AddDays(-120);
        var staleFact = new FactMemoryRecord(
            "workspace.old.preference",
            StructuredValue.FromString("旧偏好"),
            space.Id,
            0.8m,
            staleAt,
            usageCount: 0,
            updatedAt: staleAt);
        await store.UpsertFactAsync(
            space,
            staleFact,
            operationContext.ActorId,
            "unit-test",
            staleAt,
            CancellationToken.None);
        var consolidation = await new MemoryConsolidationWorker(store).RunOnceAsync(
            space.Id,
            operationContext,
            CancellationToken.None,
            new MemoryConsolidationOptions(
                enableLease: false,
                includeForgetProposals: true,
                emitOverlayCacheSnapshot: false));
        var factsAfterConsolidation = await store.ListFactsAsync(space, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(space.Id, CancellationToken.None);

        Assert.True(consolidation.ProposalsCreated >= 1);
        Assert.Contains(audit, static record =>
            record.Operation == MemoryConsolidationWorker.ProposalOperationName
            && record.Metadata["proposalKind"] == "forget_proposal"
            && record.Metadata["permissionBoundary"] == "audit-only");
        var staleFactAfterConsolidation = Assert.Single(factsAfterConsolidation, static fact => fact.Key == "workspace.old.preference");
        Assert.Equal(MemoryLifecycleStatus.Active, staleFactAfterConsolidation.LifecycleStatus);
        Assert.Contains(audit, static record =>
            record.Operation == "promote_memory_candidate"
            && record.Key == "preference.user"
            && record.Metadata["decisionKind"] == MemoryPromotionDecisionKind.AutoPromote.ToString());
        Assert.Contains(audit, static record =>
            record.Operation == "promote_memory_candidate"
            && record.Key == "workspace.rule.vsix"
            && record.Metadata["decisionKind"] == MemoryPromotionDecisionKind.SupersedeProposal.ToString());
        Assert.Contains(audit, record => record.Operation == "approve_memory_review" && record.Key == pendingCandidate.Key);
        Assert.Contains(audit, static record => record.Operation == "record_feedback" && record.Key == "preference.user");
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldApplyContextSignatureFilterBeforeOverlay()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var workspaceSpace = CreateWritableWorkspaceMemorySpace();
        var sessionSpace = new MemorySpace(
            new MemorySpaceId("memory:session:thread-001"),
            MemoryScopeKind.Session,
            "thread-001",
            "Thread Session Memory");
        var service = CreateMemoryService(store, workspaceSpace, sessionSpace);
        var operationContext = CreateMemoryOperationContext();
        var workspaceAdd = await service.AddAsync(
            new AddMemory(workspaceSpace.Id, "preference.shell", StructuredValue.FromString("pwsh")),
            operationContext,
            CancellationToken.None);
        var sessionAdd = await service.AddAsync(
            new AddMemory(sessionSpace.Id, "preference.shell", StructuredValue.FromString("bash")),
            operationContext,
            CancellationToken.None);

        var filtered = await service.FilterAsync(
            new FilterMemory(
                ContextSignature: new MemoryContextSignature(
                    scopeKinds: [MemoryScopeKind.Workspace],
                    excludeRecordIds: [Assert.NotNull(sessionAdd.RecordId)])),
            operationContext,
            CancellationToken.None);
        var excluded = await service.FilterAsync(
            new FilterMemory(
                ContextSignature: new MemoryContextSignature(
                    excludeRecordIds: [Assert.NotNull(workspaceAdd.RecordId), Assert.NotNull(sessionAdd.RecordId)])),
            operationContext,
            CancellationToken.None);

        var fact = Assert.Single(filtered.Records);
        Assert.Equal(workspaceSpace.Id, fact.MemorySpaceId);
        Assert.Empty(excluded.Records);
    }

    [Fact]
    public async Task MemoryProviderRegistry_ShouldRespectBindingsForProviderResolution()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var allowedSpace = CreateWritableWorkspaceMemorySpace();
        var blockedSpace = new MemorySpace(
            new MemorySpaceId("memory:workspace:blocked"),
            MemoryScopeKind.Workspace,
            "blocked",
            "Blocked Workspace Memory");
        var policy = new MemoryPolicyEngine();
        var provider = new TianShuLocalMemoryProvider(store, [allowedSpace, blockedSpace], policy);
        var registry = new MemoryProviderRegistry(
            [provider],
            [
                new MemoryProviderBinding(
                    TianShuLocalMemoryProvider.DefaultProviderId,
                    allowedSpace.Id,
                    MemoryProviderBindingMode.ReadWrite,
                    MemoryProviderCapability.ListSpaces | MemoryProviderCapability.Add | MemoryProviderCapability.Filter)
            ]);
        var service = new DefaultMemoryService(
            registry,
            new MemoryOverlayResolver(policy),
            auditSink: new TianShuLocalMemoryAuditSink(store));
        var operationContext = CreateMemoryOperationContext();

        var allowed = await service.AddAsync(
            new AddMemory(allowedSpace.Id, "workspace.preference", StructuredValue.FromString("cli-first")),
            operationContext,
            CancellationToken.None);
        var blocked = await service.AddAsync(
            new AddMemory(blockedSpace.Id, "workspace.preference", StructuredValue.FromString("blocked")),
            operationContext,
            CancellationToken.None);
        var unsupported = await service.BindProviderAsync(
            new BindMemoryProvider(
                TianShuLocalMemoryProvider.DefaultProviderId,
                blockedSpace.Id,
                MemoryProviderBindingMode.ImportExport,
                MemoryProviderCapability.Import),
            operationContext,
            CancellationToken.None);
        var bind = await service.BindProviderAsync(
            new BindMemoryProvider(
                TianShuLocalMemoryProvider.DefaultProviderId,
                blockedSpace.Id,
                MemoryProviderBindingMode.ReadWrite,
                MemoryProviderCapability.ListSpaces | MemoryProviderCapability.Add | MemoryProviderCapability.Filter),
            operationContext,
            CancellationToken.None);
        var unblocked = await service.AddAsync(
            new AddMemory(blockedSpace.Id, "workspace.preference", StructuredValue.FromString("unblocked")),
            operationContext,
            CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(blockedSpace.Id, CancellationToken.None);

        Assert.True(allowed.Success);
        Assert.False(blocked.Success);
        Assert.Equal("memory_provider_not_found", blocked.DegradedReason);
        Assert.False(unsupported.Success);
        Assert.Equal("memory_provider_capability_not_supported", unsupported.DegradedReason);
        Assert.Equal(MemoryProviderCapability.Import, unsupported.UnsupportedCapability);
        Assert.True(bind.Success);
        Assert.True(unblocked.Success);
        Assert.Contains(registry.Bindings, binding =>
            string.Equals(binding.ProviderId, TianShuLocalMemoryProvider.DefaultProviderId, StringComparison.Ordinal)
            && string.Equals(binding.MemorySpaceId.Value, blockedSpace.Id.Value, StringComparison.Ordinal)
            && binding.AllowedCapabilities.HasFlag(MemoryProviderCapability.Add));
        Assert.Contains(audit, record =>
            record.Operation == "bind_memory_provider"
            && record.Key == TianShuLocalMemoryProvider.DefaultProviderId
            && record.Actor == operationContext.ActorId
            && record.Source == "memory-provider-binding");
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldEnforceProviderBindingSpaceForFeedbackAndCitation()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var allowedSpace = CreateWritableWorkspaceMemorySpace();
        var blockedSpace = new MemorySpace(
            new MemorySpaceId("memory:workspace:blocked"),
            MemoryScopeKind.Workspace,
            "blocked",
            "Blocked Workspace Memory");
        var policy = new MemoryPolicyEngine();
        var provider = new TianShuLocalMemoryProvider(store, [allowedSpace, blockedSpace], policy);
        var service = new DefaultMemoryService(
            new MemoryProviderRegistry(
                [provider],
                [
                    new MemoryProviderBinding(
                        TianShuLocalMemoryProvider.DefaultProviderId,
                        allowedSpace.Id,
                        MemoryProviderBindingMode.ReadWrite,
                        MemoryProviderCapability.Feedback | MemoryProviderCapability.Citation)
                ]),
            new MemoryOverlayResolver(policy),
            auditSink: new TianShuLocalMemoryAuditSink(store));
        var operationContext = CreateMemoryOperationContext();
        var blockedFact = new FactMemoryRecord(
            "workspace.preference",
            StructuredValue.FromString("blocked"),
            blockedSpace.Id,
            recordedAt: operationContext.Timestamp);
        await store.UpsertFactAsync(
            blockedSpace,
            blockedFact,
            operationContext.ActorId,
            "unit-test",
            operationContext.Timestamp,
            CancellationToken.None);

        var feedbackResult = await service.RecordFeedbackAsync(
            new RecordMemoryFeedback(blockedFact.Id, MemoryMergeDecision.NeedsReview, "blocked space feedback"),
            operationContext,
            CancellationToken.None);
        var citationResult = await service.RecordCitationAsync(
            new RecordMemoryCitation(new MemoryCitation([new MemoryCitationEntry(blockedFact.Id, blockedSpace.Id, blockedFact.Key)])),
            operationContext,
            CancellationToken.None);
        var facts = await store.ListFactsAsync(blockedSpace, CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(blockedSpace.Id, CancellationToken.None);

        Assert.False(feedbackResult.Success);
        Assert.Equal("memory_provider_not_found", feedbackResult.DegradedReason);
        Assert.Equal(MemoryMutationEffect.Degraded, feedbackResult.Effect);
        Assert.False(citationResult.Success);
        Assert.Equal("memory_provider_not_found", citationResult.DegradedReason);
        Assert.Equal(MemoryMutationEffect.Degraded, citationResult.Effect);
        var fact = Assert.Single(facts);
        Assert.Equal(0, fact.UsageCount);
        Assert.DoesNotContain(audit, static record => record.Operation == "record_feedback");
        Assert.DoesNotContain(audit, static record => record.Operation == "record_citation");
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldDegradeUnreachableProvider()
    {
        var space = CreateWritableWorkspaceMemorySpace();
        var service = new DefaultMemoryService(new MemoryProviderRegistry([new ThrowingMemoryProvider(space)]));
        var operationContext = CreateMemoryOperationContext();

        var filter = await service.FilterAsync(new FilterMemory(space.Id), operationContext, CancellationToken.None);
        var export = await service.ExportAsync(
            new ExportMemory(space.Id),
            operationContext,
            CancellationToken.None);
        var add = await service.AddAsync(
            new AddMemory(space.Id, "workspace.preference", StructuredValue.FromString("cli-first")),
            operationContext,
            CancellationToken.None);
        var bind = await service.BindProviderAsync(
            new BindMemoryProvider(
                ThrowingMemoryProvider.ProviderId,
                space.Id,
                MemoryProviderBindingMode.ReadWrite,
                MemoryProviderCapability.ListSpaces | MemoryProviderCapability.Add | MemoryProviderCapability.Filter),
            operationContext,
            CancellationToken.None);

        Assert.Empty(filter.Records);
        Assert.Contains($"memory_provider_unreachable:{ThrowingMemoryProvider.ProviderId}", filter.DegradedProviders);
        Assert.Contains($"memory_provider_unreachable:{ThrowingMemoryProvider.ProviderId}", export.DegradedProviders);
        Assert.False(add.Success);
        Assert.Equal("memory_provider_unreachable", add.DegradedReason);
        Assert.Equal(MemoryProviderCapability.Add, add.UnsupportedCapability);
        Assert.Equal(MemoryMutationEffect.Degraded, add.Effect);
        Assert.False(bind.Success);
        Assert.Equal("memory_provider_unreachable", bind.DegradedReason);
        Assert.Equal(MemoryMutationEffect.Degraded, bind.Effect);
    }

    [Fact]
    public async Task MemoryProviderRegistry_ShouldEmitResolutionDiagnosticsThroughOperationScope()
    {
        var space = CreateWritableWorkspaceMemorySpace();
        var sink = new RecordingDiagnosticEventSink();
        var registry = new MemoryProviderRegistry(
            [new ThrowingMemoryProvider(space)],
            diagnosticEventSink: sink,
            diagnosticOperationScopeFactory: new FixedDiagnosticOperationScopeFactory("memory-op-fixed"));
        var service = new DefaultMemoryService(registry);
        var operationContext = CreateMemoryOperationContext();

        _ = await service.FilterAsync(new FilterMemory(space.Id), operationContext, CancellationToken.None);

        var diagnosticEvent = Assert.Single(sink.Events);
        Assert.Equal("memory/provider_resolution/stats", diagnosticEvent.EventName);
        Assert.Equal("memory-op-fixed", diagnosticEvent.Operation?.OperationId);
        Assert.Equal("memory.provider_resolution", diagnosticEvent.Operation?.OperationKind);
        Assert.True(diagnosticEvent.Metadata.TryGetValue("diagnosticModule", out var module));
        Assert.True(diagnosticEvent.Metadata.TryGetValue("status", out var status));
        Assert.Equal("memory", module.StringValue);
        Assert.Equal("degraded", status.StringValue);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldEmitMutationDiagnosticsThroughOperationScope()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var policy = new MemoryPolicyEngine();
        var provider = new TianShuLocalMemoryProvider(store, [space], policy);
        var sink = new RecordingDiagnosticEventSink();
        var operationFactory = new FixedDiagnosticOperationScopeFactory("memory-op-fixed");
        var service = new DefaultMemoryService(
            new MemoryProviderRegistry([provider], diagnosticEventSink: sink, diagnosticOperationScopeFactory: operationFactory),
            new MemoryOverlayResolver(policy),
            auditSink: new TianShuLocalMemoryAuditSink(store),
            policy: policy,
            diagnosticEventSink: sink,
            diagnosticOperationScopeFactory: operationFactory);
        var operationContext = CreateMemoryOperationContext();

        var result = await service.AddAsync(
            new AddMemory(space.Id, "workspace.preference", StructuredValue.FromString("diagnostics")),
            operationContext,
            CancellationToken.None);

        Assert.True(result.Success);
        var diagnosticEvent = sink.Events.Single(item => item.EventName == "memory/mutation/stats");
        Assert.Equal("memory-op-fixed", diagnosticEvent.Operation?.OperationId);
        Assert.True(diagnosticEvent.Payload.TryGetProperty("operationName", out var operationName));
        Assert.True(diagnosticEvent.Payload.TryGetProperty("success", out var success));
        Assert.NotNull(operationName);
        Assert.NotNull(success);
        Assert.Equal("add_memory", operationName.StringValue);
        Assert.True(success.BooleanValue);
    }

    [Fact]
    public async Task DefaultMemoryService_ShouldEmitGovernanceMetadataForHighRiskMemoryPromotionDiagnostics()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var space = CreateWritableWorkspaceMemorySpace();
        var policy = new MemoryPolicyEngine();
        var provider = new TianShuLocalMemoryProvider(store, [space], policy);
        var sink = new RecordingDiagnosticEventSink();
        var operationFactory = new FixedDiagnosticOperationScopeFactory("memory-op-fixed");
        var service = new DefaultMemoryService(
            new MemoryProviderRegistry([provider], diagnosticEventSink: sink, diagnosticOperationScopeFactory: operationFactory),
            new MemoryOverlayResolver(policy),
            auditSink: new TianShuLocalMemoryAuditSink(store),
            policy: policy,
            diagnosticEventSink: sink,
            diagnosticOperationScopeFactory: operationFactory);
        await service.AddAsync(
            new AddMemory(space.Id, "preference.default", StructuredValue.FromString("用英文汇报"), 0.9m),
            CreateMemoryOperationContext(),
            CancellationToken.None);

        await service.ExtractAsync(
            new ExtractMemory(
                space.Id,
                new MemorySourceRef(
                    MemorySourceKind.Conversation,
                    "thread-governance-diagnostics",
                    role: "user",
                    snippet: "以后默认用中文汇报")),
            CreateMemoryOperationContext(),
            CancellationToken.None);

        var diagnosticEvent = Assert.Single(sink.Events, static item =>
            item.EventName == "memory/mutation/stats"
            && item.Payload.TryGetProperty("operationName", out var operationName)
            && operationName.StringValue! == "promote_memory_candidate");
        Assert.True(diagnosticEvent.Payload.TryGetProperty("governanceCheckpointKind", out var checkpointKind));
        Assert.True(diagnosticEvent.Payload.TryGetProperty("riskSource", out var riskSource));
        Assert.True(diagnosticEvent.Payload.TryGetProperty("approvalQueueProjection", out var approvalQueueProjection));
        Assert.True(diagnosticEvent.Payload.TryGetProperty("userDecision", out var userDecision));
        Assert.True(diagnosticEvent.Payload.TryGetProperty("executionResult", out var executionResult));
        Assert.Equal("Approval", checkpointKind.StringValue!);
        Assert.Equal("policy_rule", riskSource.StringValue!);
        Assert.Equal("memory.review", approvalQueueProjection.StringValue!);
        Assert.Equal("pending", userDecision.StringValue!);
        Assert.Equal("not_executed", executionResult.StringValue!);
    }

    [Fact]
    public void DefaultMemoryService_ShouldListProvidersByScopeWithDescriptorMetadata()
    {
        var workspaceSpace = CreateWritableWorkspaceMemorySpace();
        var sessionSpace = new MemorySpace(
            new MemorySpaceId("memory:session:thread-001"),
            MemoryScopeKind.Session,
            "thread-001",
            "Thread Memory");
        var localProvider = new TianShuLocalMemoryProvider(
            new InMemoryTianShuLocalMemoryStore(),
            [workspaceSpace, sessionSpace]);
        var service = new DefaultMemoryService(new MemoryProviderRegistry(
            [localProvider, new ReadOnlyMemoryProvider(workspaceSpace)]));

        var allProviders = service.ListProviders(new ListMemoryProviders());
        var sessionProviders = service.ListProviders(new ListMemoryProviders(MemoryScopeKind.Session));
        var localDescriptor = Assert.Single(sessionProviders);

        Assert.Equal(2, allProviders.Count);
        Assert.Equal(TianShuLocalMemoryProvider.DefaultProviderId, localDescriptor.ProviderId);
        Assert.Equal(MemoryProviderTrustLevel.BuiltIn, localDescriptor.TrustLevel);
        Assert.Contains(MemoryLifecycleStatus.Forgotten, localDescriptor.SupportedLifecycleStatuses);
        Assert.True(localDescriptor.Capabilities.HasFlag(MemoryProviderCapability.KeywordSearch));
        Assert.False(localDescriptor.Capabilities.HasFlag(MemoryProviderCapability.SemanticSearch));
        Assert.True(localDescriptor.Capabilities.HasFlag(MemoryProviderCapability.ReadOnlyAccess));
        Assert.True(localDescriptor.Capabilities.HasFlag(MemoryProviderCapability.ReadWriteAccess));
        Assert.True(localDescriptor.Features.HasFlag(MemoryProviderFeature.SourceTracking));
        Assert.True(localDescriptor.Features.HasFlag(MemoryProviderFeature.SecretRedaction));
        Assert.Equal(MemoryProviderDegradationStrategy.UnsupportedResult, localDescriptor.DegradationStrategy);
    }

    [Fact]
    public async Task TianShuExecutionRuntime_ShouldUseInjectedIdentityMemoryPlane()
    {
        var plane = new RecordingIdentityMemoryPlane();
        var sut = new TianShuExecutionRuntime(plane);
        var memorySpaceId = new MemorySpaceId("memory-injected");
        var memoryRecordId = new MemoryRecordId("memory-record:injected");
        var citation = new MemoryCitation(
        [
            new MemoryCitationEntry(memoryRecordId, memorySpaceId, "preference.shell")
        ]);

        var account = await sut.GetAccountProfileAsync(
            new GetAccountProfile(new AccountId("account-injected")),
            CancellationToken.None);
        var spaces = await sut.ListMemorySpacesAsync(new ListMemorySpaces(), CancellationToken.None);
        var providers = await sut.ListMemoryProvidersAsync(new ListMemoryProviders(MemoryScopeKind.User), CancellationToken.None);
        var queryResult = await sut.FilterMemoryAsync(new FilterMemory(MemorySpaceId: memorySpaceId), CancellationToken.None);
        var addResult = await sut.AddMemoryAsync(
            new AddMemory(memorySpaceId, "preference.shell", StructuredValue.FromString("pwsh")),
            CancellationToken.None);
        var candidates = await sut.ExtractMemoryAsync(
            new ExtractMemory(
                memorySpaceId,
                new MemorySourceRef(MemorySourceKind.Conversation, "turn-001"),
                StructuredValue.FromString("记住我更喜欢 pwsh")),
            CancellationToken.None);
        var importResult = await sut.ImportMemoryAsync(
            new ImportMemory(memorySpaceId, new MemorySourceRef(MemorySourceKind.File, "memory.json")),
            CancellationToken.None);
        var exportResult = await sut.ExportMemoryAsync(new ExportMemory(memorySpaceId), CancellationToken.None);
        var bindResult = await sut.BindMemoryProviderAsync(
            new BindMemoryProvider(
                "provider-injected",
                memorySpaceId,
                MemoryProviderBindingMode.ReadWrite,
                MemoryProviderCapability.Add | MemoryProviderCapability.Filter),
            CancellationToken.None);
        var forgetResult = await sut.ForgetMemoryAsync(new ForgetMemory(memoryRecordId), CancellationToken.None);
        var deleteResult = await sut.DeleteMemoryAsync(new DeleteMemory(memoryRecordId), CancellationToken.None);
        var supersedeResult = await sut.SupersedeMemoryAsync(
            new SupersedeMemory(
                memoryRecordId,
                memorySpaceId,
                "preference.shell",
                StructuredValue.FromString("pwsh-preview"),
                "corrected"),
            CancellationToken.None);
        var approveResult = await sut.ApproveMemoryReviewAsync(
            new ApproveMemoryReview(memoryRecordId, memorySpaceId, "preference.shell", "accepted"),
            CancellationToken.None);
        var feedbackResult = await sut.RecordMemoryFeedbackAsync(
            new RecordMemoryFeedback(memoryRecordId, MemoryMergeDecision.Applied, "accepted"),
            CancellationToken.None);
        var citationResult = await sut.RecordMemoryCitationAsync(new RecordMemoryCitation(citation), CancellationToken.None);

        Assert.Equal(1, plane.GetAccountProfileCalls);
        Assert.Equal(1, plane.ListMemorySpacesCalls);
        Assert.Equal(1, plane.ListMemoryProvidersCalls);
        Assert.Equal(1, plane.FilterMemoryCalls);
        Assert.Equal(1, plane.AddMemoryCalls);
        Assert.Equal(1, plane.ExtractMemoryCalls);
        Assert.Equal(1, plane.ImportMemoryCalls);
        Assert.Equal(1, plane.ExportMemoryCalls);
        Assert.Equal(1, plane.BindMemoryProviderCalls);
        Assert.Equal(1, plane.ForgetMemoryCalls);
        Assert.Equal(1, plane.DeleteMemoryCalls);
        Assert.Equal(1, plane.SupersedeMemoryCalls);
        Assert.Equal(1, plane.ApproveMemoryReviewCalls);
        Assert.Equal(1, plane.RecordMemoryFeedbackCalls);
        Assert.Equal(1, plane.RecordMemoryCitationCalls);
        Assert.Equal("Injected Account", account?.DisplayName);
        Assert.Single(spaces);
        Assert.Equal("memory-injected", spaces[0].Id.Value);
        Assert.Single(providers);
        Assert.Single(queryResult.Records);
        Assert.Equal(memoryRecordId, addResult.RecordId);
        Assert.Single(candidates);
        Assert.True(importResult.Success);
        Assert.Single(exportResult.Records);
        Assert.True(bindResult.Success);
        Assert.Equal(MemoryLifecycleStatus.Forgotten, forgetResult.LifecycleStatus);
        Assert.Equal(MemoryLifecycleStatus.Deleted, deleteResult.LifecycleStatus);
        Assert.Equal(MemoryMutationEffect.Superseded, supersedeResult.Effect);
        Assert.Equal(MemoryLifecycleStatus.Active, approveResult.LifecycleStatus);
        Assert.True(feedbackResult.Success);
        Assert.True(citationResult.Success);
    }

    [Fact]
    public async Task TianShuExecutionRuntime_DefaultMemoryPlane_ShouldPersistThroughTianShuHome()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalSystemConfigRoot = Environment.GetEnvironmentVariable("TIANSHU_SYSTEM_CONFIG_ROOT");
        var root = Path.Combine(Path.GetTempPath(), $"tianshu-runtime-memory-{Guid.NewGuid():N}");
        MemorySpaceId memorySpaceId;

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", root);
            Environment.SetEnvironmentVariable("TIANSHU_SYSTEM_CONFIG_ROOT", root);

            await using (var writer = new TianShuExecutionRuntime())
            {
                await writer.InitializeAsync(
                    CreateMemoryRuntimeInitializeCommand(root),
                    dynamicToolCallHandler: null,
                    CancellationToken.None);
                var writableWorkspace = (await writer.ListMemorySpacesAsync(new ListMemorySpaces(MemoryScopeKind.Workspace), CancellationToken.None))
                    .First(static space => !space.IsReadOnly);
                memorySpaceId = writableWorkspace.Id;
                var providers = await writer.ListMemoryProvidersAsync(new ListMemoryProviders(), CancellationToken.None);
                var addResult = await writer.AddMemoryAsync(
                    new AddMemory(memorySpaceId, "workspace.runtime", StructuredValue.FromString("filesystem-backed")),
                    CancellationToken.None);

                Assert.True(
                    addResult.Success,
                    $"space={memorySpaceId.Value}; providers={string.Join(",", providers.Select(static provider => $"{provider.ProviderId}:{provider.Capabilities}"))}; reason={addResult.DegradedReason ?? addResult.UnsupportedCapability?.ToString()}");
            }

            await using var reader = new TianShuExecutionRuntime();
            await reader.InitializeAsync(
                CreateMemoryRuntimeInitializeCommand(root),
                dynamicToolCallHandler: null,
                CancellationToken.None);
            var queryResult = await reader.FilterMemoryAsync(new FilterMemory(memorySpaceId), CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(root, "data", "memory", "spaces.json")));
            var record = Assert.Single(queryResult.Records);
            Assert.Equal("workspace.runtime", record.Key);
            Assert.Equal("filesystem-backed", record.Value.StringValue);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_SYSTEM_CONFIG_ROOT", originalSystemConfigRoot);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ControlPlaneInitializeRuntimeCommand CreateMemoryRuntimeInitializeCommand(string root)
        => new()
        {
            AppHostProjectPath = Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost", "TianShu.AppHost.csproj"),
            WorkingDirectory = root,
            CreateThreadOnInitialize = false,
            UseIsolatedSessionStorage = true,
            IsolatedSessionStorageRoot = Path.Combine(root, "sessions"),
            StartupTimeout = TimeSpan.FromSeconds(30),
            RequestTimeout = TimeSpan.FromSeconds(30),
        };

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TianShu.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 TianShu.sln。");
    }

    [Fact]
    public async Task DefaultTianShuIdentityMemoryPlane_ShouldKeepProviderBindingAcrossCalls()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var sut = new DefaultTianShuIdentityMemoryPlane(store);
        var context = CreateContext();
        var workspaceSpaceId = new MemorySpaceId($"memory:workspace:{NormalizeSegmentForTest(context.WorkingDirectory!)}");
        var userSpaceId = new MemorySpaceId($"memory:user:{NormalizeSegmentForTest(context.AccountId.Value)}");

        var bind = await sut.BindMemoryProviderAsync(
            new BindMemoryProvider(
                TianShuLocalMemoryProvider.DefaultProviderId,
                workspaceSpaceId,
                MemoryProviderBindingMode.ReadWrite,
                MemoryProviderCapability.ListSpaces | MemoryProviderCapability.Filter),
            context,
            CancellationToken.None);
        var workspaceFilter = await sut.FilterMemoryAsync(new FilterMemory(workspaceSpaceId), context, CancellationToken.None);
        var userFilter = await sut.FilterMemoryAsync(new FilterMemory(userSpaceId), context, CancellationToken.None);

        Assert.True(bind.Success);
        Assert.DoesNotContain("memory_provider_not_found", workspaceFilter.DegradedProviders);
        Assert.Contains("memory_provider_not_found", userFilter.DegradedProviders);
    }

    [Fact]
    public async Task DefaultTianShuIdentityMemoryPlane_ShouldRunConsolidationThroughFormalBoundary()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var sink = new RecordingDiagnosticEventSink();
        var sut = new DefaultTianShuIdentityMemoryPlane(
            store,
            diagnosticEventSink: sink,
            diagnosticOperationScopeFactory: new FixedDiagnosticOperationScopeFactory("memory-consolidation-op"));
        var context = CreateContext();
        var workspaceSpaceId = new MemorySpaceId($"memory:workspace:{NormalizeSegmentForTest(context.WorkingDirectory!)}");
        await store.UpsertCandidateAsync(
            new MemoryCandidate(
                "workspace.cli",
                StructuredValue.FromString("tianshu"),
                workspaceSpaceId,
                0.8m,
                new MemorySourceRef(MemorySourceKind.Conversation, "thread-consolidation")),
            CancellationToken.None);

        var result = await sut.RunMemoryConsolidationAsync(
            new RunMemoryConsolidation(workspaceSpaceId, EnableLease: false),
            context,
            CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(workspaceSpaceId, CancellationToken.None);

        Assert.Equal(1, result.CandidatesScanned);
        Assert.Equal(1, result.ProposalsCreated);
        Assert.Equal("audit-only", result.PermissionBoundary);
        Assert.Contains(audit, static record =>
            record.Operation == MemoryConsolidationWorker.ProposalOperationName
            && record.Metadata["permissionBoundary"] == "audit-only");
        var diagnosticEvent = Assert.Single(sink.Events, static item => item.EventName == "memory/consolidation/stats");
        Assert.Equal("memory-consolidation-op", diagnosticEvent.Operation?.OperationId);
        Assert.True(diagnosticEvent.Payload.TryGetProperty("proposalsCreated", out var proposalsCreated));
        Assert.Equal("1", proposalsCreated.NumberValue);
    }

    [Fact]
    public async Task DefaultTianShuIdentityMemoryPlane_ShouldApplyProfileRetentionToConsolidation()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var sut = new DefaultTianShuIdentityMemoryPlane(
            store,
            memoryOptionsResolver: _ => new TianShuMemoryRuntimeOptions
            {
                Profiles =
                [
                    new TianShuMemoryProfileOptions("workspace", Retention: "forget")
                ],
            });
        var context = CreateContext();
        var workspaceSpace = CreateWritableWorkspaceMemorySpace();
        var staleAt = context.SnapshotTime.AddDays(-120);
        await store.UpsertFactAsync(
            workspaceSpace,
            new FactMemoryRecord(
                "workspace.old.preference",
                StructuredValue.FromString("旧偏好"),
                workspaceSpace.Id,
                0.8m,
                staleAt,
                usageCount: 0,
                updatedAt: staleAt),
            context.AccountId.Value,
            "unit-test",
            staleAt,
            CancellationToken.None);

        var result = await sut.RunMemoryConsolidationAsync(
            new RunMemoryConsolidation(workspaceSpace.Id, EnableLease: false),
            context,
            CancellationToken.None);
        var audit = await store.ListAuditRecordsAsync(workspaceSpace.Id, CancellationToken.None);
        var facts = await store.ListFactsAsync(workspaceSpace, CancellationToken.None);

        Assert.Equal(1, result.ProposalsCreated);
        Assert.Contains(audit, static record =>
            record.Operation == MemoryConsolidationWorker.ProposalOperationName
            && record.Metadata["proposalKind"] == "forget_proposal");
        Assert.Equal(MemoryLifecycleStatus.Active, Assert.Single(facts).LifecycleStatus);
    }

    [Fact]
    public async Task MemoryReview_ShouldExposeAndApproveArchiveProposal()
    {
        var store = new InMemoryTianShuLocalMemoryStore();
        var sut = new DefaultTianShuIdentityMemoryPlane(store);
        var context = CreateContext();
        var workspaceSpace = CreateWritableWorkspaceMemorySpace();
        var staleAt = context.SnapshotTime.AddDays(-120);
        var staleRecordId = new MemoryRecordId("memory-record-stale-archive");
        await store.UpsertFactAsync(
            workspaceSpace,
            new FactMemoryRecord(
                "workspace.old.preference",
                StructuredValue.FromString("旧偏好"),
                workspaceSpace.Id,
                0.8m,
                staleAt,
                staleRecordId,
                usageCount: 0,
                updatedAt: staleAt),
            context.AccountId.Value,
            "unit-test",
            staleAt,
            CancellationToken.None);

        await sut.RunMemoryConsolidationAsync(
            new RunMemoryConsolidation(workspaceSpace.Id, EnableLease: false, IncludeArchiveProposals: true),
            context,
            CancellationToken.None);
        var reviews = await sut.ListMemoryReviewsAsync(
            new ListMemoryReviews(workspaceSpace.Id),
            context,
            CancellationToken.None);
        var proposal = Assert.Single(reviews.Items);
        var proposalAudit = Assert.Single(proposal.Audit);

        Assert.StartsWith("memory-proposal-", proposal.Record.Id.Value, StringComparison.Ordinal);
        Assert.Equal("archive_proposal", proposalAudit.Metadata["proposalKind"]);
        Assert.Equal(staleRecordId.Value, proposalAudit.Metadata["targetRecordId"]);

        var approve = await sut.ApproveMemoryReviewAsync(
            new ApproveMemoryReview(proposal.Record.Id, workspaceSpace.Id, proposal.Record.Key, "接受归档提案"),
            context,
            CancellationToken.None);
        var facts = await store.ListFactsAsync(workspaceSpace, CancellationToken.None);
        var remainingReviews = await sut.ListMemoryReviewsAsync(
            new ListMemoryReviews(workspaceSpace.Id),
            context,
            CancellationToken.None);

        Assert.True(approve.Success);
        Assert.Equal(staleRecordId, approve.RecordId);
        Assert.Equal(MemoryLifecycleStatus.Archived, Assert.Single(facts).LifecycleStatus);
        Assert.Empty(remainingReviews.Items);
    }

    [Fact]
    public async Task FileSystemTianShuLocalMemoryStore_ShouldPersistProviderBindingsAcrossPlaneReload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tianshu-memory-bindings-{Guid.NewGuid():N}");
        var context = CreateContext();
        var workspaceSpaceId = new MemorySpaceId($"memory:workspace:{NormalizeSegmentForTest(context.WorkingDirectory!)}");
        var userSpaceId = new MemorySpaceId($"memory:user:{NormalizeSegmentForTest(context.AccountId.Value)}");

        try
        {
            var writer = new DefaultTianShuIdentityMemoryPlane(new FileSystemTianShuLocalMemoryStore(root));
            var bind = await writer.BindMemoryProviderAsync(
                new BindMemoryProvider(
                    TianShuLocalMemoryProvider.DefaultProviderId,
                    workspaceSpaceId,
                    MemoryProviderBindingMode.ReadWrite,
                    MemoryProviderCapability.ListSpaces | MemoryProviderCapability.Filter),
                context,
                CancellationToken.None);

            var reader = new DefaultTianShuIdentityMemoryPlane(new FileSystemTianShuLocalMemoryStore(root));
            var userFilter = await reader.FilterMemoryAsync(new FilterMemory(userSpaceId), context, CancellationToken.None);
            var audit = await new FileSystemTianShuLocalMemoryStore(root)
                .ListAuditRecordsAsync(workspaceSpaceId, CancellationToken.None);

            Assert.True(bind.Success);
            Assert.True(File.Exists(Path.Combine(root, "memory", "provider-bindings.json")));
            Assert.Contains("memory_provider_not_found", userFilter.DegradedProviders);
            Assert.Contains(audit, static record => record.Operation == "bind_memory_provider");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static TianShuIdentityMemoryContext CreateContext()
        => new(
            runtimeName: "tianshu",
            accountId: new AccountId("local-account:semi"),
            accountDisplayName: "Example",
            deviceName: "Example-PC",
            platform: "Windows",
            workingDirectory: @"D:\Work\TianShu",
            activeThreadId: "thread-001",
            teamKey: "platform",
            collaborationSpaceId: "space-platform",
            preferredVerbosity: "high",
            preferredTools: ["shell_command", "apply_patch"],
            snapshotTime: new DateTimeOffset(2026, 4, 28, 9, 0, 0, TimeSpan.Zero));

    private static MemoryOperationContext CreateMemoryOperationContext()
        => new("local-account:semi", timestamp: new DateTimeOffset(2026, 4, 30, 9, 0, 0, TimeSpan.Zero));

    private static MemorySpace CreateWritableWorkspaceMemorySpace()
        => new(
            new MemorySpaceId("memory:workspace:d/gitrepos/personal/TianShu"),
            MemoryScopeKind.Workspace,
            @"D:\Work\TianShu",
            "TianShu Workspace Memory");

    private static string NormalizeSegmentForTest(string value)
    {
        var normalized = value
            .Trim()
            .Replace('\\', '/')
            .Replace(' ', '-')
            .ToLowerInvariant();

        if (normalized.Length >= 3
            && char.IsLetter(normalized[0])
            && normalized[1] == ':'
            && normalized[2] == '/')
        {
            normalized = normalized[0] + normalized[2..];
        }

        return normalized.Replace(':', '_');
    }

    private static FactMemoryRecord CreateFact(
        string key,
        string value,
        MemorySpaceId memorySpaceId,
        DateTimeOffset recordedAt)
        => new(
            key,
            StructuredValue.FromString(value),
            memorySpaceId,
            recordedAt: recordedAt);

    private static DefaultMemoryService CreateMemoryService(
        ITianShuLocalMemoryStore store,
        params MemorySpace[] spaces)
    {
        var policy = new MemoryPolicyEngine();
        var provider = new TianShuLocalMemoryProvider(store, spaces, policy);
        return new DefaultMemoryService(
            new MemoryProviderRegistry([provider]),
            new MemoryOverlayResolver(policy),
            auditSink: new TianShuLocalMemoryAuditSink(store),
            policy: policy);
    }

    private sealed class LoopbackMemoryProviderServer : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly TcpListener listener;
        private readonly Func<string, string, object> handler;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task loop;
        private readonly List<string> requestPaths = [];
        private readonly List<IReadOnlyDictionary<string, string>> requestHeaders = [];

        private LoopbackMemoryProviderServer(TcpListener listener, Func<string, string, object> handler)
        {
            this.listener = listener;
            this.handler = handler;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            loop = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        public IReadOnlyList<string> RequestPaths
        {
            get
            {
                lock (requestPaths)
                {
                    return requestPaths.ToArray();
                }
            }
        }

        public IReadOnlyList<IReadOnlyDictionary<string, string>> RequestHeaders
        {
            get
            {
                lock (requestHeaders)
                {
                    return requestHeaders.ToArray();
                }
            }
        }

        public static Task<LoopbackMemoryProviderServer> StartAsync(Func<string, string, object> handler)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new LoopbackMemoryProviderServer(listener, handler));
        }

        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel();
            listener.Stop();
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
            }

            cancellation.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellation.Token).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client), cancellation.Token);
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using var _ = client;
            using var stream = client.GetStream();
            var requestText = await ReadRequestAsync(stream, cancellation.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestText))
            {
                return;
            }

            var (path, body, headers) = ParseRequest(requestText);
            lock (requestPaths)
            {
                requestPaths.Add(path);
            }

            lock (requestHeaders)
            {
                requestHeaders.Add(headers);
            }

            var response = handler(path, body);
            var responseJson = JsonSerializer.Serialize(response, JsonOptions);
            var responseBytes = Encoding.UTF8.GetBytes(responseJson);
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {responseBytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, cancellation.Token).ConfigureAwait(false);
            await stream.WriteAsync(responseBytes, cancellation.Token).ConfigureAwait(false);
        }

        private static async Task<string> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            var builder = new StringBuilder();
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                builder.Append(Encoding.UTF8.GetString(buffer, 0, read));
                var text = builder.ToString();
                var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd < 0)
                {
                    continue;
                }

                var contentLength = ReadContentLength(text[..headerEnd]);
                var bodyLength = Encoding.UTF8.GetByteCount(text[(headerEnd + 4)..]);
                if (bodyLength >= contentLength)
                {
                    break;
                }
            }

            return builder.ToString();
        }

        private static int ReadContentLength(string headers)
        {
            foreach (var line in headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = line.IndexOf(':', StringComparison.Ordinal);
                if (separator > 0
                    && string.Equals(line[..separator], "Content-Length", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(line[(separator + 1)..].Trim(), out var length))
                {
                    return length;
                }
            }

            return 0;
        }

        private static (string Path, string Body, IReadOnlyDictionary<string, string> Headers) ParseRequest(string requestText)
        {
            var firstLineEnd = requestText.IndexOf("\r\n", StringComparison.Ordinal);
            var firstLine = firstLineEnd < 0 ? requestText : requestText[..firstLineEnd];
            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var path = parts.Length >= 2 ? parts[1] : "/";
            var bodyStart = requestText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var body = bodyStart < 0 ? string.Empty : requestText[(bodyStart + 4)..];
            var headers = ParseHeaders(firstLineEnd < 0 ? string.Empty : requestText[..Math.Max(0, bodyStart)]);
            return (path, body, headers);
        }

        private static IReadOnlyDictionary<string, string> ParseHeaders(string headerText)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                var separator = line.IndexOf(':', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    continue;
                }

                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            return headers;
        }
    }

    private sealed class ProposalFailingMemoryStore(ITianShuLocalMemoryStore inner) : ITianShuLocalMemoryStore
    {
        public Task<IReadOnlyList<MemorySpace>> ListSpacesAsync(MemorySpaceId? memorySpaceId, CancellationToken cancellationToken)
            => inner.ListSpacesAsync(memorySpaceId, cancellationToken);

        public Task<IReadOnlyList<FactMemoryRecord>> ListFactsAsync(MemorySpace memorySpace, CancellationToken cancellationToken)
            => inner.ListFactsAsync(memorySpace, cancellationToken);

        public Task<FactMemoryRecord> UpsertFactAsync(
            MemorySpace memorySpace,
            string key,
            StructuredValue value,
            TianShuIdentityMemoryContext context,
            string source,
            decimal confidence,
            CancellationToken cancellationToken)
            => inner.UpsertFactAsync(memorySpace, key, value, context, source, confidence, cancellationToken);

        public Task<FactMemoryRecord> UpsertFactAsync(
            MemorySpace memorySpace,
            FactMemoryRecord fact,
            string actor,
            string source,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken)
            => inner.UpsertFactAsync(memorySpace, fact, actor, source, occurredAt, cancellationToken);

        public Task<FactMemoryRecord?> ChangeFactLifecycleAsync(
            MemorySpace memorySpace,
            MemoryRecordId? memoryRecordId,
            string? key,
            MemoryLifecycleStatus lifecycleStatus,
            string actor,
            string source,
            DateTimeOffset occurredAt,
            string? reason,
            CancellationToken cancellationToken)
            => inner.ChangeFactLifecycleAsync(memorySpace, memoryRecordId, key, lifecycleStatus, actor, source, occurredAt, reason, cancellationToken);

        public Task AppendAuditRecordAsync(TianShuMemoryAuditRecord auditRecord, CancellationToken cancellationToken)
            => auditRecord.Operation == MemoryConsolidationWorker.ProposalOperationName
                ? throw new InvalidOperationException("proposal audit append failed")
                : inner.AppendAuditRecordAsync(auditRecord, cancellationToken);

        public Task<IReadOnlyList<TianShuMemoryAuditRecord>> ListAuditRecordsAsync(MemorySpaceId? memorySpaceId, CancellationToken cancellationToken)
            => inner.ListAuditRecordsAsync(memorySpaceId, cancellationToken);

        public Task AppendEvidenceRecordAsync(MemoryEvidenceRecord evidenceRecord, CancellationToken cancellationToken)
            => inner.AppendEvidenceRecordAsync(evidenceRecord, cancellationToken);

        public Task<IReadOnlyList<MemoryEvidenceRecord>> ListEvidenceRecordsAsync(MemorySpaceId? memorySpaceId, CancellationToken cancellationToken)
            => inner.ListEvidenceRecordsAsync(memorySpaceId, cancellationToken);

        public Task UpsertCandidateAsync(MemoryCandidate candidate, CancellationToken cancellationToken)
            => inner.UpsertCandidateAsync(candidate, cancellationToken);

        public Task<IReadOnlyList<MemoryCandidate>> ListCandidatesAsync(MemorySpaceId? memorySpaceId, CancellationToken cancellationToken)
            => inner.ListCandidatesAsync(memorySpaceId, cancellationToken);

        public Task AppendSupersedeLinkAsync(MemorySpaceId memorySpaceId, MemorySupersedeLink link, CancellationToken cancellationToken)
            => inner.AppendSupersedeLinkAsync(memorySpaceId, link, cancellationToken);

        public Task<IReadOnlyList<MemorySupersedeLink>> ListSupersedeLinksAsync(MemorySpaceId? memorySpaceId, CancellationToken cancellationToken)
            => inner.ListSupersedeLinksAsync(memorySpaceId, cancellationToken);

        public Task<IReadOnlyList<MemoryProviderBinding>> ListProviderBindingsAsync(CancellationToken cancellationToken)
            => inner.ListProviderBindingsAsync(cancellationToken);

        public Task ReplaceProviderBindingsAsync(
            IReadOnlyList<MemoryProviderBinding> bindings,
            CancellationToken cancellationToken)
            => inner.ReplaceProviderBindingsAsync(bindings, cancellationToken);
    }

    private sealed class ReadOnlyMemoryProvider : IMemoryProvider
    {
        private readonly MemorySpace space;

        public ReadOnlyMemoryProvider(MemorySpace space)
        {
            this.space = space;
        }

        public MemoryProviderDescriptor Descriptor { get; } = new(
            "readonly",
            "Read-only Memory",
            "1.0",
            MemoryProviderCapability.ListSpaces | MemoryProviderCapability.Filter,
            [MemoryScopeKind.Workspace]);

        public Task<IReadOnlyList<MemorySpace>> ListSpacesAsync(MemorySpaceId? memorySpaceId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MemorySpace>>(
                memorySpaceId is null || string.Equals(memorySpaceId.Value.Value, space.Id.Value, StringComparison.Ordinal)
                    ? [space]
                    : []);

        public Task<MemoryMutationResult> AddAsync(AddMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Add, Effect: MemoryMutationEffect.Degraded));

        public Task<MemoryMutationResult> ImportAsync(ImportMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Import, Effect: MemoryMutationEffect.Degraded));

        public Task<MemoryQueryResult> ExportAsync(ExportMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryQueryResult(degradedProviders: ["unsupported_capability:export"]));

        public Task<MemoryQueryResult> FilterAsync(FilterMemory query, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryQueryResult());

        public Task<MemoryMutationResult> ForgetAsync(ForgetMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Forget, Effect: MemoryMutationEffect.Degraded));

        public Task<MemoryMutationResult> DeleteAsync(DeleteMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Delete, Effect: MemoryMutationEffect.Degraded));

        public Task<MemoryMutationResult> SupersedeAsync(SupersedeMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Supersede, Effect: MemoryMutationEffect.Degraded));

        public Task<MemoryMutationResult> ApproveReviewAsync(ApproveMemoryReview command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Review, Effect: MemoryMutationEffect.Degraded));

        public Task<MemoryMutationResult> RecordFeedbackAsync(RecordMemoryFeedback command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Feedback, Effect: MemoryMutationEffect.Degraded));

        public Task<MemoryMutationResult> RecordCitationAsync(RecordMemoryCitation command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Citation, Effect: MemoryMutationEffect.Degraded));
    }

    private sealed class ThrowingMemoryProvider : IMemoryProvider
    {
        public const string ProviderId = "tianshu.throwing-memory";

        private readonly MemorySpace space;

        public ThrowingMemoryProvider(MemorySpace space)
        {
            this.space = space;
        }

        public MemoryProviderDescriptor Descriptor { get; } = new(
            ProviderId,
            "Throwing Memory Provider",
            "1.0",
            MemoryProviderCapability.ListSpaces
            | MemoryProviderCapability.Add
            | MemoryProviderCapability.Filter
            | MemoryProviderCapability.Export,
            [MemoryScopeKind.Workspace],
            RequiresNetwork: true,
            TrustLevel: MemoryProviderTrustLevel.External,
            DegradationStrategy: MemoryProviderDegradationStrategy.FailClosed);

        public Task<IReadOnlyList<MemorySpace>> ListSpacesAsync(MemorySpaceId? memorySpaceId, CancellationToken cancellationToken)
            => throw new InvalidOperationException($"Simulated unreachable provider for {space.Id.Value}.");

        public Task<MemoryMutationResult> AddAsync(AddMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Add should not be invoked when provider discovery fails.");

        public Task<MemoryMutationResult> ImportAsync(ImportMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Import, Effect: MemoryMutationEffect.Degraded));

        public Task<MemoryQueryResult> ExportAsync(ExportMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Export should not be invoked when provider discovery fails.");

        public Task<MemoryQueryResult> FilterAsync(FilterMemory query, MemoryOperationContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Filter should not be invoked when provider discovery fails.");

        public Task<MemoryMutationResult> ForgetAsync(ForgetMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Forget, Effect: MemoryMutationEffect.Degraded));

        public Task<MemoryMutationResult> DeleteAsync(DeleteMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Delete, Effect: MemoryMutationEffect.Degraded));

        public Task<MemoryMutationResult> SupersedeAsync(SupersedeMemory command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Supersede, Effect: MemoryMutationEffect.Degraded));

        public Task<MemoryMutationResult> ApproveReviewAsync(ApproveMemoryReview command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Review, Effect: MemoryMutationEffect.Degraded));

        public Task<MemoryMutationResult> RecordFeedbackAsync(RecordMemoryFeedback command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Feedback, Effect: MemoryMutationEffect.Degraded));

        public Task<MemoryMutationResult> RecordCitationAsync(RecordMemoryCitation command, MemoryOperationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false, UnsupportedCapability: MemoryProviderCapability.Citation, Effect: MemoryMutationEffect.Degraded));
    }

    private sealed class RecordingDiagnosticEventSink : IDiagnosticEventSink
    {
        public List<DiagnosticEventEnvelope> Events { get; } = [];

        public ValueTask EmitAsync(DiagnosticEventEnvelope diagnosticEvent, CancellationToken cancellationToken)
        {
            Events.Add(diagnosticEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedDiagnosticOperationScopeFactory(string operationId) : IDiagnosticOperationScopeFactory
    {
        public IDiagnosticOperationScope BeginOperation(DiagnosticOperationStart operationStart)
        {
            return new FixedDiagnosticOperationScope(new DiagnosticOperationContext
            {
                OperationId = operationId,
                OperationName = operationStart.OperationName,
                OperationKind = operationStart.OperationKind,
                TraceId = operationStart.TraceId,
                ThreadId = operationStart.ThreadId,
                TurnId = operationStart.TurnId,
                RequestSequence = operationStart.RequestSequence,
                ParentOperationId = operationStart.ParentOperationId,
                Producer = operationStart.Producer,
                Metadata = operationStart.Metadata,
            });
        }
    }

    private sealed class FixedDiagnosticOperationScope(DiagnosticOperationContext context) : IDiagnosticOperationScope
    {
        public DiagnosticOperationContext Context { get; } = context;

        public ValueTask CompleteAsync(DiagnosticOperationCompletion completion, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask FailAsync(DiagnosticOperationFailure failure, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class RecordingIdentityMemoryPlane : ITianShuIdentityMemoryPlane
    {
        public int GetAccountProfileCalls { get; private set; }

        public int ListMemorySpacesCalls { get; private set; }

        public int ListMemoryProvidersCalls { get; private set; }

        public int FilterMemoryCalls { get; private set; }

        public int AddMemoryCalls { get; private set; }

        public int ExtractMemoryCalls { get; private set; }

        public int ImportMemoryCalls { get; private set; }

        public int ExportMemoryCalls { get; private set; }

        public int BindMemoryProviderCalls { get; private set; }

        public int RunMemoryConsolidationCalls { get; private set; }

        public int ForgetMemoryCalls { get; private set; }

        public int DeleteMemoryCalls { get; private set; }

        public int SupersedeMemoryCalls { get; private set; }

        public int ApproveMemoryReviewCalls { get; private set; }

        public int RecordMemoryFeedbackCalls { get; private set; }

        public int RecordMemoryCitationCalls { get; private set; }

        public Task<Account?> GetAccountProfileAsync(
            GetAccountProfile query,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            GetAccountProfileCalls++;
            return Task.FromResult<Account?>(new Account(query.AccountId, "Injected Account"));
        }

        public Task<IReadOnlyList<DeviceBinding>> ListBoundDevicesAsync(
            ListBoundDevices query,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DeviceBinding>>(Array.Empty<DeviceBinding>());

        public Task<IReadOnlyList<MemorySpace>> ListMemorySpacesAsync(
            ListMemorySpaces query,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            ListMemorySpacesCalls++;
            return Task.FromResult<IReadOnlyList<MemorySpace>>(
            [
                new MemorySpace(new MemorySpaceId("memory-injected"), MemoryScopeKind.User, "account-injected", "Injected Memory")
            ]);
        }

        public Task<MemoryOverlay> ResolveMemoryOverlayAsync(
            ResolveMemoryOverlay query,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryOverlay());

        public Task<IReadOnlyList<MemoryProviderDescriptor>> ListMemoryProvidersAsync(
            ListMemoryProviders query,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            ListMemoryProvidersCalls++;
            return Task.FromResult<IReadOnlyList<MemoryProviderDescriptor>>(
            [
                new MemoryProviderDescriptor(
                    "provider-injected",
                    "Injected Provider",
                    "1.0",
                    MemoryProviderCapability.Add | MemoryProviderCapability.Filter,
                    [query.ScopeKind ?? MemoryScopeKind.User])
            ]);
        }

        public Task<MemoryQueryResult> FilterMemoryAsync(
            FilterMemory query,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            FilterMemoryCalls++;
            return Task.FromResult(
                new MemoryQueryResult(
                [
                    new FactMemoryRecord(
                        "preference.shell",
                        StructuredValue.FromString("pwsh"),
                        query.MemorySpaceId ?? new MemorySpaceId("memory-injected"),
                        id: new MemoryRecordId("memory-record:injected"))
                ]));
        }

        public Task<MemoryMutationResult> AddMemoryAsync(
            AddMemory command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            AddMemoryCalls++;
            return Task.FromResult(
                new MemoryMutationResult(
                    true,
                    new MemoryRecordId("memory-record:injected"),
                    MemoryLifecycleStatus.Active,
                    Effect: MemoryMutationEffect.Upserted));
        }

        public Task<IReadOnlyList<MemoryCandidate>> ExtractMemoryAsync(
            ExtractMemory command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            ExtractMemoryCalls++;
            return Task.FromResult<IReadOnlyList<MemoryCandidate>>(
            [
                new MemoryCandidate(
                    "preference.shell",
                    StructuredValue.FromString("pwsh"),
                    command.MemorySpaceId,
                    extractionReason: "injected")
            ]);
        }

        public Task<MemoryMutationResult> ImportMemoryAsync(
            ImportMemory command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            ImportMemoryCalls++;
            return Task.FromResult(
                new MemoryMutationResult(
                    true,
                    new MemoryRecordId("memory-record:imported"),
                    MemoryLifecycleStatus.Active,
                    Effect: MemoryMutationEffect.Upserted));
        }

        public Task<MemoryQueryResult> ExportMemoryAsync(
            ExportMemory command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            ExportMemoryCalls++;
            return Task.FromResult(
                new MemoryQueryResult(
                [
                    new FactMemoryRecord(
                        "preference.shell",
                        StructuredValue.FromString("pwsh"),
                        command.MemorySpaceId,
                        id: new MemoryRecordId("memory-record:injected"))
                ]));
        }

        public Task<MemoryMutationResult> BindMemoryProviderAsync(
            BindMemoryProvider command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            BindMemoryProviderCalls++;
            return Task.FromResult(new MemoryMutationResult(true));
        }

        public Task<MemoryConsolidationRunResult> RunMemoryConsolidationAsync(
            RunMemoryConsolidation command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            RunMemoryConsolidationCalls++;
            return Task.FromResult(new MemoryConsolidationRunResult(0, 0));
        }

        public Task<MemoryMutationResult> ForgetMemoryAsync(
            ForgetMemory command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            ForgetMemoryCalls++;
            return Task.FromResult(
                new MemoryMutationResult(
                    true,
                    command.MemoryRecordId,
                    MemoryLifecycleStatus.Forgotten,
                    Effect: MemoryMutationEffect.LifecycleChanged));
        }

        public Task<MemoryMutationResult> DeleteMemoryAsync(
            DeleteMemory command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            DeleteMemoryCalls++;
            return Task.FromResult(
                new MemoryMutationResult(
                    true,
                    command.MemoryRecordId,
                    MemoryLifecycleStatus.Deleted,
                    Effect: MemoryMutationEffect.LifecycleChanged));
        }

        public Task<MemoryMutationResult> SupersedeMemoryAsync(
            SupersedeMemory command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            SupersedeMemoryCalls++;
            return Task.FromResult(
                new MemoryMutationResult(
                    true,
                    new MemoryRecordId("memory-record:superseded"),
                    MemoryLifecycleStatus.Active,
                    Effect: MemoryMutationEffect.Superseded));
        }

        public Task<MemoryMutationResult> ApproveMemoryReviewAsync(
            ApproveMemoryReview command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            ApproveMemoryReviewCalls++;
            return Task.FromResult(
                new MemoryMutationResult(
                    true,
                    command.MemoryRecordId ?? new MemoryRecordId("memory-record:approved"),
                    MemoryLifecycleStatus.Active,
                    Effect: MemoryMutationEffect.LifecycleChanged));
        }

        public Task<MemoryMutationResult> RecordMemoryFeedbackAsync(
            RecordMemoryFeedback command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            RecordMemoryFeedbackCalls++;
            return Task.FromResult(
                new MemoryMutationResult(
                    true,
                    command.MemoryRecordId,
                    MemoryLifecycleStatus.Active,
                    Effect: MemoryMutationEffect.None));
        }

        public Task<MemoryMutationResult> RecordMemoryCitationAsync(
            RecordMemoryCitation command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
        {
            RecordMemoryCitationCalls++;
            var recordId = command.Citation.Entries.Count > 0 ? command.Citation.Entries[0].MemoryRecordId : new MemoryRecordId("memory-record:citation");
            return Task.FromResult(
                new MemoryMutationResult(
                    true,
                    recordId,
                    MemoryLifecycleStatus.Active,
                    Effect: MemoryMutationEffect.None));
        }
    }
}
