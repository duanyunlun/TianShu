using Microsoft.Extensions.Logging;
using System.ComponentModel;
using Windows.Agent.Interface;

namespace Windows.Agent.Tools.SystemControl;

/// <summary>
/// 屏幕分辨率控制工具。
/// </summary>
public class ResolutionTool
{
    private readonly ISystemControlService _systemControlService;
    private readonly ILogger<ResolutionTool> _logger;

    public ResolutionTool(ISystemControlService systemControlService, ILogger<ResolutionTool> logger)
    {
        _systemControlService = systemControlService;
        _logger = logger;
    }

    /// <summary>
    /// Set screen resolution.
    /// </summary>
    /// <param name="type">Resolution type: "high", "medium", or "low"</param>
    /// <returns>Result message</returns>
    [Description("Set screen resolution")]
    public async Task<string> SetResolutionAsync(
        [Description("Resolution type: \"high\", \"medium\", or \"low\"")] string type)
    {
        _logger.LogInformation("Setting resolution to: {Type}", type);
        
        return await _systemControlService.SetResolutionAsync(type);
    }
}
