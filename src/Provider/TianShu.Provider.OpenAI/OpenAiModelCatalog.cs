using System.Reflection;
using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI;

/// <summary>
/// OpenAI provider 的模型能力目录。
/// Model capability catalog for the OpenAI provider.
/// </summary>
public sealed class OpenAiModelCatalog : IProviderModelCatalog
{
    private const string ModelsResourceName = "TianShu.Provider.OpenAI.Resources.tianshu-models.json";
    private const string PersonalityPlaceholder = "{{ personality }}";
    private const string DefaultTianShuBaseInstructions = """
你是天枢（TianShu），运行在 TianShu CLI 与 AppHost 运行时中的本地编码 Agent。你和用户共享同一个工作区，你的职责是以证据和谨慎态度协助完成软件、配置、诊断与文档工作。

# 通用规则

- 在修改代码、配置或文档之前，先从当前工作区建立上下文。
- 优先使用类型化契约、结构化数据和 TianShu 既有架构边界，避免临时字符串解析。
- 变更范围只覆盖用户请求，保留无关的用户改动。
- 可用时优先使用 `rg` / `rg --files` 等快速本地搜索工具。
- 使用用户当前语言沟通进展，并汇报可验证的结果。

# 安全

- 不得暴露 secret、token、密码、授权头或私有凭据。
- 除非用户明确授权，不得覆盖用户配置文件。
- 除非用户明确要求，不得运行破坏性的文件系统或 git 操作。
- 当事实不确定时，明确说明不确定，并指出最小验证步骤。

# 编辑

- 编辑前先读取相关文件或符号。
- 遵循仓库既有风格、测试、架构文档和 AGENTS.md 指令。
- 优先提交最小、可审查的补丁；行为或契约变化时补充测试。
- 仓库提供可行验证路径时，用聚焦测试或构建完成验证。
""";
    private static readonly Lazy<OpenAiModelCatalogSnapshot> BundledSnapshot = new(LoadBundledSnapshot);

    /// <inheritdoc />
    public bool SupportsParallelToolCalls(string? model)
    {
        return TryGetModel(model, out var descriptor) && descriptor.SupportsParallelToolCalls;
    }

    /// <inheritdoc />
    public bool SupportsSearchTool(string? model)
    {
        return TryGetModel(model, out var descriptor) && descriptor.SupportsSearchTool;
    }

