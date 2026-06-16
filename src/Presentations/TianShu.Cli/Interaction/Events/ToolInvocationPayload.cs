using TianShu.Cli.Interaction;

namespace TianShu.Cli.Interaction.Events;

internal sealed record ToolInvocationPayload(
    ToolPresentationKind Kind,
    ToolInvocationInput? Input,
    ToolInvocationOutput? Output)
{
    public static ToolInvocationPayload Create(
        string toolName,
        string? inputText,
        string? outputText,
        string? status)
        => Create(toolName, kind: null, inputText, outputText, status);

    public static ToolInvocationPayload Create(
        string toolName,
        ToolPresentationKind? kind,
        string? inputText,
        string? outputText,
        string? status)
    {
        kind ??= ToolPresentationKindResolver.ResolveFallbackFromToolName(toolName);
        return new ToolInvocationPayload(
            kind.Value,
            ToolInvocationInput.FromRaw(kind.Value, inputText),
            ToolInvocationOutput.FromRaw(kind.Value, outputText, status));
    }

    public ToolInvocationPayload MergeFallback(ToolInvocationPayload? previous)
    {
        if (previous is null)
        {
            return this;
        }

        return this with
        {
            Input = Input ?? previous.Input,
            Output = Output ?? previous.Output,
        };
    }

    internal static ToolPresentationKind ResolveKind(string toolName)
        => ToolPresentationKindResolver.ResolveFallbackFromToolName(toolName);
}
