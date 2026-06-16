using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tests;

public sealed class KernelSkillMetadataResolverTests
{
    [Fact]
    public async Task ScanAsync_ShouldLoadPermissionProfileAndManagedNetworkOverrideFromTianShuYaml()
    {
        using var scope = new TestDirectoryScope();
        var skillDir = CreateSkill(
            scope.Root,
            "managed_network_skill",
            """
            permissions:
              network:
                enabled: true
                allowed_domains:
                  - "skill.example.com"
                denied_domains:
                  - "blocked.skill.example.com"
            """);
        var manager = CreateSkillsManager(Path.Combine(scope.Root, "tianshu-home"));

        var scan = await manager.ScanAsync(scope.Root, Array.Empty<string>(), forceReload: true, CancellationToken.None);

        var skill = Assert.Single(scan.Skills);
        Assert.NotNull(skill.PermissionProfile);
        Assert.NotNull(skill.PermissionProfile!.Network);
        Assert.True(skill.PermissionProfile.Network!.Enabled);
        Assert.NotNull(skill.ManagedNetworkOverride);
        Assert.Equal(["skill.example.com"], skill.ManagedNetworkOverride!.AllowedDomains);
        Assert.Equal(["blocked.skill.example.com"], skill.ManagedNetworkOverride.DeniedDomains);
        Assert.Equal(Path.GetFullPath(Path.Combine(skillDir, "SKILL.md")), skill.PathToSkillsMd);
        Assert.Equal(Path.GetFullPath(Path.Combine(skillDir, "SKILL.md")), skill.Path);
    }

    [Fact]
    public void LoadForSkillDirectory_ShouldPreserveNetworkGateSeparatelyFromManagedNetworkOverride()
    {
        using var scope = new TestDirectoryScope();
        var skillDir = CreateSkill(
            scope.Root,
            "preserve_network_gate",
            """
            permissions:
              network:
                enabled: false
                allowed_domains:
                  - "skill.example.com"
            """);

        var metadata = KernelSkillMetadataResolver.LoadForSkillDirectory(skillDir);

        Assert.NotNull(metadata.PermissionProfile);
        Assert.NotNull(metadata.PermissionProfile!.Network);
        Assert.False(metadata.PermissionProfile.Network!.Enabled);
        Assert.NotNull(metadata.ManagedNetworkOverride);
        Assert.Equal(["skill.example.com"], metadata.ManagedNetworkOverride!.AllowedDomains);
        Assert.Null(metadata.ManagedNetworkOverride.DeniedDomains);
    }

