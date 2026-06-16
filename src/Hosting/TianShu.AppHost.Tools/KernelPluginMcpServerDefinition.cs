namespace TianShu.AppHost.Tools;

internal sealed record KernelPluginMcpServerDefinition(
    string Name,
    bool Enabled,
    string? Command,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<string> EnvVars,
    string? Cwd,
    string? Url,
    string? BearerTokenEnvVar,
    IReadOnlyDictionary<string, string> HttpHeaders,
    IReadOnlyDictionary<string, string> EnvHttpHeaders,
    TimeSpan? StartupTimeout,
    TimeSpan? ToolTimeout,
    IReadOnlyList<string> EnabledTools,
    IReadOnlyList<string> DisabledTools);
