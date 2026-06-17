using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Template.ToolModule.Tests;

public sealed class TemplateToolModuleTests
{
    [Fact]
    public void Manifest_ShouldPassToolAccessValidation()
    {
        var provider = new TemplateToolProvider();
        var descriptors = provider.DescribeTools(new TianShuToolRegistrationContext());

        var result = ToolModuleAccessValidator.Validate(
            TemplateToolProvider.CreateManifest(),
            descriptors,
            TemplateToolProvider.CreateGovernance());

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.Equal(TemplateToolProvider.ToolKey, Assert.Single(result.Access!.Tools).ToolId);
    }

    [Fact]
    public async Task Handler_ShouldEchoInputThroughPublicToolContract()
    {
        var provider = new TemplateToolProvider();
        var handler = provider.CreateHandler(TemplateToolProvider.ToolKey, new TianShuToolActivationContext());
        var request = new ToolInvocationRequest(
            new CallId("call-template-tool"),
            TemplateToolProvider.ToolKey,
            "invoke",
            StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["text"] = "hello",
            }));
        var context = new TianShuToolInvocationContext(
            "thread-template",
            "turn-template",
            ".");

        var result = await handler.InvokeAsync(request, context, CancellationToken.None);
        var projection = ToolModuleAccessValidator.ProjectResult(result);

        Assert.True(projection.Success);
        Assert.Equal(TemplateToolProvider.ToolKey, projection.ToolKey);
        Assert.Contains("hello", projection.OutputText, StringComparison.Ordinal);
    }
}
