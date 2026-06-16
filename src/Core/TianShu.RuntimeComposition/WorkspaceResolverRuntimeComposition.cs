using TianShu.Configuration;
using TianShu.Contracts.Environment;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.RuntimeComposition;

/// <summary>
/// Workspace Resolver 运行时组合入口。
/// Runtime composition entry point for workspace resolver packages.
/// </summary>
internal static class WorkspaceResolverRuntimeComposition
{
    /// <summary>
    /// 合并配置文件中的 root markers 与启用的 Workspace Resolver 包 root markers。
    /// Merges configured root markers with root markers from enabled workspace resolver packages.
    /// </summary>
    public static IReadOnlyList<string> ResolveEffectiveRootMarkers(
        string userConfigPath,
        IReadOnlyList<string> configuredMarkers)
        => ResolveEffectivePolicy(userConfigPath, configuredMarkers).RootMarkers;

    /// <summary>
    /// 合并配置文件与启用的 Workspace Resolver 包策略。
    /// Merges configured workspace settings with enabled workspace resolver package policies.
    /// </summary>
    public static WorkspaceResolverEffectivePolicy ResolveEffectivePolicy(
        string userConfigPath,
        IReadOnlyList<string> configuredMarkers)
    {
        try
        {
            var rootDirectory = TianShuWorkspaceResolverManifestConfiguration.ResolveRootDirectory(userConfigPath);
            return TianShuWorkspaceResolverManifestConfiguration.ResolveEffectivePolicy(rootDirectory, configuredMarkers);
        }
        catch
        {
            return WorkspaceResolverEffectivePolicy.Empty with { RootMarkers = configuredMarkers };
        }
    }

    /// <summary>
    /// 创建内置只读 Workspace / Environment Module。
    /// Creates the built-in read-only Workspace / Environment Module.
    /// </summary>
    public static IWorkspaceModule CreateWorkspaceModule(
        string userConfigPath,
        IReadOnlyList<string> configuredMarkers)
        => new BuiltInWorkspaceEnvironmentModule(ResolveEffectivePolicy(userConfigPath, configuredMarkers));
}

/// <summary>
/// 内置 Workspace / Environment Module，只输出 workspace facts，不执行文件变更或 Kernel decision。
/// Built-in Workspace / Environment Module that only emits workspace facts and never performs file mutations or Kernel decisions.
/// </summary>
internal sealed class BuiltInWorkspaceEnvironmentModule : IWorkspaceModule
{
    private readonly WorkspaceResolverEffectivePolicy policy;

    public BuiltInWorkspaceEnvironmentModule(WorkspaceResolverEffectivePolicy policy)
    {
        this.policy = policy ?? WorkspaceResolverEffectivePolicy.Empty;
    }

    public ModuleDescriptor Descriptor { get; } = BuiltInModuleDescriptors.WorkspaceEnvironment();

    public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(new ModuleSmokeCheckResult(Descriptor.ModuleId, true, ModuleHealthStatus.Healthy));

    public ValueTask<WorkspaceResolutionResult> ResolveAsync(
        WorkspaceResolutionRequest request,
        WorkspaceModuleInvocationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var permissionFailure = ValidateReadOnlyInvocation(context);
        if (permissionFailure is not null)
        {
            return ValueTask.FromResult(permissionFailure);
        }

        var sources = CreateSources().ToArray();
        var diagnosticsRefs = new[] { $"diagnostics://workspace/{NormalizeFactToken(request.WorkspacePath)}/resolution" };
        var workspacePath = Path.GetFullPath(request.WorkspacePath);
        if (!Directory.Exists(workspacePath))
        {
            return ValueTask.FromResult(new WorkspaceResolutionResult(
                WorkspaceResolutionStatus.Rejected,
                sources: sources,
                diagnosticsRefs: diagnosticsRefs,
                issues: ["workspace_resolver.workspace_path_missing"]));
        }

        if (IsUntrusted(policy.TrustPolicy) && request.FailClosedWhenUntrusted)
        {
            return ValueTask.FromResult(new WorkspaceResolutionResult(
                WorkspaceResolutionStatus.Rejected,
                sources: sources,
                diagnosticsRefs: diagnosticsRefs,
                issues: ["workspace_resolver.untrusted_policy"]));
        }

        var markers = Normalize(request.RootMarkers.Concat(policy.RootMarkers)).ToArray();
        var match = FindWorkspaceRoot(workspacePath, markers);
        var facts = new List<WorkspaceFact>();
        var root = match.RootPath ?? workspacePath;
        facts.Add(CreateFact("workspace.root", WorkspaceFactKind.WorkspaceRoot, root, sources));

        if (match.Marker is not null)
        {
            facts.Add(CreateFact("workspace.root_marker", WorkspaceFactKind.RootMarker, match.Marker, sources));
        }

        foreach (var marker in policy.LanguageMarkers)
        {
            facts.Add(CreateFact($"workspace.language.{NormalizeFactToken(marker)}", WorkspaceFactKind.LanguageMarker, marker, sources));
        }

        foreach (var marker in policy.FrameworkMarkers)
        {
            facts.Add(CreateFact($"workspace.framework.{NormalizeFactToken(marker)}", WorkspaceFactKind.FrameworkMarker, marker, sources));
        }

        if (!string.IsNullOrWhiteSpace(policy.TrustPolicy))
        {
            facts.Add(CreateFact("workspace.trust_policy", WorkspaceFactKind.TrustPolicy, policy.TrustPolicy!, sources));
        }

        if (!string.IsNullOrWhiteSpace(policy.ArtifactRoot))
        {
            facts.Add(CreateFact("workspace.artifact_root", WorkspaceFactKind.ArtifactRoot, policy.ArtifactRoot!, sources));
        }

        if (!string.IsNullOrWhiteSpace(policy.StateRoot))
        {
            facts.Add(CreateFact("workspace.state_root", WorkspaceFactKind.StateRoot, policy.StateRoot!, sources));
        }

        var issues = new List<string>();
        var status = WorkspaceResolutionStatus.Resolved;
        if (match.Marker is null && markers.Length > 0)
        {
            status = WorkspaceResolutionStatus.DegradedReadOnly;
            issues.Add("workspace_resolver.root_marker_not_found");
            facts.Add(CreateFact("workspace.read_only_notice", WorkspaceFactKind.ReadOnlyNotice, "Workspace root marker was not found; facts are reference-only.", sources));
        }
        else if (IsReadOnlyPrompt(policy.TrustPolicy))
        {
            status = WorkspaceResolutionStatus.DegradedReadOnly;
            issues.Add("workspace_resolver.trust_policy_requires_prompt");
            facts.Add(CreateFact("workspace.read_only_notice", WorkspaceFactKind.ReadOnlyNotice, "Workspace resolver requires trust confirmation; facts are reference-only.", sources));
        }

        return ValueTask.FromResult(new WorkspaceResolutionResult(
            status,
            facts,
            sources,
            diagnosticsRefs,
            issues));
    }

