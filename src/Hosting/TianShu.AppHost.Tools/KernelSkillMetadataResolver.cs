using YamlDotNet.Serialization;

namespace TianShu.AppHost.Tools;

internal sealed record KernelSkillInterfaceInfo(
    string? DisplayName,
    string? ShortDescription,
    string? IconSmall,
    string? IconLarge,
    string? BrandColor,
    string? DefaultPrompt);

internal sealed record KernelSkillDependencies(
    IReadOnlyList<KernelSkillToolDependency> Tools);

internal sealed record KernelSkillToolDependency(
    string Type,
    string Value,
    string? Description,
    string? Transport,
    string? Command,
    string? Url);

internal sealed record KernelSkillNetworkPermissionProfile(bool? Enabled);

internal sealed record KernelSkillPermissionProfile(KernelSkillNetworkPermissionProfile? Network);

internal sealed record KernelSkillManagedNetworkOverride(
    IReadOnlyList<string>? AllowedDomains,
    IReadOnlyList<string>? DeniedDomains)
{
    public bool HasDomainOverrides => AllowedDomains is not null || DeniedDomains is not null;
}

internal sealed record KernelResolvedSkillMetadata(
    string PathToSkillsMd,
    string? Name,
    string? Description,
    string? ShortDescription,
    KernelSkillInterfaceInfo? Interface,
    KernelSkillDependencies? Dependencies,
    KernelSkillPermissionProfile? PermissionProfile,
    KernelSkillManagedNetworkOverride? ManagedNetworkOverride);

internal static class KernelSkillMetadataResolver
{
    private const string SkillMarkdownFileName = "SKILL.md";
    private const string MetadataDirectoryName = "agents";
    private const string MetadataFileName = "tianshu.yaml";

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public static KernelResolvedSkillMetadata LoadForSkillDirectory(string skillDirectory)
    {
        var normalizedSkillDirectory = Path.GetFullPath(skillDirectory);
        var metadataPath = Path.Combine(normalizedSkillDirectory, MetadataDirectoryName, MetadataFileName);
        var skillMarkdownPath = Path.Combine(normalizedSkillDirectory, SkillMarkdownFileName);
        var pathToSkillsMd = KernelPathUtilities.NormalizeSkillDocumentPath(skillMarkdownPath);

        var skillDocument = TryParseSkillDocument(skillMarkdownPath);
        var parsed = TryParseSkillMetadataFile(metadataPath);
        return new KernelResolvedSkillMetadata(
            pathToSkillsMd,
            skillDocument?.Name,
            skillDocument?.Description,
            skillDocument?.ShortDescription,
            parsed?.Interface,
            parsed?.Dependencies,
            parsed?.PermissionProfile,
            parsed?.ManagedNetworkOverride);
    }

    public static KernelResolvedSkillMetadata? TryResolveForCommand(IReadOnlyList<string> commandArgs, string cwd)
    {
        if (commandArgs.Count == 0)
        {
            return null;
        }

        var executable = Normalize(commandArgs[0]);
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        string candidatePath;
        try
        {
            var combined = Path.IsPathRooted(executable)
                ? executable
                : Path.Combine(cwd, executable);
            candidatePath = KernelPathUtilities.TryNormalizeForComparison(combined)
                ?? Path.GetFullPath(combined);
        }
        catch
        {
            return null;
        }

        var looksLikePath = Path.IsPathRooted(executable)
            || executable.IndexOf(Path.DirectorySeparatorChar) >= 0
            || executable.IndexOf(Path.AltDirectorySeparatorChar) >= 0
            || File.Exists(candidatePath);
        if (!looksLikePath || !File.Exists(candidatePath))
        {
            return null;
        }

        var searchDirectory = Path.GetDirectoryName(candidatePath);
        while (!string.IsNullOrWhiteSpace(searchDirectory))
        {
            var metadataPath = Path.Combine(searchDirectory, MetadataDirectoryName, MetadataFileName);
            if (File.Exists(metadataPath))
            {
                return LoadForSkillDirectory(searchDirectory);
            }

            var skillMarkdownPath = Path.Combine(searchDirectory, SkillMarkdownFileName);
            if (File.Exists(skillMarkdownPath))
            {
                return LoadForSkillDirectory(searchDirectory);
            }

            var parent = Directory.GetParent(searchDirectory);
            if (parent is null)
            {
                break;
            }

            searchDirectory = parent.FullName;
        }

        return null;
    }

