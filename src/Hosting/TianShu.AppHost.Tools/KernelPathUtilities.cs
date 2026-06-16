namespace TianShu.AppHost.Tools;

internal static class KernelPathUtilities
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal sealed record SymlinkWritePaths(string? ReadPath, string WritePath);

    public static bool AreEquivalentForComparison(string? left, string? right)
    {
        var normalizedLeft = TryNormalizeForComparison(left);
        var normalizedRight = TryNormalizeForComparison(right);
        return !string.IsNullOrWhiteSpace(normalizedLeft)
               && !string.IsNullOrWhiteSpace(normalizedRight)
               && PathComparer.Equals(normalizedLeft, normalizedRight);
    }

    public static string? TryNormalizeForComparison(string? path)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        var resolved = ResolveSymlinkWritePaths(normalizedPath!);
        return NormalizeComparablePath(resolved.ReadPath ?? resolved.WritePath);
    }

    public static SymlinkWritePaths ResolveSymlinkWritePaths(string path)
    {
        var root = NormalizeComparablePath(Path.GetFullPath(path));
        var current = root;
        var visited = new HashSet<string>(PathComparer);

        while (true)
        {
            if (!TryGetLinkTarget(current, out var exists, out var linkTarget))
            {
                return new SymlinkWritePaths(null, root);
            }

            if (!exists && string.IsNullOrWhiteSpace(linkTarget))
            {
                return new SymlinkWritePaths(current, current);
            }

            if (string.IsNullOrWhiteSpace(linkTarget))
            {
                return new SymlinkWritePaths(current, current);
            }

            if (!visited.Add(current))
            {
                return new SymlinkWritePaths(null, root);
            }

            current = ResolveLinkTarget(current, linkTarget!);
        }
    }

    public static string NormalizeSkillDocumentPath(string skillPath, string? baseDirectory = null)
    {
        var normalizedPath = NormalizePath(skillPath, baseDirectory)
            ?? throw new ArgumentException("Skill path must not be empty.", nameof(skillPath));
        var resolvedSkillPath = TryResolveSkillDocumentPath(normalizedPath) ?? normalizedPath;
        return TryNormalizeForComparison(resolvedSkillPath) ?? NormalizeComparablePath(resolvedSkillPath);
    }

    public static bool ExistsOrIsSymlinkFile(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            fileInfo.Refresh();
            return fileInfo.Exists || !string.IsNullOrWhiteSpace(fileInfo.LinkTarget);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static string? TryResolveSkillDocumentPath(string path)
    {
        if (Directory.Exists(path))
        {
            var resolvedDirectory = TryNormalizeForComparison(path) ?? NormalizeComparablePath(path);
            var skillDocumentPath = Path.Combine(resolvedDirectory, "SKILL.md");
            return ExistsOrIsSymlinkFile(skillDocumentPath)
                ? NormalizeComparablePath(skillDocumentPath)
                : resolvedDirectory;
        }

        if (string.Equals(Path.GetFileName(path), "SKILL.md", StringComparison.OrdinalIgnoreCase))
        {
            var parentDirectory = Path.GetDirectoryName(path);
            var resolvedDirectory = string.IsNullOrWhiteSpace(parentDirectory)
                ? null
                : TryNormalizeForComparison(parentDirectory);
            return string.IsNullOrWhiteSpace(resolvedDirectory)
                ? NormalizeComparablePath(path)
                : NormalizeComparablePath(Path.Combine(resolvedDirectory!, "SKILL.md"));
        }

        if (string.Equals(Path.GetFileName(path), "tianshu.yaml", StringComparison.OrdinalIgnoreCase))
        {
            var metadataDirectory = Path.GetDirectoryName(path);
            var skillDirectory = metadataDirectory is null
                ? null
                : Directory.GetParent(metadataDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(skillDirectory))
            {
                var resolvedDirectory = TryNormalizeForComparison(skillDirectory) ?? NormalizeComparablePath(skillDirectory);
                var skillDocumentPath = Path.Combine(resolvedDirectory, "SKILL.md");
                if (ExistsOrIsSymlinkFile(skillDocumentPath))
                {
                    return NormalizeComparablePath(skillDocumentPath);
                }
            }
        }

        return NormalizeComparablePath(path);
    }

    private static bool TryGetLinkTarget(string path, out bool exists, out string? linkTarget)
    {
        exists = false;
        linkTarget = null;

        try
        {
            FileSystemInfo info;
            if (Directory.Exists(path))
            {
                info = new DirectoryInfo(path);
            }
            else
            {
                info = new FileInfo(path);
            }

            info.Refresh();
            exists = info.Exists;
            linkTarget = Normalize(info.LinkTarget);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static string ResolveLinkTarget(string path, string linkTarget)
    {
        if (Path.IsPathRooted(linkTarget))
        {
            return NormalizeComparablePath(linkTarget);
        }

        var parentDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
        return NormalizeComparablePath(Path.Combine(parentDirectory, linkTarget));
    }

    private static string? NormalizePath(string? value, string? baseDirectory = null)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        if (Path.IsPathRooted(normalized))
        {
            return NormalizeComparablePath(normalized);
        }

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            return NormalizeComparablePath(Path.Combine(baseDirectory!, normalized));
        }

        return NormalizeComparablePath(normalized);
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

    private static string NormalizeComparablePath(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
