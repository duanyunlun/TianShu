using TianShu.Cli.Interaction.Events;

namespace TianShu.Cli.Interaction.Projection;

internal sealed class ToolInvocationAccumulator
{
    private readonly Dictionary<string, ToolInvocationSnapshot> snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> latestSnapshotKeysByTool = new(StringComparer.OrdinalIgnoreCase);

    public ToolInvocationSnapshot Merge(ToolInvocationEvent toolEvent)
    {
        var snapshot = ToolInvocationSnapshot.FromEvent(toolEvent);
        var key = ResolveKey(snapshot);
        if (toolEvent.InvocationPhase == ToolInvocationPhase.Started)
        {
            snapshots[key] = snapshot;
            latestSnapshotKeysByTool[snapshot.ToolName] = key;
            return snapshot;
        }

        if (!snapshots.TryGetValue(key, out var previous)
            && latestSnapshotKeysByTool.TryGetValue(snapshot.ToolName, out var latestKey))
        {
            key = latestKey;
            snapshots.TryGetValue(key, out previous);
        }

        if (previous is null)
        {
            return snapshot;
        }

        snapshots.Remove(key);
        latestSnapshotKeysByTool.Remove(snapshot.ToolName);
        return snapshot with
        {
            InputText = Normalize(snapshot.InputText) ?? previous.InputText,
            OutputText = Normalize(snapshot.OutputText) ?? previous.OutputText,
            Status = Normalize(snapshot.Status) ?? previous.Status,
            Phase = Normalize(snapshot.Phase) ?? previous.Phase,
            Payload = snapshot.Payload?.MergeFallback(previous.Payload) ?? previous.Payload,
        };
    }

    private static string ResolveKey(ToolInvocationSnapshot snapshot)
        => Normalize(snapshot.CallId)
           ?? Normalize(snapshot.ItemId)
           ?? $"{snapshot.ToolName}:{snapshot.GetHashCode()}";

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record ToolInvocationSnapshot(
    string ToolName,
    string? CallId,
    string? ItemId,
    string? InputText,
    string? OutputText,
    string? Status,
    string? Phase,
    ToolInvocationPayload? Payload)
{
    public static ToolInvocationSnapshot FromEvent(ToolInvocationEvent toolEvent)
        => new(
            toolEvent.ToolName,
            toolEvent.CallId,
            toolEvent.ItemId,
            toolEvent.InputText,
            toolEvent.OutputText,
            toolEvent.Status,
            toolEvent.Phase,
            toolEvent.Payload);
}
