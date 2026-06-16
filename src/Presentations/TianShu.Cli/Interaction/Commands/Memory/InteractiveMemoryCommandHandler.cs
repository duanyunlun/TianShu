using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TianShu.ControlPlane;
using TianShu.ControlPlane.Abstractions.Memory;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;

namespace TianShu.Cli.Interaction.Commands.Memory;

/// <summary>
/// Handles the interactive /memory command through the formal memory control plane.
/// 通过正式 memory control plane 处理交互式 /memory 命令，避免 CLI 直接读写本地 store。
/// </summary>
internal sealed class InteractiveMemoryCommandHandler
{
    public async Task HandleMemoryCommandAsync(
        IExecutionRuntime runtime,
        string rest,
        InteractiveMemoryCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(context);

        var (verb, tail) = SplitVerb(rest);
        var memory = TianShuControlPlaneClientFactory.Create(runtime).Memory;
        object result;

        switch (NormalizeVerb(verb))
        {
            case "providers":
                result = await memory.ListMemoryProvidersAsync(new ListMemoryProviders(ReadScopeKind(tail)), cancellationToken).ConfigureAwait(false);
                break;
            case "spaces":
            case "list":
                result = await memory.ListMemorySpacesAsync(new ListMemorySpaces(ReadScopeKind(tail)), cancellationToken).ConfigureAwait(false);
                break;
            case "overlay":
                result = await memory.ResolveMemoryOverlayAsync(ReadOverlayQuery(tail), cancellationToken).ConfigureAwait(false);
                break;
            case "search":
            case "filter":
                result = await memory.FilterMemoryAsync(ReadPayloadOrDefault(tail, new FilterMemory(), context.JsonOptions), cancellationToken).ConfigureAwait(false);
                break;
            case "review":
                await HandleReviewCommandAsync(memory, tail, context, cancellationToken).ConfigureAwait(false);
                return;
            case "consolidate":
            case "consolidation":
                result = await memory.RunMemoryConsolidationAsync(
                    ReadPayloadOrDefault(tail, new RunMemoryConsolidation(), context.JsonOptions),
                    cancellationToken).ConfigureAwait(false);
                break;
            case "add":
                result = await memory.AddMemoryAsync(
                    await ReadAddCommandAsync(memory, tail, context.JsonOptions, cancellationToken).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);
                break;
            case "extract":
                result = await memory.ExtractMemoryAsync(ReadRequiredPayload<ExtractMemory>(tail, context.JsonOptions), cancellationToken).ConfigureAwait(false);
                break;
            case "forget":
                result = await memory.ForgetMemoryAsync(ReadRequiredPayload<ForgetMemory>(tail, context.JsonOptions), cancellationToken).ConfigureAwait(false);
                break;
            case "delete":
                result = await memory.DeleteMemoryAsync(ReadRequiredPayload<DeleteMemory>(tail, context.JsonOptions), cancellationToken).ConfigureAwait(false);
                break;
            case "supersede":
                result = await memory.SupersedeMemoryAsync(ReadRequiredPayload<SupersedeMemory>(tail, context.JsonOptions), cancellationToken).ConfigureAwait(false);
                break;
            default:
                context.WriteLine(BuildUsage(), true);
                return;
        }

        context.WriteLine(JsonSerializer.Serialize(result, context.JsonOptions), false);
    }

    private static string BuildUsage()
        => "用法：/memory providers|spaces|overlay|search|filter|add|extract|consolidate|forget|delete|supersede|review [--payload-json <json>|--payload-file <path>]";

