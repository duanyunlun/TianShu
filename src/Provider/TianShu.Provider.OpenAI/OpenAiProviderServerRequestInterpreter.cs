using System.Text;
using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI;

/// <summary>
/// OpenAI provider 服务端请求载荷解释器。
/// Server-request payload interpreter for the OpenAI provider.
/// </summary>
public sealed class OpenAiProviderServerRequestInterpreter : IProviderServerRequestInterpreter
{
    /// <inheritdoc />
    public ProviderServerRequestPayload? Interpret(ProviderServerRequestRoute route, JsonElement parameters)
    {
        ArgumentNullException.ThrowIfNull(route);

        return route.Kind switch
        {
            ProviderServerRequestKind.CommandExecutionApproval
                when route.ApprovalResponsePayloadKind == ProviderApprovalResponsePayloadKind.Legacy
                => InterpretLegacyCommandExecutionApproval(parameters),
            ProviderServerRequestKind.CommandExecutionApproval => InterpretCommandExecutionApproval(parameters),
            ProviderServerRequestKind.FileChangeApproval
                when route.ApprovalResponsePayloadKind == ProviderApprovalResponsePayloadKind.Legacy
                => InterpretLegacyApplyPatchApproval(parameters),
            ProviderServerRequestKind.FileChangeApproval => InterpretFileChangeApproval(parameters),
            ProviderServerRequestKind.ToolApproval => InterpretToolApproval(parameters),
            ProviderServerRequestKind.McpServerElicitation => InterpretMcpServerElicitation(parameters),
            ProviderServerRequestKind.PermissionApproval => InterpretPermissionApproval(parameters),
            ProviderServerRequestKind.ToolUserInput => InterpretToolUserInput(parameters),
            ProviderServerRequestKind.DynamicToolCall => InterpretDynamicToolCall(parameters),
            _ => null,
        };
    }

    private static ProviderServerRequestPayload? InterpretCommandExecutionApproval(JsonElement parameters)
    {
        var dto = OpenAiJsonHelpers.Deserialize<OpenAiCommandExecutionRequestApprovalParamsDto>(parameters);
        if (dto is null)
        {
            return null;
        }

        var callId = dto.ApprovalId
            ?? dto.ItemId
            ?? $"approval-{Guid.NewGuid():N}";
        var summary = BuildSummary(dto.Command, dto.Reason);
        var metadata = dto.SkillMetadataObject;
        var metadataFields = metadata.HasValue ? BuildApprovalMetadataFields(metadata.Value) : Array.Empty<ApprovalMetadataFieldPayload>();
        var approvalPayload = new ApprovalRequestPayload(
            "commandExecution",
            null,
            dto.ResolvedAvailableDecisions,
            summary,
            metadataFields,
            dto.ResolvedAvailableDecisionOptions,
            dto.ResolvedProposedExecPolicyAmendment,
            dto.ResolvedProposedNetworkPolicyAmendments);
        return new ProviderCommandExecutionApprovalRequest(
            dto.ThreadId,
            dto.TurnId,
            dto.ItemId,
            callId,
            summary,
            approvalPayload);
    }

    private static ProviderServerRequestPayload? InterpretLegacyCommandExecutionApproval(JsonElement parameters)
    {
        var dto = OpenAiJsonHelpers.Deserialize<OpenAiLegacyExecCommandApprovalParamsDto>(parameters);
        if (dto is null)
        {
            return null;
        }

        var callId = dto.ApprovalId
            ?? dto.CallId
            ?? $"approval-{Guid.NewGuid():N}";
        var summary = BuildSummary(dto.ResolvedCommandText, dto.Reason);
        return new ProviderCommandExecutionApprovalRequest(
            dto.ResolvedThreadId,
            dto.TurnId,
            null,
            callId,
            summary,
            new ApprovalRequestPayload(
                "commandExecution",
                null,
                null,
                summary,
                Array.Empty<ApprovalMetadataFieldPayload>(),
                null,
                null,
                null));
    }

