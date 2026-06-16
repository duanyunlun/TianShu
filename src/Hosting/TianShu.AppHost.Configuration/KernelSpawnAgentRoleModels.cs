namespace TianShu.AppHost.Configuration;

/// <summary>
/// spawn-agent role 的静态配置定义。
/// Static definition of one spawn-agent role.
/// </summary>
internal sealed record KernelSpawnAgentRoleDefinition(
    string Name,
    string? Description,
    string? ResolvedConfigFilePath,
    string? EmbeddedConfigText,
    IReadOnlyList<string>? NicknameCandidates);

/// <summary>
/// spawn-agent role 锁定的运行时覆盖项。
/// Runtime overrides locked by a spawn-agent role.
/// </summary>
internal sealed record KernelSpawnAgentRoleOverrides(
    string? Model,
    string? ReasoningEffort,
    string? DeveloperInstructions);
