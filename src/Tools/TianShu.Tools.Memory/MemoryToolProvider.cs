using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using static TianShu.Tools.Memory.MemoryToolHandlerImports;

namespace TianShu.Tools.Memory;

/// <summary>
/// Memory 工具域 Provider。
/// Provider for the Memory tool domain.
/// </summary>
public sealed class MemoryToolProvider : ITianShuToolProvider
{
    private static readonly IReadOnlyDictionary<string, ToolDescriptor> Descriptors =
        new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal)
        {
            [MemoryToolNames.Search] = MemorySearchToolHandler.DescriptorInstance,
            [MemoryToolNames.ExplainOverlay] = MemoryExplainOverlayToolHandler.DescriptorInstance,
            [MemoryToolNames.Feedback] = MemoryFeedbackToolHandler.DescriptorInstance,
        };

    public IReadOnlyList<ToolDescriptor> DescribeTools(TianShuToolRegistrationContext context)
    {
        _ = context;
        return Descriptors.Values.ToArray();
    }

    public ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context)
    {
        _ = context;
        return toolKey switch
        {
            MemoryToolNames.Search => new MemorySearchToolHandler(),
            MemoryToolNames.ExplainOverlay => new MemoryExplainOverlayToolHandler(),
            MemoryToolNames.Feedback => new MemoryFeedbackToolHandler(),
            _ => throw new InvalidOperationException($"Unknown memory tool: {toolKey}"),
        };
    }
}

internal static class MemoryToolNames
{
    public const string Search = "memory_search";
    public const string ExplainOverlay = "memory_explain_overlay";
    public const string Feedback = "memory_feedback";
    public const string ImplementationId = "tianshu.tools.memory";
}

