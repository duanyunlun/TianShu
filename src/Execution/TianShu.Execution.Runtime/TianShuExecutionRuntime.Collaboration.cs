using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using CollaborationViewProjection = TianShu.Contracts.Projections.CollaborationSpaceProjection;
using ParticipantViewProjection = TianShu.Contracts.Projections.ParticipantProjection;

namespace TianShu.Execution.Runtime;

public sealed partial class TianShuExecutionRuntime
{
    private readonly InMemoryTianShuCollaborationPlane collaborationPlane = new();

    public Task<CollaborationSpace> CreateSpaceAsync(CreateCollaborationSpace command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(collaborationPlane.CreateSpace(command));
    }

    public Task<CollaborationSpace> ConfigureSpaceAsync(ConfigureCollaborationSpace command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(collaborationPlane.ConfigureSpace(command));
    }

    public Task<bool> ArchiveSpaceAsync(ArchiveCollaborationSpace command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(collaborationPlane.ArchiveSpace(command));
    }

    public Task<CollaborationSpaceOverviewProjection?> GetSpaceOverviewAsync(GetCollaborationSpaceOverview query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(collaborationPlane.GetSpaceOverview(query));
    }

    public Task<CollaborationViewProjection?> GetSpaceProjectionAsync(GetCollaborationSpaceProjection query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(collaborationPlane.GetSpaceProjection(query));
    }

    public Task<IReadOnlyList<CollaborationSpaceOverviewProjection>> ListSpacesAsync(ListCollaborationSpaces query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(collaborationPlane.ListSpaces(query));
    }

    public Task<bool> BindParticipantToSessionAsync(BindParticipantToSession command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(collaborationPlane.BindParticipantToSession(command));
    }

    public Task<bool> BindParticipantToWorkflowAsync(BindParticipantToWorkflow command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(collaborationPlane.BindParticipantToWorkflow(command));
    }

    public Task<bool> UpdateParticipantRoleAsync(UpdateParticipantRole command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(collaborationPlane.UpdateParticipantRole(command));
    }

    public Task<ParticipantProjection?> GetParticipantProjectionAsync(GetParticipantProjection query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(collaborationPlane.GetParticipantProjection(query));
    }

    public Task<ParticipantViewProjection?> GetParticipantViewProjectionAsync(GetParticipantViewProjection query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(collaborationPlane.GetParticipantViewProjection(query));
    }

    public Task<IReadOnlyList<ParticipantProjection>> ListParticipantsInScopeAsync(ListParticipantsInScope query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(collaborationPlane.ListParticipantsInScope(query));
    }
}

internal sealed class InMemoryTianShuCollaborationPlane
{
    private const string DefaultSpaceId = "tianshu-runtime";
    private const string DefaultSpaceKey = "tianshu-runtime";
    private const string DefaultSpaceDisplayName = "TianShu Runtime";
    private const string DefaultPurpose = "默认本地协作空间。";
    private const string DefaultParticipantRole = "member";

    private readonly object gate = new();
    private readonly Dictionary<string, CollaborationSpace> spaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ParticipantEntry> participants = new(StringComparer.Ordinal);
    private string currentSpaceId = DefaultSpaceId;

    public InMemoryTianShuCollaborationPlane()
    {
        spaces[DefaultSpaceId] = new CollaborationSpace(
            new CollaborationSpaceId(DefaultSpaceId),
            DefaultSpaceKey,
            DefaultSpaceDisplayName,
            new CollaborationSpaceProfile(DefaultPurpose),
            CollaborationDefaultSet.Empty);
    }

    public CollaborationSpace CreateSpace(CreateCollaborationSpace command)
    {
        lock (gate)
        {
            var created = new CollaborationSpace(
                command.SpaceId,
                command.Key,
                command.DisplayName,
                command.Profile,
                command.Defaults,
                command.PolicyRef,
                isArchived: false);
            spaces[command.SpaceId.Value] = created;
            currentSpaceId = command.SpaceId.Value;
            return created;
        }
    }

    public CollaborationSpace ConfigureSpace(ConfigureCollaborationSpace command)
    {
        lock (gate)
        {
            if (!spaces.TryGetValue(command.SpaceId.Value, out var existing))
            {
                throw new InvalidOperationException($"未找到协作空间：{command.SpaceId.Value}");
            }

            var updated = new CollaborationSpace(
                existing.Id,
                existing.Key,
                command.DisplayName ?? existing.DisplayName,
                command.Profile ?? existing.Profile,
                command.Defaults ?? existing.Defaults,
                command.PolicyRef ?? existing.PolicyRef,
                existing.IsArchived);
            spaces[command.SpaceId.Value] = updated;
            currentSpaceId = command.SpaceId.Value;
            return updated;
        }
    }

    public bool ArchiveSpace(ArchiveCollaborationSpace command)
    {
        lock (gate)
        {
            if (!spaces.TryGetValue(command.SpaceId.Value, out var existing))
            {
                return false;
            }

            spaces[command.SpaceId.Value] = new CollaborationSpace(
                existing.Id,
                existing.Key,
                existing.DisplayName,
                existing.Profile,
                existing.Defaults,
                existing.PolicyRef,
                isArchived: true);
            currentSpaceId = ResolvePreferredSpaceId();
            return true;
        }
    }

    public CollaborationSpaceOverviewProjection? GetSpaceOverview(GetCollaborationSpaceOverview query)
    {
        lock (gate)
        {
            return spaces.TryGetValue(query.SpaceId.Value, out var space)
                ? ToOverview(space)
                : null;
        }
    }

