using System.Text.Json;
using TianShu.AppHost.Configuration;
using Tomlyn.Model;

namespace TianShu.AppHost.Tests;

public sealed class KernelConfigWriteUtilitiesTests
{
    [Fact]
    public void ExtractBatchConfigItems_ShouldCaptureItemValuesAndExplicitMergeStrategy()
    {
        var payload = JsonDocument.Parse(
            """
            {
              "mergeStrategy": "upsert",
              "items": [
                {
                  "keyPath": "features.personality",
                  "value": true
                },
                {
                  "key": "mcp_servers.demo",
                  "value": {
                    "url": "https://example.com"
                  },
                  "mergeStrategy": "replace"
                }
              ]
            }
            """).RootElement.Clone();

        var items = KernelConfigWriteUtilities.ExtractBatchConfigItems(payload);

        Assert.Collection(
            items,
            first =>
            {
                Assert.Equal("features.personality", first.Key);
                Assert.Equal("true", first.ValueJson);
                Assert.Equal("replace", first.MergeStrategy);
            },
            second =>
            {
                Assert.Equal("mcp_servers.demo", second.Key);
                var value = JsonDocument.Parse(second.ValueJson).RootElement;
                Assert.Equal("https://example.com", value.GetProperty("url").GetString());
                Assert.Equal("replace", second.MergeStrategy);
            });
    }

    [Fact]
    public void TryApplyConfigWriteValue_ShouldMergeTomlTablesWhenUpsertRequested()
    {
        var root = new TomlTable
        {
            ["mcp_servers"] = new TomlTable
            {
                ["demo"] = new TomlTable
                {
                    ["url"] = "https://example.com",
                    ["tool_timeout_ms"] = 1500L,
                },
            },
        };
        var value = JsonSerializer.SerializeToElement(new
        {
            env_vars = new[] { "OPENAI_API_KEY" },
        });

        var changed = KernelConfigWriteUtilities.TryApplyConfigWriteValue(
            root,
            ["mcp_servers", "demo"],
            value,
            "upsert",
            out var pathNotFound);

        Assert.True(changed);
        Assert.False(pathNotFound);

        var demo = Assert.IsType<TomlTable>(((TomlTable)root["mcp_servers"]!)["demo"]);
        Assert.Equal("https://example.com", demo["url"]?.ToString());
        Assert.Equal(1500L, demo["tool_timeout_ms"]);
        var envVars = Assert.IsType<TomlArray>(demo["env_vars"]);
        Assert.Equal(["OPENAI_API_KEY"], envVars.Cast<object?>().Select(static item => item?.ToString()));
    }

