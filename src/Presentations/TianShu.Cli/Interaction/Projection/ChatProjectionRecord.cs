namespace TianShu.Cli.Interaction.Projection;

internal sealed record ChatProjectionRecord(
    DateTimeOffset Timestamp,
    string Kind,
    string? Text = null,
    string? BlockType = null,
    string? Reason = null);
