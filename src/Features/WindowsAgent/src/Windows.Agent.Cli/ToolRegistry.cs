using Microsoft.Extensions.DependencyInjection;
using Windows.Agent.Tools.Desktop;
using Windows.Agent.Tools.FileSystem;
using Windows.Agent.Tools.SystemControl;
using Windows.Agent.Tools.OCR;
using Windows.Agent.Tools.Contracts;
using Windows.Agent.Tools.Diagnostics;

namespace Windows.Agent.Cli;

internal static class ToolRegistry
{
    public static void Register(IServiceCollection services)
    {
        services.AddTransient<ClickTool>();
        services.AddTransient<ClipboardTool>();
        services.AddTransient<DragTool>();
        services.AddTransient<GetWindowInfoTool>();
        services.AddTransient<KeyTool>();
        services.AddTransient<LaunchTool>();
        services.AddTransient<MoveTool>();
        services.AddTransient<OpenBrowserTool>();
        services.AddTransient<PowershellTool>();
        services.AddTransient<ResizeTool>();
        services.AddTransient<ScrapeTool>();
        services.AddTransient<ScreenshotTool>();
        services.AddTransient<ScrollTool>();
        services.AddTransient<ShortcutTool>();
        services.AddTransient<StateTool>();
        services.AddTransient<SwitchTool>();
        services.AddTransient<TypeTool>();
        services.AddTransient<UIElementTool>();
        services.AddTransient<UiaTool>();
        services.AddTransient<WaitTool>();

        services.AddTransient<ReadFileTool>();
        services.AddTransient<WriteFileTool>();
        services.AddTransient<CreateFileTool>();
        services.AddTransient<CopyFileTool>();
        services.AddTransient<MoveFileTool>();
        services.AddTransient<DeleteFileTool>();
        services.AddTransient<GetFileInfoTool>();
        services.AddTransient<ListDirectoryTool>();
        services.AddTransient<CreateDirectoryTool>();
        services.AddTransient<DeleteDirectoryTool>();
        services.AddTransient<SearchFilesTool>();

        services.AddTransient<ExtractTextFromScreenTool>();
        services.AddTransient<ExtractTextFromRegionTool>();
        services.AddTransient<FindTextOnScreenTool>();
        services.AddTransient<GetTextCoordinatesTool>();
        services.AddTransient<ExtractTextFromFileTool>();

        services.AddTransient<BrightnessTool>();
        services.AddTransient<VolumeTool>();
        services.AddTransient<ResolutionTool>();

        services.AddTransient<ContractTool>();
        services.AddTransient<TailLogTool>();
    }
}
