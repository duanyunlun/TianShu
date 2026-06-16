using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelToolSandboxResolver
{
    public static bool TryResolve(
        string? sandboxPermissionsValue,
        KernelAdditionalPermissionArguments? additionalPermissions,
        KernelToolCallContext context,
        string cwd,
        out JsonElement sandboxPolicy,
        out string sandboxMode,
        out string? errorMessage)
    {
        sandboxPolicy = context.SandboxPolicy?.Clone() ?? BuildDefaultWorkspaceWritePolicy();
        sandboxMode = ResolveSandboxMode(context.SandboxPolicy, context.SandboxMode);
        errorMessage = null;

        var sandboxPermissions = Normalize(sandboxPermissionsValue) ?? "use_default";
        var approvalPolicy = context.ApprovalPolicy ?? KernelApprovalPolicy.OnRequest;
        var approvalPolicyText = KernelApprovalPolicyHelpers.NormalizeScalar(approvalPolicy) ?? "on-request";
        var hasAdditionalPermissions = additionalPermissions is not null;

        switch (sandboxPermissions)
        {
            case "":
            case "use_default":
            case "use-default":
                if (hasAdditionalPermissions)
                {
                    errorMessage = "`additional_permissions` requires `sandbox_permissions` set to `with_additional_permissions`";
                    return false;
                }

                return true;

            case "require_escalated":
            case "require-escalated":
                if (hasAdditionalPermissions)
                {
                    errorMessage = "`additional_permissions` requires `sandbox_permissions` set to `with_additional_permissions`";
                    return false;
                }

                if (IsDangerFullAccess(sandboxMode))
                {
                    return true;
                }

                if (!KernelApprovalPolicyHelpers.IsOnRequest(approvalPolicy))
                {
                    errorMessage = $"approval policy is {approvalPolicyText}; reject command — you cannot ask for escalated permissions unless the approval policy is on-request";
                    return false;
                }

                sandboxPolicy = BuildDangerFullAccessPolicy();
                sandboxMode = "danger-full-access";
                return true;

            case "with_additional_permissions":
            case "with-additional-permissions":
                if (!hasAdditionalPermissions)
                {
                    errorMessage = "missing `additional_permissions`; provide at least one of `network` or `file_system` when using `with_additional_permissions`";
                    return false;
                }

                if (!KernelPermissionGrantProfile.TryCreateFromAdditionalPermissions(additionalPermissions, cwd, out var parsedAdditionalPermissions, out errorMessage))
                {
                    return false;
                }

                if (parsedAdditionalPermissions.IsEmpty)
                {
                    errorMessage = "`additional_permissions` must include at least one requested permission in `network` or `file_system`";
                    return false;
                }

                if (IsDangerFullAccess(sandboxMode))
                {
                    return true;
                }

                var isPreapproved = IsPreapprovedAdditionalPermissionsRequest(context, parsedAdditionalPermissions);
                if (isPreapproved)
                {
                    if (!context.ExecPermissionApprovalsEnabled && !context.RequestPermissionsToolEnabled)
                    {
                        errorMessage = "additional sandbox permissions are disabled by config";
                        return false;
                    }
                }
                else
                {
                    if (!context.ExecPermissionApprovalsEnabled)
                    {
                        errorMessage = "additional sandbox permissions are disabled by config";
                        return false;
                    }

                    if (!KernelApprovalPolicyHelpers.IsOnRequest(approvalPolicy))
                    {
                        errorMessage = $"approval policy is {approvalPolicyText}; reject command — you cannot request additional permissions unless the approval policy is on-request";
                        return false;
                    }
                }

                sandboxPolicy = MergeSandboxPolicy(sandboxPolicy, sandboxMode, parsedAdditionalPermissions, out sandboxMode);
                return true;

            default:
                errorMessage = $"unsupported sandbox_permissions value: {sandboxPermissions}";
                return false;
        }
    }

    public static bool TryResolve(
        JsonElement arguments,
        KernelToolCallContext context,
        string cwd,
        out JsonElement sandboxPolicy,
        out string sandboxMode,
        out string? errorMessage)
    {
        sandboxPolicy = context.SandboxPolicy?.Clone() ?? BuildDefaultWorkspaceWritePolicy();
        sandboxMode = ResolveSandboxMode(context.SandboxPolicy, context.SandboxMode);
        errorMessage = null;

        var sandboxPermissions = Normalize(KernelToolJsonHelpers.ReadString(arguments, "sandbox_permissions")) ?? "use_default";
        var approvalPolicy = context.ApprovalPolicy ?? KernelApprovalPolicy.OnRequest;
        var approvalPolicyText = KernelApprovalPolicyHelpers.NormalizeScalar(approvalPolicy) ?? "on-request";
        var additionalPermissionsElement = default(JsonElement);
        var hasAdditionalPermissions = arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty("additional_permissions", out additionalPermissionsElement)
            && additionalPermissionsElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

        switch (sandboxPermissions)
        {
            case "":
            case "use_default":
            case "use-default":
                if (hasAdditionalPermissions)
                {
                    errorMessage = "`additional_permissions` requires `sandbox_permissions` set to `with_additional_permissions`";
                    return false;
                }

                return true;

            case "require_escalated":
            case "require-escalated":
                if (hasAdditionalPermissions)
                {
                    errorMessage = "`additional_permissions` requires `sandbox_permissions` set to `with_additional_permissions`";
                    return false;
                }

                if (IsDangerFullAccess(sandboxMode))
                {
                    return true;
                }

                if (!KernelApprovalPolicyHelpers.IsOnRequest(approvalPolicy))
                {
                    errorMessage = $"approval policy is {approvalPolicyText}; reject command — you cannot ask for escalated permissions unless the approval policy is on-request";
                    return false;
                }

                sandboxPolicy = BuildDangerFullAccessPolicy();
                sandboxMode = "danger-full-access";
                return true;

            case "with_additional_permissions":
            case "with-additional-permissions":
                if (!hasAdditionalPermissions)
                {
                    errorMessage = "missing `additional_permissions`; provide at least one of `network` or `file_system` when using `with_additional_permissions`";
                    return false;
                }

                if (!KernelPermissionGrantProfile.TryParseAdditionalPermissions(additionalPermissionsElement, cwd, out var additionalPermissions, out errorMessage))
                {
                    return false;
                }

                if (additionalPermissions.IsEmpty)
                {
                    errorMessage = "`additional_permissions` must include at least one requested permission in `network` or `file_system`";
                    return false;
                }

                if (IsDangerFullAccess(sandboxMode))
                {
                    return true;
                }

                var isPreapproved = IsPreapprovedAdditionalPermissionsRequest(context, additionalPermissions);
                if (isPreapproved)
                {
                    if (!context.ExecPermissionApprovalsEnabled && !context.RequestPermissionsToolEnabled)
                    {
                        errorMessage = "additional sandbox permissions are disabled by config";
                        return false;
                    }
                }
                else
                {
                    if (!context.ExecPermissionApprovalsEnabled)
                    {
                        errorMessage = "additional sandbox permissions are disabled by config";
                        return false;
                    }

                    if (!KernelApprovalPolicyHelpers.IsOnRequest(approvalPolicy))
                    {
                        errorMessage = $"approval policy is {approvalPolicyText}; reject command — you cannot request additional permissions unless the approval policy is on-request";
                        return false;
                    }
                }

                sandboxPolicy = MergeSandboxPolicy(sandboxPolicy, sandboxMode, additionalPermissions, out sandboxMode);
                return true;

            default:
                errorMessage = $"unsupported sandbox_permissions value: {sandboxPermissions}";
                return false;
        }
    }

    public static JsonElement ApplyGrantedPermissions(
        JsonElement? basePolicy,
        string baseMode,
        KernelPermissionGrantProfile? additionalPermissions,
        out string sandboxMode)
    {
        if (additionalPermissions is null || additionalPermissions.IsEmpty)
        {
            var effectiveBasePolicy = basePolicy?.ValueKind == JsonValueKind.Object
                ? basePolicy.Value.Clone()
                : BuildDefaultWorkspaceWritePolicy();
            sandboxMode = ResolveSandboxMode(basePolicy, baseMode);
            return effectiveBasePolicy;
        }

        var effectiveBaseMode = ResolveSandboxMode(basePolicy, baseMode);
        var mergedPolicy = MergeSandboxPolicy(basePolicy ?? BuildDefaultWorkspaceWritePolicy(), effectiveBaseMode, additionalPermissions, out sandboxMode);
        return mergedPolicy;
    }

    private static JsonElement MergeSandboxPolicy(
        JsonElement basePolicy,
        string baseMode,
        KernelPermissionGrantProfile additionalPermissions,
        out string mergedSandboxMode)
    {
        mergedSandboxMode = baseMode;
        if (IsDangerFullAccess(baseMode) || IsExternalSandbox(baseMode))
        {
            return basePolicy.Clone();
        }

        var effectiveBasePolicy = basePolicy.ValueKind == JsonValueKind.Object
            ? basePolicy
            : BuildDefaultWorkspaceWritePolicy();
        var networkAccess = ReadBoolean(effectiveBasePolicy, "networkAccess") ?? false;
        var mergedNetworkAccess = networkAccess || additionalPermissions.NetworkEnabled;
        var mergedReadOnlyAccess = MergeReadOnlyAccess(
            ReadObject(effectiveBasePolicy, "readOnlyAccess") ?? BuildFullAccessReadOnlyAccess(),
            additionalPermissions.ReadRoots);

        if (IsReadOnly(baseMode) && additionalPermissions.WriteRoots.Length == 0)
        {
            var readOnlyPolicy = BuildReadOnlyPolicy(mergedReadOnlyAccess, mergedNetworkAccess);
            mergedSandboxMode = ResolveSandboxMode(readOnlyPolicy, baseMode);
            return readOnlyPolicy;
        }

        var writableRoots = MergePaths(
            ReadStringArray(effectiveBasePolicy, "writableRoots"),
            additionalPermissions.WriteRoots);
        var excludeTmpdirEnvVar = ReadBoolean(effectiveBasePolicy, "excludeTmpdirEnvVar") ?? false;
        var excludeSlashTmp = ReadBoolean(effectiveBasePolicy, "excludeSlashTmp") ?? false;

        var mergedPolicy = BuildWorkspaceWritePolicy(
            writableRoots,
            mergedReadOnlyAccess,
            mergedNetworkAccess,
            excludeTmpdirEnvVar,
            excludeSlashTmp);
        mergedSandboxMode = ResolveSandboxMode(mergedPolicy, baseMode);
        return mergedPolicy;
    }

    private static bool IsPreapprovedAdditionalPermissionsRequest(
        KernelToolCallContext context,
        KernelPermissionGrantProfile requestedPermissions)
        => context.GrantedPermissions?.Covers(requestedPermissions) == true;

    private static JsonElement MergeReadOnlyAccess(JsonElement readOnlyAccess, IReadOnlyList<string> extraReads)
    {
        if (extraReads.Count == 0)
        {
            return readOnlyAccess.Clone();
        }

        var accessType = Normalize(ReadString(readOnlyAccess, "type"));
        if (string.Equals(accessType, "fullaccess", StringComparison.OrdinalIgnoreCase)
            || string.Equals(accessType, "full_access", StringComparison.OrdinalIgnoreCase)
            || string.Equals(accessType, "full-access", StringComparison.OrdinalIgnoreCase))
        {
            return readOnlyAccess.Clone();
        }

        var includePlatformDefaults = ReadBoolean(readOnlyAccess, "includePlatformDefaults") ?? false;
        var readableRoots = MergePaths(ReadStringArray(readOnlyAccess, "readableRoots"), extraReads);
        return JsonSerializer.SerializeToElement(new
        {
            type = "restricted",
            includePlatformDefaults,
            readableRoots,
        });
    }

    private static string ResolveSandboxMode(JsonElement? sandboxPolicy, string? sandboxMode)
    {
        var explicitMode = Normalize(sandboxMode);
        if (!string.IsNullOrWhiteSpace(explicitMode))
        {
            return explicitMode!;
        }

        if (sandboxPolicy is { ValueKind: JsonValueKind.Object } policy)
        {
            var type = Normalize(ReadString(policy, "type"));
            if (!string.IsNullOrWhiteSpace(type))
            {
                return type!;
            }
        }

        return "workspaceWrite";
    }

    private static JsonElement BuildDefaultWorkspaceWritePolicy()
        => BuildWorkspaceWritePolicy(
            Array.Empty<string>(),
            BuildFullAccessReadOnlyAccess(),
            networkAccess: false,
            excludeTmpdirEnvVar: false,
            excludeSlashTmp: false);

    private static JsonElement BuildDangerFullAccessPolicy()
        => JsonSerializer.SerializeToElement(new
        {
            type = "danger-full-access",
        });

    private static JsonElement BuildReadOnlyPolicy(JsonElement readOnlyAccess, bool networkAccess)
        => JsonSerializer.SerializeToElement(new
        {
            type = "readOnly",
            readOnlyAccess,
            networkAccess,
        });

    private static JsonElement BuildWorkspaceWritePolicy(
        IReadOnlyList<string> writableRoots,
        JsonElement readOnlyAccess,
        bool networkAccess,
        bool excludeTmpdirEnvVar,
        bool excludeSlashTmp)
        => JsonSerializer.SerializeToElement(new
        {
            type = "workspaceWrite",
            writableRoots = writableRoots.ToArray(),
            readOnlyAccess,
            networkAccess,
            excludeTmpdirEnvVar,
            excludeSlashTmp,
        });

    private static JsonElement BuildFullAccessReadOnlyAccess()
        => JsonSerializer.SerializeToElement(new
        {
            type = "fullAccess",
        });

    private static string[] MergePaths(IReadOnlyList<string> existing, IReadOnlyList<string> additional)
        => DeduplicatePaths(existing.Concat(additional));

    private static string[] DeduplicatePaths(IEnumerable<string> paths)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(comparer)
            .ToArray();
    }

    private static bool IsDangerFullAccess(string sandboxMode)
        => sandboxMode.Contains("danger", StringComparison.OrdinalIgnoreCase);

    private static bool IsExternalSandbox(string sandboxMode)
        => sandboxMode.Equals("externalSandbox", StringComparison.OrdinalIgnoreCase)
           || sandboxMode.Equals("external-sandbox", StringComparison.OrdinalIgnoreCase)
           || sandboxMode.Equals("external_sandbox", StringComparison.OrdinalIgnoreCase);

    private static bool IsReadOnly(string sandboxMode)
        => sandboxMode.Contains("read", StringComparison.OrdinalIgnoreCase);

    private static string[] ReadStringArray(JsonElement json, string propertyName)
    {
        var array = ReadArray(json, propertyName);
        if (array is null)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in array.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = Normalize(item.GetString());
            if (!string.IsNullOrWhiteSpace(text))
            {
                values.Add(text!);
            }
        }

        return DeduplicatePaths(values);
    }

    private static JsonElement? ReadObject(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return value.Clone();
    }

    private static JsonElement? ReadArray(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return value.Clone();
    }

    private static string? ReadString(JsonElement json, string propertyName)
        => KernelToolJsonHelpers.ReadString(json, propertyName);

    private static bool? ReadBoolean(JsonElement json, string propertyName)
        => KernelToolJsonHelpers.ReadBool(json, propertyName);

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}




