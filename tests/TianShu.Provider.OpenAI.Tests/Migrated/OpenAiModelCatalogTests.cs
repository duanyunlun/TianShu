using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI.Tests;

public sealed class OpenAiModelCatalogTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("gpt-5")]
    [InlineData("gpt-5.4")]
    [InlineData("unknown-model")]
    public void GetBaseInstructions_ShouldUseTianShuNativeIdentity(string? model)
    {
        var instructions = ProviderModelCatalogs.GetBaseInstructions(model);

        Assert.Contains("你是天枢（TianShu）", instructions, StringComparison.Ordinal);
        Assert.Contains("TianShu CLI", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("Codex CLI", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("You are Codex", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("coding agent running in the Codex", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("You are TianShu", instructions, StringComparison.Ordinal);
    }
}
