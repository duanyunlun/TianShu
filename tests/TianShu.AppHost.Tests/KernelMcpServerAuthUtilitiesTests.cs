using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tests;

public sealed class McpServerAuthUtilitiesTests
{
    [Fact]
    public void ResolveMcpServerAuthStatus_ShouldReturnOauthWhenOauthTokenConfigured()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mcp_servers.demo.oauth_access_token"] = "\"secret\"",
        };

        var status = McpServerAuthUtilities.ResolveMcpServerAuthStatus("demo", values);

        Assert.Equal("oauth", status);
    }

    [Fact]
    public void ResolveMcpServerAuthStatus_ShouldReturnNotLoggedInWhenEndpointConfigured()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mcp_servers.demo.url"] = "\"https://example.com/mcp\"",
        };

        var status = McpServerAuthUtilities.ResolveMcpServerAuthStatus("demo", values);

        Assert.Equal("not_logged_in", status);
    }

    [Fact]
    public async Task ListMcpServerNamesAsync_ShouldMergeTomlAndScopedConfigValues()
    {
        var tianShuHome = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [mcp_servers.demo]
                url = "https://example.com/demo"
                """);

            var names = await McpServerAuthUtilities.ListMcpServerNamesAsync(
                tianShuHome,
                static cancellationToken => Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["mcp_servers.extra.url"] = "\"https://example.com/extra\"",
                }),
                CancellationToken.None);

            Assert.Equal(["demo", "extra"], names);
        }
        finally
        {
            DeleteDirectory(tianShuHome);
        }
    }

    [Fact]
    public async Task ListMcpServerNamesAsync_ShouldIncludeMcpServerPackageManifests()
    {
        var tianShuHome = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(tianShuHome, "modules", "mcp-servers", "company"));
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "modules", "mcp-servers", "company", "server.toml"),
                """
                id = "company"
                enabled = true

                [[servers]]
                id = "docs"
                enabled = true
                url = "https://docs.example.com/mcp"
                """);

            var names = await McpServerAuthUtilities.ListMcpServerNamesAsync(
                tianShuHome,
                static cancellationToken => Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
                CancellationToken.None);

            Assert.Equal(["docs"], names);
        }
        finally
        {
            DeleteDirectory(tianShuHome);
        }
    }

    [Fact]
    public async Task ResolveMcpServerAuthorizationUrlAsync_ShouldPreferEffectiveConfigValues()
    {
        var tianShuHome = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [mcp_servers.demo]
                url = "https://fallback.example.com/mcp"
                """);

            var url = await McpServerAuthUtilities.ResolveMcpServerAuthorizationUrlAsync(
                "demo",
                tianShuHome,
                static cancellationToken => Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["mcp_servers.demo.oauth_authorization_url"] = "\"https://config.example.com/mcp\"",
                }),
                CancellationToken.None);

            Assert.Equal("https://config.example.com/mcp", url);
        }
        finally
        {
            DeleteDirectory(tianShuHome);
        }
    }

    [Fact]
    public async Task ResolveMcpServerAuthorizationUrlAsync_ShouldFallbackToTomlSection()
    {
        var tianShuHome = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [mcp_servers."demo-server"]
                oauth_authorization_url = "https://toml.example.com/oauth"
                """);

            var url = await McpServerAuthUtilities.ResolveMcpServerAuthorizationUrlAsync(
                "demo-server",
                tianShuHome,
                static cancellationToken => Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
                CancellationToken.None);

            Assert.Equal("https://toml.example.com/oauth", url);
        }
        finally
        {
            DeleteDirectory(tianShuHome);
        }
    }

    [Fact]
    public async Task ResolveMcpServerAuthorizationUrlAsync_ShouldFallbackToMcpServerPackageManifest()
    {
        var tianShuHome = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(tianShuHome, "modules", "mcp-servers", "company"));
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "modules", "mcp-servers", "company", "server.toml"),
                """
                id = "company"
                enabled = true

                [[servers]]
                id = "docs"
                enabled = true
                url = "https://manifest.example.com/mcp"
                """);

            var url = await McpServerAuthUtilities.ResolveMcpServerAuthorizationUrlAsync(
                "docs",
                tianShuHome,
                static cancellationToken => Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
                CancellationToken.None);

            Assert.Equal("https://manifest.example.com/mcp", url);
        }
        finally
        {
            DeleteDirectory(tianShuHome);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShu.McpAuth." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

