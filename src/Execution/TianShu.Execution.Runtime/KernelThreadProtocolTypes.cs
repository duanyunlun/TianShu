using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

[JsonConverter(typeof(KernelServiceTierJsonConverter))]
internal sealed class KernelServiceTier : IEquatable<KernelServiceTier>
{
    private KernelServiceTier(string value)
    {
        Value = value;
    }

    public static KernelServiceTier Fast { get; } = new("fast");

    public static KernelServiceTier Flex { get; } = new("flex");

    public string Value { get; }

    public static KernelServiceTier Parse(string value)
        => TryParse(value, out var tier)
            ? tier
            : throw new FormatException($"不支持的 serviceTier：{value}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out KernelServiceTier? tier)
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

    public bool Equals(KernelServiceTier? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj switch
        {
            KernelServiceTier other => Equals(other),
            string otherText => string.Equals(Value, Normalize(otherText), StringComparison.Ordinal),
            _ => false,
        };

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString()
        => Value;

    public static implicit operator KernelServiceTier?(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Parse(value);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

internal sealed class KernelApprovalGranularPolicy : IEquatable<KernelApprovalGranularPolicy>
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

    public bool Equals(KernelApprovalGranularPolicy? other)
        => other is not null
           && SandboxApproval == other.SandboxApproval
           && Rules == other.Rules
           && SkillApproval == other.SkillApproval
           && RequestPermissions == other.RequestPermissions
           && McpElicitations == other.McpElicitations;

    public override bool Equals(object? obj)
        => obj is KernelApprovalGranularPolicy other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(SandboxApproval, Rules, SkillApproval, RequestPermissions, McpElicitations);
}

[JsonConverter(typeof(KernelApprovalPolicyJsonConverter))]
internal sealed class KernelApprovalPolicy : IEquatable<KernelApprovalPolicy>
{
    private KernelApprovalPolicy(string scalarValue, KernelApprovalGranularPolicy? granularPolicy = null)
    {
        ScalarValue = scalarValue;
        GranularPolicy = granularPolicy;
    }

    public static KernelApprovalPolicy Untrusted { get; } = new("untrusted");

    public static KernelApprovalPolicy OnFailure { get; } = new("on-failure");

    public static KernelApprovalPolicy OnRequest { get; } = new("on-request");

    public static KernelApprovalPolicy Never { get; } = new("never");

    public string ScalarValue { get; }

    public KernelApprovalGranularPolicy? GranularPolicy { get; }

    public bool IsGranular => GranularPolicy is not null;

    public static KernelApprovalPolicy FromGranular(KernelApprovalGranularPolicy granularPolicy)
    {
        ArgumentNullException.ThrowIfNull(granularPolicy);
        return new KernelApprovalPolicy("granular", granularPolicy);
    }

    public static KernelApprovalPolicy Parse(string value)
        => TryParse(value, out var policy)
            ? policy
            : throw new FormatException($"不支持的 approvalPolicy：{value}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out KernelApprovalPolicy? policy)
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

    public bool Equals(KernelApprovalPolicy? other)
        => other is not null
           && string.Equals(ScalarValue, other.ScalarValue, StringComparison.Ordinal)
           && EqualityComparer<KernelApprovalGranularPolicy?>.Default.Equals(GranularPolicy, other.GranularPolicy);

    public override bool Equals(object? obj)
        => obj switch
        {
            KernelApprovalPolicy other => Equals(other),
            string otherText => string.Equals(ToString(), Normalize(otherText), StringComparison.Ordinal)
                || string.Equals(ScalarValue, Normalize(otherText), StringComparison.Ordinal),
            _ => false,
        };

    public override int GetHashCode()
        => HashCode.Combine(ScalarValue, GranularPolicy);

    public override string ToString()
        => GranularPolicy is null
            ? ScalarValue
            : JsonSerializer.Serialize(ToPlainObject());

    public static implicit operator KernelApprovalPolicy?(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Parse(value);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

internal sealed class KernelServiceTierJsonConverter : JsonConverter<KernelServiceTier>
{
    public override KernelServiceTier? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("serviceTier 必须是字符串。");
        }

        return KernelServiceTier.Parse(reader.GetString() ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, KernelServiceTier value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

internal sealed class KernelApprovalPolicyJsonConverter : JsonConverter<KernelApprovalPolicy>
{
    public override KernelApprovalPolicy? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return KernelApprovalPolicy.Parse(reader.GetString() ?? string.Empty);
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("granular", out var granular))
        {
            throw new JsonException("approvalPolicy 必须是字符串或 granular 对象。");
        }

        var policy = JsonSerializer.Deserialize<KernelApprovalGranularPolicy>(granular.GetRawText(), options);
        if (policy is null)
        {
            throw new JsonException("approvalPolicy.granular 解析失败。");
        }

        return KernelApprovalPolicy.FromGranular(policy);
    }

    public override void Write(Utf8JsonWriter writer, KernelApprovalPolicy value, JsonSerializerOptions options)
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

internal readonly struct KernelOptional<T>
{
    public KernelOptional(bool isSpecified, T? value)
    {
        IsSpecified = isSpecified;
        Value = value;
    }

    public bool IsSpecified { get; }

    public T? Value { get; }
}

internal sealed class KernelOptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType
           && typeToConvert.GetGenericTypeDefinition() == typeof(KernelOptional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(KernelOptionalJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class KernelOptionalJsonConverter<T> : JsonConverter<KernelOptional<T>>
    {
        public override KernelOptional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = JsonSerializer.Deserialize<T>(ref reader, options);
            return new KernelOptional<T>(isSpecified: true, value);
        }

        public override void Write(Utf8JsonWriter writer, KernelOptional<T> value, JsonSerializerOptions options)
        {
            if (!value.IsSpecified)
            {
                writer.WriteNullValue();
                return;
            }

            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}

internal sealed class KernelThreadStartRequest
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("modelProvider")]
    public string? ModelProvider { get; init; }

    [JsonPropertyName("serviceTier")]
    public KernelOptional<KernelServiceTier?> ServiceTier { get; init; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("approvalPolicy")]
    public KernelApprovalPolicy? ApprovalPolicy { get; init; }

    [JsonPropertyName("sandbox")]
    public KernelSandboxPolicyOverride? Sandbox { get; init; }

    [JsonPropertyName("config")]
    public KernelConfigOverridePayload? Config { get; init; }

    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; init; }

    [JsonPropertyName("baseInstructions")]
    public string? BaseInstructions { get; init; }

    [JsonPropertyName("developerInstructions")]
    public string? DeveloperInstructions { get; init; }

    [JsonPropertyName("dynamicTools")]
    [JsonConverter(typeof(KernelDynamicToolListJsonConverter))]
    public IReadOnlyList<KernelDynamicToolDescriptor>? DynamicTools { get; init; }

    [JsonPropertyName("mockExperimentalField")]
    public string? MockExperimentalField { get; init; }

    [JsonPropertyName("personality")]
    public KernelPersonality? Personality { get; init; }

    [JsonPropertyName("ephemeral")]
    public bool? Ephemeral { get; init; }

    [JsonPropertyName("persistExtendedHistory")]
    public bool PersistExtendedHistory { get; init; }

    [JsonPropertyName("experimentalRawEvents")]
    public bool? ExperimentalRawEvents { get; init; }

    [JsonPropertyName("sessionSource")]
    public KernelSessionSource? SessionSource { get; init; }
}

internal sealed class KernelThreadResumeRequest
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("history")]
    public KernelConversationHistoryOverride? History { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("modelProvider")]
    public string? ModelProvider { get; init; }

    [JsonPropertyName("serviceTier")]
    public KernelOptional<KernelServiceTier?> ServiceTier { get; init; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("approvalPolicy")]
    public KernelApprovalPolicy? ApprovalPolicy { get; init; }

    [JsonPropertyName("sandbox")]
    public KernelSandboxPolicyOverride? Sandbox { get; init; }

    [JsonPropertyName("config")]
    public KernelConfigOverridePayload? Config { get; init; }

    [JsonPropertyName("baseInstructions")]
    public string? BaseInstructions { get; init; }

    [JsonPropertyName("developerInstructions")]
    public string? DeveloperInstructions { get; init; }

    [JsonPropertyName("personality")]
    public KernelPersonality? Personality { get; init; }

    [JsonPropertyName("persistExtendedHistory")]
    public bool PersistExtendedHistory { get; init; }

    [JsonPropertyName("sessionSource")]
    public KernelSessionSource? SessionSource { get; init; }
}

internal sealed class KernelThreadForkRequest
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("modelProvider")]
    public string? ModelProvider { get; init; }

    [JsonPropertyName("serviceTier")]
    public KernelOptional<KernelServiceTier?> ServiceTier { get; init; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("approvalPolicy")]
    public KernelApprovalPolicy? ApprovalPolicy { get; init; }

    [JsonPropertyName("sandbox")]
    public KernelSandboxPolicyOverride? Sandbox { get; init; }

    [JsonPropertyName("config")]
    public KernelConfigOverridePayload? Config { get; init; }

    [JsonPropertyName("baseInstructions")]
    public string? BaseInstructions { get; init; }

    [JsonPropertyName("developerInstructions")]
    public string? DeveloperInstructions { get; init; }

    [JsonPropertyName("ephemeral")]
    public bool Ephemeral { get; init; }

    [JsonPropertyName("persistExtendedHistory")]
    public bool PersistExtendedHistory { get; init; }

    [JsonPropertyName("sessionSource")]
    public KernelSessionSource? SessionSource { get; init; }
}

internal sealed class KernelThreadElicitationRequest
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }
}

