using System.Text;
using TianShu.Configuration;
using TianShu.Contracts.Configuration;
using TianShu.RuntimeComposition;

namespace TianShu.AppHost.Configuration.Tests;

public sealed class VersionCompatibilityConfigurationTests
{
    [Fact]
    public void P31_2_OldTianShuToml_ShouldLoadCurrentFieldsAndDiagnoseLegacyAliases()
    {
        var path = WriteTempConfig(
            """
            schema_version = "0.5.0"
            model = "gpt-5.5"
            provider = "openai"
            modelProvider = "anthropic"

            [providers.openai]
            base_url = "https://api.example.invalid/v1"
            api_key_env = "OPENAI_API_KEY"
            default_protocol = "openai_responses"
            apiKey = "legacy-inline-secret"

            [mcpServers.legacy]
            url = "https://legacy.example.invalid/mcp"
            """);

        try
        {
            var projection = new TianShuConfigurationTomlProjectionLoader().LoadFile(path);
            var runtimeConfig = new TianShuTomlConfigurationLoader().Load(path, profileOverride: null);

            Assert.Equal("openai", runtimeConfig.ModelProvider);
            Assert.Equal("OPENAI_API_KEY", runtimeConfig.ProviderEnvKey);
            Assert.False(TryReadNested(runtimeConfig.RawConfig, ["providers", "openai", "api_key"], out _));
            Assert.False(TryReadNested(runtimeConfig.RawConfig, ["mcp_servers", "legacy", "url"], out _));

            Assert.Contains(
                projection.Issues,
                static issue => issue.Code == ConfigurationIssueCodes.ValueKindInvalid
                                && string.Equals(issue.FieldKey, "schema_version", StringComparison.OrdinalIgnoreCase));
            AssertUnmapped(projection, "modelProvider");
            AssertUnmapped(projection, "providers.openai.apiKey");
            AssertUnmapped(projection, "mcpServers.legacy.url");

            var legacySecret = Assert.Single(projection.Values, static value => value.Key == "providers.openai.apiKey");
            Assert.True(legacySecret.IsSensitive);
            Assert.Equal("<redacted>", legacySecret.Value?.StringValue);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempConfig(string content)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tianshu-p31-version-compat", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "tianshu.toml");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static void AssertUnmapped(ConfigurationProjection projection, string key)
        => Assert.Contains(
            projection.Issues,
            issue => issue.Code == ConfigurationIssueCodes.FieldUnmapped
                     && string.Equals(issue.FieldKey, key, StringComparison.OrdinalIgnoreCase));

    private static bool TryReadNested(
        Dictionary<string, object?> root,
        IReadOnlyList<string> path,
        out object? value)
    {
        var current = root;
        for (var index = 0; index < path.Count; index++)
        {
            if (!current.TryGetValue(path[index], out value))
            {
                return false;
            }

            if (index == path.Count - 1)
            {
                return true;
            }

            if (value is not Dictionary<string, object?> next)
            {
                return false;
            }

            current = next;
        }

        value = null;
        return false;
    }
}
