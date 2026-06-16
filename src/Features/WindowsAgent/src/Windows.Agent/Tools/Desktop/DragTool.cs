using Microsoft.Extensions.Logging;
using System.ComponentModel;
using Windows.Agent.Interface;

namespace Windows.Agent.Tools.Desktop;

/// <summary>
/// 拖拽工具。
/// </summary>
public class DragTool
{
    private readonly IDesktopService _desktopService;
    private readonly ILogger<DragTool> _logger;

    public DragTool(IDesktopService desktopService, ILogger<DragTool> logger)
    {
        _desktopService = desktopService;
        _logger = logger;
    }

    /// <summary>
    /// Drag and drop operation from source coordinates to destination coordinates.
    /// </summary>
    /// <param name="fromX">Source X coordinate</param>
    /// <param name="fromY">Source Y coordinate</param>
    /// <param name="toX">Destination X coordinate</param>
    /// <param name="toY">Destination Y coordinate</param>
    /// <returns>Result message</returns>
    [Description("Drag and drop operation from source coordinates to destination coordinates")]
    public async Task<string> DragAsync(
        [Description("Source X coordinate")] int fromX,
        [Description("Source Y coordinate")] int fromY,
        [Description("Destination X coordinate")] int toX,
        [Description("Destination Y coordinate")] int toY)
    {
        _logger.LogInformation("Dragging from ({FromX},{FromY}) to ({ToX},{ToY})", fromX, fromY, toX, toY);
        
        return await _desktopService.DragAsync(fromX, fromY, toX, toY);
    }
}
