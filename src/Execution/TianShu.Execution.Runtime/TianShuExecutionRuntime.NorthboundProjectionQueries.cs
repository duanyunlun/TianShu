using System.Globalization;
using TianShu.ArtifactStore;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Workflows;
using Task = System.Threading.Tasks.Task;
using ThreadViewProjection = TianShu.Contracts.Projections.ThreadProjection;
using TianShu.Contracts.Projections;
using TianShu.ProjectionStores;

namespace TianShu.Execution.Runtime;

public sealed partial class TianShuExecutionRuntime
{
    private ArtifactRuntimeStoreSet artifactRuntimeStoreSet = CreateDefaultArtifactRuntimeStoreSet();

    private IArtifactStore artifactStore => artifactRuntimeStoreSet.MetadataStore;

    public async Task<ThreadViewProjection?> GetThreadProjectionAsync(GetThreadProjection query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var snapshot = await projectionRuntimeStores.Snapshots
            .GetAsync(new ProjectionSnapshotKey(ProjectionScopeKind.Thread, query.ThreadId.Value), cancellationToken)
            .ConfigureAwait(false);
        if (snapshot?.Delta?.Payload is ThreadProjectionPayload payload)
        {
            return payload.Projection;
        }

        var detail = await TryReadThreadDetailAsync(query.ThreadId, cancellationToken).ConfigureAwait(false);
        if (detail is not null)
        {
            return ToThreadProjection(detail);
        }

        var activeId = Normalize(activeThreadId);
        return string.Equals(activeId, query.ThreadId.Value, StringComparison.Ordinal)
            ? BuildRuntimeThreadProjection(query.ThreadId, Normalize(activeTurnId) ?? Normalize(submittedTurnId))
            : null;
    }

    public async Task<ApprovalQueueProjection?> GetApprovalQueueProjectionAsync(ListPendingApprovals query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var requestedFrom = BuildMemoryReviewRequestedFromParticipant();
        var memoryReviewItems = await BuildMemoryReviewApprovalQueueItemsAsync(query, requestedFrom, cancellationToken)
            .ConfigureAwait(false);
        var items = pendingApprovalProjectionStates.Values
            .Where(item => query.RequestedFromParticipantId is null || item.RequestedFromParticipant.Id == query.RequestedFromParticipantId)
            .Select(static item => new ApprovalQueueItem(
                new ApprovalId(item.CallId),
                item.Title,
                item.Reason,
                item.RequestedFromParticipant,
                item.RequestedAt))
            .Concat(memoryReviewItems)
            .OrderBy(static item => item.RequestedAt)
            .ToArray();
        if (items.Length > 0)
        {
            return new ApprovalQueueProjection(items);
        }

        var snapshot = await projectionRuntimeStores.Snapshots
            .GetAsync(new ProjectionSnapshotKey(ProjectionScopeKind.ApprovalQueue, "global"), cancellationToken)
            .ConfigureAwait(false);
        if (snapshot?.Delta?.Payload is ApprovalQueueProjectionPayload payload)
        {
            var projected = query.RequestedFromParticipantId is null
                ? payload.Projection
                : new ApprovalQueueProjection(
                    payload.Projection.Items
                        .Where(item => item.RequestedFrom.Id == query.RequestedFromParticipantId)
                        .ToArray());
            if (projected.Items.Count > 0 || process is null)
            {
                return projected;
            }
        }

        return process is null
            ? null
            : await TryReadApprovalQueueProjectionFromAppHostAsync(query, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ApprovalQueueItem>> BuildMemoryReviewApprovalQueueItemsAsync(
        ListPendingApprovals query,
        ParticipantRef requestedFrom,
        CancellationToken cancellationToken)
    {
        if (query.RequestedFromParticipantId is not null
            && query.RequestedFromParticipantId != requestedFrom.Id)
        {
            return Array.Empty<ApprovalQueueItem>();
        }

        if (identityMemoryPlane is null)
        {
            return Array.Empty<ApprovalQueueItem>();
        }

        MemoryQueryResult pendingReviews;
        try
        {
            pendingReviews = await identityMemoryPlane.FilterMemoryAsync(
                    new FilterMemory(LifecycleStatus: MemoryLifecycleStatus.PendingReview),
                    BuildIdentityMemoryContext(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return Array.Empty<ApprovalQueueItem>();
        }

        return pendingReviews.Records
            .OrderBy(static fact => fact.RecordedAt)
            .Select(fact => new ApprovalQueueItem(
                new ApprovalId($"memory-review:{fact.Id.Value}"),
                $"记忆审核：{fact.Key}",
                BuildMemoryReviewApprovalReason(fact),
                requestedFrom,
                fact.RecordedAt,
                CheckpointKind: "Approval",
                RiskSource: "policy_rule",
                RequestContent: BuildMemoryReviewRequestContent(fact),
                UserDecision: "pending",
                ExecutionResult: "not_executed",
                Metadata: new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["domain"] = StructuredValue.FromString("memory"),
                    ["memorySpaceId"] = StructuredValue.FromString(fact.MemorySpaceId.Value),
                    ["memoryRecordId"] = StructuredValue.FromString(fact.Id.Value),
                    ["key"] = StructuredValue.FromString(fact.Key),
                    ["lifecycleStatus"] = StructuredValue.FromString(fact.LifecycleStatus.ToString()),
                })))
            .ToArray();
    }

    private static string BuildMemoryReviewApprovalReason(FactMemoryRecord fact)
        => $"待审记忆需要通过记忆治理确认后才能进入 Active：space={fact.MemorySpaceId.Value}; confidence={fact.Confidence:0.##}。";

    private static string BuildMemoryReviewRequestContent(FactMemoryRecord fact)
    {
        var valueText = fact.Value.Kind is StructuredValueKind.String
            or StructuredValueKind.Number
            or StructuredValueKind.Boolean
            or StructuredValueKind.Null
                ? fact.Value.GetString()
                : null;
        if (string.IsNullOrWhiteSpace(valueText))
        {
            valueText = $"<{fact.Value.Kind}>";
        }

        if (valueText.Length > 160)
        {
            valueText = string.Concat(valueText.AsSpan(0, 157), "...");
        }

        return $"key={fact.Key}; value={valueText}; valueKind={fact.Value.Kind}";
    }

    private static ParticipantRef BuildMemoryReviewRequestedFromParticipant()
        => new(new ParticipantId("tianshu-user"), ParticipantKind.Human, "TianShu User");

    public Task<WorkflowBoardProjection?> GetWorkflowBoardProjectionAsync(GetWorkflowBoard query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workflowPlane.GetWorkflowBoardProjection(query));
    }

    public Task<TaskBoardProjection?> GetTaskBoardProjectionAsync(GetTaskBoard query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workflowPlane.GetTaskBoardProjection(query));
    }

