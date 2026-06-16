using System.Collections.Concurrent;
using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelToolRuntimeApprovalHelpers
{
    public static bool IsDynamicToolApprovalAcceptedForSession(
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> approvalSessionKeysByThread,
        string threadId,
        string? approvalKey)
    {
        var normalizedThreadId = KernelToolJsonHelpers.Normalize(threadId);
        var normalizedApprovalKey = KernelToolJsonHelpers.Normalize(approvalKey);
        if (string.IsNullOrWhiteSpace(normalizedThreadId)
            || string.IsNullOrWhiteSpace(normalizedApprovalKey))
        {
            return false;
        }

        return approvalSessionKeysByThread.TryGetValue(normalizedThreadId!, out var approvals)
               && approvals.ContainsKey(normalizedApprovalKey!);
    }

    public static void MarkDynamicToolApprovalAcceptedForSession(
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> approvalSessionKeysByThread,
        string threadId,
        string? approvalKey)
    {
        var normalizedThreadId = KernelToolJsonHelpers.Normalize(threadId);
        var normalizedApprovalKey = KernelToolJsonHelpers.Normalize(approvalKey);
        if (string.IsNullOrWhiteSpace(normalizedThreadId)
            || string.IsNullOrWhiteSpace(normalizedApprovalKey))
        {
            return;
        }

        var approvals = approvalSessionKeysByThread.GetOrAdd(
            normalizedThreadId!,
            static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        approvals[normalizedApprovalKey!] = 0;
    }

    public static bool IsDynamicToolApprovalRememberedPersistently(
        Dictionary<string, object?> config,
        KernelDynamicToolDescriptor descriptor)
    {
        if (!TryGetDynamicToolApprovalOverrideKey(descriptor, out var connectorId, out var toolName, out _))
        {
            return false;
        }

        return TryReadNestedValueExact(config, ["apps", connectorId, "tools", toolName, "approval_mode"], out var rawApprovalMode)
               && TryReadString(rawApprovalMode, out var approvalMode)
               && string.Equals(KernelToolJsonHelpers.Normalize(approvalMode), "approve", StringComparison.OrdinalIgnoreCase);
    }

    public static bool DynamicToolRequiresApproval(KernelDynamicToolDescriptor descriptor)
    {
        if (descriptor.Annotations is not { ValueKind: JsonValueKind.Object } annotations)
        {
            return false;
        }

        var destructiveHint = KernelToolJsonHelpers.ReadBool(annotations, "destructive_hint") ?? KernelToolJsonHelpers.ReadBool(annotations, "destructiveHint");
        if (destructiveHint == true)
        {
            return true;
        }

        var openWorldHint = KernelToolJsonHelpers.ReadBool(annotations, "open_world_hint") ?? KernelToolJsonHelpers.ReadBool(annotations, "openWorldHint");
        var readOnlyHint = KernelToolJsonHelpers.ReadBool(annotations, "read_only_hint") ?? KernelToolJsonHelpers.ReadBool(annotations, "readOnlyHint");
        return openWorldHint == true && readOnlyHint == false;
    }

    public static string[] BuildDynamicToolApprovalAvailableDecisions(KernelDynamicToolDescriptor descriptor)
    {
        var decisions = new List<string> { "accept" };
        if (CanRememberDynamicToolApprovalForSession(descriptor))
        {
            decisions.Add("acceptForSession");
        }

        if (CanRememberDynamicToolApprovalPersistently(descriptor))
        {
            decisions.Add("acceptAndRemember");
        }

        decisions.Add("decline");
        decisions.Add("cancel");
        return decisions.ToArray();
    }

    public static bool CanRememberDynamicToolApprovalForSession(KernelDynamicToolDescriptor descriptor)
        => !string.IsNullOrWhiteSpace(BuildDynamicToolApprovalSessionKey(descriptor));

    public static bool CanRememberDynamicToolApprovalPersistently(KernelDynamicToolDescriptor descriptor)
        => TryGetDynamicToolApprovalOverrideKey(descriptor, out _, out _, out _);

    public static string? BuildDynamicToolApprovalSessionKey(KernelDynamicToolDescriptor descriptor)
    {
        var connectorId = KernelToolJsonHelpers.Normalize(descriptor.ConnectorId);
        var shortName = KernelToolJsonHelpers.Normalize(descriptor.ShortName);
        var connectorApprovalKey = OpenAiAppCatalogCompatibilityAdapter.BuildConnectorApprovalSessionKey(connectorId, shortName);
        if (!string.IsNullOrWhiteSpace(connectorApprovalKey))
        {
            return connectorApprovalKey;
        }

        var serverName = KernelToolJsonHelpers.Normalize(descriptor.ApprovalServerName);
        var fullName = KernelToolJsonHelpers.Normalize(descriptor.FullName);
        if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(fullName))
        {
            return null;
        }

        return $"mcp::{serverName}::{fullName}";
    }

    public static bool TryGetDynamicToolApprovalOverrideKey(
        KernelDynamicToolDescriptor descriptor,
        out string connectorId,
        out string toolName,
        out string overrideKey)
    {
        connectorId = KernelToolJsonHelpers.Normalize(descriptor.ConnectorId) ?? string.Empty;
        toolName = KernelToolJsonHelpers.Normalize(descriptor.ShortName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectorId) || string.IsNullOrWhiteSpace(toolName))
        {
            overrideKey = string.Empty;
            return false;
        }

        overrideKey = $"apps.{connectorId}.tools.{toolName}.approval_mode";
        return true;
    }

    public static string ResolveDynamicToolApprovalDecision(JsonElement approvalResponse)
    {
        var decision = NormalizeApprovalDecision(KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(approvalResponse, "decision")));
        if (!string.Equals(decision, "accept", StringComparison.OrdinalIgnoreCase)
            || !TryReadJsonProperty(approvalResponse, "_meta", out var meta))
        {
            return decision ?? string.Empty;
        }

        var persist = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(meta, "persist"));
        return persist?.ToLowerInvariant() switch
        {
            "session" => "acceptForSession",
            "always" => "acceptAndRemember",
            _ => decision ?? string.Empty,
        };
    }

    public static Dictionary<string, object?> BuildDynamicToolApprovalMetadata(
        KernelDynamicToolDescriptor descriptor,
        JsonElement arguments)
    {
        Dictionary<string, object?> metadata;
        if (descriptor.Meta is { ValueKind: JsonValueKind.Object } meta)
        {
            metadata = JsonSerializer.Deserialize<Dictionary<string, object?>>(meta.GetRawText())
                       ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        }
        else
        {
            metadata = new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(descriptor.ConnectorId))
        {
            metadata["connector_id"] = descriptor.ConnectorId;
        }

        if (!string.IsNullOrWhiteSpace(descriptor.ConnectorName))
        {
            metadata["connector_name"] = descriptor.ConnectorName;
        }

        if (!string.IsNullOrWhiteSpace(descriptor.ConnectorDescription))
        {
            metadata["connector_description"] = descriptor.ConnectorDescription;
        }

        if (!string.IsNullOrWhiteSpace(descriptor.ApprovalServerName))
        {
            metadata["server_name"] = descriptor.ApprovalServerName;
        }

        if (!string.IsNullOrWhiteSpace(descriptor.ConnectorId)
            || !string.IsNullOrWhiteSpace(descriptor.ConnectorName)
            || !string.IsNullOrWhiteSpace(descriptor.ConnectorDescription))
        {
            metadata["source"] = "connector";
        }

        metadata["tool_name"] = descriptor.ShortName;
        metadata["tool_full_name"] = descriptor.FullName;

        if (!string.IsNullOrWhiteSpace(descriptor.Title))
        {
            metadata["tool_title"] = descriptor.Title;
        }

        if (!string.IsNullOrWhiteSpace(descriptor.Description))
        {
            metadata["tool_description"] = descriptor.Description;
        }

        if (descriptor.Annotations is { } annotations)
        {
            metadata["annotations"] = JsonSerializer.Deserialize<object?>(annotations.GetRawText());
        }

        metadata["tool_params"] = JsonSerializer.Deserialize<object?>(arguments.GetRawText());

        return metadata;
    }

    public static bool IsFileChangeApprovalTool(string toolName)
    {
        return string.Equals(toolName, "write", StringComparison.Ordinal)
            || string.Equals(toolName, "apply_patch", StringComparison.Ordinal);
    }

    public static IReadOnlyList<string>? TryResolveFileChangePaths(string toolName, JsonElement arguments, string cwd)
    {
        if (string.Equals(toolName, "write", StringComparison.Ordinal))
        {
            var path = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "path"));
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var fullPath = Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(cwd, path));
            return [KernelFileChangeApprovalHelpers.NormalizeApprovalKey(fullPath)];
        }

        if (string.Equals(toolName, "apply_patch", StringComparison.Ordinal))
        {
            var patch = KernelToolJsonHelpers.ReadString(arguments, "input");
            if (string.IsNullOrWhiteSpace(patch))
            {
                return null;
            }

            try
            {
                return KernelApplyPatch.CollectAffectedFullPaths(patch!, cwd);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public static bool AreFileChangesApprovedForSession(
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> fileChangeApprovalSessionPathsByThread,
        string threadId,
        IReadOnlyList<string>? paths)
    {
        var normalizedThreadId = KernelToolJsonHelpers.Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId) || paths is null || paths.Count == 0)
        {
            return false;
        }

        if (!fileChangeApprovalSessionPathsByThread.TryGetValue(normalizedThreadId!, out var approvedPaths))
        {
            return false;
        }

        foreach (var path in paths)
        {
            if (!approvedPaths.ContainsKey(KernelFileChangeApprovalHelpers.NormalizeApprovalKey(path)))
            {
                return false;
            }
        }

        return true;
    }

    public static void MarkFileChangesApprovedForSession(
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> fileChangeApprovalSessionPathsByThread,
        string threadId,
        IReadOnlyList<string>? paths)
    {
        var normalizedThreadId = KernelToolJsonHelpers.Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId) || paths is null || paths.Count == 0)
        {
            return;
        }

        var approvedPaths = fileChangeApprovalSessionPathsByThread.GetOrAdd(
            normalizedThreadId!,
            static _ => new ConcurrentDictionary<string, byte>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));
        foreach (var path in paths)
        {
            approvedPaths[KernelFileChangeApprovalHelpers.NormalizeApprovalKey(path)] = 0;
        }
    }

    public static KernelPermissionGrantProfile? GetGrantedPermissions(
        ConcurrentDictionary<string, KernelPermissionGrantProfile> grantedPermissionSessionByThread,
        ConcurrentDictionary<string, KernelPermissionGrantProfile> grantedPermissionTurnByTurn,
        string threadId,
        string turnId)
    {
        grantedPermissionSessionByThread.TryGetValue(threadId, out var sessionPermissions);
        grantedPermissionTurnByTurn.TryGetValue(turnId, out var turnPermissions);
        var merged = KernelPermissionGrantProfile.Merge(sessionPermissions, turnPermissions);
        return merged.IsEmpty ? null : merged;
    }

    public static bool AreGrantedPermissionsApproved(KernelPermissionGrantProfile? grantedPermissions, IReadOnlyList<string>? paths)
        => grantedPermissions is not null && grantedPermissions.CoversAllWritePaths(paths);

    public static bool ResolveRequestPermissionsEnabled(Dictionary<string, object?> config, KernelApprovalPolicy? approvalPolicy)
    {
        if (KernelApprovalPolicyHelpers.IsNever(approvalPolicy))
        {
            return false;
        }

        if (KernelApprovalPolicyHelpers.TryGetGranularFlag(
                approvalPolicy,
                "request_permissions",
                "requestPermissions",
                out var requestPermissionsEnabled))
        {
            return requestPermissionsEnabled;
        }

        var explicitFlag = ReadBooleanExact(
            config,
            ["approval_policy", "granular", "request_permissions"],
            ["granular", "request_permissions"]);
        if (explicitFlag.HasValue)
        {
            return explicitFlag.Value;
        }

        var hasGranularApprovalConfig =
            (TryReadNestedValueExact(config, ["approval_policy", "granular"], out var granularApprovalPolicy)
                && TryAsDictionary(granularApprovalPolicy, out _))
            || TryReadObjectExact(config, "granular", out _);

        return !hasGranularApprovalConfig;
    }

    public static bool IsBuiltInToolExecutionEnabled(
        string toolName,
        KernelResponsesNativeToolOptions nativeToolOptions)
    {
        if (nativeToolOptions.ToolProfileOptions?.TryGetDisabledReason(toolName, toolName, out _) == true)
        {
            return false;
        }

        return toolName switch
        {
            "shell" => nativeToolOptions.ShellToolType == KernelShellToolType.Default,
            "local_shell" => nativeToolOptions.ShellToolType == KernelShellToolType.Local,
            "container.exec" or "shell_command"
                => nativeToolOptions.ShellToolType != KernelShellToolType.Disabled,
            "exec_command" or "write_stdin"
                => nativeToolOptions.ShellToolType == KernelShellToolType.UnifiedExec,
            "request_permissions" => nativeToolOptions.RequestPermissionsToolEnabled,
            _ => true,
        };
    }

    public static bool TryResolveFileChangeApprovalDecision(string? decision, out bool approvedForSession)
    {
        approvedForSession = false;
        var normalized = NormalizeApprovalDecision(decision);
        if (string.Equals(normalized, "acceptForSession", StringComparison.OrdinalIgnoreCase))
        {
            approvedForSession = true;
            return true;
        }

        return string.Equals(normalized, "accept", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadJsonProperty(JsonElement json, string propertyName, out JsonElement value)
    {
        value = default;
        return json.ValueKind == JsonValueKind.Object && json.TryGetProperty(propertyName, out value);
    }

    private static bool? ReadBooleanExact(Dictionary<string, object?> config, params string[][] propertyPaths)
    {
        foreach (var propertyPath in propertyPaths)
        {
            if (TryReadNestedValueExact(config, propertyPath, out var rawValue)
                && TryReadBoolean(rawValue, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryReadObjectExact(
        Dictionary<string, object?> config,
        string propertyName,
        out Dictionary<string, object?> value)
    {
        if (config.TryGetValue(propertyName, out var rawValue)
            && TryAsDictionary(rawValue, out value))
        {
            return true;
        }

        value = null!;
        return false;
    }

    private static bool TryReadNestedValueExact(
        Dictionary<string, object?> config,
        IReadOnlyList<string> propertyPath,
        out object? value)
    {
        var current = config;
        for (var index = 0; index < propertyPath.Count; index++)
        {
            if (!current.TryGetValue(propertyPath[index], out value))
            {
                return false;
            }

            if (index == propertyPath.Count - 1)
            {
                return true;
            }

            if (!TryAsDictionary(value, out current))
            {
                value = null;
                return false;
            }
        }

        value = null;
        return false;
    }

    private static bool TryAsDictionary(object? value, out Dictionary<string, object?> dictionary)
    {
        switch (value)
        {
            case Dictionary<string, object?> concrete:
                dictionary = concrete;
                return true;
            case IReadOnlyDictionary<string, object?> readOnly:
                dictionary = readOnly.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case IDictionary<string, object?> mutable:
                dictionary = mutable.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                dictionary = pairs.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Object:
                dictionary = ConvertJsonObject(element);
                return true;
            default:
                dictionary = null!;
                return false;
        }
    }

    private static bool TryReadString(object? value, out string text)
    {
        switch (value)
        {
            case string stringValue:
                text = stringValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                text = element.GetString() ?? string.Empty;
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    private static bool TryReadBoolean(object? value, out bool booleanValue)
    {
        switch (value)
        {
            case bool native:
                booleanValue = native;
                return true;
            case JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                booleanValue = element.GetBoolean();
                return true;
            case string text when bool.TryParse(text, out var parsed):
                booleanValue = parsed;
                return true;
            default:
                booleanValue = default;
                return false;
        }
    }

    private static Dictionary<string, object?> ConvertJsonObject(JsonElement element)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = ConvertJsonValue(property.Value);
        }

        return dictionary;
    }

    private static object? ConvertJsonValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var intValue)
                ? intValue
                : element.TryGetDouble(out var doubleValue)
                    ? doubleValue
                    : element.GetRawText(),
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };

    private static string? NormalizeApprovalDecision(string? decision)
    {
        var normalized = KernelToolJsonHelpers.Normalize(decision);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.ToLowerInvariant() switch
        {
            "accept" => "accept",
            "approved" => "accept",
            "approve" => "accept",
            "acceptforsession" => "acceptForSession",
            "accept_for_session" => "acceptForSession",
            "acceptandremember" => "acceptAndRemember",
            "accept_and_remember" => "acceptAndRemember",
            "acceptwithexecpolicyamendment" => "acceptWithExecpolicyAmendment",
            "accept_with_execpolicy_amendment" => "acceptWithExecpolicyAmendment",
            "applynetworkpolicyamendment" => "applyNetworkPolicyAmendment",
            "apply_network_policy_amendment" => "applyNetworkPolicyAmendment",
            "decline" => "decline",
            "denied" => "decline",
            "deny" => "decline",
            "reject" => "decline",
            "rejected" => "decline",
            "cancel" => "cancel",
            _ => normalized,
        };
    }
}
