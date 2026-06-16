using System.Text;
using System.Text.Json;
using TianShu.AppHost.Configuration;

namespace TianShu.AppHost.Tools;

/// <summary>
/// home/project 指令文档发现与拼装辅助件。
/// Helpers for home/project instruction discovery and composition.
/// </summary>
internal static class KernelInstructionScopeUtilities
{
    private const string DefaultProjectDocFilename = "AGENTS.md";
    private const string LocalProjectDocFilename = "AGENTS.override.md";
    private const long DefaultProjectDocMaxBytes = 32 * 1024;
    private const string ProjectDocSeparator = "\n\n--- project-doc ---\n\n";

    private sealed record KernelProjectDocScopeOptions(
        string[] CandidateFilenames,
        IReadOnlyList<string> ProjectRootMarkers,
        long MaxBytes);

    public static string? BuildScopedDeveloperInstructions(
        string? cwd,
        string? configuredDeveloperInstructions,
        Dictionary<string, object?>? scopedConfig = null)
    {
        _ = cwd;
        _ = scopedConfig;
        return Normalize(configuredDeveloperInstructions);
    }

    public static string? BuildScopedUserInstructions(
        string? cwd,
        Dictionary<string, object?>? scopedConfig = null,
        string? homePath = null)
    {
        var homeInstructions = BuildHomeInstructionSection(homePath);
        var projectDocs = BuildScopedInstructionFileSections(cwd, scopedConfig);
        if (string.IsNullOrWhiteSpace(homeInstructions))
        {
            return projectDocs;
        }

        if (string.IsNullOrWhiteSpace(projectDocs))
        {
            return homeInstructions;
        }

        return homeInstructions + ProjectDocSeparator + projectDocs;
    }

    public static string? SerializeUserInstructions(string? cwd, string? instructions)
    {
        var normalizedInstructions = Normalize(instructions);
        if (string.IsNullOrWhiteSpace(normalizedInstructions))
        {
            return null;
        }

        var directory = Normalize(cwd) ?? Environment.CurrentDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = ".";
        }

        try
        {
            directory = Path.GetFullPath(directory);
        }
        catch
        {
            // 使用原始 cwd 文本作为兜底。
        }