internal sealed class MemorySearchToolHandler : ITianShuToolHandler
{
    private const int DefaultLimit = 8;
    private static readonly Regex TokenRegex = new(@"[\p{L}\p{N}_\-.#/\\]+", RegexOptions.Compiled);
    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "Natural-language search text for relevant TianShu memory facts." },
            limit = new { type = "integer", description = $"Maximum records to return. Defaults to {DefaultLimit}." },
            scope_kind = new
            {
                type = "string",
                description = "Optional scope filter: User, Workspace, Team, Session, Agent, or Collaboration.",
            },
            minimum_confidence = new
            {
                type = "number",
                description = "Optional minimum confidence between 0 and 1.",
            },
        },
        required = new[] { "query" },
        additionalProperties = false,
    });

    public static ToolDescriptor DescriptorInstance { get; } = new(
        MemoryToolNames.Search,
        "Memory Search",
        "Searches TianShu memory facts. Current session/workspace facts are prioritized; other workspace facts are returned only as transferable candidates when they match the query.",
        capabilities: [new ToolCapability("memory-read", "Search governed TianShu memory records.")],
        approvalRequirement: ToolApprovalRequirement.None,
        concurrencyClass: ToolConcurrencyClass.SharedReadOnly,
        implementationBinding: new ToolImplementationBinding(
            MemoryToolNames.Search,
            ToolImplementationKind.Managed,
            implementationId: MemoryToolNames.ImplementationId),
        inputSchema: InputSchemaElement);

    public ToolDescriptor Descriptor => DescriptorInstance;

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (context.MemoryServices is null)
        {
            return Failure(request, "memory_search is unavailable");
        }

        var query = Normalize(ReadString(request.Input, "query"));
        if (string.IsNullOrWhiteSpace(query))
        {
            return Failure(request, "query must not be empty");
        }

        var limit = ReadInt(request.Input, "limit") ?? DefaultLimit;
        if (limit <= 0)
        {
            return Failure(request, "limit must be greater than zero");
        }

        var result = await context.MemoryServices.FilterMemoryAsync(
                new FilterMemory(
                    MinimumConfidence: ReadDecimal(request.Input, "minimum_confidence"),
                    ScopeKind: ReadScopeKind(request.Input, "scope_kind")),
                cancellationToken)
            .ConfigureAwait(false);
        var ranked = RankFacts(result.Records, query!, ResolveCurrentWorkspaceMemorySpaceId(context.WorkingDirectory))
            .Where(static item => item.Score > 0)
            .Take(limit)
            .Select(static item => SerializeFact(item.Fact, item.Applicability, item.Score))
            .ToArray();

        return Success(request, new
        {
            query,
            records = ranked,
            returned = ranked.Length,
            degradedProviders = result.DegradedProviders,
        });
    }

    internal static IReadOnlyList<ScoredMemoryFact> RankFacts(
        IEnumerable<FactMemoryRecord> facts,
        string query,
        string? currentWorkspaceMemorySpaceId)
    {
        var terms = Tokenize(query);
        return facts
            .Select(fact =>
            {
                var applicability = ClassifyApplicability(fact, currentWorkspaceMemorySpaceId);
                var matchScore = Score(fact, terms);
                var score = matchScore == 0
                    ? 0
                    : matchScore + ApplicabilityBoost(applicability) + (fact.IsCounterexample ? 2 : 0);
                return new ScoredMemoryFact(fact, applicability, score);
            })
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => ApplicabilityOrder(item.Applicability))
            .ThenByDescending(static item => item.Fact.Confidence)
            .ThenBy(static item => item.Fact.Key, StringComparer.Ordinal)
            .ToArray();
    }

    internal static string? ResolveCurrentWorkspaceMemorySpaceId(string? cwd)
    {
        var normalized = NormalizeSegment(cwd);
        return normalized is null ? null : $"memory:workspace:{normalized}";
    }

    internal static string ClassifyApplicability(FactMemoryRecord fact, string? currentWorkspaceMemorySpaceId)
    {
        var space = fact.MemorySpaceId.Value;
        if (space.StartsWith("memory:session:", StringComparison.OrdinalIgnoreCase))
        {
            return "current_session";
        }

        if (!string.IsNullOrWhiteSpace(currentWorkspaceMemorySpaceId)
            && string.Equals(space, currentWorkspaceMemorySpaceId, StringComparison.Ordinal))
        {
            return "current_workspace";
        }

        if (space.StartsWith("memory:user:", StringComparison.OrdinalIgnoreCase))
        {
            return "user_global";
        }

        if (space.StartsWith("memory:team:", StringComparison.OrdinalIgnoreCase))
        {
            return "team";
        }

        if (space.StartsWith("memory:workspace:", StringComparison.OrdinalIgnoreCase))
        {
            return "transfer_candidate";
        }

        return "other";
    }

    private static object SerializeFact(FactMemoryRecord fact, string applicability, int score)
        => new
        {
            recordId = fact.Id.Value,
            memorySpaceId = fact.MemorySpaceId.Value,
            key = fact.Key,
            value = fact.Value,
            confidence = fact.Confidence,
            lifecycle = fact.LifecycleStatus.ToString(),
            isCounterexample = fact.IsCounterexample,
            applicability,
            score,
            tags = fact.Tags,
        };

    private static int Score(FactMemoryRecord fact, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return 0;
        }

        var searchable = BuildSearchableText(fact);
        var score = 0;
        foreach (var term in terms)
        {
            if (searchable.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }
        }

        return score;
    }

    private static int ApplicabilityBoost(string applicability)
        => applicability switch
        {
            "current_session" => 8,
            "current_workspace" => 7,
            "user_global" => 5,
            "team" => 4,
            "transfer_candidate" => 1,
            _ => 0,
        };

    private static int ApplicabilityOrder(string applicability)
        => applicability switch
        {
            "current_session" => 0,
            "current_workspace" => 1,
            "user_global" => 2,
            "team" => 3,
            "transfer_candidate" => 4,
            _ => 5,
        };

    private static IReadOnlyList<string> Tokenize(string text)
        => TokenRegex.Matches(text)
            .Select(static match => match.Value.Trim().ToLowerInvariant())
            .Where(static token => token.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string BuildSearchableText(FactMemoryRecord fact)
    {
        var parts = new List<string> { fact.Key };
        AppendValue(parts, fact.Value);
        parts.AddRange(fact.Tags);
        if (fact.ContextSignature is { } signature)
        {
            parts.AddRange(signature.Tags);
        }

        return string.Join(' ', parts);
    }

    private static void AppendValue(List<string> parts, StructuredValue value)
    {
        if (!string.IsNullOrWhiteSpace(value.StringValue))
        {
            parts.Add(value.StringValue!);
        }

        if (!string.IsNullOrWhiteSpace(value.NumberValue))
        {
            parts.Add(value.NumberValue!);
        }

        if (value.BooleanValue is { } booleanValue)
        {
            parts.Add(booleanValue.ToString());
        }

        foreach (var pair in value.Properties)
        {
            parts.Add(pair.Key);
            AppendValue(parts, pair.Value);
        }

        foreach (var item in value.Items)
        {
            AppendValue(parts, item);
        }
    }

    private static string? NormalizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value
            .Trim()
            .Replace('\\', '/')
            .Replace(' ', '-')
            .ToLowerInvariant();

        if (normalized.Length >= 3
            && char.IsLetter(normalized[0])
            && normalized[1] == ':'
            && normalized[2] == '/')
        {
            normalized = normalized[0] + normalized[2..];
        }

        return normalized.Replace(':', '_');
    }

    private static decimal? ReadDecimal(StructuredValue input, string propertyName)
    {
        var raw = ReadString(input, propertyName);
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static MemoryScopeKind? ReadScopeKind(StructuredValue input, string propertyName)
    {
        var raw = Normalize(ReadString(input, propertyName));
        return Enum.TryParse<MemoryScopeKind>(raw, ignoreCase: true, out var parsed) ? parsed : null;
    }

    internal sealed record ScoredMemoryFact(FactMemoryRecord Fact, string Applicability, int Score);
}

