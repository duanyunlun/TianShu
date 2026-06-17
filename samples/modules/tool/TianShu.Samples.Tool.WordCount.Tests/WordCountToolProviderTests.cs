using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Samples.Tool.WordCount.Tests;

public sealed class WordCountToolProviderTests
{
    [Fact]
    public void Manifest_ShouldPassToolAccessValidation()
    {
        var provider = new WordCountToolProvider();
        var result = ToolModuleAccessValidator.Validate(
            WordCountToolProvider.CreateManifest(),
            provider.DescribeTools(new TianShuToolRegistrationContext()),
            WordCountToolProvider.CreateGovernance());

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.Equal(WordCountToolProvider.ToolKey, Assert.Single(result.Access!.Tools).ToolId);
    }

    [Fact]
    public async Task Handler_ShouldReturnWordCountProjection()
    {
        var provider = new WordCountToolProvider();
        var handler = provider.CreateHandler(WordCountToolProvider.ToolKey, new TianShuToolActivationContext());
        var request = new ToolInvocationRequest(
            new CallId("call-sample-word-count"),
            WordCountToolProvider.ToolKey,
            "invoke",
            StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["text"] = "one two three",
            }));
        var context = new TianShuToolInvocationContext("thread-sample", "turn-sample", ".");

        var result = await handler.InvokeAsync(request, context, CancellationToken.None);
        var projection = ToolModuleAccessValidator.ProjectResult(result);

        Assert.True(projection.Success);
        Assert.Equal(WordCountToolProvider.ToolKey, projection.ToolKey);
        Assert.Contains("words=3", projection.OutputText, StringComparison.Ordinal);
        Assert.Equal("3", Assert.Single(result.StreamItems).Payload.Properties["wordCount"].NumberValue);
    }
}