    private static async Task HandleReviewCommandAsync(
        IMemoryControlPlane memory,
        string tail,
        InteractiveMemoryCommandContext context,
        CancellationToken cancellationToken)
    {
        var (action, actionTail) = SplitVerb(tail);
        if (action.StartsWith("--", StringComparison.Ordinal))
        {
            actionTail = tail;
            action = string.Empty;
        }

        switch (NormalizeVerb(action))
        {
            case "":
            case "list":
            case "show":
            {
                var result = await memory.ListMemoryReviewsAsync(
                        ReadPayloadOrDefault(actionTail, BuildReviewListQuery(actionTail), context.JsonOptions),
                        cancellationToken)
                    .ConfigureAwait(false);
                context.WriteLine(
                    HasFlag(tail, "--json")
                        ? JsonSerializer.Serialize(result, context.JsonOptions)
                        : FormatReviewResult(result),
                    false);
                return;
            }
            case "reject":
            case "forget":
            {
                var target = ReadReviewTarget(actionTail);
                if (string.IsNullOrWhiteSpace(target))
                {
                    context.WriteLine("缺少待拒绝的记忆 record id：/memory review reject <record-id>", true);
                    return;
                }

                var match = await ResolveReviewRecordAsync(memory, target, actionTail, cancellationToken).ConfigureAwait(false);
                var memorySpaceId = match?.MemorySpaceId ?? ReadMemorySpaceId(actionTail);
                if (memorySpaceId is null)
                {
                    context.WriteLine("无法确定 memory space；请确认该记录仍处于待审列表，或显式传入 --memory-space-id <id>。", true);
                    return;
                }

                var result = await memory.ForgetMemoryAsync(
                        new ForgetMemory(match?.Id ?? new MemoryRecordId(target), memorySpaceId, match?.Key),
                        cancellationToken)
                    .ConfigureAwait(false);
                context.WriteLine(FormatMutationResult("已拒绝待审记忆", result), !result.Success);
                return;
            }
            case "delete":
            {
                var target = ReadReviewTarget(actionTail);
                if (string.IsNullOrWhiteSpace(target))
                {
                    context.WriteLine("缺少待删除的记忆 record id：/memory review delete <record-id>", true);
                    return;
                }

                var match = await ResolveReviewRecordAsync(memory, target, actionTail, cancellationToken).ConfigureAwait(false);
                var memorySpaceId = match?.MemorySpaceId ?? ReadMemorySpaceId(actionTail);
                if (memorySpaceId is null)
                {
                    context.WriteLine("无法确定 memory space；请确认该记录仍处于待审列表，或显式传入 --memory-space-id <id>。", true);
                    return;
                }

                var result = await memory.DeleteMemoryAsync(
                        new DeleteMemory(match?.Id ?? new MemoryRecordId(target), memorySpaceId, match?.Key, ReadOptionValue(actionTail, "--reason")),
                        cancellationToken)
                    .ConfigureAwait(false);
                context.WriteLine(FormatMutationResult("已删除待审记忆", result), !result.Success);
                return;
            }
            case "feedback":
            {
                var target = ReadReviewTarget(actionTail);
                if (string.IsNullOrWhiteSpace(target))
                {
                    context.WriteLine("缺少反馈目标 record id：/memory review feedback <record-id> --feedback <text>", true);
                    return;
                }

                var feedback = ReadOptionValue(actionTail, "--feedback") ?? ReadTailAfterFirstToken(actionTail);
                if (string.IsNullOrWhiteSpace(feedback))
                {
                    context.WriteLine("缺少反馈内容：请传入 --feedback <text>。", true);
                    return;
                }

                var decision = ReadMergeDecision(actionTail) ?? MemoryMergeDecision.NeedsReview;
                var result = await memory.RecordMemoryFeedbackAsync(
                        new RecordMemoryFeedback(new MemoryRecordId(target), decision, feedback),
                        cancellationToken)
                    .ConfigureAwait(false);
                context.WriteLine(FormatMutationResult("已记录待审记忆反馈", result), !result.Success);
                return;
            }
            case "approve":
            {
                var target = ReadReviewTarget(actionTail);
                if (string.IsNullOrWhiteSpace(target))
                {
                    context.WriteLine("缺少待批准的记忆 record id：/memory review approve <record-id>", true);
                    return;
                }

                var match = await ResolveReviewRecordAsync(memory, target, actionTail, cancellationToken).ConfigureAwait(false);
                var memorySpaceId = match?.MemorySpaceId ?? ReadMemorySpaceId(actionTail);
                if (memorySpaceId is null)
                {
                    context.WriteLine("无法确定 memory space；请确认该记录仍处于待审列表，或显式传入 --memory-space-id <id>。", true);
                    return;
                }

                var result = await memory.ApproveMemoryReviewAsync(
                        new ApproveMemoryReview(
                            match?.Id ?? new MemoryRecordId(target),
                            memorySpaceId,
                            match?.Key,
                            ReadOptionValue(actionTail, "--reason")),
                        cancellationToken)
                    .ConfigureAwait(false);
                context.WriteLine(FormatMutationResult("已批准待审记忆", result), !result.Success);
                return;
            }
            case "demote":
            {
                var target = ReadReviewTarget(actionTail);
                if (string.IsNullOrWhiteSpace(target))
                {
                    context.WriteLine("缺少待降权的记忆 record id：/memory review demote <record-id>", true);
                    return;
                }

                var match = await ResolveReviewRecordAsync(memory, target, actionTail, cancellationToken).ConfigureAwait(false);
                var result = await memory.DemoteMemoryReviewAsync(
                        new DemoteMemoryReview(
                            match?.Id ?? new MemoryRecordId(target),
                            match?.MemorySpaceId ?? ReadMemorySpaceId(actionTail),
                            match?.Key,
                            ReadOptionValue(actionTail, "--reason")),
                        cancellationToken)
                    .ConfigureAwait(false);
                context.WriteLine(FormatMutationResult("已降权待审记忆", result), !result.Success);
                return;
            }
            case "restore":
            {
                var target = ReadReviewTarget(actionTail);
                if (string.IsNullOrWhiteSpace(target))
                {
                    context.WriteLine("缺少待恢复的记忆 record id：/memory review restore <record-id>", true);
                    return;
                }

                var result = await memory.RestoreMemoryReviewAsync(
                        new RestoreMemoryReview(
                            new MemoryRecordId(target),
                            ReadMemorySpaceId(actionTail),
                            ReadOptionValue(actionTail, "--key"),
                            ReadOptionValue(actionTail, "--reason")),
                        cancellationToken)
                    .ConfigureAwait(false);
                context.WriteLine(FormatMutationResult("已恢复待审记忆", result), !result.Success);
                return;
            }
            case "merge":
            {
                var target = ReadReviewTarget(actionTail);
                var targetRecordId = ReadOptionValue(actionTail, "--target-record-id") ?? ReadOptionValue(actionTail, "--target-id");
                var memorySpaceId = ReadMemorySpaceId(actionTail);
                var reason = ReadOptionValue(actionTail, "--reason");
                if (string.IsNullOrWhiteSpace(target)
                    || string.IsNullOrWhiteSpace(targetRecordId)
                    || memorySpaceId is null
                    || string.IsNullOrWhiteSpace(reason))
                {
                    context.WriteLine("用法：/memory review merge <review-record-id> --target-record-id <record-id> --memory-space-id <id> --reason <text>", true);
                    return;
                }

                var result = await memory.MergeMemoryReviewAsync(
                        new MergeMemoryReview(
                            new MemoryRecordId(target),
                            new MemoryRecordId(targetRecordId),
                            memorySpaceId.Value,
                            reason!,
                            ReadOptionValue(actionTail, "--merged-key")),
                        cancellationToken)
                    .ConfigureAwait(false);
                context.WriteLine(FormatMutationResult("已合并待审记忆", result), !result.Success);
                return;
            }
            default:
                context.WriteLine(BuildReviewUsage(), true);
                return;
        }
    }