    [Fact]
    public void ComputeConfigWriteOverriddenMetadata_ShouldDescribeProjectLayer()
    {
        var snapshot = new KernelConfigReadSnapshot(
            Config: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["model"] = "gpt-5.4",
            },
            Origins: new Dictionary<string, object?>(StringComparer.Ordinal),
            Layers: null,
            HasPersistentConfig: true,
            OrderedLayers:
            [
                new KernelConfigReadLayer(
                    new
                    {
                        type = "user",
                        file = "~/.tianshu/tianshu.toml",
                    },
                    "user-v1",
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["model"] = "gpt-5",
                    }),
                new KernelConfigReadLayer(
                    new
                    {
                        type = "project",
                        dotTianShuFolder = ".tianshu",
                    },
                    "project-v2",
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["model"] = "gpt-5.4",
                    }),
            ]);

        var metadata = KernelConfigWriteUtilities.ComputeConfigWriteOverriddenMetadata(
            snapshot,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["model"] = "gpt-5",
            },
            ["model"]);

        Assert.NotNull(metadata);

        var element = JsonSerializer.SerializeToElement(metadata);
        Assert.Equal("Overridden by project config: .tianshu/tianshu.toml", element.GetProperty("message").GetString());
        Assert.Equal("project-v2", element.GetProperty("overridingLayer").GetProperty("version").GetString());
        Assert.Equal("gpt-5.4", element.GetProperty("effectiveValue").GetString());
    }

    [Fact]
    public void ResolveConfigWritePath_ShouldAcceptUserConfigPath()
    {
        var userConfigPath = Path.GetFullPath(TianShuConfigTomlPathResolver.ResolveUserConfigTomlPath());

        var resolved = KernelConfigWriteUtilities.ResolveConfigWritePath(
            userConfigPath,
            cwd: Environment.CurrentDirectory,
            key: "model");

        Assert.Equal(userConfigPath, resolved);
    }

    [Fact]
    public void ResolveConfigWritePath_ShouldRejectNonUserConfigPath()
    {
        var otherPath = Path.Combine(Path.GetTempPath(), $"tianshu-other-{Guid.NewGuid():N}.toml");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            KernelConfigWriteUtilities.ResolveConfigWritePath(
                otherPath,
                cwd: Environment.CurrentDirectory,
                key: "model"));

        Assert.Equal("Only writes to the user config are allowed", exception.Message);
    }

    [Fact]
    public void ResolveCliConfigOverrideBaseDirectory_ShouldPreferCwd()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"tianshu-cwd-{Guid.NewGuid():N}");

        var resolved = KernelConfigWriteUtilities.ResolveCliConfigOverrideBaseDirectory(
            cwd,
            tianShuHome: Path.Combine(Path.GetTempPath(), "fallback-tianshu-home"));

        Assert.Equal(Path.GetFullPath(cwd), resolved);
    }

    [Fact]
    public void ResolveCliConfigOverrideBaseDirectory_ShouldFallBackToTianShuHome()
    {
        var tianShuHome = Path.Combine(Path.GetTempPath(), $"tianshu-home-{Guid.NewGuid():N}");

        var resolved = KernelConfigWriteUtilities.ResolveCliConfigOverrideBaseDirectory(
            cwd: null,
            tianShuHome: tianShuHome);

        Assert.Equal(tianShuHome, resolved);
    }

    [Theory]
    [InlineData("sandbox.type")]
    [InlineData("sandbox_mode")]
    [InlineData("permissions.sandbox_mode")]
    public void IsSandboxModeKey_ShouldAcceptFormalConfigKeys(string key)
    {
        Assert.True(KernelConfigWriteUtilities.IsSandboxModeKey(key));
    }

    [Theory]
    [InlineData("sandboxPolicy.type")]
    [InlineData("sandboxMode")]
    [InlineData("permissions.sandboxMode")]
    public void IsSandboxModeKey_ShouldRejectLegacyCamelCaseConfigKeys(string key)
    {
        Assert.False(KernelConfigWriteUtilities.IsSandboxModeKey(key));
    }

    [Fact]
    public async Task MutatePersistedConfigTableAsync_ShouldRejectVersionConflict()
    {
        var tempDirectory = CreateTempDirectory();
        var configPath = Path.Combine(tempDirectory, "config.toml");
        await File.WriteAllTextAsync(configPath, "model = \"gpt-5\"\n");

        try
        {
            var exception = await Assert.ThrowsAsync<KernelConfigWriteException>(() =>
                KernelConfigWriteUtilities.MutatePersistedConfigTableAsync(
                    new SemaphoreSlim(1, 1),
                    configPath,
                    expectedVersion: "stale-version",
                    static _ => { },
                    (new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase)),
                    new Dictionary<string, bool>(StringComparer.Ordinal),
                    CancellationToken.None));

            Assert.Equal(-32600, exception.Code);
            Assert.Equal("configVersionConflict", JsonSerializer.SerializeToElement(exception.DataPayload).GetProperty("errorCode").GetString());
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task MutatePersistedConfigTableAsync_ShouldRejectRequirementTypeMismatch()
    {
        var tempDirectory = CreateTempDirectory();
        var configPath = Path.Combine(tempDirectory, "config.toml");

        try
        {
            var exception = await Assert.ThrowsAsync<KernelConfigWriteException>(() =>
                KernelConfigWriteUtilities.MutatePersistedConfigTableAsync(
                    new SemaphoreSlim(1, 1),
                    configPath,
                    expectedVersion: null,
                    static root => KernelConfigPersistenceUtilities.SetTomlPathValue(root, ["features", "personality"], "yes"),
                    (new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase)),
                    new Dictionary<string, bool>(StringComparer.Ordinal)
                    {
                        ["personality"] = true,
                    },
                    CancellationToken.None));

            Assert.Equal(-32602, exception.Code);
            Assert.Equal("configValidationError", JsonSerializer.SerializeToElement(exception.DataPayload).GetProperty("errorCode").GetString());
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tianshu-config-write-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, recursive: true);
    }
}
