using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Modules;

/// <summary>
/// Module 发现来源分类，用于计算 trust level、优先级和失败关闭策略。
/// Module discovery source kind used to compute trust level, priority, and fail-closed policy.
/// </summary>
public enum ModuleDiscoverySourceKind
{
    Unspecified = 0,
    BuiltIn = 1,
    Workspace = 2,
    UserHome = 3,
    ThirdPartyDirectory = 4,
    Package = 5,
    RemoteReserved = 100,
}

/// <summary>
/// Module 发现候选状态；只有 Selected 才能进入后续 admission / loading。
/// Module discovery candidate status; only Selected can enter later admission / loading.
/// </summary>
public enum ModuleDiscoveryCandidateStatus
{
    Discovered = 0,
    ManifestInvalid = 1,
    Disabled = 2,
    DuplicateRejected = 3,
    Selected = 4,
    Unavailable = 5,
}

/// <summary>
/// Module 发现诊断严重性。
/// Module discovery diagnostic severity.
/// </summary>
public enum ModuleDiscoveryIssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// Module 发现根；发现根本身不代表其中的模块可执行。
/// Module discovery root; a root being discoverable does not make modules executable.
/// </summary>
public sealed record ModuleDiscoveryRoot
{
    public ModuleDiscoveryRoot(
        string rootId,
        string path,
        ModuleDiscoverySourceKind sourceKind,
        ModuleTrustLevel trustLevel,
        int priority = 0,
        bool enabled = true)
    {
        RootId = IdentifierGuard.AgainstNullOrWhiteSpace(rootId, nameof(rootId));
        Path = IdentifierGuard.AgainstNullOrWhiteSpace(path, nameof(path));
        SourceKind = sourceKind;
        TrustLevel = trustLevel;
        Priority = priority;
        Enabled = enabled;
    }

    public string RootId { get; }

    public string Path { get; }

    public ModuleDiscoverySourceKind SourceKind { get; }

    public ModuleTrustLevel TrustLevel { get; }

    public int Priority { get; }

    public bool Enabled { get; }
}

/// <summary>
/// Module manifest 源位置。
/// Module manifest source location.
/// </summary>
public sealed record ModuleManifestSource
{
    public ModuleManifestSource(string manifestPath, ModuleDiscoveryRoot root)
    {
        ManifestPath = IdentifierGuard.AgainstNullOrWhiteSpace(manifestPath, nameof(manifestPath));
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public string ManifestPath { get; }

    public ModuleDiscoveryRoot Root { get; }
}

/// <summary>
/// 已解析的通用 Module manifest 投影；它是 descriptor projection 和 assembly loading 之前的安全输入。
/// Parsed generic Module manifest projection; safe input before descriptor projection and assembly loading.
/// </summary>
public sealed record ModuleManifestProjection
{
    public ModuleManifestProjection(
        string moduleId,
        ModuleKind kind,
        string displayName,
        string version,
        ModuleManifestSource source,
        bool enabled = true,
        int priority = 0,
        string? sdkContractVersion = null,
        string? minimumTianShuVersion = null,
        IReadOnlyList<string>? capabilities = null,
        IReadOnlyList<string>? diagnostics = null,
        ModuleImplementationBinding? implementationBinding = null,
        string? capabilitySchemaVersion = null)
    {
        ModuleId = IdentifierGuard.AgainstNullOrWhiteSpace(moduleId, nameof(moduleId));
        Kind = kind;
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        Version = IdentifierGuard.AgainstNullOrWhiteSpace(version, nameof(version));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Enabled = enabled;
        Priority = priority;
        SdkContractVersion = sdkContractVersion;
        MinimumTianShuVersion = minimumTianShuVersion;
        Capabilities = capabilities ?? Array.Empty<string>();
        Diagnostics = diagnostics ?? Array.Empty<string>();
        ImplementationBinding = implementationBinding;
        CapabilitySchemaVersion = capabilitySchemaVersion;
    }

    public string ModuleId { get; }

    public ModuleKind Kind { get; }

    public string DisplayName { get; }

    public string Version { get; }

    public ModuleManifestSource Source { get; }

    public bool Enabled { get; }

    public int Priority { get; }

    public string? SdkContractVersion { get; }

    public string? MinimumTianShuVersion { get; }

    public string? CapabilitySchemaVersion { get; }

