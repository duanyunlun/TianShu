using Microsoft.Extensions.Logging;
using System.ComponentModel;
using Windows.Agent.Interface;

namespace Windows.Agent.Tools.Desktop;

/// <summary>
/// PowerShell 执行工具。
/// </summary>
public class PowershellTool
{
    private readonly IDesktopService _desktopService;
    private readonly ILogger<PowershellTool> _logger;

    public PowershellTool(IDesktopService desktopService, ILogger<PowershellTool> logger)
    {
        _desktopService = desktopService;
        _logger = logger;
    }

    /// <summary>
    /// Execute PowerShell commands and return the output with status code.
    /// </summary>
    /// <param name="command">The PowerShell command to execute</param>
    /// <returns>Command output with status code</returns>
    [Description("Execute PowerShell commands and return the output with status code")]
    public async Task<string> ExecuteCommandAsync(
        [Description("The PowerShell command to execute")] string command)
    {
        _logger.LogInformation("Executing PowerShell command: {Command}", command);
        
        var (response, status) = await _desktopService.ExecuteCommandAsync(command);
        
        return $"Status Code: {status}\nResponse: {response}";
    }
}