internal sealed class KernelThreadMetadataUpdateRequest
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("gitInfo")]
    public KernelThreadMetadataGitInfoUpdateRequest? GitInfo { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

internal sealed class KernelThreadMetadataGitInfoUpdateRequest
{
    [JsonPropertyName("sha")]
    public KernelOptional<string?> Sha { get; init; }

    [JsonPropertyName("branch")]
    public KernelOptional<string?> Branch { get; init; }

    [JsonPropertyName("originUrl")]
    public KernelOptional<string?> OriginUrl { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

internal sealed class KernelThreadPendingInputStateUpdateRequest
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("pendingInputState")]
    public KernelOptional<KernelPendingInputStateRecord?> PendingInputState { get; init; }
}

internal sealed record KernelThreadSessionResponsePayload(
    KernelThreadPayload Thread,
    string Model,
    string ModelProvider,
    KernelServiceTier? ServiceTier,
    string Cwd,
    KernelApprovalPolicy ApprovalPolicy,
    KernelSandboxPolicyOverride Sandbox,
    string? ReasoningEffort,
    KernelThreadSessionConfigurationPayload? SessionConfiguration = null,
    object[]? Messages = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    KernelPendingInputStateRecord? PendingInputState = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    object[]? PendingInteractiveRequests = null);

internal sealed record KernelThreadListResponsePayload(
    IReadOnlyList<KernelThreadPayload> Data,
    string? NextCursor);

internal sealed record KernelThreadElicitationResponsePayload(
    ulong Count,
    bool Paused);

internal sealed record KernelThreadPayload(
    string Id,
    string Preview,
    bool Ephemeral,
    string ModelProvider,
    long CreatedAt,
    long UpdatedAt,
    KernelThreadStatusPayload Status,
    string? Path,
    string Cwd,
    string CliVersion,
    KernelSessionSource Source,
    string? AgentNickname,
    string? AgentRole,
    KernelThreadGitInfoPayload? GitInfo,
    string? Name,
    IReadOnlyList<object> Turns,
    KernelThreadSessionProjectionPayload? SessionState = null,
    KernelThreadSessionConfigurationPayload? SessionConfiguration = null,
    object[]? Messages = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    KernelPendingInputStateRecord? PendingInputState = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    object[]? PendingInteractiveRequests = null);

internal sealed record KernelThreadSessionProjectionPayload(
    string SessionId,
    string Title,
    string CollaborationSpaceId,
    string CollaborationSpaceKey,
    string CollaborationSpaceDisplayName,
    string SessionMode,
    bool IsClosed,
    string? ActiveThreadId,
    bool HasActiveTurn,
    KernelThreadOrchestrationProjectionPayload? Orchestration = null);

internal sealed record KernelThreadOrchestrationProjectionPayload(
    string? CurrentStageId,
    KernelThreadOrchestratorDecisionProjectionPayload? LastDecision,
    KernelThreadStageContextPackageProjectionPayload? LastContextPackage,
    IReadOnlyList<KernelThreadStageContextSegmentProjectionPayload> ContextLedgerSegments,
    IReadOnlyList<KernelThreadStageCheckpointProjectionPayload> Checkpoints);

internal sealed record KernelThreadOrchestratorDecisionProjectionPayload(
    string DecisionId,
    string SelectedStageId,
    IReadOnlyList<string> CandidateStageIds,
    string ReasonCode,
    string? PreviousStageId,
    string? ContextProjectionReason,
    IReadOnlyList<string> PolicyHits,
    DateTimeOffset DecidedAt);

internal sealed record KernelThreadStageContextPackageProjectionPayload(
    string PackageId,
    string StageId,
    string ProjectionMode,
    int? BudgetTokens,
    IReadOnlyList<string> SourceCheckpointIds,
    int SegmentCount,
    int ArtifactRefCount);

internal sealed record KernelThreadStageContextSegmentProjectionPayload(
    string Kind,
    string Content,
    string? Title,
    KernelThreadResourceRefProjectionPayload? Source,
    bool Required,
    int? EstimatedTokens);

internal sealed record KernelThreadStageCheckpointProjectionPayload(
    string CheckpointId,
    string StageId,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Summary,
    IReadOnlyList<KernelThreadArtifactRefProjectionPayload> ArtifactRefs,
    string? ModelRouteSetId,
    string? ModelRouteKind,
    JsonElement? Diagnostics,
    IReadOnlyList<string> NextStageSuggestions);

internal sealed record KernelThreadArtifactRefProjectionPayload(
    string Id,
    string? Name,
    string? Kind);

internal sealed record KernelThreadResourceRefProjectionPayload(
    string Kind,
    string Key);

internal sealed record KernelThreadStatusPayload(
    string Type,
    IReadOnlyList<string>? ActiveFlags = null);

internal sealed record KernelThreadGitInfoPayload(
    string? Sha,
    string? Branch,
    string? OriginUrl);

internal sealed record KernelThreadSessionConfigurationPayload(
    string Model,
    string ModelProvider,
    string ModelProviderId,
    KernelServiceTier? ServiceTier,
    KernelApprovalPolicy ApprovalPolicy,
    string SandboxPolicy,
    KernelSandboxPolicyOverride SandboxPolicyPayload,
    string? ReasoningEffort,
    string? HistoryLogId,
    int HistoryEntryCount,
    string? RolloutPath,
    string? ForkedFromId,
    string Cwd,
    bool Ephemeral,
    bool AllowLoginShell,
    KernelShellEnvironmentPolicy ShellEnvironmentPolicy,
    string? ProviderBaseUrl,
    string? ProviderApiKeyEnvironmentVariable,
    string? ProviderWireApi,
    int? ProviderRequestMaxRetries,
    int? ProviderStreamMaxRetries,
    long? ProviderStreamIdleTimeoutMs,
    long? ProviderWebsocketConnectTimeoutMs,
    bool? ProviderSupportsWebsockets,
    string? WebSearchMode,
    string? ServiceName,
    string? BaseInstructions,
    string? DeveloperInstructions,
    string? UserInstructions,
    string? ReasoningSummary,
    string? Verbosity,
    string? Personality,
    IReadOnlyList<KernelDynamicToolDescriptor>? DynamicTools,
    KernelCollaborationModeState? CollaborationMode,
    bool PersistExtendedHistory,
    KernelSessionSource SessionSource,
    KernelWindowsSandboxLevel WindowsSandboxLevel,
    bool DefaultModeRequestUserInputEnabled,
    string? ModelRouteSetId = null);

internal sealed class KernelTurnStartRequest
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; init; }

    [JsonPropertyName("input")]
    [JsonConverter(typeof(KernelTurnInputListJsonConverter))]
    public IReadOnlyList<KernelTurnInputItem>? Input { get; init; }

    [JsonPropertyName("interactionEnvelope")]
    public KernelInteractionEnvelopePayload? InteractionEnvelope { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("serviceTier")]
    public KernelOptional<KernelServiceTier?> ServiceTier { get; init; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("providerBaseUrl")]
    public string? ProviderBaseUrl { get; init; }

    [JsonPropertyName("providerApiKeyEnvironmentVariable")]
    public string? ProviderApiKeyEnvironmentVariable { get; init; }

    [JsonPropertyName("providerWireApi")]
    public string? ProviderWireApi { get; init; }

    [JsonPropertyName("providerRequestMaxRetries")]
    public int? ProviderRequestMaxRetries { get; init; }

    [JsonPropertyName("providerStreamMaxRetries")]
    public int? ProviderStreamMaxRetries { get; init; }

    [JsonPropertyName("providerStreamIdleTimeoutMs")]
    public long? ProviderStreamIdleTimeoutMs { get; init; }

    [JsonPropertyName("providerWebsocketConnectTimeoutMs")]
    public long? ProviderWebsocketConnectTimeoutMs { get; init; }

    [JsonPropertyName("providerSupportsWebsockets")]
    public bool? ProviderSupportsWebsockets { get; init; }

    [JsonPropertyName("approvalPolicy")]
    public KernelApprovalPolicy? ApprovalPolicy { get; init; }

    [JsonPropertyName("sandboxPolicy")]
    public KernelSandboxPolicyOverride? SandboxPolicy { get; init; }

    [JsonPropertyName("effort")]
    public string? Effort { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("verbosity")]
    public string? Verbosity { get; init; }

    [JsonPropertyName("personality")]
    public KernelPersonality? Personality { get; init; }

    [JsonPropertyName("outputSchema")]
    public KernelJsonSchemaPayload? OutputSchema { get; init; }

    [JsonPropertyName("collaborationMode")]
    public KernelCollaborationModeOverride? CollaborationMode { get; init; }
}

