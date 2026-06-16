using System.Collections;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using TianShu.AppHost.Tools;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.AppHost.Tests;

[Collection("EnvironmentVariables")]
public sealed class KernelPluginsManagerTests
{
    [Fact]
    public async Task InstallAsync_ShouldInstallPluginAndExposeCapabilitiesWhenFeatureEnabled()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                "[plugins]\nenabled = true\n");
            WriteMarketplacePlugin(workspace, "debug", "sample");

            var installManager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var install = await installManager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None);

            Assert.Equal("sample@debug", install.PluginKey);
            var installedRoot = Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample", "local");
            Assert.Equal(Path.GetFullPath(installedRoot), Path.GetFullPath(install.InstalledPath));
            Assert.True(File.Exists(Path.Combine(installedRoot, ".tianshu-plugin", "plugin.json")));
            Assert.True(File.Exists(Path.Combine(installedRoot, "skills", "search", "SKILL.md")));

            var workspaceConfigPath = Path.Combine(workspace, ".tianshu", "tianshu.toml");
            var toml = Toml.ToModel(await File.ReadAllTextAsync(workspaceConfigPath, CancellationToken.None)) as TomlTable;
            Assert.NotNull(toml);
            var plugins = Assert.IsType<TomlTable>(toml!["plugins"]);
            var installed = Assert.IsType<TomlTable>(plugins["installed"]);
            var sample = Assert.IsType<TomlTable>(installed["sample@debug"]);
            Assert.True((bool)sample["enabled"]);

            var manager = new KernelPluginsManager(
                _ => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["plugins.enabled"] = "true",
                    ["plugins.installed.sample@debug.enabled"] = "true",
                }),
                tianShuHome: tianShuHome);
            manager.ClearCache();
            var skillRoots = await manager.GetEffectiveSkillRootsAsync(CancellationToken.None);
            var skillRoot = Assert.Single(skillRoots);
            Assert.Equal("sample", skillRoot.Namespace);
            Assert.Equal(Path.GetFullPath(Path.Combine(installedRoot, "skills")), skillRoot.RootPath);

            var mcpServers = await manager.GetEffectiveMcpServersAsync(CancellationToken.None);
            var server = Assert.Single(mcpServers);
            Assert.Equal("sample", server.Key);
            Assert.Equal("rg", server.Value.Command);
            Assert.Equal(Path.GetFullPath(Path.Combine(installedRoot, "workspace")), server.Value.Cwd);
            Assert.Equal(TimeSpan.FromSeconds(5), server.Value.StartupTimeout);
            Assert.Equal(TimeSpan.FromSeconds(30), server.Value.ToolTimeout);
            Assert.Equal(["rg"], server.Value.EnabledTools);
            Assert.Equal(["blocked"], server.Value.DisabledTools);

            var appIds = await manager.GetEffectiveAppIdsAsync(CancellationToken.None);
            Assert.Equal(new[] { "connector_example" }, appIds);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetEffectiveSkillRootsAsync_ShouldIgnoreLegacyPluginConfigOverrideKeys()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var installedRoot = Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample", "local");
        Directory.CreateDirectory(tianShuHome);
        WriteInstalledPlugin(installedRoot, "sample");

        try
        {
            var legacyFeatureManager = new KernelPluginsManager(
                _ => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["features.plugins"] = "true",
                    ["plugins.installed.sample@debug.enabled"] = "true",
                }),
                tianShuHome: tianShuHome);

            Assert.Empty(await legacyFeatureManager.GetEffectiveSkillRootsAsync(CancellationToken.None));

            var legacyStateManager = new KernelPluginsManager(
                _ => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["plugins.enabled"] = "true",
                    ["plugins.sample@debug.enabled"] = "true",
                }),
                tianShuHome: tianShuHome);

            Assert.Empty(await legacyStateManager.GetEffectiveSkillRootsAsync(CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetEffectiveAppPluginDisplayNamesAsync_ShouldAggregateManifestNamesPerApp()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true
                [plugins.installed."sample@debug"]
                enabled = true

                [plugins.installed."other@debug"]
                enabled = true
                """);

            WriteInstalledPlugin(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample", "local"), "sample");
            WriteInstalledPlugin(Path.Combine(tianShuHome, "plugins", "cache", "debug", "other", "local"), "other");

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var pluginDisplayNames = await manager.GetEffectiveAppPluginDisplayNamesAsync(CancellationToken.None);

            Assert.True(pluginDisplayNames.TryGetValue("connector_example", out var names));
            Assert.Equal(["other", "sample"], names);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task ListMarketplacesAsync_ShouldDiscoverHomeAndRepoMarketplacesWithEnabledState()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true
                [plugins.installed."home-plugin@tianshu-curated"]
                enabled = true

                [plugins.installed."repo-plugin@debug"]
                enabled = false
                """);
            WriteMarketplacePlugin(tianShuHome, "tianshu-curated", "home-plugin");
            WriteMarketplacePlugin(workspace, "debug", "repo-plugin");

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var marketplaces = await manager.ListMarketplacesAsync([workspace], CancellationToken.None);

            var homeMarketplace = Assert.Single(marketplaces.Where(x => x.Name == "tianshu-curated"));
            Assert.Equal(NormalizePath(Path.Combine(tianShuHome, ".agents", "plugins", "marketplace.json")), NormalizePath(homeMarketplace.MarketplacePath));
            var homePlugin = Assert.Single(homeMarketplace.Plugins);
            Assert.Equal("home-plugin", homePlugin.Name);
            Assert.True(homePlugin.Enabled);
            Assert.Equal("local", homePlugin.Source.Type);
            Assert.Equal(NormalizePath(Path.Combine(tianShuHome, ".agents", "plugins", "home-plugin")), NormalizePath(homePlugin.Source.Path));

            var repoMarketplace = Assert.Single(marketplaces.Where(x => x.Name == "debug"));
            Assert.Equal(NormalizePath(Path.Combine(workspace, ".agents", "plugins", "marketplace.json")), NormalizePath(repoMarketplace.MarketplacePath));
            var repoPlugin = Assert.Single(repoMarketplace.Plugins);
            Assert.Equal("repo-plugin", repoPlugin.Name);
            Assert.False(repoPlugin.Enabled);
            Assert.Equal("local", repoPlugin.Source.Type);
            Assert.Equal(NormalizePath(Path.Combine(workspace, ".agents", "plugins", "repo-plugin")), NormalizePath(repoPlugin.Source.Path));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ListAsync_WhenForceRemoteSyncRequested_ReturnsRemoteSyncError()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true
                """);
            WriteMarketplacePlugin(workspace, "debug", "repo-plugin");

            var manager = new KernelPluginsManager(
                syncRemotePluginStatesAsync: _ => throw new InvalidOperationException("chatgpt authentication required"),
                tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace], ForceRemoteSync: true), CancellationToken.None);

            var marketplace = Assert.Single(result.Marketplaces);
            Assert.Equal("debug", marketplace.Name);
            Assert.Equal("chatgpt authentication required", result.RemoteSyncError);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ListAsync_WhenRemoteMarketplaceSyncIsConfigured_DownloadsCachesAndListsMarketplace()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var url = "https://plugins.example.test/marketplace.json";
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var marketplaceBytes = Encoding.UTF8.GetBytes(
                """
                {
                  "name": "remote-debug",
                  "plugins": [
                    {
                      "name": "remote-plugin",
                      "source": {
                        "source": "local",
                        "path": "./remote-plugin"
                      }
                    }
                  ]
                }
                """);
            var marketplaceSha256 = Convert.ToHexString(SHA256.HashData(marketplaceBytes)).ToLowerInvariant();
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                allow_remote_marketplace_sources = true

                [plugins.remote_marketplaces.lab]
                enabled = true
                url = "{{url}}"
                sha256 = "{{marketplaceSha256}}"
                """);
            var handler = new ArchiveHttpMessageHandler(url, marketplaceBytes);
            var manager = new KernelPluginsManager(tianShuHome: tianShuHome, httpClient: new HttpClient(handler));

            var result = await manager.ListAsync(new KernelPluginListRequest([], ForceRemoteSync: true), CancellationToken.None);

            Assert.Null(result.RemoteSyncError);
            Assert.Equal(1, handler.RequestCount);
            var marketplace = Assert.Single(result.Marketplaces);
            Assert.Equal("remote-debug", marketplace.Name);
            Assert.Equal(
                NormalizePath(Path.Combine(tianShuHome, "plugins", "marketplaces", "lab", "marketplace.json")),
                NormalizePath(marketplace.MarketplacePath));
            var plugin = Assert.Single(marketplace.Plugins);
            Assert.Equal("remote-plugin", plugin.Name);
            Assert.Equal("not-declared", plugin.Source.IntegrityStatus);
            Assert.Equal(
                NormalizePath(Path.Combine(tianShuHome, "plugins", "marketplaces", "lab", "remote-plugin")),
                NormalizePath(plugin.Source.Path));
            Assert.True(File.Exists(Path.Combine(tianShuHome, "plugins", "marketplaces", "lab", "marketplace.json")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ListAsync_WhenRemoteMarketplaceShaMismatches_KeepsCachedMarketplaceAndReportsError()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var url = "https://plugins.example.test/marketplace.json";
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var cachedMarketplacePath = Path.Combine(tianShuHome, "plugins", "marketplaces", "lab", "marketplace.json");
            WriteFile(
                cachedMarketplacePath,
                """
                {
                  "name": "cached-debug",
                  "plugins": [
                    {
                      "name": "cached-plugin",
                      "source": {
                        "source": "local",
                        "path": "./cached-plugin"
                      }
                    }
                  ]
                }
                """);
            var marketplaceBytes = Encoding.UTF8.GetBytes(
                """
                {
                  "name": "remote-debug",
                  "plugins": []
                }
                """);
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                allow_remote_marketplace_sources = true

                [plugins.remote_marketplaces.lab]
                enabled = true
                url = "{{url}}"
                sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
                """);
            var handler = new ArchiveHttpMessageHandler(url, marketplaceBytes);
            var manager = new KernelPluginsManager(tianShuHome: tianShuHome, httpClient: new HttpClient(handler));

            var result = await manager.ListAsync(new KernelPluginListRequest([], ForceRemoteSync: true), CancellationToken.None);

            Assert.Equal(1, handler.RequestCount);
            Assert.Contains("integrity check failed", result.RemoteSyncError, StringComparison.Ordinal);
            var marketplace = Assert.Single(result.Marketplaces);
            Assert.Equal("cached-debug", marketplace.Name);
            Assert.Equal("cached-plugin", Assert.Single(marketplace.Plugins).Name);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceDeclaresSha256_VerifiesDirectoryDigest()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(Path.Combine(tianShuHome, "tianshu.toml"), "[plugins]\nenabled = true\n");
            var pluginRoot = Path.Combine(workspace, ".agents", "plugins", "sample");
            WriteInstalledPlugin(pluginRoot, "sample");
            var sha256 = ComputeDirectorySha256(pluginRoot);
            WriteMarketplacePluginWithSha(workspace, "debug", "sample", sha256);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal(sha256, plugin.Source.Sha256);
            Assert.Equal("verified", plugin.Source.IntegrityStatus);

            var install = await manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None);

            Assert.True(Directory.Exists(install.InstalledPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceSha256Mismatches_RejectsInstall()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(Path.Combine(tianShuHome, "tianshu.toml"), "[plugins]\nenabled = true\n");
            WriteInstalledPlugin(Path.Combine(workspace, ".agents", "plugins", "sample"), "sample");
            WriteMarketplacePluginWithSha(workspace, "debug", "sample", new string('0', 64));

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);
            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("mismatch", plugin.Source.IntegrityStatus);

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("integrity check failed", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceSignerIsTrusted_AllowsInstallAndProjectsTrustStatus()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]
                """);
            WriteMarketplacePluginWithSigner(workspace, "debug", "sample", "lab-signer");

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("lab-signer", plugin.Source.Signer);
            Assert.Equal("trusted", plugin.Source.TrustStatus);

            var install = await manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None);

            Assert.True(Directory.Exists(install.InstalledPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceSignerIsUntrusted_RejectsInstall()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["other-signer"]
                """);
            WriteMarketplacePluginWithSigner(workspace, "debug", "sample", "lab-signer");

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);
            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("untrusted", plugin.Source.TrustStatus);

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("not trusted", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenTrustPolicyRequiresSignerAndMarketplaceOmitsSigner_RejectsInstall()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                require_signer = true
                trusted_signers = ["lab-signer"]
                """);
            WriteMarketplacePlugin(workspace, "debug", "sample");

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);
            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("missing-required", plugin.Source.TrustStatus);

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("signer is required", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceSignatureIsValid_VerifiesSignatureAndInstalls()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var signedPlugin = CreateSignedMarketplacePlugin(workspace, "debug", "sample", "lab-signer");
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "{{signedPlugin.PublicKeySha256}}"
                """);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("verified", plugin.Source.IntegrityStatus);
            Assert.Equal("trusted", plugin.Source.TrustStatus);
            Assert.Equal("verified", plugin.Source.SignatureStatus);

            var install = await manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None);

            Assert.True(Directory.Exists(install.InstalledPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceSignatureUsesConfiguredSignerPublicKey_VerifiesSignatureAndInstalls()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var signedPlugin = CreateSignedMarketplacePlugin(workspace, "debug", "sample", "lab-signer", includePublicKey: false);
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key = "{{signedPlugin.PublicKeyBase64}}"
                public_key_sha256 = "{{signedPlugin.PublicKeySha256}}"
                """);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("verified", plugin.Source.SignatureStatus);

            var install = await manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None);

            Assert.True(Directory.Exists(install.InstalledPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceDeclaresTransparencyLogProof_RejectsInstall()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var signedPlugin = CreateSignedMarketplacePlugin(
                workspace,
                "debug",
                "sample",
                "lab-signer",
                extraIntegrityJson:
                """
                    "transparency_log": { "kind": "rekor", "log_index": 1 },
                """);
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "{{signedPlugin.PublicKeySha256}}"
                """);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("transparency-log-unsupported", plugin.Source.SignatureStatus);

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("signature check failed", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceTransparencyLogIsNotTrusted_RejectsInstall()
    {
        await AssertMarketplaceTransparencyLogProofRejectedAsync(
            additionalTrustToml: string.Empty,
            expectedSignatureStatus: "transparency-log-untrusted");
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceTransparencyLogTrustMissingPin_RejectsInstall()
    {
        await AssertMarketplaceTransparencyLogProofRejectedAsync(
            additionalTrustToml:
            """

                [plugins.marketplace_trust.transparency_logs.rekor]
                enabled = true
            """,
            expectedSignatureStatus: "transparency-log-missing-public-key-pin");
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceTransparencyLogTrustExistsButVerifierUnsupported_RejectsInstall()
    {
        await AssertMarketplaceTransparencyLogProofRejectedAsync(
            additionalTrustToml:
            """

                [plugins.marketplace_trust.transparency_logs.rekor]
                enabled = true
                public_key_sha256 = "log-public-key-pin"
            """,
            expectedSignatureStatus: "transparency-log-unsupported");
    }

    [Fact]
    public async Task InstallAsync_WhenTrustedMarketplaceTransparencyLogProofIsIncomplete_RejectsInstall()
    {
        await AssertMarketplaceTransparencyLogProofRejectedAsync(
            additionalTrustToml:
            """

                [plugins.marketplace_trust.transparency_logs.rekor]
                enabled = true
                public_key_sha256 = "log-public-key-pin"
            """,
            expectedSignatureStatus: "transparency-log-proof-incomplete",
            transparencyLogJson:
            """
                    "transparency_log": {
                      "kind": "rekor",
                      "log_id": "rekor",
                      "log_index": 1,
                      "tree_size": 8,
                      "root_hash": "root-hash",
                      "inclusion_proof": ["node-a", "node-b"]
                    },
            """);
    }

    [Fact]
    public async Task InstallAsync_WhenTrustedMarketplaceTransparencyLogCheckpointEnvelopeIsIncomplete_RejectsInstall()
    {
        await AssertMarketplaceTransparencyLogProofRejectedAsync(
            additionalTrustToml:
            """

                [plugins.marketplace_trust.transparency_logs.rekor]
                enabled = true
                public_key_sha256 = "log-public-key-pin"
            """,
            expectedSignatureStatus: "transparency-log-checkpoint-incomplete",
            transparencyLogJson:
            """
                    "transparency_log": {
                      "kind": "rekor",
                      "log_id": "rekor",
                      "log_index": 1,
                      "tree_size": 8,
                      "root_hash": "root-hash",
                      "checkpoint": {
                        "origin": "rekor",
                        "tree_size": 8,
                        "root_hash": "root-hash"
                      },
                      "inclusion_proof": ["node-a", "node-b"]
                    },
            """);
    }

    [Fact]
    public async Task InstallAsync_WhenTrustedMarketplaceTransparencyLogCheckpointEnvelopeIsComplete_RejectsInstall()
    {
        await AssertMarketplaceTransparencyLogProofRejectedAsync(
            additionalTrustToml:
            """

                [plugins.marketplace_trust.transparency_logs.rekor]
                enabled = true
                public_key_sha256 = "log-public-key-pin"
            """,
            expectedSignatureStatus: "transparency-log-unsupported",
            transparencyLogJson:
            """
                    "transparency_log": {
                      "kind": "rekor",
                      "log_id": "rekor",
                      "log_index": 1,
                      "tree_size": 8,
                      "root_hash": "root-hash",
                      "checkpoint": {
                        "origin": "rekor",
                        "tree_size": 8,
                        "root_hash": "root-hash",
                        "signature": "checkpoint-signature"
                      },
                      "inclusion_proof": ["node-a", "node-b"]
                    },
            """);
    }

    [Fact]
    public async Task InstallAsync_WhenTrustedMarketplaceTransparencyLogInclusionProofEnvelopeIsIncomplete_RejectsInstall()
    {
        await AssertMarketplaceTransparencyLogProofRejectedAsync(
            additionalTrustToml:
            """

                [plugins.marketplace_trust.transparency_logs.rekor]
                enabled = true
                public_key_sha256 = "log-public-key-pin"
            """,
            expectedSignatureStatus: "transparency-log-inclusion-proof-incomplete",
            transparencyLogJson:
            """
                    "transparency_log": {
                      "kind": "rekor",
                      "log_id": "rekor",
                      "log_index": 1,
                      "tree_size": 8,
                      "root_hash": "root-hash",
                      "checkpoint": "checkpoint",
                      "inclusion_proof": [
                        { "hash": "node-a" }
                      ]
                    },
            """);
    }

    [Fact]
    public async Task InstallAsync_WhenTrustedMarketplaceTransparencyLogInclusionProofEnvelopeIsComplete_RejectsInstall()
    {
        await AssertMarketplaceTransparencyLogProofRejectedAsync(
            additionalTrustToml:
            """

                [plugins.marketplace_trust.transparency_logs.rekor]
                enabled = true
                public_key_sha256 = "log-public-key-pin"
            """,
            expectedSignatureStatus: "transparency-log-unsupported",
            transparencyLogJson:
            """
                    "transparency_log": {
                      "kind": "rekor",
                      "log_id": "rekor",
                      "log_index": 1,
                      "tree_size": 8,
                      "root_hash": "root-hash",
                      "checkpoint": "checkpoint",
                      "inclusion_proof": [
                        { "hash": "node-a", "position": "left" },
                        { "hash": "node-b", "position": "right" }
                      ]
                    },
            """);
    }

    [Fact]
    public async Task InstallAsync_WhenTrustedMarketplaceTransparencyLogProofRangeIsInvalid_RejectsInstall()
    {
        await AssertMarketplaceTransparencyLogProofRejectedAsync(
            additionalTrustToml:
            """

                [plugins.marketplace_trust.transparency_logs.rekor]
                enabled = true
                public_key_sha256 = "log-public-key-pin"
            """,
            expectedSignatureStatus: "transparency-log-proof-range-invalid",
            transparencyLogJson:
            """
                    "transparency_log": {
                      "kind": "rekor",
                      "log_id": "rekor",
                      "log_index": 8,
                      "tree_size": 8,
                      "root_hash": "root-hash",
                      "checkpoint": "checkpoint",
                      "inclusion_proof": ["node-a", "node-b"]
                    },
            """);
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceTransparencyLogPublicKeyPinMismatches_RejectsInstall()
    {
        await AssertMarketplaceTransparencyLogProofRejectedAsync(
            additionalTrustToml:
            """

                [plugins.marketplace_trust.transparency_logs.rekor]
                enabled = true
                public_key = "AQID"
                public_key_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
            """,
            expectedSignatureStatus: "transparency-log-public-key-mismatch");
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceTransparencyLogPublicKeyIsInvalid_RejectsInstall()
    {
        await AssertMarketplaceTransparencyLogProofRejectedAsync(
            additionalTrustToml:
            """

                [plugins.marketplace_trust.transparency_logs.rekor]
                enabled = true
                public_key = "not-base64"
                public_key_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
            """,
            expectedSignatureStatus: "transparency-log-invalid-public-key");
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceDeclaresOcspResponse_RejectsInstall()
    {
        await AssertMarketplaceRevocationMaterialRejectedAsync(
            """
                "ocsp_response": "base64-ocsp-response",
            """,
            expectedSignatureStatus: "revocation-check-missing-certificate-chain");
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceDeclaresCrlSet_RejectsInstall()
    {
        await AssertMarketplaceRevocationMaterialRejectedAsync(
            """
                "crlSet": ["base64-crl"],
            """,
            expectedSignatureStatus: "revocation-check-missing-certificate-chain");
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceDeclaresRevocationEnvelope_RejectsInstall()
    {
        await AssertMarketplaceRevocationMaterialRejectedAsync(
            """
                "ocsp_response": { "response": "base64-ocsp-response", "produced_at": "2026-06-04T00:00:00Z" },
                "crl_set": [{ "value": "base64-crl", "url": "https://example.invalid/root.crl" }],
                "revocation_proof": { "kind": "ocsp-staple", "proof": "base64-proof" },
                "revocation_status": { "status": "unknown", "checked_at": "2026-06-04T00:00:00Z" },
            """,
            expectedSignatureStatus: "revocation-check-missing-certificate-chain");
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceCertificateChainDeclaresRevocationEnvelope_RejectsInstall()
    {
        await AssertMarketplaceCertificateChainRevocationMaterialRejectedAsync(
            """
                "ocsp_response": { "response": "base64-ocsp-response", "produced_at": "2026-06-04T00:00:00Z" },
                "revocation_status": { "status": "unknown", "checked_at": "2026-06-04T00:00:00Z" },
            """,
            expectedSignatureStatus: "revocation-check-unsupported");
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceCertificateChainDeclaresIncompleteRevocationProof_RejectsInstall()
    {
        await AssertMarketplaceCertificateChainRevocationMaterialRejectedAsync(
            """
                "revocation_proof": { "kind": "ocsp-staple" },
            """,
            expectedSignatureStatus: "revocation-check-envelope-incomplete");
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceCertificateChainDeclaresEmptyOcspEnvelope_RejectsInstall()
    {
        await AssertMarketplaceCertificateChainRevocationMaterialRejectedAsync(
            """
                "ocsp_response": { "produced_at": "2026-06-04T00:00:00Z" },
            """,
            expectedSignatureStatus: "revocation-check-envelope-incomplete");
    }

    private static async Task AssertMarketplaceCertificateChainRevocationMaterialRejectedAsync(
        string extraIntegrityJson,
        string expectedSignatureStatus)
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var signedPlugin = CreateCertificateChainSignedMarketplacePlugin(
                workspace,
                "debug",
                "sample",
                "lab-signer",
                extraIntegrityJson: extraIntegrityJson);
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "{{signedPlugin.LeafPublicKeySha256}}"

                [plugins.marketplace_trust.certificate_authorities.lab-root]
                certificate_sha256 = "{{signedPlugin.RootCertificateSha256}}"
                """);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal(expectedSignatureStatus, plugin.Source.SignatureStatus);

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("signature check failed", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceSignatureUsesCertificateChain_VerifiesChainAndInstalls()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var signedPlugin = CreateCertificateChainSignedMarketplacePlugin(workspace, "debug", "sample", "lab-signer");
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "{{signedPlugin.LeafPublicKeySha256}}"

                [plugins.marketplace_trust.certificate_authorities.lab-root]
                certificate_sha256 = "{{signedPlugin.RootCertificateSha256}}"
                """);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("verified", plugin.Source.SignatureStatus);

            var install = await manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None);

            Assert.True(Directory.Exists(install.InstalledPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task AssertMarketplaceTransparencyLogProofRejectedAsync(
        string additionalTrustToml,
        string expectedSignatureStatus,
        string? transparencyLogJson = null)
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var signedPlugin = CreateSignedMarketplacePlugin(
                workspace,
                "debug",
                "sample",
                "lab-signer",
                extraIntegrityJson: transparencyLogJson ??
                """
                    "transparency_log": {
                      "kind": "rekor",
                      "log_id": "rekor",
                      "log_index": 1,
                      "tree_size": 8,
                      "root_hash": "root-hash",
                      "checkpoint": "checkpoint",
                      "inclusion_proof": ["node-a", "node-b"]
                    },
                """);
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "{{signedPlugin.PublicKeySha256}}"
                {{additionalTrustToml}}
                """);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal(expectedSignatureStatus, plugin.Source.SignatureStatus);

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("signature check failed", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task AssertMarketplaceRevocationMaterialRejectedAsync(
        string extraIntegrityJson,
        string expectedSignatureStatus)
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var signedPlugin = CreateSignedMarketplacePlugin(
                workspace,
                "debug",
                "sample",
                "lab-signer",
                extraIntegrityJson: extraIntegrityJson);
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "{{signedPlugin.PublicKeySha256}}"
                """);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal(expectedSignatureStatus, plugin.Source.SignatureStatus);

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("signature check failed", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceCertificateChainLeafPinMismatches_RejectsInstall()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var signedPlugin = CreateCertificateChainSignedMarketplacePlugin(workspace, "debug", "sample", "lab-signer");
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"

                [plugins.marketplace_trust.certificate_authorities.lab-root]
                certificate_sha256 = "{{signedPlugin.RootCertificateSha256}}"
                """);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("certificate-leaf-public-key-mismatch", plugin.Source.SignatureStatus);

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("signature check failed", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceCertificateAuthorityIsDisabled_RejectsInstall()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var signedPlugin = CreateCertificateChainSignedMarketplacePlugin(workspace, "debug", "sample", "lab-signer");
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "{{signedPlugin.LeafPublicKeySha256}}"

                [plugins.marketplace_trust.certificate_authorities.lab-root]
                enabled = false
                certificate_sha256 = "{{signedPlugin.RootCertificateSha256}}"
                """);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("certificate-authority-mismatch", plugin.Source.SignatureStatus);

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("signature check failed", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceSignaturePublicKeyPinMismatches_RejectsInstall()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            _ = CreateSignedMarketplacePlugin(workspace, "debug", "sample", "lab-signer");
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
                """);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("public-key-mismatch", plugin.Source.SignatureStatus);

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("signature check failed", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceSignatureConfiguredPublicKeyPinMismatches_RejectsInstall()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var signedPlugin = CreateSignedMarketplacePlugin(workspace, "debug", "sample", "lab-signer", includePublicKey: false);
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key = "{{signedPlugin.PublicKeyBase64}}"
                public_key_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
                """);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("public-key-mismatch", plugin.Source.SignatureStatus);

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("signature check failed", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceSignatureConfiguredPublicKeyIsMissing_RejectsInstall()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var signedPlugin = CreateSignedMarketplacePlugin(workspace, "debug", "sample", "lab-signer", includePublicKey: false);
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "{{signedPlugin.PublicKeySha256}}"
                """);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("missing-public-key", plugin.Source.SignatureStatus);

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("signature check failed", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceSignatureIsInvalid_RejectsInstall()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var signedPlugin = CreateSignedMarketplacePlugin(workspace, "debug", "sample", "lab-signer", signatureOverride: Convert.ToBase64String(new byte[] { 1, 2, 3 }));
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "{{signedPlugin.PublicKeySha256}}"
                """);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("invalid", plugin.Source.SignatureStatus);

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("signature check failed", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceArchiveSourceIsValid_ExtractsAndInstalls()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(Path.Combine(tianShuHome, "tianshu.toml"), "[plugins]\nenabled = true\n");
            var pluginRoot = Path.Combine(root, "package-src", "sample");
            WriteInstalledPlugin(pluginRoot, "sample");
            var sha256 = ComputeDirectorySha256(pluginRoot);
            WriteMarketplacePluginWithArchive(workspace, "debug", "sample", pluginRoot, sha256);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("archive", plugin.Source.Type);
            Assert.Equal("deferred", plugin.Source.IntegrityStatus);
            Assert.True(File.Exists(plugin.Source.Path));

            var install = await manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(install.InstalledPath, ".tianshu-plugin", "plugin.json")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceArchiveSourceIsMissing_RejectsInstall()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(Path.Combine(tianShuHome, "tianshu.toml"), "[plugins]\nenabled = true\n");
            WriteMarketplacePluginWithMissingArchive(workspace, "debug", "sample", new string('0', 64));

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("archive", plugin.Source.Type);
            Assert.Equal("source-missing", plugin.Source.IntegrityStatus);

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("archive source path does not exist", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceArchiveEntryEscapesExtractionRoot_RejectsInstall()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(Path.Combine(tianShuHome, "tianshu.toml"), "[plugins]\nenabled = true\n");
            WriteMarketplacePluginWithEscapingArchive(workspace, "debug", "sample");

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("escapes extraction root", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(tianShuHome, "tmp", "plugins", "archives", "evil.txt")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceRemoteArchiveSourceIsEnabled_DownloadsVerifiesAndInstalls()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var remotePlugin = CreateSignedRemoteArchiveMarketplacePlugin(workspace, "debug", "sample", "lab-signer");
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]
                allow_remote_archive_sources = true
                remote_archive_max_bytes = 1048576

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "{{remotePlugin.PublicKeySha256}}"
                """);
            var handler = new ArchiveHttpMessageHandler(remotePlugin.Url, remotePlugin.ArchiveBytes);
            var manager = new KernelPluginsManager(tianShuHome: tianShuHome, httpClient: new HttpClient(handler));
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace]), CancellationToken.None);

            var plugin = Assert.Single(Assert.Single(result.Marketplaces).Plugins);
            Assert.Equal("remote_archive", plugin.Source.Type);
            Assert.Equal(remotePlugin.Url, plugin.Source.Path);
            Assert.Equal("deferred", plugin.Source.IntegrityStatus);
            Assert.Equal("verified", plugin.Source.SignatureStatus);

            var install = await manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None);

            Assert.Equal(1, handler.RequestCount);
            Assert.True(File.Exists(Path.Combine(install.InstalledPath, ".tianshu-plugin", "plugin.json")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceRemoteArchiveSourceIsDisabled_RejectsBeforeDownload()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var remotePlugin = CreateSignedRemoteArchiveMarketplacePlugin(workspace, "debug", "sample", "lab-signer");
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "{{remotePlugin.PublicKeySha256}}"
                """);
            var handler = new ArchiveHttpMessageHandler(remotePlugin.Url, remotePlugin.ArchiveBytes);
            var manager = new KernelPluginsManager(tianShuHome: tianShuHome, httpClient: new HttpClient(handler));

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("remote archive plugin sources are disabled", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_WhenMarketplaceRemoteArchiveExceedsMaxBytes_RejectsInstall()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            var remotePlugin = CreateSignedRemoteArchiveMarketplacePlugin(workspace, "debug", "sample", "lab-signer");
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]
                allow_remote_archive_sources = true
                remote_archive_max_bytes = 8

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "{{remotePlugin.PublicKeySha256}}"
                """);
            var handler = new ArchiveHttpMessageHandler(remotePlugin.Url, remotePlugin.ArchiveBytes);
            var manager = new KernelPluginsManager(tianShuHome: tianShuHome, httpClient: new HttpClient(handler));

            var exception = await Assert.ThrowsAsync<KernelPluginInstallException>(() => manager.InstallAsync(
                new KernelPluginInstallRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample", workspace),
                CancellationToken.None));

            Assert.True(exception.InvalidRequest);
            Assert.Contains("exceeds configured max bytes", exception.Message, StringComparison.Ordinal);
            Assert.Equal(1, handler.RequestCount);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ListAsync_WhenForceRemoteSyncSucceeds_ReconcilesCuratedPluginState()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        var curatedRoot = Path.Combine(tianShuHome, ".tmp", "plugins");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(Path.Combine(tianShuHome, ".tmp", "plugins.sha"), "sha-curated-001");
            var signedMarketplace = WriteSignedMarketplacePlugins(curatedRoot, "openai-curated", "lab-signer", "linear", "gmail", "calendar");
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                [plugins]
                enabled = true

                [plugins.marketplace_trust]
                trusted_signers = ["lab-signer"]

                [plugins.marketplace_trust.signers.lab-signer]
                public_key_sha256 = "{{signedMarketplace.PublicKeySha256}}"

                [plugins.installed."linear@openai-curated"]
                enabled = false

                [plugins.installed."calendar@openai-curated"]
                enabled = true
                """);
            WriteInstalledPlugin(Path.Combine(tianShuHome, "plugins", "cache", "openai-curated", "linear", "local"), "linear");
            WriteInstalledPlugin(Path.Combine(tianShuHome, "plugins", "cache", "openai-curated", "calendar", "local"), "calendar");

            var manager = new KernelPluginsManager(
                syncRemotePluginStatesAsync: _ => Task.FromResult<IReadOnlyList<KernelRemotePluginState>>(
                [
                    new("linear@openai-curated", true),
                    new("gmail@openai-curated", false),
                    new("ignored@openai-curated", true),
                ]),
                tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace], ForceRemoteSync: true), CancellationToken.None);

            Assert.Null(result.RemoteSyncError);
            var marketplace = Assert.Single(result.Marketplaces);
            Assert.Equal("openai-curated", marketplace.Name);

            Assert.Collection(
                marketplace.Plugins.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase),
                plugin =>
                {
                    Assert.Equal("calendar", plugin.Name);
                    Assert.False(plugin.Installed);
                    Assert.False(plugin.Enabled);
                },
                plugin =>
                {
                    Assert.Equal("gmail", plugin.Name);
                    Assert.True(plugin.Installed);
                    Assert.False(plugin.Enabled);
                },
                plugin =>
                {
                    Assert.Equal("linear", plugin.Name);
                    Assert.True(plugin.Installed);
                    Assert.True(plugin.Enabled);
                });

            var configText = await File.ReadAllTextAsync(Path.Combine(tianShuHome, "tianshu.toml"), CancellationToken.None);
            Assert.Contains("[plugins.installed.\"linear@openai-curated\"]", configText, StringComparison.Ordinal);
            Assert.Contains("enabled = true", configText, StringComparison.Ordinal);
            Assert.Contains("[plugins.installed.\"gmail@openai-curated\"]", configText, StringComparison.Ordinal);
            Assert.DoesNotContain("[plugins.installed.\"calendar@openai-curated\"]", configText, StringComparison.Ordinal);

            Assert.True(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "openai-curated", "gmail", "sha-curated-001")));
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "openai-curated", "calendar")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ListAsync_WhenForceRemoteSyncAutoInstallsUnsignedCuratedPlugin_ReportsRemoteSyncError()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        var curatedRoot = Path.Combine(tianShuHome, ".tmp", "plugins");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true
                """);
            WriteFile(Path.Combine(tianShuHome, ".tmp", "plugins.sha"), "sha-curated-001");
            WriteMarketplacePlugins(curatedRoot, "openai-curated", "gmail");

            var manager = new KernelPluginsManager(
                syncRemotePluginStatesAsync: _ => Task.FromResult<IReadOnlyList<KernelRemotePluginState>>(
                [
                    new("gmail@openai-curated", true),
                ]),
                tianShuHome: tianShuHome);
            var result = await manager.ListAsync(new KernelPluginListRequest([workspace], ForceRemoteSync: true), CancellationToken.None);

            Assert.Contains("requires integrity.sha256", result.RemoteSyncError, StringComparison.Ordinal);
            var marketplace = Assert.Single(result.Marketplaces);
            var plugin = Assert.Single(marketplace.Plugins);
            Assert.Equal("gmail", plugin.Name);
            Assert.False(plugin.Installed);
            Assert.False(Directory.Exists(Path.Combine(tianShuHome, "plugins", "cache", "openai-curated", "gmail")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnPluginDetailsAndBundleContents()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true
                [plugins.installed."sample@debug"]
                enabled = true
                """);
            WriteMarketplacePlugin(workspace, "debug", "sample");

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var plugin = await manager.ReadAsync(
                new KernelPluginReadRequest(Path.Combine(workspace, ".agents", "plugins", "marketplace.json"), "sample"),
                CancellationToken.None);

            Assert.Equal("debug", plugin.MarketplaceName);
            Assert.Equal(NormalizePath(Path.Combine(workspace, ".agents", "plugins", "marketplace.json")), NormalizePath(plugin.MarketplacePath));
            Assert.Equal("sample@debug", plugin.Summary.Id);
            Assert.Equal("sample", plugin.Summary.Name);
            Assert.Equal("local", plugin.Summary.Source.Type);
            Assert.Equal(NormalizePath(Path.Combine(workspace, ".agents", "plugins", "sample")), NormalizePath(plugin.Summary.Source.Path));
            Assert.False(plugin.Summary.Installed);
            Assert.True(plugin.Summary.Enabled);
            Assert.Equal("AVAILABLE", plugin.Summary.InstallPolicy);
            Assert.Equal("ON_INSTALL", plugin.Summary.AuthPolicy);
            Assert.Null(plugin.Description);

            var skill = Assert.Single(plugin.Skills);
            Assert.Equal("sample:search", skill.Name);
            Assert.Equal("sample search", skill.Description);
            Assert.Equal(NormalizePath(Path.Combine(workspace, ".agents", "plugins", "sample", "skills", "search")), NormalizePath(skill.Path));

            var app = Assert.Single(plugin.Apps);
            Assert.Equal("connector_example", app.Id);
            Assert.Equal("connector_example", app.Name);
            Assert.Null(app.InstallUrl);

            Assert.Equal(["sample"], plugin.McpServers);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UninstallAsync_ShouldRemoveCacheAndWorkspaceConfig_AndBeIdempotent()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var workspace = Path.Combine(root, "repo");
        var installedRoot = Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample", "local");
        Directory.CreateDirectory(Path.Combine(workspace, ".git"));
        Directory.CreateDirectory(tianShuHome);

        try
        {
            WriteInstalledPlugin(installedRoot, "sample");
            WriteFile(
                Path.Combine(workspace, ".tianshu", "tianshu.toml"),
                """
                [plugins]
                enabled = true
                [plugins.installed."sample@debug"]
                enabled = true

                [plugins.installed."other@debug"]
                enabled = false
                """);

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            await manager.UninstallAsync(new KernelPluginUninstallRequest("sample@debug", workspace), CancellationToken.None);

            Assert.False(Directory.Exists(installedRoot));
            var rootTable = Assert.IsType<TomlTable>(Toml.ToModel(await File.ReadAllTextAsync(Path.Combine(workspace, ".tianshu", "tianshu.toml"), CancellationToken.None)));
            var plugins = Assert.IsType<TomlTable>(rootTable["plugins"]);
            var installed = Assert.IsType<TomlTable>(plugins["installed"]);
            Assert.False(installed.ContainsKey("sample@debug"));
            Assert.True(installed.ContainsKey("other@debug"));

            await manager.UninstallAsync(new KernelPluginUninstallRequest("sample@debug", workspace), CancellationToken.None);
            Assert.False(Directory.Exists(installedRoot));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetEffectiveCapabilitiesAsync_ShouldReturnEmptyWhenPluginsFeatureDisabled()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var installedRoot = Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample", "local");

        try
        {
            Directory.CreateDirectory(tianShuHome);
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                "[plugins]\nenabled = false\n\n[plugins.\"sample@debug\"]\nenabled = true\n");
            WriteInstalledPlugin(installedRoot, "sample");

            var manager = new KernelPluginsManager(tianShuHome: tianShuHome);
            Assert.Empty(await manager.GetEffectiveSkillRootsAsync(CancellationToken.None));
            Assert.Empty(await manager.GetEffectiveMcpServersAsync(CancellationToken.None));
            Assert.Empty(await manager.GetEffectiveAppIdsAsync(CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task LoadResolvedServerConfigsAsync_ShouldPreserveUserConfiguredServerOverPluginDefinition()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var installedRoot = Path.Combine(tianShuHome, "plugins", "cache", "debug", "sample", "local");

        try
        {
            Directory.CreateDirectory(tianShuHome);
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                "[plugins]\nenabled = true\n\n[plugins.\"sample@debug\"]\nenabled = true\n\n[mcp_servers.sample]\ncommand = \"user-cmd\"\nargs = [\"--help\"]\n");
            WriteInstalledPlugin(installedRoot, "sample");

            var pluginsManager = new KernelPluginsManager(tianShuHome: tianShuHome);
            var mcpManager = new KernelMcpManager(
                _ => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal)),
                tianShuHome: tianShuHome,
                loadPluginMcpServersAsync: pluginsManager.GetEffectiveMcpServersAsync);

            var method = typeof(KernelMcpManager).GetMethod(
                "LoadResolvedServerConfigsAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var task = Assert.IsAssignableFrom<Task>(method!.Invoke(mcpManager, new object[] { CancellationToken.None }));
            await task;
            var result = Assert.IsAssignableFrom<IDictionary>(task.GetType().GetProperty("Result")!.GetValue(task));
            var sampleConfig = result["sample"];
            Assert.NotNull(sampleConfig);
            var transport = sampleConfig!.GetType().GetProperty("Transport")!.GetValue(sampleConfig);
            Assert.NotNull(transport);
            Assert.Contains("Stdio", transport!.GetType().Name, StringComparison.Ordinal);
            Assert.Equal("user-cmd", transport.GetType().GetProperty("Command")!.GetValue(transport));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void WriteMarketplacePlugin(string workspace, string marketplaceName, string pluginName)
        => WriteMarketplacePlugins(workspace, marketplaceName, pluginName);

    private static void WriteMarketplacePluginWithSha(string workspace, string marketplaceName, string pluginName, string sha256)
    {
        var marketplaceRoot = Path.Combine(workspace, ".agents", "plugins");
        var pluginRoot = Path.Combine(marketplaceRoot, pluginName);
        if (!Directory.Exists(pluginRoot))
        {
            WriteInstalledPlugin(pluginRoot, pluginName);
        }

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
                  },
                  "sha256": "{{sha256}}"
                }
              ]
            }
            """);
    }

    private static void WriteMarketplacePluginWithSigner(string workspace, string marketplaceName, string pluginName, string signer)
    {
        var marketplaceRoot = Path.Combine(workspace, ".agents", "plugins");
        var pluginRoot = Path.Combine(marketplaceRoot, pluginName);
        if (!Directory.Exists(pluginRoot))
        {
            WriteInstalledPlugin(pluginRoot, pluginName);
        }

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
                  },
                  "signer": "{{signer}}"
                }
              ]
            }
            """);
    }

    private static void WriteMarketplacePluginWithArchive(string workspace, string marketplaceName, string pluginName, string pluginRoot, string sha256)
    {
        var marketplaceRoot = Path.Combine(workspace, ".agents", "plugins");
        Directory.CreateDirectory(marketplaceRoot);
        var archivePath = Path.Combine(marketplaceRoot, $"{pluginName}.zip");
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        ZipFile.CreateFromDirectory(pluginRoot, archivePath);
        WriteMarketplaceArchiveEntry(workspace, marketplaceName, pluginName, sha256);
    }

    private static void WriteMarketplacePluginWithMissingArchive(string workspace, string marketplaceName, string pluginName, string sha256)
        => WriteMarketplaceArchiveEntry(workspace, marketplaceName, pluginName, sha256);

    private static void WriteMarketplacePluginWithEscapingArchive(string workspace, string marketplaceName, string pluginName)
    {
        var marketplaceRoot = Path.Combine(workspace, ".agents", "plugins");
        Directory.CreateDirectory(marketplaceRoot);
        using (var archive = ZipFile.Open(Path.Combine(marketplaceRoot, $"{pluginName}.zip"), ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../evil.txt");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("escape");
        }

        WriteMarketplaceArchiveEntry(workspace, marketplaceName, pluginName, new string('0', 64));
    }

    private static void WriteMarketplaceArchiveEntry(string workspace, string marketplaceName, string pluginName, string sha256)
    {
        var marketplaceRoot = Path.Combine(workspace, ".agents", "plugins");
        Directory.CreateDirectory(marketplaceRoot);
        WriteFile(
            Path.Combine(marketplaceRoot, "marketplace.json"),
            $$"""
            {
              "name": "{{marketplaceName}}",
              "plugins": [
                {
                  "name": "{{pluginName}}",
                  "source": {
                    "source": "archive",
                    "path": "./{{pluginName}}.zip"
                  },
                  "integrity": {
                    "sha256": "{{sha256}}"
                  }
                }
              ]
            }
            """);
    }

    private static SignedRemoteArchiveMarketplacePlugin CreateSignedRemoteArchiveMarketplacePlugin(
        string workspace,
        string marketplaceName,
        string pluginName,
        string signer)
    {
        var pluginRoot = Path.Combine(workspace, ".tmp", "remote-package-src", pluginName);
        WriteInstalledPlugin(pluginRoot, pluginName);
        var sha256 = ComputeDirectorySha256(pluginRoot);
        using var archiveStream = new MemoryStream();
        ZipFile.CreateFromDirectory(pluginRoot, archiveStream);
        var archiveBytes = archiveStream.ToArray();
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var publicKeyBase64 = Convert.ToBase64String(publicKeyBytes);
        var publicKeySha256 = Convert.ToHexString(SHA256.HashData(publicKeyBytes)).ToLowerInvariant();
        var signature = Convert.ToBase64String(ecdsa.SignData(
            Encoding.UTF8.GetBytes(BuildMarketplaceSignaturePayload(marketplaceName, pluginName, signer, sha256)),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence));
        var url = $"https://plugins.example.test/{pluginName}.zip";
        WriteRemoteArchiveMarketplaceEntry(
            workspace,
            marketplaceName,
            pluginName,
            url,
            sha256,
            signer,
            publicKeyBase64,
            signature);

        return new SignedRemoteArchiveMarketplacePlugin(publicKeySha256, archiveBytes, url);
    }

    private static void WriteRemoteArchiveMarketplaceEntry(
        string workspace,
        string marketplaceName,
        string pluginName,
        string url,
        string sha256,
        string signer,
        string publicKeyBase64,
        string signature)
    {
        var marketplaceRoot = Path.Combine(workspace, ".agents", "plugins");
        Directory.CreateDirectory(marketplaceRoot);
        WriteFile(
            Path.Combine(marketplaceRoot, "marketplace.json"),
            $$"""
            {
              "name": "{{marketplaceName}}",
              "plugins": [
                {
                  "name": "{{pluginName}}",
                  "source": {
                    "source": "remote_archive",
                    "url": "{{url}}"
                  },
                  "integrity": {
                    "sha256": "{{sha256}}",
                    "signer": "{{signer}}",
                    "signature_algorithm": "ecdsa-p256-sha256",
                    "public_key": "{{publicKeyBase64}}",
                    "signature": "{{signature}}"
                  }
                }
              ]
            }
            """);
    }

    private static SignedMarketplacePlugin CreateSignedMarketplacePlugin(
        string workspace,
        string marketplaceName,
        string pluginName,
        string signer,
        string? signatureOverride = null,
        bool includePublicKey = true,
        string? extraIntegrityJson = null)
    {
        var marketplaceRoot = Path.Combine(workspace, ".agents", "plugins");
        var pluginRoot = Path.Combine(marketplaceRoot, pluginName);
        if (!Directory.Exists(pluginRoot))
        {
            WriteInstalledPlugin(pluginRoot, pluginName);
        }

        var sha256 = ComputeDirectorySha256(pluginRoot);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var publicKeyBase64 = Convert.ToBase64String(publicKeyBytes);
        var publicKeySha256 = Convert.ToHexString(SHA256.HashData(publicKeyBytes)).ToLowerInvariant();
        var payload = BuildMarketplaceSignaturePayload(marketplaceName, pluginName, signer, sha256);
        var signature = signatureOverride ?? Convert.ToBase64String(ecdsa.SignData(
            Encoding.UTF8.GetBytes(payload),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence));
        var publicKeyJson = includePublicKey
            ? $"                    \"public_key\": \"{publicKeyBase64}\",{Environment.NewLine}"
            : string.Empty;

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
                  },
                  "integrity": {
                    "sha256": "{{sha256}}",
                    "signer": "{{signer}}",
                    "signature_algorithm": "ecdsa-p256-sha256",
                    {{publicKeyJson}}
                    {{extraIntegrityJson}}
                    "signature": "{{signature}}"
                  }
                }
              ]
            }
            """);

        return new SignedMarketplacePlugin(publicKeySha256, publicKeyBase64);
    }

    private static CertificateChainSignedMarketplacePlugin CreateCertificateChainSignedMarketplacePlugin(
        string workspace,
        string marketplaceName,
        string pluginName,
        string signer,
        string? extraIntegrityJson = null)
    {
        var marketplaceRoot = Path.Combine(workspace, ".agents", "plugins");
        var pluginRoot = Path.Combine(marketplaceRoot, pluginName);
        if (!Directory.Exists(pluginRoot))
        {
            WriteInstalledPlugin(pluginRoot, pluginName);
        }

        var sha256 = ComputeDirectorySha256(pluginRoot);
        var now = DateTimeOffset.UtcNow;
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=TianShu Test Marketplace Root", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        using var rootCertificate = rootRequest.CreateSelfSigned(now.AddDays(-1), now.AddDays(30));

        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafRequest = new CertificateRequest("CN=TianShu Test Marketplace Signer", leafKey, HashAlgorithmName.SHA256);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        leafRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(leafRequest.PublicKey, false));
        var serialNumber = RandomNumberGenerator.GetBytes(16);
        serialNumber[0] &= 0x7F;
        using var leafCertificate = leafRequest.Create(rootCertificate, now.AddDays(-1), now.AddDays(7), serialNumber);

        var leafPublicKeyBytes = leafKey.ExportSubjectPublicKeyInfo();
        var leafPublicKeySha256 = Convert.ToHexString(SHA256.HashData(leafPublicKeyBytes)).ToLowerInvariant();
        var rootCertificateBytes = rootCertificate.Export(X509ContentType.Cert);
        var rootCertificateSha256 = Convert.ToHexString(SHA256.HashData(rootCertificateBytes)).ToLowerInvariant();
        var leafCertificateBase64 = Convert.ToBase64String(leafCertificate.Export(X509ContentType.Cert));
        var rootCertificateBase64 = Convert.ToBase64String(rootCertificateBytes);
        var payload = BuildMarketplaceSignaturePayload(marketplaceName, pluginName, signer, sha256);
        var signature = Convert.ToBase64String(leafKey.SignData(
            Encoding.UTF8.GetBytes(payload),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence));

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
                  },
                  "integrity": {
                    "sha256": "{{sha256}}",
                    "signer": "{{signer}}",
                    "signature_algorithm": "ecdsa-p256-sha256",
                    "certificate_chain": [
                      "{{leafCertificateBase64}}",
                      "{{rootCertificateBase64}}"
                    ],
                    {{extraIntegrityJson}}
                    "signature": "{{signature}}"
                  }
                }
              ]
            }
            """);

        return new CertificateChainSignedMarketplacePlugin(leafPublicKeySha256, rootCertificateSha256);
    }

    private static SignedMarketplacePlugin WriteSignedMarketplacePlugins(
        string workspace,
        string marketplaceName,
        string signer,
        params string[] pluginNames)
    {
        var marketplaceRoot = Path.Combine(workspace, ".agents", "plugins");
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
        var publicKeyBase64 = Convert.ToBase64String(publicKeyBytes);
        var publicKeySha256 = Convert.ToHexString(SHA256.HashData(publicKeyBytes)).ToLowerInvariant();
        var pluginJson = string.Join(
            "," + Environment.NewLine,
            pluginNames.Select(pluginName =>
            {
                var pluginRoot = Path.Combine(marketplaceRoot, pluginName);
                WriteInstalledPlugin(pluginRoot, pluginName);
                var sha256 = ComputeDirectorySha256(pluginRoot);
                var signature = Convert.ToBase64String(ecdsa.SignData(
                    Encoding.UTF8.GetBytes(BuildMarketplaceSignaturePayload(marketplaceName, pluginName, signer, sha256)),
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence));
                return $$"""
                  {
                    "name": "{{pluginName}}",
                    "source": {
                      "source": "local",
                      "path": "./{{pluginName}}"
                    },
                    "integrity": {
                      "sha256": "{{sha256}}",
                      "signer": "{{signer}}",
                      "signature_algorithm": "ecdsa-p256-sha256",
                      "public_key": "{{publicKeyBase64}}",
                      "signature": "{{signature}}"
                    }
                  }
                """;
            }));
        WriteFile(
            Path.Combine(marketplaceRoot, "marketplace.json"),
            $$"""
            {
              "name": "{{marketplaceName}}",
              "plugins": [
            {{pluginJson}}
              ]
            }
            """);

        return new SignedMarketplacePlugin(publicKeySha256, publicKeyBase64);
    }

    private static void WriteMarketplacePlugins(string workspace, string marketplaceName, params string[] pluginNames)
    {
        var marketplaceRoot = Path.Combine(workspace, ".agents", "plugins");
        foreach (var pluginName in pluginNames)
        {
            var pluginRoot = Path.Combine(marketplaceRoot, pluginName);
            WriteInstalledPlugin(pluginRoot, pluginName);
        }

        var pluginJson = string.Join(
            "," + Environment.NewLine,
            pluginNames.Select(pluginName =>
                $$"""
                  {
                    "name": "{{pluginName}}",
                    "source": {
                      "source": "local",
                      "path": "./{{pluginName}}"
                    }
                  }
                """));
        WriteFile(
            Path.Combine(marketplaceRoot, "marketplace.json"),
            $$"""
            {
              "name": "{{marketplaceName}}",
              "plugins": [
            {{pluginJson}}
              ]
            }
            """);
    }

    private static string ComputeDirectorySha256(string sourcePath)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
                     .OrderBy(path => NormalizeDigestPath(Path.GetRelativePath(sourcePath, path)), StringComparer.Ordinal))
        {
            var relativePath = NormalizeDigestPath(Path.GetRelativePath(sourcePath, file));
            AppendDigestText(hash, relativePath);
            AppendDigestText(hash, new FileInfo(file).Length.ToString(CultureInfo.InvariantCulture));
            using var stream = File.OpenRead(file);
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }

            hash.AppendData(new byte[] { 0 });
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendDigestText(IncrementalHash hash, string text)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(text));
        hash.AppendData(new byte[] { 0 });
    }

    private static string NormalizeDigestPath(string path)
        => path.Replace('\\', '/');

    private static string BuildMarketplaceSignaturePayload(string marketplaceName, string pluginName, string signer, string expectedSha256)
        => string.Join(
            "\n",
            "TianShu plugin marketplace signature v1",
            $"marketplace:{Normalize(marketplaceName)}",
            $"plugin:{Normalize(pluginName)}",
            $"signer:{Normalize(signer)}",
            $"sha256:{Normalize(expectedSha256)?.ToLowerInvariant()}",
            string.Empty);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record SignedMarketplacePlugin(string PublicKeySha256, string PublicKeyBase64);

    private sealed record CertificateChainSignedMarketplacePlugin(string LeafPublicKeySha256, string RootCertificateSha256);

    private sealed record SignedRemoteArchiveMarketplacePlugin(string PublicKeySha256, byte[] ArchiveBytes, string Url);

    private sealed class ArchiveHttpMessageHandler(string expectedUrl, byte[] archiveBytes) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (!string.Equals(request.RequestUri?.ToString(), expectedUrl, StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archiveBytes),
            };
            response.Content.Headers.ContentLength = archiveBytes.Length;
            return Task.FromResult(response);
        }
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
                  "args": ["--version"],
                  "cwd": "./workspace",
                  "env": { "TOKEN": "1" },
                  "envVars": ["OPENAI_API_KEY"],
                  "startup_timeout_sec": 5,
                  "enabled_tools": ["rg"],
                  "disabled_tools": ["blocked"],
                  "tool_timeout_sec": 30
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
