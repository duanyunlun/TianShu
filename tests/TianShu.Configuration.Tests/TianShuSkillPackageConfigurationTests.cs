namespace TianShu.Configuration.Tests;

public sealed class TianShuSkillPackageConfigurationTests
{
    [Fact]
    public void Load_ScansSkillPackagesAndReadsMetadata()
    {
        using var temp = TempTianShuHome.Create();
        WriteSkill(
            Path.Combine(temp.Root, "modules", "skills", "demo"),
            """
            ---
            name: demo
            description: Demo skill
            metadata:
              short-description: Demo short
            ---

            Body description.
            """,
            """
            interface:
              display_name: "Demo Skill"
              short_description: "Metadata short"
              icon_small: "assets/small.png"
              default_prompt: "Use demo."
            dependencies:
              tools:
                - type: tool
                  value: shell
                  description: "Needs shell."
            permissions:
              network:
                enabled: true
                allowed_domains:
                  - "example.com"
            """);

        var projection = new TianShuSkillPackageConfiguration().Load(temp.Root, Path.Combine(temp.Root, "tianshu.toml"));

        var package = Assert.Single(projection.Packages);
        Assert.Equal("demo", package.Name);
        Assert.Equal("Demo Skill", package.DisplayName);
        Assert.Equal("Demo skill", package.Description);
        Assert.Equal("Metadata short", package.ShortDescription);
        Assert.True(package.Enabled);
        Assert.True(package.HasAssetsDirectory);
        Assert.Equal("shell", Assert.Single(package.Dependencies!.Tools).Value);
        Assert.Equal("example.com", Assert.Single(package.ManagedNetworkOverride!.AllowedDomains!));
    }

    [Fact]
    public void Load_AppliesPersistedDisabledState()
    {
        using var temp = TempTianShuHome.Create();
        var skillPath = WriteSkill(Path.Combine(temp.Root, "modules", "skills", "demo"));
        File.WriteAllText(
            Path.Combine(temp.Root, "tianshu.toml"),
            $$"""
            [[skills.config]]
            path = "{{skillPath.Replace("\\", "\\\\", StringComparison.Ordinal)}}"
            enabled = false
            """);

        var projection = new TianShuSkillPackageConfiguration().Load(temp.Root, Path.Combine(temp.Root, "tianshu.toml"));

        Assert.False(Assert.Single(projection.Packages).Enabled);
    }

    [Fact]
    public void SaveEnabled_WritesSkillsConfigWithoutChangingSkillDocument()
    {
        using var temp = TempTianShuHome.Create();
        var configPath = Path.Combine(temp.Root, "tianshu.toml");
        var skillDirectory = Path.Combine(temp.Root, "modules", "skills", "demo");
        var skillPath = WriteSkill(skillDirectory);
        var originalSkillDocument = File.ReadAllText(skillPath);
        File.WriteAllText(configPath, "model = \"gpt-test\"\n");

        var configuration = new TianShuSkillPackageConfiguration();
        configuration.SaveEnabled(configPath, skillPath, enabled: false);

        var saved = File.ReadAllText(configPath);
        Assert.Contains("[[skills.config]]", saved, StringComparison.Ordinal);
        Assert.Contains("enabled = false", saved, StringComparison.Ordinal);
        Assert.Equal(originalSkillDocument, File.ReadAllText(skillPath));

        configuration.SaveEnabled(configPath, skillPath, enabled: true);

        var reenabled = File.ReadAllText(configPath);
        Assert.DoesNotContain("[[skills.config]]", reenabled, StringComparison.Ordinal);
        Assert.Equal(originalSkillDocument, File.ReadAllText(skillPath));
    }

    private static string WriteSkill(string directory, string? skillDocument = null, string? metadata = null)
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "assets"));
        var skillPath = Path.Combine(directory, "SKILL.md");
        File.WriteAllText(skillPath, skillDocument ?? "# Demo\n\nDemo body.");
        if (metadata is not null)
        {
            var metadataDirectory = Path.Combine(directory, "agents");
            Directory.CreateDirectory(metadataDirectory);
            File.WriteAllText(Path.Combine(metadataDirectory, "tianshu.yaml"), metadata);
        }

        return skillPath;
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
            var root = Path.Combine(Path.GetTempPath(), $"tianshu-skill-package-config-{Guid.NewGuid():N}");
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

