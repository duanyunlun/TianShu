using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Windows.Agent.Contracts;

namespace Windows.Agent.Tools.Contracts;

/// <summary>
/// UI Contract（对象库）工具：用于加载/校验/解释契约文件（YAML/JSON）。
/// </summary>
public sealed class ContractTool
{
    private readonly ILogger<ContractTool> _logger;

    public ContractTool(ILogger<ContractTool> logger)
    {
        _logger = logger;
    }

    [Description("Validate a UI contract file (YAML/JSON)")]
    public async Task<string> ValidateAsync(
        [Description("Path to UI contract file (.yml/.yaml/.json)")] string path)
    {
        try
        {
            _logger.LogInformation("Validating UI contract: {Path}", path);

            var contract = await UiContractLoader.LoadFromFileAsync(path);
            var validation = UiContractValidator.Validate(contract);

            var payload = new
            {
                success = validation.IsValid,
                path,
                contractVersion = contract.ContractVersion,
                app = new { name = contract.App.Name, processNames = contract.App.ProcessNames ?? Array.Empty<string>() },
                summary = new
                {
                    windowCount = contract.Windows.Count,
                    controlCount = contract.Controls.Count,
                    assertionCount = contract.Assertions?.Count ?? 0
                },
                errors = validation.Errors,
                warnings = validation.Warnings
            };

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate UI contract");
            var payload = new
            {
                success = false,
                path,
                errors = new[] { ex.Message }
            };
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    [Description("Explain a UI contract file (YAML/JSON) and output a summary for audit")]
    public async Task<string> ExplainAsync(
        [Description("Path to UI contract file (.yml/.yaml/.json)")] string path)
    {
        try
        {
            _logger.LogInformation("Explaining UI contract: {Path}", path);

            var contract = await UiContractLoader.LoadFromFileAsync(path);
            var validation = UiContractValidator.Validate(contract);

            var payload = new
            {
                success = true,
                path,
                isValid = validation.IsValid,
                errors = validation.Errors,
                warnings = validation.Warnings,
                contract = UiContractExplain.ToSummary(contract)
            };

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to explain UI contract");
            var payload = new
            {
                success = false,
                path,
                error = ex.Message
            };
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}