    private static ProviderServerRequestPayload? InterpretFileChangeApproval(JsonElement parameters)
    {
        var dto = OpenAiJsonHelpers.Deserialize<OpenAiFileChangeRequestApprovalParamsDto>(parameters);
        if (dto is null)
        {
            return null;
        }

        var callId = dto.ItemId ?? $"file-{Guid.NewGuid():N}";
        var summary = Normalize(dto.Reason) ?? string.Empty;
        return new ProviderFileChangeApprovalRequest(
            dto.ThreadId,
            dto.TurnId,
            dto.ItemId,
            callId,
            summary,
            new ApprovalRequestPayload("fileChange", null, dto.ResolvedAvailableDecisions, summary, Array.Empty<ApprovalMetadataFieldPayload>()));
    }

    private static ProviderServerRequestPayload? InterpretLegacyApplyPatchApproval(JsonElement parameters)
    {
        var dto = OpenAiJsonHelpers.Deserialize<OpenAiLegacyApplyPatchApprovalParamsDto>(parameters);
        if (dto is null)
        {
            return null;
        }

        var callId = dto.CallId ?? $"file-{Guid.NewGuid():N}";
        var summary = BuildSummary(
            dto.Reason,
            string.IsNullOrWhiteSpace(dto.GrantRoot) ? null : $"grantRoot={dto.GrantRoot}");
        return new ProviderFileChangeApprovalRequest(
            dto.ResolvedThreadId,
            dto.TurnId,
            null,
            callId,
            summary,
            new ApprovalRequestPayload(
                "fileChange",
                null,
                null,
                summary,
                Array.Empty<ApprovalMetadataFieldPayload>(),
                null,
                null,
                null));
    }

    private static ProviderServerRequestPayload? InterpretToolApproval(JsonElement parameters)
    {
        var dto = OpenAiJsonHelpers.Deserialize<OpenAiToolRequestApprovalParamsDto>(parameters);
        if (dto is null)
        {
            return null;
        }

        var callId = dto.CallId ?? dto.ItemId ?? $"approval-{Guid.NewGuid():N}";
        var toolName = Normalize(dto.ToolName)
            ?? Normalize(dto.Item?.ToolName)
            ?? "tool";
        var summary = BuildSummary(
            dto.ToolName ?? dto.Item?.ToolName,
            dto.Arguments ?? dto.Input ?? dto.Item?.Arguments ?? dto.Item?.Input,
            dto.Reason);
        var metadata = dto.ResolvedMeta;
        var metadataFields = metadata.HasValue ? BuildApprovalMetadataFields(metadata.Value) : Array.Empty<ApprovalMetadataFieldPayload>();
        return new ProviderToolApprovalRequest(
            dto.ThreadId,
            dto.TurnId,
            dto.ItemId,
            callId,
            toolName,
            Normalize(dto.ServerName),
            summary,
            new ApprovalRequestPayload(toolName, dto.ResolvedApprovalKind, dto.ResolvedAvailableDecisions, summary, metadataFields));
    }

    private static ProviderServerRequestPayload? InterpretMcpServerElicitation(JsonElement parameters)
    {
        var dto = OpenAiJsonHelpers.Deserialize<OpenAiMcpServerElicitationRequestParamsDto>(parameters);
        if (dto is null)
        {
            return null;
        }

        var summary = BuildMcpServerElicitationSummary(dto);
        var normalizedApprovalKind = Normalize(dto.ResolvedApprovalKind);
        var callId = Normalize(dto.ElicitationId) ?? $"elicitation-{Guid.NewGuid():N}";
        var serverName = Normalize(dto.ServerName);
        var toolName = string.Equals(normalizedApprovalKind, "tool_suggestion", StringComparison.OrdinalIgnoreCase)
            ? "tool_suggest"
            : serverName ?? "mcpServerElicitation";

        if (string.Equals(normalizedApprovalKind, "tool_suggestion", StringComparison.OrdinalIgnoreCase))
        {
            var metadata = dto.ResolvedMeta;
            var metadataFields = metadata.HasValue ? BuildApprovalMetadataFields(metadata.Value) : Array.Empty<ApprovalMetadataFieldPayload>();
            return new ProviderMcpServerElicitationApprovalRequest(
                dto.ThreadId,
                dto.TurnId,
                callId,
                toolName,
                serverName,
                summary,
                new ApprovalRequestPayload(toolName, normalizedApprovalKind, dto.ResolvedAvailableDecisions, summary, metadataFields));
        }

        return new ProviderMcpServerElicitationUserInputRequest(
            dto.ThreadId,
            dto.TurnId,
            callId,
            toolName,
            serverName,
            summary,
            BuildMcpServerElicitationUserInputPayload(dto, summary));
    }

