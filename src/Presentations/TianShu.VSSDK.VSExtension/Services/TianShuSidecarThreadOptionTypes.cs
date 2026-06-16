using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.VSSDK.VSExtension.Services;

/// <summary>
/// VSIX 本地线程配置相关 typed carrier。
/// VSIX-local typed carriers for thread/session configuration fields.
/// </summary>
[JsonConverter(typeof(TianShuSidecarServiceTierJsonConverter))]
internal sealed class TianShuSidecarServiceTier : IEquatable<TianShuSidecarServiceTier>
{
    private TianShuSidecarServiceTier(string value)
    {
        Value = value;
    }

    public static TianShuSidecarServiceTier Fast { get; } = new("fast");

    public static TianShuSidecarServiceTier Flex { get; } = new("flex");

    public string Value { get; }

    public static TianShuSidecarServiceTier Parse(string value)
    {
        if (TryParse(value, out var tier) && tier is not null)
        {
            return tier;
        }

        throw new FormatException($"不支持的 serviceTier：{value}");
    }

    public static bool TryParse(string? value, out TianShuSidecarServiceTier? tier)
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

    public bool Equals(TianShuSidecarServiceTier? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is TianShuSidecarServiceTier other && Equals(other);

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString()
        => Value;

    public static implicit operator TianShuSidecarServiceTier?(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Parse(value!);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value!.Trim().ToLowerInvariant();
    }
}

/// <summary>
/// VSIX 本地线程 service tier 覆写模型。
/// VSIX-local override model for thread service tier selection.
/// </summary>
[JsonConverter(typeof(TianShuSidecarServiceTierOverrideJsonConverter))]
internal sealed class TianShuSidecarServiceTierOverride : IEquatable<TianShuSidecarServiceTierOverride>
{
    private TianShuSidecarServiceTierOverride(bool isSpecified, TianShuSidecarServiceTier? value)
    {
        IsSpecified = isSpecified;
        Value = value;
    }

    public static TianShuSidecarServiceTierOverride Unspecified { get; } = new(false, null);

    public static TianShuSidecarServiceTierOverride Clear { get; } = new(true, null);

    public bool IsSpecified { get; }

    public TianShuSidecarServiceTier? Value { get; }

    public static TianShuSidecarServiceTierOverride FromValue(TianShuSidecarServiceTier value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new TianShuSidecarServiceTierOverride(true, value);
    }

    public bool Equals(TianShuSidecarServiceTierOverride? other)
        => other is not null
           && IsSpecified == other.IsSpecified
           && EqualityComparer<TianShuSidecarServiceTier?>.Default.Equals(Value, other.Value);

    public override bool Equals(object? obj)
        => obj is TianShuSidecarServiceTierOverride other && Equals(other);

    public override int GetHashCode()
        => ((IsSpecified ? 397 : 0) ^ (Value?.GetHashCode() ?? 0));

    public override string ToString()
        => !IsSpecified ? "<unspecified>" : Value?.Value ?? "<null>";

    public static implicit operator TianShuSidecarServiceTierOverride(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Unspecified;
        }

        return FromValue(TianShuSidecarServiceTier.Parse(value!));
    }

    public static implicit operator TianShuSidecarServiceTierOverride(TianShuSidecarServiceTier value)
        => FromValue(value);
}

/// <summary>
/// VSIX 本地 personality typed carrier。
/// VSIX-local typed carrier for thread/session personality metadata.
/// </summary>
[JsonConverter(typeof(TianShuSidecarPersonalityJsonConverter))]
internal sealed class TianShuSidecarPersonality : IEquatable<TianShuSidecarPersonality>
{
    private TianShuSidecarPersonality(string value)
    {
        Value = value;
    }

    public static TianShuSidecarPersonality None { get; } = new("none");

    public static TianShuSidecarPersonality Friendly { get; } = new("friendly");

    public static TianShuSidecarPersonality Pragmatic { get; } = new("pragmatic");

    public string Value { get; }

    public static TianShuSidecarPersonality Parse(string value)
    {
        if (TryParse(value, out var personality) && personality is not null)
        {
            return personality;
        }

        throw new FormatException($"不支持的 personality：{value}");
    }

    public static bool TryParse(string? value, out TianShuSidecarPersonality? personality)
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

    public bool Equals(TianShuSidecarPersonality? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is TianShuSidecarPersonality other && Equals(other);

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString()
        => Value;

    public static implicit operator TianShuSidecarPersonality?(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Parse(value!);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value!.Trim().ToLowerInvariant();
    }
}

/// <summary>
/// VSIX 本地 granular approval policy 模型。
/// VSIX-local granular approval policy model.
/// </summary>
internal sealed class TianShuSidecarApprovalGranularPolicy : IEquatable<TianShuSidecarApprovalGranularPolicy>
{
    [JsonPropertyName("sandbox_approval")]
    public bool SandboxApproval { get; set; }

    [JsonPropertyName("rules")]
    public bool Rules { get; set; }

    [JsonPropertyName("skill_approval")]
    public bool SkillApproval { get; set; }

    [JsonPropertyName("request_permissions")]
    public bool RequestPermissions { get; set; }

    [JsonPropertyName("mcp_elicitations")]
    public bool McpElicitations { get; set; }

    internal object ToPlainObject()
        => new Dictionary<string, object?>
        {
            ["sandbox_approval"] = SandboxApproval,
            ["rules"] = Rules,
            ["skill_approval"] = SkillApproval,
            ["request_permissions"] = RequestPermissions,
            ["mcp_elicitations"] = McpElicitations,
        };