    private static string BuildReviewUsage()
        => "用法：/memory review [list|approve|reject|demote|merge|restore|delete|feedback] [record-id] [--memory-space-id <id>] [--json]";

    private static (string Verb, string Tail) SplitVerb(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        var index = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return index < 0
            ? (trimmed, string.Empty)
            : (trimmed[..index], trimmed[(index + 1)..].Trim());
    }

    private static string NormalizeVerb(string verb)
        => verb.Trim().ToLowerInvariant();

    private static MemoryScopeKind? ReadScopeKind(string tail)
    {
        var value = ReadOptionValue(tail, "--scope-kind") ?? ReadOptionValue(tail, "--memory-scope-kind");
        return Enum.TryParse<MemoryScopeKind>(value, ignoreCase: true, out var scopeKind) ? scopeKind : null;
    }

    private static MemorySpaceId? ReadMemorySpaceId(string tail)
        => ReadOptionValue(tail, "--memory-space-id") is { Length: > 0 } memorySpaceId
            ? new MemorySpaceId(memorySpaceId)
            : null;

    private static MemoryMergeDecision? ReadMergeDecision(string tail)
        => Enum.TryParse<MemoryMergeDecision>(
            ReadOptionValue(tail, "--decision"),
            ignoreCase: true,
            out var decision)
            ? decision
            : null;

    private static ResolveMemoryOverlay ReadOverlayQuery(string tail)
        => new(
            ReadOptionValue(tail, "--memory-space-id") is { Length: > 0 } memorySpaceId
                ? new MemorySpaceId(memorySpaceId)
                : null,
            ReadOptionValue(tail, "--space-id") is { Length: > 0 } collaborationSpaceId
                ? new CollaborationSpaceId(collaborationSpaceId)
                : null);

