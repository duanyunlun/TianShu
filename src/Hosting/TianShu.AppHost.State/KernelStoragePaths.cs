using TianShu.Configuration;

namespace TianShu.AppHost.State;

/// <summary>
/// 解析 AppHost 本地状态目录与线程存储文件路径。
/// Resolves the app host local state root and thread store file path.
/// </summary>
internal sealed record KernelStoragePaths(
    string StateDirectory,
    string ThreadStoreFilePath,
    string SessionsDirectory,
    string ArchivedSessionsDirectory)
{
    public static KernelStoragePaths ResolveDefault()
    {
        var stateDirectory = TianShuHomePathUtilities.ResolveTianShuStateRootPath();
        var sessionsDirectory = TianShuHomePathUtilities.ResolveTianShuSessionsRootPath();
        var archivedSessionsDirectory = Path.Combine(sessionsDirectory, "archived");
        var threadStorePath = Path.Combine(stateDirectory, "threads.json");
        return new KernelStoragePaths(
            stateDirectory,
            threadStorePath,
            sessionsDirectory,
            archivedSessionsDirectory);
    }
}
