using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.VSSDK.VSExtension.Services;

/// <summary>
/// VSIX 内部使用的 sidecar 会话来源 typed carrier。
/// Internal typed carrier for sidecar session sources used only inside the VSIX.
/// </summary>
[JsonConverter(typeof(TianShuSidecarSessionSourceJsonConverter))]
internal sealed class TianShuSidecarSessionSource : IEquatable<TianShuSidecarSessionSource>
{
    private TianShuSidecarSessionSource(string value, TianShuSidecarSubAgentSource? subAgentSource = null)
    {
        Value = value;
        SubAgentSource = subAgentSource;
    }

    public static TianShuSidecarSessionSource Cli { get; } = new("cli");

    public static TianShuSidecarSessionSource VsCode { get; } = new("vscode");

    public static TianShuSidecarSessionSource Exec { get; } = new("exec");

    public static TianShuSidecarSessionSource AppServer { get; } = new("appServer");

    public static TianShuSidecarSessionSource Unknown { get; } = new("unknown");

    public string Value { get; }

    public TianShuSidecarSubAgentSource? SubAgentSource { get; }

    public static TianShuSidecarSessionSource SubAgent(TianShuSidecarSubAgentSource subAgentSource)
    {
        if (subAgentSource is null)
        {
            throw new ArgumentNullException(nameof(subAgentSource));
        }

        return new TianShuSidecarSessionSource("subAgent", subAgentSource);
    }

    public static TianShuSidecarSessionSource? FromJsonElement(JsonElement element)
        => TryRead(element, out var source) ? source : null;

    public static bool TryRead(JsonElement element, out TianShuSidecarSessionSource? source)
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
                if (!TryGetPropertyIgnoreCase(element, "subAgent", out var subAgentElement)
                    || !TianShuSidecarSubAgentSource.TryRead(subAgentElement, out var subAgentSource))
                {
                    return false;
                }