    [Fact]
    public void TryResolveForCommand_ShouldFindSkillMetadataForSkillScript()
    {
        using var scope = new TestDirectoryScope();
        var skillDir = CreateSkill(
            scope.Root,
            "resolve_command_skill",
            """
            permissions:
              network:
                allowed_domains: []
                denied_domains:
                  - "blocked.skill.example.com"
            """);
        var scriptPath = Path.Combine(skillDir, "scripts", "hello.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        File.WriteAllText(scriptPath, "@echo off\r\necho hello\r\n");

        var metadata = KernelSkillMetadataResolver.TryResolveForCommand([scriptPath], scope.Root);

        Assert.NotNull(metadata);
        Assert.Equal(Path.GetFullPath(Path.Combine(skillDir, "SKILL.md")), metadata!.PathToSkillsMd);
        Assert.NotNull(metadata.ManagedNetworkOverride);
        Assert.Empty(metadata.ManagedNetworkOverride!.AllowedDomains!);
        Assert.Equal(["blocked.skill.example.com"], metadata.ManagedNetworkOverride.DeniedDomains);
    }

    [Fact]
    public async Task ScanAsync_ShouldLoadSkillDocumentMetadata_Interface_And_Dependencies()
    {
        using var scope = new TestDirectoryScope();
        var skillDir = CreateSkill(
            scope.Root,
            "demo_skill",
            """
            interface:
              display_name: "Demo Skill"
              short_description: "  short    desc   "
              icon_small: "./assets/small.png"
              icon_large: "./assets/large.svg"
              brand_color: "#112233"
              default_prompt: "  prompt   text "
            dependencies:
              tools:
                - type: "env_var"
                  value: "GITHUB_TOKEN"
                  description: " GitHub token "
                - type: "mcp"
                  value: "github"
                  transport: "streamable_http"
                  url: "https://example.com/mcp"
            """,
            """
            ---
            name: demo-skill
            description: long description
            metadata:
              short-description: short summary
            ---

            # Body
            """);
        Directory.CreateDirectory(Path.Combine(skillDir, "assets"));
        File.WriteAllText(Path.Combine(skillDir, "assets", "small.png"), string.Empty);
        File.WriteAllText(Path.Combine(skillDir, "assets", "large.svg"), string.Empty);
        var manager = CreateSkillsManager(Path.Combine(scope.Root, "tianshu-home"));

        var scan = await manager.ScanAsync(scope.Root, Array.Empty<string>(), forceReload: true, CancellationToken.None);

        var skill = Assert.Single(scan.Skills);
        Assert.Equal("demo-skill", skill.Name);
        Assert.Equal("long description", skill.Description);
        Assert.Equal("short summary", skill.ShortDescription);
        Assert.Equal(Path.GetFullPath(Path.Combine(skillDir, "SKILL.md")), skill.PathToSkillsMd);
        Assert.Equal(Path.GetFullPath(Path.Combine(skillDir, "SKILL.md")), skill.Path);
        Assert.NotNull(skill.Interface);
        Assert.Equal("Demo Skill", skill.Interface!.DisplayName);
        Assert.Equal("short desc", skill.Interface.ShortDescription);
        Assert.Equal(Path.GetFullPath(Path.Combine(skillDir, "assets", "small.png")), skill.Interface.IconSmall);
        Assert.Equal(Path.GetFullPath(Path.Combine(skillDir, "assets", "large.svg")), skill.Interface.IconLarge);
        Assert.Equal("#112233", skill.Interface.BrandColor);
        Assert.Equal("prompt text", skill.Interface.DefaultPrompt);
        Assert.NotNull(skill.Dependencies);
        Assert.Equal(2, skill.Dependencies!.Tools.Count);
        Assert.Equal("env_var", skill.Dependencies.Tools[0].Type);
        Assert.Equal("GITHUB_TOKEN", skill.Dependencies.Tools[0].Value);
        Assert.Equal("GitHub token", skill.Dependencies.Tools[0].Description);
        Assert.Equal("mcp", skill.Dependencies.Tools[1].Type);
        Assert.Equal("github", skill.Dependencies.Tools[1].Value);
        Assert.Equal("streamable_http", skill.Dependencies.Tools[1].Transport);
        Assert.Equal("https://example.com/mcp", skill.Dependencies.Tools[1].Url);
    }

    private static KernelSkillsManager CreateSkillsManager(string tianshuHome)
    {
        Directory.CreateDirectory(tianshuHome);
        return new KernelSkillsManager(
            (_, _) => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal)),
            (_, _, _) => Task.FromResult(""),
            tianshuHome,
            userHome: Path.Combine(Path.GetDirectoryName(tianshuHome)!, "home"));
    }

    private static string CreateSkill(string root, string skillName, string metadataYaml, string? skillMarkdown = null)
    {
        var skillDir = Path.Combine(root, ".tianshu", "skills", skillName);
        Directory.CreateDirectory(Path.Combine(skillDir, "agents"));
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), skillMarkdown ?? $"# {skillName}{Environment.NewLine}");
        File.WriteAllText(Path.Combine(skillDir, "agents", "tianshu.yaml"), metadataYaml);
        return skillDir;
    }

    private sealed class TestDirectoryScope : IDisposable
    {
        public TestDirectoryScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "tianshu-kernel-skill-metadata-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