    private static ProviderServerRequestPayload? InterpretPermissionApproval(JsonElement parameters)
    {
        var dto = OpenAiJsonHelpers.Deserialize<OpenAiPermissionRequestParamsDto>(parameters);
        if (dto is null)
        {
            return null;
        }

        var callId = dto.ItemId ?? dto.CallId ?? $"permissions-{Guid.NewGuid():N}";
        var permissionsJson = dto.Permissions.ValueKind == JsonValueKind.Object
            ? dto.Permissions.GetRawText()
            : "{}";
        var summary = BuildSummary(dto.Reason, permissionsJson);
        return new ProviderPermissionRequestApprovalRequest(
            dto.ThreadId,
            dto.TurnId,
            dto.ItemId,
            callId,
            summary,
            new PermissionRequestPayload(dto.Reason, BuildPermissionFieldPayloads(dto.Permissions), permissionsJson, summary));
    }

    private static ProviderServerRequestPayload? InterpretToolUserInput(JsonElement parameters)
    {
        var dto = OpenAiJsonHelpers.Deserialize<OpenAiToolRequestUserInputParamsDto>(parameters);
        if (dto is null)
        {
            return null;
        }

        var callId = dto.ItemId ?? $"input-{Guid.NewGuid():N}";
        var summary = BuildToolRequestUserInputSummary(dto);
        return new ProviderToolUserInputRequest(
            dto.ThreadId,
            dto.TurnId,
            dto.ItemId,
            callId,
            summary,
            BuildToolUserInputPayload(dto, summary));
    }

