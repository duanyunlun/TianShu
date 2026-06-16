using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Windows.Agent.Cli;
using Windows.Agent.Services;
using Windows.Agent.Interface;

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
Environment.ExitCode = await CliDispatcher.RunAsync(args, sp);