    public IReadOnlyList<CollaborationSpaceOverviewProjection> ListSpaces(ListCollaborationSpaces query)
    {
        lock (gate)
        {
            return spaces.Values
                .Where(space => query.IncludeArchived || !space.IsArchived)
                .OrderBy(static space => space.DisplayName, StringComparer.Ordinal)
                .Select(ToOverview)
                .ToArray();
        }
    }

    public CollaborationViewProjection? GetSpaceProjection(GetCollaborationSpaceProjection query)
    {
        lock (gate)
        {
            return spaces.TryGetValue(query.SpaceId.Value, out var space)
                ? ToViewProjection(space)
                : null;
        }
    }

    public bool BindParticipantToSession(BindParticipantToSession command)
    {
        lock (gate)
        {
            UpsertParticipant(command.ParticipantId, role: null, ResolvePreferredSpaceId());
            return true;
        }
    }

    public bool BindParticipantToWorkflow(BindParticipantToWorkflow command)
    {
        lock (gate)
        {
            UpsertParticipant(command.ParticipantId, role: null, ResolvePreferredSpaceId());
            return true;
        }
    }

    public bool UpdateParticipantRole(UpdateParticipantRole command)
    {
        lock (gate)
        {
            UpsertParticipant(command.ParticipantId, command.Role, ResolvePreferredSpaceId());
            return true;
        }
    }

    public ParticipantProjection? GetParticipantProjection(GetParticipantProjection query)
    {
        lock (gate)
        {
            return participants.TryGetValue(query.ParticipantId.Value, out var entry)
                ? entry.ToProjection()
                : null;
        }
    }

    public ParticipantViewProjection? GetParticipantViewProjection(GetParticipantViewProjection query)
    {
        lock (gate)
        {
            return participants.TryGetValue(query.ParticipantId.Value, out var entry)
                ? entry.ToViewProjection()
                : null;
        }
    }

    public CollaborationSpaceRef? TryGetSpaceReference(CollaborationSpaceId spaceId)
    {
        lock (gate)
        {
            return spaces.TryGetValue(spaceId.Value, out var space)
                ? CollaborationSpaceRef.From(space)
                : null;
        }
    }

    public IReadOnlyList<ParticipantProjection> ListParticipantsInScope(ListParticipantsInScope query)
    {
        lock (gate)
        {
            return participants.Values
                .Where(entry => entry.SpaceIds.Contains(query.CollaborationSpaceId.Value))
                .OrderBy(static entry => entry.DisplayName, StringComparer.Ordinal)
                .Select(static entry => entry.ToProjection())
                .ToArray();
        }
    }

    private static CollaborationSpaceOverviewProjection ToOverview(CollaborationSpace space)
        => new(space.Id, space.Key, space.DisplayName, space.IsArchived);

    private static CollaborationViewProjection ToViewProjection(CollaborationSpace space)
        => new(
            CollaborationSpaceRef.From(space),
            ActiveSessionCount: 0,
            ActiveThreadCount: 0,
            space.IsArchived);

    private void UpsertParticipant(ParticipantId participantId, string? role, string collaborationSpaceId)
    {
        if (!participants.TryGetValue(participantId.Value, out var entry))
        {
            entry = new ParticipantEntry(
                participantId,
                InferParticipantKind(participantId),
                InferParticipantDisplayName(participantId),
                NormalizeRole(role),
                new HashSet<string>(StringComparer.Ordinal));
        }

        entry.Role = NormalizeRole(role, entry.Role);
        entry.SpaceIds.Add(collaborationSpaceId);
        participants[participantId.Value] = entry;
        currentSpaceId = collaborationSpaceId;
    }

    private string ResolvePreferredSpaceId()
    {
        if (spaces.TryGetValue(currentSpaceId, out var currentSpace) && !currentSpace.IsArchived)
        {
            return currentSpaceId;
        }

        var fallback = spaces.Values.FirstOrDefault(static space => !space.IsArchived)?.Id.Value;
        return fallback ?? DefaultSpaceId;
    }

    private static ParticipantKind InferParticipantKind(ParticipantId participantId)
    {
        var value = participantId.Value;
        if (value.Contains("human", StringComparison.OrdinalIgnoreCase)
            || value.Contains("user", StringComparison.OrdinalIgnoreCase))
        {
            return ParticipantKind.Human;
        }

        if (value.Contains("service", StringComparison.OrdinalIgnoreCase))
        {
            return ParticipantKind.Service;
        }

        if (value.Contains("automation", StringComparison.OrdinalIgnoreCase)
            || value.Contains("bot", StringComparison.OrdinalIgnoreCase))
        {
            return ParticipantKind.Automation;
        }

        return ParticipantKind.Agent;
    }

    private static string InferParticipantDisplayName(ParticipantId participantId)
        => participantId.Value;

    private static string NormalizeRole(string? role, string? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(role))
        {
            return role.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        return DefaultParticipantRole;
    }

    private sealed class ParticipantEntry
    {
        public ParticipantEntry(
            ParticipantId participantId,
            ParticipantKind kind,
            string displayName,
            string role,
            HashSet<string> spaceIds)
        {
            ParticipantId = participantId;
            Kind = kind;
            DisplayName = displayName;
            Role = role;
            SpaceIds = spaceIds;
        }

        public ParticipantId ParticipantId { get; }

        public ParticipantKind Kind { get; }

        public string DisplayName { get; }

        public string Role { get; set; }

        public HashSet<string> SpaceIds { get; }

        public ParticipantProjection ToProjection()
            => new(ParticipantId, Kind, DisplayName, Role);

        public ParticipantViewProjection ToViewProjection()
            => new(
                new ParticipantRef(ParticipantId, Kind, DisplayName),
                ScopeKind: "participant",
                ScopeKey: ParticipantId.Value,
                Role,
                IsActive: true);
    }
}