internal sealed class MemoryExplainOverlayToolHandler : ITianShuToolHandler
{
    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            query = new
            {
                type = "string",
                description = "Optional query text used to explain overlay relevance. If omitted, the current turn text is used.",
            },
        },
        additionalProperties = false,
    });

    public static ToolDescriptor DescriptorInstance { get; } = new(
        MemoryToolNames.ExplainOverlay,
        "Memory Overlay Explanation",
        "Explains the TianShu memory overlay visible to the current turn, including applicability class and merge decision. It does not mutate memory.",
        capabilities: [new ToolCapability("memory-read", "Explain governed TianShu memory overlay.")],
        approvalRequirement: ToolApprovalRequirement.None,
        concurrencyClass: ToolConcurrencyClass.SharedReadOnly,
        implementationBinding: new ToolImplementationBinding(
            MemoryToolNames.ExplainOverlay,
            ToolImplementationKind.Managed,
            implementationId: MemoryToolNames.ImplementationId),
        inputSchema: InputSchemaElement);

    public ToolDescriptor Descriptor => DescriptorInstance;

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (context.MemoryServices is null)
        {
            return Failure(request, "memory_explain_overlay is unavailable");
        }

        var query = Normalize(ReadString(request.Input, "query"));
        var overlay = await context.MemoryServices.ResolveMemoryOverlayAsync(
                new ResolveMemoryOverlay(QueryText: query),
                cancellationToken)
            .ConfigureAwait(false);
        var currentWorkspaceMemorySpaceId = MemorySearchToolHandler.ResolveCurrentWorkspaceMemorySpaceId(context.WorkingDirectory);
        var facts = overlay.Facts
            .Select(fact => new
            {
                recordId = fact.Id.Value,
                memorySpaceId = fact.MemorySpaceId.Value,
                key = fact.Key,
                value = fact.Value,
                confidence = fact.Confidence,
                isCounterexample = fact.IsCounterexample,
                applicability = MemorySearchToolHandler.ClassifyApplicability(fact, currentWorkspaceMemorySpaceId),
            })
            .ToArray();

        return Success(request, new
        {
            mergeDecision = overlay.MergeDecision.ToString(),
            factCount = facts.Length,
            facts,
            habitProfile = overlay.HabitProfile,
        });
    }
}

