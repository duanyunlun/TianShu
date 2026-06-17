using TianShu.Contracts.Provider;
using TianShu.Contracts.Primitives;

namespace TianShu.Template.ProviderModule.Tests;

public sealed class TemplateProviderModuleTests
{
    [Fact]
    public void Manifest_ShouldPassProviderAccessValidation()
    {
        var module = new TemplateProviderModule();
        var manifest = TemplateProviderModule.CreateManifest();

        var result = ProviderModuleAccessValidator.Validate(manifest, module.Descriptor, "default");

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.NotNull(result.Access);
        Assert.Equal(TemplateProviderModule.ProviderId, result.Access!.Manifest.ProviderId);
        Assert.Equal(TemplateProviderModule.WireApi, result.Access.ProtocolBinding.WireApi);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnTextDeltaAndCompletion()
    {
        var module = new TemplateProviderModule();
        var request = new ProviderInvocationRequest(
            new ExecutionId("execution-template-provider"),
            TemplateProviderModule.ProviderId,
            TemplateProviderModule.DefaultModel,
            new ProviderConversationContext(),
            [new TextProviderInputItem("hello")]);

        var events = new List<ProviderStreamEvent>();
        await foreach (var item in module.InvokeAsync(request, CancellationToken.None))
        {
            events.Add(item);
        }

        Assert.Contains(events, static item => item is ProviderTextDeltaEvent);
        var completion = Assert.IsType<ProviderCompletionEvent>(events.Last());
        Assert.Contains("hello", completion.Completion.OutputText, StringComparison.Ordinal);
        Assert.NotNull(completion.Completion.Usage);
    }
}