    private static ListMemoryReviews BuildReviewListQuery(string tail)
        => new(
            ReadMemorySpaceId(tail),
            ReadOptionValue(tail, "--key"),
            ReadLifecycleStatus(tail) ?? MemoryLifecycleStatus.PendingReview);

    private static MemoryLifecycleStatus? ReadLifecycleStatus(string tail)
        => Enum.TryParse<MemoryLifecycleStatus>(
            ReadOptionValue(tail, "--status") ?? ReadOptionValue(tail, "--lifecycle-status"),
            ignoreCase: true,
            out var status)
            ? status
            : null;

    private static T ReadPayloadOrDefault<T>(string tail, T defaultValue, JsonSerializerOptions jsonOptions)
        where T : class
        => TryReadPayloadText(tail, out var payloadText)
            ? DeserializePayload<T>(payloadText, jsonOptions)
            : defaultValue;

    private static T ReadRequiredPayload<T>(string tail, JsonSerializerOptions jsonOptions)
        where T : class
    {
        if (!TryReadPayloadText(tail, out var payloadText))
        {
            throw new InvalidOperationException("缺少必填参数：--payload-json <json> 或 --payload-file <path>");
        }

        return DeserializePayload<T>(payloadText, jsonOptions);
    }

    private static async Task<AddMemory> ReadAddCommandAsync(
        IMemoryControlPlane memory,
        string tail,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken)
    {
        if (TryReadPayloadText(tail, out var payloadText))
        {
            return DeserializePayload<AddMemory>(payloadText, jsonOptions);
        }

        var key = ReadOptionValue(tail, "--key");
        var value = ReadOptionValue(tail, "--value") ?? ReadOptionValue(tail, "--text");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("缺少必填参数：/memory add 需要 --key <key> 与 --value <value>，或传入 --payload-json / --payload-file。");
        }

