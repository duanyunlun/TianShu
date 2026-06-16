using System.Text.Json;
using TianShu.AppHost.Configuration;
using TianShu.Contracts.Catalog;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Catalog;

/// <summary>
/// catalog/list northbound surface 宿主运行时。
/// Host runtime for northbound catalog/list surfaces.
/// </summary>
internal sealed class KernelCatalogSurfaceAppHostRuntime
{
    private static readonly TimeSpan ModelCatalogRequestTimeout = TimeSpan.FromSeconds(10);
    private readonly Func<CancellationToken, Task<Dictionary<string, string>>> loadEffectiveConfigValuesAsync;
    private readonly Func<string?, bool, ResolvedToolCatalogSnapshot> buildResolvedToolCatalog;
    private readonly Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;

    public KernelCatalogSurfaceAppHostRuntime(
        Func<CancellationToken, Task<Dictionary<string, string>>> loadEffectiveConfigValuesAsync,
        Func<string?, bool, ResolvedToolCatalogSnapshot> buildResolvedToolCatalog,
        Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync)
    {
        this.loadEffectiveConfigValuesAsync = loadEffectiveConfigValuesAsync;
        this.buildResolvedToolCatalog = buildResolvedToolCatalog;
        this.writeErrorAsync = writeErrorAsync;
        this.writeResultAsync = writeResultAsync;
    }

