using Microsoft.Extensions.Logging;
using System.ComponentModel;
using Windows.Agent.Interface;

namespace Windows.Agent.Tools.Desktop;

/// <summary>
/// 桌面状态采集工具。
/// </summary>
public class StateTool
{
    private readonly IDesktopService _desktopService;
    private readonly ILogger<StateTool> _logger;

    public StateTool(IDesktopService desktopService, ILogger<StateTool> logger)
    {
        _desktopService = desktopService;
        _logger = logger;
    }

    /// <summary>
    /// Capture comprehensive desktop state including default language, focused/opened applications, 
    /// interactive UI elements, informative content, and scrollable areas.
    /// </summary>
    /// <param name="useVision">Whether to include visual screenshot when available</param>
    /// <returns>Desktop state information</returns>
    [Description("Capture comprehensive desktop state including applications, UI elements, and content")]
    public async Task<string> GetDesktopStateAsync(
        [Description("Whether to include visual screenshot when available")] bool useVision = false)
    {
        _logger.LogInformation("Getting desktop state with vision: {UseVision}", useVision);
        
        return await _desktopService.GetDesktopStateAsync(useVision);
    }
}
