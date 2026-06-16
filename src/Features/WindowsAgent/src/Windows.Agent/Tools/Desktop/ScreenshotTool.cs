using Microsoft.Extensions.Logging;
using System.ComponentModel;
using Windows.Agent.Interface;

namespace Windows.Agent.Tools.Desktop;

/// <summary>
/// 截图工具。
/// </summary>
public class ScreenshotTool
{
    private readonly IDesktopService _desktopService;
    private readonly ILogger<ScreenshotTool> _logger;

    public ScreenshotTool(IDesktopService desktopService, ILogger<ScreenshotTool> logger)
    {
        _desktopService = desktopService;
        _logger = logger;
    }

    /// <summary>
    /// Take a screenshot and save it to the temp directory.
    /// </summary>
    /// <returns>The file path of the saved screenshot</returns>
    [Description("Take a screenshot and save it to the temp directory")]
    public async Task<string> TakeScreenshotAsync()
    {
        _logger.LogInformation("Taking screenshot");
        
        return await _desktopService.TakeScreenshotAsync();
    }
}
