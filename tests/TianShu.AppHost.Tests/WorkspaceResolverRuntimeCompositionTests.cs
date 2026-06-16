using TianShu.Contracts.Environment;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.RuntimeComposition;

namespace TianShu.AppHost.Tests;

public sealed class WorkspaceResolverRuntimeCompositionTests
{
    [Fact]
    public async Task WorkspaceModule_ResolvesReadOnlyWorkspaceFactsWithSources()
    {
        using var temp = TempWorkspace.Create();
        Directory.CreateDirectory(Path.Combine(temp.WorkspaceRoot, ".git"));
        WriteManifest(temp.TianShuHome, "builtin", "default", [".git"], "trusted");
        var sentinelPath = Path.Combine(temp.WorkspaceRoot, "sentinel.txt");
        File.WriteAllText(sentinelPath, "unchanged");
        var module = WorkspaceResolverRuntimeComposition.CreateWorkspaceModule(temp.ConfigPath, []);

        var result = await module.ResolveAsync(
            new WorkspaceResolutionRequest(temp.WorkspaceRoot),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(WorkspaceResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Facts, fact => fact.Kind == WorkspaceFactKind.WorkspaceRoot && fact.Value == temp.WorkspaceRoot);
        Assert.Contains(result.Facts, fact => fact.Kind == WorkspaceFactKind.RootMarker && fact.Value == ".git");
        Assert.Contains(result.Sources, source => source.PackageId == "builtin" && source.ResolverId == "default");
        Assert.Equal("unchanged", File.ReadAllText(sentinelPath));
    }

    [Fact]
    public async Task WorkspaceModule_DegradesToReadOnlyWhenTrustPolicyRequiresPrompt()
    {
        using var temp = TempWorkspace.Create();
        Directory.CreateDirectory(Path.Combine(temp.WorkspaceRoot, ".git"));
        WriteManifest(temp.TianShuHome, "builtin", "default", [".git"], "prompt");
        var module = WorkspaceResolverRuntimeComposition.CreateWorkspaceModule(temp.ConfigPath, []);

        var result = await module.ResolveAsync(
            new WorkspaceResolutionRequest(temp.WorkspaceRoot),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(WorkspaceResolutionStatus.DegradedReadOnly, result.Status);
        Assert.Contains("workspace_resolver.trust_policy_requires_prompt", result.Issues);
        Assert.Contains(result.Facts, fact => fact.Kind == WorkspaceFactKind.ReadOnlyNotice);
    }

    [Fact]
    public async Task WorkspaceModule_FailsClosedForMissingPermissionScope()
    {
        using var temp = TempWorkspace.Create();
        var module = WorkspaceResolverRuntimeComposition.CreateWorkspaceModule(temp.ConfigPath, [".git"]);

        var result = await module.ResolveAsync(
            new WorkspaceResolutionRequest(temp.WorkspaceRoot),
            CreateContext(permission: new PermissionEnvelope(["module.other"], requiresHumanGate: false)),
            CancellationToken.None);

        Assert.Equal(WorkspaceResolutionStatus.Rejected, result.Status);
        Assert.Contains("workspace_resolver.missing_permission_scope", result.Issues);
    }

    [Fact]
    public async Task WorkspaceModule_FailsClosedForMissingWorkspacePath()
    {
        using var temp = TempWorkspace.Create();
        var module = WorkspaceResolverRuntimeComposition.CreateWorkspaceModule(temp.ConfigPath, [".git"]);

        var result = await module.ResolveAsync(
            new WorkspaceResolutionRequest(Path.Combine(temp.TianShuHome, "missing")),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(WorkspaceResolutionStatus.Rejected, result.Status);
        Assert.Contains("workspace_resolver.workspace_path_missing", result.Issues);
    }

    private static WorkspaceModuleInvocationContext CreateContext(PermissionEnvelope? permission = null)
        => new(
            "step-workspace",
            "intent-workspace",
            "graph-workspace",
            "stage-workspace",
            "operation-workspace",
            permission ?? new PermissionEnvelope(["module.workspace.environment"], requiresHumanGate: false),
            new SideEffectProfile(SideEffectLevel.ReadOnly));

    private static void WriteManifest(
        string root,
        string packageId,
        string resolverId,
        IReadOnlyList<string> rootMarkers,
        string trustPolicy)
    {
        var path = Path.Combine(root, "modules", "workspace", "resolvers", packageId, "resolver.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $$"""
            id = "{{packageId}}"
            display_name = "{{packageId}}"
            enabled = true
            type = "builtin"
            priority = 0

            [[resolvers]]
            id = "{{resolverId}}"
            display_name = "{{resolverId}}"
            enabled = true
            type = "marker"
            priority = 0
            root_markers = [{{string.Join(", ", rootMarkers.Select(static marker => $"\"{marker}\""))}}]
            trust_policy = "{{trustPolicy}}"
            artifact_root = ".tianshu/artifacts"
            state_root = ".tianshu/state"
            language_markers = ["*.csproj"]
            framework_markers = ["*.sln"]
            """);
    }

    private sealed class TempWorkspace : IDisposable
    {
        private TempWorkspace(string root)
        {
            TianShuHome = root;
            WorkspaceRoot = Path.Combine(root, "workspace");
            ConfigPath = Path.Combine(root, "tianshu.toml");
            Directory.CreateDirectory(WorkspaceRoot);
            File.WriteAllText(ConfigPath, "");
        }

        public string TianShuHome { get; }

        public string WorkspaceRoot { get; }

        public string ConfigPath { get; }

        public static TempWorkspace Create()
            => new(Path.Combine(Path.GetTempPath(), $"tianshu-workspace-module-{Guid.NewGuid():N}"));

        public void Dispose()
        {
            if (Directory.Exists(TianShuHome))
            {
                Directory.Delete(TianShuHome, recursive: true);
            }
        }
    }
}