    public IReadOnlyList<string> Capabilities { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    public ModuleImplementationBinding? ImplementationBinding { get; }
}

/// <summary>
/// Module 发现候选；它保留 manifest 投影和可选 descriptor，但不执行模块代码。
/// Module discovery candidate; keeps manifest projection and optional descriptor without executing module code.
/// </summary>
public sealed record ModuleDiscoveryCandidate
{
    public ModuleDiscoveryCandidate(
        ModuleManifestProjection manifest,
        ModuleDescriptor? descriptor = null,
        ModuleDiscoveryCandidateStatus status = ModuleDiscoveryCandidateStatus.Discovered,
        string? statusReason = null)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Descriptor = descriptor;
        Status = status;
        StatusReason = statusReason;
    }

    public ModuleManifestProjection Manifest { get; init; }

    public ModuleDescriptor? Descriptor { get; init; }

    public ModuleDiscoveryCandidateStatus Status { get; init; }

    public string? StatusReason { get; init; }
}

/// <summary>
/// Module 发现诊断事件。
/// Module discovery diagnostic event.
/// </summary>
public sealed record ModuleDiscoveryIssue
{
    public ModuleDiscoveryIssue(
        string code,
        string message,
        ModuleDiscoveryIssueSeverity severity = ModuleDiscoveryIssueSeverity.Error,
        string? moduleId = null,
        string? manifestPath = null)
    {
        Code = IdentifierGuard.AgainstNullOrWhiteSpace(code, nameof(code));
        Message = IdentifierGuard.AgainstNullOrWhiteSpace(message, nameof(message));
        Severity = severity;
        ModuleId = moduleId;
        ManifestPath = manifestPath;
    }

    public string Code { get; }

    public string Message { get; }

    public ModuleDiscoveryIssueSeverity Severity { get; }

    public string? ModuleId { get; }

    public string? ManifestPath { get; }
}

/// <summary>
/// Module 发现快照；后续 admission、health probe 和 loading 必须消费这个快照。
/// Module discovery snapshot consumed by later admission, health probe, and loading.
/// </summary>
public sealed record ModuleDiscoverySnapshot
{
    public ModuleDiscoverySnapshot(
        IReadOnlyList<ModuleDiscoveryRoot>? roots = null,
        IReadOnlyList<ModuleDiscoveryCandidate>? candidates = null,
        IReadOnlyList<ModuleDiscoveryIssue>? issues = null)
    {
        Roots = roots ?? Array.Empty<ModuleDiscoveryRoot>();
        Candidates = candidates ?? Array.Empty<ModuleDiscoveryCandidate>();
        Issues = issues ?? Array.Empty<ModuleDiscoveryIssue>();
    }

    public IReadOnlyList<ModuleDiscoveryRoot> Roots { get; }

    public IReadOnlyList<ModuleDiscoveryCandidate> Candidates { get; }

    public IReadOnlyList<ModuleDiscoveryIssue> Issues { get; }

    public IReadOnlyList<ModuleDiscoveryCandidate> SelectedCandidates
        => Candidates
            .Where(static candidate => candidate.Status == ModuleDiscoveryCandidateStatus.Selected)
            .ToArray();
}

/// <summary>
/// Module 发现解析器，提供内置与第三方候选的确定性选择规则。
/// Module discovery resolver that provides deterministic selection rules for built-in and third-party candidates.
/// </summary>
public static class ModuleDiscoveryResolver
{
    public static ModuleDiscoverySnapshot Resolve(
        IReadOnlyList<ModuleDiscoveryRoot> roots,
        IReadOnlyList<ModuleDiscoveryCandidate> discoveredCandidates,
        IReadOnlyList<ModuleDiscoveryIssue>? existingIssues = null,
        IReadOnlySet<string>? disabledModuleIds = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(discoveredCandidates);

        var disabled = disabledModuleIds ?? new HashSet<string>(StringComparer.Ordinal);
        var issues = new List<ModuleDiscoveryIssue>(existingIssues ?? Array.Empty<ModuleDiscoveryIssue>());
        var resolved = discoveredCandidates
            .Select(candidate => NormalizeInitialStatus(candidate, disabled, issues))
            .ToList();

        foreach (var group in resolved
                     .Where(static candidate => candidate.Status == ModuleDiscoveryCandidateStatus.Discovered)
                     .GroupBy(static candidate => candidate.Manifest.ModuleId, StringComparer.Ordinal))
        {
            var selected = group
                .OrderBy(static candidate => SourceRank(candidate.Manifest.Source.Root.SourceKind))
                .ThenBy(static candidate => TrustRank(candidate.Manifest.Source.Root.TrustLevel))
                .ThenBy(static candidate => candidate.Manifest.Priority)
                .ThenBy(static candidate => candidate.Manifest.Source.ManifestPath, StringComparer.OrdinalIgnoreCase)
                .First();

            for (var index = 0; index < resolved.Count; index++)
            {
                if (!ReferenceEquals(resolved[index], selected) && !Equals(resolved[index], selected))
                {
                    continue;
                }

                resolved[index] = selected with
                {
                    Status = ModuleDiscoveryCandidateStatus.Selected,
                    StatusReason = "module_discovery.selected",
                };
                break;
            }

            foreach (var duplicate in group.Where(candidate => !Equals(candidate, selected)))
            {
                var rejected = duplicate with
                {
                    Status = ModuleDiscoveryCandidateStatus.DuplicateRejected,
                    StatusReason = "module_discovery.duplicate_rejected",
                };
                ReplaceResolved(resolved, duplicate, rejected);
                issues.Add(new ModuleDiscoveryIssue(
                    "module_discovery.duplicate_rejected",
                    $"模块 id '{duplicate.Manifest.ModuleId}' 已由更高优先级候选选择，当前候选被拒绝。",
                    ModuleDiscoveryIssueSeverity.Warning,
                    duplicate.Manifest.ModuleId,
                    duplicate.Manifest.Source.ManifestPath));
            }
        }

        return new ModuleDiscoverySnapshot(roots, resolved, issues);
    }

