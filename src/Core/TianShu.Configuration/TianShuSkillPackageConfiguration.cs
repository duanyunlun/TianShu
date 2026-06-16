using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// 读取并管理 TianShu Skill / Agent 内容能力包。
/// Loads and manages TianShu Skill / Agent content capability packages.
/// </summary>
public sealed class TianShuSkillPackageConfiguration
{
    public const string SkillDirectoryName = "skills";
    public const string SkillModuleDirectoryName = "modules/skills";
    public const string SystemSkillDirectoryName = ".system";
    public const string SkillMarkdownFileName = "SKILL.md";
    public const string MetadataDirectoryName = "agents";
    public const string MetadataFileName = "tianshu.yaml";

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public SkillPackageProjection Load(
        string rootDirectory,
        string? configPath = null,
        string? selectedSkillPath = null,
        IReadOnlyList<string>? extraRoots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.GetFullPath(rootDirectory);
        var resolvedConfigPath = string.IsNullOrWhiteSpace(configPath)
            ? Path.Combine(root, "tianshu.toml")
            : Path.GetFullPath(configPath);
        var enabledOverrides = ReadEnabledOverrides(resolvedConfigPath);
        var issues = new List<SkillPackageIssue>();
        var packages = new List<SkillPackageDescriptor>();

        foreach (var scanRoot in EnumerateSkillRoots(root, extraRoots ?? []))
        {
            if (!Directory.Exists(scanRoot.Path))
            {
                continue;
            }

            try
            {
                foreach (var skillPath in Directory.EnumerateFiles(scanRoot.Path, SkillMarkdownFileName, SearchOption.AllDirectories))
                {
                    if (ContainsHiddenRelativeDirectory(scanRoot.Path, skillPath))
                    {
                        continue;
                    }

                    packages.Add(ReadPackage(root, scanRoot, skillPath, enabledOverrides));
                }
            }
            catch (Exception ex)
            {
                issues.Add(new SkillPackageIssue("skill_package.scan_failed", ex.Message, scanRoot.Path));
            }
        }

        var deduplicated = packages
            .GroupBy(static package => package.SkillMarkdownPath, PathComparer)
            .Select(static group => group.OrderBy(static package => GetScopeSortRank(package.Scope)).First())
            .OrderBy(static package => GetScopeSortRank(package.Scope))
            .ThenBy(static package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.SkillMarkdownPath, PathComparer)
            .ToArray();

        var selectedPath = ResolveSelectedSkillPath(root, selectedSkillPath, deduplicated);
        var selectedPackage = deduplicated.FirstOrDefault(package =>
            selectedPath is not null
            && string.Equals(package.SkillMarkdownPath, selectedPath, StringComparison.OrdinalIgnoreCase));

        return new SkillPackageProjection(
            root,
            ResolveUserSkillRootDirectory(root),
            resolvedConfigPath,
            selectedPath,
            deduplicated.Select(package => package with { IsSelected = string.Equals(package.SkillMarkdownPath, selectedPath, StringComparison.OrdinalIgnoreCase) }).ToArray(),
            selectedPackage,
            issues);
    }

    public void SaveEnabled(string configPath, string skillMarkdownPath, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(skillMarkdownPath);

        var fullConfigPath = Path.GetFullPath(configPath);
        var root = File.Exists(fullConfigPath)
            ? TomlTable.From(Toml.Parse(File.ReadAllText(fullConfigPath, Encoding.UTF8), fullConfigPath))
            : new TomlTable();
        var normalizedSkillPath = NormalizeSkillDocumentPath(skillMarkdownPath);
        var skills = GetOrCreateTomlTable(root, "skills");
        var config = GetOrCreateTomlTableArray(skills, "config");
        var existing = config
            .Select(static (entry, index) => new { entry, index })
            .FirstOrDefault(item => AreEquivalentSkillPaths(ReadString(item.entry, "path"), normalizedSkillPath));

        if (enabled)
        {
            if (existing is not null)
            {
                config.RemoveAt(existing.index);
            }

            CleanupSkillsConfig(root, skills, config);
        }
        else
        {
            var entry = existing?.entry ?? new TomlTable();
            entry["path"] = normalizedSkillPath;
            entry["enabled"] = false;
            if (existing is null)
            {
                config.Add(entry);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullConfigPath)!);
        File.WriteAllText(fullConfigPath, Toml.FromModel(root).TrimEnd() + Environment.NewLine, Encoding.UTF8);
    }

