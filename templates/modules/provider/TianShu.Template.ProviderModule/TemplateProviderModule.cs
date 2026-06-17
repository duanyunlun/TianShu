using System.Runtime.CompilerServices;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Provider;
using TianShu.Provider.Abstractions;

namespace TianShu.Template.ProviderModule;

/// <summary>
/// 自定义 Provider 模块模板：演示第三方 Provider 只通过公开 IProviderModule 接入。
/// Custom Provider module template that demonstrates third-party access through the public IProviderModule surface only.
/// </summary>
public sealed class TemplateProviderModule : IProviderModule
{
    public const string ProviderId = "template.provider";
    public const string WireApi = "template_provider_protocol";
    public const string DefaultModel = "template-model";

    public ProviderDescriptor Descriptor { get; } = CreateDescriptor();

    public static ProviderModuleManifest CreateManifest()
        => ProviderModuleDescriptorFactory.CreateAccessManifest(
            CreateDescriptor(),
            WireApi,
            routeSetId: "default",
            errorSpecs:
            [
                new ProviderErrorSpec(
                    "template_provider_unavailable",
                    ProviderErrorKind.ProviderUnavailable,
                    retryable: true,
                    safeForUser: true,
                    remediation: "Replace the template transport with a real provider client."),
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
        var output = string.IsNullOrWhiteSpace(prompt)
            ? "Template provider response."
            : $"Template provider received: {prompt}";

        await Task.Yield();
        yield return new ProviderTextDeltaEvent(output);
        yield return new ProviderCompletionEvent(new ProviderCompletion(
            output,
            new ProviderUsage(
                InputTokens: Math.Max(1, prompt.Length / 4),
                OutputTokens: Math.Max(1, output.Length / 4))));
    }

    private static ProviderDescriptor CreateDescriptor()
        => ProviderModuleDescriptorFactory.Create(
            ProviderId,
            "Template Provider",
            ProviderProtocolKind.Custom,
            "https://provider.example.invalid/v1",
            "TIANSHU_TEMPLATE_PROVIDER_API_KEY",
            new ProviderCapabilityProfile(SupportsStreaming: true),
            models:
            [
                new TianShu.Contracts.Provider.ProviderModelDescriptor(DefaultModel, "Template Model", "template"),
            ],
            wireApi: WireApi);
}
