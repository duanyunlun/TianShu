namespace TianShu.RuntimeComposition;

/// <summary>
/// AppHost Core Loop runtime config reader，负责按工作目录读取 routing 所需的运行时配置。
/// AppHost core-loop runtime config reader that reads routing runtime config for the current workspace.
/// </summary>
internal sealed class AppHostCoreLoopRuntimeConfigReader
{
    private readonly Func<string?, Dictionary<string, object?>> readRuntimeConfig;

    public AppHostCoreLoopRuntimeConfigReader(Func<string?, Dictionary<string, object?>> readRuntimeConfig)
        => this.readRuntimeConfig = readRuntimeConfig ?? throw new ArgumentNullException(nameof(readRuntimeConfig));

    public Dictionary<string, object?> Read(string? cwd)
        => readRuntimeConfig(cwd);
}