    public bool Equals(TianShuSidecarApprovalGranularPolicy? other)
        => other is not null
           && SandboxApproval == other.SandboxApproval
           && Rules == other.Rules
           && SkillApproval == other.SkillApproval
           && RequestPermissions == other.RequestPermissions
           && McpElicitations == other.McpElicitations;

    public override bool Equals(object? obj)
        => obj is TianShuSidecarApprovalGranularPolicy other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + SandboxApproval.GetHashCode();
            hash = (hash * 31) + Rules.GetHashCode();
            hash = (hash * 31) + SkillApproval.GetHashCode();
            hash = (hash * 31) + RequestPermissions.GetHashCode();
            hash = (hash * 31) + McpElicitations.GetHashCode();
            return hash;
        }
    }
}

/// <summary>
/// VSIX 本地 approval policy typed carrier。
/// VSIX-local typed carrier for approval policy metadata.
/// </summary>
[JsonConverter(typeof(TianShuSidecarApprovalPolicyJsonConverter))]
internal sealed class TianShuSidecarApprovalPolicy : IEquatable<TianShuSidecarApprovalPolicy>
{
    private TianShuSidecarApprovalPolicy(
        string scalarValue,
        TianShuSidecarApprovalGranularPolicy? granularPolicy = null)
    {
        ScalarValue = scalarValue;
        GranularPolicy = granularPolicy;
    }

    public static TianShuSidecarApprovalPolicy Untrusted { get; } = new("untrusted");

    public static TianShuSidecarApprovalPolicy OnFailure { get; } = new("on-failure");

    public static TianShuSidecarApprovalPolicy OnRequest { get; } = new("on-request");

    public static TianShuSidecarApprovalPolicy Never { get; } = new("never");

    public string ScalarValue { get; }

    public TianShuSidecarApprovalGranularPolicy? GranularPolicy { get; }

    public bool IsGranular => GranularPolicy is not null;

    public static TianShuSidecarApprovalPolicy FromGranular(TianShuSidecarApprovalGranularPolicy granularPolicy)
    {
        if (granularPolicy is null)
        {
            throw new ArgumentNullException(nameof(granularPolicy));
        }

        return new TianShuSidecarApprovalPolicy("granular", granularPolicy);
    }

    public static TianShuSidecarApprovalPolicy Parse(string value)
    {
        if (TryParse(value, out var policy) && policy is not null)
        {
            return policy;
        }

        throw new FormatException($"不支持的 approvalPolicy：{value}");
    }

    public static bool TryParse(string? value, out TianShuSidecarApprovalPolicy? policy)
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

    public bool Equals(TianShuSidecarApprovalPolicy? other)
        => other is not null
           && string.Equals(ScalarValue, other.ScalarValue, StringComparison.Ordinal)
           && EqualityComparer<TianShuSidecarApprovalGranularPolicy?>.Default.Equals(GranularPolicy, other.GranularPolicy);

    public override bool Equals(object? obj)
        => obj is TianShuSidecarApprovalPolicy other && Equals(other);

    public override int GetHashCode()
        => ((StringComparer.Ordinal.GetHashCode(ScalarValue) * 397) ^ (GranularPolicy?.GetHashCode() ?? 0));

    public override string ToString()
    {
        if (!IsGranular)
        {
            return ScalarValue;
        }

        var granularPolicy = GranularPolicy;
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["granular"] = granularPolicy!.ToPlainObject(),
        });
    }

    public static implicit operator TianShuSidecarApprovalPolicy?(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Parse(value!);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value!.Trim().ToLowerInvariant();
    }
}

internal sealed class TianShuSidecarServiceTierJsonConverter : JsonConverter<TianShuSidecarServiceTier>
{
    public override TianShuSidecarServiceTier? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("serviceTier 必须是字符串。");
        }

        return TianShuSidecarServiceTier.Parse(reader.GetString() ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, TianShuSidecarServiceTier value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

internal sealed class TianShuSidecarServiceTierOverrideJsonConverter : JsonConverter<TianShuSidecarServiceTierOverride>
{
    public override bool HandleNull => true;

    public override TianShuSidecarServiceTierOverride Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return TianShuSidecarServiceTierOverride.Clear;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("serviceTier 必须是字符串或 null。");
        }

        return TianShuSidecarServiceTierOverride.FromValue(
            TianShuSidecarServiceTier.Parse(reader.GetString() ?? string.Empty));
    }

    public override void Write(
        Utf8JsonWriter writer,
        TianShuSidecarServiceTierOverride value,
        JsonSerializerOptions options)
    {
        if (!value.IsSpecified || value.Value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.Value);
    }
}

internal sealed class TianShuSidecarPersonalityJsonConverter : JsonConverter<TianShuSidecarPersonality>
{
    public override TianShuSidecarPersonality? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("personality 必须是字符串。");
        }

        return TianShuSidecarPersonality.Parse(reader.GetString() ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, TianShuSidecarPersonality value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

internal sealed class TianShuSidecarApprovalPolicyJsonConverter : JsonConverter<TianShuSidecarApprovalPolicy>
{
    public override TianShuSidecarApprovalPolicy? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return TianShuSidecarApprovalPolicy.Parse(reader.GetString() ?? string.Empty);
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("granular", out var granular))
        {
            throw new JsonException("approvalPolicy 必须是字符串或 granular 对象。");
        }

        var policy = JsonSerializer.Deserialize<TianShuSidecarApprovalGranularPolicy>(granular.GetRawText(), options);
        if (policy is null)
        {
            throw new JsonException("approvalPolicy.granular 解析失败。");
        }

        return TianShuSidecarApprovalPolicy.FromGranular(policy);
    }

    public override void Write(Utf8JsonWriter writer, TianShuSidecarApprovalPolicy value, JsonSerializerOptions options)
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
