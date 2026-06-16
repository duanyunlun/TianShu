using System.Reflection;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelCollaborationModePrompts
{
    private const string DefaultTemplateResourceName = "TianShu.AppHost.Tools.Runtime.Resources.collaboration-mode.default.md";
    private const string PlanTemplateResourceName = "TianShu.AppHost.Tools.Runtime.Resources.collaboration-mode.plan.md";
    private const string KnownModeNamesPlaceholder = "{{KNOWN_MODE_NAMES}}";
    private const string RequestUserInputAvailabilityPlaceholder = "{{REQUEST_USER_INPUT_AVAILABILITY}}";
    private const string AskingQuestionsGuidancePlaceholder = "{{ASKING_QUESTIONS_GUIDANCE}}";

    private static readonly Lazy<string> DefaultTemplate = new(() => LoadTemplate(DefaultTemplateResourceName));
    private static readonly Lazy<string> PlanTemplate = new(() => LoadTemplate(PlanTemplateResourceName));

    public static string? ResolveDeveloperInstructions(
        KernelCollaborationModeState? state,
        bool defaultModeRequestUserInputEnabled = false)
    {
        return ResolveDeveloperInstructions(state?.Mode, defaultModeRequestUserInputEnabled);
    }

    public static string? ResolveDeveloperInstructions(
        string? mode,
        bool defaultModeRequestUserInputEnabled = false)
    {
        var normalizedMode = Normalize(mode) ?? KernelCollaborationModeState.DefaultMode;
        return normalizedMode.ToLowerInvariant() switch
        {
            KernelCollaborationModeState.DefaultMode => BuildDefaultPrompt(defaultModeRequestUserInputEnabled),
            KernelCollaborationModeState.PlanMode => PlanTemplate.Value,
            _ => null,
        };
    }

    private static string BuildDefaultPrompt(bool defaultModeRequestUserInputEnabled)
    {
        return DefaultTemplate.Value
            .Replace(KnownModeNamesPlaceholder, "default 和 plan", StringComparison.Ordinal)
            .Replace(
                RequestUserInputAvailabilityPlaceholder,
                defaultModeRequestUserInputEnabled
                    ? "Default 模式下可以使用 `request_user_input` 工具。"
                    : "Default 模式下不能使用 `request_user_input` 工具。如果你在 Default 模式下调用它，会返回错误。",
                StringComparison.Ordinal)
            .Replace(
                AskingQuestionsGuidancePlaceholder,
                defaultModeRequestUserInputEnabled
                    ? "在 Default 模式下，优先做出合理假设并执行用户请求，不要轻易停下来提问。如果确实必须提问，因为答案无法从本地上下文发现且合理假设风险较高，优先使用 `request_user_input` 工具，而不是在文本回复里写多选题。不要在文本回复中手写多选题。"
                    : "在 Default 模式下，优先做出合理假设并执行用户请求，不要轻易停下来提问。如果确实必须提问，因为答案无法从本地上下文发现且合理假设风险较高，请直接用简洁的纯文本问题询问用户。不要在文本回复中手写多选题。",
                StringComparison.Ordinal);
    }

    private static string LoadTemplate(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"未找到嵌入资源：{resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
