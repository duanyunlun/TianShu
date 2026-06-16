using System.Text.Json;
using TianShu.Configuration;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// permission profile 与本地 sandbox 配置解析器。
/// Resolves permission profiles and local sandbox configuration.
/// </summary>
internal static class KernelPermissionProfileResolver
{
    public static KernelResolvedPermissionConfiguration ResolveConfiguredPermissionConfiguration(
        KernelConfigReadSnapshot snapshot,
        string? cwd,
        object? defaultApprovalPolicyValue,
        string tianShuHome,
        PolicyStrategyEffectiveDefaults policyStrategyDefaults)
    {
        var effectiveCwd = NormalizeConfigText(cwd) ?? Environment.CurrentDirectory;
        var projectRootMarkers = TianShuProjectRootResolver.ResolveProjectRootMarkers(snapshot.Config);
        var approvalPolicyValue = ReadConfiguredApprovalPolicyValue(snapshot.Config)
            ?? (TianShuProjectRootResolver.IsProjectExplicitlyUntrusted(effectiveCwd, snapshot.Config, projectRootMarkers)
                ? "untrusted"
                : null)
            ?? NormalizePolicyStrategyApprovalPolicy(policyStrategyDefaults.ApprovalPolicy)
            ?? defaultApprovalPolicyValue;
        var allowLoginShell = ReadBooleanExact(snapshot.Config, "allow_login_shell")
            ?? policyStrategyDefaults.AllowLoginShell
            ?? true;
        var shellEnvironmentPolicy = ReadConfiguredShellEnvironmentPolicy(snapshot.Config);

        var syntax = ResolvePermissionConfigSyntax(snapshot.OrderedLayers, snapshot.Config);
        var profilesAreActive = syntax == KernelPermissionConfigSyntax.Profiles
            || (!syntax.HasValue && HasPermissionProfiles(snapshot.Config));
        if (profilesAreActive)
        {
            var (profilePolicy, profileMode) = BuildPermissionsProfileSandbox(snapshot.Config, effectiveCwd, tianShuHome);
            return new KernelResolvedPermissionConfiguration(
                approvalPolicyValue,
                profilePolicy,
                profileMode,
                allowLoginShell,
                shellEnvironmentPolicy);
        }

        if (TryBuildLegacyConfiguredSandbox(snapshot.Config, effectiveCwd, out var legacyPolicy, out var legacyMode))
        {
            return new KernelResolvedPermissionConfiguration(
                approvalPolicyValue,
                legacyPolicy,
                legacyMode,
                allowLoginShell,
                shellEnvironmentPolicy);
        }

        var (fallbackPolicy, fallbackMode) = BuildPolicyStrategyFallbackSandbox(policyStrategyDefaults);
        return new KernelResolvedPermissionConfiguration(
            approvalPolicyValue,
            fallbackPolicy,
            fallbackMode,
            allowLoginShell,
            shellEnvironmentPolicy);
    }

    public static KernelPermissionConfigSyntax? ResolvePermissionConfigSyntax(
        IReadOnlyList<KernelConfigReadLayer> orderedLayers,
        Dictionary<string, object?> effectiveConfig)
    {
        KernelPermissionConfigSyntax? selection = null;
        foreach (var layer in orderedLayers)
        {
            if (ContainsLegacyPermissionSyntax(layer.Config))
            {
                selection = KernelPermissionConfigSyntax.Legacy;
            }

            if (ContainsProfilePermissionSyntax(layer.Config))
            {
                selection = KernelPermissionConfigSyntax.Profiles;
            }
        }

        if (selection.HasValue)
        {
            return selection.Value;
        }

        if (ContainsProfilePermissionSyntax(effectiveConfig))
        {
            return KernelPermissionConfigSyntax.Profiles;
        }

        return ContainsLegacyPermissionSyntax(effectiveConfig)
            ? KernelPermissionConfigSyntax.Legacy
            : null;
    }

