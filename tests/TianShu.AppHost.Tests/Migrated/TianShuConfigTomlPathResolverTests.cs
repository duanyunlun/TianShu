using TianShu.AppHost;
using TianShu.AppHost.Catalog;
using TianShu.AppHost.Configuration;
using TianShu.AppHost.Tools.Runtime;
using TianShu.RuntimeComposition;
using System.Text.Json;

namespace TianShu.AppHost.Tests;

[Collection("EnvironmentVariables")]
public sealed class TianShuConfigTomlPathResolverTests
{
    [Fact]
    public void ResolveSystemRequirementsTomlPath_ShouldMatchTianShuSystemLocation()
    {
        var path = TianShuConfigTomlPathResolver.ResolveSystemRequirementsTomlPath();
        if (OperatingSystem.IsWindows())
        {
            Assert.EndsWith(
                Path.Combine("TianShu", "requirements.toml"),
                path,
                StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal("/etc/tianshu/requirements.toml", path);
        }
    }

    [Fact]
    public void ResolveSystemConfigTomlPath_ShouldMatchTianShuSystemLocation()
    {
        var path = TianShuConfigTomlPathResolver.ResolveSystemConfigTomlPath();
        if (OperatingSystem.IsWindows())
        {
            Assert.EndsWith(
                Path.Combine("TianShu", "tianshu.toml"),
                path,
                StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal("/etc/tianshu/tianshu.toml", path);
        }
    }

    [Fact]
    public void ResolveDefaultSystemSkillsRoot_ShouldMatchTianShuSystemLocation()
    {
        var path = TianShuSkillRootPaths.ResolveDefaultSystemConfigRoot();
        if (OperatingSystem.IsWindows())
        {
            Assert.EndsWith(
                "TianShu",
                path,
                StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal("/etc/tianshu", path);
        }
    }

    [Fact]
    public void ResolveCwdConfigTomlPath_ShouldReturnCwdConfigToml()
    {
        var root = CreateTempDirectory();

        try
        {
            var cwd = Path.Combine(root, "workspace");
            Directory.CreateDirectory(cwd);

            var resolved = TianShuConfigTomlPathResolver.ResolveCwdConfigTomlPath(cwd);

            Assert.Equal(
                Path.Combine(cwd, ".tianshu", "tianshu.toml"),
                resolved,
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveLegacyManagedConfigTomlPath_ShouldMatchTianShuManagedLocation()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));
            var path = TianShuConfigTomlPathResolver.ResolveLegacyManagedConfigTomlPath();

            Assert.Equal(
                Path.Combine(root, "tianshu-home", "managed_config.toml"),
                path,
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveUserRequirementsTomlPath_ShouldTrackUserConfigLocation()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();

        try
        {
            var TianShuHome = Path.Combine(root, "tianshu-home");
            Environment.SetEnvironmentVariable("TIANSHU_HOME", TianShuHome);

            var path = TianShuConfigTomlPathResolver.ResolveUserRequirementsTomlPath();

            Assert.Equal(
                Path.Combine(TianShuHome, "requirements.toml"),
                path,
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveUserConfigTomlPath_ShouldPreferTianShuHome()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();

        try
        {
            var tianShuHome = Path.Combine(root, "tianshu-home");
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

            var path = TianShuConfigTomlPathResolver.ResolveUserConfigTomlPath();

            Assert.Equal(
                Path.Combine(tianShuHome, "tianshu.toml"),
                path,
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveWritableProjectConfigTomlPath_ShouldUseCurrentWorkspaceLayer_WhenCwdIsNestedUnderRepo()
    {
        var root = CreateTempDirectory();
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "src", "feature", "demo");

        try
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(nestedWorkspace);

            var resolved = TianShuConfigTomlPathResolver.ResolveWritableProjectConfigTomlPath(nestedWorkspace);

            Assert.Equal(
                Path.Combine(nestedWorkspace, ".tianshu", "tianshu.toml"),
                resolved,
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void EnumerateProjectLayerDirectories_ShouldReturnDotTianShuFoldersEvenWithoutTianShuToml()
    {
        var root = CreateTempDirectory();
        var repoRoot = Path.Combine(root, "repo");
        var cwd = Path.Combine(repoRoot, "src", "feature");

        try
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".tianshu"));
            Directory.CreateDirectory(cwd);

            var directories = TianShuConfigTomlPathResolver.EnumerateProjectLayerDirectories(cwd);

            Assert.Single(directories);
            Assert.Equal(
                Path.Combine(repoRoot, ".tianshu"),
                directories[0],
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void EnumerateProjectConfigPaths_ShouldRespectProjectRootMarkers()
    {
        var root = CreateTempDirectory();
        var parentRoot = Path.Combine(root, "parent");
        var projectRoot = Path.Combine(parentRoot, "workspace");
        var cwd = Path.Combine(projectRoot, "src", "feature");

        try
        {
            Directory.CreateDirectory(Path.Combine(parentRoot, ".git"));
            Directory.CreateDirectory(cwd);
            File.WriteAllText(Path.Combine(projectRoot, ".project-root"), string.Empty);

            var projectConfigPath = Path.Combine(projectRoot, ".tianshu", "tianshu.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(projectConfigPath)!);
            File.WriteAllText(projectConfigPath, "model = \"gpt-5\"\n");

            var parentConfigPath = Path.Combine(parentRoot, ".tianshu", "tianshu.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(parentConfigPath)!);
            File.WriteAllText(parentConfigPath, "model = \"o3\"\n");

            var paths = TianShuConfigTomlPathResolver.EnumerateProjectConfigPaths(
                cwd,
                [".project-root"]);

            Assert.Single(paths);
            Assert.Equal(
                projectConfigPath,
                paths[0],
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void TianShuTomlConfigurationLoader_LoadWithoutExplicitConfigOrProfile_ShouldDiscoverProjectTianShuToml()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var TianShuHome = Path.Combine(root, "home");
        var projectRoot = Path.Combine(root, "workspace");
        var cwd = Path.Combine(projectRoot, "src", "feature");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", TianShuHome);
            Directory.CreateDirectory(TianShuHome);
            Directory.CreateDirectory(Path.Combine(projectRoot, ".git"));
            Directory.CreateDirectory(cwd);

            var projectConfigPath = Path.Combine(projectRoot, ".tianshu", "tianshu.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(projectConfigPath)!);
            File.WriteAllText(
                projectConfigPath,
                """
                profile = "default"
                model = "gpt-project"
                provider = "openai-compatible"

                [profiles.default]
                agent = "default"
                execution = "default"

                [agents.default]
                model = "gpt-project"
                provider = "openai-compatible"

                [execution_profiles.default]
                provider = "openai-compatible"

                [providers.openai-compatible]
                base_url = "http://127.0.0.1:3001/v1"
                api_key_env = "OPENAI_COMPATIBLE_API_KEY"
                default_protocol = "responses"
                """);

            var config = new TianShuTomlConfigurationLoader().Load(
                configFilePath: null,
                profileOverride: null,
                configOverrides: null,
                workingDirectory: cwd);

            Assert.Equal("default", config.ActiveProfile);
            Assert.Equal("gpt-project", config.Model);
            Assert.Equal("openai-compatible", config.ModelProvider);
            Assert.Equal("http://127.0.0.1:3001/v1", config.ProviderBaseUrl);
            Assert.Equal("OPENAI_COMPATIBLE_API_KEY", config.ProviderEnvKey);
            Assert.Equal(
                projectConfigPath,
                config.ConfigFilePath,
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ParseEndpointModelCatalog_ShouldUseEndpointReturnedModelIds()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "object": "list",
              "data": [
                { "id": "provider-model-b", "object": "model" },
                { "id": "provider-model-a", "object": "model" },
                { "id": "provider-model-a", "object": "model" }
              ]
            }
            """);

        var models = KernelCatalogSurfaceAppHostRuntime.ParseEndpointModelCatalog(document.RootElement);

        Assert.Equal(["provider-model-a", "provider-model-b"], models.Select(static model => model.Model).ToArray());
        Assert.All(models, static model => Assert.False(model.Hidden));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-config-path-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
