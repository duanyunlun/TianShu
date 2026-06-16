using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Windows.Agent.Tools.Desktop;
using Windows.Agent.Tools.FileSystem;
using Windows.Agent.Tools.SystemControl;
using Windows.Agent.Tools.OCR;
using Windows.Agent.Tools.Contracts;
using Windows.Agent.Tools.Diagnostics;

namespace Windows.Agent.Cli;

internal static class CliDispatcher
{
    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
        => await RunAsync(args, services, Console.Out);

    public static async Task<int> RunAsync(string[] args, IServiceProvider services, TextWriter output)
    {
        CliInvocation? invocation = null;
        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintHelp(output);
                return 0;
            }

            var group = args[0].ToLowerInvariant();
            var action = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
            var optionArgs = args.Skip(2).ToArray();
            var options = CliOptions.Parse(optionArgs);

            var pretty = options.GetBool("pretty", false);
            var dangerous = options.GetBool("dangerous", false);
            var snapshotOnError = options.GetBool("snapshot-on-error", false);
            var session = options.GetString("session");

            invocation = new CliInvocation(
                group,
                action,
                options.ToDictionary(),
                pretty,
                dangerous,
                snapshotOnError,
                session);

            switch (group)
            {
                case "desktop":
                    return await RunDesktopAsync(invocation, options, services, output);
                case "fs":
                case "filesystem":
                    return await RunFileSystemAsync(invocation, options, services, output);
                case "ocr":
                    return await RunOcrAsync(invocation, options, services, output);
                case "sys":
                case "system":
                    return await RunSystemAsync(invocation, options, services, output);
                case "contract":
                    return await RunContractAsync(invocation, options, services, output);
                case "diag":
                    return await RunDiagAsync(invocation, options, services, output);
                default:
                    throw new ArgumentException($"Unknown command group: {group}");
            }
        }
        catch (Exception ex)
        {
            var pretty = invocation?.Pretty ?? args.Any(a => a.Equals("--pretty", StringComparison.OrdinalIgnoreCase));
            var (code, message) = MapCliException(ex);

            object input = invocation != null
                ? new { group = invocation.Group, action = invocation.Action, options = invocation.Options }
                : new { args };

            var payload = new
            {
                schemaVersion = "1.0",
                success = false,
                code,
                message,
                tool = (string?)null,
                input,
                result = (object?)null,
                error = new
                {
                    kind = ex.GetType().Name,
                    message = ex.Message,
                    stack = ex.ToString()
                },
                artifacts = Array.Empty<object>(),
                session = invocation?.Session
            };

            output.WriteLine(CliJson.Serialize(payload, pretty));
            return 1;
        }
    }

    private static bool IsHelp(string token)
        => token.Equals("help", StringComparison.OrdinalIgnoreCase) ||
           token.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
           token.Equals("--help", StringComparison.OrdinalIgnoreCase);

    private static void PrintHelp(TextWriter output)
    {
        output.WriteLine(
@"Windows Agent CLI (non-MCP)

用法：
  windows-agent|wa <group> <action> [options]

通用 options：
  --pretty             JSON 美化输出
  --dangerous          启用高危命令（默认禁用；涉及桌面交互/写文件/改系统设置/执行命令等）
  --snapshot-on-error  失败时自动采集 screenshot + state（作为 artifacts）
  --session <string>   运行会话 ID（用于日志/证据归档）

示例：
  windows-agent desktop click --x 100 --y 200 --dangerous
  wa desktop click --x 100 --y 200 --dangerous
  windows-agent fs read --path ""C:\temp\a.txt""

Desktop：
  desktop state [--vision true|false]
  desktop launch --name <app>
  desktop switch --name <app>
  desktop windowinfo --name <app>
  desktop resize --name <app> [--width <int>] [--height <int>] [--x <int>] [--y <int>]
  desktop click --x <int> --y <int> [--button left|right|middle] [--clicks <int>]
  desktop move --x <int> --y <int>
  desktop drag --startx <int> --starty <int> --endx <int> --endy <int>
  desktop scroll [--x <int>] [--y <int>] [--type vertical|horizontal] [--direction up|down|left|right] [--wheel <int>]
  desktop type --x <int> --y <int> --text <string> [--clear] [--enter] [--interpretSpecialCharacters]
  desktop key --key <string>
  desktop shortcut --keys <string> 例：""ctrl+shift+esc""
  desktop clipboard --mode copy|paste [--text <string>]
  desktop wait --seconds <int>
  desktop screenshot
  desktop openbrowser [--url <string>] [--search <string>]
  desktop scrape --url <string>
  desktop powershell --command <string>
  desktop ui-find --type text|className|automationId --value <string>
  desktop ui-props --x <int> --y <int>
  desktop ui-wait --type text|className|automationId --value <string> [--timeoutMs <int>]
  desktop uia-tree --window <titleRegex> [--depth <int>] [--backend uia3|uia2]
  desktop uia-find --window <titleRegex> --selector <string> [--limit <int>] [--backend uia3|uia2]
  desktop uia-invoke --window <titleRegex> --selector <string> [--backend uia3|uia2]
  desktop uia-setvalue --window <titleRegex> --selector <string> --value <string> [--backend uia3|uia2]

FileSystem：
  fs read --path <string>
  fs write --path <string> --content <string> [--append]
  fs create --path <string> --content <string>
  fs delete --path <string>
  fs copy --source <string> --destination <string> [--overwrite]
  fs move --source <string> --destination <string> [--overwrite]
  fs info --path <string>
  fs list --path <string> [--files true|false] [--dirs true|false] [--recursive]
  fs mkdir --path <string> [--parents]
  fs rmdir --path <string> [--recursive]
  fs search-name --directory <string> --pattern <string> [--recursive]
  fs search-ext --directory <string> --ext <string> [--recursive]

OCR：
  ocr screen
  ocr region --x <int> --y <int> --width <int> --height <int>
  ocr find --text <string>
  ocr coords --text <string>
  ocr file --path <string>

System：
  sys volume [--inc|--dec] [--percent <int>] [--mute true|false]
  sys brightness [--inc|--dec] [--percent <int>]
  sys resolution --type high|medium|low

Contract：
  contract validate --path <string>
  contract explain --path <string>

Diag：
  diag tail-log --path <string> [--lines <int>]

说明：
  CLI 内部调用现存 Windows.Agent.Tools.* 类（而不是直接调用 Service）。
");
    }

    private static async Task<int> WriteToolResultAsync(CliInvocation invocation, string tool, string raw, IServiceProvider services, TextWriter output)
    {
        var hasParsed = CliJson.TryParse(raw, out var parsed);
        var toolSuccess = GetToolSuccess(hasParsed ? parsed : (JsonElement?)null, raw);
        var code = toolSuccess ? "OK" : "TOOL_FAILED";
        var message = toolSuccess ? "OK" : GetToolMessage(hasParsed ? parsed : (JsonElement?)null, raw);

        string? session = invocation.Session;
        var artifacts = new List<object>();
        if (!toolSuccess && invocation.SnapshotOnError)
        {
            session ??= $"wa-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            artifacts.AddRange(await TryCollectFailureArtifactsAsync(session, services));
        }

        object? error = null;
        if (!toolSuccess)
        {
            error = new
            {
                kind = hasParsed && TryGetBoolProperty(parsed, "success", out _) ? "ToolReportedFailure" : "ToolOutputIndicatesFailure",
                message
            };
        }

        var input = new { group = invocation.Group, action = invocation.Action, options = invocation.Options };
        object payload = hasParsed
            ? new
            {
                schemaVersion = "1.0",
                success = toolSuccess,
                code,
                message,
                tool,
                input,
                result = new { raw, parsed },
                error,
                artifacts,
                session
            }
            : new
            {
                schemaVersion = "1.0",
                success = toolSuccess,
                code,
                message,
                tool,
                input,
                result = new { raw },
                error,
                artifacts,
                session
            };

        output.WriteLine(CliJson.Serialize(payload, invocation.Pretty));
        return toolSuccess ? 0 : 1;
    }

    private static async Task<int> RunDesktopAsync(CliInvocation invocation, CliOptions options, IServiceProvider services, TextWriter output)
    {
        var action = invocation.Action;
        if (RequiresDangerousDesktop(action))
        {
            EnsureDangerous("desktop", action, invocation.Dangerous);
        }

        switch (action)
        {
            case "click":
            {
                var tool = services.GetRequiredService<ClickTool>();
                var x = options.RequireInt("x");
                var y = options.RequireInt("y");
                var button = options.GetString("button", "left") ?? "left";
                var clicks = options.GetInt("clicks", 1);
                var raw = await tool.ClickAsync(x, y, button, clicks);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.ClickTool.ClickAsync", raw, services, output);
            }
            case "move":
            {
                var tool = services.GetRequiredService<MoveTool>();
                var x = options.RequireInt("x");
                var y = options.RequireInt("y");
                var raw = await tool.MoveAsync(x, y);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.MoveTool.MoveAsync", raw, services, output);
            }
            case "scroll":
            {
                var tool = services.GetRequiredService<ScrollTool>();
                var x = options.Has("x") ? options.RequireInt("x") : (int?)null;
                var y = options.Has("y") ? options.RequireInt("y") : (int?)null;
                var type = options.GetString("type", "vertical") ?? "vertical";
                var direction = options.GetString("direction", "down") ?? "down";
                var wheelTimes = options.GetInt("wheel", 1);
                var raw = await tool.ScrollAsync(x, y, type, direction, wheelTimes);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.ScrollTool.ScrollAsync", raw, services, output);
            }
            case "type":
            {
                var tool = services.GetRequiredService<TypeTool>();
                var x = options.RequireInt("x");
                var y = options.RequireInt("y");
                var text = options.RequireString("text");
                var clear = options.GetBool("clear", false);
                var pressEnter = options.GetBool("enter", false);
                var interpret = options.GetBool("interpretSpecialCharacters", false);
                var raw = await tool.TypeAsync(x, y, text, clear, pressEnter, interpret);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.TypeTool.TypeAsync", raw, services, output);
            }
            case "state":
            {
                var tool = services.GetRequiredService<StateTool>();
                var useVision = options.GetBool("vision", false);
                var raw = await tool.GetDesktopStateAsync(useVision);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.StateTool.GetDesktopStateAsync", raw, services, output);
            }
            case "launch":
            {
                var tool = services.GetRequiredService<LaunchTool>();
                var name = options.RequireString("name");
                var raw = await tool.LaunchAppAsync(name);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.LaunchTool.LaunchAppAsync", raw, services, output);
            }
            case "switch":
            {
                var tool = services.GetRequiredService<SwitchTool>();
                var name = options.RequireString("name");
                var raw = await tool.SwitchAppAsync(name);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.SwitchTool.SwitchAppAsync", raw, services, output);
            }
            case "windowinfo":
            {
                var tool = services.GetRequiredService<GetWindowInfoTool>();
                var name = options.RequireString("name");
                var raw = await tool.GetWindowInfoAsync(name);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.GetWindowInfoTool.GetWindowInfoAsync", raw, services, output);
            }
            case "powershell":
            {
                var tool = services.GetRequiredService<PowershellTool>();
                var command = options.RequireString("command");
                var raw = await tool.ExecuteCommandAsync(command);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.PowershellTool.ExecuteCommandAsync", raw, services, output);
            }
            case "screenshot":
            {
                var tool = services.GetRequiredService<ScreenshotTool>();
                var raw = await tool.TakeScreenshotAsync();
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.ScreenshotTool.TakeScreenshotAsync", raw, services, output);
            }
            case "openbrowser":
            {
                var tool = services.GetRequiredService<OpenBrowserTool>();
                var url = options.GetString("url");
                var search = options.GetString("search");
                var raw = await tool.OpenBrowserAsync(url, search);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.OpenBrowserTool.OpenBrowserAsync", raw, services, output);
            }
            case "scrape":
            {
                var tool = services.GetRequiredService<ScrapeTool>();
                var url = options.RequireString("url");
                var raw = await tool.ScrapeAsync(url);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.ScrapeTool.ScrapeAsync", raw, services, output);
            }
            case "wait":
            {
                var tool = services.GetRequiredService<WaitTool>();
                var seconds = options.RequireInt("seconds");
                var raw = await tool.WaitAsync(seconds);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.WaitTool.WaitAsync", raw, services, output);
            }
            case "clipboard":
            {
                var tool = services.GetRequiredService<ClipboardTool>();
                var mode = options.RequireString("mode");
                var text = options.GetString("text");
                var raw = await tool.ClipboardAsync(mode, text);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.ClipboardTool.ClipboardAsync", raw, services, output);
            }
            case "drag":
            {
                var tool = services.GetRequiredService<DragTool>();
                var startX = options.RequireInt("startx");
                var startY = options.RequireInt("starty");
                var endX = options.RequireInt("endx");
                var endY = options.RequireInt("endy");
                var raw = await tool.DragAsync(startX, startY, endX, endY);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.DragTool.DragAsync", raw, services, output);
            }
            case "resize":
            {
                var tool = services.GetRequiredService<ResizeTool>();
                var name = options.RequireString("name");
                var width = options.Has("width") ? options.RequireInt("width") : (int?)null;
                var height = options.Has("height") ? options.RequireInt("height") : (int?)null;
                var x = options.Has("x") ? options.RequireInt("x") : (int?)null;
                var y = options.Has("y") ? options.RequireInt("y") : (int?)null;
                var raw = await tool.ResizeAppAsync(name, width, height, x, y);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.ResizeTool.ResizeAppAsync", raw, services, output);
            }
            case "key":
            {
                var tool = services.GetRequiredService<KeyTool>();
                var key = options.RequireString("key");
                var raw = await tool.KeyAsync(key);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.KeyTool.KeyAsync", raw, services, output);
            }
            case "shortcut":
            {
                var tool = services.GetRequiredService<ShortcutTool>();
                var keysRaw = options.RequireString("keys");
                var keys = SplitKeys(keysRaw);
                var raw = await tool.ShortcutAsync(keys);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.ShortcutTool.ShortcutAsync", raw, services, output);
            }
            case "ui-find":
            {
                var tool = services.GetRequiredService<UIElementTool>();
                var type = options.RequireString("type");
                var value = options.RequireString("value");
                var normalizedType = type.ToLowerInvariant();
                string toolName;
                string raw;
                switch (normalizedType)
                {
                    case "text":
                        toolName = "Windows.Agent.Tools.Desktop.UIElementTool.FindElementByTextAsync";
                        raw = await tool.FindElementByTextAsync(value);
                        break;
                    case "classname":
                        toolName = "Windows.Agent.Tools.Desktop.UIElementTool.FindElementByClassNameAsync";
                        raw = await tool.FindElementByClassNameAsync(value);
                        break;
                    case "automationid":
                        toolName = "Windows.Agent.Tools.Desktop.UIElementTool.FindElementByAutomationIdAsync";
                        raw = await tool.FindElementByAutomationIdAsync(value);
                        break;
                    default:
                        throw new ArgumentException("Invalid --type. Allowed: text, className, automationId");
                }

                return await WriteToolResultAsync(invocation, toolName, raw, services, output);
            }
            case "ui-props":
            {
                var tool = services.GetRequiredService<UIElementTool>();
                var x = options.RequireInt("x");
                var y = options.RequireInt("y");
                var raw = await tool.GetElementPropertiesAsync(x, y);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.UIElementTool.GetElementPropertiesAsync", raw, services, output);
            }
            case "ui-wait":
            {
                var tool = services.GetRequiredService<UIElementTool>();
                var type = options.RequireString("type");
                var value = options.RequireString("value");
                var timeoutMs = options.GetInt("timeoutMs", 5000);
                var raw = await tool.WaitForElementAsync(value, type, timeoutMs);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.UIElementTool.WaitForElementAsync", raw, services, output);
            }
            case "uia-tree":
            {
                var tool = services.GetRequiredService<UiaTool>();
                var window = options.RequireString("window");
                var depth = options.GetInt("depth", 3);
                var backend = options.GetString("backend", "uia3") ?? "uia3";
                var raw = await tool.GetTreeAsync(window, depth, backend);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.UiaTool.GetTreeAsync", raw, services, output);
            }
            case "uia-find":
            {
                var tool = services.GetRequiredService<UiaTool>();
                var window = options.RequireString("window");
                var selector = options.RequireString("selector");
                var limit = options.GetInt("limit", 5);
                var backend = options.GetString("backend", "uia3") ?? "uia3";
                var raw = await tool.FindAsync(window, selector, limit, backend);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.UiaTool.FindAsync", raw, services, output);
            }
            case "uia-invoke":
            {
                var tool = services.GetRequiredService<UiaTool>();
                var window = options.RequireString("window");
                var selector = options.RequireString("selector");
                var backend = options.GetString("backend", "uia3") ?? "uia3";
                var raw = await tool.InvokeAsync(window, selector, backend);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.UiaTool.InvokeAsync", raw, services, output);
            }
            case "uia-setvalue":
            {
                var tool = services.GetRequiredService<UiaTool>();
                var window = options.RequireString("window");
                var selector = options.RequireString("selector");
                var value = options.RequireString("value");
                var backend = options.GetString("backend", "uia3") ?? "uia3";
                var raw = await tool.SetValueAsync(window, selector, value, backend);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Desktop.UiaTool.SetValueAsync", raw, services, output);
            }
            default:
                throw new ArgumentException($"Unknown desktop action: {action}");
        }
    }

    private static async Task<int> RunFileSystemAsync(CliInvocation invocation, CliOptions options, IServiceProvider services, TextWriter output)
    {
        var action = invocation.Action;
        if (RequiresDangerousFileSystem(action))
        {
            EnsureDangerous("fs", action, invocation.Dangerous);
        }

        switch (action)
        {
            case "read":
            {
                var tool = services.GetRequiredService<ReadFileTool>();
                var path = options.RequireString("path");
                var raw = await tool.ReadFileAsync(path);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.FileSystem.ReadFileTool.ReadFileAsync", raw, services, output);
            }
            case "write":
            {
                var tool = services.GetRequiredService<WriteFileTool>();
                var path = options.RequireString("path");
                var content = options.RequireString("content");
                var append = options.GetBool("append", false);
                var raw = await tool.WriteFileAsync(path, content, append);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.FileSystem.WriteFileTool.WriteFileAsync", raw, services, output);
            }
            case "create":
            {
                var tool = services.GetRequiredService<CreateFileTool>();
                var path = options.RequireString("path");
                var content = options.RequireString("content");
                var raw = await tool.CreateFileAsync(path, content);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.FileSystem.CreateFileTool.CreateFileAsync", raw, services, output);
            }
            case "delete":
            {
                var tool = services.GetRequiredService<DeleteFileTool>();
                var path = options.RequireString("path");
                var raw = await tool.DeleteFileAsync(path);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.FileSystem.DeleteFileTool.DeleteFileAsync", raw, services, output);
            }
            case "copy":
            {
                var tool = services.GetRequiredService<CopyFileTool>();
                var source = options.RequireString("source");
                var destination = options.RequireString("destination");
                var overwrite = options.GetBool("overwrite", false);
                var raw = await tool.CopyFileAsync(source, destination, overwrite);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.FileSystem.CopyFileTool.CopyFileAsync", raw, services, output);
            }
            case "move":
            {
                var tool = services.GetRequiredService<MoveFileTool>();
                var source = options.RequireString("source");
                var destination = options.RequireString("destination");
                var overwrite = options.GetBool("overwrite", false);
                var raw = await tool.MoveFileAsync(source, destination, overwrite);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.FileSystem.MoveFileTool.MoveFileAsync", raw, services, output);
            }
            case "info":
            {
                var tool = services.GetRequiredService<GetFileInfoTool>();
                var path = options.RequireString("path");
                var raw = await tool.GetFileInfoAsync(path);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.FileSystem.GetFileInfoTool.GetFileInfoAsync", raw, services, output);
            }
            case "list":
            {
                var tool = services.GetRequiredService<ListDirectoryTool>();
                var path = options.RequireString("path");
                var includeFiles = options.GetBool("files", true);
                var includeDirs = options.GetBool("dirs", true);
                var recursive = options.GetBool("recursive", false);
                var raw = await tool.ListDirectoryAsync(path, includeFiles, includeDirs, recursive);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.FileSystem.ListDirectoryTool.ListDirectoryAsync", raw, services, output);
            }
            case "mkdir":
            {
                var tool = services.GetRequiredService<CreateDirectoryTool>();
                var path = options.RequireString("path");
                var parents = options.GetBool("parents", true);
                var raw = await tool.CreateDirectoryAsync(path, parents);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.FileSystem.CreateDirectoryTool.CreateDirectoryAsync", raw, services, output);
            }
            case "rmdir":
            {
                var tool = services.GetRequiredService<DeleteDirectoryTool>();
                var path = options.RequireString("path");
                var recursive = options.GetBool("recursive", false);
                var raw = await tool.DeleteDirectoryAsync(path, recursive);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.FileSystem.DeleteDirectoryTool.DeleteDirectoryAsync", raw, services, output);
            }
            case "search-name":
            {
                var tool = services.GetRequiredService<SearchFilesTool>();
                var directory = options.RequireString("directory");
                var pattern = options.RequireString("pattern");
                var recursive = options.GetBool("recursive", false);
                var raw = await tool.SearchFilesByNameAsync(directory, pattern, recursive);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.FileSystem.SearchFilesTool.SearchFilesByNameAsync", raw, services, output);
            }
            case "search-ext":
            {
                var tool = services.GetRequiredService<SearchFilesTool>();
                var directory = options.RequireString("directory");
                var ext = options.RequireString("ext");
                var recursive = options.GetBool("recursive", false);
                var raw = await tool.SearchFilesByExtensionAsync(directory, ext, recursive);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.FileSystem.SearchFilesTool.SearchFilesByExtensionAsync", raw, services, output);
            }
            default:
                throw new ArgumentException($"Unknown filesystem action: {action}");
        }
    }

    private static async Task<int> RunOcrAsync(CliInvocation invocation, CliOptions options, IServiceProvider services, TextWriter output)
    {
        var action = invocation.Action;
        switch (action)
        {
            case "screen":
            {
                var tool = services.GetRequiredService<ExtractTextFromScreenTool>();
                var raw = await tool.ExtractTextFromScreenAsync();
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.OCR.ExtractTextFromScreenTool.ExtractTextFromScreenAsync", raw, services, output);
            }
            case "region":
            {
                var tool = services.GetRequiredService<ExtractTextFromRegionTool>();
                var x = options.RequireInt("x");
                var y = options.RequireInt("y");
                var width = options.RequireInt("width");
                var height = options.RequireInt("height");
                var raw = await tool.ExtractTextFromRegionAsync(x, y, width, height);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.OCR.ExtractTextFromRegionTool.ExtractTextFromRegionAsync", raw, services, output);
            }
            case "find":
            {
                var tool = services.GetRequiredService<FindTextOnScreenTool>();
                var text = options.RequireString("text");
                var raw = await tool.FindTextOnScreenAsync(text);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.OCR.FindTextOnScreenTool.FindTextOnScreenAsync", raw, services, output);
            }
            case "coords":
            {
                var tool = services.GetRequiredService<GetTextCoordinatesTool>();
                var text = options.RequireString("text");
                var raw = await tool.GetTextCoordinatesAsync(text);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.OCR.GetTextCoordinatesTool.GetTextCoordinatesAsync", raw, services, output);
            }
            case "file":
            {
                var tool = services.GetRequiredService<ExtractTextFromFileTool>();
                var path = options.RequireString("path");
                var raw = await tool.ExtractTextFromFileAsync(path);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.OCR.ExtractTextFromFileTool.ExtractTextFromFileAsync", raw, services, output);
            }
            default:
                throw new ArgumentException($"Unknown ocr action: {action}");
        }
    }

    private static async Task<int> RunSystemAsync(CliInvocation invocation, CliOptions options, IServiceProvider services, TextWriter output)
    {
        var action = invocation.Action;
        switch (action)
        {
            case "volume":
            {
                if (options.Has("inc") || options.Has("dec") || options.Has("percent") || options.Has("mute"))
                {
                    EnsureDangerous("sys", action, invocation.Dangerous);
                }

                var tool = services.GetRequiredService<VolumeTool>();
                if (options.Has("mute"))
                {
                    var mute = options.GetBool("mute", true);
                    var rawMute = await tool.SetMuteStateAsync(mute);
                    return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.SystemControl.VolumeTool.SetMuteStateAsync", rawMute, services, output);
                }
                if (options.Has("percent"))
                {
                    var percent = options.RequireInt("percent");
                    var raw = await tool.SetVolumePercentAsync(percent);
                    return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.SystemControl.VolumeTool.SetVolumePercentAsync", raw, services, output);
                }
                if (options.Has("inc"))
                {
                    var raw = await tool.SetVolumeAsync(true);
                    return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.SystemControl.VolumeTool.SetVolumeAsync", raw, services, output);
                }
                if (options.Has("dec"))
                {
                    var raw = await tool.SetVolumeAsync(false);
                    return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.SystemControl.VolumeTool.SetVolumeAsync", raw, services, output);
                }
                var rawGet = await tool.GetCurrentVolumeAsync();
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.SystemControl.VolumeTool.GetCurrentVolumeAsync", rawGet, services, output);
            }
            case "brightness":
            {
                if (options.Has("inc") || options.Has("dec") || options.Has("percent"))
                {
                    EnsureDangerous("sys", action, invocation.Dangerous);
                }

                var tool = services.GetRequiredService<BrightnessTool>();
                if (options.Has("percent"))
                {
                    var percent = options.RequireInt("percent");
                    var raw = await tool.SetBrightnessPercentAsync(percent);
                    return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.SystemControl.BrightnessTool.SetBrightnessPercentAsync", raw, services, output);
                }
                if (options.Has("inc"))
                {
                    var raw = await tool.SetBrightnessAsync(true);
                    return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.SystemControl.BrightnessTool.SetBrightnessAsync", raw, services, output);
                }
                if (options.Has("dec"))
                {
                    var raw = await tool.SetBrightnessAsync(false);
                    return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.SystemControl.BrightnessTool.SetBrightnessAsync", raw, services, output);
                }
                var rawGet = await tool.GetCurrentBrightnessAsync();
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.SystemControl.BrightnessTool.GetCurrentBrightnessAsync", rawGet, services, output);
            }
            case "resolution":
            {
                EnsureDangerous("sys", action, invocation.Dangerous);
                var tool = services.GetRequiredService<ResolutionTool>();
                var type = options.RequireString("type");
                var raw = await tool.SetResolutionAsync(type);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.SystemControl.ResolutionTool.SetResolutionAsync", raw, services, output);
            }
            default:
                throw new ArgumentException($"Unknown system action: {action}");
        }
    }

    private static async Task<int> RunContractAsync(CliInvocation invocation, CliOptions options, IServiceProvider services, TextWriter output)
    {
        var action = invocation.Action;
        switch (action)
        {
            case "validate":
            {
                var tool = services.GetRequiredService<ContractTool>();
                var path = options.RequireString("path");
                var raw = await tool.ValidateAsync(path);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Contracts.ContractTool.ValidateAsync", raw, services, output);
            }
            case "explain":
            {
                var tool = services.GetRequiredService<ContractTool>();
                var path = options.RequireString("path");
                var raw = await tool.ExplainAsync(path);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Contracts.ContractTool.ExplainAsync", raw, services, output);
            }
            default:
                throw new ArgumentException($"Unknown contract action: {action}");
        }
    }

    private static async Task<int> RunDiagAsync(CliInvocation invocation, CliOptions options, IServiceProvider services, TextWriter output)
    {
        var action = invocation.Action;
        switch (action)
        {
            case "tail-log":
            {
                var tool = services.GetRequiredService<TailLogTool>();
                var path = options.RequireString("path");
                var lines = options.GetInt("lines", 200);
                var raw = await tool.TailAsync(path, lines);
                return await WriteToolResultAsync(invocation, "Windows.Agent.Tools.Diagnostics.TailLogTool.TailAsync", raw, services, output);
            }
            default:
                throw new ArgumentException($"Unknown diag action: {action}");
        }
    }

    private static void EnsureDangerous(string group, string action, bool dangerous)
    {
        if (dangerous)
        {
            return;
        }

        throw new InvalidOperationException($"Command requires --dangerous: {group} {action}");
    }

    private static bool RequiresDangerousDesktop(string action)
        => action is
            "launch" or "switch" or "resize" or
            "click" or "move" or "drag" or "scroll" or "type" or "key" or "shortcut" or "clipboard" or
            "openbrowser" or "powershell" or
            "uia-invoke" or "uia-setvalue";

    private static bool RequiresDangerousFileSystem(string action)
        => action is
            "write" or "create" or "delete" or "copy" or "move" or "mkdir" or "rmdir";

    private static (string Code, string Message) MapCliException(Exception ex)
    {
        if (ex is InvalidOperationException ioe && ioe.Message.Contains("--dangerous", StringComparison.OrdinalIgnoreCase))
        {
            return ("POLICY_DENIED", ex.Message);
        }

        if (ex is ArgumentException)
        {
            return ("BAD_ARGS", ex.Message);
        }

        return ("CLI_ERROR", ex.Message);
    }

    private static bool GetToolSuccess(JsonElement? parsed, string raw)
    {
        if (parsed is { ValueKind: JsonValueKind.Object } p &&
            TryGetBoolProperty(p, "success", out var success))
        {
            return success;
        }

        // 某些 Tool 返回普通字符串；做最小启发式判定（避免把明显错误当成成功）。
        var head = raw.TrimStart();
        if (head.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ||
            head.StartsWith("Failed", StringComparison.OrdinalIgnoreCase) ||
            head.StartsWith("Exception", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string GetToolMessage(JsonElement? parsed, string raw)
    {
        if (parsed is { ValueKind: JsonValueKind.Object } p)
        {
            if (p.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? raw;
            }

            if (p.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString() ?? raw;
            }
        }

        return raw;
    }

    private static bool TryGetBoolProperty(JsonElement obj, string name, out bool value)
    {
        if (obj.TryGetProperty(name, out var prop) &&
            (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
        {
            value = prop.GetBoolean();
            return true;
        }

        value = default;
        return false;
    }

    private static async Task<IReadOnlyList<object>> TryCollectFailureArtifactsAsync(string session, IServiceProvider services)
    {
        var artifacts = new List<object>();
        var root = Path.Combine(Path.GetTempPath(), "Windows.Agent", "artifacts", session);
        Directory.CreateDirectory(root);

        try
        {
            var screenshotTool = services.GetService<ScreenshotTool>();
            if (screenshotTool != null)
            {
                var screenshotPath = await screenshotTool.TakeScreenshotAsync();
                var fileName = $"screenshot-{DateTime.Now:HHmmss}.png";
                var dest = Path.Combine(root, fileName);
                var final = await TryCopyAsync(screenshotPath, dest) ? dest : screenshotPath;
                artifacts.Add(new { kind = "screenshot", path = final, note = "失败快照（自动采集）" });
            }
        }
        catch
        {
            // 采集失败不应影响主流程：尽最大努力采集即可。
        }

        try
        {
            var stateTool = services.GetService<StateTool>();
            if (stateTool != null)
            {
                var state = await stateTool.GetDesktopStateAsync(false);
                var dest = Path.Combine(root, "desktop_state.txt");
                await File.WriteAllTextAsync(dest, state);
                artifacts.Add(new { kind = "desktop_state", path = dest, note = "失败时前台窗口与可见窗口列表（自动采集）" });
            }
        }
        catch
        {
        }

        return artifacts;

        static async Task<bool> TryCopyAsync(string source, string dest)
        {
            try
            {
                if (!File.Exists(source))
                {
                    return false;
                }

                await using var src = File.OpenRead(source);
                await using var dst = File.Open(dest, FileMode.Create, FileAccess.Write, FileShare.None);
                await src.CopyToAsync(dst);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static string[] SplitKeys(string keys)
    {
        return keys
            .Split(new[] { '+', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private sealed record CliInvocation(
        string Group,
        string Action,
        IReadOnlyDictionary<string, string?> Options,
        bool Pretty,
        bool Dangerous,
        bool SnapshotOnError,
        string? Session);
}
