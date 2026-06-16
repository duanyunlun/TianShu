using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.Execution.Runtime;

[JsonConverter(typeof(KernelSessionSourceJsonConverter))]
internal sealed class KernelSessionSource : IEquatable<KernelSessionSource>
{
    private KernelSessionSource(string value, KernelSubAgentSource? subAgentSource = null)
    {
        Value = value;
        SubAgentSource = subAgentSource;
    }

    public static KernelSessionSource Cli { get; } = new("cli");

    public static KernelSessionSource VsCode { get; } = new("vscode");

    public static KernelSessionSource Exec { get; } = new("exec");

    public static KernelSessionSource AppServer { get; } = new("appServer");

    public static KernelSessionSource Unknown { get; } = new("unknown");

    public string Value { get; }

    public KernelSubAgentSource? SubAgentSource { get; }

    public static KernelSessionSource SubAgent(KernelSubAgentSource subAgentSource)
    {
        ArgumentNullException.ThrowIfNull(subAgentSource);
        return new KernelSessionSource("subAgent", subAgentSource);
    }

    public static KernelSessionSource FromJsonElement(JsonElement element)
        => TryRead(element, out var source)
            ? source
            : throw new JsonException("sessionSource/source 格式无效。");

    public static bool TryRead(JsonElement element, [NotNullWhen(true)] out KernelSessionSource? source)
    {
        source = null;
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return false;
            case JsonValueKind.String:
                source = FromString(element.GetString());
                return true;
            case JsonValueKind.Object:
                if (!element.TryGetProperty("subAgent", out var subAgentElement)
                    || !KernelSubAgentSource.TryRead(subAgentElement, out var subAgentSource))
                {
                    return false;
                }

                source = SubAgent(subAgentSource);
                return true;
            default:
                return false;
        }
    }

    public static KernelSessionSource Parse(string value)
        => TryParse(value, out var source)
            ? source
            : throw new FormatException($"不支持的 session source：{value}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out KernelSessionSource? source)
    {
        source = null;
        var normalized = NormalizeToken(value);
        if (normalized is null)
        {
            return false;
        }

        switch (normalized)
        {
            case "cli":
                source = Cli;
                return true;
            case "vscode":
                source = VsCode;
                return true;
            case "exec":
                source = Exec;
                return true;
            case "appserver":
            case "mcp":
            case "configtoml":
                source = AppServer;
                return true;
            case "unknown":
                source = Unknown;
                return true;
        }

        if (TryParseLegacySubAgent(value, out source))
        {
            return true;
        }

        return false;
    }

    public static KernelSessionSource FromString(string? value)
        => TryParse(value, out var source) ? source : Unknown;

    public KernelThreadSourceKind GetThreadSourceKind()
        => SubAgentSource?.Kind switch
        {
            KernelSubAgentSourceKind.Review => KernelThreadSourceKind.SubAgentReview,
            KernelSubAgentSourceKind.Compact => KernelThreadSourceKind.SubAgentCompact,
            KernelSubAgentSourceKind.ThreadSpawn => KernelThreadSourceKind.SubAgentThreadSpawn,
            KernelSubAgentSourceKind.MemoryConsolidation => KernelThreadSourceKind.SubAgent,
            KernelSubAgentSourceKind.Other => KernelThreadSourceKind.SubAgentOther,
            _ when ReferenceEquals(this, Cli) => KernelThreadSourceKind.Cli,
            _ when ReferenceEquals(this, VsCode) => KernelThreadSourceKind.VsCode,
            _ when ReferenceEquals(this, Exec) => KernelThreadSourceKind.Exec,
            _ when ReferenceEquals(this, AppServer) => KernelThreadSourceKind.AppServer,
            _ when SubAgentSource is not null => KernelThreadSourceKind.SubAgent,
            _ => KernelThreadSourceKind.Unknown,
        };

    public int GetThreadSpawnDepth()
        => SubAgentSource?.Depth ?? 0;

    public KernelSessionSource WithThreadSpawnMetadata(string? agentNickname, string? agentRole)
    {
        if (SubAgentSource is not { Kind: KernelSubAgentSourceKind.ThreadSpawn } threadSpawn)
        {
            return this;
        }

        var normalizedNickname = Normalize(agentNickname);
        var normalizedRole = Normalize(agentRole);
        if (string.Equals(threadSpawn.AgentNickname, normalizedNickname, StringComparison.Ordinal)
            && string.Equals(threadSpawn.AgentRole, normalizedRole, StringComparison.Ordinal))
        {
            return this;
        }

        return threadSpawn.ParentThreadId is { } parentThreadId && threadSpawn.Depth is { } depth
            ? SubAgent(KernelSubAgentSource.ThreadSpawn(
                parentThreadId,
                depth,
                normalizedNickname ?? threadSpawn.AgentNickname,
                normalizedRole ?? threadSpawn.AgentRole))
            : this;
    }

    public bool Matches(KernelThreadSourceKind kind)
        => kind.Matches(this);

    public void Write(Utf8JsonWriter writer)
    {
        if (SubAgentSource is null)
        {
            writer.WriteStringValue(Value);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("subAgent");
        SubAgentSource.Write(writer);
        writer.WriteEndObject();
    }

    public bool Equals(KernelSessionSource? other)
        => other is not null
           && string.Equals(Value, other.Value, StringComparison.Ordinal)
           && EqualityComparer<KernelSubAgentSource?>.Default.Equals(SubAgentSource, other.SubAgentSource);

    public override bool Equals(object? obj)
        => obj switch
        {
            KernelSessionSource other => Equals(other),
            string text => MatchesString(text),
            _ => false,
        };

    public override int GetHashCode()
        => HashCode.Combine(Value, SubAgentSource);

    public override string ToString()
        => SubAgentSource is null
            ? Value
            : $"subagent_{SubAgentSource.ToLegacyValue()}";

    public static implicit operator KernelSessionSource?(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : FromString(value);

    private bool MatchesString(string value)
    {
        if (SubAgentSource is null)
        {
            return TryParse(value, out var parsed)
                   && parsed.SubAgentSource is null
                   && string.Equals(Value, parsed.Value, StringComparison.Ordinal);
        }

        if (TryParse(value, out var parsedSource))
        {
            return Equals(parsedSource);
        }

        return string.Equals(
            Normalize(value),
            $"subagent_{SubAgentSource.ToLegacyValue()}",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseLegacySubAgent(string? rawValue, [NotNullWhen(true)] out KernelSessionSource? source)
    {
        source = null;
        var normalized = Normalize(rawValue);
        if (normalized is null)
        {
            return false;
        }

        var suffix = ExtractLegacySubAgentSuffix(normalized);
        if (suffix is null)
        {
            return false;
        }

        source = SubAgent(KernelSubAgentSource.FromLegacyValue(suffix));
        return true;
    }

    private static string? ExtractLegacySubAgentSuffix(string value)
    {
        if (value.StartsWith("subagent_", StringComparison.OrdinalIgnoreCase))
        {
            return value["subagent_".Length..];
        }

        if (value.StartsWith("subagent.", StringComparison.OrdinalIgnoreCase))
        {
            return value["subagent.".Length..];
        }

        if (value.StartsWith("subAgent.", StringComparison.Ordinal))
        {
            return value["subAgent.".Length..];
        }

        if (value.StartsWith("subAgent", StringComparison.Ordinal))
        {
            return value["subAgent".Length..];
        }

        return null;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? NormalizeToken(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        Span<char> buffer = stackalloc char[normalized.Length];
        var index = 0;
        foreach (var ch in normalized)
        {
            if (char.IsWhiteSpace(ch) || ch is '_' or '-' or '.')
            {
                continue;
            }

            buffer[index++] = char.ToLowerInvariant(ch);
        }

        return index == 0 ? null : new string(buffer[..index]);
    }
}

internal enum KernelSubAgentSourceKind
{
    Review = 0,
    Compact = 1,
    ThreadSpawn = 2,
    MemoryConsolidation = 3,
    Other = 4,
}

internal sealed class KernelSubAgentSource : IEquatable<KernelSubAgentSource>
{
    private KernelSubAgentSource(
        KernelSubAgentSourceKind kind,
        string? parentThreadId = null,
        int? depth = null,
        string? agentNickname = null,
        string? agentRole = null,
        string? otherValue = null)
    {
        Kind = kind;
        ParentThreadId = Normalize(parentThreadId);
        Depth = depth;
        AgentNickname = Normalize(agentNickname);
        AgentRole = Normalize(agentRole);
        OtherValue = Normalize(otherValue);
    }

    public static KernelSubAgentSource Review { get; } = new(KernelSubAgentSourceKind.Review);

    public static KernelSubAgentSource Compact { get; } = new(KernelSubAgentSourceKind.Compact);

    public static KernelSubAgentSource MemoryConsolidation { get; } = new(KernelSubAgentSourceKind.MemoryConsolidation);

    public KernelSubAgentSourceKind Kind { get; }

    public string? ParentThreadId { get; }

    public int? Depth { get; }

    public string? AgentNickname { get; }

    public string? AgentRole { get; }

    public string? OtherValue { get; }

    public static KernelSubAgentSource ThreadSpawn(
        string parentThreadId,
        int depth,
        string? agentNickname = null,
        string? agentRole = null)
        => new(KernelSubAgentSourceKind.ThreadSpawn, parentThreadId, depth, agentNickname, agentRole);

    public static KernelSubAgentSource Other(string? value)
        => new(KernelSubAgentSourceKind.Other, otherValue: Normalize(value) ?? "unknown");

    public static KernelSubAgentSource FromLegacyValue(string value)
    {
        var normalized = Normalize(value) ?? string.Empty;
        var token = NormalizeToken(value);
        return token switch
        {
            "review" => Review,
            "compact" => Compact,
            "memoryconsolidation" => MemoryConsolidation,
            _ when token.StartsWith("threadspawn", StringComparison.Ordinal) => Other(normalized),
            _ => Other(normalized),
        };
    }

    public static bool TryRead(JsonElement element, [NotNullWhen(true)] out KernelSubAgentSource? source)
    {
        source = null;
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                source = FromLegacyValue(element.GetString() ?? string.Empty);
                return true;
            case JsonValueKind.Object:
                if (TryGetPropertyIgnoreCase(element, "thread_spawn", out var threadSpawn)
                    || TryGetPropertyIgnoreCase(element, "threadSpawn", out threadSpawn))
                {
                    if (threadSpawn.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }

                    var parentThreadId = ReadString(threadSpawn, "parent_thread_id", "parentThreadId");
                    var depth = ReadInt(threadSpawn, "depth");
                    if (string.IsNullOrWhiteSpace(parentThreadId) || depth is null)
                    {
                        return false;
                    }

                    source = ThreadSpawn(
                        parentThreadId,
                        depth.Value,
                        ReadString(threadSpawn, "agent_nickname", "agentNickname"),
                        ReadString(threadSpawn, "agent_role", "agentRole", "agent_type", "agentType"));
                    return true;
                }

                if (TryGetPropertyIgnoreCase(element, "other", out var otherValue))
                {
                    var otherText = otherValue.ValueKind == JsonValueKind.String
                        ? otherValue.GetString()
                        : otherValue.GetRawText();
                    source = Other(otherText);
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    public void Write(Utf8JsonWriter writer)
    {
        switch (Kind)
        {
            case KernelSubAgentSourceKind.Review:
                writer.WriteStringValue("review");
                return;
            case KernelSubAgentSourceKind.Compact:
                writer.WriteStringValue("compact");
                return;
            case KernelSubAgentSourceKind.MemoryConsolidation:
                writer.WriteStringValue("memory_consolidation");
                return;
            case KernelSubAgentSourceKind.ThreadSpawn:
                writer.WriteStartObject();
                writer.WritePropertyName("thread_spawn");
                writer.WriteStartObject();
                writer.WriteString("parent_thread_id", ParentThreadId);
                writer.WriteNumber("depth", Depth ?? 0);
                if (!string.IsNullOrWhiteSpace(AgentNickname))
                {
                    writer.WriteString("agent_nickname", AgentNickname);
                }

                if (!string.IsNullOrWhiteSpace(AgentRole))
                {
                    writer.WriteString("agent_role", AgentRole);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
                return;
            case KernelSubAgentSourceKind.Other:
                writer.WriteStartObject();
                writer.WriteString("other", OtherValue ?? "unknown");
                writer.WriteEndObject();
                return;
            default:
                throw new JsonException("不支持的 subAgent source。");
        }
    }

    public string ToLegacyValue()
        => Kind switch
        {
            KernelSubAgentSourceKind.Review => "review",
            KernelSubAgentSourceKind.Compact => "compact",
            KernelSubAgentSourceKind.MemoryConsolidation => "memory_consolidation",
            KernelSubAgentSourceKind.ThreadSpawn => OtherValue
                ?? $"thread_spawn_{ParentThreadId ?? "unknown"}_d{Depth ?? 0}",
            KernelSubAgentSourceKind.Other => OtherValue ?? "unknown",
            _ => "unknown",
        };

    public bool Equals(KernelSubAgentSource? other)
        => other is not null
           && Kind == other.Kind
           && string.Equals(ParentThreadId, other.ParentThreadId, StringComparison.Ordinal)
           && Depth == other.Depth
           && string.Equals(AgentNickname, other.AgentNickname, StringComparison.Ordinal)
           && string.Equals(AgentRole, other.AgentRole, StringComparison.Ordinal)
           && string.Equals(OtherValue, other.OtherValue, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj switch
        {
            KernelSubAgentSource other => Equals(other),
            string text => string.Equals(ToLegacyValue(), Normalize(text), StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    public override int GetHashCode()
        => HashCode.Combine(Kind, ParentThreadId, Depth, AgentNickname, AgentRole, OtherValue);

    public override string ToString()
        => ToLegacyValue();

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, params string[] candidateNames)
    {
        foreach (var candidateName in candidateNames)
        {
            if (!TryGetPropertyIgnoreCase(element, candidateName, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return Normalize(property.GetString());
            }
        }

        return null;
    }

    private static int? ReadInt(JsonElement element, params string[] candidateNames)
    {
        foreach (var candidateName in candidateNames)
        {
            if (!TryGetPropertyIgnoreCase(element, candidateName, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || ch is '_' or '-' or '.')
            {
                continue;
            }

            buffer[index++] = char.ToLowerInvariant(ch);
        }

        return index == 0 ? string.Empty : new string(buffer[..index]);
    }
}

internal sealed class KernelSessionSourceJsonConverter : JsonConverter<KernelSessionSource>
{
    public override KernelSessionSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        return KernelSessionSource.FromJsonElement(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, KernelSessionSource value, JsonSerializerOptions options)
        => value.Write(writer);
}

internal sealed class KernelThreadSourceKind : IEquatable<KernelThreadSourceKind>
{
    private KernelThreadSourceKind(string value)
    {
        Value = value;
    }

    public static KernelThreadSourceKind Cli { get; } = new("cli");

    public static KernelThreadSourceKind VsCode { get; } = new("vscode");

    public static KernelThreadSourceKind Exec { get; } = new("exec");

    public static KernelThreadSourceKind AppServer { get; } = new("appServer");

    public static KernelThreadSourceKind SubAgent { get; } = new("subAgent");

    public static KernelThreadSourceKind SubAgentReview { get; } = new("subAgentReview");

    public static KernelThreadSourceKind SubAgentCompact { get; } = new("subAgentCompact");

    public static KernelThreadSourceKind SubAgentThreadSpawn { get; } = new("subAgentThreadSpawn");

    public static KernelThreadSourceKind SubAgentOther { get; } = new("subAgentOther");

    public static KernelThreadSourceKind Unknown { get; } = new("unknown");

    public string Value { get; }

    public static KernelThreadSourceKind Parse(string value)
        => TryParse(value, out var kind)
            ? kind
            : throw new FormatException($"不支持的 thread source kind：{value}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out KernelThreadSourceKind? kind)
    {
        kind = null;
        var normalized = NormalizeToken(value);
        switch (normalized)
        {
            case "cli":
                kind = Cli;
                return true;
            case "vscode":
                kind = VsCode;
                return true;
            case "exec":
                kind = Exec;
                return true;
            case "appserver":
                kind = AppServer;
                return true;
            case "subagent":
                kind = SubAgent;
                return true;
            case "subagentreview":
                kind = SubAgentReview;
                return true;
            case "subagentcompact":
                kind = SubAgentCompact;
                return true;
            case "subagentthreadspawn":
                kind = SubAgentThreadSpawn;
                return true;
            case "subagentother":
                kind = SubAgentOther;
                return true;
            case "unknown":
                kind = Unknown;
                return true;
            default:
                return false;
        }
    }

    public static KernelThreadSourceKind FromString(string? value)
        => TryParse(value, out var kind) ? kind : Unknown;

    public bool Matches(KernelSessionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (Equals(SubAgent))
        {
            return source.SubAgentSource is not null;
        }

        if (Equals(SubAgentReview))
        {
            return source.SubAgentSource?.Kind == KernelSubAgentSourceKind.Review;
        }

        if (Equals(SubAgentCompact))
        {
            return source.SubAgentSource?.Kind == KernelSubAgentSourceKind.Compact;
        }

        if (Equals(SubAgentThreadSpawn))
        {
            return source.SubAgentSource?.Kind == KernelSubAgentSourceKind.ThreadSpawn;
        }

        if (Equals(SubAgentOther))
        {
            return source.SubAgentSource?.Kind == KernelSubAgentSourceKind.Other;
        }

        return Equals(source.GetThreadSourceKind());
    }

    public bool Equals(KernelThreadSourceKind? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj switch
        {
            KernelThreadSourceKind other => Equals(other),
            string text => TryParse(text, out var parsed) && Equals(parsed),
            _ => false,
        };

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString()
        => Value;

    public static implicit operator KernelThreadSourceKind?(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : FromString(value);

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || ch is '_' or '-' or '.')
            {
                continue;
            }

            buffer[index++] = char.ToLowerInvariant(ch);
        }

        return index == 0 ? null : new string(buffer[..index]);
    }
}