    public static KernelConfiguredShellEnvironmentPolicySettings ReadConfiguredShellEnvironmentPolicy(
        Dictionary<string, object?> config)
    {
        if (!TryReadObjectExact(config, "shell_environment_policy", out var policyConfig))
        {
            return KernelConfiguredShellEnvironmentPolicySettings.Default;
        }

        var inherit = ParseShellEnvironmentPolicyInherit(ReadStringExact(policyConfig, "inherit"));
        var ignoreDefaultExcludes = ReadBooleanExact(policyConfig, "ignore_default_excludes") ?? true;
        var excludePatterns = ReadStringArrayExact(policyConfig, "exclude");
        var includeOnlyPatterns = ReadStringArrayExact(policyConfig, "include_only");
        var setVariables = ReadConfiguredStringDictionary(policyConfig, "set");
        var useProfile = ReadBooleanExact(policyConfig, "experimental_use_profile", "use_profile") ?? false;

        return new KernelConfiguredShellEnvironmentPolicySettings(
            inherit,
            ignoreDefaultExcludes,
            excludePatterns,
            setVariables,
            includeOnlyPatterns,
            useProfile);
    }

    public static object? ReadConfiguredApprovalPolicyValue(Dictionary<string, object?> config)
    {
        return TryReadConfiguredApprovalPolicyValueFromActiveProfile(config, out var approvalPolicyValue, "approval_policy")
               || TryReadConfiguredNestedApprovalPolicyValueFromActiveProfile(
                   config,
                   out approvalPolicyValue,
                   ["permissions", "approval_policy"])
               || TryReadConfiguredApprovalPolicyValue(config, out approvalPolicyValue, "approval_policy")
               || TryReadConfiguredNestedApprovalPolicyValue(
                   config,
                   out approvalPolicyValue,
                   ["permissions", "approval_policy"])
            ? approvalPolicyValue
            : null;
    }

    private static bool ContainsLegacyPermissionSyntax(Dictionary<string, object?> config)
    {
        return TryReadStringExact(config, out _, "sandbox_mode")
               || TryReadNestedStringExact(config, out _, ["permissions", "sandbox_mode"])
               || TryReadObjectExact(config, "sandbox", out _);
    }

    private static bool ContainsProfilePermissionSyntax(Dictionary<string, object?> config)
        => TryReadStringExact(config, out _, "default_permissions");

    private static bool HasPermissionProfiles(Dictionary<string, object?> config)
        => TryReadObjectExact(config, "permissions", out var permissionsRoot) && permissionsRoot.Count > 0;

    private static bool TryBuildLegacyConfiguredSandbox(
        Dictionary<string, object?> config,
        string cwd,
        out JsonElement sandboxPolicy,
        out string sandboxMode)
    {
        if (TryReadObjectExact(config, "sandbox", out var explicitSandbox))
        {
            sandboxPolicy = JsonSerializer.SerializeToElement(explicitSandbox);
            sandboxMode = NormalizeConfigText(
                    ReadStringExact(explicitSandbox, "type")
                    ?? ReadStringExact(explicitSandbox, "mode"))
                ?? "workspaceWrite";
            return true;
        }

        var configuredMode = ReadStringExact(config, "sandbox_mode")
            ?? ReadNestedStringExact(config, ["permissions", "sandbox_mode"]);
        if (string.IsNullOrWhiteSpace(configuredMode))
        {
            sandboxPolicy = default;
            sandboxMode = string.Empty;
            return false;
        }

        sandboxMode = NormalizeLegacySandboxMode(configuredMode!) ?? "workspaceWrite";
        sandboxPolicy = sandboxMode switch
        {
            "readOnly" => BuildReadOnlySandboxPolicy(BuildFullAccessPayload(), networkAccess: false),
            "danger-full-access" => JsonSerializer.SerializeToElement(new
            {
                type = "danger-full-access",
            }),
            _ => BuildWorkspaceWriteSandboxPolicy(
                ReadStringArrayExact(config, ["sandbox_workspace_write", "writable_roots"]),
                BuildFullAccessPayload(),
                ReadBooleanExact(config, ["sandbox_workspace_write", "network_access"]) ?? false,
                ReadBooleanExact(config, ["sandbox_workspace_write", "exclude_tmpdir_env_var"]) ?? false,
                ReadBooleanExact(config, ["sandbox_workspace_write", "exclude_slash_tmp"]) ?? false),
        };
        return true;
    }

    private static (JsonElement SandboxPolicy, string SandboxMode) BuildPolicyStrategyFallbackSandbox(PolicyStrategyEffectiveDefaults defaults)
    {
        var networkAccess = defaults.NetworkAccess ?? false;
        var sandboxMode = NormalizePolicyStrategySandboxMode(defaults.SandboxMode);
        if (string.Equals(sandboxMode, "readOnly", StringComparison.Ordinal))
        {
            return (BuildReadOnlySandboxPolicy(BuildFullAccessPayload(), networkAccess), "readOnly");
        }

        return (BuildWorkspaceWriteSandboxPolicy(
            writableRoots: Array.Empty<string>(),
            readOnlyAccess: BuildFullAccessPayload(),
            networkAccess,
            excludeTmpdirEnvVar: false,
            excludeSlashTmp: false), "workspaceWrite");
    }

