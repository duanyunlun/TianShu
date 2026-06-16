using System.Security.Cryptography;
using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Memory;

namespace TianShu.IdentityMemory;

/// <summary>
/// Identity / Memory 本地 store 的审计记录。
/// Audit entry emitted by the local identity-memory store.
/// </summary>
public sealed record TianShuMemoryAuditRecord
{
    private const string RedactedValue = "[redacted]";
    private static readonly JsonSerializerOptions ValueHashSerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 初始化本地记忆审计记录。
    /// Initializes a local memory audit entry.
    /// </summary>
    public TianShuMemoryAuditRecord(
        string operation,
        MemorySpaceId memorySpaceId,
        string key,
        string actor,
        string source,
        DateTimeOffset occurredAt,
        StructuredValue? value = null,
        decimal? confidence = null,
        StructuredValueKind? valueKind = null,
        string? valueHash = null,
        MemoryMutationEffect effect = MemoryMutationEffect.None,
        IReadOnlyList<MemoryRiskReasonCode>? reasonCodes = null,
        string? reason = null,
        string? snippet = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Operation = IdentifierGuard.AgainstNullOrWhiteSpace(operation, nameof(operation));
        MemorySpaceId = memorySpaceId;
        Key = SanitizeAuditField(key, nameof(key), allowKeywordRedaction: true);
        Actor = SanitizeAuditField(actor, nameof(actor), allowKeywordRedaction: true);
        Source = SanitizeAuditField(source, nameof(source), allowKeywordRedaction: true);
        OccurredAt = occurredAt;
        ValueKind = valueKind ?? value?.Kind;
        ValueHash = !string.IsNullOrWhiteSpace(valueHash)
            ? valueHash
            : value is null ? null : ComputeValueHash(value);
        Confidence = confidence;
        Effect = effect;
        ReasonCodes = reasonCodes ?? Array.Empty<MemoryRiskReasonCode>();
        Reason = SanitizeOptionalAuditField(reason, allowKeywordRedaction: true);
        Snippet = SanitizeOptionalAuditField(snippet, allowKeywordRedaction: true);
        Metadata = SanitizeMetadata(metadata);
    }

    public string Operation { get; }

    public MemorySpaceId MemorySpaceId { get; }

    public string Key { get; }

    public string Actor { get; }

    public string Source { get; }

    public DateTimeOffset OccurredAt { get; }

    public StructuredValueKind? ValueKind { get; }

    public string? ValueHash { get; }

    public decimal? Confidence { get; }

    public MemoryMutationEffect Effect { get; }

    public IReadOnlyList<MemoryRiskReasonCode> ReasonCodes { get; }

    public string? Reason { get; }

    public string? Snippet { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static string ComputeValueHash(StructuredValue value)
    {
        var json = JsonSerializer.Serialize(value, ValueHashSerializerOptions);
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string SanitizeAuditField(
        string value,
        string parameterName,
        bool allowKeywordRedaction)
    {
        var normalized = IdentifierGuard.AgainstNullOrWhiteSpace(value, parameterName);
        return LooksLikeSecret(normalized, allowKeywordRedaction) ? RedactedValue : normalized;
    }

    private static string? SanitizeOptionalAuditField(
        string? value,
        bool allowKeywordRedaction)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return LooksLikeSecret(normalized, allowKeywordRedaction) ? RedactedValue : normalized;
    }

    private static IReadOnlyDictionary<string, string> SanitizeMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var sanitized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in metadata)
        {
            var safeKey = LooksLikeSecret(key, allowKeywordRedaction: true) ? RedactedValue : key;
            var safeValue = LooksLikeSecret(value, allowKeywordRedaction: true) ? RedactedValue : value;
            sanitized[safeKey] = safeValue;
        }

        return sanitized;
    }

    private static bool LooksLikeSecret(
        string value,
        bool allowKeywordRedaction)
    {
        var normalized = value.Trim();
        var lower = normalized.ToLowerInvariant();
        if (lower.StartsWith("sk-", StringComparison.Ordinal)
            || lower.StartsWith("sk_", StringComparison.Ordinal)
            || lower.StartsWith("ghp_", StringComparison.Ordinal)
            || lower.StartsWith("github_pat_", StringComparison.Ordinal)
            || lower.StartsWith("xoxb-", StringComparison.Ordinal)
            || lower.StartsWith("bearer ", StringComparison.Ordinal)
            || lower.Contains("=sk-", StringComparison.Ordinal)
            || lower.Contains("=ghp_", StringComparison.Ordinal))
        {
            return true;
        }

        return allowKeywordRedaction
               && (lower.Contains("password", StringComparison.Ordinal)
                   || lower.Contains("secret", StringComparison.Ordinal)
                   || lower.Contains("api_key", StringComparison.Ordinal)
                   || lower.Contains("apikey", StringComparison.Ordinal)
                   || lower.Contains("access_token", StringComparison.Ordinal)
                   || lower.Contains("bearer_token", StringComparison.Ordinal));
    }
}