internal sealed class KernelInteractionEnvelopePayload
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("sourceKind")]
    public int? SourceKind { get; init; }

    [JsonPropertyName("surface")]
    public string? Surface { get; init; }

    [JsonPropertyName("createdAtUnixMs")]
    public long? CreatedAtUnixMs { get; init; }

    public static KernelInteractionEnvelopePayload FromContract(InteractionEnvelopeRef interactionEnvelope)
    {
        ArgumentNullException.ThrowIfNull(interactionEnvelope);
        return new KernelInteractionEnvelopePayload
        {
            Id = interactionEnvelope.Id.ToString(),
            SourceKind = (int)interactionEnvelope.SourceKind,
            Surface = interactionEnvelope.Surface,
            CreatedAtUnixMs = interactionEnvelope.CreatedAt.ToUnixTimeMilliseconds(),
        };
    }

    public InteractionEnvelopeRef? ToContract()
    {
        if (string.IsNullOrWhiteSpace(Id)
            || !SourceKind.HasValue
            || string.IsNullOrWhiteSpace(Surface)
            || !Enum.IsDefined(typeof(InteractionSourceKind), SourceKind.Value))
        {
            return null;
        }

        var createdAt = CreatedAtUnixMs.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUnixMs.Value)
            : DateTimeOffset.UtcNow;
        return new InteractionEnvelopeRef(
            new InteractionEnvelopeId(Id),
            (InteractionSourceKind)SourceKind.Value,
            Surface,
            createdAt);
    }
}