    /// <inheritdoc />
    public bool SupportsImageInput(string? model)
    {
        return TryResolveModelOrFallback(model, out var descriptor)
               && descriptor.InputModalities.Contains("image", StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool SupportsImageDetailOriginal(string? model)
    {
        return TryResolveModelOrFallback(model, out var descriptor) && descriptor.SupportsImageDetailOriginal;
    }

    /// <inheritdoc />
    public bool SupportsWebSearchImageContent(string? model)
    {
        return TryResolveModelOrFallback(model, out var descriptor)
               && string.Equals(descriptor.WebSearchToolType, "text_and_image", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public string? GetShellToolType(string? model)
    {
        return TryResolveModelOrFallback(model, out var descriptor)
            ? descriptor.ShellToolType
            : (OperatingSystem.IsWindows() ? "shell_command" : "default");
    }

    /// <inheritdoc />
    public bool SupportsReasoningSummaries(string? model)
    {
        return TryResolveModelOrFallback(model, out var descriptor) && descriptor.SupportsReasoningSummaries;
    }

    /// <inheritdoc />
    public string GetDefaultReasoningEffort(string? model)
    {
        if (TryResolveModelOrFallback(model, out var descriptor)
            && !string.IsNullOrWhiteSpace(descriptor.DefaultReasoningEffort))
        {
            return descriptor.DefaultReasoningEffort;
        }

        return "medium";
    }

    /// <inheritdoc />
    public string? GetDefaultReasoningSummary(string? model)
    {
        return TryResolveModelOrFallback(model, out var descriptor)
            ? descriptor.DefaultReasoningSummary
            : null;
    }

    /// <inheritdoc />
    public bool SupportsVerbosity(string? model)
    {
        return TryResolveModelOrFallback(model, out var descriptor) && descriptor.SupportsVerbosity;
    }

    /// <inheritdoc />
    public bool PrefersResponsesWebsockets(string? model)
    {
        return TryResolveModelOrFallback(model, out var descriptor) && descriptor.PreferWebsockets;
    }

    /// <inheritdoc />
    public string? GetDefaultVerbosity(string? model)
    {
        return TryResolveModelOrFallback(model, out var descriptor)
            ? descriptor.DefaultVerbosity
            : null;
    }

    /// <inheritdoc />
    public string GetBaseInstructions(string? model)
        => DefaultTianShuBaseInstructions;

    /// <inheritdoc />
    public bool UsesFreeformApplyPatchTool(string? model)
    {
        return TryResolveModelOrFallback(model, out var descriptor)
               && string.Equals(descriptor.ApplyPatchToolType, "freeform", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public string? GetApplyPatchToolType(string? model)
    {
        return TryResolveModelOrFallback(model, out var descriptor)
            ? descriptor.ApplyPatchToolType
            : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string>? GetExperimentalSupportedTools(string? model)
    {
        return TryResolveModelOrFallback(model, out var descriptor)
            ? descriptor.ExperimentalSupportedTools
            : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<ProviderModelDescriptor> ListModels()
    {
        return GetCurrentSnapshot().OrderedModels;
    }

    /// <inheritdoc />
    public bool TryGetModel(string? model, out ProviderModelDescriptor descriptor)
    {
        descriptor = default!;
        var normalized = Normalize(model);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (!GetCurrentSnapshot().ModelsBySlug.TryGetValue(normalized, out var resolvedDescriptor))
        {
            return false;
        }

        descriptor = resolvedDescriptor;
        return true;
    }

    private static bool TryResolveModelOrFallback(string? model, out ProviderModelDescriptor descriptor)
    {
        var catalog = new OpenAiModelCatalog();
        if (catalog.TryGetModel(model, out descriptor))
        {
            return true;
        }

        if (catalog.TryGetModel("gpt-5", out descriptor))
        {
            return true;
        }

        var snapshot = GetCurrentSnapshot();
        descriptor = snapshot.OrderedModels.FirstOrDefault(static item => !item.Hidden)
                     ?? snapshot.OrderedModels.FirstOrDefault()!;
        return descriptor is not null;
    }

    private static OpenAiModelCatalogSnapshot GetCurrentSnapshot()
        => BundledSnapshot.Value;

    private static OpenAiModelCatalogSnapshot LoadBundledSnapshot()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ModelsResourceName)
            ?? throw new InvalidOperationException($"未找到嵌入资源：{ModelsResourceName}");
        using var document = JsonDocument.Parse(stream);
        return BuildSnapshot(ParseModels(document.RootElement));
    }

    private static OpenAiModelCatalogSnapshot BuildSnapshot(IReadOnlyList<ProviderModelDescriptor> sourceModels)
    {
        var orderedModels = sourceModels
            .Select(static (model, index) => new { model, index })
            .OrderBy(static item => item.model.Priority)
            .ThenBy(static item => item.index)
            .Select(static item => item.model)
            .ToArray();
        var modelsBySlug = orderedModels.ToDictionary(static model => model.Id, StringComparer.OrdinalIgnoreCase);
        return new OpenAiModelCatalogSnapshot(sourceModels.ToArray(), orderedModels, modelsBySlug);
    }

    private static IReadOnlyList<ProviderModelDescriptor> ParseModels(JsonElement root)
    {
        if (!root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("OpenAI models.json 格式无效：缺少 models 数组。");
        }

        var parsedModels = new List<ProviderModelDescriptor>();
        foreach (var item in models.EnumerateArray())
        {
            var descriptor = ParseModel(item);
            if (descriptor is not null)
            {
                parsedModels.Add(descriptor);
            }
        }

        return parsedModels;
    }

    private static ProviderModelDescriptor? ParseModel(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var slug = Normalize(ReadString(item, "slug"));
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var reasoningEfforts = ReadReasoningEfforts(item);
        var defaultReasoningEffort = Normalize(ReadString(item, "default_reasoning_level"));
        if (string.IsNullOrWhiteSpace(defaultReasoningEffort))
        {
            defaultReasoningEffort = reasoningEfforts.Count > 0
                ? reasoningEfforts[0].Effort
                : "medium";
        }

        return new ProviderModelDescriptor(
            Id: slug,
            Model: slug,
            DisplayName: ReadString(item, "display_name") ?? slug,
            Description: SanitizeDescription(ReadString(item, "description")),
            Hidden: !string.Equals(ReadString(item, "visibility"), "list", StringComparison.OrdinalIgnoreCase),
            SupportedInApi: ReadBoolean(item, "supported_in_api"),
            AvailabilityNuxMessage: ReadNestedString(item, "availability_nux", "message"),
            BaseInstructions: string.Empty,
            ApplyPatchToolType: Normalize(ReadString(item, "apply_patch_tool_type")),
            DefaultReasoningEffort: defaultReasoningEffort,
            SupportedReasoningEfforts: reasoningEfforts,
            WebSearchToolType: Normalize(ReadString(item, "web_search_tool_type")) ?? "text",
            InputModalities: ReadStringArray(item, "input_modalities"),
            ExperimentalSupportedTools: ReadOptionalStringArray(item, "experimental_supported_tools"),
            SupportsImageDetailOriginal: ReadBoolean(item, "supports_image_detail_original"),
            SupportsPersonality: SupportsPersonality(item),
            SupportsParallelToolCalls: ReadBoolean(item, "supports_parallel_tool_calls"),
            ShellToolType: ReadShellToolType(item),
            SupportsSearchTool: ReadBoolean(item, "supports_search_tool"),
            SupportsReasoningSummaries: ReadBoolean(item, "supports_reasoning_summaries"),
            DefaultReasoningSummary: Normalize(ReadString(item, "default_reasoning_summary")),
            PreferWebsockets: ReadBoolean(item, "prefer_websockets"),
            SupportsVerbosity: ReadBoolean(item, "support_verbosity"),
            DefaultVerbosity: Normalize(ReadString(item, "default_verbosity")),
            Priority: ReadInt(item, "priority") ?? int.MaxValue,
            UpgradeModel: ReadNestedString(item, "upgrade", "model"),
            UpgradeMigrationMarkdown: null);
    }

    private static string SanitizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        return description
            .Replace("Codex-optimized", "OpenAI coding-optimized", StringComparison.Ordinal)
            .Replace("codex-optimized", "OpenAI coding-optimized", StringComparison.OrdinalIgnoreCase)
            .Replace("Optimized for codex.", "OpenAI coding-optimized model.", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static string? ReadNestedString(JsonElement element, string objectPropertyName, string propertyName)
    {
        if (!element.TryGetProperty(objectPropertyName, out var nested) || nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(nested, propertyName);
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
               && property.ValueKind is JsonValueKind.True or JsonValueKind.False
               && property.GetBoolean();
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!.Trim())
            .ToArray();
    }

    private static IReadOnlyList<string>? ReadOptionalStringArray(JsonElement element, string propertyName)
    {
        return !element.TryGetProperty(propertyName, out _)
            ? null
            : ReadStringArray(element, propertyName);
    }

    private static IReadOnlyList<ProviderReasoningEffortDescriptor> ReadReasoningEfforts(JsonElement element)
    {
        if (!element.TryGetProperty("supported_reasoning_levels", out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ProviderReasoningEffortDescriptor>();
        }

        return property
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(static item =>
            {
                var effort = Normalize(ReadString(item, "effort"));
                if (string.IsNullOrWhiteSpace(effort))
                {
                    return null;
                }

                return new ProviderReasoningEffortDescriptor(
                    effort!,
                    ReadString(item, "description") ?? effort!);
            })
            .Where(static item => item is not null)
            .Select(static item => item!)
            .ToArray();
    }

    private static bool SupportsPersonality(JsonElement element)
    {
        if (!element.TryGetProperty("model_messages", out var modelMessages) || modelMessages.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var template = ReadString(modelMessages, "instructions_template");
        if (string.IsNullOrWhiteSpace(template) || !template.Contains(PersonalityPlaceholder, StringComparison.Ordinal))
        {
            return false;
        }

        if (!modelMessages.TryGetProperty("instructions_variables", out var variables) || variables.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return HasStringProperty(variables, "personality_default")
               && HasStringProperty(variables, "personality_friendly")
               && HasStringProperty(variables, "personality_pragmatic");
    }

    private static string? ReadShellToolType(JsonElement element)
    {
        return Normalize(ReadString(element, "shell_type")) switch
        {
            "default" => "default",
            "shell_command" => "shell_command",
            "unified_exec" => "unified_exec",
            "local" => "local",
            "disabled" => "disabled",
            _ => OperatingSystem.IsWindows() ? "shell_command" : "default",
        };
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool HasStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.String;
    }

    private sealed record OpenAiModelCatalogSnapshot(
        IReadOnlyList<ProviderModelDescriptor> SourceModels,
        IReadOnlyList<ProviderModelDescriptor> OrderedModels,
        IReadOnlyDictionary<string, ProviderModelDescriptor> ModelsBySlug);
}
