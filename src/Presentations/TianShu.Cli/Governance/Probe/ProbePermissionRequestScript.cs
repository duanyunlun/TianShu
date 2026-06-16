using System.Text.Json;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli;

internal sealed class ProbePermissionRequestScript
{
    private readonly Dictionary<string, ControlPlanePermissionGrant> requestResponses;
    private readonly ControlPlanePermissionGrant? defaultResponse;

    private ProbePermissionRequestScript(
        string sourcePath,
        Dictionary<string, ControlPlanePermissionGrant> requestResponses,
        ControlPlanePermissionGrant? defaultResponse)
    {
        SourcePath = sourcePath;
        this.requestResponses = requestResponses;
        this.defaultResponse = defaultResponse;
    }

    public string SourcePath { get; }

    public static ProbePermissionRequestScript? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"权限响应 JSON 不存在：{fullPath}", fullPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("权限响应 JSON 的根节点必须是对象。支持直接响应对象，或使用 requests/defaultResponse 包装。");
        }

        var root = document.RootElement;
        var requestResponses = new Dictionary<string, ControlPlanePermissionGrant>(StringComparer.Ordinal);
        ControlPlanePermissionGrant? defaultResponse = null;

        root.TryGetProperty("requests", out var requestsElement);
        root.TryGetProperty("defaultResponse", out var defaultResponseElement);
        root.TryGetProperty("response", out var responseElement);
        var hasEnvelope = requestsElement.ValueKind != JsonValueKind.Undefined
            || defaultResponseElement.ValueKind != JsonValueKind.Undefined
            || responseElement.ValueKind != JsonValueKind.Undefined;

        if (requestsElement.ValueKind != JsonValueKind.Undefined)
        {
            if (requestsElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("permissions-json 中的 requests 必须是对象，其键为 callId，值为权限响应对象。");
            }

            foreach (var property in requestsElement.EnumerateObject())
            {
                requestResponses[property.Name] = ParseResponse(property.Value, $"requests.{property.Name}");
            }
        }

        if (defaultResponseElement.ValueKind != JsonValueKind.Undefined)
        {
            defaultResponse = ParseResponse(defaultResponseElement, "defaultResponse");
        }
        else if (responseElement.ValueKind != JsonValueKind.Undefined)
        {
            defaultResponse = ParseResponse(responseElement, "response");
        }
        else if (!hasEnvelope)
        {
            defaultResponse = ParseResponse(root, "root");
        }

        if (requestResponses.Count == 0 && defaultResponse is null)
        {
            throw new FormatException("权限响应 JSON 中未找到可用响应。请提供权限响应对象，或使用 requests/defaultResponse 包装。");
        }

        return new ProbePermissionRequestScript(fullPath, requestResponses, defaultResponse);
    }

    public bool TryResolveResponse(string callId, out ControlPlanePermissionGrant response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);

        if (requestResponses.TryGetValue(callId, out var requestSpecificResponse))
        {
            response = CliGovernanceEnvelopeFactory.Normalize(CloneResponse(callId, requestSpecificResponse));
            return true;
        }

        if (defaultResponse is not null)
        {
            response = CliGovernanceEnvelopeFactory.Normalize(CloneResponse(callId, defaultResponse));
            return true;
        }

        response = CliGovernanceEnvelopeFactory.Normalize(new ControlPlanePermissionGrant
        {
            CallId = new CallId(callId),
            Permissions = new Dictionary<string, StructuredValue>(StringComparer.Ordinal),
            Scope = ControlPlanePermissionScope.Turn,
        });
        return false;
    }

    public static ControlPlanePermissionGrant ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ParseResponse(document.RootElement, "inline");
    }

    private static ControlPlanePermissionGrant ParseResponse(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{context} 必须是对象。");
        }

        var scope = ParseScope(element, context);
        var permissionsElement = element.TryGetProperty("permissions", out var nestedPermissions)
            ? nestedPermissions
            : element;
        var permissions = ConvertPermissionsObject(permissionsElement, context);
        return new ControlPlanePermissionGrant
        {
            Permissions = permissions,
            Scope = scope,
        };
    }

    private static ControlPlanePermissionScope ParseScope(JsonElement element, string context)
    {
        if (!element.TryGetProperty("scope", out var scopeElement)
            || scopeElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return ControlPlanePermissionScope.Turn;
        }

        if (scopeElement.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"{context}.scope 必须是字符串，取值为 turn 或 session。");
        }

        return scopeElement.GetString() switch
        {
            "session" => ControlPlanePermissionScope.Session,
            "turn" => ControlPlanePermissionScope.Turn,
            var value => throw new FormatException($"{context}.scope 只支持 turn 或 session，收到：{value ?? "<null>"}"),
        };
    }

    private static Dictionary<string, StructuredValue> ConvertPermissionsObject(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{context}.permissions 必须是对象。");
        }

        var dictionary = new Dictionary<string, StructuredValue>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = ConvertStructuredValue(property.Value);
        }

        return dictionary;
    }

    private static ControlPlanePermissionGrant CloneResponse(string callId, ControlPlanePermissionGrant response)
    {
        var permissions = new Dictionary<string, StructuredValue>(StringComparer.Ordinal);
        foreach (var pair in response.Permissions)
        {
            permissions[pair.Key] = pair.Value;
        }

        return response with
        {
            CallId = new CallId(callId),
            Permissions = permissions,
        };
    }

    private static StructuredValue ConvertStructuredValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => StructuredValue.FromObject(
                element.EnumerateObject().ToDictionary(
                    static property => property.Name,
                    static property => ConvertStructuredValue(property.Value),
                    StringComparer.Ordinal)),
            JsonValueKind.Array => StructuredValue.FromArray(
                element.EnumerateArray().Select(ConvertStructuredValue).ToArray()),
            JsonValueKind.String => StructuredValue.FromString(element.GetString() ?? string.Empty),
            JsonValueKind.Number => StructuredValue.FromNumber(element.GetRawText()),
            JsonValueKind.True => StructuredValue.FromBoolean(true),
            JsonValueKind.False => StructuredValue.FromBoolean(false),
            JsonValueKind.Null or JsonValueKind.Undefined => StructuredValue.Null,
            _ => StructuredValue.FromString(element.GetRawText()),
        };
}
