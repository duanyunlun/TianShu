using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.Contracts.Conversations;

/// <summary>
/// 控制平面会话来源。
/// Control-plane session source.
/// </summary>
[JsonConverter(typeof(ControlPlaneSessionSourceJsonConverter))]
public sealed class ControlPlaneSessionSource : IEquatable<ControlPlaneSessionSource>
{
    private ControlPlaneSessionSource(string value, ControlPlaneSubAgentSource? subAgentSource = null)
    {
        Value = value;
        SubAgentSource = subAgentSource;
    }

    public static ControlPlaneSessionSource Cli { get; } = new("cli");

    public static ControlPlaneSessionSource VsCode { get; } = new("vscode");

    public static ControlPlaneSessionSource Exec { get; } = new("exec");

    public static ControlPlaneSessionSource AppServer { get; } = new("appServer");

    public static ControlPlaneSessionSource Unknown { get; } = new("unknown");

    public string Value { get; }

    public ControlPlaneSubAgentSource? SubAgentSource { get; }

    public static ControlPlaneSessionSource SubAgent(ControlPlaneSubAgentSource subAgentSource)
    {
        ArgumentNullException.ThrowIfNull(subAgentSource);
        return new ControlPlaneSessionSource("subAgent", subAgentSource);
    }

    public static ControlPlaneSessionSource? FromJsonElement(JsonElement element)
        => TryRead(element, out var source) ? source : null;