                source = SubAgent(subAgentSource!);
                return true;
            default:
                return false;
        }
    }

    public static TianShuSidecarSessionSource Parse(string value)
    {
        if (TryParse(value, out var source) && source is not null)
        {
            return source;
        }

        throw new FormatException($"不支持的 session source：{value}");
    }

    public static bool TryParse(string? value, out TianShuSidecarSessionSource? source)
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

    public static TianShuSidecarSessionSource FromString(string? value)
        => TryParse(value, out var source) && source is not null ? source : Unknown;

    public TianShuSidecarThreadSourceKind GetThreadSourceKind()
        => SubAgentSource?.Kind switch
        {
            TianShuSidecarSubAgentSourceKind.Review => TianShuSidecarThreadSourceKind.SubAgentReview,
            TianShuSidecarSubAgentSourceKind.Compact => TianShuSidecarThreadSourceKind.SubAgentCompact,
            TianShuSidecarSubAgentSourceKind.ThreadSpawn => TianShuSidecarThreadSourceKind.SubAgentThreadSpawn,
            TianShuSidecarSubAgentSourceKind.MemoryConsolidation => TianShuSidecarThreadSourceKind.SubAgent,
            TianShuSidecarSubAgentSourceKind.Other => TianShuSidecarThreadSourceKind.SubAgentOther,
            _ when ReferenceEquals(this, Cli) => TianShuSidecarThreadSourceKind.Cli,
            _ when ReferenceEquals(this, VsCode) => TianShuSidecarThreadSourceKind.VsCode,
            _ when ReferenceEquals(this, Exec) => TianShuSidecarThreadSourceKind.Exec,
            _ when ReferenceEquals(this, AppServer) => TianShuSidecarThreadSourceKind.AppServer,
            _ when SubAgentSource is not null => TianShuSidecarThreadSourceKind.SubAgent,
            _ => TianShuSidecarThreadSourceKind.Unknown,
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

    public bool Equals(TianShuSidecarSessionSource? other)
        => other is not null
           && string.Equals(Value, other.Value, StringComparison.Ordinal)
           && EqualityComparer<TianShuSidecarSubAgentSource?>.Default.Equals(SubAgentSource, other.SubAgentSource);

    public override bool Equals(object? obj)
        => obj switch
        {
            TianShuSidecarSessionSource other => Equals(other),
            string text => MatchesString(text),
            _ => false,
        };

    public override int GetHashCode()
        => ((StringComparer.Ordinal.GetHashCode(Value) * 397) ^ (SubAgentSource?.GetHashCode() ?? 0));

    public override string ToString()
        => SubAgentSource is null ? Value : $"subagent_{SubAgentSource.ToLegacyValue()}";

    public static implicit operator TianShuSidecarSessionSource?(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : FromString(value);

    private bool MatchesString(string value)
    {
        if (SubAgentSource is null)
        {
            return TryParse(value, out var parsed)
                   && parsed is not null
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

    private static bool TryParseLegacySubAgent(string? rawValue, out TianShuSidecarSessionSource? source)
    {
        source = null;
        var suffix = ExtractLegacySubAgentSuffix(rawValue);
        if (suffix is null)
        {
            return false;
        }

        source = SubAgent(TianShuSidecarSubAgentSource.FromLegacyValue(suffix));
        return true;
    }

    private static string? ExtractLegacySubAgentSuffix(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        if (normalized.StartsWith("subagent_", StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Substring("subagent_".Length);
        }

        if (normalized.StartsWith("subagent.", StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Substring("subagent.".Length);
        }

        if (normalized.StartsWith("subAgent.", StringComparison.Ordinal))
        {
            return normalized.Substring("subAgent.".Length);
        }

        if (normalized.StartsWith("subAgent", StringComparison.Ordinal))
        {
            return normalized.Substring("subAgent".Length);
        }

        return null;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value!.Trim();
    }

    private static string? NormalizeToken(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        var chars = new List<char>(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsWhiteSpace(ch) || ch == '_' || ch == '-' || ch == '.')
            {
                continue;
            }

            chars.Add(char.ToLowerInvariant(ch));
        }

        return chars.Count == 0 ? null : new string(chars.ToArray());
    }

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
}

/// <summary>
/// VSIX 内部使用的 sidecar 子代理来源类别。
/// Internal sub-agent source kind used by the VSIX sidecar projection.
/// </summary>
internal enum TianShuSidecarSubAgentSourceKind
{
    Review = 0,
    Compact = 1,
    ThreadSpawn = 2,
    MemoryConsolidation = 3,
    Other = 4,
}

/// <summary>
/// 线程派生型子代理来源明细。
/// Details for thread-spawn sub-agent sources.
/// </summary>
internal sealed class TianShuSidecarThreadSpawnSubAgentSource : IEquatable<TianShuSidecarThreadSpawnSubAgentSource>
{
    public TianShuSidecarThreadSpawnSubAgentSource(
        string parentThreadId,
        int depth,
        string? agentNickname = null,
        string? agentRole = null,
        string? parentTurnId = null,
        string? reason = null)
    {
        ParentThreadId = Normalize(parentThreadId) ?? string.Empty;
        Depth = depth;
        AgentNickname = Normalize(agentNickname);
        AgentRole = Normalize(agentRole);
        ParentTurnId = Normalize(parentTurnId);
        Reason = Normalize(reason);
    }

    public string ParentThreadId { get; }

    public int Depth { get; }

    public string? AgentNickname { get; }

    public string? AgentRole { get; }

    public string? ParentTurnId { get; }

    public string? Reason { get; }

    public bool Equals(TianShuSidecarThreadSpawnSubAgentSource? other)
        => other is not null
           && string.Equals(ParentThreadId, other.ParentThreadId, StringComparison.Ordinal)
           && Depth == other.Depth
           && string.Equals(AgentNickname, other.AgentNickname, StringComparison.Ordinal)
           && string.Equals(AgentRole, other.AgentRole, StringComparison.Ordinal)
           && string.Equals(ParentTurnId, other.ParentTurnId, StringComparison.Ordinal)
           && string.Equals(Reason, other.Reason, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is TianShuSidecarThreadSpawnSubAgentSource other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StringComparer.Ordinal.GetHashCode(ParentThreadId);
            hash = (hash * 397) ^ Depth;
            hash = (hash * 397) ^ (AgentNickname is null ? 0 : StringComparer.Ordinal.GetHashCode(AgentNickname));
            hash = (hash * 397) ^ (AgentRole is null ? 0 : StringComparer.Ordinal.GetHashCode(AgentRole));
            hash = (hash * 397) ^ (ParentTurnId is null ? 0 : StringComparer.Ordinal.GetHashCode(ParentTurnId));
            hash = (hash * 397) ^ (Reason is null ? 0 : StringComparer.Ordinal.GetHashCode(Reason));
            return hash;
        }
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value!.Trim();
    }
}

/// <summary>
/// VSIX 内部使用的 sidecar 子代理来源。
/// Internal sidecar sub-agent source used by the VSIX thread/session view.
/// </summary>
internal sealed class TianShuSidecarSubAgentSource : IEquatable<TianShuSidecarSubAgentSource>
{
    private TianShuSidecarSubAgentSource(
        TianShuSidecarSubAgentSourceKind kind,
        TianShuSidecarThreadSpawnSubAgentSource? threadSpawn = null,
        string? otherValue = null)
    {
        Kind = kind;
        ThreadSpawn = threadSpawn;
        OtherValue = Normalize(otherValue);
    }

    public static TianShuSidecarSubAgentSource Review { get; } = new(TianShuSidecarSubAgentSourceKind.Review);

    public static TianShuSidecarSubAgentSource Compact { get; } = new(TianShuSidecarSubAgentSourceKind.Compact);

    public static TianShuSidecarSubAgentSource MemoryConsolidation { get; } =
        new(TianShuSidecarSubAgentSourceKind.MemoryConsolidation);

    public TianShuSidecarSubAgentSourceKind Kind { get; }

    public TianShuSidecarThreadSpawnSubAgentSource? ThreadSpawn { get; }

    public string? OtherValue { get; }

    public string? ParentThreadId => ThreadSpawn?.ParentThreadId;

    public int? Depth => ThreadSpawn?.Depth;

    public string? AgentNickname => ThreadSpawn?.AgentNickname;

    public string? AgentRole => ThreadSpawn?.AgentRole;

    public static TianShuSidecarSubAgentSource CreateThreadSpawn(
        string parentThreadId,
        int depth,
        string? agentNickname = null,
        string? agentRole = null,
        string? parentTurnId = null,
        string? reason = null)
        => new(
            TianShuSidecarSubAgentSourceKind.ThreadSpawn,
            new TianShuSidecarThreadSpawnSubAgentSource(parentThreadId, depth, agentNickname, agentRole, parentTurnId, reason));

    public static TianShuSidecarSubAgentSource CreateOther(string? otherValue)
        => new(TianShuSidecarSubAgentSourceKind.Other, otherValue: Normalize(otherValue) ?? "other");

    public static TianShuSidecarSubAgentSource FromLegacyValue(string value)
    {
        switch (NormalizeToken(value))
        {
            case "review":
                return Review;
            case "compact":
                return Compact;
            case "memoryconsolidation":
                return MemoryConsolidation;
            default:
                return CreateOther(value);
        }
    }

    public static bool TryRead(JsonElement element, out TianShuSidecarSubAgentSource? source)
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
                    if (string.IsNullOrWhiteSpace(parentThreadId) || !depth.HasValue)
                    {
                        return false;
                    }

                    source = CreateThreadSpawn(
                        parentThreadId!,
                        depth.Value,
                        ReadString(threadSpawn, "agent_nickname", "agentNickname"),
                        ReadString(threadSpawn, "agent_role", "agentRole", "agent_type", "agentType"),
                        ReadString(threadSpawn, "parent_turn_id", "parentTurnId"),
                        ReadString(threadSpawn, "reason"));
                    return true;
                }

                if (TryGetPropertyIgnoreCase(element, "other", out var otherValue))
                {
                    var otherText = otherValue.ValueKind == JsonValueKind.String
                        ? otherValue.GetString()
                        : otherValue.GetRawText();
                    source = CreateOther(otherText);
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
            case TianShuSidecarSubAgentSourceKind.Review:
                writer.WriteStringValue("review");
                return;
            case TianShuSidecarSubAgentSourceKind.Compact:
                writer.WriteStringValue("compact");
                return;
            case TianShuSidecarSubAgentSourceKind.MemoryConsolidation:
                writer.WriteStringValue("memory_consolidation");
                return;
            case TianShuSidecarSubAgentSourceKind.ThreadSpawn:
                writer.WriteStartObject();
                writer.WritePropertyName("thread_spawn");
                writer.WriteStartObject();
                writer.WriteString("parent_thread_id", ThreadSpawn!.ParentThreadId);
                writer.WriteNumber("depth", ThreadSpawn.Depth);
                if (!string.IsNullOrWhiteSpace(ThreadSpawn.ParentTurnId))
                {
                    writer.WriteString("parent_turn_id", ThreadSpawn.ParentTurnId);
                }

                if (!string.IsNullOrWhiteSpace(ThreadSpawn.AgentNickname))
                {
                    writer.WriteString("agent_nickname", ThreadSpawn.AgentNickname);
                }

                if (!string.IsNullOrWhiteSpace(ThreadSpawn.AgentRole))
                {
                    writer.WriteString("agent_role", ThreadSpawn.AgentRole);
                }

                if (!string.IsNullOrWhiteSpace(ThreadSpawn.Reason))
                {
                    writer.WriteString("reason", ThreadSpawn.Reason);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
                return;
            case TianShuSidecarSubAgentSourceKind.Other:
                writer.WriteStartObject();
                writer.WriteString("other", OtherValue ?? "other");
                writer.WriteEndObject();
                return;
            default:
                throw new JsonException("不支持的 subAgent source。");
        }
    }

    public string ToLegacyValue()
        => Kind switch
        {
            TianShuSidecarSubAgentSourceKind.Review => "review",
            TianShuSidecarSubAgentSourceKind.Compact => "compact",
            TianShuSidecarSubAgentSourceKind.MemoryConsolidation => "memory_consolidation",
            TianShuSidecarSubAgentSourceKind.ThreadSpawn => OtherValue
                ?? $"thread_spawn_{ParentThreadId ?? "unknown"}_d{Depth ?? 0}",
            TianShuSidecarSubAgentSourceKind.Other => OtherValue ?? "unknown",
            _ => "unknown",
        };

    public bool Equals(TianShuSidecarSubAgentSource? other)
        => other is not null
           && Kind == other.Kind
           && EqualityComparer<TianShuSidecarThreadSpawnSubAgentSource?>.Default.Equals(ThreadSpawn, other.ThreadSpawn)
           && string.Equals(OtherValue, other.OtherValue, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj switch
        {
            TianShuSidecarSubAgentSource other => Equals(other),
            string text => string.Equals(ToLegacyValue(), Normalize(text), StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)Kind;
            hash = (hash * 397) ^ (ThreadSpawn?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (OtherValue is null ? 0 : StringComparer.Ordinal.GetHashCode(OtherValue));
            return hash;
        }
    }

    public override string ToString()
        => ThreadSpawn is not null ? "thread_spawn" : OtherValue ?? Kind.ToString();

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

        return value!.Trim();
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value!;
        var chars = new List<char>(text.Length);
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || ch == '_' || ch == '-' || ch == '.')
            {
                continue;
            }

            chars.Add(char.ToLowerInvariant(ch));
        }

        return chars.Count == 0 ? string.Empty : new string(chars.ToArray());
    }
}

