using TianShu.Contracts.Primitives;

namespace TianShu.IdentityMemory;

/// <summary>
/// Identity / Memory 运行时上下文。
/// Runtime context for the identity and memory plane.
/// </summary>
public sealed record TianShuIdentityMemoryContext
{
    /// <summary>
    /// 初始化 Identity / Memory 运行时上下文。
    /// Initializes the runtime context for the identity and memory plane.
    /// </summary>
    public TianShuIdentityMemoryContext(
        string runtimeName,
        AccountId accountId,
        string accountDisplayName,
        string deviceName,
        string platform,
        string? workingDirectory = null,
        string? activeThreadId = null,
        string? teamKey = null,
        string? collaborationSpaceId = null,
        string? preferredVerbosity = null,
        IReadOnlyList<string>? preferredTools = null,
        DateTimeOffset? snapshotTime = null)
    {
        RuntimeName = NormalizeRequired(runtimeName, nameof(runtimeName));
        AccountId = accountId;
        AccountDisplayName = NormalizeRequired(accountDisplayName, nameof(accountDisplayName));
        DeviceName = NormalizeRequired(deviceName, nameof(deviceName));
        Platform = NormalizeRequired(platform, nameof(platform));
        WorkingDirectory = NormalizeOptional(workingDirectory);
        ActiveThreadId = NormalizeOptional(activeThreadId);
        TeamKey = NormalizeOptional(teamKey) ?? "local";
        CollaborationSpaceId = NormalizeOptional(collaborationSpaceId);
        PreferredVerbosity = NormalizeOptional(preferredVerbosity);
        PreferredTools = NormalizePreferredTools(preferredTools);
        SnapshotTime = snapshotTime ?? DateTimeOffset.UtcNow;
    }

    public string RuntimeName { get; }

    public AccountId AccountId { get; }

    public string AccountDisplayName { get; }

    public string DeviceName { get; }

    public string Platform { get; }

    public string? WorkingDirectory { get; }

    public string? ActiveThreadId { get; }

    public string TeamKey { get; }

    public string? CollaborationSpaceId { get; }

    public string? PreferredVerbosity { get; }

    public IReadOnlyList<string> PreferredTools { get; }

    public DateTimeOffset SnapshotTime { get; }

    private static string NormalizeRequired(string value, string paramName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("值不能为空。", paramName)
            : value.Trim();

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static IReadOnlyList<string> NormalizePreferredTools(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return ["shell_command"];
        }

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var normalized = NormalizeOptional(value);
            if (normalized is null || !seen.Add(normalized))
            {
                continue;
            }

            results.Add(normalized);
        }

        return results.Count == 0 ? ["shell_command"] : results;
    }
}
