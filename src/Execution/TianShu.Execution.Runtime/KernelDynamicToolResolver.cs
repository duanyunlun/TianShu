using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TianShu.Execution.Runtime;

internal static partial class KernelDynamicToolResolver
{
    private static readonly Regex IdentifierCleanupRegex = CreateIdentifierCleanupRegex();

    public static IReadOnlyList<KernelDynamicToolDescriptor>? Parse(JsonElement? dynamicTools)
    {
        if (dynamicTools is not { ValueKind: JsonValueKind.Array } array)
        {
            return null;
        }

        return EnumerateDescriptors(array.EnumerateArray()).ToArray();
    }

    public static IReadOnlyList<KernelDynamicToolDescriptor>? Clone(IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools)
    {
        if (dynamicTools is null)
        {
            return null;
        }

        if (dynamicTools.Count == 0)
        {
            return Array.Empty<KernelDynamicToolDescriptor>();
        }

        return dynamicTools.Select(static descriptor => descriptor.DeepClone()).ToArray();
    }

    public static bool HasAnyTools(IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools)
        => dynamicTools is { Count: > 0 };

    public static IReadOnlyList<string> GetConnectorNames(IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools)
    {
        if (dynamicTools is null || dynamicTools.Count == 0)
        {
            return Array.Empty<string>();
        }

        return dynamicTools
            .Select(static tool => Normalize(tool.ConnectorName))
            .Where(static tool => !string.IsNullOrWhiteSpace(tool))
            .Select(static tool => tool!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static tool => tool, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string? ResolveFullToolName(
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools,
        string? name,
        string? toolNamespace)
    {
        var normalizedName = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        var normalizedNamespace = Normalize(toolNamespace);
        if (string.IsNullOrWhiteSpace(normalizedNamespace) || dynamicTools is null || dynamicTools.Count == 0)
        {
            return normalizedName;
        }

        foreach (var descriptor in dynamicTools)
        {
            if (!string.Equals(descriptor.Namespace, normalizedNamespace, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(descriptor.ShortName, normalizedName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(descriptor.FullName, normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                return descriptor.FullName;
            }
        }

        return normalizedName;
    }

    public static string? DeriveNamespace(JsonElement tool)
    {
        var explicitNamespace = ReadString(tool, "namespace")
            ?? ReadString(tool, "tool_namespace");
        if (!string.IsNullOrWhiteSpace(explicitNamespace))
        {
            return explicitNamespace;
        }

        var fullName = ReadString(tool, "name");
        if (TryDeriveNamespaceFromFullName(fullName, out var namespaceFromFullName))
        {
            return namespaceFromFullName;
        }

        var connectorName = ReadString(tool, "connectorName")
            ?? ReadString(tool, "connector_name");
        if (!string.IsNullOrWhiteSpace(connectorName))
        {
            var sanitized = SanitizeIdentifier(connectorName!);
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                return $"mcp__dynamic__{sanitized}";
            }
        }

        var server = ReadString(tool, "server")
            ?? ReadString(tool, "server_name");
        var normalizedServer = Normalize(server);
        if (!string.IsNullOrWhiteSpace(normalizedServer)
            && !string.Equals(normalizedServer, "dynamic", StringComparison.OrdinalIgnoreCase))
        {
            return SanitizeIdentifier(normalizedServer!);
        }

        return null;
    }

    public static string? DeriveShortName(JsonElement tool)
    {
        var explicitShortName = ReadString(tool, "tool_name")
            ?? ReadString(tool, "shortName")
            ?? ReadString(tool, "short_name");
        if (!string.IsNullOrWhiteSpace(explicitShortName))
        {
            return explicitShortName;
        }

        var fullName = ReadString(tool, "name");
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return null;
        }

        var toolNamespace = DeriveNamespace(tool);
        if (string.IsNullOrWhiteSpace(toolNamespace))
        {
            return fullName;
        }

        var prefix = toolNamespace + "__";
        if (fullName!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return fullName[prefix.Length..];
        }

        return fullName;
    }

    public static IReadOnlyList<KernelDynamicToolDescriptor> Describe(IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools)
    {
        if (dynamicTools is null || dynamicTools.Count == 0)
        {
            return Array.Empty<KernelDynamicToolDescriptor>();
        }

        return dynamicTools;
    }

    public static bool TryResolveDescriptor(
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools,
        string? toolName,
        out KernelDynamicToolDescriptor descriptor)
    {
        descriptor = null!;
        var normalizedToolName = Normalize(toolName);
        if (string.IsNullOrWhiteSpace(normalizedToolName)
            || dynamicTools is null
            || dynamicTools.Count == 0)
        {
            return false;
        }

        foreach (var candidate in dynamicTools)
        {
            if (!string.Equals(candidate.FullName, normalizedToolName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(candidate.ShortName, normalizedToolName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            descriptor = candidate;
            return true;
        }

        return false;
    }

    private static IEnumerable<KernelDynamicToolDescriptor> EnumerateDescriptors(JsonElement.ArrayEnumerator array)
    {
        foreach (var tool in array)
        {
            var fullName = ReadString(tool, "name");
            if (string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            var shortName = DeriveShortName(tool) ?? fullName;
            var toolNamespace = DeriveNamespace(tool);
            var description = ReadString(tool, "description");
            var title = ReadString(tool, "title");
            var server = ReadString(tool, "server") ?? ReadString(tool, "server_name");
            var connectorName = ReadString(tool, "connectorName") ?? ReadString(tool, "connector_name");
            var connectorDescription = ReadString(tool, "connectorDescription") ?? ReadString(tool, "connector_description");
            var meta = TryReadJsonProperty(tool, "_meta", out var rawMeta)
                ? rawMeta.Clone()
                : TryReadJsonProperty(tool, "meta", out rawMeta)
                    ? rawMeta.Clone()
                    : (JsonElement?)null;
            var connectorId = meta is { ValueKind: JsonValueKind.Object }
                ? ReadString(meta.Value, "connector_id") ?? ReadString(meta.Value, "connectorId")
                : null;
            var annotations = TryReadJsonProperty(tool, "annotations", out var rawAnnotations)
                ? rawAnnotations.Clone()
                : (JsonElement?)null;
            var inputSchema = TryReadJsonProperty(tool, "inputSchema", out var schema)
                ? schema.Clone()
                : (JsonElement?)null;
            var outputSchema = TryReadJsonProperty(tool, "outputSchema", out var rawOutputSchema)
                ? rawOutputSchema.Clone()
                : TryReadJsonProperty(tool, "output_schema", out rawOutputSchema)
                    ? rawOutputSchema.Clone()
                    : (JsonElement?)null;

            yield return new KernelDynamicToolDescriptor(
                FullName: fullName!,
                ShortName: shortName,
                Namespace: toolNamespace,
                Description: description,
                Title: title,
                Server: server,
                ConnectorName: connectorName,
                ConnectorDescription: connectorDescription,
                ConnectorId: connectorId,
                InputSchema: inputSchema,
                OutputSchema: outputSchema,
                Meta: meta,
                Annotations: annotations);
        }
    }

    private static string SanitizeIdentifier(string value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(IdentifierCleanupRegex.Replace(normalized!, "_"));
        for (var index = 0; index < builder.Length; index++)
        {
            builder[index] = char.ToLowerInvariant(builder[index]);
        }

        var result = builder.ToString().Trim('_');
        while (result.Contains("__", StringComparison.Ordinal))
        {
            result = result.Replace("__", "_", StringComparison.Ordinal);
        }

        return result;
    }

    private static bool TryDeriveNamespaceFromFullName(string? fullName, out string? toolNamespace)
    {
        toolNamespace = null;
        var normalizedFullName = Normalize(fullName);
        if (string.IsNullOrWhiteSpace(normalizedFullName))
        {
            return false;
        }

        var lastSeparator = normalizedFullName!.LastIndexOf("__", StringComparison.Ordinal);
        if (lastSeparator <= 0)
        {
            return false;
        }

        toolNamespace = normalizedFullName[..lastSeparator];
        return !string.IsNullOrWhiteSpace(toolNamespace);
    }

    private static bool TryReadJsonProperty(JsonElement json, string propertyName, out JsonElement value)
    {
        value = default;
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(propertyName, out value);
    }

    private static string? ReadString(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => Normalize(value.GetString()),
            JsonValueKind.Number => Normalize(value.GetRawText()),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null,
        };
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    [GeneratedRegex("[^A-Za-z0-9_]+", RegexOptions.Compiled)]
    private static partial Regex CreateIdentifierCleanupRegex();
}

internal sealed record KernelDynamicToolDescriptor(
    string FullName,
    string ShortName,
    string? Namespace,
    string? Description,
    string? Title,
    string? Server,
    string? ConnectorName,
    string? ConnectorDescription,
    string? ConnectorId,
    JsonElement? InputSchema,
    JsonElement? OutputSchema,
    JsonElement? Meta,
    JsonElement? Annotations)
{
    public string? ApprovalServerName
        => !string.IsNullOrWhiteSpace(ConnectorId)
            ? TryDeriveServerNameFromNamespace(Namespace) ?? TryDeriveServerNameFromNamespace(FullName)
            : string.Equals(Server, "dynamic", StringComparison.OrdinalIgnoreCase)
                ? null
                : Server;

    public KernelDynamicToolDescriptor DeepClone()
        => this with
        {
            InputSchema = InputSchema?.Clone(),
            OutputSchema = OutputSchema?.Clone(),
            Meta = Meta?.Clone(),
            Annotations = Annotations?.Clone(),
        };

    private static string? TryDeriveServerNameFromNamespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var segments = value.Split(new[] { "__" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length >= 2
            && string.Equals(segments[0], "mcp", StringComparison.OrdinalIgnoreCase)
            ? segments[1]
            : null;
    }
}