    private static KernelParsedSkillMetadata? TryParseSkillMetadataFile(string metadataPath)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(metadataPath);
            var parsed = YamlDeserializer.Deserialize<KernelSkillMetadataFile>(text);
            var metadataDirectory = Path.GetDirectoryName(metadataPath);
            var skillDirectory = !string.IsNullOrWhiteSpace(metadataDirectory)
                ? Directory.GetParent(metadataDirectory)?.FullName ?? metadataDirectory
                : null;
            return Normalize(parsed, skillDirectory);
        }
        catch
        {
            return null;
        }
    }

    private static KernelParsedSkillMetadata? Normalize(KernelSkillMetadataFile? parsed, string? metadataDirectoryPath)
    {
        var permissionProfile = parsed?.Permissions?.Network is null
            ? null
            : new KernelSkillPermissionProfile(
                new KernelSkillNetworkPermissionProfile(parsed.Permissions.Network.Enabled));
        if (permissionProfile?.Network?.Enabled is null)
        {
            permissionProfile = null;
        }

        var managedNetworkOverride = parsed?.Permissions?.Network is null
            ? null
            : new KernelSkillManagedNetworkOverride(
                NormalizeOptionalList(parsed.Permissions.Network.AllowedDomains),
                NormalizeOptionalList(parsed.Permissions.Network.DeniedDomains));
        if (managedNetworkOverride is not { HasDomainOverrides: true })
        {
            managedNetworkOverride = null;
        }

        var interfaceInfo = ResolveInterface(parsed?.Interface, metadataDirectoryPath);
        var dependencies = ResolveDependencies(parsed?.Dependencies);
        if (permissionProfile is null && managedNetworkOverride is null && interfaceInfo is null && dependencies is null)
        {
            return null;
        }

        return new KernelParsedSkillMetadata(interfaceInfo, dependencies, permissionProfile, managedNetworkOverride);
    }

    private static IReadOnlyList<string>? NormalizeOptionalList(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return null;
        }

        return values
            .Select(Normalize)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static KernelSkillInterfaceInfo? ResolveInterface(KernelSkillInterfaceSection? section, string? metadataDirectoryPath)
    {
        if (section is null)
        {
            return null;
        }

        var displayName = NormalizeText(section.DisplayName);
        var shortDescription = NormalizeText(section.ShortDescription);
        var iconSmall = ResolveInterfacePath(metadataDirectoryPath, section.IconSmall);
        var iconLarge = ResolveInterfacePath(metadataDirectoryPath, section.IconLarge);
        var brandColor = NormalizeText(section.BrandColor);
        var defaultPrompt = NormalizeText(section.DefaultPrompt);
        if (displayName is null
            && shortDescription is null
            && iconSmall is null
            && iconLarge is null
            && brandColor is null
            && defaultPrompt is null)
        {
            return null;
        }

        return new KernelSkillInterfaceInfo(
            displayName,
            shortDescription,
            iconSmall,
            iconLarge,
            brandColor,
            defaultPrompt);
    }

    private static KernelSkillDependencies? ResolveDependencies(KernelSkillDependenciesSection? dependencies)
    {
        if (dependencies?.Tools is null)
        {
            return null;
        }

        var tools = dependencies.Tools
            .Select(static tool => ResolveDependencyTool(tool))
            .Where(static tool => tool is not null)
            .Select(static tool => tool!)
            .ToArray();
        return tools.Length == 0
            ? null
            : new KernelSkillDependencies(tools);
    }

    private static KernelSkillToolDependency? ResolveDependencyTool(KernelSkillToolDependencySection? tool)
    {
        if (tool is null)
        {
            return null;
        }

        var type = NormalizeText(tool.Type);
        var value = NormalizeText(tool.Value);
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new KernelSkillToolDependency(
            type,
            value,
            NormalizeText(tool.Description),
            NormalizeText(tool.Transport),
            NormalizeText(tool.Command),
            NormalizeText(tool.Url));
    }

    private static string? ResolveInterfacePath(string? metadataDirectoryPath, string? path)
    {
        var normalized = NormalizePathValue(path);
        if (normalized is null)
        {
            return null;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(metadataDirectoryPath) && !Path.IsPathRooted(normalized))
            {
                return Path.GetFullPath(Path.Combine(metadataDirectoryPath, normalized));
            }

            return Path.GetFullPath(normalized);
        }
        catch
        {
            return normalized;
        }
    }

    private static KernelParsedSkillDocument? TryParseSkillDocument(string skillMarkdownPath)
    {
        if (!File.Exists(skillMarkdownPath))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(skillMarkdownPath);
            return ParseSkillDocument(text);
        }
        catch
        {
            return null;
        }
    }

    private static KernelParsedSkillDocument ParseSkillDocument(string text)
    {
        var frontmatter = ExtractFrontmatter(text);
        if (!string.IsNullOrWhiteSpace(frontmatter))
        {
            try
            {
                var parsed = YamlDeserializer.Deserialize<KernelSkillDocumentFrontmatter>(frontmatter);
                return new KernelParsedSkillDocument(
                    NormalizeText(parsed?.Name),
                    NormalizeText(parsed?.Description) ?? ReadBodyDescription(text),
                    NormalizeText(parsed?.Metadata?.ShortDescription ?? parsed?.Metadata?.LegacyShortDescription));
            }
            catch
            {
                // 保持向后兼容：frontmatter 解析失败时退回正文首行。
            }
        }

        return new KernelParsedSkillDocument(
            null,
            ReadBodyDescription(text),
            null);
    }

    private static string? ExtractFrontmatter(string text)
    {
        using var reader = new StringReader(text);
        var firstLine = reader.ReadLine();
        if (!string.Equals(NormalizeFence(firstLine), "---", StringComparison.Ordinal))
        {
            return null;
        }

        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(NormalizeFence(line), "---", StringComparison.Ordinal))
            {
                return string.Join(Environment.NewLine, lines);
            }

            lines.Add(line);
        }

        return null;
    }

    private static string? ReadBodyDescription(string text)
    {
        using var reader = new StringReader(text);
        var inFrontmatter = false;
        var firstLine = true;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (firstLine)
            {
                firstLine = false;
                if (string.Equals(NormalizeFence(line), "---", StringComparison.Ordinal))
                {
                    inFrontmatter = true;
                    continue;
                }
            }

            if (inFrontmatter)
            {
                if (string.Equals(NormalizeFence(line), "---", StringComparison.Ordinal))
                {
                    inFrontmatter = false;
                }

                continue;
            }

            var normalized = NormalizeText(line);
            if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('#'))
            {
                continue;
            }

            return normalized;
        }

        return null;
    }

    private static string? NormalizeFence(string? value)
        => value?.TrimStart('\uFEFF').Trim();

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : string.Join(" ", parts);
    }

    private static string? NormalizePathValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private sealed record KernelParsedSkillMetadata(
        KernelSkillInterfaceInfo? Interface,
        KernelSkillDependencies? Dependencies,
        KernelSkillPermissionProfile? PermissionProfile,
        KernelSkillManagedNetworkOverride? ManagedNetworkOverride);

    private sealed class KernelSkillMetadataFile
    {
        [YamlMember(Alias = "interface")]
        public KernelSkillInterfaceSection? Interface { get; init; }

        [YamlMember(Alias = "dependencies")]
        public KernelSkillDependenciesSection? Dependencies { get; init; }

        [YamlMember(Alias = "permissions")]
        public KernelSkillPermissionSection? Permissions { get; init; }
    }

    private sealed class KernelSkillDocumentFrontmatter
    {
        [YamlMember(Alias = "name")]
        public string? Name { get; init; }

        [YamlMember(Alias = "description")]
        public string? Description { get; init; }

        [YamlMember(Alias = "metadata")]
        public KernelSkillDocumentMetadataSection? Metadata { get; init; }
    }

    private sealed class KernelSkillDocumentMetadataSection
    {
        [YamlMember(Alias = "short-description")]
        public string? ShortDescription { get; init; }

        [YamlMember(Alias = "short_description")]
        public string? LegacyShortDescription { get; init; }
    }

    private sealed class KernelSkillInterfaceSection
    {
        [YamlMember(Alias = "display_name")]
        public string? DisplayName { get; init; }

        [YamlMember(Alias = "short_description")]
        public string? ShortDescription { get; init; }

        [YamlMember(Alias = "icon_small")]
        public string? IconSmall { get; init; }

        [YamlMember(Alias = "icon_large")]
        public string? IconLarge { get; init; }

        [YamlMember(Alias = "brand_color")]
        public string? BrandColor { get; init; }

        [YamlMember(Alias = "default_prompt")]
        public string? DefaultPrompt { get; init; }
    }

    private sealed class KernelSkillDependenciesSection
    {
        [YamlMember(Alias = "tools")]
        public List<KernelSkillToolDependencySection>? Tools { get; init; }
    }

    private sealed class KernelSkillToolDependencySection
    {
        [YamlMember(Alias = "type")]
        public string? Type { get; init; }

        [YamlMember(Alias = "value")]
        public string? Value { get; init; }

        [YamlMember(Alias = "description")]
        public string? Description { get; init; }

        [YamlMember(Alias = "transport")]
        public string? Transport { get; init; }

        [YamlMember(Alias = "command")]
        public string? Command { get; init; }

        [YamlMember(Alias = "url")]
        public string? Url { get; init; }
    }

    private sealed class KernelSkillPermissionSection
    {
        [YamlMember(Alias = "network")]
        public KernelSkillNetworkPermissionSection? Network { get; init; }
    }

    private sealed class KernelSkillNetworkPermissionSection
    {
        [YamlMember(Alias = "enabled")]
        public bool? Enabled { get; init; }

        [YamlMember(Alias = "allowed_domains")]
        public List<string>? AllowedDomains { get; init; }

        [YamlMember(Alias = "denied_domains")]
        public List<string>? DeniedDomains { get; init; }
    }

    private sealed record KernelParsedSkillDocument(
        string? Name,
        string? Description,
        string? ShortDescription);
}
