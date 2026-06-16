using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tests;

public sealed class KernelConfigurationManagersTests
{
    [Fact]
    public async Task KernelSkillsManager_ShouldCacheResultsUntilClearedAndRespectOverrides()
    {
        using var scope = new TestDirectoryScope();
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        var skillsRoot = Path.Combine(scope.WorkspaceRoot, ".tianshu", "skills");
        var alphaDir = Path.Combine(skillsRoot, "alpha");
        Directory.CreateDirectory(alphaDir);
        await File.WriteAllTextAsync(Path.Combine(alphaDir, "SKILL.md"), "Alpha description");

        var manager = new KernelSkillsManager(
            (_, _) => Task.FromResult(new Dictionary<string, string>(overrides, StringComparer.Ordinal)),
            (values, _, _) =>
            {
                overrides = new Dictionary<string, string>(values, StringComparer.Ordinal);
                return Task.FromResult(Path.Combine(scope.TianShuHome, "tianshu.toml"));
            },
            scope.TianShuHome,
            userHome: Path.Combine(scope.Root, "home"));

        var first = await manager.ScanAsync(scope.WorkspaceRoot, Array.Empty<string>(), forceReload: false, CancellationToken.None);
        Assert.Single(first.Skills);
        Assert.True(first.Skills[0].Enabled);

        var betaDir = Path.Combine(skillsRoot, "beta");
        Directory.CreateDirectory(betaDir);
        await File.WriteAllTextAsync(Path.Combine(betaDir, "SKILL.md"), "Beta description");

        var cached = await manager.ScanAsync(scope.WorkspaceRoot, Array.Empty<string>(), forceReload: false, CancellationToken.None);
        Assert.Single(cached.Skills);

        manager.ClearCache();

        var afterClear = await manager.ScanAsync(scope.WorkspaceRoot, Array.Empty<string>(), forceReload: false, CancellationToken.None);
        Assert.Equal(2, afterClear.Skills.Count);

        var alphaPath = Path.Combine(skillsRoot, "alpha");
        var effectiveEnabled = await manager.WriteEnabledAsync(alphaPath, enabled: false, scope.WorkspaceRoot, CancellationToken.None);
        Assert.False(effectiveEnabled);

        var afterDisable = await manager.ScanAsync(scope.WorkspaceRoot, Array.Empty<string>(), forceReload: false, CancellationToken.None);
        var alpha = Assert.Single(afterDisable.Skills.Where(skill => string.Equals(skill.Name, "alpha", StringComparison.OrdinalIgnoreCase)));
        Assert.False(alpha.Enabled);
    }

