using System.Net.Http;
using System.Text;
using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI.Tests;

public sealed class OpenAiAppCatalogCompatibilityAdapterTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "TianShu.Provider.OpenAI.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TryReadConfiguredBaseUrl_WhenTomlContainsQuotedValueAndComment_ReturnsNormalizedBaseUrl()
    {
        var configText =
            """
            # comment
            provider = "openai"
            chatgpt_base_url = " https://chatgpt.example.com/api/ "
            """;

        var baseUrl = OpenAiAppCatalogCompatibilityAdapter.TryReadConfiguredBaseUrl(configText);

        Assert.Equal("https://chatgpt.example.com/api/", baseUrl);
    }

    [Fact]
    public void TryReadConfiguredBaseUrl_WhenKeyIsMissing_ReturnsNull()
    {
        var configText =
            """
            provider = "openai"
            base_url = "https://api.example.com/v1"
            """;

        Assert.Null(OpenAiAppCatalogCompatibilityAdapter.TryReadConfiguredBaseUrl(configText));
    }

    [Fact]
    public async Task TryReadAuthContextAsync_WhenAuthJsonContainsDirectAccountId_ReturnsAccessTokenAndAccountId()
    {
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(
            Path.Combine(tempRoot, "auth.json"),
            """
            {
              "tokens": {
                "access_token": "token-direct",
                "account_id": "acct-direct"
              }
            }
            """);

        var auth = await OpenAiAppCatalogCompatibilityAdapter.TryReadAuthContextAsync(tempRoot, CancellationToken.None);

        Assert.NotNull(auth);
        Assert.Equal("token-direct", auth?.AccessToken);
        Assert.Equal("acct-direct", auth?.AccountId);
    }

    [Fact]
    public async Task TryReadAuthContextAsync_WhenOnlyIdTokenContainsClaim_FallsBackToJwtPayload()
    {
        Directory.CreateDirectory(tempRoot);
        var idToken = BuildIdToken("""{"https://api.openai.com/auth":{"chatgpt_account_id":"acct-from-jwt"}}""");
        await File.WriteAllTextAsync(
            Path.Combine(tempRoot, "auth.json"),
            $$"""
            {
              "tokens": {
                "access_token": "token-jwt",
                "id_token": "{{idToken}}"
              }
            }
            """);

        var auth = await OpenAiAppCatalogCompatibilityAdapter.TryReadAuthContextAsync(tempRoot, CancellationToken.None);

        Assert.NotNull(auth);
        Assert.Equal("token-jwt", auth?.AccessToken);
        Assert.Equal("acct-from-jwt", auth?.AccountId);
    }

    [Fact]
    public async Task TryReadAuthContextAsync_WhenAuthJsonIsInvalid_ReturnsNull()
    {
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(Path.Combine(tempRoot, "auth.json"), "{ invalid json");

        var auth = await OpenAiAppCatalogCompatibilityAdapter.TryReadAuthContextAsync(tempRoot, CancellationToken.None);

        Assert.Null(auth);
    }

    [Fact]
    public void BuildCatalogUri_WhenBaseUrlIsNull_UsesDefaultBaseUrl()
    {
        var uri = OpenAiAppCatalogCompatibilityAdapter.BuildCatalogUri(null, "backend-api/connector-bridge/apps");

        Assert.Equal("https://chatgpt.com/backend-api/connector-bridge/apps", uri.ToString());
    }

    [Fact]
    public void ApplyAuthHeaders_WhenInvoked_WritesBearerAccountIdAndAcceptHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        OpenAiAppCatalogCompatibilityAdapter.ApplyAuthHeaders(request.Headers, "token-123", "acct-456");

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("token-123", request.Headers.Authorization?.Parameter);
        Assert.Equal("acct-456", Assert.Single(request.Headers.GetValues(OpenAiAppCatalogCompatibilityAdapter.ChatGptAccountIdHeaderName)));
        Assert.Contains(request.Headers.Accept, static header => header.MediaType == "application/json");
    }

    [Theory]
    [InlineData("connector_openai_demo", true)]
    [InlineData("connector_68de829bf7648191acd70a907364c67c", true)]
    [InlineData("connector_custom_allowed", false)]
    public void IsDisallowedConnector_ReturnsExpectedResult(string connectorId, bool expected)
        => Assert.Equal(expected, OpenAiAppCatalogCompatibilityAdapter.IsDisallowedConnector(connectorId));

    [Theory]
    [InlineData("connector_2128aebfecb84f64a069897515042a44", true)]
    [InlineData("connector_unknown", false)]
    public void IsToolSuggestDiscoverableConnector_ReturnsExpectedResult(string connectorId, bool expected)
        => Assert.Equal(expected, OpenAiAppCatalogCompatibilityAdapter.IsToolSuggestDiscoverableConnector(connectorId));

    [Fact]
    public void BuildConnectorApprovalSessionKey_WhenInputsPresent_ReturnsCanonicalKey()
    {
        var key = OpenAiAppCatalogCompatibilityAdapter.BuildConnectorApprovalSessionKey(" connector-demo ", " docs ");

        Assert.Equal("codex_apps::connector-demo::docs", key);
    }

    [Fact]
    public void BuildConnectorApprovalSessionKey_WhenInputsMissing_ReturnsNull()
    {
        Assert.Null(OpenAiAppCatalogCompatibilityAdapter.BuildConnectorApprovalSessionKey("connector-demo", null));
        Assert.Null(OpenAiAppCatalogCompatibilityAdapter.BuildConnectorApprovalSessionKey(null, "docs"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
        catch
        {
            // 测试清理不影响断言结果。
        }
    }

    private static string BuildIdToken(string payloadJson)
    {
        var header = Base64UrlEncode("""{"alg":"none","typ":"JWT"}""");
        var payload = Base64UrlEncode(payloadJson);
        return $"{header}.{payload}.signature";
    }

    private static string Base64UrlEncode(string text)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
