using Microsoft.Extensions.Logging;
using System.ComponentModel;
using Windows.Agent.Interface;

namespace Windows.Agent.Tools.Desktop;

/// <summary>
/// 快捷键工具。
/// </summary>
public class ShortcutTool
{
    private readonly IDesktopService _desktopService;
    private readonly ILogger<ShortcutTool> _logger;

    public ShortcutTool(IDesktopService desktopService, ILogger<ShortcutTool> logger)
    {
        _desktopService = desktopService;
        _logger = logger;
    }

    /// <summary>
    /// Execute keyboard shortcuts using key combinations.
    /// </summary>
    /// <param name="keys">Array of keys to press simultaneously (e.g., ["ctrl", "c"] for copy, ["alt", "tab"] for app switching, ["win", "r"] for Run dialog)</param>
    /// <returns>Result message</returns>
    [Description("Execute keyboard shortcuts using key combinations")]
    public async Task<string> ShortcutAsync(
        [Description("Array of keys to press simultaneously")] string[] keys)
    {
        _logger.LogInformation("Executing shortcut: {Keys}", string.Join("+", keys));
        
        return await _desktopService.ShortcutAsync(keys);
    }
}
