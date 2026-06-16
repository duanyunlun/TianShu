using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelAutoCompactionRuntimeHelpersTests
{
    [Fact]
    public void ResolveConfiguredModelAutoCompactTokenLimit_ShouldOnlyHonorFormalKey()
    {
        var snakeCaseConfig = new Dictionary<string, object?>
        {
            ["model_auto_compact_token_limit"] = 1024L,
        };
        var legacyCamelCaseConfig = new Dictionary<string, object?>
        {
            ["modelAutoCompactTokenLimit"] = "2048",
        };

        var snakeCase = KernelAutoCompactionRuntimeHelpers.ResolveConfiguredModelAutoCompactTokenLimit(snakeCaseConfig);
        var legacyCamelCase = KernelAutoCompactionRuntimeHelpers.ResolveConfiguredModelAutoCompactTokenLimit(legacyCamelCaseConfig);

        Assert.Equal(1024L, snakeCase);
        Assert.Null(legacyCamelCase);
    }

    [Fact]
    public void EstimatePromptTokenCount_ShouldCountInstructionsAndStringContent()
    {
        var messages = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["role"] = "user",
                ["content"] = "123456",
            },
            new()
            {
                ["role"] = "assistant",
                ["content"] = new { ignored = true },
            },
        };

        var tokens = KernelAutoCompactionRuntimeHelpers.EstimatePromptTokenCount("abcdef", messages);

        Assert.Equal(4, tokens);
    }

    [Fact]
    public void BuildResponsesFollowUpInput_ShouldAppendResponseItemsBeforeNextInput()
    {
        var input = KernelAutoCompactionRuntimeHelpers.BuildResponsesFollowUpInput(
            priorInput:
            [
                new { type = "message", value = "prior" },
            ],
            responseItems:
            [
                new { type = "message", value = "response" },
            ],
            nextInput:
            [
                new { type = "message", value = "next" },
            ]);

        Assert.Equal(3, input.Count);
        Assert.Contains("prior", input[0].ToString(), StringComparison.Ordinal);
        Assert.Contains("response", input[1].ToString(), StringComparison.Ordinal);
        Assert.Contains("next", input[2].ToString(), StringComparison.Ordinal);
    }
}