        return
            $"# AGENTS.md instructions for {directory}{Environment.NewLine}{Environment.NewLine}<INSTRUCTIONS>{Environment.NewLine}{normalizedInstructions}{Environment.NewLine}</INSTRUCTIONS>";
    }

    private static string? BuildHomeInstructionSection(string? homePath)
    {
        var resolvedHomePath = Normalize(homePath) ?? TianShuHomePathUtilities.ResolveTianShuHomePath();
        if (string.IsNullOrWhiteSpace(resolvedHomePath))
        {
            return null;
        }

        foreach (var fileName in new[] { LocalProjectDocFilename, DefaultProjectDocFilename })
        {
            var filePath = Path.Combine(resolvedHomePath!, fileName);
            try
            {
                if (!KernelPathUtilities.ExistsOrIsSymlinkFile(filePath))
                {
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                throw new InvalidOperationException($"发现 home instructions 失败：{filePath}", ex);
            }

            return TryReadInstructionContent(
                filePath,
                long.MaxValue,
                out var content,
                out _,
                errorKind: "home instructions")
                ? content
                : null;
        }

        return null;
    }

    private static string? BuildScopedInstructionFileSections(string? cwd, Dictionary<string, object?>? scopedConfig)
    {
        var options = ResolveProjectDocScopeOptions(scopedConfig);
        if (options.MaxBytes == 0)
        {
            return null;
        }

        var filePaths = EnumerateScopedInstructionFilePaths(cwd, options);
        if (filePaths.Count == 0)
        {
            return null;
        }

        var sections = new List<string>();
        var remainingBytes = options.MaxBytes;

        foreach (var filePath in filePaths)
        {
            if (remainingBytes == 0)
            {
                break;
            }

            if (!TryReadInstructionContent(filePath, remainingBytes, out var content, out var bytesRead))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            sections.Add(content);
            remainingBytes = Math.Max(remainingBytes - bytesRead, 0);
        }

        return sections.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static bool TryReadInstructionContent(
        string filePath,
        long maxBytes,
        out string? content,
        out long bytesRead,
        string errorKind = "project doc")
    {
        content = null;
        bytesRead = 0;
        if (maxBytes <= 0)
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            using var memory = new MemoryStream();
            var buffer = new byte[8 * 1024];
            while (bytesRead < maxBytes)
            {
                var chunkSize = (int)Math.Min(buffer.Length, maxBytes - bytesRead);
                var read = stream.Read(buffer, 0, chunkSize);
                if (read <= 0)
                {
                    break;
                }

                memory.Write(buffer, 0, read);
                bytesRead += read;
            }

            content = Normalize(Encoding.UTF8.GetString(memory.ToArray()));
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidOperationException($"读取 {errorKind} 失败：{filePath}", ex);
        }
    }

    private static List<string> EnumerateScopedInstructionFilePaths(string? cwd, KernelProjectDocScopeOptions options)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        foreach (var directory in TianShuProjectRootResolver.EnumerateDirectoriesBetweenProjectRootAndCwd(cwd, options.ProjectRootMarkers))
        {
            foreach (var fileName in options.CandidateFilenames)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                var candidatePath = Path.Combine(directory, fileName);
                try
                {
                    if (!KernelPathUtilities.ExistsOrIsSymlinkFile(candidatePath))
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    throw new InvalidOperationException($"发现 project doc 失败：{candidatePath}", ex);
                }

                var fullPath = Path.GetFullPath(candidatePath);
                if (seen.Add(fullPath))
                {
                    paths.Add(fullPath);
                }

                break;
            }
        }

        return paths;
    }

    private static KernelProjectDocScopeOptions ResolveProjectDocScopeOptions(Dictionary<string, object?>? scopedConfig)
    {
        var candidateFilenames = ResolveCandidateFilenames(scopedConfig);
        var projectRootMarkers = ResolveProjectRootMarkers(scopedConfig);
        var maxBytes = ResolveProjectDocMaxBytes(scopedConfig);
        return new KernelProjectDocScopeOptions(candidateFilenames, projectRootMarkers, maxBytes);
    }

    private static string[] ResolveCandidateFilenames(Dictionary<string, object?>? scopedConfig)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            LocalProjectDocFilename,
            DefaultProjectDocFilename,
        };
        var candidates = new List<string>(capacity: 2)
        {
            LocalProjectDocFilename,
            DefaultProjectDocFilename,
        };

        if (scopedConfig is null)
        {
            return candidates.ToArray();
        }

        var fallbackNames = ReadStringArrayExact(scopedConfig, "project_doc_fallback_filenames");
        foreach (var fallbackName in fallbackNames)
        {
            var name = Normalize(fallbackName);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (seen.Add(name))
            {
                candidates.Add(name);
            }
        }

        return candidates.ToArray();
    }

    private static IReadOnlyList<string> ResolveProjectRootMarkers(Dictionary<string, object?>? scopedConfig)
    {
        return scopedConfig is null
            ? TianShuProjectRootResolver.ResolveProjectRootMarkers()
            : TianShuProjectRootResolver.ResolveProjectRootMarkers(scopedConfig);
    }

    private static long ResolveProjectDocMaxBytes(Dictionary<string, object?>? scopedConfig)
    {
        if (scopedConfig is null)
        {
            return DefaultProjectDocMaxBytes;
        }

        if (!TryReadValueExact(scopedConfig, "project_doc_max_bytes", out var rawMaxBytes))
        {
            return DefaultProjectDocMaxBytes;
        }

        if (!TryReadLong(rawMaxBytes, out var maxBytes))
        {
            return DefaultProjectDocMaxBytes;
        }

        return maxBytes <= 0 ? 0 : maxBytes;
    }

    private static string[] ReadStringArrayExact(Dictionary<string, object?> config, string propertyName)
        => TryReadValueExact(config, propertyName, out var rawValue)
           && TryReadStringArray(rawValue, out var values)
            ? values
            : Array.Empty<string>();

    private static bool TryReadValueExact(Dictionary<string, object?> config, string propertyName, out object? value)
        => config.TryGetValue(propertyName, out value);

    private static bool TryReadLong(object? value, out long longValue)
    {
        switch (value)
        {
            case long typedLong:
                longValue = typedLong;
                return true;
            case int typedInt:
                longValue = typedInt;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var parsedLong):
                longValue = parsedLong;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out var parsedLongFromString):
                longValue = parsedLongFromString;
                return true;
            case string text when long.TryParse(text, out var parsedLongFromText):
                longValue = parsedLongFromText;
                return true;
            default:
                longValue = default;
                return false;
        }
    }

    private static bool TryReadStringArray(object? value, out string[] values)
    {
        if (value is string)
        {
            values = Array.Empty<string>();
            return false;
        }

        if (value is IEnumerable<object?> items)
        {
            values = items
                .Select(static item => TryReadString(item, out var text) ? Normalize(text) : null)
                .Where(static item => item is not null)
                .Cast<string>()
                .ToArray();
            return true;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            values = element
                .EnumerateArray()
                .Select(static item => item.ValueKind == JsonValueKind.String ? Normalize(item.GetString()) : null)
                .Where(static item => item is not null)
                .Cast<string>()
                .ToArray();
            return true;
        }

        values = Array.Empty<string>();
        return false;
    }

    private static bool TryReadString(object? value, out string text)
    {
        switch (value)
        {
            case string stringValue:
                text = stringValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                text = element.GetString() ?? string.Empty;
                return true;
            default:
                text = string.Empty;
                return false;
        }
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
}
