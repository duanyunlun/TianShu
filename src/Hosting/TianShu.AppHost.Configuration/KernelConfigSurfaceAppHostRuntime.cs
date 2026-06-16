using System.Text.Json;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// config/* northbound surface 宿主运行时。
/// Host runtime for config/* northbound surfaces.
/// </summary>
internal sealed class KernelConfigSurfaceAppHostRuntime
{
    private readonly SemaphoreSlim configGate;
    private readonly Func<bool, string?, CancellationToken, Task<KernelConfigReadSnapshot>> buildConfigReadSnapshotAsync;
    private readonly Func<CancellationToken, Task> reloadLoadedThreadsUserConfigAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<JsonElement?, int, string, object?, CancellationToken, Task> writeErrorAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;

    public KernelConfigSurfaceAppHostRuntime(
        SemaphoreSlim configGate,
        Func<bool, string?, CancellationToken, Task<KernelConfigReadSnapshot>> buildConfigReadSnapshotAsync,
        Func<CancellationToken, Task> reloadLoadedThreadsUserConfigAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<JsonElement?, int, string, object?, CancellationToken, Task> writeErrorAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync)
    {
        this.configGate = configGate;
        this.buildConfigReadSnapshotAsync = buildConfigReadSnapshotAsync;
        this.reloadLoadedThreadsUserConfigAsync = reloadLoadedThreadsUserConfigAsync;
        this.writeResultAsync = writeResultAsync;
        this.writeErrorAsync = writeErrorAsync;
        this.writeNotificationAsync = writeNotificationAsync;
    }

    public async Task HandleConfigReadAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var includeLayers = ReadBool(@params, "includeLayers")
            ?? ReadBool(@params, "include_layers")
            ?? false;
        var cwd = ReadString(@params, "cwd");
        var snapshot = await buildConfigReadSnapshotAsync(includeLayers, cwd, cancellationToken).ConfigureAwait(false);

        await writeResultAsync(id, new
        {
            config = snapshot.Config,
            origins = snapshot.Origins,
            layers = snapshot.Layers,
        }, cancellationToken).ConfigureAwait(false);

