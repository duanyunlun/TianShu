namespace TianShu.Cli;

/// <summary>
/// CLI 本地线程 service tier 覆写模型，仅用于 northbound 参数解析与传输。
/// CLI-local thread service-tier override used only for northbound parsing and transport.
/// </summary>
internal sealed class CliServiceTierOverride : IEquatable<CliServiceTierOverride>
{
    private CliServiceTierOverride(bool isSpecified, string? value)
    {
        IsSpecified = isSpecified;
        Value = string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();
    }

    public static CliServiceTierOverride Unspecified { get; } = new(false, null);

    public static CliServiceTierOverride Clear { get; } = new(true, null);

    public bool IsSpecified { get; }

    public string? Value { get; }

    public bool IsCleared => IsSpecified && Value is null;

    public static CliServiceTierOverride FromValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new CliServiceTierOverride(true, value);
    }

    public bool Equals(CliServiceTierOverride? other)
        => other is not null
           && IsSpecified == other.IsSpecified
           && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is CliServiceTierOverride other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(IsSpecified, Value);

    public override string ToString()
        => !IsSpecified ? "<unspecified>" : Value ?? "<null>";
}
