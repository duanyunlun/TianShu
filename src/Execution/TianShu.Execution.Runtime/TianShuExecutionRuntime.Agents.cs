using TianShu.Contracts.Agents;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Projections;

namespace TianShu.Execution.Runtime;

public sealed partial class TianShuExecutionRuntime
{
    public async Task<AgentRosterProjection?> GetAgentRosterProjectionAsync(GetAgentRoster query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.WorkflowId is not null)
        {
            return null;
        }

        var roster = await ListAgentsAsync(
            new ControlPlaneAgentListQuery
            {
                IncludePrimaryThreads = false,
            },
            cancellationToken).ConfigureAwait(false);

        return new AgentRosterProjection(roster.Agents.Select(ToAgentRosterEntry).ToArray());
    }

    public async Task<ControlPlaneAgentRosterResult> ListAgentsAsync(ControlPlaneAgentListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var loadedThreads = await ListLoadedThreadsAsync(
            new ControlPlaneLoadedThreadListQuery
            {
                Limit = query.Limit,
                Cursor = query.Cursor,
            },
            cancellationToken).ConfigureAwait(false);

        var agents = new List<ControlPlaneAgentDescriptor>(loadedThreads.ThreadIds.Count);
        foreach (var threadId in loadedThreads.ThreadIds)
        {
            var readResult = await ReadThreadAsync(
                new ControlPlaneReadThreadQuery
                {
                    ThreadId = threadId,
                    IncludeTurns = false,
                },
                cancellationToken).ConfigureAwait(false);

            var descriptor = ToControlPlaneAgentDescriptor(readResult.Thread);
            if (descriptor is null)
            {
                continue;
            }

            if (!query.IncludePrimaryThreads
                && descriptor.Lineage is null
                && string.IsNullOrWhiteSpace(descriptor.AgentNickname)
                && string.IsNullOrWhiteSpace(descriptor.AgentRole))
            {
                continue;
            }

            agents.Add(descriptor);
        }

        return new ControlPlaneAgentRosterResult
        {
            Agents = agents,
            NextCursor = loadedThreads.NextCursor,
        };
    }

    private static AgentRosterEntry ToAgentRosterEntry(ControlPlaneAgentDescriptor descriptor)
    {
        var displayName = !string.IsNullOrWhiteSpace(descriptor.AgentNickname)
            ? descriptor.AgentNickname
            : !string.IsNullOrWhiteSpace(descriptor.Name)
                ? descriptor.Name
                : descriptor.ThreadId.Value;
        var role = !string.IsNullOrWhiteSpace(descriptor.AgentRole)
            ? descriptor.AgentRole
            : "agent";
        var isBusy = descriptor.ActiveFlags.Any(flag => string.Equals(flag, "busy", StringComparison.OrdinalIgnoreCase))
            || string.Equals(descriptor.Status, "busy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(descriptor.Status, "running", StringComparison.OrdinalIgnoreCase)
            || string.Equals(descriptor.Status, "streaming", StringComparison.OrdinalIgnoreCase);

        return new AgentRosterEntry(
            new AgentId(descriptor.ThreadId.Value),
            new ParticipantRef(
                new ParticipantId(descriptor.ThreadId.Value),
                ParticipantKind.Agent,
                displayName),
            role,
            descriptor.Lineage?.Depth ?? 0,
            isBusy);
    }
}
