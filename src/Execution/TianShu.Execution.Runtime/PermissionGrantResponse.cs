namespace TianShu.Execution.Runtime;

internal enum PermissionGrantScope
{
    Turn = 0,
    Session = 1,
}

internal sealed record PermissionGrantResponse(
    IReadOnlyDictionary<string, AgentStructuredValue> Permissions,
    PermissionGrantScope Scope = PermissionGrantScope.Turn)
{
    public static PermissionGrantResponse EmptyTurn { get; } = new(
        new Dictionary<string, AgentStructuredValue>(StringComparer.Ordinal),
        PermissionGrantScope.Turn);
}
