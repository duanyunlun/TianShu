using Microsoft.Extensions.Logging;
using System.ComponentModel;
using Windows.Agent.Interface;

namespace Windows.Agent.Tools.Desktop;

/// <summary>
/// 剪贴板工具。
/// </summary>
public class ClipboardTool
{
    private readonly IDesktopService _desktopService;
    private readonly ILogger<ClipboardTool> _logger;

    public ClipboardTool(IDesktopService desktopService, ILogger<ClipboardTool> logger)
    {
        _desktopService = desktopService;
        _logger = logger;
    }

    /// <summary>
    /// Copy text to clipboard or retrieve current clipboard content.
    /// </summary>
    /// <param name="mode">The mode: "copy" to copy text, "paste" to retrieve clipboard content</param>
    /// <param name="text">Text to copy (only required when mode is "copy")</param>
    /// <returns>Result message or clipboard content</returns>
    [Description("Copy text to clipboard or retrieve current clipboard content")]
    public async Task<string> ClipboardAsync(
        [Description("The mode: \"copy\" to copy text, \"paste\" to retrieve clipboard content")] string mode,
        [Description("The text to copy (required for copy mode)")] string? text = null)
    {
        _logger.LogInformation("Clipboard operation: {Mode}", mode);
        
        return await _desktopService.ClipboardOperationAsync(mode, text);
    }
}