    private static ProviderServerRequestPayload? InterpretDynamicToolCall(JsonElement parameters)
    {
        var dto = OpenAiJsonHelpers.Deserialize<OpenAiDynamicToolCallRequestParamsDto>(parameters);
        if (dto is null)
        {
            return null;
        }

        var arguments = dto.Arguments.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null
            ? StructuredValue.FromJsonElement(dto.Arguments)
            : StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal));
        var toolName = Normalize(dto.Tool) ?? "dynamicTool";
        var inputText = JsonSerializer.Serialize(arguments, OpenAiJsonHelpers.SerializerOptions);
        return new ProviderDynamicToolCallRequest(
            dto.ThreadId,
            dto.TurnId,
            dto.CallId ?? $"tool-{Guid.NewGuid():N}",
            toolName,
            arguments,
            inputText);
    }

    private static string BuildMcpServerElicitationSummary(OpenAiMcpServerElicitationRequestParamsDto parameters)
    {
        var parts = new List<string>();
        var message = Normalize(parameters.Message);
        if (!string.IsNullOrWhiteSpace(message))
        {
            parts.Add(message);
        }

        var toolName = Normalize(parameters.ResolvedMetaToolName);
        if (!string.IsNullOrWhiteSpace(toolName))
        {
            parts.Add($"tool={toolName}");
        }

        var mode = Normalize(parameters.Mode);
        if (!string.IsNullOrWhiteSpace(mode))
        {
            parts.Add($"mode={mode}");
        }

        var url = Normalize(parameters.ResolvedUrl);
        if (!string.IsNullOrWhiteSpace(url))
        {
            parts.Add($"url={url}");
        }

        var installUrl = Normalize(parameters.ResolvedInstallUrl);
        if (!string.IsNullOrWhiteSpace(installUrl))
        {
            parts.Add($"install_url={installUrl}");
        }

        return parts.Count == 0 ? "mcp server elicitation request" : string.Join(" | ", parts);
    }

    private static UserInputRequestPayload BuildMcpServerElicitationUserInputPayload(
        OpenAiMcpServerElicitationRequestParamsDto parameters,
        string summary)
    {
        var questions = BuildMcpServerElicitationQuestions(parameters);
        var requestedSchema = parameters.ResolvedRequestedSchema is { } schema
            ? StructuredValue.FromJsonElement(schema)
            : null;
        return new UserInputRequestPayload(
            questions,
            Normalize(summary) ?? BuildMcpServerElicitationSummary(parameters),
            Normalize(parameters.Mode),
            requestedSchema,
            parameters.ResolvedUrl,
            Normalize(parameters.ServerName),
            Normalize(parameters.ElicitationId));
    }

    private static IReadOnlyList<UserInputQuestionPayload> BuildMcpServerElicitationQuestions(OpenAiMcpServerElicitationRequestParamsDto parameters)
    {
        if (string.Equals(Normalize(parameters.Mode), "url", StringComparison.OrdinalIgnoreCase))
        {
            var prompt = Normalize(parameters.Message)
                ?? parameters.ResolvedUrl
                ?? "请输入 MCP server elicitation 内容。";
            return
            [
                new UserInputQuestionPayload(
                    "content",
                    Normalize(parameters.ServerName) ?? "content",
                    prompt,
                    false,
                    false,
                    null),
            ];
        }

        if (parameters.ResolvedRequestedSchema is { ValueKind: JsonValueKind.Object } schema)
        {
            var questions = BuildMcpServerElicitationQuestionsFromSchema(schema);
            if (questions.Count > 0)
            {
                return questions;
            }
        }

        return Array.Empty<UserInputQuestionPayload>();
    }

    private static IReadOnlyList<UserInputQuestionPayload> BuildMcpServerElicitationQuestionsFromSchema(JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<UserInputQuestionPayload>();
        }

        var required = schema.TryGetProperty("required", out var requiredElement) && requiredElement.ValueKind == JsonValueKind.Array
            ? requiredElement.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => Normalize(item.GetString()))
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var questions = new List<UserInputQuestionPayload>();
        foreach (var property in properties.EnumerateObject())
        {
            var id = Normalize(property.Name);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var header = Normalize(OpenAiServerRequestDtoHelpers.ReadString(property.Value, "title")) ?? id;
            var description = Normalize(OpenAiServerRequestDtoHelpers.ReadString(property.Value, "description"));
            var prompt = description ?? header;
            if (required.Contains(id))
            {
                prompt += "（必填）";
            }

            UserInputOptionPayload[]? options = null;
            if (property.Value.TryGetProperty("enum", out var enumElement) && enumElement.ValueKind == JsonValueKind.Array)
            {
                options = enumElement
                    .EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => new UserInputOptionPayload(item.GetString() ?? string.Empty, null))
                    .Where(static option => !string.IsNullOrWhiteSpace(option.Label))
                    .ToArray();
            }

            var format = Normalize(OpenAiServerRequestDtoHelpers.ReadString(property.Value, "format"));
            var isSecret = string.Equals(format, "password", StringComparison.OrdinalIgnoreCase);
            questions.Add(new UserInputQuestionPayload(
                id,
                header,
                prompt,
                isSecret,
                false,
                options));
        }

        return questions;
    }

    private static UserInputRequestPayload BuildToolUserInputPayload(OpenAiToolRequestUserInputParamsDto parameters, string summary)
    {
        var questions = new List<UserInputQuestionPayload>();

        if (parameters.Questions is { Count: > 0 })
        {
            foreach (var item in parameters.Questions)
            {
                var id = Normalize(item.Id);
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var header = Normalize(item.Header) ?? id;
                var prompt = Normalize(item.Prompt) ?? Normalize(item.Question) ?? header;
                var options = item.Options?
                    .Select(static option => new UserInputOptionPayload(
                        option.Label ?? string.Empty,
                        Normalize(option.Description)))
                    .Where(static option => !string.IsNullOrWhiteSpace(option.Label))
                    .ToArray();

                questions.Add(new UserInputQuestionPayload(
                    id,
                    header,
                    prompt,
                    item.IsSecret == true,
                    item.IsOther == true,
                    options));
            }
        }

        return new UserInputRequestPayload(questions, Normalize(summary) ?? BuildToolRequestUserInputSummary(parameters));
    }

    private static string BuildToolRequestUserInputSummary(OpenAiToolRequestUserInputParamsDto parameters)
    {
        if (parameters.Questions is not { Count: > 0 } questions)
        {
            return "等待用户补充输入";
        }

        var summary = new StringBuilder();
        foreach (var question in questions)
        {
            var header = Normalize(question.Header)
                ?? Normalize(question.Prompt)
                ?? Normalize(question.Question)
                ?? Normalize(question.Id);
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            if (summary.Length > 0)
            {
                summary.AppendLine();
            }

            summary.Append("- ");
            summary.Append(header);
        }

        return summary.Length == 0 ? "等待用户补充输入" : summary.ToString();
    }

    private static IReadOnlyList<PermissionFieldPayload> BuildPermissionFieldPayloads(JsonElement permissions)
    {
        if (permissions.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<PermissionFieldPayload>();
        }

        var fields = new List<PermissionFieldPayload>();
        foreach (var property in permissions.EnumerateObject())
        {
            fields.Add(BuildPermissionFieldPayload(property.Name, property.Value));
        }

        return fields;
    }

    private static PermissionFieldPayload BuildPermissionFieldPayload(string key, JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => new PermissionFieldPayload(key, "bool", value.GetBoolean().ToString().ToLowerInvariant()),
            JsonValueKind.Number => new PermissionFieldPayload(key, "number", value.ToString()),
            JsonValueKind.Object or JsonValueKind.Array => new PermissionFieldPayload(key, "json", value.GetRawText()),
            JsonValueKind.Null => new PermissionFieldPayload(key, "null", string.Empty),
            _ => new PermissionFieldPayload(
                key,
                "string",
                value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()),
        };
    }

    private static IReadOnlyList<ApprovalMetadataFieldPayload> BuildApprovalMetadataFields(JsonElement metadata)
    {
        var fields = new List<ApprovalMetadataFieldPayload>();
        AppendApprovalMetadataFields(fields, prefix: null, metadata);
        return fields;
    }

    private static void AppendApprovalMetadataFields(
        List<ApprovalMetadataFieldPayload> fields,
        string? prefix,
        JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
                return;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    AppendApprovalMetadataFields(fields, CombineApprovalMetadataPath(prefix, property.Name), property.Value);
                }

                return;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    AppendApprovalMetadataFields(fields, CombineApprovalMetadataPath(prefix, index.ToString()), item);
                    index++;
                }

                return;
            case JsonValueKind.True:
            case JsonValueKind.False:
                fields.Add(new ApprovalMetadataFieldPayload(prefix ?? string.Empty, "bool", value.GetBoolean().ToString().ToLowerInvariant()));
                return;
            case JsonValueKind.Number:
                fields.Add(new ApprovalMetadataFieldPayload(prefix ?? string.Empty, "number", value.ToString()));
                return;
            case JsonValueKind.String:
                fields.Add(new ApprovalMetadataFieldPayload(prefix ?? string.Empty, "string", value.GetString() ?? string.Empty));
                return;
            default:
                fields.Add(new ApprovalMetadataFieldPayload(prefix ?? string.Empty, value.ValueKind.ToString().ToLowerInvariant(), value.GetRawText()));
                return;
        }
    }

    private static string CombineApprovalMetadataPath(string? prefix, string segment)
        => string.IsNullOrWhiteSpace(prefix) ? segment : $"{prefix}.{segment}";

    private static string BuildSummary(params string?[] parts)
        => string.Join(" | ", parts.Where(static value => !string.IsNullOrWhiteSpace(value)));

    private static string? Normalize(string? value)
        => OpenAiServerRequestDtoHelpers.Normalize(value);
}