    public static string ResolveRootDirectory(string TianShuConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TianShuConfigPath);
        return Path.GetDirectoryName(Path.GetFullPath(TianShuConfigPath)) ?? Environment.CurrentDirectory;
    }

    private static IEnumerable<SkillRootDescriptor> EnumerateSkillRoots(string rootDirectory, IReadOnlyList<string> extraRoots)
    {
        var userSkills = ResolveUserSkillRootDirectory(rootDirectory);
        yield return new SkillRootDescriptor(userSkills, "user", null);
        yield return new SkillRootDescriptor(Path.Combine(userSkills, SystemSkillDirectoryName), "system", null);

        var parent = Directory.GetParent(rootDirectory)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            yield return new SkillRootDescriptor(Path.Combine(parent!, ".agents", "skills"), "repo", null);
        }

        foreach (var extraRoot in extraRoots)
        {
            var normalized = NormalizePath(extraRoot);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                yield return new SkillRootDescriptor(normalized!, "user", null);
            }
        }
    }

    public static string ResolveUserSkillRootDirectory(string rootDirectory)
        => Path.Combine(Path.GetFullPath(rootDirectory), "modules", SkillDirectoryName);

    private static SkillPackageDescriptor ReadPackage(
        string rootDirectory,
        SkillRootDescriptor scanRoot,
        string skillMarkdownPath,
        IReadOnlyDictionary<string, bool> enabledOverrides)
    {
        var fullSkillPath = Path.GetFullPath(skillMarkdownPath);
        var packageDirectory = Path.GetDirectoryName(fullSkillPath)!;
        var metadataPath = Path.Combine(packageDirectory, MetadataDirectoryName, MetadataFileName);
        var document = ParseSkillDocument(fullSkillPath);
        var metadata = ParseMetadata(metadataPath);
        var packageId = ResolvePackageId(rootDirectory, packageDirectory);
        var normalizedSkillPath = NormalizeSkillDocumentPath(fullSkillPath);
        var enabled = !enabledOverrides.TryGetValue(normalizedSkillPath, out var configuredEnabled) || configuredEnabled;
        var name = NormalizeText(document.Name) ?? Path.GetFileName(packageDirectory);
        var displayName = metadata.Interface?.DisplayName ?? name;

        return new SkillPackageDescriptor(
            packageId,
            name,
            displayName,
            document.Description ?? string.Empty,
            metadata.Interface?.ShortDescription ?? document.ShortDescription,
            fullSkillPath,
            packageDirectory,
            metadataPath,
            scanRoot.Path,
            scanRoot.Scope,
            enabled,
            metadata.Interface,
            metadata.Dependencies,
            metadata.PermissionProfile,
            metadata.ManagedNetworkOverride,
            Directory.Exists(Path.Combine(packageDirectory, "assets")),
            Directory.Exists(Path.Combine(packageDirectory, "scripts")),
            Directory.Exists(Path.Combine(packageDirectory, "templates")));
    }

    private static IReadOnlyDictionary<string, bool> ReadEnabledOverrides(string configPath)
    {
        var values = new Dictionary<string, bool>(PathComparer);
        if (!File.Exists(configPath))
        {
            return values;
        }

        TomlTable root;
        try
        {
            root = TomlTable.From(Toml.Parse(File.ReadAllText(configPath, Encoding.UTF8), configPath));
        }
        catch
        {
            return values;
        }

        if (!root.TryGetValue("skills", out var skillsValue) || skillsValue is not TomlTable skills)
        {
            return values;
        }

        if (!skills.TryGetValue("config", out var configValue) || configValue is not TomlTableArray config)
        {
            return values;
        }

        var sourceDirectory = Path.GetDirectoryName(configPath);
        foreach (var entry in config.OfType<TomlTable>())
        {
            var path = ResolveSkillConfigPath(ReadString(entry, "path"), sourceDirectory);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            values[path!] = ReadBoolean(entry, "enabled") ?? true;
        }

        return values;
    }

    private static SkillDocumentInfo ParseSkillDocument(string path)
    {
        if (!File.Exists(path))
        {
            return new SkillDocumentInfo(null, null, null);
        }

        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var frontmatter = ExtractFrontmatter(text);
            if (!string.IsNullOrWhiteSpace(frontmatter))
            {
                var parsed = ParseSkillDocumentFrontmatter(frontmatter);
                return new SkillDocumentInfo(
                    NormalizeText(parsed?.Name),
                    NormalizeText(parsed?.Description) ?? ReadBodyDescription(text),
                    NormalizeText(parsed?.Metadata?.ShortDescription ?? parsed?.Metadata?.LegacyShortDescription));
            }

            return new SkillDocumentInfo(null, ReadBodyDescription(text), null);
        }
        catch
        {
            return new SkillDocumentInfo(null, null, null);
        }
    }

    private static SkillMetadataValue ParseMetadata(string metadataPath)
    {
        if (!File.Exists(metadataPath))
        {
            return new SkillMetadataValue();
        }

        try
        {
            var metadataDirectory = Path.GetDirectoryName(metadataPath);
            var parsed = ParseSkillMetadataFile(File.ReadAllText(metadataPath, Encoding.UTF8));
            return new SkillMetadataValue
            {
                Interface = ResolveInterface(parsed?.Interface, metadataDirectory),
                Dependencies = ResolveDependencies(parsed?.Dependencies),
                PermissionProfile = ResolvePermissionProfile(parsed?.Permissions),
                ManagedNetworkOverride = ResolveManagedNetworkOverride(parsed?.Permissions),
            };
        }
        catch
        {
            return new SkillMetadataValue();
        }
    }

    private static SkillDocumentFrontmatter ParseSkillDocumentFrontmatter(string yaml)
    {
        var result = new SkillDocumentFrontmatter();
        var metadata = new SkillDocumentMetadataSection();
        var hasMetadata = false;
        var section = string.Empty;

        foreach (var rawLine in ReadYamlContentLines(yaml))
        {
            var indent = CountLeadingSpaces(rawLine);
            var line = rawLine.Trim();
            if (indent == 0 && line.EndsWith(':'))
            {
                section = NormalizeYamlKey(line[..^1]);
                continue;
            }

            if (!TrySplitYamlKeyValue(line, out var key, out var value))
            {
                continue;
            }

            if (indent == 0)
            {
                section = string.Empty;
                switch (NormalizeYamlKey(key))
                {
                    case "name":
                        result.Name = value;
                        break;
                    case "description":
                        result.Description = value;
                        break;
                }

                continue;
            }

            if (string.Equals(section, "metadata", StringComparison.Ordinal))
            {
                switch (NormalizeYamlKey(key))
                {
                    case "short-description":
                        metadata.ShortDescription = value;
                        hasMetadata = true;
                        break;
                    case "short_description":
                        metadata.LegacyShortDescription = value;
                        hasMetadata = true;
                        break;
                }
            }
        }

        result.Metadata = hasMetadata ? metadata : null;
        return result;
    }

    private static SkillMetadataFile ParseSkillMetadataFile(string yaml)
    {
        var result = new SkillMetadataFile();
        var section = string.Empty;
        var nestedSection = string.Empty;
        SkillToolDependencySection? currentTool = null;
        var currentDomainList = string.Empty;

        foreach (var rawLine in ReadYamlContentLines(yaml))
        {
            var indent = CountLeadingSpaces(rawLine);
            var line = rawLine.Trim();

            if (indent == 0 && line.EndsWith(':'))
            {
                section = NormalizeYamlKey(line[..^1]);
                nestedSection = string.Empty;
                currentTool = null;
                currentDomainList = string.Empty;
                continue;
            }

            if (section == "interface" && indent >= 2 && TrySplitYamlKeyValue(line, out var interfaceKey, out var interfaceValue))
            {
                result.Interface ??= new SkillInterfaceSection();
                ApplyInterfaceValue(result.Interface, interfaceKey, interfaceValue);
                continue;
            }

            if (section == "dependencies")
            {
                if (indent >= 2 && line == "tools:")
                {
                    result.Dependencies ??= new SkillDependenciesSection();
                    result.Dependencies.Tools ??= [];
                    currentTool = null;
                    continue;
                }

                if (indent >= 4 && line.StartsWith("- ", StringComparison.Ordinal))
                {
                    result.Dependencies ??= new SkillDependenciesSection();
                    result.Dependencies.Tools ??= [];
                    currentTool = new SkillToolDependencySection();
                    result.Dependencies.Tools.Add(currentTool);
                    var inline = line[2..].Trim();
                    if (TrySplitYamlKeyValue(inline, out var inlineKey, out var inlineValue))
                    {
                        ApplyToolDependencyValue(currentTool, inlineKey, inlineValue);
                    }

                    continue;
                }

                if (currentTool is not null && indent >= 6 && TrySplitYamlKeyValue(line, out var toolKey, out var toolValue))
                {
                    ApplyToolDependencyValue(currentTool, toolKey, toolValue);
                    continue;
                }
            }

            if (section == "permissions")
            {
                if (indent == 2 && line.EndsWith(':'))
                {
                    nestedSection = NormalizeYamlKey(line[..^1]);
                    currentDomainList = string.Empty;
                    if (nestedSection == "network")
                    {
                        result.Permissions ??= new SkillPermissionSection();
                        result.Permissions.Network ??= new SkillNetworkPermissionSection();
                    }

                    continue;
                }

                if (nestedSection == "network" && indent >= 4)
                {
                    result.Permissions ??= new SkillPermissionSection();
                    result.Permissions.Network ??= new SkillNetworkPermissionSection();
                    if (line.StartsWith("- ", StringComparison.Ordinal))
                    {
                        AddNetworkDomainValue(result.Permissions.Network, currentDomainList, line[2..]);
                        continue;
                    }

                    if (line.EndsWith(':'))
                    {
                        currentDomainList = NormalizeYamlKey(line[..^1]);
                        continue;
                    }

                    if (TrySplitYamlKeyValue(line, out var permissionKey, out var permissionValue))
                    {
                        ApplyNetworkPermissionValue(result.Permissions.Network, permissionKey, permissionValue);
                        currentDomainList = NormalizeYamlKey(permissionKey);
                    }
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> ReadYamlContentLines(string yaml)
    {
        using var reader = new StringReader(yaml);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var withoutComment = RemoveYamlComment(line).TrimEnd();
            if (!string.IsNullOrWhiteSpace(withoutComment))
            {
                yield return withoutComment;
            }
        }
    }

    private static string RemoveYamlComment(string line)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
            }
            else if (ch == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
            }
            else if (ch == '#' && !inSingleQuote && !inDoubleQuote)
            {
                return line[..index];
            }
        }

        return line;
    }

    private static bool TrySplitYamlKeyValue(string line, out string key, out string? value)
    {
        var separator = line.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
        {
            key = string.Empty;
            value = null;
            return false;
        }

        key = line[..separator].Trim();
        value = NormalizeYamlScalar(line[(separator + 1)..].Trim());
        return key.Length > 0;
    }

    private static string? NormalizeYamlScalar(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if ((trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            || (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\''))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static int CountLeadingSpaces(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

    private static string NormalizeYamlKey(string key)
        => key.Trim().ToLowerInvariant();

    private static void ApplyInterfaceValue(SkillInterfaceSection section, string key, string? value)
    {
        switch (NormalizeYamlKey(key))
        {
            case "display_name":
                section.DisplayName = value;
                break;
            case "short_description":
                section.ShortDescription = value;
                break;
            case "icon_small":
                section.IconSmall = value;
                break;
            case "icon_large":
                section.IconLarge = value;
                break;
            case "brand_color":
                section.BrandColor = value;
                break;
            case "default_prompt":
                section.DefaultPrompt = value;
                break;
        }
    }

    private static void ApplyToolDependencyValue(SkillToolDependencySection section, string key, string? value)
    {
        switch (NormalizeYamlKey(key))
        {
            case "type":
                section.Type = value;
                break;
            case "value":
                section.Value = value;
                break;
            case "description":
                section.Description = value;
                break;
            case "transport":
                section.Transport = value;
                break;
            case "command":
                section.Command = value;
                break;
            case "url":
                section.Url = value;
                break;
        }
    }

    private static void ApplyNetworkPermissionValue(SkillNetworkPermissionSection section, string key, string? value)
    {
        switch (NormalizeYamlKey(key))
        {
            case "enabled":
                section.Enabled = bool.TryParse(value, out var enabled) ? enabled : null;
                break;
            case "allowed_domains":
                AddNetworkDomainValue(section, "allowed_domains", value);
                break;
            case "denied_domains":
                AddNetworkDomainValue(section, "denied_domains", value);
                break;
        }
    }

    private static void AddNetworkDomainValue(SkillNetworkPermissionSection section, string listName, string? value)
    {
        var normalized = NormalizeYamlScalar(value ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        switch (NormalizeYamlKey(listName))
        {
            case "allowed_domains":
                section.AllowedDomains ??= [];
                section.AllowedDomains.Add(normalized!);
                break;
            case "denied_domains":
                section.DeniedDomains ??= [];
                section.DeniedDomains.Add(normalized!);
                break;
        }
    }

    private static SkillInterfaceInfo? ResolveInterface(SkillInterfaceSection? section, string? metadataDirectoryPath)
    {
        if (section is null)
        {
            return null;
        }

        var info = new SkillInterfaceInfo(
            NormalizeText(section.DisplayName),
            NormalizeText(section.ShortDescription),
            ResolveInterfacePath(metadataDirectoryPath, section.IconSmall),
            ResolveInterfacePath(metadataDirectoryPath, section.IconLarge),
            NormalizeText(section.BrandColor),
            NormalizeText(section.DefaultPrompt));
        return info.HasValues ? info : null;
    }

    private static SkillDependencies? ResolveDependencies(SkillDependenciesSection? section)
    {
        var tools = section?.Tools?
            .Select(static tool => ResolveDependency(tool))
            .Where(static tool => tool is not null)
            .Select(static tool => tool!)
            .ToArray() ?? [];
        return tools.Length == 0 ? null : new SkillDependencies(tools);
    }

    private static SkillToolDependency? ResolveDependency(SkillToolDependencySection? section)
    {
        var type = NormalizeText(section?.Type);
        var value = NormalizeText(section?.Value);
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new SkillToolDependency(
            type!,
            value!,
            NormalizeText(section?.Description),
            NormalizeText(section?.Transport),
            NormalizeText(section?.Command),
            NormalizeText(section?.Url));
    }

    private static SkillPermissionProfile? ResolvePermissionProfile(SkillPermissionSection? section)
        => section?.Network?.Enabled is null
            ? null
            : new SkillPermissionProfile(new SkillNetworkPermissionProfile(section.Network.Enabled));

    private static SkillManagedNetworkOverride? ResolveManagedNetworkOverride(SkillPermissionSection? section)
    {
        if (section?.Network is null)
        {
            return null;
        }

        var value = new SkillManagedNetworkOverride(
            NormalizeOptionalList(section.Network.AllowedDomains),
            NormalizeOptionalList(section.Network.DeniedDomains));
        return value.HasDomainOverrides ? value : null;
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
            return !string.IsNullOrWhiteSpace(metadataDirectoryPath) && !Path.IsPathRooted(normalized)
                ? Path.GetFullPath(Path.Combine(metadataDirectoryPath, normalized))
                : Path.GetFullPath(normalized);
        }
        catch
        {
            return normalized;
        }
    }

    private static string? ResolveSelectedSkillPath(string rootDirectory, string? selectedSkillPath, IReadOnlyList<SkillPackageDescriptor> packages)
    {
        if (packages.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(selectedSkillPath))
        {
            var normalized = Path.GetFullPath(Path.IsPathFullyQualified(selectedSkillPath)
                ? selectedSkillPath
                : Path.Combine(rootDirectory, selectedSkillPath));
            var selected = packages.FirstOrDefault(package => string.Equals(package.SkillMarkdownPath, normalized, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                return selected.SkillMarkdownPath;
            }
        }

        return packages[0].SkillMarkdownPath;
    }

    private static string ResolvePackageId(string rootDirectory, string packageDirectory)
    {
        var relative = Path.GetRelativePath(rootDirectory, packageDirectory)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        return relative.StartsWith("..", StringComparison.Ordinal)
            ? Path.GetFileName(packageDirectory)
            : relative;
    }

    private static string? ResolveSkillConfigPath(string? rawPath, string? sourceDirectory)
    {
        var normalized = NormalizePathValue(rawPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        const string skillScheme = "skill://";
        if (normalized.StartsWith(skillScheme, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[skillScheme.Length..];
        }

        if (!Path.IsPathRooted(normalized) && !string.IsNullOrWhiteSpace(sourceDirectory))
        {
            normalized = Path.Combine(sourceDirectory!, normalized);
        }

        return NormalizeSkillDocumentPath(normalized);
    }

    private static string NormalizeSkillDocumentPath(string skillPath)
    {
        var normalized = skillPath.Trim();
        const string skillScheme = "skill://";
        if (normalized.StartsWith(skillScheme, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[skillScheme.Length..];
        }

        return Path.GetFullPath(normalized);
    }

    private static bool AreEquivalentSkillPaths(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }

        try
        {
            return string.Equals(NormalizeSkillDocumentPath(left!), right, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool ContainsHiddenRelativeDirectory(string rootPath, string skillFilePath)
    {
        var relativePath = Path.GetRelativePath(rootPath, skillFilePath);
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1)
        {
            return false;
        }

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].StartsWith(".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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

    private static IReadOnlyList<string>? NormalizeOptionalList(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return null;
        }

        var normalized = values
            .Select(NormalizeText)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized.Length == 0 ? null : normalized;
    }

    private static TomlTable GetOrCreateTomlTable(TomlTable root, string key)
    {
        if (root.TryGetValue(key, out var existing) && existing is TomlTable table)
        {
            return table;
        }

        var created = new TomlTable();
        root[key] = created;
        return created;
    }

    private static TomlTableArray GetOrCreateTomlTableArray(TomlTable root, string key)
    {
        if (root.TryGetValue(key, out var existing) && existing is TomlTableArray tableArray)
        {
            return tableArray;
        }

        var created = new TomlTableArray();
        root[key] = created;
        return created;
    }

    private static void CleanupSkillsConfig(TomlTable root, TomlTable skills, TomlTableArray config)
    {
        if (config.Count == 0)
        {
            skills.Remove("config");
        }

        if (skills.Count == 0)
        {
            root.Remove("skills");
        }
    }

    private static string? NormalizePath(string? value)
    {
        var normalized = NormalizePathValue(value);
        if (normalized is null)
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(normalized);
        }
        catch
        {
            return null;
        }
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

    private static string? ReadString(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value as string : null;

    private static bool? ReadBoolean(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value as bool? : null;

    private static int GetScopeSortRank(string scope)
        => scope switch
        {
            "repo" => 0,
            "user" => 1,
            "system" => 2,
            "admin" => 3,
            _ => 4,
        };

    private sealed record SkillRootDescriptor(string Path, string Scope, string? Namespace);

    private sealed record SkillDocumentInfo(string? Name, string? Description, string? ShortDescription);

    private sealed class SkillMetadataFile
    {
        public SkillInterfaceSection? Interface { get; set; }

        public SkillDependenciesSection? Dependencies { get; set; }

        public SkillPermissionSection? Permissions { get; set; }
    }

    private sealed class SkillDocumentFrontmatter
    {
        public string? Name { get; set; }

        public string? Description { get; set; }

        public SkillDocumentMetadataSection? Metadata { get; set; }
    }

    private sealed class SkillDocumentMetadataSection
    {
        public string? ShortDescription { get; set; }

        public string? LegacyShortDescription { get; set; }
    }

    private sealed class SkillInterfaceSection
    {
        public string? DisplayName { get; set; }

        public string? ShortDescription { get; set; }

        public string? IconSmall { get; set; }

        public string? IconLarge { get; set; }

        public string? BrandColor { get; set; }

        public string? DefaultPrompt { get; set; }
    }

    private sealed class SkillDependenciesSection
    {
        public List<SkillToolDependencySection>? Tools { get; set; }
    }

    private sealed class SkillToolDependencySection
    {
        public string? Type { get; set; }

        public string? Value { get; set; }

        public string? Description { get; set; }

        public string? Transport { get; set; }

        public string? Command { get; set; }

        public string? Url { get; set; }
    }

    private sealed class SkillPermissionSection
    {
        public SkillNetworkPermissionSection? Network { get; set; }
    }

    private sealed class SkillNetworkPermissionSection
    {
        public bool? Enabled { get; set; }

        public List<string>? AllowedDomains { get; set; }

        public List<string>? DeniedDomains { get; set; }
    }
}

public sealed record SkillPackageProjection(
    string RootDirectory,
    string SkillRootDirectory,
    string ConfigPath,
    string? SelectedSkillPath,
    IReadOnlyList<SkillPackageDescriptor> Packages,
    SkillPackageDescriptor? SelectedPackage,
    IReadOnlyList<SkillPackageIssue> Issues);

public sealed record SkillPackageDescriptor(
    string Id,
    string Name,
    string DisplayName,
    string Description,
    string? ShortDescription,
    string SkillMarkdownPath,
    string PackageDirectory,
    string MetadataPath,
    string SourceRoot,
    string Scope,
    bool Enabled,
    SkillInterfaceInfo? Interface,
    SkillDependencies? Dependencies,
    SkillPermissionProfile? PermissionProfile,
    SkillManagedNetworkOverride? ManagedNetworkOverride,
    bool HasAssetsDirectory,
    bool HasScriptsDirectory,
    bool HasTemplatesDirectory)
{
    public bool IsSelected { get; init; }
}

public sealed record SkillPackageIssue(string Code, string Message, string? Path);

public sealed class SkillMetadataValue
{
    public SkillInterfaceInfo? Interface { get; init; }

    public SkillDependencies? Dependencies { get; init; }

    public SkillPermissionProfile? PermissionProfile { get; init; }

    public SkillManagedNetworkOverride? ManagedNetworkOverride { get; init; }
}

public sealed record SkillInterfaceInfo(
    string? DisplayName,
    string? ShortDescription,
    string? IconSmall,
    string? IconLarge,
    string? BrandColor,
    string? DefaultPrompt)
{
    public bool HasValues =>
        !string.IsNullOrWhiteSpace(DisplayName)
        || !string.IsNullOrWhiteSpace(ShortDescription)
        || !string.IsNullOrWhiteSpace(IconSmall)
        || !string.IsNullOrWhiteSpace(IconLarge)
        || !string.IsNullOrWhiteSpace(BrandColor)
        || !string.IsNullOrWhiteSpace(DefaultPrompt);
}

public sealed record SkillDependencies(IReadOnlyList<SkillToolDependency> Tools);

public sealed record SkillToolDependency(
    string Type,
    string Value,
    string? Description,
    string? Transport,
    string? Command,
    string? Url);

public sealed record SkillNetworkPermissionProfile(bool? Enabled);

public sealed record SkillPermissionProfile(SkillNetworkPermissionProfile? Network);

public sealed record SkillManagedNetworkOverride(
    IReadOnlyList<string>? AllowedDomains,
    IReadOnlyList<string>? DeniedDomains)
{
    public bool HasDomainOverrides => AllowedDomains is not null || DeniedDomains is not null;
}
