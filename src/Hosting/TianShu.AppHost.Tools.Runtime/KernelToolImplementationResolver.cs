using System.Diagnostics;
using TianShu.Contracts.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal interface IToolCapabilityProbeService
{
    ToolCapabilityProbe Probe(ToolImplementationBinding binding);
}

internal sealed class KernelToolCapabilityProbeService : IToolCapabilityProbeService
{
    public ToolCapabilityProbe Probe(ToolImplementationBinding binding)
    {
        var failures = new List<string>();
        var optionalFailures = new List<string>();
        foreach (var requirement in binding.Requirements)
        {
            if (RequirementAvailable(requirement.Key))
            {
                continue;
            }

            var message = $"{requirement.Key} unavailable";
            if (requirement.Required)
            {
                failures.Add(message);
            }
            else
            {
                optionalFailures.Add(message);
            }
        }

        if (failures.Count > 0)
        {
            return new ToolCapabilityProbe(false, string.Join("; ", failures.Concat(optionalFailures)), DateTimeOffset.UtcNow);
        }

        return new ToolCapabilityProbe(true, optionalFailures.Count == 0 ? null : string.Join("; ", optionalFailures), DateTimeOffset.UtcNow);
    }

    private static bool RequirementAvailable(string key)
    {
        return NormalizeRequirementKey(key) switch
        {
            "file_system" => true,
            "network" => true,
            "gui_automation" => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux(),
            "rg" => CommandAvailable("rg", "--version"),
            "node" => CommandAvailable("node", "--version"),
            "powershell" => CommandAvailable("pwsh", "--version") || CommandAvailable("powershell", "-NoProfile", "-Command", "$PSVersionTable.PSVersion.ToString()"),
            "bash" => CommandAvailable("bash", "--version"),
            _ => false,
        };
    }

    private static string NormalizeRequirementKey(string key)
        => key.Trim().Replace('-', '_').ToLowerInvariant();

    private static bool CommandAvailable(string fileName, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            return process is not null && process.WaitForExit(2000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class KernelToolImplementationResolver
{
    private readonly PlatformToolProfile platformProfile;
    private readonly IToolCapabilityProbeService probeService;

    public KernelToolImplementationResolver()
        : this(KernelToolPlatformProfiles.Current(), new KernelToolCapabilityProbeService())
    {
    }

    public KernelToolImplementationResolver(
        PlatformToolProfile platformProfile,
        IToolCapabilityProbeService probeService)
    {
        this.platformProfile = platformProfile;
        this.probeService = probeService;
    }

    public ToolImplementationBinding Resolve(IKernelToolHandler handler)
    {
        var declared = handler.ImplementationBinding;
        if (platformProfile.DisabledToolKeys.Contains(handler.Name, StringComparer.Ordinal)
            || platformProfile.DisabledToolKeys.Contains(declared.ToolKey, StringComparer.Ordinal))
        {
            return WithProbe(declared, ToolImplementationKind.Unavailable, "disabled by platform profile");
        }

        if (platformProfile.EnabledToolKeys.Count > 0
            && !platformProfile.EnabledToolKeys.Contains(handler.Name, StringComparer.Ordinal)
            && !platformProfile.EnabledToolKeys.Contains(declared.ToolKey, StringComparer.Ordinal))
        {
            return WithProbe(declared, ToolImplementationKind.Unavailable, "not enabled by platform profile");
        }

        if (platformProfile.DefaultImplementationKinds.Count > 0
            && !platformProfile.DefaultImplementationKinds.Contains(declared.ImplementationKind))
        {
            return WithProbe(declared, ToolImplementationKind.Unavailable, $"implementation kind {declared.ImplementationKind} disabled by platform profile");
        }

        var probe = probeService.Probe(declared);
        return new ToolImplementationBinding(
            declared.ToolKey,
            probe.Available ? declared.ImplementationKind : ToolImplementationKind.Unavailable,
            declared.ImplementationId,
            declared.Requirements,
            probe,
            declared.FallbackPolicy,
            platformProfile);
    }

    public bool IsAvailable(IKernelToolHandler handler)
        => Resolve(handler).ImplementationKind != ToolImplementationKind.Unavailable;

    private ToolImplementationBinding WithProbe(ToolImplementationBinding declared, ToolImplementationKind kind, string reason)
    {
        return new ToolImplementationBinding(
            declared.ToolKey,
            kind,
            declared.ImplementationId,
            declared.Requirements,
            new ToolCapabilityProbe(kind != ToolImplementationKind.Unavailable, reason, DateTimeOffset.UtcNow),
            declared.FallbackPolicy,
            platformProfile);
    }
}

internal static class KernelToolPlatformProfiles
{
    public static PlatformToolProfile Current()
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsDesktop();
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacOSDesktop();
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxDesktop();
        }

        if (OperatingSystem.IsAndroid())
        {
            return Android();
        }

        return BrowserHosted();
    }

    public static PlatformToolProfile WindowsDesktop()
        => new("windows", defaultImplementationKinds:
        [
            ToolImplementationKind.Managed,
            ToolImplementationKind.ExternalProcess,
            ToolImplementationKind.McpStdio,
            ToolImplementationKind.McpHttp,
            ToolImplementationKind.ProviderHosted,
            ToolImplementationKind.PlatformNative,
        ]);

    public static PlatformToolProfile MacOSDesktop()
        => new("macos", defaultImplementationKinds:
        [
            ToolImplementationKind.Managed,
            ToolImplementationKind.ExternalProcess,
            ToolImplementationKind.McpStdio,
            ToolImplementationKind.McpHttp,
            ToolImplementationKind.ProviderHosted,
            ToolImplementationKind.PlatformNative,
        ]);

    public static PlatformToolProfile LinuxDesktop()
        => new("linux", defaultImplementationKinds:
        [
            ToolImplementationKind.Managed,
            ToolImplementationKind.ExternalProcess,
            ToolImplementationKind.McpStdio,
            ToolImplementationKind.McpHttp,
            ToolImplementationKind.ProviderHosted,
        ]);

    public static PlatformToolProfile Android()
        => new(
            "android",
            defaultImplementationKinds:
            [
                ToolImplementationKind.Managed,
                ToolImplementationKind.McpHttp,
                ToolImplementationKind.ProviderHosted,
                ToolImplementationKind.PlatformNative,
            ]);

    public static PlatformToolProfile BrowserHosted()
        => new(
            "browser-hosted",
            defaultImplementationKinds:
            [
                ToolImplementationKind.McpHttp,
                ToolImplementationKind.ProviderHosted,
            ]);
}