    private static string? NormalizePolicyStrategyApprovalPolicy(string? approvalPolicy)
    {
        var normalized = NormalizeConfigText(approvalPolicy);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized!.ToLowerInvariant() switch
        {
            "never" => "never",
            "on-request" or "onrequest" or "on_request" => "on-request",
            "on-failure" or "onfailure" or "on_failure" => "on-failure",
            "untrusted" => "untrusted",
            _ => null,
        };
    }

    private static string? NormalizePolicyStrategySandboxMode(string? sandboxMode)
    {
        var normalized = NormalizeConfigText(sandboxMode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized!.ToLowerInvariant() switch
        {
            "readonly" or "read-only" or "read_only" => "readOnly",
            "workspacewrite" or "workspace-write" or "workspace_write" => "workspaceWrite",
            _ => null,
        };
    }

    private static (JsonElement SandboxPolicy, string SandboxMode) BuildPermissionsProfileSandbox(
        Dictionary<string, object?> config,
        string cwd,
        string tianShuHome)
    {
        var hasPermissionProfiles = HasPermissionProfiles(config);
        var profileName = ReadStringExact(config, "default_permissions");
        if (!hasPermissionProfiles)
        {
            throw new InvalidOperationException("default_permissions requires a `[permissions]` table");
        }

        if (profileName is null)
        {
            throw new InvalidOperationException("config defines `[permissions]` profiles but does not set `default_permissions`");
        }

        if (string.IsNullOrWhiteSpace(profileName))
        {
            throw new InvalidOperationException("default_permissions requires a named permissions profile");
        }

        _ = TryReadObjectExact(config, "permissions", out var permissionsRoot);
        if (!TryReadObjectExact(permissionsRoot, profileName!, out var profile))
        {
            throw new InvalidOperationException($"default_permissions refers to undefined profile `{profileName}`");
        }

        if (!TryReadObjectExact(profile, "filesystem", out var filesystem))
        {
            throw new InvalidOperationException($"permissions profile `{profileName}` must define a `[permissions.{profileName}.filesystem]` table");
        }

        if (filesystem.Count == 0)
        {
            throw new InvalidOperationException($"permissions profile `{profileName}` must define at least one filesystem entry");
        }

        var state = new KernelCompiledPermissionState();
        foreach (var pair in filesystem)
        {
            if (!TryApplyFilesystemPermission(pair.Key, pair.Value, cwd, state, out var errorMessage))
            {
                throw new InvalidOperationException(errorMessage ?? $"invalid filesystem permission entry `{pair.Key}`");
            }
        }

        var networkEnabled = false;
        if (TryReadObjectExact(profile, "network", out var network))
        {
            networkEnabled = ReadBooleanExact(network, "enabled") ?? false;
        }

        var sandboxPolicy = BuildPermissionsProfileSandboxPolicy(state, networkEnabled);
        if (string.Equals(ReadString(sandboxPolicy, "type"), "workspaceWrite", StringComparison.OrdinalIgnoreCase))
        {
            sandboxPolicy = MergeWorkspaceWriteWritableRoots(
                sandboxPolicy,
                cwd,
                ResolveAdditionalPermissionsProfileWritableRoots(tianShuHome));
        }

        var sandboxMode = sandboxPolicy.GetProperty("type").GetString() ?? "workspaceWrite";
        return (sandboxPolicy, sandboxMode);
    }

    private static bool TryApplyFilesystemPermission(
        string path,
        object? rawPermission,
        string cwd,
        KernelCompiledPermissionState state,
        out string? errorMessage)
    {
        errorMessage = null;
        if (TryReadAccessMode(rawPermission, out var accessMode))
        {
            return ApplyFilesystemEntry(path, subpath: null, accessMode, cwd, state, out errorMessage);
        }

        if (!TryAsDictionary(rawPermission, out var scopedPermissions))
        {
            errorMessage = $"invalid filesystem permission entry `{path}`";
            return false;
        }

        foreach (var scoped in scopedPermissions)
        {
            if (!TryReadAccessMode(scoped.Value, out accessMode))
            {
                errorMessage = $"invalid filesystem access mode for `{path}`";
                return false;
            }

            if (!ApplyFilesystemEntry(path, scoped.Key, accessMode, cwd, state, out errorMessage))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ApplyFilesystemEntry(
        string rawPath,
        string? subpath,
        string accessMode,
        string cwd,
        KernelCompiledPermissionState state,
        out string? errorMessage)
    {
        errorMessage = null;
        if (string.Equals(accessMode, "none", StringComparison.OrdinalIgnoreCase))
        {
            state.HasExplicitDenyEntries = true;
            if (state.RootWriteRequested)
            {
                state.HasWriteNarrowingEntries = true;
            }

            return true;
        }

        var effectiveAccess = NormalizeConfigText(accessMode)?.ToLowerInvariant();
        if (effectiveAccess is not ("read" or "write"))
        {
            errorMessage = $"invalid filesystem access mode {accessMode}";
            return false;
        }

        var normalizedPath = NormalizeConfigText(rawPath);
        if (normalizedPath is null)
        {
            errorMessage = $"filesystem path `{rawPath}` must be absolute, use `~/...`, or start with `:`";
            return false;
        }

        var hasNestedSubpath = !string.IsNullOrWhiteSpace(subpath) && !string.Equals(subpath, ".", StringComparison.Ordinal);
        var cwdFullPath = Path.GetFullPath(cwd);
        switch (normalizedPath)
        {
            case ":root":
                if (hasNestedSubpath)
                {
                    errorMessage = $"filesystem path `{rawPath}` does not support nested entries";
                    return false;
                }

                if (effectiveAccess == "write")
                {
                    state.RootWriteRequested = true;
                    if (state.ReadableRoots.Count > 0 || state.HasExplicitDenyEntries)
                    {
                        state.HasWriteNarrowingEntries = true;
                    }

                    state.FullDiskRead = true;
                    state.FullDiskWrite = true;
                }
                else
                {
                    if (state.RootWriteRequested)
                    {
                        state.HasWriteNarrowingEntries = true;
                    }

                    state.FullDiskRead = true;
                }

                return true;

            case ":minimal":
                if (hasNestedSubpath)
                {
                    errorMessage = $"filesystem path `{rawPath}` does not support nested entries";
                    return false;
                }

                if (effectiveAccess == "read")
                {
                    state.IncludePlatformDefaults = true;
                }

                return true;

            case ":current_working_directory":
                if (hasNestedSubpath)
                {
                    errorMessage = $"filesystem path `{rawPath}` does not support nested entries";
                    return false;
                }

                if (effectiveAccess == "write")
                {
                    state.WorkspaceRootWrite = true;
                }
                else
                {
                    if (state.RootWriteRequested)
                    {
                        state.HasWriteNarrowingEntries = true;
                    }

                    state.ReadableRoots.Add(cwdFullPath);
                }

                return true;

            case ":project_roots":
            {
                var resolvedProjectPath = ResolveProjectRootsPath(cwdFullPath, subpath, out errorMessage);
                if (resolvedProjectPath is null)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(subpath) || string.Equals(subpath, ".", StringComparison.Ordinal))
                {
                    if (effectiveAccess == "write")
                    {
                        state.WorkspaceRootWrite = true;
                    }
                    else
                    {
                        if (state.RootWriteRequested)
                        {
                            state.HasWriteNarrowingEntries = true;
                        }

                        state.ReadableRoots.Add(resolvedProjectPath);
                    }
                }
                else if (effectiveAccess == "write")
                {
                    state.WritableRoots.Add(resolvedProjectPath);
                }
                else
                {
                    state.ReadableRoots.Add(resolvedProjectPath);
                }

                return true;
            }

            case ":tmpdir":
                if (hasNestedSubpath)
                {
                    errorMessage = $"filesystem path `{rawPath}` does not support nested entries";
                    return false;
                }

                var tempPath = ResolveTempDirectoryPath();
                if (effectiveAccess == "write")
                {
                    state.TmpdirWritable = true;
                    return true;
                }

                if (tempPath is not null)
                {
                    state.ReadableRoots.Add(tempPath);
                }

                return true;

            case ":slash_tmp":
                if (hasNestedSubpath)
                {
                    errorMessage = $"filesystem path `{rawPath}` does not support nested entries";
                    return false;
                }

                if (effectiveAccess == "write")
                {
                    state.SlashTmpWritable = true;
                    return true;
                }

                var slashTmpPath = ResolveSlashTmpDirectoryPath();
                if (slashTmpPath is not null)
                {
                    if (state.RootWriteRequested)
                    {
                        state.HasWriteNarrowingEntries = true;
                    }

                    state.ReadableRoots.Add(slashTmpPath);
                }

                return true;
        }

        if (normalizedPath.StartsWith(":", StringComparison.Ordinal))
        {
            return true;
        }

        var resolvedPath = ResolveAbsolutePermissionPath(normalizedPath, subpath, out errorMessage);
        if (resolvedPath is null)
        {
            return false;
        }

        if (effectiveAccess == "write")
        {
            if (string.Equals(resolvedPath, cwdFullPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                state.WorkspaceRootWrite = true;
            }
            else
            {
                state.WritableRoots.Add(resolvedPath);
            }
        }
        else
        {
            if (state.RootWriteRequested)
            {
                state.HasWriteNarrowingEntries = true;
            }

            state.ReadableRoots.Add(resolvedPath);
        }

        return true;
    }

    private static JsonElement BuildPermissionsProfileSandboxPolicy(KernelCompiledPermissionState state, bool networkEnabled)
    {
        if (state.HasExplicitDenyEntries)
        {
            throw new InvalidOperationException(
                "permissions profile contains deny entries that require direct runtime enforcement, which is not yet supported");
        }

        if (state.HasWriteNarrowingEntries)
        {
            throw new InvalidOperationException(
                "permissions profile narrows `:root = write` with read/none carveouts, which requires direct runtime enforcement");
        }

        var readOnlyAccess = BuildRestrictedAccessPayload(
            includePlatformDefaults: state.IncludePlatformDefaults,
            readableRoots: state.FullDiskRead ? null : DeduplicatePaths(state.ReadableRoots));
        if (state.FullDiskRead)
        {
            readOnlyAccess = BuildFullAccessPayload();
        }

        if (state.FullDiskWrite)
        {
            return networkEnabled
                ? JsonSerializer.SerializeToElement(new
                {
                    type = "danger-full-access",
                })
                : BuildExternalSandboxPolicy(networkEnabled: false);
        }

        if (state.WorkspaceRootWrite)
        {
            return BuildWorkspaceWriteSandboxPolicy(
                DeduplicatePaths(state.WritableRoots),
                readOnlyAccess,
                networkEnabled,
                excludeTmpdirEnvVar: !state.TmpdirWritable,
                excludeSlashTmp: !state.SlashTmpWritable);
        }

        if (state.WritableRoots.Count > 0 || state.TmpdirWritable || state.SlashTmpWritable)
        {
            throw new InvalidOperationException(
                "permissions profile requests filesystem writes outside the workspace root, which is not supported until the runtime enforces FileSystemSandboxPolicy directly");
        }

        return BuildReadOnlySandboxPolicy(readOnlyAccess, networkEnabled);
    }

    private static JsonElement BuildExternalSandboxPolicy(bool networkEnabled)
    {
        return JsonSerializer.SerializeToElement(new
        {
            type = "externalSandbox",
            networkAccess = networkEnabled ? "enabled" : "restricted",
        });
    }

    private static JsonElement MergeWorkspaceWriteWritableRoots(
        JsonElement sandboxPolicy,
        string workspaceRoot,
        IReadOnlyList<string> additionalWritableRoots)
    {
        var existingWritableRoots = ReadStringArray(sandboxPolicy, "writableRoots");
        var normalizedWorkspaceRoot = NormalizeConfigText(workspaceRoot);
        IEnumerable<string> mergedSources = existingWritableRoots;
        if (existingWritableRoots.Count == 0 && !string.IsNullOrWhiteSpace(normalizedWorkspaceRoot))
        {
            mergedSources = mergedSources.Concat([normalizedWorkspaceRoot!]);
        }

        if (additionalWritableRoots.Count == 0 && existingWritableRoots.Count > 0)
        {
            return sandboxPolicy.Clone();
        }

        var mergedWritableRoots = DeduplicatePaths(
            mergedSources.Concat(additionalWritableRoots));
        var readOnlyAccess = ReadObject(sandboxPolicy, "readOnlyAccess") ?? BuildFullAccessPayload();
        var networkAccess = ReadBoolean(sandboxPolicy, "networkAccess") ?? false;
        var excludeTmpdirEnvVar = ReadBoolean(sandboxPolicy, "excludeTmpdirEnvVar") ?? false;
        var excludeSlashTmp = ReadBoolean(sandboxPolicy, "excludeSlashTmp") ?? false;
        return BuildWorkspaceWriteSandboxPolicy(
            mergedWritableRoots,
            readOnlyAccess,
            networkAccess,
            excludeTmpdirEnvVar,
            excludeSlashTmp);
    }

    private static IReadOnlyList<string> ResolveAdditionalPermissionsProfileWritableRoots(string tianShuHome)
    {
        var memoriesRoot = TianShuHomePathUtilities.ResolveDataPathFromHome(tianShuHome, "memory");
        Directory.CreateDirectory(memoriesRoot);
        return [Path.GetFullPath(memoriesRoot)];
    }

    private static JsonElement BuildWorkspaceWriteSandboxPolicy(
        IReadOnlyList<string> writableRoots,
        object readOnlyAccess,
        bool networkAccess,
        bool excludeTmpdirEnvVar,
        bool excludeSlashTmp)
    {
        return JsonSerializer.SerializeToElement(new
        {
            type = "workspaceWrite",
            writableRoots = writableRoots.ToArray(),
            readOnlyAccess,
            networkAccess,
            excludeTmpdirEnvVar,
            excludeSlashTmp,
        });
    }

    private static JsonElement BuildReadOnlySandboxPolicy(object readOnlyAccess, bool networkAccess)
    {
        return JsonSerializer.SerializeToElement(new
        {
            type = "readOnly",
            readOnlyAccess,
            networkAccess,
        });
    }

    private static object BuildFullAccessPayload()
        => new
        {
            type = "fullAccess",
        };

    private static object BuildRestrictedAccessPayload(bool includePlatformDefaults, IReadOnlyList<string>? readableRoots)
        => new
        {
            type = "restricted",
            includePlatformDefaults,
            readableRoots = readableRoots?.ToArray() ?? Array.Empty<string>(),
        };

    private static string[] DeduplicatePaths(IEnumerable<string> paths)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(comparer)
            .ToArray();
    }

    private static JsonElement? ReadObject(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Object ? property.Clone() : null;
    }

    private static bool? ReadBoolean(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? ResolveAbsolutePermissionPath(string rawPath, string? subpath, out string? errorMessage)
    {
        errorMessage = null;
        var expandedPath = NormalizeWindowsVerbatimPath(ExpandHomePath(rawPath));
        if (string.IsNullOrWhiteSpace(expandedPath) || !Path.IsPathRooted(expandedPath))
        {
            errorMessage = $"filesystem path `{rawPath}` must be absolute, use `~/...`, or start with `:`";
            return null;
        }

        var fullPath = Path.GetFullPath(expandedPath);
        if (string.IsNullOrWhiteSpace(subpath) || string.Equals(subpath, ".", StringComparison.Ordinal))
        {
            return fullPath;
        }

        if (!IsValidRelativePermissionSubpath(subpath))
        {
            errorMessage = BuildInvalidRelativeSubpathMessage(subpath);
            return null;
        }

        return Path.GetFullPath(Path.Combine(fullPath, subpath));
    }

    private static string? ResolveProjectRootsPath(string cwd, string? subpath, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(subpath) || string.Equals(subpath, ".", StringComparison.Ordinal))
        {
            return cwd;
        }

        if (!IsValidRelativePermissionSubpath(subpath))
        {
            errorMessage = BuildInvalidRelativeSubpathMessage(subpath);
            return null;
        }

        return Path.GetFullPath(Path.Combine(cwd, subpath));
    }

    private static string BuildInvalidRelativeSubpathMessage(string subpath)
        => $"filesystem subpath `{subpath}` must be a descendant path without `.` or `..` components";

    private static string? ResolveTempDirectoryPath()
    {
        var tmpDir = NormalizeConfigText(Environment.GetEnvironmentVariable("TMPDIR"));
        return !string.IsNullOrWhiteSpace(tmpDir) && Path.IsPathRooted(tmpDir)
            ? Path.GetFullPath(tmpDir)
            : null;
    }

    private static string? ResolveSlashTmpDirectoryPath()
    {
        if (OperatingSystem.IsWindows() || !Directory.Exists("/tmp"))
        {
            return null;
        }

        return Path.GetFullPath("/tmp");
    }

    private static string? ExpandHomePath(string path)
    {
        if (string.Equals(path, "~", StringComparison.Ordinal))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[2..]);
        }

        return path;
    }