    public async Task HandleModelListAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var cursor = ReadString(@params, "cursor");
        var limit = ReadInt(@params, "limit");
        var includeHidden = ReadBool(@params, "includeHidden") ?? false;
        var requireEndpoint = ReadBool(@params, "requireEndpoint") ?? false;
        var configValues = await loadEffectiveConfigValuesAsync(cancellationToken).ConfigureAwait(false);
        var endpointCatalog = await TryListEndpointModelsAsync(configValues, cancellationToken).ConfigureAwait(false);
        if (requireEndpoint && endpointCatalog.Models is null)
        {
            await writeErrorAsync(id, -32050, BuildEndpointCatalogFailureMessage(endpointCatalog), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var models = endpointCatalog.Models
                     ?? KernelCatalogSurfaceUtilities.GetBuiltInModels()
                         .Where(x => includeHidden || !x.Hidden)
                         .ToList();
        models = models
            .Where(x => includeHidden || !x.Hidden)
            .ToList();
        var total = models.Count;

        if (total == 0)
        {
            await writeResultAsync(id, new
            {
                data = Array.Empty<object>(),
                nextCursor = (string?)null,
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var start = 0;
        if (!string.IsNullOrWhiteSpace(cursor) && !int.TryParse(cursor, out start))
        {
            await writeErrorAsync(id, -32600, $"invalid cursor: {cursor}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (start < 0 || start > total)
        {
            await writeErrorAsync(id, -32600, $"cursor {start} exceeds total models {total}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var effectiveLimit = Math.Clamp(limit ?? total, 1, total);
        var end = Math.Min(start + effectiveLimit, total);
        var data = models
            .Skip(start)
            .Take(end - start)
            .Select(KernelCatalogSurfaceUtilities.ToModelPayload)
            .ToArray();
        var nextCursor = end < total ? end.ToString() : null;

        await writeResultAsync(id, new
        {
            data,
            nextCursor,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleExperimentalFeatureListAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var cursor = ReadString(@params, "cursor");
        var limit = ReadInt(@params, "limit");
        var descriptors = KernelCatalogSurfaceUtilities.GetExperimentalFeatureDescriptors();
        var values = await loadEffectiveConfigValuesAsync(cancellationToken).ConfigureAwait(false);
        var items = descriptors
            .Select(x => KernelCatalogSurfaceUtilities.ToExperimentalFeaturePayload(
                x,
                KernelCatalogSurfaceUtilities.ResolveFeatureEnabledState(x, values)))
            .ToList();
        var total = items.Count;

        if (total == 0)
        {
            await writeResultAsync(id, new
            {
                data = Array.Empty<object>(),
                nextCursor = (string?)null,
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var start = 0;
        if (!string.IsNullOrWhiteSpace(cursor) && !int.TryParse(cursor, out start))
        {
            await writeErrorAsync(id, -32600, $"invalid cursor: {cursor}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (start < 0 || start > total)
        {
            await writeErrorAsync(id, -32600, $"cursor {start} exceeds total feature flags {total}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var effectiveLimit = Math.Clamp(limit ?? total, 1, total);
        var end = Math.Min(start + effectiveLimit, total);
        var data = items.Skip(start).Take(end - start).ToArray();
        var nextCursor = end < total ? end.ToString() : null;

        await writeResultAsync(id, new
        {
            data,
            nextCursor,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleToolsCatalogReadAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var cwd = ReadString(@params, "cwd");
        var includeHidden = ReadBool(@params, "includeHidden") ?? false;
        await writeResultAsync(id, buildResolvedToolCatalog(cwd, includeHidden), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<EndpointModelCatalogResult> TryListEndpointModelsAsync(
        IReadOnlyDictionary<string, string> configValues,
        CancellationToken cancellationToken)
    {
        var providerId = ReadConfigString(configValues, "provider")
                         ?? ResolveNativeProviderId(configValues)
                         ?? "openai";
        var baseUrl = KernelModelProviderConfigUtilities.ReadConfiguredModelProviderSetting(
            configValues,
            providerId,
            "base_url");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return EndpointModelCatalogResult.Failed(
                providerId,
                baseUrl: null,
                apiKeyEnv: null,
                endpoints: [],
                "TianShu 原生配置中没有当前 provider 的 base_url。请在 ~/.tianshu/tianshu.toml 或项目 .tianshu/tianshu.toml 中配置 providers.<id>.base_url。");
        }

        var apiKeyEnv = KernelModelProviderConfigUtilities.ReadConfiguredModelProviderSetting(
            configValues,
            providerId,
            "api_key_env");
        if (string.IsNullOrWhiteSpace(apiKeyEnv))
        {
            return EndpointModelCatalogResult.Failed(
                providerId,
                baseUrl,
                apiKeyEnv: null,
                BuildModelsEndpoints(baseUrl, null),
                $"TianShu 原生配置中没有当前 provider 的 api_key_env。请配置 providers.{providerId}.api_key_env。");
        }

        var apiKey = Normalize(Environment.GetEnvironmentVariable(apiKeyEnv));
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return EndpointModelCatalogResult.Failed(
                providerId,
                baseUrl,
                apiKeyEnv,
                BuildModelsEndpoints(baseUrl, null),
                $"环境变量 {apiKeyEnv} 未设置或为空，无法请求模型目录。");
        }

        var protocol = KernelModelProviderConfigUtilities.ReadConfiguredModelProviderProtocolValue(configValues, providerId);
        var endpoints = BuildModelsEndpoints(baseUrl, protocol);
        if (endpoints.Count == 0)
        {
            return EndpointModelCatalogResult.Failed(
                providerId,
                baseUrl,
                apiKeyEnv,
                endpoints,
                $"无法从 base_url 构造 models endpoint：{baseUrl}");
        }

        string? lastFailure = null;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ModelCatalogRequestTimeout);
            using var client = new HttpClient();
            foreach (var endpoint in endpoints)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                using var response = await client.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    lastFailure = $"{endpoint} 返回 HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                    continue;
                }

                List<ControlPlaneModelCatalogItem> models;
                try
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token).ConfigureAwait(false);
                    models = ParseEndpointModelCatalog(document.RootElement);
                }
                catch (JsonException ex)
                {
                    lastFailure = $"{endpoint} 返回成功，但响应不是 models JSON：{ex.GetType().Name}: {ex.Message}";
                    continue;
                }

                if (models.Count == 0)
                {
                    lastFailure = $"{endpoint} 返回成功，但 data 中没有可用模型 id。";
                    continue;
                }

                var defaultModel = ReadConfigString(configValues, "model")
                                   ?? ResolveNativeModelName(configValues);
                MarkDefaultModel(models, defaultModel);
                return EndpointModelCatalogResult.Succeeded(providerId, baseUrl, apiKeyEnv, endpoint, endpoints, models);
            }

            return EndpointModelCatalogResult.Failed(
                providerId,
                baseUrl,
                apiKeyEnv,
                endpoints,
                lastFailure ?? "models endpoint 没有返回可用模型。");
        }
        catch (Exception ex) when (ex is HttpRequestException
                                  or TaskCanceledException
                                  or JsonException
                                  or IOException
                                  or InvalidOperationException)
        {
            return EndpointModelCatalogResult.Failed(
                providerId,
                baseUrl,
                apiKeyEnv,
                endpoints,
                $"请求模型目录失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static List<ControlPlaneModelCatalogItem> ParseEndpointModelCatalog(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<ControlPlaneModelCatalogItem>();
        foreach (var item in data.EnumerateArray())
        {
            var id = ReadEndpointModelId(item);
            if (string.IsNullOrWhiteSpace(id)
                || models.Any(existing => string.Equals(existing.Model, id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            models.Add(new ControlPlaneModelCatalogItem
            {
                Id = id,
                Model = id,
                DisplayName = id,
                InputModalities = ["text"],
                Hidden = false,
                DefaultReasoningEffort = "medium",
            });
        }

        return models
            .OrderBy(static model => model.Model, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ReadEndpointModelId(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            return Normalize(item.GetString());
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(item, "id")
               ?? ReadString(item, "model")
               ?? ReadString(item, "name");
    }

    private static void MarkDefaultModel(List<ControlPlaneModelCatalogItem> models, string? defaultModel)
    {
        var defaultIndex = string.IsNullOrWhiteSpace(defaultModel)
            ? 0
            : models.FindIndex(item => string.Equals(item.Model, defaultModel, StringComparison.OrdinalIgnoreCase));
        if (defaultIndex < 0)
        {
            defaultIndex = 0;
        }

        for (var index = 0; index < models.Count; index++)
        {
            models[index] = models[index] with
            {
                IsDefault = index == defaultIndex,
            };
        }
    }

    private static IReadOnlyList<Uri> BuildModelsEndpoints(string baseUrl, string? protocol)
    {
        var normalized = Normalize(baseUrl);
        if (string.IsNullOrWhiteSpace(normalized)
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var baseUri))
        {
            return [];
        }

        var endpoints = new List<Uri>();
        var normalizedProtocol = Normalize(protocol);
        if (string.Equals(normalizedProtocol, "google_generative", StringComparison.OrdinalIgnoreCase))
        {
            AddEndpoint(ProviderEndpointPathUtilities.ResolveVersionedEndpoint(normalized, "v1beta", "models"));
        }
        else
        {
            AddEndpoint(ProviderEndpointPathUtilities.ResolveVersionedEndpoint(normalized, "v1", "models"));
        }

        AddEndpoint(normalized.TrimEnd('/') + "/models");
        var path = baseUri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith("/v1beta", StringComparison.OrdinalIgnoreCase))
        {
            AddEndpoint(ProviderEndpointPathUtilities.ResolveVersionedEndpoint(normalized, "v1", "models"));
            AddEndpoint(ProviderEndpointPathUtilities.ResolveVersionedEndpoint(normalized, "v1beta", "models"));
        }

        return endpoints;

        void AddEndpoint(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
                && !endpoints.Any(existing => Uri.Compare(
                    existing,
                    endpoint,
                    UriComponents.AbsoluteUri,
                    UriFormat.SafeUnescaped,
                    StringComparison.OrdinalIgnoreCase) == 0))
            {
                endpoints.Add(endpoint);
            }
        }
    }

    private static string BuildEndpointCatalogFailureMessage(EndpointModelCatalogResult result)
    {
        var attempted = result.Endpoints.Count == 0
            ? "未生成请求 URL"
            : string.Join(", ", result.Endpoints.Select(static endpoint => endpoint.AbsoluteUri));
        return $"无法从 provider endpoint 获取模型目录。provider={result.ProviderId ?? "unknown"}; base_url={result.BaseUrl ?? "未配置"}; api_key_env={result.ApiKeyEnv ?? "未配置"}; requested={attempted}; reason={result.FailureReason ?? "unknown"}";
    }

    private static string? ResolveNativeProviderId(IReadOnlyDictionary<string, string> configValues)
    {
        var profile = ReadConfigString(configValues, "profile") ?? "default";
        var executionId = ReadConfigString(configValues, $"profiles.{profile}.execution") ?? "default";
        var agentId = ReadConfigString(configValues, $"profiles.{profile}.agent")
                      ?? ReadConfigString(configValues, $"execution_profiles.{executionId}.agent")
                      ?? "default";
        return ReadConfigString(configValues, $"execution_profiles.{executionId}.provider")
               ?? ReadConfigString(configValues, $"agents.{agentId}.provider");
    }

    private static string? ResolveNativeModelName(IReadOnlyDictionary<string, string> configValues)
    {
        var profile = ReadConfigString(configValues, "profile") ?? "default";
        var executionId = ReadConfigString(configValues, $"profiles.{profile}.execution") ?? "default";
        var agentId = ReadConfigString(configValues, $"profiles.{profile}.agent")
                      ?? ReadConfigString(configValues, $"execution_profiles.{executionId}.agent")
                      ?? "default";
        return ReadConfigString(configValues, $"agents.{agentId}.model");
    }

    private static string? ReadConfigString(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!values.TryGetValue(key, out var raw))
            {
                continue;
            }

            var normalized = ReadJsonString(raw) ?? Normalize(raw);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static string? ReadJsonString(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.String => Normalize(document.RootElement.GetString()),
                JsonValueKind.Number => document.RootElement.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task HandleCollaborationModeListAsync(JsonElement id, CancellationToken cancellationToken)
    {
        var data = KernelCatalogSurfaceUtilities.BuildCollaborationModeMasks();
        await writeResultAsync(id, new
        {
            data = data
                .Select(KernelCatalogSurfaceUtilities.ToCollaborationModePayload)
                .ToArray(),
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

    private static int? ReadInt(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), out var parsed) => parsed,
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

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record EndpointModelCatalogResult(
        string? ProviderId,
        string? BaseUrl,
        string? ApiKeyEnv,
        IReadOnlyList<Uri> Endpoints,
        Uri? SucceededEndpoint,
        List<ControlPlaneModelCatalogItem>? Models,
        string? FailureReason)
    {
        public static EndpointModelCatalogResult Succeeded(
            string providerId,
            string baseUrl,
            string apiKeyEnv,
            Uri endpoint,
            IReadOnlyList<Uri> endpoints,
            List<ControlPlaneModelCatalogItem> models)
            => new(providerId, baseUrl, apiKeyEnv, endpoints, endpoint, models, FailureReason: null);

        public static EndpointModelCatalogResult Failed(
            string? providerId,
            string? baseUrl,
            string? apiKeyEnv,
            IReadOnlyList<Uri> endpoints,
            string failureReason)
            => new(providerId, baseUrl, apiKeyEnv, endpoints, SucceededEndpoint: null, Models: null, failureReason);
    }
}
