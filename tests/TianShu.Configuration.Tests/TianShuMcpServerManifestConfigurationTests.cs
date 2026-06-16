namespace TianShu.Configuration.Tests;

public sealed class TianShuMcpServerManifestConfigurationTests
{
    [Fact]
    public void Load_ScansMcpServerPackageManifests()
    {
        using var temp = TempTianShuHome.Create();
        WriteManifest(Path.Combine(temp.Root, "modules", "mcp-servers", "builtin", "server.toml"), "builtin", "docs");
        WriteManifest(Path.Combine(temp.Root, "modules", "mcp-servers", "company", "server.toml"), "company", "company_docs");

        var projection = new TianShuMcpServerManifestConfiguration().Load(temp.Root);

        Assert.Equal(2, projection.Files.Count);
        Assert.Contains(projection.Files, static file => file.DisplayName.EndsWith(Path.Combine("builtin", "server.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Files, static file => file.DisplayName.EndsWith(Path.Combine("company", "server.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Equal("builtin", projection.SelectedPackage?.Id);
        Assert.Equal("docs", Assert.Single(projection.SelectedPackage!.Servers).Id);
    }

    [Fact]
    public void SavePackage_UpdatesServerManifestWithoutTouchingTianShuToml()
    {
        using var temp = TempTianShuHome.Create();
        var configPath = Path.Combine(temp.Root, "tianshu.toml");
        var manifestPath = Path.Combine(temp.Root, "modules", "mcp-servers", "builtin", "server.toml");
        File.WriteAllText(configPath, "model = \"gpt-test\"\n");
        WriteManifest(manifestPath, "builtin", "docs");

        var configuration = new TianShuMcpServerManifestConfiguration();
        var package = configuration.Load(temp.Root, manifestPath).SelectedPackage!;
        package.Servers =
        [
            .. package.Servers,
            new McpServerManifestValue
            {
                Id = "config_gui_smoke",
                DisplayName = "ConfigGUI Smoke MCP",
                Enabled = true,
                Required = true,
                Transport = "http",
                Url = "https://example.com/mcp",
                BearerTokenEnvVar = "SMOKE_MCP_TOKEN",
                StartupTimeoutMs = 5000,
                ToolTimeoutMs = 60000,
                EnabledTools = ["search"],
            },
        ];

        configuration.SavePackage(manifestPath, package);

        var saved = File.ReadAllText(manifestPath);
        Assert.Contains("id = \"config_gui_smoke\"", saved, StringComparison.Ordinal);
        Assert.Contains("display_name = \"ConfigGUI Smoke MCP\"", saved, StringComparison.Ordinal);
        Assert.Contains("url = \"https://example.com/mcp\"", saved, StringComparison.Ordinal);
        Assert.Contains("bearer_token_env_var = \"SMOKE_MCP_TOKEN\"", saved, StringComparison.Ordinal);
        Assert.Equal("model = \"gpt-test\"\n", File.ReadAllText(configPath));
    }

    [Fact]
    public void CreateCopyAndDeletePackage_OnlyWritesMcpServersDirectory()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuMcpServerManifestConfiguration();

        var createdPath = configuration.CreatePackage(temp.Root, "company-mcp");
        Assert.Equal(Path.Combine(temp.Root, "modules", "mcp-servers", "company-mcp", "server.toml"), createdPath);
        Assert.True(File.Exists(createdPath));

        var copiedPath = configuration.CopyPackage(temp.Root, createdPath, "company-mcp-copy");
        Assert.Equal(Path.Combine(temp.Root, "modules", "mcp-servers", "company-mcp-copy", "server.toml"), copiedPath);
        Assert.True(File.Exists(copiedPath));

        configuration.DeletePackage(temp.Root, copiedPath);
        Assert.False(File.Exists(copiedPath));
    }

    [Fact]
    public void CreatePackage_RejectsPathsOutsideMcpServersRoot()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuMcpServerManifestConfiguration();

        Assert.Throws<InvalidOperationException>(() => configuration.CreatePackage(temp.Root, "..\\outside"));
        Assert.Throws<InvalidOperationException>(() => configuration.CreatePackage(temp.Root, "nested\\server"));
    }

    private static void WriteManifest(string path, string packageId, string serverId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $$"""
            id = "{{packageId}}"
            display_name = "{{packageId}}"
            enabled = true
            type = "builtin"
            priority = 0

            [[servers]]
            id = "{{serverId}}"
            display_name = "{{serverId}}"
            enabled = true
            required = false
            transport = "stdio"
            command = "npx"
            args = ["-y", "@example/{{serverId}}"]
            cwd = "."
            env_vars = ["{{serverId}}_TOKEN"]
            startup_timeout_ms = 10000
            tool_timeout_ms = 120000
            """);
    }

    private sealed class TempTianShuHome : IDisposable
    {
        private TempTianShuHome(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TempTianShuHome Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"tianshu-mcp-server-manifest-config-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new TempTianShuHome(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