    public Task<PlanProjection?> GetPlanProjectionAsync(GetPlanProjection query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workflowPlane.GetPlanProjection(query));
    }

    public async Task<TeamProjection?> GetTeamProjectionAsync(GetTeamProjection query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var snapshot = await projectionRuntimeStores.Snapshots
            .GetAsync(new ProjectionSnapshotKey(ProjectionScopeKind.Team, query.TeamId.Value), cancellationToken)
            .ConfigureAwait(false);
        if (snapshot?.Delta?.Payload is TeamProjectionPayload payload)
        {
            return payload.Projection;
        }

        ControlPlaneAgentRosterResult roster;
        try
        {
            roster = await ListAgentsAsync(
                new ControlPlaneAgentListQuery
                {
                    IncludePrimaryThreads = true,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var members = roster.Agents
            .Select(ToAgent)
            .Where(static item => item is not null)
            .Cast<Agent>()
            .ToArray();
        if (members.Length == 0)
        {
            return null;
        }

        return new TeamProjection(
            new Team(query.TeamId, query.TeamId.Value, members.Select(static item => item.Id).ToArray()),
            members);
    }

    public async Task<IReadOnlyList<UserInputRequest>> ListUserInputRequestsAsync(ListUserInputRequests query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var requests = pendingUserInputProjectionStates.Values
            .Where(item => query.RequestedFromParticipantId is null || item.RequestedFromParticipant.Id == query.RequestedFromParticipantId)
            .OrderBy(static item => item.RequestedAt)
            .Select(static item => new UserInputRequest(
                new UserInputRequestId(item.CallId),
                item.Prompt,
                item.RequestedFromParticipant,
                requestedAt: item.RequestedAt))
            .ToArray();
        if (requests.Length > 0 || process is null)
        {
            return requests;
        }

        return await TryReadUserInputRequestsFromAppHostAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public Task<ArtifactProjection?> GetArtifactProjectionAsync(GetArtifactDetail query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return GetArtifactProjectionCoreAsync(query, cancellationToken);
    }

    public Task<ArtifactCollectionProjection?> GetArtifactCollectionProjectionAsync(ListArtifacts query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return GetArtifactCollectionProjectionCoreAsync(query, cancellationToken);
    }

    public async Task<Artifact> PublishArtifactAsync(PublishArtifact command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var record = await artifactStore.PublishAsync(command.Artifact, cancellationToken).ConfigureAwait(false);
        return record.Artifact;
    }

    public async Task<Artifact> PromoteArtifactAsync(PromoteArtifact command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var record = await artifactStore.PromoteAsync(command.ArtifactId, command.TargetChannel, cancellationToken).ConfigureAwait(false);
        return record.Artifact;
    }

    public async Task<Artifact> AttachArtifactToTaskAsync(AttachArtifactToTask command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var record = await artifactStore.AttachToTaskAsync(command.ArtifactId, command.TaskId, cancellationToken).ConfigureAwait(false);
        return record.Artifact;
    }

    private async Task<ArtifactProjection?> GetArtifactProjectionCoreAsync(GetArtifactDetail query, CancellationToken cancellationToken)
    {
        var record = await artifactStore.GetAsync(query.ArtifactId, cancellationToken).ConfigureAwait(false);
        return record is null ? null : ToArtifactProjection(record);
    }

    private async Task<ArtifactCollectionProjection?> GetArtifactCollectionProjectionCoreAsync(ListArtifacts query, CancellationToken cancellationToken)
    {
        var records = await artifactStore.ListAsync(query, cancellationToken).ConfigureAwait(false);
        if (records.Count == 0)
        {
            return null;
        }

        var collaborationSpace = records[0].Artifact.CollaborationSpace;
        var items = records
            .Select(static record => new ArtifactCollectionItem(
                record.Artifact.Id,
                record.Artifact.Name,
                record.Artifact.Kind,
                record.Artifact.State,
                record.Artifact.ProducedByParticipant,
                record.PromotionChannels,
                record.AttachedTaskIds,
                record.UpdatedAt))
            .ToArray();

        return new ArtifactCollectionProjection(collaborationSpace, items);
    }

    private static ArtifactProjection ToArtifactProjection(ArtifactStoreRecord record)
        => new(
            record.Artifact.Id,
            record.Artifact.Name,
            record.Artifact.Kind,
            record.Artifact.State,
            record.Artifact.CollaborationSpace,
            record.Artifact.ProducedByParticipant);

    private static ArtifactRuntimeStoreSet CreateDefaultArtifactRuntimeStoreSet()
    {
        var store = new InMemoryArtifactStore();
        return new ArtifactRuntimeStoreSet(store, null, null, null, "memory", null);
    }

    private async Task<ControlPlaneThreadDetail?> TryReadThreadDetailAsync(ThreadId threadId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await ReadThreadAsync(
                new ControlPlaneReadThreadQuery
                {
                    ThreadId = threadId,
                    IncludeTurns = true,
                },
                cancellationToken).ConfigureAwait(false);
            return result.Thread;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<ApprovalQueueProjection?> TryReadApprovalQueueProjectionFromAppHostAsync(
        ListPendingApprovals query,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?>();
        if (query.RequestedFromParticipantId is not null)
        {
            parameters["requestedFromParticipantId"] = query.RequestedFromParticipantId.Value;
        }

        var rpcResult = await InvokeDiagnosticRpcAsync(
                "governance/approvalQueue/read",
                StructuredValue.FromPlainObject(parameters),
                cancellationToken)
            .ConfigureAwait(false);
        var projection = ParseApprovalQueueProjection(rpcResult);
        return projection is null || projection.Items.Count == 0 ? null : projection;
    }

    private async Task<IReadOnlyList<UserInputRequest>> TryReadUserInputRequestsFromAppHostAsync(
        ListUserInputRequests query,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?>();
        if (query.RequestedFromParticipantId is not null)
        {
            parameters["requestedFromParticipantId"] = query.RequestedFromParticipantId.Value;
        }

        var rpcResult = await InvokeDiagnosticRpcAsync(
                "governance/userInputs/list",
                StructuredValue.FromPlainObject(parameters),
                cancellationToken)
            .ConfigureAwait(false);
        return ParseUserInputRequests(rpcResult);
    }

    private static ApprovalQueueProjection? ParseApprovalQueueProjection(StructuredValue value)
    {
        if (value.Kind == StructuredValueKind.Null)
        {
            return null;
        }

        var element = System.Text.Json.JsonSerializer.SerializeToElement(value.ToPlainObject(), StructuredJsonOptions);
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object
            || !element.TryGetProperty("items", out var itemsElement)
            || itemsElement.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<ApprovalQueueItem>();
        foreach (var item in itemsElement.EnumerateArray())
        {
            var id = Normalize(ReadString(item, "approvalId", "value"))
                     ?? Normalize(ReadString(item, "approvalId"));
            var title = Normalize(ReadString(item, "title"));
            var reason = Normalize(ReadString(item, "reason"));
            var requestedFrom = ParseParticipantRef(item, "requestedFrom");
            if (string.IsNullOrWhiteSpace(id)
                || string.IsNullOrWhiteSpace(title)
                || string.IsNullOrWhiteSpace(reason)
                || requestedFrom is null)
            {
                continue;
            }

            items.Add(new ApprovalQueueItem(
                new ApprovalId(id!),
                title!,
                reason!,
                requestedFrom,
                ReadDateTimeOffset(item, "requestedAt") ?? DateTimeOffset.UtcNow));
        }

        return new ApprovalQueueProjection(items);
    }

    private static IReadOnlyList<UserInputRequest> ParseUserInputRequests(StructuredValue value)
    {
        if (value.Kind == StructuredValueKind.Null)
        {
            return [];
        }

        var element = System.Text.Json.JsonSerializer.SerializeToElement(value.ToPlainObject(), StructuredJsonOptions);
        if (element.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<UserInputRequest>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                continue;
            }

            var id = Normalize(ReadString(item, "id", "value")) ?? Normalize(ReadString(item, "id"));
            var prompt = Normalize(ReadString(item, "prompt"));
            var requestedFrom = ParseParticipantRef(item, "requestedFromParticipant")
                                ?? ParseParticipantRef(item, "requestedFrom");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(prompt) || requestedFrom is null)
            {
                continue;
            }

            results.Add(new UserInputRequest(
                new UserInputRequestId(id!),
                prompt!,
                requestedFrom,
                requestedAt: ReadDateTimeOffset(item, "requestedAt")));
        }

        return results;
    }

    private static ParticipantRef? ParseParticipantRef(System.Text.Json.JsonElement item, string propertyName)
    {
        var id = Normalize(ReadString(item, propertyName, "id", "value"))
                 ?? Normalize(ReadString(item, propertyName, "id"));
        var displayName = Normalize(ReadString(item, propertyName, "displayName"));
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var kind = ReadInt(item, propertyName, "kind") is { } kindValue
            ? (ParticipantKind)kindValue
            : ParticipantKind.Human;
        return new ParticipantRef(new ParticipantId(id!), kind, displayName!);
    }

    private static DateTimeOffset? ReadDateTimeOffset(System.Text.Json.JsonElement item, params string[] path)
        => DateTimeOffset.TryParse(ReadString(item, path), out var parsed) ? parsed : null;

    private static int? ReadInt(System.Text.Json.JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != System.Text.Json.JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number when current.TryGetInt32(out var value) => value,
            System.Text.Json.JsonValueKind.String when int.TryParse(current.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private ThreadViewProjection ToThreadProjection(ControlPlaneThreadDetail detail)
    {
        var title = Normalize(detail.Name)
                    ?? Normalize(detail.Preview)
                    ?? detail.ThreadId.Value;
        var activeTurn = ResolveActiveTurn(detail);
        return new ThreadViewProjection(
            detail.ThreadId,
            title,
            BuildRuntimeCollaborationSpace(detail.ThreadId.Value),
            BuildRuntimeParticipantRef(),
            activeTurn,
            HasActiveTurn: activeTurn is not null || string.Equals(detail.Status, "active", StringComparison.OrdinalIgnoreCase),
            RuntimeStatus: new ThreadRuntimeStatusProjection(
                Normalize(detail.Status) ?? "unknown",
                TurnStatus: Normalize(detail.Status),
                HasActiveRun: activeTurn is not null));
    }

    private ThreadViewProjection BuildRuntimeThreadProjection(ThreadId threadId, string? turnId)
    {
        var title = threadProjectionTitles.TryGetValue(threadId.Value, out var existingTitle)
            ? existingTitle
            : threadId.Value;
        var hasActiveTurn = !string.IsNullOrWhiteSpace(turnId);
        var runtimeStatus = threadProjectionStatuses.TryGetValue(threadId.Value, out var status)
            ? status
            : new ThreadRuntimeStatusProjection(
                hasActiveTurn ? "running" : "unknown",
                TurnStatus: hasActiveTurn ? "running" : null,
                HasActiveRun: hasActiveTurn);
        return new ThreadViewProjection(
            threadId,
            title,
            BuildRuntimeCollaborationSpace(threadId.Value),
            BuildRuntimeParticipantRef(),
            string.IsNullOrWhiteSpace(turnId) ? null : new TurnId(turnId),
            HasActiveTurn: hasActiveTurn,
            RuntimeStatus: runtimeStatus,
            TokenUsage: threadProjectionTokenUsage.TryGetValue(threadId.Value, out var usage) ? usage : null);
    }

    private void UpdateRuntimeThreadProjectionState(string threadId, ControlPlaneConversationStreamEvent streamEvent)
    {
        switch (streamEvent.Kind)
        {
            case ControlPlaneConversationStreamEventKind.TurnStarted:
                threadProjectionStatuses[threadId] = new ThreadRuntimeStatusProjection(
                    "running",
                    TurnStatus: Normalize(streamEvent.Status) ?? "running",
                    BackgroundStatus: "running",
                    HasActiveRun: true,
                    NotificationCode: streamEvent.Kind.ToString());
                break;
            case ControlPlaneConversationStreamEventKind.TurnCompleted:
                var terminalStatus = Normalize(streamEvent.Status) ?? "completed";
                threadProjectionStatuses[threadId] = new ThreadRuntimeStatusProjection(
                    terminalStatus,
                    TurnStatus: terminalStatus,
                    BackgroundStatus: "idle",
                    HasActiveRun: false,
                    NotificationCode: streamEvent.Kind.ToString());
                break;
            case ControlPlaneConversationStreamEventKind.ThreadStatusChanged:
                var lifecycle = Normalize(streamEvent.Status)
                                ?? TryReadScalar(streamEvent.Payload, "type")
                                ?? "unknown";
                threadProjectionStatuses[threadId] = new ThreadRuntimeStatusProjection(
                    lifecycle,
                    TurnStatus: lifecycle,
                    BackgroundStatus: TryReadActiveFlags(streamEvent.Payload),
                    HasActiveRun: IsActiveLifecycle(lifecycle),
                    NotificationCode: streamEvent.Kind.ToString());
                break;
            case ControlPlaneConversationStreamEventKind.ThreadTokenUsageUpdated:
                if (TryBuildThreadTokenUsageProjection(streamEvent.Payload) is { } usage)
                {
                    threadProjectionTokenUsage[threadId] = usage;
                }

                break;
        }
    }

    private static bool IsActiveLifecycle(string lifecycle)
        => lifecycle.Equals("active", StringComparison.OrdinalIgnoreCase)
           || lifecycle.Equals("running", StringComparison.OrdinalIgnoreCase)
           || lifecycle.Equals("streaming", StringComparison.OrdinalIgnoreCase)
           || lifecycle.Equals("in_progress", StringComparison.OrdinalIgnoreCase)
           || lifecycle.Equals("inProgress", StringComparison.OrdinalIgnoreCase);

    private static string? TryReadActiveFlags(StructuredValue? payload)
    {
        if (payload?.Kind != StructuredValueKind.Object
            || !TryGetProperty(payload, out var flags, "activeFlags", "ActiveFlags")
            || flags?.Kind != StructuredValueKind.Array)
        {
            return null;
        }

        var values = flags.Items
            .Select(static item => item.Kind is StructuredValueKind.String ? Normalize(item.StringValue) : null)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        return values.Length == 0 ? null : string.Join(",", values);
    }

    private static ThreadTokenUsageProjection? TryBuildThreadTokenUsageProjection(StructuredValue? payload)
    {
        if (payload?.Kind != StructuredValueKind.Object)
        {
            return null;
        }

        return new ThreadTokenUsageProjection(
            TryBuildThreadTokenUsageBreakdown(payload, "last", "Last"),
            TryBuildThreadTokenUsageBreakdown(payload, "total", "Total"),
            TryReadInt(payload, "modelContextWindow", "ModelContextWindow"),
            TryReadBool(payload, "estimated", "Estimated") ?? false,
            TryReadScalar(payload, "source", "Source"));
    }

    private static ThreadTokenUsageBreakdownProjection TryBuildThreadTokenUsageBreakdown(
        StructuredValue payload,
        params string[] propertyNames)
    {
        if (!TryGetProperty(payload, out var value, propertyNames) || value?.Kind != StructuredValueKind.Object)
        {
            return new ThreadTokenUsageBreakdownProjection(0, 0, 0, 0, 0);
        }

        return new ThreadTokenUsageBreakdownProjection(
            TryReadInt(value, "totalTokens", "TotalTokens") ?? 0,
            TryReadInt(value, "inputTokens", "InputTokens") ?? 0,
            TryReadInt(value, "cachedInputTokens", "CachedInputTokens") ?? 0,
            TryReadInt(value, "outputTokens", "OutputTokens") ?? 0,
            TryReadInt(value, "reasoningOutputTokens", "ReasoningOutputTokens") ?? 0);
    }

    private static string? TryReadScalar(StructuredValue? payload, params string[] propertyNames)
    {
        if (!TryGetProperty(payload, out var value, propertyNames) || value is null)
        {
            return null;
        }

        try
        {
            return Normalize(value.GetString());
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static int? TryReadInt(StructuredValue? payload, params string[] propertyNames)
        => int.TryParse(TryReadScalar(payload, propertyNames), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static bool? TryReadBool(StructuredValue? payload, params string[] propertyNames)
        => bool.TryParse(TryReadScalar(payload, propertyNames), out var value) ? value : null;

    private static bool TryGetProperty(StructuredValue? payload, out StructuredValue? value, params string[] propertyNames)
    {
        if (payload?.Kind == StructuredValueKind.Object)
        {
            foreach (var propertyName in propertyNames)
            {
                if (payload.Properties.TryGetValue(propertyName, out value))
                {
                    return true;
                }
            }
        }

        value = null;
        return false;
    }

    private static TurnId? ResolveActiveTurn(ControlPlaneThreadDetail detail)
    {
        var activeTurn = detail.Turns.LastOrDefault(static turn =>
            string.Equals(turn.Status, "active", StringComparison.OrdinalIgnoreCase)
            || string.Equals(turn.Status, "running", StringComparison.OrdinalIgnoreCase)
            || string.Equals(turn.Status, "streaming", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(activeTurn?.Id) ? null : new TurnId(activeTurn.Id);
    }

    private static Agent? ToAgent(ControlPlaneAgentDescriptor descriptor)
    {
        var displayName = Normalize(descriptor.AgentNickname)
                          ?? Normalize(descriptor.Name)
                          ?? descriptor.ThreadId.Value;
        var roleText = Normalize(descriptor.AgentRole) ?? "agent";
        var role = roleText.ToLowerInvariant() switch
        {
            "coordinator" => AgentRole.Coordinator,
            "reviewer" => AgentRole.Reviewer,
            "explorer" => AgentRole.Explorer,
            "worker" => AgentRole.Worker,
            _ => AgentRole.Implementer,
        };

        return new Agent(
            new AgentId(descriptor.ThreadId.Value),
            new ParticipantRef(new ParticipantId(descriptor.ThreadId.Value), ParticipantKind.Agent, displayName),
            displayName,
            role,
            descriptor.Lineage is null
                ? null
                : new TianShu.Contracts.Agents.AgentLineage(
                    descriptor.Lineage.ParentThreadId is null ? null : new AgentId(descriptor.Lineage.ParentThreadId.Value),
                    descriptor.Lineage.Depth ?? 0));
    }

    private static CollaborationSpaceRef BuildRuntimeCollaborationSpace(string key)
        => new(
            new CollaborationSpaceId($"runtime:{key}"),
            $"runtime:{key}",
            "TianShu Runtime");

    private static ParticipantRef BuildRuntimeParticipantRef()
        => new(new ParticipantId("tianshu-runtime"), ParticipantKind.Service, "TianShu Runtime");
}