    [Fact]
    public async Task KernelSkillsManager_ShouldScanRepoAgentsSkillsAlongsideProjectSkillRoots()
    {
        using var scope = new TestDirectoryScope();
        var repoRoot = Path.Combine(scope.Root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "src", "feature");
        Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
        Directory.CreateDirectory(nestedWorkspace);

        var repoAgentsSkill = Path.Combine(repoRoot, ".agents", "skills", "repo-skill");
        Directory.CreateDirectory(repoAgentsSkill);
        await File.WriteAllTextAsync(Path.Combine(repoAgentsSkill, "SKILL.md"), "Repo skill");

        var nestedProjectSkill = Path.Combine(nestedWorkspace, ".tianshu", "skills", "project-skill");
        Directory.CreateDirectory(nestedProjectSkill);
        await File.WriteAllTextAsync(Path.Combine(nestedProjectSkill, "SKILL.md"), "Project skill");

        var manager = new KernelSkillsManager(
            (_, _) => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal)),
            (_, _, _) => Task.FromResult(Path.Combine(scope.TianShuHome, "tianshu.toml")),
            scope.TianShuHome,
            userHome: Path.Combine(scope.Root, "home"));

        var result = await manager.ScanAsync(nestedWorkspace, Array.Empty<string>(), forceReload: false, CancellationToken.None);

        Assert.Contains(result.Skills, static skill => skill.Name == "repo-skill" && skill.Scope == "repo");
        Assert.Contains(result.Skills, static skill => skill.Name == "project-skill" && skill.Scope == "repo");
    }

    [Fact]
    public async Task KernelSkillsManager_ShouldResolveEnabledStateUsingScanCwd()
    {
        using var scope = new TestDirectoryScope();
        var repoRoot = Path.Combine(scope.Root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "src", "feature");
        Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
        Directory.CreateDirectory(nestedWorkspace);

        var repoSkill = Path.Combine(repoRoot, ".agents", "skills", "repo-skill");
        Directory.CreateDirectory(repoSkill);
        await File.WriteAllTextAsync(Path.Combine(repoSkill, "SKILL.md"), "Repo skill");

        var repoConfig = Path.Combine(repoRoot, ".tianshu", "tianshu.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(repoConfig)!);
        await File.WriteAllTextAsync(
            repoConfig,
            $$"""
            [[skills.config]]
            path = "{{Path.Combine(repoSkill, "SKILL.md").Replace("\\", "/")}}"
            enabled = false
            """);

        var manager = new KernelSkillsManager(
            async (cwd, _) =>
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                var userConfig = Path.Combine(scope.TianShuHome, "tianshu.toml");
                if (File.Exists(userConfig))
                {
                    var text = await File.ReadAllTextAsync(userConfig);
                    if (text.Contains("skills", StringComparison.Ordinal))
                    {
                        values[$"skills::{Path.GetFullPath(Path.Combine(repoSkill, "SKILL.md"))}"] = "false";
                    }
                }

                if (string.Equals(cwd, nestedWorkspace, StringComparison.OrdinalIgnoreCase))
                {
                    values[$"skills::{Path.GetFullPath(Path.Combine(repoSkill, "SKILL.md"))}"] = "false";
                }

                return values;
            },
            (_, _, _) => Task.FromResult(Path.Combine(scope.TianShuHome, "tianshu.toml")),
            scope.TianShuHome,
            userHome: Path.Combine(scope.Root, "home"));

        var repoResult = await manager.ScanAsync(repoRoot, Array.Empty<string>(), forceReload: true, CancellationToken.None);
        var nestedResult = await manager.ScanAsync(nestedWorkspace, Array.Empty<string>(), forceReload: true, CancellationToken.None);

        Assert.True(Assert.Single(repoResult.Skills).Enabled);
        Assert.False(Assert.Single(nestedResult.Skills).Enabled);
    }

    [Fact]
    public async Task KernelSkillsManager_ShouldUseNonProjectRootMarkersWhenResolvingRepoRoots()
    {
        using var scope = new TestDirectoryScope();
        var parentRoot = Path.Combine(scope.Root, "parent");
        var workspaceRoot = Path.Combine(parentRoot, "workspace");
        var nestedWorkspace = Path.Combine(workspaceRoot, "src", "feature");
        Directory.CreateDirectory(Path.Combine(parentRoot, ".git"));
        Directory.CreateDirectory(nestedWorkspace);
        File.WriteAllText(Path.Combine(workspaceRoot, ".project-root"), string.Empty);

        var parentSkill = Path.Combine(parentRoot, ".agents", "skills", "parent-skill");
        Directory.CreateDirectory(parentSkill);
        await File.WriteAllTextAsync(Path.Combine(parentSkill, "SKILL.md"), "Parent skill");

        var workspaceSkill = Path.Combine(workspaceRoot, ".agents", "skills", "workspace-skill");
        Directory.CreateDirectory(workspaceSkill);
        await File.WriteAllTextAsync(Path.Combine(workspaceSkill, "SKILL.md"), "Workspace skill");

        var manager = new KernelSkillsManager(
            (_, _) => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal)),
            (_, _, _) => Task.FromResult(Path.Combine(scope.TianShuHome, "tianshu.toml")),
            scope.TianShuHome,
            loadProjectRootConfigOverridesAsync: (_, _) => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["project_root_markers"] = "[\".project-root\"]",
            }),
            userHome: Path.Combine(scope.Root, "home"));

        var result = await manager.ScanAsync(nestedWorkspace, Array.Empty<string>(), forceReload: true, CancellationToken.None);

        Assert.DoesNotContain(result.Skills, static skill => skill.Name == "parent-skill");
        Assert.Contains(result.Skills, static skill => skill.Name == "workspace-skill");
    }

    [Fact]
    public async Task KernelSkillsManager_ShouldTreatEmptyProjectRootMarkersAsStopAtCwd()
    {
        using var scope = new TestDirectoryScope();
        var parentRoot = Path.Combine(scope.Root, "parent");
        var workspaceRoot = Path.Combine(parentRoot, "workspace");
        var nestedWorkspace = Path.Combine(workspaceRoot, "src", "feature");
        Directory.CreateDirectory(Path.Combine(parentRoot, ".git"));
        Directory.CreateDirectory(nestedWorkspace);

        var workspaceSkill = Path.Combine(workspaceRoot, ".agents", "skills", "workspace-skill");
        Directory.CreateDirectory(workspaceSkill);
        await File.WriteAllTextAsync(Path.Combine(workspaceSkill, "SKILL.md"), "Workspace skill");

        var nestedSkill = Path.Combine(nestedWorkspace, ".agents", "skills", "nested-skill");
        Directory.CreateDirectory(nestedSkill);
        await File.WriteAllTextAsync(Path.Combine(nestedSkill, "SKILL.md"), "Nested skill");

        var manager = new KernelSkillsManager(
            (_, _) => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal)),
            (_, _, _) => Task.FromResult(Path.Combine(scope.TianShuHome, "tianshu.toml")),
            scope.TianShuHome,
            loadProjectRootConfigOverridesAsync: (_, _) => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["project_root_markers"] = "[]",
            }),
            userHome: Path.Combine(scope.Root, "home"));

        var result = await manager.ScanAsync(nestedWorkspace, Array.Empty<string>(), forceReload: true, CancellationToken.None);

        Assert.DoesNotContain(result.Skills, static skill => skill.Name == "workspace-skill");
        Assert.Contains(result.Skills, static skill => skill.Name == "nested-skill");
    }

    [Fact]
    public async Task KernelSkillsManager_ShouldResolveSymlinkedSkillPathWhenWritingEnabledState()
    {
        using var scope = new TestDirectoryScope();
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        var skillsRoot = Path.Combine(scope.WorkspaceRoot, ".tianshu", "skills");
        var realSkillDir = Path.Combine(skillsRoot, "alpha");
        Directory.CreateDirectory(realSkillDir);
        await File.WriteAllTextAsync(Path.Combine(realSkillDir, "SKILL.md"), "Alpha description");

        var symlinkRoot = Path.Combine(scope.Root, "skill-links");
        Directory.CreateDirectory(symlinkRoot);
        var symlinkDir = Path.Combine(symlinkRoot, "alpha-link");
        try
        {
            Directory.CreateSymbolicLink(symlinkDir, realSkillDir);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException or NotSupportedException)
        {
            return;
        }

        var manager = new KernelSkillsManager(
            (_, _) => Task.FromResult(new Dictionary<string, string>(overrides, StringComparer.Ordinal)),
            (values, _, _) =>
            {
                overrides = new Dictionary<string, string>(values, StringComparer.Ordinal);
                return Task.FromResult(Path.Combine(scope.TianShuHome, "tianshu.toml"));
            },
            scope.TianShuHome,
            userHome: Path.Combine(scope.Root, "home"));

        var effectiveEnabled = await manager.WriteEnabledAsync(symlinkDir, enabled: false, scope.WorkspaceRoot, CancellationToken.None);
        Assert.False(effectiveEnabled);

        var afterDisable = await manager.ScanAsync(scope.WorkspaceRoot, Array.Empty<string>(), forceReload: true, CancellationToken.None);
        var alpha = Assert.Single(afterDisable.Skills.Where(skill => string.Equals(skill.Name, "alpha", StringComparison.OrdinalIgnoreCase)));
        Assert.False(alpha.Enabled);
    }

    [Fact]
    public async Task KernelSkillsManager_ShouldIncludeSystemAndAdminSkillsWithoutDeduplicatingByName()
    {
        using var scope = new TestDirectoryScope();
        var userHome = Path.Combine(scope.Root, "home");
        var systemConfigRoot = Path.Combine(scope.Root, "etc", "tianshu");
        var repoSkill = Path.Combine(scope.WorkspaceRoot, ".tianshu", "skills", "shared-skill");
        var userSkill = Path.Combine(scope.TianShuHome, "modules", "skills", "shared-skill");
        var systemSkill = Path.Combine(scope.TianShuHome, "modules", "skills", ".system", "shared-skill");
        var adminSkill = Path.Combine(systemConfigRoot, "skills", "shared-skill");

        Directory.CreateDirectory(repoSkill);
        Directory.CreateDirectory(userSkill);
        Directory.CreateDirectory(systemSkill);
        Directory.CreateDirectory(adminSkill);
        await File.WriteAllTextAsync(Path.Combine(repoSkill, "SKILL.md"), "Repo skill");
        await File.WriteAllTextAsync(Path.Combine(userSkill, "SKILL.md"), "User skill");
        await File.WriteAllTextAsync(Path.Combine(systemSkill, "SKILL.md"), "System skill");
        await File.WriteAllTextAsync(Path.Combine(adminSkill, "SKILL.md"), "Admin skill");

        var manager = new KernelSkillsManager(
            (_, _) => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal)),
            (_, _, _) => Task.FromResult(Path.Combine(scope.TianShuHome, "tianshu.toml")),
            scope.TianShuHome,
            userHome: userHome,
            systemConfigRoot: systemConfigRoot);

        var result = await manager.ScanAsync(scope.WorkspaceRoot, Array.Empty<string>(), forceReload: true, CancellationToken.None);

        Assert.Equal(4, result.Skills.Count);
        Assert.Equal(["repo", "user", "system", "admin"], result.Skills.Select(static skill => skill.Scope).ToArray());
        Assert.Equal(4, result.Skills.Select(static skill => skill.PathToSkillsMd).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task KernelSkillsManager_ShouldExcludeSystemSkillsWhenBundledSkillsDisabled()
    {
        using var scope = new TestDirectoryScope();
        var userHome = Path.Combine(scope.Root, "home");
        var systemConfigRoot = Path.Combine(scope.Root, "etc", "tianshu");
        var repoSkill = Path.Combine(scope.WorkspaceRoot, ".tianshu", "skills", "repo-skill");
        var userSkill = Path.Combine(scope.TianShuHome, "modules", "skills", "user-skill");
        var systemSkill = Path.Combine(scope.TianShuHome, "modules", "skills", ".system", "bundled-skill");
        var adminSkill = Path.Combine(systemConfigRoot, "skills", "admin-skill");

        Directory.CreateDirectory(repoSkill);
        Directory.CreateDirectory(userSkill);
        Directory.CreateDirectory(systemSkill);
        Directory.CreateDirectory(adminSkill);
        await File.WriteAllTextAsync(Path.Combine(repoSkill, "SKILL.md"), "Repo skill");
        await File.WriteAllTextAsync(Path.Combine(userSkill, "SKILL.md"), "User skill");
        await File.WriteAllTextAsync(Path.Combine(systemSkill, "SKILL.md"), "Bundled skill");
        await File.WriteAllTextAsync(Path.Combine(adminSkill, "SKILL.md"), "Admin skill");

        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["skills.bundled.enabled"] = JsonSerializer.Serialize(false),
        };

        var manager = new KernelSkillsManager(
            (_, _) => Task.FromResult(new Dictionary<string, string>(overrides, StringComparer.Ordinal)),
            (_, _, _) => Task.FromResult(Path.Combine(scope.TianShuHome, "tianshu.toml")),
            scope.TianShuHome,
            userHome: userHome,
            systemConfigRoot: systemConfigRoot);

        var result = await manager.ScanAsync(scope.WorkspaceRoot, Array.Empty<string>(), forceReload: true, CancellationToken.None);

        Assert.Contains(result.Skills, static skill => skill.Name == "repo-skill" && skill.Scope == "repo");
        Assert.Contains(result.Skills, static skill => skill.Name == "user-skill" && skill.Scope == "user");
        Assert.Contains(result.Skills, static skill => skill.Name == "admin-skill" && skill.Scope == "admin");
        Assert.DoesNotContain(result.Skills, static skill => skill.Name == "bundled-skill");
        Assert.DoesNotContain(result.Skills, static skill => skill.Scope == "system");
    }

    [Fact]
    public async Task KernelSkillsManager_ShouldRefreshCachedSkillRootsWhenBundledFlagChanges()
    {
        using var scope = new TestDirectoryScope();
        var userHome = Path.Combine(scope.Root, "home");
        var systemConfigRoot = Path.Combine(scope.Root, "etc", "tianshu");
        var repoSkill = Path.Combine(scope.WorkspaceRoot, ".tianshu", "skills", "repo-skill");
        var systemSkill = Path.Combine(scope.TianShuHome, "modules", "skills", ".system", "bundled-skill");
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);

        Directory.CreateDirectory(repoSkill);
        Directory.CreateDirectory(systemSkill);
        await File.WriteAllTextAsync(Path.Combine(repoSkill, "SKILL.md"), "Repo skill");
        await File.WriteAllTextAsync(Path.Combine(systemSkill, "SKILL.md"), "Bundled skill");

        var manager = new KernelSkillsManager(
            (_, _) => Task.FromResult(new Dictionary<string, string>(overrides, StringComparer.Ordinal)),
            (_, _, _) => Task.FromResult(Path.Combine(scope.TianShuHome, "tianshu.toml")),
            scope.TianShuHome,
            userHome: userHome,
            systemConfigRoot: systemConfigRoot);

        var enabledResult = await manager.ScanAsync(scope.WorkspaceRoot, Array.Empty<string>(), forceReload: false, CancellationToken.None);
        Assert.Contains(enabledResult.Skills, static skill => skill.Name == "bundled-skill" && skill.Scope == "system");

        overrides["skills.bundled.enabled"] = JsonSerializer.Serialize(false);

        var disabledResult = await manager.ScanAsync(scope.WorkspaceRoot, Array.Empty<string>(), forceReload: false, CancellationToken.None);
        Assert.Contains(disabledResult.Skills, static skill => skill.Name == "repo-skill" && skill.Scope == "repo");
        Assert.DoesNotContain(disabledResult.Skills, static skill => skill.Name == "bundled-skill");
        Assert.DoesNotContain(disabledResult.Skills, static skill => skill.Scope == "system");
    }

    [Fact]
    public async Task KernelSkillsManager_ShouldDeduplicateByCanonicalSkillDocumentPathKeepingFirstRoot()
    {
        using var scope = new TestDirectoryScope();
        var userHome = Path.Combine(scope.Root, "home");
        var realSkill = Path.Combine(scope.Root, "shared-skill-source");
        Directory.CreateDirectory(realSkill);
        await File.WriteAllTextAsync(Path.Combine(realSkill, "SKILL.md"), "Shared skill");

        var repoLink = Path.Combine(scope.WorkspaceRoot, ".tianshu", "skills", "shared-skill");
        var userLink = Path.Combine(scope.TianShuHome, "modules", "skills", "shared-skill");
        Directory.CreateDirectory(Path.GetDirectoryName(repoLink)!);
        Directory.CreateDirectory(Path.GetDirectoryName(userLink)!);

        try
        {
            Directory.CreateSymbolicLink(repoLink, realSkill);
            Directory.CreateSymbolicLink(userLink, realSkill);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException or NotSupportedException)
        {
            return;
        }

        var manager = new KernelSkillsManager(
            (_, _) => Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal)),
            (_, _, _) => Task.FromResult(Path.Combine(scope.TianShuHome, "tianshu.toml")),
            scope.TianShuHome,
            userHome: userHome);

        var result = await manager.ScanAsync(scope.WorkspaceRoot, Array.Empty<string>(), forceReload: true, CancellationToken.None);

        var skill = Assert.Single(result.Skills);
        Assert.Equal("repo", skill.Scope);
        Assert.Equal(KernelPathUtilities.NormalizeSkillDocumentPath(Path.Combine(realSkill, "SKILL.md")), skill.PathToSkillsMd);
    }

    [Fact]
    public async Task KernelMcpManager_ShouldMergeTomlAndOverridesAndResolveAuthStatus()
    {
        using var scope = new TestDirectoryScope();
        Directory.CreateDirectory(scope.TianShuHome);
        await File.WriteAllTextAsync(
            Path.Combine(scope.TianShuHome, "tianshu.toml"),
            "[mcp_servers.demo]\nurl = \"https://example.com/mcp\"\n\n[mcp_servers.authenticated]\nurl = \"https://example.com/auth\"\n");

        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mcp_servers.authenticated.api_key"] = "\"secret\"",
            ["mcp_servers.override.url"] = "\"https://example.com/override\"",
        };

        var manager = new KernelMcpManager(
            _ => Task.FromResult(new Dictionary<string, string>(overrides, StringComparer.Ordinal)),
            scope.TianShuHome);

        var names = await manager.ListServerNamesAsync(CancellationToken.None);
        Assert.Equal(new[] { "authenticated", "demo", "override" }, names);

        var statuses = await manager.BuildStatusDataAsync(names, CancellationToken.None);
        Assert.Equal("bearer_token", statuses.Single(x => x.Name == "authenticated").AuthStatus);
        Assert.Equal("not_logged_in", statuses.Single(x => x.Name == "demo").AuthStatus);
        Assert.Equal("not_logged_in", statuses.Single(x => x.Name == "override").AuthStatus);
    }

    [Fact]
    public async Task KernelMcpManager_ShouldLoadMcpServerPackageManifestsBeforeTomlAndOverrides()
    {
        using var scope = new TestDirectoryScope();
        Directory.CreateDirectory(scope.TianShuHome);
        var manifestPath = Path.Combine(scope.TianShuHome, "modules", "mcp-servers", "company", "server.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(
            manifestPath,
            """
            id = "company"
            enabled = true
            type = "package"
            priority = 0

            [[servers]]
            id = "docs"
            enabled = true
            required = true
            transport = "http"
            url = "https://manifest.example.com/mcp"
            bearer_token_env_var = "DOCS_TOKEN"
            startup_timeout_ms = 5000
            tool_timeout_ms = 60000
            """);
        await File.WriteAllTextAsync(
            Path.Combine(scope.TianShuHome, "tianshu.toml"),
            """
            [mcp_servers.docs]
            url = "https://toml.example.com/mcp"
            """);

        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mcp_servers.docs.url"] = "\"https://override.example.com/mcp\"",
        };

        var manager = new KernelMcpManager(
            _ => Task.FromResult(new Dictionary<string, string>(overrides, StringComparer.Ordinal)),
            scope.TianShuHome);

        var names = await manager.ListServerNamesAsync(CancellationToken.None);
        Assert.Equal(["docs"], names);

        var resolved = await InvokeLoadResolvedServerConfigsAsync(manager);
        var docs = Assert.Single(resolved);
        Assert.Equal("docs", docs.Key);
        Assert.Equal("https://override.example.com/mcp", ReadTransportStringProperty(docs.Value, "Url"));
        Assert.True((bool)docs.Value.GetType().GetProperty("Required")!.GetValue(docs.Value)!);
    }

    private sealed class TestDirectoryScope : IDisposable
    {
        public TestDirectoryScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "tianshu-kernel-config-tests", Guid.NewGuid().ToString("N"));
            TianShuHome = Path.Combine(Root, ".tianshu-home");
            WorkspaceRoot = Path.Combine(Root, "workspace");
            Directory.CreateDirectory(TianShuHome);
            Directory.CreateDirectory(WorkspaceRoot);
        }

        public string Root { get; }

        public string TianShuHome { get; }

        public string WorkspaceRoot { get; }

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

    private static async Task<IReadOnlyDictionary<string, object>> InvokeLoadResolvedServerConfigsAsync(KernelMcpManager manager)
    {
        var method = typeof(KernelMcpManager).GetMethod("LoadResolvedServerConfigsAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(manager, [CancellationToken.None])!;
        await task.ConfigureAwait(false);
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        return ((System.Collections.IEnumerable)result)
            .Cast<object>()
            .ToDictionary(
                static item => (string)item.GetType().GetProperty("Key")!.GetValue(item)!,
                static item => item.GetType().GetProperty("Value")!.GetValue(item)!,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? ReadTransportStringProperty(object resolved, string propertyName)
    {
        var transport = resolved.GetType().GetProperty("Transport")!.GetValue(resolved)!;
        return (string?)transport.GetType().GetProperty(propertyName)!.GetValue(transport);
    }
}

