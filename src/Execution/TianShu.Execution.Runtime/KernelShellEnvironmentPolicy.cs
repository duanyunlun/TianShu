using System.Collections;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace TianShu.Execution.Runtime;

internal enum KernelShellEnvironmentPolicyInherit
{
    Core,
    All,
    None,
}

internal sealed class KernelShellEnvironmentPolicy
{
    public static KernelShellEnvironmentPolicy Default { get; } = new();

    public KernelShellEnvironmentPolicy(
        KernelShellEnvironmentPolicyInherit inherit = KernelShellEnvironmentPolicyInherit.All,
        bool ignoreDefaultExcludes = true,
        IReadOnlyList<string>? excludePatterns = null,
        IReadOnlyDictionary<string, string>? setVariables = null,
        IReadOnlyList<string>? includeOnlyPatterns = null,
        bool useProfile = false)
    {
        Inherit = inherit;
        IgnoreDefaultExcludes = ignoreDefaultExcludes;
        ExcludePatterns = (excludePatterns ?? Array.Empty<string>())
            .Where(static pattern => !string.IsNullOrWhiteSpace(pattern))
            .ToArray();
        IncludeOnlyPatterns = (includeOnlyPatterns ?? Array.Empty<string>())
            .Where(static pattern => !string.IsNullOrWhiteSpace(pattern))
            .ToArray();
        SetVariables = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(
            setVariables ?? new Dictionary<string, string>(StringComparer.Ordinal),
            StringComparer.Ordinal));
        UseProfile = useProfile;
    }

    public KernelShellEnvironmentPolicyInherit Inherit { get; }

    public bool IgnoreDefaultExcludes { get; }

    public IReadOnlyList<string> ExcludePatterns { get; }

    public IReadOnlyDictionary<string, string> SetVariables { get; }

    public IReadOnlyList<string> IncludeOnlyPatterns { get; }

    public bool UseProfile { get; }
}

internal static class KernelShellEnvironmentBuilder
{
    public const string ThreadIdEnvironmentVariable = "TIANSHU_THREAD_ID";

    private static readonly string[] DefaultExcludePatterns = ["*KEY*", "*SECRET*", "*TOKEN*"];
    private static readonly string[] UnixCoreEnvironmentVariables = ["HOME", "LOGNAME", "PATH", "SHELL", "USER", "USERNAME", "TMPDIR", "TEMP", "TMP"];
    private static readonly string[] WindowsCoreEnvironmentVariables =
    [
        "APPDATA",
        "ComSpec",
        "HOME",
        "HOMEDRIVE",
        "HOMEPATH",
        "LOCALAPPDATA",
        "LOGNAME",
        "PATH",
        "PATHEXT",
        "ProgramData",
        "SHELL",
        "SystemRoot",
        "TEMP",
        "TMP",
        "TMPDIR",
        "USER",
        "USERNAME",
        "USERPROFILE",
        "WINDIR",
    ];

    public static Dictionary<string, string> CreateEnvironment(
        KernelShellEnvironmentPolicy? policy,
        string? threadId,
        IReadOnlyDictionary<string, string>? sourceEnvironment = null)
    {
        var effectivePolicy = policy ?? KernelShellEnvironmentPolicy.Default;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var source = sourceEnvironment ?? ReadProcessEnvironment(comparer);
        Dictionary<string, string> environment = effectivePolicy.Inherit switch
        {
            KernelShellEnvironmentPolicyInherit.None => new Dictionary<string, string>(comparer),
            KernelShellEnvironmentPolicyInherit.Core => source
                .Where(static pair => IsCoreEnvironmentVariable(pair.Key))
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, comparer),
            _ => new Dictionary<string, string>(source, comparer),
        };

        if (!effectivePolicy.IgnoreDefaultExcludes)
        {
            environment = Filter(environment, DefaultExcludePatterns, keepMatches: false, comparer);
        }

        if (effectivePolicy.ExcludePatterns.Count > 0)
        {
            environment = Filter(environment, effectivePolicy.ExcludePatterns, keepMatches: false, comparer);
        }

        foreach (var variable in effectivePolicy.SetVariables)
        {
            environment[variable.Key] = variable.Value;
        }

        var dependencyEnvironment = KernelDependencyEnvironmentScope.Current;
        if (dependencyEnvironment is not null)
        {
            foreach (var pair in dependencyEnvironment)
            {
                environment[pair.Key] = pair.Value;
            }
        }

        if (effectivePolicy.IncludeOnlyPatterns.Count > 0)
        {
            environment = Filter(environment, effectivePolicy.IncludeOnlyPatterns, keepMatches: true, comparer);
        }

        if (!string.IsNullOrWhiteSpace(threadId))
        {
            environment[ThreadIdEnvironmentVariable] = threadId;
        }

        return environment;
    }

    private static Dictionary<string, string> ReadProcessEnvironment(StringComparer comparer)
    {
        var environment = new Dictionary<string, string>(comparer);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                environment[key] = value;
            }
        }

        return environment;
    }

    private static bool IsCoreEnvironmentVariable(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsCoreEnvironmentVariables.Any(allowed => string.Equals(allowed, name, StringComparison.OrdinalIgnoreCase));
        }

        return UnixCoreEnvironmentVariables.Contains(name, StringComparer.Ordinal);
    }

    private static Dictionary<string, string> Filter(
        IReadOnlyDictionary<string, string> source,
        IReadOnlyList<string> patterns,
        bool keepMatches,
        StringComparer comparer)
    {
        var filtered = new Dictionary<string, string>(comparer);
        foreach (var variable in source)
        {
            var matches = patterns.Any(pattern => MatchesPattern(variable.Key, pattern));
            if ((keepMatches && matches) || (!keepMatches && !matches))
            {
                filtered[variable.Key] = variable.Value;
            }
        }

        return filtered;
    }

    private static bool MatchesPattern(string value, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
