using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using TianShu.Contracts.Primitives;

namespace TianShu.VSSDK.Sidecar;

/// <summary>
/// Sidecar 本地线程 service tier typed carrier。
/// Sidecar-local typed carrier for thread service tier metadata.
/// </summary>
[JsonConverter(typeof(SidecarServiceTierJsonConverter))]
internal sealed class SidecarServiceTier : IEquatable<SidecarServiceTier>
{
    private SidecarServiceTier(string value)
    {
        Value = value;
    }

    public static SidecarServiceTier Fast { get; } = new("fast");

    public static SidecarServiceTier Flex { get; } = new("flex");

    public string Value { get; }

    public static SidecarServiceTier Parse(string value)
        => TryParse(value, out var tier)
            ? tier
            : throw new FormatException($"不支持的 serviceTier：{value}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out SidecarServiceTier? tier)
    {
        switch (Normalize(value))
        {
            case "fast":
                tier = Fast;
                return true;
            case "flex":
                tier = Flex;
                return true;
            default:
                tier = null;
                return false;
        }
    }

    public bool Equals(SidecarServiceTier? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is SidecarServiceTier other && Equals(other);

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString()
        => Value;

    public static implicit operator SidecarServiceTier?(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Parse(value);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

/// <summary>
/// Sidecar 本地线程 service tier 覆写模型。
/// Sidecar-local override model for thread service tier selection.
/// </summary>
[JsonConverter(typeof(SidecarServiceTierOverrideJsonConverter))]
internal sealed class SidecarServiceTierOverride : IEquatable<SidecarServiceTierOverride>
{
    private SidecarServiceTierOverride(bool isSpecified, SidecarServiceTier? value)
    {
        IsSpecified = isSpecified;
        Value = value;
    }

    public static SidecarServiceTierOverride Unspecified { get; } = new(false, null);

    public static SidecarServiceTierOverride Clear { get; } = new(true, null);

    public bool IsSpecified { get; }

    public SidecarServiceTier? Value { get; }

    public bool IsCleared => IsSpecified && Value is null;

    public static SidecarServiceTierOverride FromValue(SidecarServiceTier value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new SidecarServiceTierOverride(true, value);
    }

    public bool Equals(SidecarServiceTierOverride? other)
        => other is not null
           && IsSpecified == other.IsSpecified
           && EqualityComparer<SidecarServiceTier?>.Default.Equals(Value, other.Value);

    public override bool Equals(object? obj)
        => obj is SidecarServiceTierOverride other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(IsSpecified, Value);

    public override string ToString()
        => !IsSpecified ? "<unspecified>" : Value?.Value ?? "<null>";

    public static implicit operator SidecarServiceTierOverride(string? value)
        => string.IsNullOrWhiteSpace(value) ? Unspecified : FromValue(SidecarServiceTier.Parse(value));

    public static implicit operator SidecarServiceTierOverride(SidecarServiceTier value)
        => FromValue(value);
}

/// <summary>
/// Sidecar 本地 personality typed carrier。
/// Sidecar-local typed carrier for thread/session personality metadata.
/// </summary>
[JsonConverter(typeof(SidecarPersonalityJsonConverter))]
internal sealed class SidecarPersonality : IEquatable<SidecarPersonality>
{
    private SidecarPersonality(string value)
    {
        Value = value;
    }

    public static SidecarPersonality None { get; } = new("none");

    public static SidecarPersonality Friendly { get; } = new("friendly");

    public static SidecarPersonality Pragmatic { get; } = new("pragmatic");

    public string Value { get; }

    public static SidecarPersonality Parse(string value)
        => TryParse(value, out var personality)
            ? personality
            : throw new FormatException($"不支持的 personality：{value}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out SidecarPersonality? personality)
    {
        switch (Normalize(value))
        {
            case "none":
                personality = None;
                return true;
            case "friendly":
                personality = Friendly;
                return true;
            case "pragmatic":
                personality = Pragmatic;
                return true;
            default:
                personality = null;
                return false;
        }
    }

    public bool Equals(SidecarPersonality? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is SidecarPersonality other && Equals(other);

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString()
        => Value;

    public static implicit operator SidecarPersonality?(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Parse(value);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

/// <summary>
/// Sidecar 本地 granular approval policy 模型。
/// Sidecar-local granular approval policy model.
/// </summary>
internal sealed class SidecarApprovalGranularPolicy : IEquatable<SidecarApprovalGranularPolicy>
{
    [JsonPropertyName("sandbox_approval")]
    public bool SandboxApproval { get; init; }

    [JsonPropertyName("rules")]
    public bool Rules { get; init; }

    [JsonPropertyName("skill_approval")]
    public bool SkillApproval { get; init; }

    [JsonPropertyName("request_permissions")]
    public bool RequestPermissions { get; init; }

    [JsonPropertyName("mcp_elicitations")]
    public bool McpElicitations { get; init; }

    internal object ToPlainObject()
        => new Dictionary<string, object?>
        {
            ["sandbox_approval"] = SandboxApproval,
            ["rules"] = Rules,
            ["skill_approval"] = SkillApproval,
            ["request_permissions"] = RequestPermissions,
            ["mcp_elicitations"] = McpElicitations,
        };

    public bool Equals(SidecarApprovalGranularPolicy? other)
        => other is not null
           && SandboxApproval == other.SandboxApproval
           && Rules == other.Rules
           && SkillApproval == other.SkillApproval
           && RequestPermissions == other.RequestPermissions
           && McpElicitations == other.McpElicitations;

    public override bool Equals(object? obj)
        => obj is SidecarApprovalGranularPolicy other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(SandboxApproval, Rules, SkillApproval, RequestPermissions, McpElicitations);
}

/// <summary>
/// Sidecar 本地 approval policy typed carrier。
/// Sidecar-local typed carrier for approval policy metadata.
/// </summary>
[JsonConverter(typeof(SidecarApprovalPolicyJsonConverter))]
internal sealed class SidecarApprovalPolicy : IEquatable<SidecarApprovalPolicy>
{
    private SidecarApprovalPolicy(string scalarValue, SidecarApprovalGranularPolicy? granularPolicy = null)
    {
        ScalarValue = scalarValue;
        GranularPolicy = granularPolicy;
    }

    public static SidecarApprovalPolicy Untrusted { get; } = new("untrusted");

    public static SidecarApprovalPolicy OnFailure { get; } = new("on-failure");

    public static SidecarApprovalPolicy OnRequest { get; } = new("on-request");

    public static SidecarApprovalPolicy Never { get; } = new("never");

    public string ScalarValue { get; }

    public SidecarApprovalGranularPolicy? GranularPolicy { get; }

    public bool IsGranular => GranularPolicy is not null;

    public static SidecarApprovalPolicy FromGranular(SidecarApprovalGranularPolicy granularPolicy)
    {
        ArgumentNullException.ThrowIfNull(granularPolicy);
        return new SidecarApprovalPolicy("granular", granularPolicy);
    }

    public static SidecarApprovalPolicy Parse(string value)
        => TryParse(value, out var policy)
            ? policy
            : throw new FormatException($"不支持的 approvalPolicy：{value}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out SidecarApprovalPolicy? policy)
    {
        switch (Normalize(value))
        {
            case "untrusted":
                policy = Untrusted;
                return true;
            case "on-failure":
                policy = OnFailure;
                return true;
            case "on-request":
                policy = OnRequest;
                return true;
            case "never":
                policy = Never;
                return true;
            default:
                policy = null;
                return false;
        }
    }

    public bool Equals(SidecarApprovalPolicy? other)
        => other is not null
           && string.Equals(ScalarValue, other.ScalarValue, StringComparison.Ordinal)
           && EqualityComparer<SidecarApprovalGranularPolicy?>.Default.Equals(GranularPolicy, other.GranularPolicy);

    public override bool Equals(object? obj)
        => obj is SidecarApprovalPolicy other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(StringComparer.Ordinal.GetHashCode(ScalarValue), GranularPolicy);

    public override string ToString()
    {
        if (!IsGranular)
        {
            return ScalarValue;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["granular"] = GranularPolicy!.ToPlainObject(),
        });
    }

    public static implicit operator SidecarApprovalPolicy?(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Parse(value);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

/// <summary>
/// Sidecar 本地线程 history 项，仅用于 stdio resume 请求载荷。
/// Sidecar-local thread history item used for stdio resume payloads.
/// </summary>
internal sealed class SidecarThreadHistoryItem
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("content")]
    public IReadOnlyList<StructuredValue>? Content { get; init; }

    [JsonPropertyName("end_turn")]
    public bool? EndTurn { get; init; }

    [JsonPropertyName("phase")]
    public string? Phase { get; init; }

    [JsonPropertyName("summary")]
    public IReadOnlyList<StructuredValue>? Summary { get; init; }

    [JsonPropertyName("encrypted_content")]
    public string? EncryptedContent { get; init; }

    [JsonPropertyName("call_id")]
    public string? CallId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("action")]
    public StructuredValue? Action { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("namespace")]
    public string? Namespace { get; init; }

    [JsonPropertyName("arguments")]
    public StructuredValue? Arguments { get; init; }

    [JsonPropertyName("execution")]
    public string? Execution { get; init; }

    [JsonPropertyName("output")]
    public StructuredValue? Output { get; init; }

    [JsonPropertyName("input")]
    public string? Input { get; init; }

    [JsonPropertyName("tools")]
    public IReadOnlyList<StructuredValue>? Tools { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("revised_prompt")]
    public string? RevisedPrompt { get; init; }

    [JsonPropertyName("result")]
    public string? Result { get; init; }

    [JsonPropertyName("ghost_commit")]
    public StructuredValue? GhostCommit { get; init; }
}

internal sealed class SidecarServiceTierJsonConverter : JsonConverter<SidecarServiceTier>
{
    public override SidecarServiceTier? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("serviceTier 必须是字符串。");
        }

        return SidecarServiceTier.Parse(reader.GetString() ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, SidecarServiceTier value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

internal sealed class SidecarServiceTierOverrideJsonConverter : JsonConverter<SidecarServiceTierOverride>
{
    public override bool HandleNull => true;

    public override SidecarServiceTierOverride Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return SidecarServiceTierOverride.Clear;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("serviceTier 必须是字符串或 null。");
        }

        return SidecarServiceTierOverride.FromValue(SidecarServiceTier.Parse(reader.GetString() ?? string.Empty));
    }

    public override void Write(Utf8JsonWriter writer, SidecarServiceTierOverride value, JsonSerializerOptions options)
    {
        if (!value.IsSpecified || value.Value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.Value);
    }
}

internal sealed class SidecarPersonalityJsonConverter : JsonConverter<SidecarPersonality>
{
    public override SidecarPersonality? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("personality 必须是字符串。");
        }

        return SidecarPersonality.Parse(reader.GetString() ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, SidecarPersonality value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

internal sealed class SidecarApprovalPolicyJsonConverter : JsonConverter<SidecarApprovalPolicy>
{
    public override SidecarApprovalPolicy? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return SidecarApprovalPolicy.Parse(reader.GetString() ?? string.Empty);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("approvalPolicy 必须是字符串或对象。");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!root.TryGetProperty("granular", out var granularElement))
        {
            throw new JsonException("approvalPolicy 对象必须包含 granular。");
        }

        var granularPolicy = JsonSerializer.Deserialize<SidecarApprovalGranularPolicy>(granularElement.GetRawText(), options)
                             ?? throw new JsonException("approvalPolicy.granular 无法解析。");
        return SidecarApprovalPolicy.FromGranular(granularPolicy);
    }

    public override void Write(Utf8JsonWriter writer, SidecarApprovalPolicy value, JsonSerializerOptions options)
    {
        if (!value.IsGranular)
        {
            writer.WriteStringValue(value.ScalarValue);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("granular");
        JsonSerializer.Serialize(writer, value.GranularPolicy, options);
        writer.WriteEndObject();
    }
}
