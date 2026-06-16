using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelUnifiedExecAvailability
{
    public static bool IsAllowed(
        JsonElement? sandboxPolicy,
        string? sandboxMode,
        KernelWindowsSandboxLevel windowsSandboxLevel = KernelWindowsSandboxLevel.Disabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        var mode = ResolveSandboxMode(sandboxPolicy, sandboxMode);
        return IsDangerFullAccess(mode)
               || IsExternalSandbox(mode)
               || windowsSandboxLevel == KernelWindowsSandboxLevel.Disabled;
    }

    public static string BuildBlockedMessage()
        => "unified exec is unavailable when Windows sandboxing is enabled";

    private static string ResolveSandboxMode(JsonElement? sandboxPolicy, string? sandboxMode)
    {
        var explicitMode = Normalize(sandboxMode);
        if (!string.IsNullOrWhiteSpace(explicitMode))
        {
            return explicitMode!;
        }

        if (sandboxPolicy is { ValueKind: JsonValueKind.Object } policy)
        {
            var type = Normalize(KernelToolJsonHelpers.ReadString(policy, "type"));
            if (!string.IsNullOrWhiteSpace(type))
            {
                return type!;
            }
        }

        return "workspaceWrite";
    }

    private static bool IsDangerFullAccess(string sandboxMode)
        => sandboxMode.Contains("danger", StringComparison.OrdinalIgnoreCase);

    private static bool IsExternalSandbox(string sandboxMode)
        => sandboxMode.Equals("externalSandbox", StringComparison.OrdinalIgnoreCase)
           || sandboxMode.Equals("external-sandbox", StringComparison.OrdinalIgnoreCase)
           || sandboxMode.Equals("external_sandbox", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
