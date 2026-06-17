using System.Runtime.CompilerServices;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Provider;
using TianShu.Provider.Abstractions;

namespace TianShu.Samples.Provider.Echo;

/// <summary>
/// Echo Provider 示例：演示第三方 Provider 如何只依赖公开 Provider SDK 返回流式文本和 usage 投影。
/// Echo Provider sample that demonstrates a third-party provider returning streamed text and usage through the public SDK only.
/// </summary>
public sealed class EchoProviderModule : IProviderModule
{
    public const string ProviderId = "sample.provider.echo";
    public const string WireApi = "sample_echo_protocol";
    public const string DefaultModel = "echo-small";

    public ProviderDescriptor Descriptor { get; } = CreateDescriptor();

    public static ProviderModuleManifest CreateManifest()
        => ProviderModuleDescriptorFactory.CreateAccessManifest(
            CreateDescriptor(),
            WireApi,
            routeSetId: "sample",
            errorSpecs:
            [
                new ProviderErrorSpec(
                    "sample_echo_unavailable",
                    ProviderErrorKind.ProviderUnavailable,
                    retryable: true,
                    safeForUser: true,
                    remediation: "The sample provider is in-process and should only fail when the module is not loaded."),
            ]);

    public async IAsyncEnumerable<ProviderStreamEvent> InvokeAsync(
        ProviderInvocationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var prompt = string.Join(
            Environment.NewLine,
            request.Inputs.OfType<TextProviderInputItem>().Select(static item => item.Text));
        var normalizedPrompt = string.IsNullOrWhiteSpace(prompt) ? "(empty input)" : prompt.Trim();
        var output = $"Echo provider sample: {normalizedPrompt}";

        await Task.Yield();
        yield return new ProviderTextDeltaEvent(output);
        yield return new ProviderCompletionEvent(new ProviderCompletion(
            output,
            new ProviderUsage(
                InputTokens: EstimateTokens(normalizedPrompt),
                OutputTokens: EstimateTokens(output))));
    }

    private static ProviderDescriptor CreateDescriptor()
        => ProviderModuleDescriptorFactory.Create(
            ProviderId,
            "Sample Echo Provider",
            ProviderProtocolKind.Custom,
            "https://sample-provider.example.invalid/v1",
            "TIANSHU_SAMPLE_ECHO_API_KEY",
            new ProviderCapabilityProfile(SupportsStreaming: true),
            models:
            [
                new TianShu.Contracts.Provider.ProviderModelDescriptor(DefaultModel, "Echo Small", "sample"),
            ],
            wireApi: WireApi);

    private static int EstimateTokens(string text)
        => Math.Max(1, (int)Math.Ceiling(text.Length / 4m));
}
