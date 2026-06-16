using System.Globalization;
using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI;

/// <summary>
/// OpenAI provider 服务端请求响应序列化器。
/// Server-request response serializer for the OpenAI provider.
/// </summary>
public sealed class OpenAiProviderServerRequestResponseSerializer : IProviderServerRequestResponseSerializer
{
    /// <inheritdoc />
    public ProviderApprovalResponseFormats SerializeApprovalResponse(ProviderApprovalOutcome response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var decisionPayload = BuildApprovalDecisionPayload(response);
        return new ProviderApprovalResponseFormats(
            decisionPayload,
            BuildStandardApprovalResponsePayload(response, decisionPayload),
            BuildLegacyApprovalResponsePayload(response),
            BuildMcpServerElicitationApprovalResponsePayload(response));
    }

    /// <inheritdoc />
    public ProviderPermissionResponseFormats SerializePermissionResponse(ProviderPermissionGrantOutcome response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var scope = response.Scope == ProviderPermissionScope.Session ? "session" : "turn";
        var payload = StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["permissions"] = response.Permissions.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToPlainObject(),
                StringComparer.Ordinal),
            ["scope"] = scope,
        });

        return new ProviderPermissionResponseFormats(
            payload,
            $"scope={scope} | permissions={response.Permissions.Count}");
    }

    /// <inheritdoc />
    public ProviderUserInputResponseFormats SerializeUserInputResponse(ProviderUserInputOutcome response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var toolRequestPayload = BuildToolRequestUserInputPayload(response);
        var toolRequestSummary = JsonSerializer.Serialize(
            response.Answers.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToPlainObject(),
                StringComparer.Ordinal));

        var mcpAction = ResolveMcpServerElicitationAction(response);
        var mcpContent = BuildMcpServerElicitationResponseContent(response);
        var mcpPayload = StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["action"] = mcpAction,
            ["content"] = string.Equals(mcpAction, "accept", StringComparison.OrdinalIgnoreCase)
                ? mcpContent.ToPlainObject()
                : null,
        });

        var mcpSummary = string.Equals(mcpAction, "accept", StringComparison.OrdinalIgnoreCase)
            ? JsonSerializer.Serialize(mcpContent.ToPlainObject())
            : $"action={mcpAction}";
        var mcpStatus = string.Equals(mcpAction, "decline", StringComparison.OrdinalIgnoreCase)
            ? "declined"
            : string.Equals(mcpAction, "cancel", StringComparison.OrdinalIgnoreCase)
                ? "cancelled"
                : "completed";

        return new ProviderUserInputResponseFormats(
            toolRequestPayload,
            toolRequestSummary,
            mcpPayload,
            mcpAction,
            mcpSummary,
            mcpStatus);
    }

    /// <inheritdoc />
    public ProviderDynamicToolCallResponseFormats SerializeDynamicToolCallResponse(ToolInvocationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var contentItems = MaterializeDynamicToolOutputItems(result);
        var payload = StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["contentItems"] = contentItems.Select(static item => item.ToPlainObject()).ToArray(),
            ["success"] = result.Failure is null,
        });

        var outputText = string.Join(
            Environment.NewLine,
            contentItems
                .Select(static item => Normalize(item.Text) ?? Normalize(item.ImageUrl))
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .Cast<string>());

        return new ProviderDynamicToolCallResponseFormats(
            payload,
            outputText,
            result.Failure is null);
    }

    private static StructuredValue BuildApprovalDecisionPayload(ProviderApprovalOutcome response)
    {
        return response.Decision switch
        {
            ProviderApprovalDecision.Accept => StructuredValue.FromString("accept"),
            ProviderApprovalDecision.AcceptForSession => StructuredValue.FromString("acceptForSession"),
            ProviderApprovalDecision.AcceptAndRemember => StructuredValue.FromString("acceptAndRemember"),
            ProviderApprovalDecision.AcceptWithExecPolicyAmendment => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["acceptWithExecpolicyAmendment"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["execpolicy_amendment"] = response.ExecPolicyCommandPrefix?.ToArray() ?? Array.Empty<string>(),
                },
            }),
            ProviderApprovalDecision.ApplyNetworkPolicyAmendment => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["applyNetworkPolicyAmendment"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["network_policy_amendment"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["host"] = response.NetworkPolicyHost ?? string.Empty,
                        ["action"] = response.NetworkPolicyAction ?? "allow",
                    },
                },
            }),
            ProviderApprovalDecision.Cancel => StructuredValue.FromString("cancel"),
            _ => StructuredValue.FromString("decline"),
        };
    }

    private static StructuredValue BuildStandardApprovalResponsePayload(
        ProviderApprovalOutcome response,
        StructuredValue decisionPayload)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["decision"] = decisionPayload.ToPlainObject(),
        };

        var note = Normalize(response.Note);
        if (!string.IsNullOrWhiteSpace(note))
        {
            payload["reason"] = note;
        }

        var persist = response.Decision switch
        {
            ProviderApprovalDecision.AcceptForSession => "session",
            ProviderApprovalDecision.AcceptAndRemember => "always",
            _ => null,
        };
        if (!string.IsNullOrWhiteSpace(persist))
        {
            payload["_meta"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["persist"] = persist,
            };
        }

        return StructuredValue.FromPlainObject(payload);
    }

    private static StructuredValue BuildLegacyApprovalResponsePayload(ProviderApprovalOutcome response)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["decision"] = BuildLegacyApprovalDecisionPayload(response).ToPlainObject(),
        };

        var note = Normalize(response.Note);
        if (!string.IsNullOrWhiteSpace(note))
        {
            payload["reason"] = note;
        }

        return StructuredValue.FromPlainObject(payload);
    }

    private static StructuredValue BuildLegacyApprovalDecisionPayload(ProviderApprovalOutcome response)
    {
        return response.Decision switch
        {
            ProviderApprovalDecision.Accept => StructuredValue.FromString("approved"),
            ProviderApprovalDecision.AcceptForSession => StructuredValue.FromString("approved_for_session"),
            ProviderApprovalDecision.AcceptAndRemember => StructuredValue.FromString("approved_for_session"),
            ProviderApprovalDecision.AcceptWithExecPolicyAmendment => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["approved_execpolicy_amendment"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["proposed_execpolicy_amendment"] = response.ExecPolicyCommandPrefix?.ToArray() ?? Array.Empty<string>(),
                },
            }),
            ProviderApprovalDecision.ApplyNetworkPolicyAmendment => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["network_policy_amendment"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["network_policy_amendment"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["host"] = response.NetworkPolicyHost ?? string.Empty,
                        ["action"] = response.NetworkPolicyAction ?? "allow",
                    },
                },
            }),
            ProviderApprovalDecision.Cancel => StructuredValue.FromString("abort"),
            _ => StructuredValue.FromString("denied"),
        };
    }

    private static StructuredValue BuildMcpServerElicitationApprovalResponsePayload(ProviderApprovalOutcome response)
    {
        if (response.Decision == ProviderApprovalDecision.Decline)
        {
            return StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["action"] = "decline",
            });
        }

        if (response.Decision == ProviderApprovalDecision.Cancel)
        {
            return StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["action"] = "cancel",
            });
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["action"] = "accept",
            ["content"] = new Dictionary<string, object?>(StringComparer.Ordinal),
        };

        var persist = response.Decision switch
        {
            ProviderApprovalDecision.AcceptForSession => "session",
            ProviderApprovalDecision.AcceptAndRemember => "always",
            _ => null,
        };
        if (!string.IsNullOrWhiteSpace(persist))
        {
            payload["_meta"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["persist"] = persist,
            };
        }

        return StructuredValue.FromPlainObject(payload);
    }

    private static StructuredValue BuildToolRequestUserInputPayload(ProviderUserInputOutcome response)
    {
        var answers = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in response.Answers)
        {
            var questionId = Normalize(pair.Key);
            if (string.IsNullOrWhiteSpace(questionId))
            {
                continue;
            }

            answers[questionId] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["answers"] = NormalizeToolRequestUserInputAnswers(pair.Value),
            };
        }

        return StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["answers"] = answers,
        });
    }

    private static string[] NormalizeToolRequestUserInputAnswers(StructuredValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Kind switch
        {
            StructuredValueKind.String => string.IsNullOrWhiteSpace(value.StringValue) ? Array.Empty<string>() : [value.StringValue],
            StructuredValueKind.Number => string.IsNullOrWhiteSpace(value.NumberValue) ? Array.Empty<string>() : [value.NumberValue],
            StructuredValueKind.Boolean => value.BooleanValue is bool booleanValue
                ? [booleanValue ? "true" : "false"]
                : Array.Empty<string>(),
            StructuredValueKind.Array => value.Items.SelectMany(NormalizeToolRequestUserInputAnswers).ToArray(),
            StructuredValueKind.Object => [JsonSerializer.Serialize(value.ToPlainObject())],
            StructuredValueKind.Null => Array.Empty<string>(),
            _ => [Convert.ToString(value.ToPlainObject(), CultureInfo.InvariantCulture) ?? string.Empty],
        };
    }

    private static string ResolveMcpServerElicitationAction(ProviderUserInputOutcome response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (TryReadMcpServerElicitationActionToken(response.Answers, "_action", out var action))
        {
            return action;
        }

        return TryReadMcpServerElicitationActionToken(response.Answers, "action", out action)
            ? action
            : "accept";
    }

    private static bool TryReadMcpServerElicitationActionToken(
        IReadOnlyDictionary<string, StructuredValue> answers,
        string key,
        out string action)
    {
        action = string.Empty;
        if (!answers.TryGetValue(key, out var value) || value is null)
        {
            return false;
        }

        var token = Normalize(value.GetString());
        if (string.Equals(token, "decline", StringComparison.OrdinalIgnoreCase))
        {
            action = "decline";
            return true;
        }

        if (string.Equals(token, "cancel", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "abort", StringComparison.OrdinalIgnoreCase))
        {
            action = "cancel";
            return true;
        }

        if (string.Equals(token, "accept", StringComparison.OrdinalIgnoreCase))
        {
            action = "accept";
            return true;
        }

        return false;
    }

    private static StructuredValue BuildMcpServerElicitationResponseContent(ProviderUserInputOutcome response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var contentEntries = response.Answers
            .Where(static pair =>
                !string.Equals(pair.Key, "_action", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pair.Key, "action", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (contentEntries.Length == 0)
        {
            return StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal));
        }

        var explicitContentEntry = contentEntries.FirstOrDefault(static pair => string.Equals(pair.Key, "content", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(explicitContentEntry.Key))
        {
            return explicitContentEntry.Value;
        }

        if (contentEntries.Length == 1)
        {
            return contentEntries[0].Value;
        }

        return StructuredValue.FromObject(contentEntries.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.Ordinal));
    }

    private static IReadOnlyList<OpenAiDynamicToolOutputItem> MaterializeDynamicToolOutputItems(ToolInvocationResult result)
    {
        if (result.StreamItems.Count > 0)
        {
            var items = new List<OpenAiDynamicToolOutputItem>();
            foreach (var streamItem in result.StreamItems)
            {
                if (TryConvertDynamicToolOutputItem(streamItem, out var item))
                {
                    items.Add(item);
                }
            }

            if (items.Count > 0)
            {
                return items;
            }
        }

        return result.Failure is null
            ? Array.Empty<OpenAiDynamicToolOutputItem>()
            : [new OpenAiDynamicToolOutputItem("inputText", result.Failure.Message, null)];
    }

    private static bool TryConvertDynamicToolOutputItem(ToolStreamItem streamItem, out OpenAiDynamicToolOutputItem item)
    {
        var payload = streamItem.Payload;
        var channel = Normalize(streamItem.Channel);

        if (payload.Kind == StructuredValueKind.Object)
        {
            var type = Normalize(ReadStructuredScalarText(payload, "type"))
                ?? (IsImageDynamicToolOutputType(channel) ? "inputImage" : null);
            if (IsImageDynamicToolOutputType(type))
            {
                var imageUrl = Normalize(ReadStructuredScalarText(payload, "imageUrl"))
                    ?? Normalize(ReadStructuredScalarText(payload, "image_url"))
                    ?? Normalize(ReadStructuredScalarText(payload, "url"));
                if (!string.IsNullOrWhiteSpace(imageUrl))
                {
                    item = new OpenAiDynamicToolOutputItem("inputImage", null, imageUrl);
                    return true;
                }
            }

            var text = Normalize(ReadStructuredScalarText(payload, "text"))
                ?? Normalize(ReadStructuredScalarText(payload, "value"))
                ?? Normalize(ReadStructuredScalarText(payload, "message"));
            if (string.IsNullOrWhiteSpace(text))
            {
                text = JsonSerializer.Serialize(payload.ToPlainObject());
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                item = new OpenAiDynamicToolOutputItem(Normalize(type) ?? "inputText", text, null);
                return true;
            }
        }
        else
        {
            var scalar = Normalize(payload.GetString());
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                item = IsImageDynamicToolOutputType(channel)
                    ? new OpenAiDynamicToolOutputItem("inputImage", null, scalar)
                    : new OpenAiDynamicToolOutputItem("inputText", scalar, null);
                return true;
            }
        }

        item = null!;
        return false;
    }

    private static string? ReadStructuredScalarText(StructuredValue value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) || property is null || property.Kind == StructuredValueKind.Null)
        {
            return null;
        }

        return property.Kind switch
        {
            StructuredValueKind.String or StructuredValueKind.Number or StructuredValueKind.Boolean => property.GetString(),
            _ => JsonSerializer.Serialize(property.ToPlainObject()),
        };
    }

    private static bool IsImageDynamicToolOutputType(string? value)
        => string.Equals(value, "inputImage", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "input_image", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "image", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "image_url", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private sealed record OpenAiDynamicToolOutputItem(string Type, string? Text, string? ImageUrl)
    {
        public object ToPlainObject()
        {
            if (string.Equals(Type, "inputImage", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Type, "input_image", StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = Type,
                    ["image_url"] = ImageUrl ?? string.Empty,
                };
            }

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = Type,
                ["text"] = Text ?? string.Empty,
            };
        }
    }
}
