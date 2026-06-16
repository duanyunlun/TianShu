using TianShu.AppHost.Configuration;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Configuration;

namespace TianShu.AppHost.Tests;

public sealed class KernelTurnPromptConfigurationTests
{
    [Fact]
    public void ResolveTurnDeveloperMessage_WhenPromptConfigured_ShouldApplyPromptSections()
    {
        var promptConfiguration = TianShuPromptConfiguration.Empty with
        {
            ApplyPatch = new TianShuPromptSection(
                Enabled: true,
                Mode: TianShuPromptMergeMode.Replace,
                Text: "APPLY PATCH OVERRIDE"),
            LanguagePolicy = new TianShuPromptSection(
                Enabled: true,
                Mode: TianShuPromptMergeMode.Replace,
                Text: "LANGUAGE OVERRIDE"),
        };
        var context = new TurnRequestContext(
            Model: "gpt-5-codex",
            ModelProvider: null,
            ServiceTier: null,
            ApprovalPolicy: null,
            SandboxPolicy: null,
            SandboxMode: null,
            DeveloperInstructions: "DEVELOPER",
            PromptConfiguration: promptConfiguration);

        var developerMessage = KernelTurnExecutionRuntimeHelpers.ResolveTurnDeveloperMessage(
            context,
            includeBaseInstructions: false);

        Assert.NotNull(developerMessage);
        Assert.Contains("APPLY PATCH OVERRIDE", developerMessage, StringComparison.Ordinal);
        Assert.Contains("DEVELOPER", developerMessage, StringComparison.Ordinal);
        Assert.Contains("LANGUAGE OVERRIDE", developerMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("You must use the apply_patch tool", developerMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveTurnDeveloperMessage_WhenLanguagePolicyDisabled_ShouldOmitLanguagePolicy()
    {
        var promptConfiguration = TianShuPromptConfiguration.Empty with
        {
            LanguagePolicy = new TianShuPromptSection(
                Enabled: false,
                Mode: TianShuPromptMergeMode.Replace,
                Text: "LANGUAGE OVERRIDE"),
        };
        var context = new TurnRequestContext(
            Model: "plain-model",
            ModelProvider: null,
            ServiceTier: null,
            ApprovalPolicy: null,
            SandboxPolicy: null,
            SandboxMode: null,
            DeveloperInstructions: "DEVELOPER",
            PromptConfiguration: promptConfiguration);

        var developerMessage = KernelTurnExecutionRuntimeHelpers.ResolveTurnDeveloperMessage(
            context,
            includeBaseInstructions: false);

        Assert.NotNull(developerMessage);
        Assert.Contains("DEVELOPER", developerMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("LANGUAGE OVERRIDE", developerMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("语言策略：", developerMessage, StringComparison.Ordinal);
    }
}