    private static string? NormalizeWindowsVerbatimPath(string? path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        const string verbatimPrefix = @"\\?\";
        const string uncVerbatimPrefix = @"\\?\UNC\";
        const string slashVerbatimPrefix = "//?/";
        const string slashUncVerbatimPrefix = "//?/UNC/";
        if (path.StartsWith(uncVerbatimPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[uncVerbatimPrefix.Length..];
        }

        if (path.StartsWith(slashUncVerbatimPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[slashUncVerbatimPrefix.Length..].Replace('/', '\\');
        }

        if (path.StartsWith(verbatimPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return path[verbatimPrefix.Length..];
        }

        if (path.StartsWith(slashVerbatimPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return path[slashVerbatimPrefix.Length..].Replace('/', '\\');
        }

        return path;
    }

    private static bool IsValidRelativePermissionSubpath(string subpath)
    {
        if (string.IsNullOrWhiteSpace(subpath) || Path.IsPathRooted(subpath))
        {
            return false;
        }

        var normalized = subpath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0
               && segments.All(static segment => segment is not "." and not "..");
    }

    private static string? NormalizeLegacySandboxMode(string value)
    {
        var normalized = NormalizeConfigText(value)?.ToLowerInvariant();
        return normalized switch
        {
            "read-only" or "readonly" or "read_only" or "readOnly" => "readOnly",
            "workspace-write" or "workspacewrite" or "workspace_write" or "workspaceWrite" => "workspaceWrite",
            "danger-full-access" or "dangerfullaccess" or "danger_full_access" => "danger-full-access",
            _ => null,
        };
    }

    private static bool TryReadAccessMode(object? value, out string accessMode)
    {
        accessMode = string.Empty;
        if (TryReadString(value, out var text))
        {
            accessMode = text;
            return true;
        }

        return false;
    }

    private static KernelConfiguredShellEnvironmentPolicyInherit ParseShellEnvironmentPolicyInherit(string? value)
    {
        return NormalizeConfigText(value) switch
        {
            "core" => KernelConfiguredShellEnvironmentPolicyInherit.Core,
            "none" => KernelConfiguredShellEnvironmentPolicyInherit.None,
            _ => KernelConfiguredShellEnvironmentPolicyInherit.All,
        };
    }

    private static Dictionary<string, string> ReadConfiguredStringDictionary(
        Dictionary<string, object?> config,
        params string[] propertyNames)
    {
        if (!TryReadObjectExact(config, out var values, propertyNames))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (TryReadString(pair.Value, out var text) && !string.IsNullOrWhiteSpace(pair.Key))
            {
                dictionary[pair.Key] = text;
            }
        }

        return dictionary;
    }

    private static bool TryReadConfiguredApprovalPolicyValueFromActiveProfile(
        Dictionary<string, object?> config,
        out object? approvalPolicyValue,
        params string[] propertyNames)
    {
        if (TryReadActiveProfileConfig(config, out var profileConfig)
            && TryReadConfiguredApprovalPolicyValue(profileConfig, out approvalPolicyValue, propertyNames))
        {
            return true;
        }

        approvalPolicyValue = null;
        return false;
    }

    private static bool TryReadConfiguredApprovalPolicyValue(
        Dictionary<string, object?> config,
        out object? approvalPolicyValue,
        params string[] propertyNames)
    {
        return TryReadValueExact(config, out approvalPolicyValue, propertyNames);
    }

    private static bool TryReadConfiguredNestedApprovalPolicyValue(
        Dictionary<string, object?> config,
        out object? approvalPolicyValue,
        params string[][] propertyPaths)
    {
        foreach (var propertyPath in propertyPaths)
        {
            if (TryReadNestedValueExact(config, propertyPath, out approvalPolicyValue))
            {
                return true;
            }
        }

        approvalPolicyValue = null;
        return false;
    }

    private static bool TryReadConfiguredNestedApprovalPolicyValueFromActiveProfile(
        Dictionary<string, object?> config,
        out object? approvalPolicyValue,
        params string[][] propertyPaths)
    {
        if (TryReadActiveProfileConfig(config, out var profileConfig)
            && TryReadConfiguredNestedApprovalPolicyValue(profileConfig, out approvalPolicyValue, propertyPaths))
        {
            return true;
        }

        approvalPolicyValue = null;
        return false;
    }

    private static bool TryReadActiveProfileConfig(
        Dictionary<string, object?> config,
        out Dictionary<string, object?> profileConfig)
    {
        var activeProfile = NormalizeConfigText(ReadStringExact(config, "profile"));
        if (!string.IsNullOrWhiteSpace(activeProfile)
            && TryReadObjectExact(config, "profiles", out var profiles)
            && TryReadObjectExact(profiles, activeProfile!, out profileConfig))
        {
            return true;
        }

        profileConfig = null!;
        return false;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToArray();
    }

    private static string? ReadString(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static string? ReadStringExact(Dictionary<string, object?> config, params string[] propertyNames)
        => TryReadStringExact(config, out var value, propertyNames) ? value : null;

    private static bool TryReadStringExact(
        Dictionary<string, object?> config,
        out string value,
        params string[] propertyNames)
    {
        if (TryReadValueExact(config, out var rawValue, propertyNames)
            && TryReadString(rawValue, out value))
        {
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string? ReadNestedStringExact(Dictionary<string, object?> config, params string[][] propertyPaths)
        => TryReadNestedStringExact(config, out var value, propertyPaths) ? value : null;

    private static bool TryReadNestedStringExact(
        Dictionary<string, object?> config,
        out string value,
        params string[][] propertyPaths)
    {
        foreach (var propertyPath in propertyPaths)
        {
            if (TryReadNestedValueExact(config, propertyPath, out var rawValue)
                && TryReadString(rawValue, out value))
            {
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool? ReadBooleanExact(Dictionary<string, object?> config, params string[] propertyNames)
        => TryReadValueExact(config, out var rawValue, propertyNames)
           && TryReadBoolean(rawValue, out var value)
            ? value
            : null;

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

    private static string[] ReadStringArrayExact(Dictionary<string, object?> config, string propertyName)
        => TryReadValueExact(config, propertyName, out var rawValue)
           && TryReadStringArray(rawValue, out var values)
            ? values
            : Array.Empty<string>();

    private static string[] ReadStringArrayExact(Dictionary<string, object?> config, params string[][] propertyPaths)
    {
        foreach (var propertyPath in propertyPaths)
        {
            if (TryReadNestedValueExact(config, propertyPath, out var rawValue)
                && TryReadStringArray(rawValue, out var values))
            {
                return values;
            }
        }

        return Array.Empty<string>();
    }

    private static bool TryReadObjectExact(
        Dictionary<string, object?> config,
        string propertyName,
        out Dictionary<string, object?> value)
    {
        if (TryReadValueExact(config, propertyName, out var rawValue)
            && TryAsDictionary(rawValue, out value))
        {
            return true;
        }

        value = null!;
        return false;
    }

    private static bool TryReadObjectExact(
        Dictionary<string, object?> config,
        out Dictionary<string, object?> value,
        params string[] propertyNames)
    {
        if (TryReadValueExact(config, out var rawValue, propertyNames)
            && TryAsDictionary(rawValue, out value))
        {
            return true;
        }

        value = null!;
        return false;
    }

    private static bool TryReadValueExact(Dictionary<string, object?> config, string propertyName, out object? value)
        => config.TryGetValue(propertyName, out value);

    private static bool TryReadValueExact(
        Dictionary<string, object?> config,
        out object? value,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryReadValueExact(config, propertyName, out value))
            {
                return true;
            }
        }

        value = null;
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
            if (!TryReadValueExact(current, propertyPath[index], out value))
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

    private static bool TryReadStringArray(object? value, out string[] values)
    {
        if (value is string)
        {
            values = Array.Empty<string>();
            return false;
        }

        if (value is IEnumerable<object?> items)
        {
            values = items
                .Select(static item => TryReadString(item, out var text) ? NormalizeConfigText(text) : null)
                .Where(static item => item is not null)
                .Cast<string>()
                .ToArray();
            return true;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            values = element
                .EnumerateArray()
                .Select(static item => item.ValueKind == JsonValueKind.String ? NormalizeConfigText(item.GetString()) : null)
                .Where(static item => item is not null)
                .Cast<string>()
                .ToArray();
            return true;
        }

        values = Array.Empty<string>();
        return false;
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

    private static string? NormalizeConfigText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