        if (!snapshot.HasPersistentConfig)
        {
            var suggestedPath = TianShuConfigTomlPathResolver.ResolveWritableProjectConfigTomlPath(cwd);
            await writeNotificationAsync("configWarning", new
            {
                summary = "未检测到本地配置文件，已使用默认配置。",
                details = "如需持久化参数，建议写入当前工作区的 .tianshu/tianshu.toml；用户级 tianshu.toml 仅参与读取，不再作为默认写入目标。",
                path = suggestedPath,
                range = (object?)null,
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleConfigValueWriteAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var key = ReadString(@params, "keyPath")
            ?? ReadString(@params, "key")
            ?? ReadString(@params, "path");
        if (string.IsNullOrWhiteSpace(key))
        {
            await writeErrorAsync(id, -32602, "keyPath/key/path 不能为空。", null, cancellationToken).ConfigureAwait(false);
            return;
        }

        var filePath = ReadString(@params, "filePath");
        var cwd = ReadString(@params, "cwd");
        var expectedVersion = ReadString(@params, "expectedVersion");
        var mergeStrategy = KernelConfigWriteUtilities.NormalizeConfigMergeStrategy(ReadString(@params, "mergeStrategy"));
        var valueJson = TryGetRawProperty(@params, "value") ?? "null";
        var validationRules = KernelConfigRequirementsUtilities.LoadConfigValidationRules(cancellationToken);
        var requiredFeatureFlags = KernelConfigRequirementsUtilities.LoadRequiredFeatureFlags(cancellationToken);
        if (KernelConfigWriteUtilities.TryValidateConfigEdit(key, valueJson, validationRules, out var validationError))
        {
            await writeErrorAsync(id, -32602, validationError!, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!KernelConfigWriteUtilities.TryParseConfigJsonValue(valueJson, out var value))
        {
            await writeErrorAsync(id, -32602, $"配置项 `{key}` 不是合法 JSON。", null, cancellationToken).ConfigureAwait(false);
            return;
        }

        string resolvedPath;
        try
        {
            resolvedPath = KernelConfigWriteUtilities.ResolveConfigWritePath(filePath, cwd, key);
        }
        catch (InvalidOperationException ex)
        {
            await writeErrorAsync(
                    id,
                    -32600,
                    ex.Message,
                    new
                    {
                        errorCode = "configLayerReadonly",
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var segments = KernelConfigPersistenceUtilities.SplitConfigKeyPath(
            KernelConfigPersistenceUtilities.CanonicalizePersistedConfigKeyPath(key));
        if (segments.Count == 0)
        {
            await writeErrorAsync(id, -32602, "keyPath/path 不能为空。", null, cancellationToken).ConfigureAwait(false);
            return;
        }

        KernelConfigWriteMutationResult mutationResult;
        try
        {
            mutationResult = await KernelConfigWriteUtilities.MutatePersistedConfigTableAsync(
                    configGate,
                    resolvedPath,
                    expectedVersion,
                    root =>
                    {
                        if (!KernelConfigWriteUtilities.TryApplyConfigWriteValue(root, segments, value, mergeStrategy, out var pathNotFound))
                        {
                            throw new KernelConfigWriteException(
                                -32600,
                                pathNotFound ? "Path not found" : "Invalid configuration edit.",
                                new
                                {
                                    errorCode = pathNotFound ? "configPathNotFound" : "configValidationError",
                                });
                        }
                    },
                    validationRules,
                    requiredFeatureFlags,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (KernelConfigWriteException ex)
        {
            await writeErrorAsync(id, ex.Code, ex.Message, ex.DataPayload, cancellationToken).ConfigureAwait(false);
            return;
        }

        var snapshot = await buildConfigReadSnapshotAsync(false, cwd, cancellationToken).ConfigureAwait(false);
        var overriddenMetadata = KernelConfigWriteUtilities.ComputeConfigWriteOverriddenMetadata(
            snapshot,
            KernelConfigObjectUtilities.ConvertTomlTableToDictionary(mutationResult.Root),
            segments);

        await writeResultAsync(id, new
        {
            status = overriddenMetadata is null ? "ok" : "okOverridden",
            version = mutationResult.Version,
            filePath = mutationResult.FilePath,
            overriddenMetadata,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleConfigBatchWriteAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var filePath = ReadString(@params, "filePath");
        var cwd = ReadString(@params, "cwd");
        var expectedVersion = ReadString(@params, "expectedVersion");
        var reloadUserConfig = ReadBool(@params, "reloadUserConfig") ?? ReadBool(@params, "reload_user_config") ?? false;
        var items = KernelConfigWriteUtilities.ExtractBatchConfigItems(@params);
        var validationRules = KernelConfigRequirementsUtilities.LoadConfigValidationRules(cancellationToken);
        var requiredFeatureFlags = KernelConfigRequirementsUtilities.LoadRequiredFeatureFlags(cancellationToken);
        foreach (var item in items)
        {
            if (!KernelConfigWriteUtilities.TryValidateConfigEdit(item.Key, item.ValueJson, validationRules, out var validationError))
            {
                continue;
            }

            await writeErrorAsync(id, -32602, validationError!, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        string resolvedPath;
        try
        {
            resolvedPath = KernelConfigWriteUtilities.ResolveConfigWritePath(filePath, cwd, items.Select(static item => item.Key));
        }
        catch (InvalidOperationException ex)
        {
            await writeErrorAsync(
                    id,
                    -32600,
                    ex.Message,
                    new
                    {
                        errorCode = "configLayerReadonly",
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var parsedItems = new List<(string Key, JsonElement Value, IReadOnlyList<string> Segments, string? MergeStrategy)>(items.Count);
        foreach (var item in items)
        {
            if (!KernelConfigWriteUtilities.TryParseConfigJsonValue(item.ValueJson, out var value))
            {
                await writeErrorAsync(id, -32602, $"配置项 `{item.Key}` 不是合法 JSON。", null, cancellationToken).ConfigureAwait(false);
                return;
            }

            var segments = KernelConfigPersistenceUtilities.SplitConfigKeyPath(
                KernelConfigPersistenceUtilities.CanonicalizePersistedConfigKeyPath(item.Key));
            if (segments.Count == 0)
            {
                await writeErrorAsync(id, -32602, "keyPath/path 不能为空。", null, cancellationToken).ConfigureAwait(false);
                return;
            }

            parsedItems.Add((item.Key, value, segments, item.MergeStrategy));
        }

        KernelConfigWriteMutationResult mutationResult;
        try
        {
            mutationResult = await KernelConfigWriteUtilities.MutatePersistedConfigTableAsync(
                    configGate,
                    resolvedPath,
                    expectedVersion,
                    root =>
                    {
                        foreach (var item in parsedItems)
                        {
                            if (!KernelConfigWriteUtilities.TryApplyConfigWriteValue(root, item.Segments, item.Value, item.MergeStrategy, out var pathNotFound))
                            {
                                throw new KernelConfigWriteException(
                                    -32600,
                                    pathNotFound ? "Path not found" : "Invalid configuration edit.",
                                    new
                                    {
                                        errorCode = pathNotFound ? "configPathNotFound" : "configValidationError",
                                    });
                            }
                        }
                    },
                    validationRules,
                    requiredFeatureFlags,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (KernelConfigWriteException ex)
        {
            await writeErrorAsync(id, ex.Code, ex.Message, ex.DataPayload, cancellationToken).ConfigureAwait(false);
            return;
        }

        var snapshot = await buildConfigReadSnapshotAsync(false, cwd, cancellationToken).ConfigureAwait(false);
        var overriddenMetadata = parsedItems
            .Select(item => KernelConfigWriteUtilities.ComputeConfigWriteOverriddenMetadata(
                snapshot,
                KernelConfigObjectUtilities.ConvertTomlTableToDictionary(mutationResult.Root),
                item.Segments))
            .FirstOrDefault(static metadata => metadata is not null);
        if (reloadUserConfig)
        {
            await reloadLoadedThreadsUserConfigAsync(cancellationToken).ConfigureAwait(false);
        }

        await writeResultAsync(id, new
        {
            status = overriddenMetadata is null ? "ok" : "okOverridden",
            version = mutationResult.Version,
            filePath = mutationResult.FilePath,
            overriddenMetadata,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleConfigRequirementsReadAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        _ = @params;
        cancellationToken.ThrowIfCancellationRequested();
        var requirements = KernelConfigRequirementsUtilities.BuildConfigRequirementsPayload(
            KernelConfigRequirementsUtilities.LoadMergedConfigRequirements());

        await writeResultAsync(id, new
        {
            requirements,
        }, cancellationToken).ConfigureAwait(false);
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

    private static bool? ReadBool(JsonElement json, string propertyName)
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

    private static string? TryGetRawProperty(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.GetRawText();
    }
}
