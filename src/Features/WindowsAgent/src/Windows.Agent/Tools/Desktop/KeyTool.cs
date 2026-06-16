using Microsoft.Extensions.Logging;
using System.ComponentModel;
using Windows.Agent.Interface;

namespace Windows.Agent.Tools.Desktop;

/// <summary>
/// 键盘按键工具。
/// </summary>
public class KeyTool
{
    private readonly IDesktopService _desktopService;
    private readonly ILogger<KeyTool> _logger;

    public KeyTool(IDesktopService desktopService, ILogger<KeyTool> logger)
    {
        _desktopService = desktopService;
        _logger = logger;
    }

    /// <summary>
    /// Press individual keyboard keys.
    /// </summary>
    /// <param name="key">The key to press (supports special keys like "enter", "escape", "tab", "space", "backspace", "delete", arrow keys ("up", "down", "left", "right"), function keys ("f1"-"f12"))</param>
    /// <returns>Result message</returns>
    [Description("Press individual keyboard keys")]
    public async Task<string> KeyAsync(
        [Description("The key to press")] string key)
    {
        _logger.LogInformation("Pressing key: {Key}", key);
        
        return await _desktopService.KeyAsync(key);
    }
}
