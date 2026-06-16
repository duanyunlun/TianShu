using System.Text.Json.Serialization;

namespace Windows.Agent.Contracts;

/// <summary>
/// UI Contract（对象库）用于把窗口/控件/断言结构化，便于编排器把自然语言用例编译为可执行步骤。
/// </summary>
public sealed class UiContract
{
    [JsonPropertyName("contractVersion")]
    public string ContractVersion { get; init; } = string.Empty;

    [JsonPropertyName("app")]
    public UiContractApp App { get; init; } = new();

    [JsonPropertyName("windows")]
    public Dictionary<string, UiContractWindow> Windows { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("controls")]
    public Dictionary<string, UiContractControl> Controls { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("assertions")]
    public Dictionary<string, UiContractAssertion>? Assertions { get; init; }
}

public sealed class UiContractApp
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("processNames")]
    public string[]? ProcessNames { get; init; }
}

public sealed class UiContractWindow
{
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("titleContains")]
    public string? TitleContains { get; init; }

    [JsonPropertyName("titleRegex")]
    public string? TitleRegex { get; init; }

    [JsonPropertyName("className")]
    public string? ClassName { get; init; }
}

public sealed class UiContractControl
{
    [JsonPropertyName("windowId")]
    public string WindowId { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("uia")]
    public UiContractUiaLocator? Uia { get; init; }

    [JsonPropertyName("ocr")]
    public UiContractOcrLocator? Ocr { get; init; }

    [JsonPropertyName("fallbackCoords")]
    public UiContractFallbackCoords? FallbackCoords { get; init; }
}

public sealed class UiContractUiaLocator
{
    [JsonPropertyName("automationId")]
    public string? AutomationId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("controlType")]
    public string? ControlType { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }
}

public sealed class UiContractOcrLocator
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("occurrence")]
    public int? Occurrence { get; init; }
}

public sealed class UiContractFallbackCoords
{
    [JsonPropertyName("offsetX")]
    public int OffsetX { get; init; }

    [JsonPropertyName("offsetY")]
    public int OffsetY { get; init; }
}

public sealed class UiContractAssertion
{
    [JsonPropertyName("ocrText")]
    public string? OcrText { get; init; }

    [JsonPropertyName("windowTitleContains")]
    public string? WindowTitleContains { get; init; }

    [JsonPropertyName("logPattern")]
    public string? LogPattern { get; init; }

    [JsonPropertyName("fileExists")]
    public string? FileExists { get; init; }

    [JsonPropertyName("clipboardContains")]
    public string? ClipboardContains { get; init; }
}