internal sealed class MemoryFeedbackToolHandler : ITianShuToolHandler
{
    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            memory_record_id = new { type = "string", description = "The memory record id returned by memory_search or memory_explain_overlay." },
            decision = new
            {
                type = "string",
                description = "Feedback decision: Applied, Ignored, or NeedsReview.",
            },
            feedback = new { type = "string", description = "Short explanation of why the memory was useful, wrong, stale, conflicting, or needs review." },
        },
        required = new[] { "memory_record_id", "decision", "feedback" },
        additionalProperties = false,
    });

    public static ToolDescriptor DescriptorInstance { get; } = new(
        MemoryToolNames.Feedback,
        "Memory Feedback",
        "Records feedback about a memory record. This only writes feedback/audit evidence and never directly overwrites or activates a long-term memory fact.",
        capabilities: [new ToolCapability("memory-feedback", "Record governed feedback about a memory record.")],
        approvalRequirement: ToolApprovalRequirement.Required,
        concurrencyClass: ToolConcurrencyClass.Sequential,
        implementationBinding: new ToolImplementationBinding(
            MemoryToolNames.Feedback,
            ToolImplementationKind.Managed,
            implementationId: MemoryToolNames.ImplementationId),
        inputSchema: InputSchemaElement);

    public ToolDescriptor Descriptor => DescriptorInstance;

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (context.MemoryServices is null)
        {
            return Failure(request, "memory_feedback is unavailable");
        }

        var recordId = Normalize(ReadString(request.Input, "memory_record_id"));
        var feedback = Normalize(ReadString(request.Input, "feedback"));
        var rawDecision = Normalize(ReadString(request.Input, "decision"));
        if (string.IsNullOrWhiteSpace(recordId))
        {
            return Failure(request, "memory_record_id must not be empty");
        }

        if (string.IsNullOrWhiteSpace(feedback))
        {
            return Failure(request, "feedback must not be empty");
        }

        if (!Enum.TryParse<MemoryMergeDecision>(rawDecision, ignoreCase: true, out var decision))
        {
            return Failure(request, "decision must be Applied, Ignored, or NeedsReview");
        }

        var result = await context.MemoryServices.RecordMemoryFeedbackAsync(
                new RecordMemoryFeedback(
                    new MemoryRecordId(recordId!),
                    decision,
                    feedback!,
                    new MemorySourceRef(
                        MemorySourceKind.ToolResult,
                        request.CallId.Value,
                        role: "assistant",
                        path: context.WorkingDirectory,
                        snippet: feedback)),
                cancellationToken)
            .ConfigureAwait(false);
        return Success(request, new
        {
            succeeded = result.Success,
            recordId = result.RecordId?.Value,
            lifecycleStatus = result.LifecycleStatus?.ToString(),
            effect = result.Effect.ToString(),
            result.DegradedReason,
        });
    }
}

internal static class MemoryToolResult
{
    public static ToolInvocationResult Success(ToolInvocationRequest request, object payload)
        => new(
            request.CallId,
            request.ToolKey,
            [new ToolStreamItem("text", StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(payload)), isTerminal: true)]);

    public static ToolInvocationResult Failure(ToolInvocationRequest request, string message)
        => new(
            request.CallId,
            request.ToolKey,
            failure: new ToolInvocationFailure($"{request.ToolKey}.invalid_request", message));
}

internal static class MemoryToolInput
{
    public static string? ReadString(StructuredValue input, string propertyName)
        => input.TryGetProperty(propertyName, out var value) ? value?.GetString() : null;

    public static int? ReadInt(StructuredValue input, string propertyName)
    {
        if (!input.TryGetProperty(propertyName, out var value) || value is null)
        {
            return null;
        }

        return int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class MemoryToolHandlerImports
{
    public static ToolInvocationResult Success(ToolInvocationRequest request, object payload)
        => MemoryToolResult.Success(request, payload);

    public static ToolInvocationResult Failure(ToolInvocationRequest request, string message)
        => MemoryToolResult.Failure(request, message);

    public static string? ReadString(StructuredValue input, string propertyName)
        => MemoryToolInput.ReadString(input, propertyName);

    public static int? ReadInt(StructuredValue input, string propertyName)
        => MemoryToolInput.ReadInt(input, propertyName);

    public static string? Normalize(string? value)
        => MemoryToolInput.Normalize(value);
}
