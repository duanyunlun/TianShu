using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;

namespace TianShu.Samples.Provider.Echo.Tests;

public sealed class EchoProviderModuleTests
{
    [Fact]
    public void Manifest_ShouldPassProviderAccessValidation()
    {
        var module = new EchoProviderModule();
        var result = ProviderModuleAccessValidator.Validate(
            EchoProviderModule.CreateManifest(),
            module.Descriptor,
            routeSetId: "sample");

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.NotNull(result.Access);
        Assert.Equal(EchoProviderModule.ProviderId, result.Access!.Manifest.ProviderId);
        Assert.Equal(EchoProviderModule.WireApi, result.Access.ProtocolBinding.WireApi);
    }

    [Fact]
    public async Task InvokeAsync_ShouldEchoTextAndProjectUsage()
    {
        var module = new EchoProviderModule();
        var request = new ProviderInvocationRequest(
            new ExecutionId("execution-sample-provider"),
            EchoProviderModule.ProviderId,
            EchoProviderModule.DefaultModel,
            new ProviderConversationContext(),
            [new TextProviderInputItem("hello module ecosystem")]);

        var events = new List<ProviderStreamEvent>();
        await foreach (var item in module.InvokeAsync(request, CancellationToken.None))
        {
            events.Add(item);
        }

        Assert.Contains(events, static item => item is ProviderTextDeltaEvent);
        var completion = Assert.IsType<ProviderCompletionEvent>(events.Last());
        Assert.Contains("hello module ecosystem", completion.Completion.OutputText, StringComparison.Ordinal);
        Assert.NotNull(completion.Completion.Usage);
        Assert.True(completion.Completion.Usage!.InputTokens > 0);
        Assert.True(completion.Completion.Usage.OutputTokens > 0);
    }
}