    private static ModuleDiscoveryCandidate NormalizeInitialStatus(
        ModuleDiscoveryCandidate candidate,
        IReadOnlySet<string> disabledModuleIds,
        ICollection<ModuleDiscoveryIssue> issues)
    {
        if (candidate.Manifest.Kind == ModuleKind.Unspecified)
        {
            issues.Add(new ModuleDiscoveryIssue(
                "module_manifest.kind_unspecified",
                $"模块 manifest 缺少有效 kind：{candidate.Manifest.ModuleId}",
                ModuleDiscoveryIssueSeverity.Error,
                candidate.Manifest.ModuleId,
                candidate.Manifest.Source.ManifestPath));
            return candidate with
            {
                Status = ModuleDiscoveryCandidateStatus.ManifestInvalid,
                StatusReason = "module_manifest.kind_unspecified",
            };
        }

        if (!candidate.Manifest.Enabled || disabledModuleIds.Contains(candidate.Manifest.ModuleId))
        {
            issues.Add(new ModuleDiscoveryIssue(
                "module_discovery.disabled",
                $"模块 '{candidate.Manifest.ModuleId}' 已禁用，不会进入后续加载。",
                ModuleDiscoveryIssueSeverity.Info,
                candidate.Manifest.ModuleId,
                candidate.Manifest.Source.ManifestPath));
            return candidate with
            {
                Status = ModuleDiscoveryCandidateStatus.Disabled,
                StatusReason = "module_discovery.disabled",
            };
        }

        return candidate;
    }

    private static void ReplaceResolved(
        IList<ModuleDiscoveryCandidate> resolved,
        ModuleDiscoveryCandidate current,
        ModuleDiscoveryCandidate replacement)
    {
        for (var index = 0; index < resolved.Count; index++)
        {
            if (Equals(resolved[index], current))
            {
                resolved[index] = replacement;
                return;
            }
        }
    }

    private static int SourceRank(ModuleDiscoverySourceKind sourceKind)
        => sourceKind switch
        {
            ModuleDiscoverySourceKind.BuiltIn => 0,
            ModuleDiscoverySourceKind.Workspace => 1,
            ModuleDiscoverySourceKind.UserHome => 2,
            ModuleDiscoverySourceKind.ThirdPartyDirectory => 3,
            ModuleDiscoverySourceKind.Package => 4,
            ModuleDiscoverySourceKind.RemoteReserved => 5,
            _ => 100,
        };

    private static int TrustRank(ModuleTrustLevel trustLevel)
        => trustLevel switch
        {
            ModuleTrustLevel.BuiltIn => 0,
            ModuleTrustLevel.WorkspaceTrusted => 1,
            ModuleTrustLevel.UserInstalled => 2,
            ModuleTrustLevel.ThirdParty => 3,
            ModuleTrustLevel.Untrusted => 4,
            _ => 100,
        };
}