    public static bool TryRead(JsonElement element, [NotNullWhen(true)] out ControlPlaneSessionSource? source)
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
                    || !ControlPlaneSubAgentSource.TryRead(subAgentElement, out var subAgentSource))
                {
                    return false;
                }

                source = SubAgent(subAgentSource);
                return true;
            default:
                return false;
        }
    }

    public static ControlPlaneSessionSource Parse(string value)
        => TryParse(value, out var source)
            ? source
            : throw new FormatException($"不支持的 session source：{value}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out ControlPlaneSessionSource? source)
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

    public static ControlPlaneSessionSource FromString(string? value)
        => TryParse(value, out var source) ? source : Unknown;

    public ControlPlaneThreadSourceKind GetThreadSourceKind()
        => SubAgentSource?.Kind switch
        {
            ControlPlaneSubAgentSourceKind.Review => ControlPlaneThreadSourceKind.SubAgentReview,
            ControlPlaneSubAgentSourceKind.Compact => ControlPlaneThreadSourceKind.SubAgentCompact,
            ControlPlaneSubAgentSourceKind.ThreadSpawn => ControlPlaneThreadSourceKind.SubAgentThreadSpawn,
            ControlPlaneSubAgentSourceKind.MemoryConsolidation => ControlPlaneThreadSourceKind.SubAgent,
            ControlPlaneSubAgentSourceKind.Other => ControlPlaneThreadSourceKind.SubAgentOther,
            _ when ReferenceEquals(this, Cli) => ControlPlaneThreadSourceKind.Cli,
            _ when ReferenceEquals(this, VsCode) => ControlPlaneThreadSourceKind.VsCode,
            _ when ReferenceEquals(this, Exec) => ControlPlaneThreadSourceKind.Exec,
            _ when ReferenceEquals(this, AppServer) => ControlPlaneThreadSourceKind.AppServer,
            _ when SubAgentSource is not null => ControlPlaneThreadSourceKind.SubAgent,
            _ => ControlPlaneThreadSourceKind.Unknown,
        };

    public string ToProtocolString()
        => Value;

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

    public bool Equals(ControlPlaneSessionSource? other)
        => other is not null
           && string.Equals(Value, other.Value, StringComparison.Ordinal)
           && EqualityComparer<ControlPlaneSubAgentSource?>.Default.Equals(SubAgentSource, other.SubAgentSource);

    public override bool Equals(object? obj)
        => obj switch
        {
            ControlPlaneSessionSource other => Equals(other),
            string text => MatchesString(text),
            _ => false,
        };

    public override int GetHashCode()
        => HashCode.Combine(Value, SubAgentSource);

    public override string ToString()
        => SubAgentSource is null
            ? Value
            : $"subagent_{SubAgentSource.ToLegacyValue()}";

    public static implicit operator ControlPlaneSessionSource?(string? value)
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

    private static bool TryParseLegacySubAgent(string? rawValue, [NotNullWhen(true)] out ControlPlaneSessionSource? source)
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

        source = SubAgent(ControlPlaneSubAgentSource.FromLegacyValue(suffix));
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

/// <summary>
/// 控制平面子代理来源类别。
/// Control-plane sub-agent source kind.
/// </summary>
public enum ControlPlaneSubAgentSourceKind
{
    Review = 0,
    Compact = 1,
    ThreadSpawn = 2,
    MemoryConsolidation = 3,
    Other = 4,
}

/// <summary>
/// 控制平面子代理来源。
/// Control-plane sub-agent source.
/// </summary>
public sealed class ControlPlaneSubAgentSource : IEquatable<ControlPlaneSubAgentSource>
{
    private ControlPlaneSubAgentSource(
        ControlPlaneSubAgentSourceKind kind,
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

    public static ControlPlaneSubAgentSource Review { get; } = new(ControlPlaneSubAgentSourceKind.Review);

    public static ControlPlaneSubAgentSource Compact { get; } = new(ControlPlaneSubAgentSourceKind.Compact);

    public static ControlPlaneSubAgentSource MemoryConsolidation { get; } = new(ControlPlaneSubAgentSourceKind.MemoryConsolidation);

    public ControlPlaneSubAgentSourceKind Kind { get; }

    public string? ParentThreadId { get; }

    public int? Depth { get; }

    public string? AgentNickname { get; }

    public string? AgentRole { get; }

    public string? OtherValue { get; }

    public static ControlPlaneSubAgentSource ThreadSpawn(
        string parentThreadId,
        int depth,
        string? agentNickname = null,
        string? agentRole = null)
        => new(ControlPlaneSubAgentSourceKind.ThreadSpawn, parentThreadId, depth, agentNickname, agentRole);

    public static ControlPlaneSubAgentSource Other(string? value)
        => new(ControlPlaneSubAgentSourceKind.Other, otherValue: Normalize(value) ?? "unknown");

    public static ControlPlaneSubAgentSource FromLegacyValue(string value)
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

    public static bool TryRead(JsonElement element, [NotNullWhen(true)] out ControlPlaneSubAgentSource? source)
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
            case ControlPlaneSubAgentSourceKind.Review:
                writer.WriteStringValue("review");
                return;
            case ControlPlaneSubAgentSourceKind.Compact:
                writer.WriteStringValue("compact");
                return;
            case ControlPlaneSubAgentSourceKind.MemoryConsolidation:
                writer.WriteStringValue("memory_consolidation");
                return;
            case ControlPlaneSubAgentSourceKind.ThreadSpawn:
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
            case ControlPlaneSubAgentSourceKind.Other:
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
            ControlPlaneSubAgentSourceKind.Review => "review",
            ControlPlaneSubAgentSourceKind.Compact => "compact",
            ControlPlaneSubAgentSourceKind.MemoryConsolidation => "memory_consolidation",
            ControlPlaneSubAgentSourceKind.ThreadSpawn => OtherValue
                ?? $"thread_spawn_{ParentThreadId ?? "unknown"}_d{Depth ?? 0}",
            ControlPlaneSubAgentSourceKind.Other => OtherValue ?? "unknown",
            _ => "unknown",
        };

    public bool Equals(ControlPlaneSubAgentSource? other)
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
            ControlPlaneSubAgentSource other => Equals(other),
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

internal sealed class ControlPlaneSessionSourceJsonConverter : JsonConverter<ControlPlaneSessionSource>
{
    public override ControlPlaneSessionSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var source = ControlPlaneSessionSource.FromJsonElement(document.RootElement);
        if (source is null)
        {
            throw new JsonException("sessionSource/source 格式无效。");
        }

        return source;
    }

    public override void Write(Utf8JsonWriter writer, ControlPlaneSessionSource value, JsonSerializerOptions options)
        => value.Write(writer);
}

/// <summary>
/// 控制平面线程来源类别。
/// Control-plane thread source kind.
/// </summary>
[JsonConverter(typeof(ControlPlaneThreadSourceKindJsonConverter))]
public sealed class ControlPlaneThreadSourceKind : IEquatable<ControlPlaneThreadSourceKind>
{
    private ControlPlaneThreadSourceKind(string value)
    {
        Value = value;
    }

    public static ControlPlaneThreadSourceKind Cli { get; } = new("cli");

    public static ControlPlaneThreadSourceKind VsCode { get; } = new("vscode");

    public static ControlPlaneThreadSourceKind Exec { get; } = new("exec");

    public static ControlPlaneThreadSourceKind AppServer { get; } = new("appServer");

    public static ControlPlaneThreadSourceKind SubAgent { get; } = new("subAgent");

    public static ControlPlaneThreadSourceKind SubAgentReview { get; } = new("subAgentReview");

    public static ControlPlaneThreadSourceKind SubAgentCompact { get; } = new("subAgentCompact");

    public static ControlPlaneThreadSourceKind SubAgentThreadSpawn { get; } = new("subAgentThreadSpawn");

    public static ControlPlaneThreadSourceKind SubAgentOther { get; } = new("subAgentOther");

    public static ControlPlaneThreadSourceKind Unknown { get; } = new("unknown");

    public string Value { get; }

    public static ControlPlaneThreadSourceKind Parse(string value)
        => TryParse(value, out var kind)
            ? kind
            : throw new FormatException($"不支持的 thread source kind：{value}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out ControlPlaneThreadSourceKind? kind)
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

    public static ControlPlaneThreadSourceKind FromString(string? value)
        => TryParse(value, out var kind) ? kind : Unknown;

    public string ToProtocolString()
        => Value;

    public bool Matches(ControlPlaneSessionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (Equals(SubAgent))
        {
            return source.SubAgentSource is not null;
        }

        if (Equals(SubAgentReview))
        {
            return source.SubAgentSource?.Kind == ControlPlaneSubAgentSourceKind.Review;
        }

        if (Equals(SubAgentCompact))
        {
            return source.SubAgentSource?.Kind == ControlPlaneSubAgentSourceKind.Compact;
        }

        if (Equals(SubAgentThreadSpawn))
        {
            return source.SubAgentSource?.Kind == ControlPlaneSubAgentSourceKind.ThreadSpawn;
        }

        if (Equals(SubAgentOther))
        {
            return source.SubAgentSource?.Kind == ControlPlaneSubAgentSourceKind.Other;
        }

        return Equals(source.GetThreadSourceKind());
    }

    public bool Equals(ControlPlaneThreadSourceKind? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj switch
        {
            ControlPlaneThreadSourceKind other => Equals(other),
            string text => TryParse(text, out var parsed) && Equals(parsed),
            _ => false,
        };

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString()
        => Value;

    public static implicit operator ControlPlaneThreadSourceKind?(string? value)
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

internal sealed class ControlPlaneThreadSourceKindJsonConverter : JsonConverter<ControlPlaneThreadSourceKind>
{
    public override ControlPlaneThreadSourceKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("sourceKind 必须是字符串。");
        }

        var value = reader.GetString();
        if (!ControlPlaneThreadSourceKind.TryParse(value, out var kind) || kind is null)
        {
            throw new JsonException($"不支持的 thread source kind：{value}");
        }

        return kind;
    }

    public override void Write(Utf8JsonWriter writer, ControlPlaneThreadSourceKind value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToProtocolString());
}
