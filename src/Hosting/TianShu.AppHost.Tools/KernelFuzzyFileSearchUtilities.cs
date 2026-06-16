namespace TianShu.AppHost.Tools;

/// <summary>
/// fuzzy file search 搜索算法与会话辅助件。
/// Helpers for fuzzy file search session shaping and file-match scoring.
/// </summary>
internal static class KernelFuzzyFileSearchUtilities
{
    public static IReadOnlyList<string> NormalizeFuzzyFileSearchRoots(IReadOnlyList<string> roots, string? fallbackRoot = null)
    {
        var normalized = new List<string>(roots.Count > 0 ? roots.Count : 1);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            var value = KernelToolJsonHelpers.Normalize(root);
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
            {
                normalized.Add(value);
            }
        }

        if (normalized.Count > 0)
        {
            return normalized;
        }

        var fallback = KernelToolJsonHelpers.Normalize(fallbackRoot) ?? Environment.CurrentDirectory;
        normalized.Add(fallback);
        return normalized;
    }

    public static KernelFuzzyFileSearchSession CreateFuzzyFileSearchSession(
        string sessionId,
        IReadOnlyList<string> roots,
        string? query = null,
        string? fallbackRoot = null)
        => new(
            sessionId,
            NormalizeFuzzyFileSearchRoots(roots, fallbackRoot),
            KernelToolJsonHelpers.Normalize(query) ?? string.Empty);

    public static KernelFuzzyFileSearchSession UpdateFuzzyFileSearchSessionQuery(
        KernelFuzzyFileSearchSession session,
        string? query)
        => session with
        {
            Query = KernelToolJsonHelpers.Normalize(query) ?? string.Empty,
        };

    public static IReadOnlyList<KernelFuzzyFileSearchMatch> SearchFilesAcrossRoots(string? query, IReadOnlyList<string> roots, int limit)
    {
        var normalizedQuery = KernelToolJsonHelpers.Normalize(query) ?? string.Empty;
        var max = Math.Clamp(limit, 1, 200);
        var entries = new List<KernelFuzzyFileSearchMatch>(max);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in NormalizeFuzzyFileSearchRoots(roots))
        {
            if (entries.Count >= max)
            {
                break;
            }

            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (entries.Count >= max)
                {
                    break;
                }

                string relative;
                try
                {
                    relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
                {
                    continue;
                }

                var fileName = Path.GetFileName(relative);
                if (normalizedQuery.Length > 0
                    && relative.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) < 0
                    && fileName.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var dedup = $"{root}|{relative}";
                if (!seen.Add(dedup))
                {
                    continue;
                }

                entries.Add(new KernelFuzzyFileSearchMatch(
                    root,
                    relative,
                    fileName,
                    (uint)Math.Max(0, Math.Round(ComputeFileMatchScore(relative, normalizedQuery))),
                    ComputeMatchIndices(relative, normalizedQuery)));
            }
        }

        return entries
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToArray();
    }

    public static double ComputeFileMatchScore(string path, string? query)
    {
        var normalizedQuery = KernelToolJsonHelpers.Normalize(query) ?? string.Empty;
        if (normalizedQuery.Length == 0)
        {
            return 50d;
        }

        var index = path.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return 100d - Math.Min(index, 80d);
        }

        return Math.Max(1d, 30d - path.Length / 8d);
    }

    public static uint[]? ComputeMatchIndices(string path, string? query)
    {
        var normalizedQuery = KernelToolJsonHelpers.Normalize(query) ?? string.Empty;
        if (normalizedQuery.Length == 0)
        {
            return null;
        }

        var index = path.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var indices = new uint[normalizedQuery.Length];
        for (var i = 0; i < normalizedQuery.Length; i++)
        {
            indices[i] = (uint)(index + i);
        }

        return indices;
    }
}

internal sealed record KernelFuzzyFileSearchSession(string SessionId, IReadOnlyList<string> Roots, string Query);

internal sealed record KernelFuzzyFileSearchMatch(string Root, string Path, string FileName, uint Score, uint[]? Indices);
