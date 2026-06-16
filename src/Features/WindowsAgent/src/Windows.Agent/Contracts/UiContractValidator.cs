using System.Text.RegularExpressions;

namespace Windows.Agent.Contracts;

public static class UiContractValidator
{
    public static UiContractValidationResult Validate(UiContract contract)
    {
        var result = new UiContractValidationResult();

        if (contract == null)
        {
            result.Errors.Add("contract: null");
            return result;
        }

        if (string.IsNullOrWhiteSpace(contract.ContractVersion))
        {
            result.Errors.Add("contractVersion: required");
        }

        if (contract.App == null || string.IsNullOrWhiteSpace(contract.App.Name))
        {
            result.Errors.Add("app.name: required");
        }

        if (contract.Windows == null || contract.Windows.Count == 0)
        {
            result.Errors.Add("windows: required and must not be empty");
        }
        else
        {
            foreach (var (windowId, window) in contract.Windows)
            {
                if (string.IsNullOrWhiteSpace(windowId))
                {
                    result.Errors.Add("windows: window id must not be empty");
                    continue;
                }

                if (window == null)
                {
                    result.Errors.Add($"windows.{windowId}: null");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(window.TitleContains) &&
                    string.IsNullOrWhiteSpace(window.TitleRegex) &&
                    string.IsNullOrWhiteSpace(window.ClassName))
                {
                    result.Errors.Add($"windows.{windowId}: require one of titleContains/titleRegex/className");
                }

                if (!string.IsNullOrWhiteSpace(window.TitleRegex))
                {
                    try
                    {
                        _ = new Regex(window.TitleRegex);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"windows.{windowId}.titleRegex: invalid regex ({ex.Message})");
                    }
                }
            }
        }

        if (contract.Controls == null || contract.Controls.Count == 0)
        {
            result.Errors.Add("controls: required and must not be empty");
        }
        else
        {
            foreach (var (controlId, control) in contract.Controls)
            {
                if (string.IsNullOrWhiteSpace(controlId))
                {
                    result.Errors.Add("controls: control id must not be empty");
                    continue;
                }

                if (control == null)
                {
                    result.Errors.Add($"controls.{controlId}: null");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(control.WindowId))
                {
                    result.Errors.Add($"controls.{controlId}.windowId: required");
                }
                else if (contract.Windows != null && contract.Windows.Count > 0 && !contract.Windows.ContainsKey(control.WindowId))
                {
                    result.Errors.Add($"controls.{controlId}.windowId: unknown window '{control.WindowId}'");
                }

                var hasUia = control.Uia != null && HasAny(control.Uia.AutomationId, control.Uia.Name, control.Uia.ControlType, control.Uia.Path);
                var hasOcr = control.Ocr != null && !string.IsNullOrWhiteSpace(control.Ocr.Text);
                var hasCoords = control.FallbackCoords != null;

                if (!hasUia && !hasOcr && !hasCoords)
                {
                    result.Errors.Add($"controls.{controlId}: require one locator of uia/ocr/fallbackCoords");
                }

                if (control.Ocr != null && string.IsNullOrWhiteSpace(control.Ocr.Text))
                {
                    result.Errors.Add($"controls.{controlId}.ocr.text: required when ocr is present");
                }
            }
        }

        if (contract.App?.ProcessNames != null && contract.App.ProcessNames.Length > 0)
        {
            var empties = contract.App.ProcessNames.Where(p => string.IsNullOrWhiteSpace(p)).ToArray();
            if (empties.Length > 0)
            {
                result.Errors.Add("app.processNames: must not contain empty entries");
            }
        }

        if (contract.Assertions != null && contract.Assertions.Count > 0)
        {
            foreach (var (assertionId, assertion) in contract.Assertions)
            {
                if (string.IsNullOrWhiteSpace(assertionId))
                {
                    result.Errors.Add("assertions: assertion id must not be empty");
                    continue;
                }

                if (assertion == null)
                {
                    result.Errors.Add($"assertions.{assertionId}: null");
                    continue;
                }

                if (!HasAny(assertion.OcrText, assertion.WindowTitleContains, assertion.LogPattern, assertion.FileExists, assertion.ClipboardContains))
                {
                    result.Warnings.Add($"assertions.{assertionId}: empty assertion definition (no fields set)");
                }
            }
        }

        return result;
    }

    private static bool HasAny(params string?[] values)
        => values.Any(v => !string.IsNullOrWhiteSpace(v));
}