    private static WorkspaceResolutionResult? ValidateReadOnlyInvocation(WorkspaceModuleInvocationContext context)
    {
        if (context.SideEffect.Level is SideEffectLevel.Unspecified || context.SideEffect.Level > SideEffectLevel.ReadOnly)
        {
            return new WorkspaceResolutionResult(
                WorkspaceResolutionStatus.Rejected,
                issues: ["workspace_resolver.side_effect_not_read_only"]);
        }

        if (!context.Permission.Scopes.Contains("module.workspace.environment", StringComparer.Ordinal)
            && !context.Permission.Scopes.Contains("module.workspace.environment.resolve", StringComparer.Ordinal))
        {
            return new WorkspaceResolutionResult(
                WorkspaceResolutionStatus.Rejected,
                issues: ["workspace_resolver.missing_permission_scope"]);
        }

        return null;
    }

    private IEnumerable<WorkspaceFactSource> CreateSources()
    {
        if (policy.Resolvers.Count == 0)
        {
            yield return new WorkspaceFactSource("workspace-resolver:configured", "configured_policy");
            yield break;
        }

        foreach (var resolver in policy.Resolvers)
        {
            yield return new WorkspaceFactSource(
                $"workspace-resolver:{resolver.PackageId}:{resolver.ResolverId}",
                "workspace_resolver_manifest",
                packageId: resolver.PackageId,
                resolverId: resolver.ResolverId);
        }
    }

    private static WorkspaceFact CreateFact(
        string factId,
        WorkspaceFactKind kind,
        string value,
        IReadOnlyList<WorkspaceFactSource> sources)
        => new(
            factId,
            kind,
            value,
            sources.Count == 0 ? new WorkspaceFactSource("workspace-resolver:unknown", "unknown") : sources[0]);

    private static (string? RootPath, string? Marker) FindWorkspaceRoot(string startPath, IReadOnlyList<string> markers)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            foreach (var marker in markers)
            {
                if (MatchesMarker(current.FullName, marker))
                {
                    return (current.FullName, marker);
                }
            }

            current = current.Parent;
        }

        return (null, null);
    }

    private static bool MatchesMarker(string directory, string marker)
    {
        try
        {
            if (marker.Contains('*', StringComparison.Ordinal) || marker.Contains('?', StringComparison.Ordinal))
            {
                return Directory.EnumerateFileSystemEntries(directory, marker, SearchOption.TopDirectoryOnly).Any();
            }

            return File.Exists(Path.Combine(directory, marker))
                   || Directory.Exists(Path.Combine(directory, marker));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUntrusted(string? trustPolicy)
        => string.Equals(trustPolicy, "deny", StringComparison.OrdinalIgnoreCase)
           || string.Equals(trustPolicy, "untrusted", StringComparison.OrdinalIgnoreCase)
           || string.Equals(trustPolicy, "blocked", StringComparison.OrdinalIgnoreCase);

    private static bool IsReadOnlyPrompt(string? trustPolicy)
        => string.Equals(trustPolicy, "prompt", StringComparison.OrdinalIgnoreCase)
           || string.Equals(trustPolicy, "readonly", StringComparison.OrdinalIgnoreCase)
           || string.Equals(trustPolicy, "read_only", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Normalize(IEnumerable<string> values)
        => values
            .Select(static value => value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeFactToken(string value)
    {
        var normalized = new string(value.Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '.').ToArray());
        while (normalized.Contains("..", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("..", ".", StringComparison.Ordinal);
        }

        return normalized.Trim('.');
    }
}
