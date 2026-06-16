using TianShu.Cli.Interaction;

namespace TianShu.Cli.Interaction.Events;

internal sealed record ToolInvocationInput(
    string? RawText,
    string? Subject,
    string? Command,
    string? Path)
{
    public static ToolInvocationInput? FromRaw(ToolPresentationKind kind, string? rawText)
    {
        var raw = ToolInvocationJsonHelpers.NormalizeDisplayText(rawText);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!ToolInvocationJsonHelpers.TryParseJsonObject(raw, out var document) || document is null)
        {
            return new ToolInvocationInput(raw, raw, kind == ToolPresentationKind.Command ? raw : null, null);
        }

        using (document)
        {
            var root = document.RootElement;
            return kind switch
            {
                ToolPresentationKind.Command => ToolInvocationCommandInputParser.Build(raw, root),
                ToolPresentationKind.FileChange or ToolPresentationKind.CodePatch => ToolInvocationFileInputParser.Build(raw, root),
                ToolPresentationKind.PlanUpdate => ToolInvocationPlanInputParser.Build(raw, root),
                _ => ToolInvocationGenericInputParser.Build(raw, root),
            };
        }
    }
}
