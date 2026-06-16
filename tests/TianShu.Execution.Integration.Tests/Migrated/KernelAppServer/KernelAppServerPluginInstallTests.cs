using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerPluginInstallTests
{
    [Fact]
    public async Task RunAsync_ShouldInstallPluginAndSurfaceCapabilitiesAcrossProtocolEndpoints()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var kernelHome = Path.Combine(root, ".kernel-home");
        var storePath = Path.Combine(root, "threads.json");
        var originalCurrentDirectory = Environment.CurrentDirectory;
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");

        try
        {
            Environment.CurrentDirectory = root;
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", kernelHome);
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(tianShuHome);
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                "[plugins]\nenabled = true\n");
            WriteMarketplacePlugin(root, "debug", "sample");

            var input = string.Join(
                Environment.NewLine,
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"plugin/install\",\"params\":{\"marketplacePath\":\"__MARKET__\",\"pluginName\":\"sample\",\"cwd\":\"__ROOT__\"}}".Replace("__MARKET__", NormalizePath(Path.Combine(root, ".agents", "plugins", "marketplace.json")), StringComparison.Ordinal).Replace("__ROOT__", NormalizePath(root), StringComparison.Ordinal),
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"skills/list\",\"params\":{\"cwds\":[\"__ROOT__\"],\"forceReload\":true}}".Replace("__ROOT__", NormalizePath(root), StringComparison.Ordinal),
                "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"mcpserverstatus/list\",\"params\":{}}",
                "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"app/list\",\"params\":{\"forceRefetch\":true}}");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var installMessage = messages.SingleOrDefault(x => x.RootElement.TryGetProperty("id", out var idElement)
                    && idElement.ValueKind == JsonValueKind.Number
                    && idElement.GetInt32() == 1);
                Assert.NotNull(installMessage);
                Assert.True(installMessage!.RootElement.TryGetProperty("result", out var installResult), writer.ToString());
                Assert.True(installResult.TryGetProperty("appsNeedingAuth", out var appsNeedingAuth), writer.ToString());
                Assert.Equal(JsonValueKind.Array, appsNeedingAuth.ValueKind);
                Assert.Empty(appsNeedingAuth.EnumerateArray());

                var skills = messages.Single(x => IsResponseId(x.RootElement, 2))
                    .RootElement
                    .GetProperty("result")
                    .GetProperty("data")[0]
                    .GetProperty("skills")
                    .EnumerateArray()
                    .Select(item => item.GetProperty("name").GetString())
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .ToArray();
                Assert.Contains("sample:search", skills);

                var mcpNames = messages.Single(x => IsResponseId(x.RootElement, 3))
                    .RootElement
                    .GetProperty("result")
                    .GetProperty("data")
                    .EnumerateArray()
                    .Select(item => item.GetProperty("name").GetString())
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .ToArray();
                Assert.Contains("sample", mcpNames);

                var apps = messages.Single(x => IsResponseId(x.RootElement, 4))
                    .RootElement
                    .GetProperty("result")
                    .GetProperty("data")
                    .EnumerateArray()
                    .ToArray();
                Assert.Contains(apps, static item => item.GetProperty("id").GetString() == "connector_example");
                var connectorApp = apps.Single(static item => item.GetProperty("id").GetString() == "connector_example");
                Assert.Equal(["sample"], connectorApp.GetProperty("pluginDisplayNames").EnumerateArray().Select(static item => item.GetString()).ToArray());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            DeleteDirectory(root);
        }
    }

    private static void WriteMarketplacePlugin(string workspace, string marketplaceName, string pluginName)
    {
        var marketplaceRoot = Path.Combine(workspace, ".agents", "plugins");
        var pluginRoot = Path.Combine(marketplaceRoot, pluginName);
        WriteInstalledPlugin(pluginRoot, pluginName);
        WriteFile(
            Path.Combine(marketplaceRoot, "marketplace.json"),
            $$"""
            {
              "name": "{{marketplaceName}}",
              "plugins": [
                {
                  "name": "{{pluginName}}",
                  "source": {
                    "source": "local",
                    "path": "./{{pluginName}}"
                  }
                }
              ]
            }
            """);
    }

    private static void WriteInstalledPlugin(string pluginRoot, string pluginName)
    {
        WriteFile(
            Path.Combine(pluginRoot, ".tianshu-plugin", "plugin.json"),
            $$"""{"name":"{{pluginName}}"}""" );
        WriteFile(
            Path.Combine(pluginRoot, "skills", "search", "SKILL.md"),
            "---\ndescription: sample search\n---\n");
        WriteFile(
            Path.Combine(pluginRoot, ".mcp.json"),
            """
            {
              "mcpServers": {
                "sample": {
                  "command": "rg",
                  "args": ["--version"]
                }
              }
            }
            """);
        WriteFile(
            Path.Combine(pluginRoot, ".app.json"),
            """
            {
              "apps": {
                "example": {
                  "id": "connector_example"
                }
              }
            }
            """);
    }

    private static bool IsResponseId(JsonElement element, int id)
        => element.TryGetProperty("id", out var idElement)
           && idElement.ValueKind == JsonValueKind.Number
           && idElement.GetInt32() == id
           && element.TryGetProperty("result", out _);

    private static string NormalizePath(string path)
        => path.Replace("\\", "/", StringComparison.Ordinal);

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var normalizedContent = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        File.WriteAllText(path, normalizedContent);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShuTests", Guid.NewGuid().ToString("N"));
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
