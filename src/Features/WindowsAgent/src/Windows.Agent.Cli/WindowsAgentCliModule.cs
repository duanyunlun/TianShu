using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Windows.Agent.Interface;
using Windows.Agent.Services;

namespace Windows.Agent.Cli;

/// <summary>
/// Windows-Agent CLI 作为库的对外入口：用于被 TianShu 宿主或独立调用方复用。
/// </summary>
public static class WindowsAgentCliModule
{
    public static async Task<int> RunAsync(string[] args, TextWriter? output = null)
    {
        args ??= Array.Empty<string>();
        output ??= Console.Out;

        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            // CLI 输出走 stdout；日志走 stderr，避免污染结果。
            builder.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        });

        // 复用现有实现（工具内部依赖这些 Service 接口）。
        services
            .AddSingleton<IDesktopService, DesktopService>()
            .AddSingleton<IFileSystemService, FileSystemService>()
            .AddSingleton<IOcrService, OcrService>()
            .AddSingleton<ISystemControlService, SystemControlService>()
            .AddSingleton<IUiaService, UiaService>();

        // Tool 作为可调用单元注册到容器中（CLI 内部通过 Tool 调用能力）。
        ToolRegistry.Register(services);

        using var sp = services.BuildServiceProvider();
        return await CliDispatcher.RunAsync(args, sp, output).ConfigureAwait(false);
    }
}