        var memorySpaceId = ReadMemorySpaceId(tail)
                            ?? await ResolveDefaultWritableMemorySpaceAsync(memory, cancellationToken).ConfigureAwait(false);
        var confidence = decimal.TryParse(
            ReadOptionValue(tail, "--confidence"),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsedConfidence)
            ? parsedConfidence
            : 1m;
        return new AddMemory(
            memorySpaceId,
            key,
            StructuredValue.FromString(value),
            confidence);
    }

    private static async Task<MemorySpaceId> ResolveDefaultWritableMemorySpaceAsync(
        IMemoryControlPlane memory,
        CancellationToken cancellationToken)
    {
        var spaces = await memory.ListMemorySpacesAsync(new ListMemorySpaces(), cancellationToken).ConfigureAwait(false);
        var preferred = spaces
            .Where(static space => !space.IsReadOnly)
            .OrderBy(static space => DefaultWritePriority(space.ScopeKind))
            .ThenBy(static space => space.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (preferred is not null)
        {
            return preferred.Id;
        }

        throw new InvalidOperationException("未发现可写记忆空间；请通过 --memory-space-id 显式指定目标，或先检查 /memory spaces。");
    }

    private static int DefaultWritePriority(MemoryScopeKind scopeKind)
        => scopeKind switch
        {
            MemoryScopeKind.Workspace => 0,
            MemoryScopeKind.User => 1,
            MemoryScopeKind.Session => 2,
            _ => 3,
        };

    private static async Task<FactMemoryRecord?> ResolveReviewRecordAsync(
        IMemoryControlPlane memory,
        string target,
        string tail,
        CancellationToken cancellationToken)
    {
        var result = await memory.ListMemoryReviewsAsync(
                new ListMemoryReviews(
                    ReadMemorySpaceId(tail),
                    ReadOptionValue(tail, "--key"),
                    LifecycleStatus: MemoryLifecycleStatus.PendingReview),
                cancellationToken)
            .ConfigureAwait(false);
        return result.Items.Select(static item => item.Record).FirstOrDefault(record =>
            string.Equals(record.Id.Value, target, StringComparison.Ordinal)
            || string.Equals(record.Key, target, StringComparison.Ordinal));
    }

    private static string FormatReviewResult(MemoryReviewQueryResult result)
    {
        var lines = new List<string>
        {
            $"待审记忆：{result.Items.Count} 项（total={result.TotalCount}）",
        };

        if (result.DegradedProviders.Count > 0)
        {
            lines.Add($"Provider 降级：{string.Join(", ", result.DegradedProviders)}");
        }

        if (result.Items.Count == 0)
        {
            lines.Add("暂无 PendingReview 记忆。");
            return string.Join(Environment.NewLine, lines);
        }

        for (var index = 0; index < result.Items.Count; index++)
        {
            var item = result.Items[index];
            var record = item.Record;
            lines.Add($"{index + 1}. {record.Key}");
            lines.Add($"   id: {record.Id.Value}");
            lines.Add($"   space: {record.MemorySpaceId.Value}");
            lines.Add($"   value: {FormatStructuredValue(record.Value)}");
            lines.Add($"   confidence: {record.Confidence:0.###}; lifecycle: {record.LifecycleStatus}; formation: {record.FormationPath}; counterexample: {(record.IsCounterexample ? "yes" : "no")}");
            lines.Add($"   usage: {record.UsageCount}; lastUsed: {FormatDate(record.LastUsedAt)}");
            lines.Add($"   sources: {FormatSources(record.Sources)}");
            if (item.Candidate is not null)
            {
                lines.Add($"   candidate: confidence={item.Candidate.Confidence:0.###}; source={FormatOptionalSource(item.Candidate.Source)}");
            }

            if (item.Evidence.Count > 0 || record.ValidationEvidence.Count > 0)
            {
                lines.Add($"   evidence: stored={item.Evidence.Count}; validation={record.ValidationEvidence.Count}");
            }

            if (item.SupersedeLinks.Count > 0)
            {
                lines.Add($"   supersede: {string.Join(", ", item.SupersedeLinks.Select(FormatSupersedeLink))}");
            }

            if (item.Audit.Count > 0)
            {
                lines.Add($"   audit: {string.Join(", ", item.Audit.Take(3).Select(FormatAuditSummary))}");
            }
        }

        lines.Add("操作：/memory review approve|reject|demote|merge|restore|delete|feedback ...");
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatMutationResult(string label, MemoryMutationResult result)
    {
        if (result.Success)
        {
            return $"{label}：{result.RecordId?.Value ?? "<unknown>"}；状态={result.LifecycleStatus?.ToString() ?? "<unknown>"}；效果={result.Effect}";
        }

        return $"{label}失败：{FormatDegradedReason(result.DegradedReason)}";
    }

    private static string FormatDegradedReason(string? reason)
        => string.Equals(reason, "memory_space_read_only", StringComparison.Ordinal)
            ? "目标记忆空间是只读空间。请通过 /memory spaces 查看 read-write 目标，通常优先写入当前 workspace，其次 user 或 session。"
            : reason ?? "unknown";

    private static string FormatStructuredValue(StructuredValue value)
    {
        var text = value.Kind switch
        {
            StructuredValueKind.String => value.StringValue ?? string.Empty,
            StructuredValueKind.Number => value.NumberValue ?? string.Empty,
            StructuredValueKind.Boolean => value.BooleanValue?.ToString() ?? string.Empty,
            StructuredValueKind.Null => "<null>",
            _ => JsonSerializer.Serialize(value.ToPlainObject()),
        };
        return text.Length <= 160 ? text : text[..157] + "...";
    }

    private static string FormatSources(IReadOnlyList<MemorySourceRef> sources)
        => sources.Count == 0
            ? "<none>"
            : string.Join(", ", sources.Select(source => $"{source.SourceKind}:{source.SourceId}"));

    private static string FormatOptionalSource(MemorySourceRef? source)
        => source is null ? "<none>" : $"{source.SourceKind}:{source.SourceId}";

    private static string FormatSupersedeLink(MemorySupersedeLink link)
        => $"{link.OldRecordId.Value}->{link.NewRecordId.Value}";

    private static string FormatAuditSummary(MemoryReviewAuditSummary audit)
    {
        var parts = new List<string>
        {
            $"{audit.Operation}/{audit.Effect}@{audit.OccurredAt:yyyy-MM-dd HH:mm:ss}",
        };
        if (!string.IsNullOrWhiteSpace(audit.Reason))
        {
            parts.Add($"原因={audit.Reason}");
        }

        if (audit.Metadata.TryGetValue("proposalKind", out var proposalKind)
            && !string.IsNullOrWhiteSpace(proposalKind))
        {
            parts.Add($"提案={proposalKind}");
        }

        if (audit.Metadata.TryGetValue("targetRecordId", out var targetRecordId)
            && !string.IsNullOrWhiteSpace(targetRecordId))
        {
            parts.Add($"目标={targetRecordId}");
        }

        return string.Join("; ", parts);
    }

    private static string FormatDate(DateTimeOffset? value)
        => value?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "<never>";

    private static string? ReadReviewTarget(string tail)
        => ReadOptionValue(tail, "--record-id")
           ?? ReadOptionValue(tail, "--id")
           ?? ReadFirstToken(tail);

    private static string? ReadFirstToken(string tail)
    {
        var value = tail.Trim();
        if (value.Length == 0 || value.StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        var index = value.IndexOf(' ', StringComparison.Ordinal);
        return index < 0 ? value : value[..index];
    }

    private static string? ReadTailAfterFirstToken(string tail)
    {
        var value = tail.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        var firstToken = ReadFirstToken(value);
        if (string.IsNullOrWhiteSpace(firstToken) || value.Length <= firstToken.Length)
        {
            return null;
        }

        var rest = value[firstToken.Length..].Trim();
        return rest.StartsWith("--", StringComparison.Ordinal) ? null : rest;
    }

    private static bool HasFlag(string tail, string optionName)
        => tail.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(token => string.Equals(token, optionName, StringComparison.OrdinalIgnoreCase));

    private static T DeserializePayload<T>(string payloadText, JsonSerializerOptions jsonOptions)
        where T : class
        => JsonSerializer.Deserialize<T>(payloadText, CreateMemoryPayloadJsonOptions(jsonOptions))
           ?? throw new InvalidOperationException($"无法解析 memory payload：{typeof(T).Name}");

    private static JsonSerializerOptions CreateMemoryPayloadJsonOptions(JsonSerializerOptions baseOptions)
    {
        var options = new JsonSerializerOptions(baseOptions);
        options.Converters.Add(new MemorySpaceIdJsonConverter());
        options.Converters.Add(new MemoryRecordIdJsonConverter());
        return options;
    }

    private sealed class MemorySpaceIdJsonConverter : JsonConverter<MemorySpaceId>
    {
        public override MemorySpaceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(ReadIdentifierValue(ref reader, "memorySpaceId"));

        public override void Write(Utf8JsonWriter writer, MemorySpaceId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    private sealed class MemoryRecordIdJsonConverter : JsonConverter<MemoryRecordId>
    {
        public override MemoryRecordId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(ReadIdentifierValue(ref reader, "memoryRecordId"));

        public override void Write(Utf8JsonWriter writer, MemoryRecordId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    private static string ReadIdentifierValue(ref Utf8JsonReader reader, string subject)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() ?? throw new JsonException($"{subject} 不能为空。");
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"{subject} 必须是字符串或包含 value 的对象。");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        if (!document.RootElement.TryGetProperty("value", out var valueElement) || valueElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{subject} 对象必须包含字符串 value 字段。");
        }

        return valueElement.GetString() ?? throw new JsonException($"{subject}.value 不能为空。");
    }

    private static bool TryReadPayloadText(string tail, out string payloadText)
    {
        var trimmed = tail.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            payloadText = trimmed;
            return true;
        }

        if (ReadOptionValue(tail, "--payload-json") is { Length: > 0 } inlineJson)
        {
            payloadText = inlineJson;
            return true;
        }

        if (ReadOptionValue(tail, "--payload-file") is { Length: > 0 } payloadFile)
        {
            payloadText = File.ReadAllText(payloadFile);
            return true;
        }

        payloadText = string.Empty;
        return false;
    }

    private static string? ReadOptionValue(string tail, string optionName)
    {
        var marker = optionName + " ";
        var index = tail.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = index + marker.Length;
        var value = tail[start..].TrimStart();
        if (value.Length == 0)
        {
            return null;
        }

        if (value[0] is '"' or '\'')
        {
            var quote = value[0];
            var end = value.IndexOf(quote, 1);
            return end > 0 ? value[1..end] : value[1..];
        }

        if (value.StartsWith("{", StringComparison.Ordinal) || value.StartsWith("[", StringComparison.Ordinal))
        {
            return value;
        }

        var next = value.IndexOf(' ', StringComparison.Ordinal);
        return next < 0 ? value : value[..next];
    }
}

internal sealed record InteractiveMemoryCommandContext(
    JsonSerializerOptions JsonOptions,
    Action<string, bool> WriteLine);