internal sealed class TianShuSidecarSessionSourceJsonConverter : JsonConverter<TianShuSidecarSessionSource>
{
    public override TianShuSidecarSessionSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var source = TianShuSidecarSessionSource.FromJsonElement(document.RootElement);
        if (source is null)
        {
            throw new JsonException("sessionSource/source 格式无效。");
        }

        return source;
    }

    public override void Write(Utf8JsonWriter writer, TianShuSidecarSessionSource value, JsonSerializerOptions options)
        => value.Write(writer);
}

/// <summary>
/// VSIX 内部使用的线程来源类别。
/// Internal thread source kind used by the VSIX thread list filters.
/// </summary>
[JsonConverter(typeof(TianShuSidecarThreadSourceKindJsonConverter))]
internal sealed class TianShuSidecarThreadSourceKind : IEquatable<TianShuSidecarThreadSourceKind>
{
    private TianShuSidecarThreadSourceKind(string value)
    {
        Value = value;
    }

    public static TianShuSidecarThreadSourceKind Cli { get; } = new("cli");

    public static TianShuSidecarThreadSourceKind VsCode { get; } = new("vscode");

    public static TianShuSidecarThreadSourceKind Exec { get; } = new("exec");

    public static TianShuSidecarThreadSourceKind AppServer { get; } = new("appServer");

