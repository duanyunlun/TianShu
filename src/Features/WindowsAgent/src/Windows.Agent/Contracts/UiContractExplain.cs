namespace Windows.Agent.Contracts;

public static class UiContractExplain
{
    public static object ToSummary(UiContract contract)
    {
        return new
        {
            contractVersion = contract.ContractVersion,
            app = new
            {
                name = contract.App.Name,
                processNames = contract.App.ProcessNames ?? Array.Empty<string>()
            },
            windows = contract.Windows.Select(kvp => new
            {
                id = kvp.Key,
                description = kvp.Value.Description,
                titleContains = kvp.Value.TitleContains,
                titleRegex = kvp.Value.TitleRegex,
                className = kvp.Value.ClassName
            }),
            controls = contract.Controls.Select(kvp => new
            {
                id = kvp.Key,
                windowId = kvp.Value.WindowId,
                description = kvp.Value.Description,
                locatorPriority = new[] { "uia", "ocr", "fallbackCoords" },
                uia = kvp.Value.Uia,
                ocr = kvp.Value.Ocr,
                fallbackCoords = kvp.Value.FallbackCoords
            }),
            assertions = contract.Assertions == null
                ? Array.Empty<object>()
                : contract.Assertions.Select(kvp => (object)new
                {
                    id = kvp.Key,
                    ocrText = kvp.Value.OcrText,
                    windowTitleContains = kvp.Value.WindowTitleContains,
                    logPattern = kvp.Value.LogPattern,
                    fileExists = kvp.Value.FileExists,
                    clipboardContains = kvp.Value.ClipboardContains
                }).ToArray()
        };
    }
}
