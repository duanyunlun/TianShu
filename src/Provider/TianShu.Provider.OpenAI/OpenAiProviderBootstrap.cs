using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI;

/// <summary>
/// OpenAI provider 的 southbound bootstrap 实现。
/// Southbound bootstrap implementation for the OpenAI provider.
/// </summary>
public sealed class OpenAiProviderBootstrap : IProviderRuntimeBootstrap, IProviderResponsesComponentBootstrap
{
    /// <inheritdoc />
    public string ProtocolAdapterId => OpenAiResponsesProtocolAdapter.AdapterId;

    /// <inheritdoc />
    public string WireApi => "responses";

    /// <inheritdoc />
    public IProtocolAdapter CreateProtocolAdapter()
        => new OpenAiResponsesProtocolAdapter();

    /// <inheritdoc />
    public IProviderNotificationInterpreter CreateNotificationInterpreter()
        => new OpenAiProviderNotificationInterpreter();

    /// <inheritdoc />
    public IProviderToolEventFactory CreateToolEventFactory()
        => new OpenAiProviderToolEventFactory();

    /// <inheritdoc />
    public IProviderServerRequestRouter CreateServerRequestRouter()
        => new OpenAiProviderServerRequestRouter();

    /// <inheritdoc />
    public IProviderServerRequestInterpreter CreateServerRequestInterpreter()
        => new OpenAiProviderServerRequestInterpreter();

    /// <inheritdoc />
    public IProviderServerRequestResponseSerializer CreateServerRequestResponseSerializer()
        => new OpenAiProviderServerRequestResponseSerializer();

    /// <inheritdoc />
    public IProviderModelCatalog CreateModelCatalog()
        => new OpenAiModelCatalog();

    /// <inheritdoc />
    public IProviderResponsesRequestComposer CreateRequestComposer()
        => new OpenAiResponsesRequestComposer();

    /// <inheritdoc />
    public IProviderResponsesTransportProtocolBinding CreateTransportProtocolBinding()
        => new OpenAiResponsesTransportProtocolBinding();

    /// <inheritdoc />
    public IProviderResponsesTransportRetryStrategy CreateTransportRetryStrategy()
        => new OpenAiResponsesTransportRetryStrategy();

    /// <inheritdoc />
    public IProviderResponsesStreamChunkParser CreateStreamChunkParser()
        => NullProviderResponsesStreamChunkParser.Instance;

    /// <inheritdoc />
    public IProviderResponsesToolSurfaceBuilder CreateToolSurfaceBuilder()
        => new OpenAiResponsesToolSurfaceBuilder();

    /// <inheritdoc />
    public IReadOnlyList<string> BuildCliArguments(ProviderRuntimeCliArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        List<string> segments = [];

        if (arguments.ConfigOverrides is not null)
        {
            foreach (var pair in arguments.ConfigOverrides.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(Normalize(pair.Key)))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(Normalize(arguments.ProfileName))
                    && string.Equals(pair.Key, "profile", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                segments.Add("-c");
                segments.Add(QuoteArg($"{pair.Key}={pair.Value}"));
            }
        }

        var profileName = Normalize(arguments.ProfileName);
        if (!string.IsNullOrWhiteSpace(profileName)
            && !arguments.ProfileNameResolvedFromConfig)
        {
            segments.Add("-c");
            segments.Add(QuoteArg($"profile={profileName}"));
        }

        var explicitConfigPath = NormalizeCliPath(arguments.ConfigFilePath);
        var defaultConfigPath = NormalizeCliPath(arguments.DefaultConfigFilePath);
        if (!string.IsNullOrWhiteSpace(explicitConfigPath)
            && !string.Equals(explicitConfigPath, defaultConfigPath, PathComparison))
        {
            segments.Add("--config-file");
            segments.Add(QuoteArg(explicitConfigPath));
        }

        return segments;
    }

    private static string QuoteArg(string value)
    {
        if (value.Contains('"') || value.Contains(' '))
        {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        return value;
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeCliPath(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(normalized);
        }
        catch
        {
            return normalized;
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