    public static TianShuSidecarThreadSourceKind SubAgent { get; } = new("subAgent");

    public static TianShuSidecarThreadSourceKind SubAgentReview { get; } = new("subAgentReview");

    public static TianShuSidecarThreadSourceKind SubAgentCompact { get; } = new("subAgentCompact");

    public static TianShuSidecarThreadSourceKind SubAgentThreadSpawn { get; } = new("subAgentThreadSpawn");

    public static TianShuSidecarThreadSourceKind SubAgentOther { get; } = new("subAgentOther");

    public static TianShuSidecarThreadSourceKind Unknown { get; } = new("unknown");

    public string Value { get; }

    public static TianShuSidecarThreadSourceKind Parse(string value)
    {
        if (TryParse(value, out var kind) && kind is not null)
        {
            return kind;
        }

        throw new FormatException($"不支持的 thread source kind：{value}");
    }

    public static bool TryParse(string? value, out TianShuSidecarThreadSourceKind? kind)
    {
        kind = null;
        switch (NormalizeToken(value))
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

    public static TianShuSidecarThreadSourceKind FromString(string? value)
        => TryParse(value, out var kind) && kind is not null ? kind : Unknown;

    public string ToProtocolString()
        => Value;

    public bool Matches(TianShuSidecarSessionSource source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (Equals(SubAgent))
        {
            return source.SubAgentSource is not null;
        }

        if (Equals(SubAgentReview))
        {
            return source.SubAgentSource?.Kind == TianShuSidecarSubAgentSourceKind.Review;
        }

        if (Equals(SubAgentCompact))
        {
            return source.SubAgentSource?.Kind == TianShuSidecarSubAgentSourceKind.Compact;
        }

        if (Equals(SubAgentThreadSpawn))
        {
            return source.SubAgentSource?.Kind == TianShuSidecarSubAgentSourceKind.ThreadSpawn;
        }

        if (Equals(SubAgentOther))
        {
            return source.SubAgentSource?.Kind == TianShuSidecarSubAgentSourceKind.Other;
        }

        return Equals(source.GetThreadSourceKind());
    }

    public bool Equals(TianShuSidecarThreadSourceKind? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj switch
        {
            TianShuSidecarThreadSourceKind other => Equals(other),
            string text => TryParse(text, out var parsed) && Equals(parsed),
            _ => false,
        };

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString()
        => Value;

    public static implicit operator TianShuSidecarThreadSourceKind?(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : FromString(value);

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var chars = new List<char>(value!.Length);
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || ch == '_' || ch == '-' || ch == '.')
            {
                continue;
            }

            chars.Add(char.ToLowerInvariant(ch));
        }

        return chars.Count == 0 ? null : new string(chars.ToArray());
    }
}

internal sealed class TianShuSidecarThreadSourceKindJsonConverter : JsonConverter<TianShuSidecarThreadSourceKind>
{
    public override TianShuSidecarThreadSourceKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("sourceKind 必须是字符串。");
        }

        var value = reader.GetString();
        if (!TianShuSidecarThreadSourceKind.TryParse(value, out var kind) || kind is null)
        {
            throw new JsonException($"不支持的 thread source kind：{value}");
        }

        return kind;
    }

    public override void Write(Utf8JsonWriter writer, TianShuSidecarThreadSourceKind value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToProtocolString());
}
