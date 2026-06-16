using TianShu.Contracts.Agents;
using TianShu.Contracts.Conversations;

namespace TianShu.Execution.Runtime.ControlPlane;

public sealed partial class RuntimeControlPlaneAdapter
{
    private static ThreadId? ToOptionalThreadId(string? value)
        => string.IsNullOrWhiteSpace(value) ? default(ThreadId?) : new ThreadId(value);

    private static ControlPlaneAgentDescriptor? ToControlPlaneAgentDescriptor(ControlPlaneThreadDetail? thread)
    {
        if (thread is null)
        {
            return null;
        }

        return new ControlPlaneAgentDescriptor
        {
            ThreadId = thread.ThreadId,
            Preview = thread.Preview,
            Name = thread.Name,
            WorkingDirectory = thread.WorkingDirectory,
            Path = thread.Path,
            Source = thread.Source?.Value,
            AgentNickname = thread.AgentNickname,
            AgentRole = thread.AgentRole,
            CreatedAt = thread.CreatedAt,
            UpdatedAt = thread.UpdatedAt,
            IsEphemeral = thread.IsEphemeral,
            Status = thread.Status,
            ActiveFlags = thread.ActiveFlags,
            Lineage = thread.ParentThreadId is not null || thread.LineageDepth is not null
                ? new ControlPlaneAgentLineage
                {
                    ParentThreadId = thread.ParentThreadId,
                    Depth = thread.LineageDepth ?? 0,
                }
                : ToControlPlaneAgentLineage(thread.Source?.Value),
        };
    }

    private static ControlPlaneAgentLineage? ToControlPlaneAgentLineage(string? source)
    {
        var sessionSource = ControlPlaneSessionSource.TryParse(source, out var parsed) ? parsed : null;
        var subAgentSource = sessionSource?.SubAgentSource;
        if (subAgentSource is null)
        {
            return null;
        }

        return new ControlPlaneAgentLineage
        {
            ParentThreadId = ToOptionalThreadId(subAgentSource.ParentThreadId),
            Depth = subAgentSource.Depth,
        };
    }
}
