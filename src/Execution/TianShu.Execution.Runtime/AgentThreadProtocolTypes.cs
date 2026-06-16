using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.Execution.Runtime;

[JsonConverter(typeof(AgentServiceTierJsonConverter))]
public sealed class AgentServiceTier : IEquatable<AgentServiceTier>
{
    private AgentServiceTier(string value)
    {
        Value = value;
    }

    public static AgentServiceTier Fast { get; } = new("fast");

    public static AgentServiceTier Flex { get; } = new("flex");

    public string Value { get; }

    public static AgentServiceTier Parse(string value)
        => TryParse(value, out var tier)
            ? tier
            : throw new FormatException($"不支持的 serviceTier：{value}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out AgentServiceTier? tier)
    {
        var normalized = Normalize(value);
        switch (normalized)
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

    public bool Equals(AgentServiceTier? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is AgentServiceTier other && Equals(other);

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString()
        => Value;

    public static implicit operator AgentServiceTier?(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Parse(value);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

[JsonConverter(typeof(AgentServiceTierOverrideJsonConverter))]
public sealed class AgentServiceTierOverride : IEquatable<AgentServiceTierOverride>
{
    private AgentServiceTierOverride(bool isSpecified, AgentServiceTier? value)
    {
        IsSpecified = isSpecified;
        Value = value;
    }

    public static AgentServiceTierOverride Unspecified { get; } = new(false, null);

    public static AgentServiceTierOverride Clear { get; } = new(true, null);

    public bool IsSpecified { get; }

    public AgentServiceTier? Value { get; }

    public bool IsCleared => IsSpecified && Value is null;

    public static AgentServiceTierOverride FromValue(AgentServiceTier value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new AgentServiceTierOverride(true, value);
    }

    public bool Equals(AgentServiceTierOverride? other)
        => other is not null
           && IsSpecified == other.IsSpecified
           && EqualityComparer<AgentServiceTier?>.Default.Equals(Value, other.Value);

    public override bool Equals(object? obj)
        => obj is AgentServiceTierOverride other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(IsSpecified, Value);

    public override string ToString()
        => !IsSpecified ? "<unspecified>" : Value?.Value ?? "<null>";

    public static implicit operator AgentServiceTierOverride(string? value)
        => string.IsNullOrWhiteSpace(value) ? Unspecified : FromValue(AgentServiceTier.Parse(value));

    public static implicit operator AgentServiceTierOverride(AgentServiceTier value)
        => FromValue(value);
}

[JsonConverter(typeof(AgentPersonalityJsonConverter))]
public sealed class AgentPersonality : IEquatable<AgentPersonality>
{
    private AgentPersonality(string value)
    {
        Value = value;
    }

    public static AgentPersonality None { get; } = new("none");

    public static AgentPersonality Friendly { get; } = new("friendly");

    public static AgentPersonality Pragmatic { get; } = new("pragmatic");

    public string Value { get; }

    public static AgentPersonality Parse(string value)
        => TryParse(value, out var personality)
            ? personality
            : throw new FormatException($"不支持的 personality：{value}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out AgentPersonality? personality)
    {
        var normalized = Normalize(value);
        switch (normalized)
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

    public bool Equals(AgentPersonality? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is AgentPersonality other && Equals(other);

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString()
        => Value;

    public static implicit operator AgentPersonality?(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Parse(value);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

public sealed class AgentApprovalGranularPolicy : IEquatable<AgentApprovalGranularPolicy>
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

    public bool Equals(AgentApprovalGranularPolicy? other)
        => other is not null
           && SandboxApproval == other.SandboxApproval
           && Rules == other.Rules
           && SkillApproval == other.SkillApproval
           && RequestPermissions == other.RequestPermissions
           && McpElicitations == other.McpElicitations;

    public override bool Equals(object? obj)
        => obj is AgentApprovalGranularPolicy other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(SandboxApproval, Rules, SkillApproval, RequestPermissions, McpElicitations);
}

[JsonConverter(typeof(AgentApprovalPolicyJsonConverter))]
public sealed class AgentApprovalPolicy : IEquatable<AgentApprovalPolicy>
{
    private AgentApprovalPolicy(string scalarValue, AgentApprovalGranularPolicy? granularPolicy = null)
    {
        ScalarValue = scalarValue;
        GranularPolicy = granularPolicy;
    }

    public static AgentApprovalPolicy Untrusted { get; } = new("untrusted");

    public static AgentApprovalPolicy OnFailure { get; } = new("on-failure");

    public static AgentApprovalPolicy OnRequest { get; } = new("on-request");

    public static AgentApprovalPolicy Never { get; } = new("never");

    public string ScalarValue { get; }

    public AgentApprovalGranularPolicy? GranularPolicy { get; }

    public bool IsGranular => GranularPolicy is not null;

    public static AgentApprovalPolicy FromGranular(AgentApprovalGranularPolicy granularPolicy)
    {
        ArgumentNullException.ThrowIfNull(granularPolicy);
        return new AgentApprovalPolicy("granular", granularPolicy);
    }

    public static AgentApprovalPolicy Parse(string value)
        => TryParse(value, out var policy)
            ? policy
            : throw new FormatException($"不支持的 approvalPolicy：{value}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out AgentApprovalPolicy? policy)
    {
        var normalized = Normalize(value);
        switch (normalized)
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

    internal object ToPlainObject()
        => GranularPolicy is null
            ? ScalarValue
            : new Dictionary<string, object?>
            {
                ["granular"] = GranularPolicy.ToPlainObject(),
            };

    public bool Equals(AgentApprovalPolicy? other)
        => other is not null
           && string.Equals(ScalarValue, other.ScalarValue, StringComparison.Ordinal)
           && EqualityComparer<AgentApprovalGranularPolicy?>.Default.Equals(GranularPolicy, other.GranularPolicy);

    public override bool Equals(object? obj)
        => obj is AgentApprovalPolicy other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(ScalarValue, GranularPolicy);

    public override string ToString()
        => GranularPolicy is null
            ? ScalarValue
            : JsonSerializer.Serialize(ToPlainObject());

    public static implicit operator AgentApprovalPolicy?(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Parse(value);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

public sealed class AgentDynamicToolSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("inputSchema")]
    public AgentStructuredValue InputSchema { get; init; } = AgentStructuredValue.Null;

    internal object ToPlainObject()
        => new Dictionary<string, object?>
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = InputSchema.ToPlainObject(),
        };
}

public sealed class AgentResponseItem
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("content")]
    public IReadOnlyList<AgentStructuredValue>? Content { get; init; }

    [JsonPropertyName("end_turn")]
    public bool? EndTurn { get; init; }

    [JsonPropertyName("phase")]
    public string? Phase { get; init; }

    [JsonPropertyName("summary")]
    public IReadOnlyList<AgentStructuredValue>? Summary { get; init; }

    [JsonPropertyName("encrypted_content")]
    public string? EncryptedContent { get; init; }

    [JsonPropertyName("call_id")]
    public string? CallId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("action")]
    public AgentStructuredValue? Action { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("namespace")]
    public string? Namespace { get; init; }

    [JsonPropertyName("arguments")]
    public AgentStructuredValue? Arguments { get; init; }

    [JsonPropertyName("execution")]
    public string? Execution { get; init; }

    [JsonPropertyName("output")]
    public AgentStructuredValue? Output { get; init; }

    [JsonPropertyName("input")]
    public string? Input { get; init; }

    [JsonPropertyName("tools")]
    public IReadOnlyList<AgentStructuredValue>? Tools { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("revised_prompt")]
    public string? RevisedPrompt { get; init; }

    [JsonPropertyName("result")]
    public string? Result { get; init; }

    [JsonPropertyName("ghost_commit")]
    public AgentStructuredValue? GhostCommit { get; init; }

    internal object ToPlainObject()
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = Type,
        };

        AddIfNotNull(payload, "role", Role);
        AddStructuredArrayIfNotNull(payload, "content", Content);
        AddIfNotNull(payload, "end_turn", EndTurn);
        AddIfNotNull(payload, "phase", Phase);
        AddStructuredArrayIfNotNull(payload, "summary", Summary);
        AddIfNotNull(payload, "encrypted_content", EncryptedContent);
        AddIfNotNull(payload, "call_id", CallId);
        AddIfNotNull(payload, "status", Status);
        AddStructuredIfNotNull(payload, "action", Action);
        AddIfNotNull(payload, "name", Name);
        AddIfNotNull(payload, "namespace", Namespace);
        AddStructuredIfNotNull(payload, "arguments", Arguments);
        AddIfNotNull(payload, "execution", Execution);
        AddStructuredIfNotNull(payload, "output", Output);
        AddIfNotNull(payload, "input", Input);
        AddStructuredArrayIfNotNull(payload, "tools", Tools);
        AddIfNotNull(payload, "id", Id);
        AddIfNotNull(payload, "revised_prompt", RevisedPrompt);
        AddIfNotNull(payload, "result", Result);
        AddStructuredIfNotNull(payload, "ghost_commit", GhostCommit);

        return payload;
    }

    private static void AddIfNotNull(IDictionary<string, object?> payload, string key, object? value)
    {
        if (value is not null)
        {
            payload[key] = value;
        }
    }

    private static void AddStructuredIfNotNull(IDictionary<string, object?> payload, string key, AgentStructuredValue? value)
    {
        if (value is not null)
        {
            payload[key] = value.ToPlainObject();
        }
    }

    private static void AddStructuredArrayIfNotNull(
        IDictionary<string, object?> payload,
        string key,
        IReadOnlyList<AgentStructuredValue>? values)
    {
        if (values is not null)
        {
            payload[key] = values.Select(static item => item.ToPlainObject()).ToArray();
        }
    }
}

internal sealed class AgentServiceTierJsonConverter : JsonConverter<AgentServiceTier>
{
    public override AgentServiceTier? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("serviceTier 必须是字符串。");
        }

        return AgentServiceTier.Parse(reader.GetString() ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, AgentServiceTier value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

internal sealed class AgentServiceTierOverrideJsonConverter : JsonConverter<AgentServiceTierOverride>
{
    public override bool HandleNull => true;

    public override AgentServiceTierOverride Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return AgentServiceTierOverride.Clear;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("serviceTier 必须是字符串或 null。");
        }

        return AgentServiceTierOverride.FromValue(AgentServiceTier.Parse(reader.GetString() ?? string.Empty));
    }

    public override void Write(Utf8JsonWriter writer, AgentServiceTierOverride value, JsonSerializerOptions options)
    {
        if (!value.IsSpecified)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.Value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.Value);
    }
}

internal sealed class AgentPersonalityJsonConverter : JsonConverter<AgentPersonality>
{
    public override AgentPersonality? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("personality 必须是字符串。");
        }

        return AgentPersonality.Parse(reader.GetString() ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, AgentPersonality value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

internal sealed class AgentApprovalPolicyJsonConverter : JsonConverter<AgentApprovalPolicy>
{
    public override AgentApprovalPolicy? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return AgentApprovalPolicy.Parse(reader.GetString() ?? string.Empty);
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("granular", out var granular))
        {
            throw new JsonException("approvalPolicy 必须是字符串或 granular 对象。");
        }

        var policy = JsonSerializer.Deserialize<AgentApprovalGranularPolicy>(granular.GetRawText(), options);
        if (policy is null)
        {
            throw new JsonException("approvalPolicy.granular 解析失败。");
        }

        return AgentApprovalPolicy.FromGranular(policy);
    }

    public override void Write(Utf8JsonWriter writer, AgentApprovalPolicy value, JsonSerializerOptions options)
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
