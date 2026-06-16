namespace TianShu.Cli.Interaction.Commands;

internal enum SlashCommandKind
{
    Empty = 0,
    Unknown = 1,
    Help = 2,
    Exit = 3,
    Interrupt = 4,
    FollowUp = 5,
    Init = 6,
    Draft = 7,
    SendRestored = 8,
    DropRestored = 9,
    Approve = 10,
    ApproveSession = 11,
    ApproveAlways = 12,
    Reject = 13,
    CancelApproval = 14,
    Permissions = 15,
    Input = 16,
    Threads = 17,
    Thread = 18,
    Model = 19,
    Config = 20,
    Reload = 21,
    New = 22,
    Fork = 23,
    Archive = 24,
    Rename = 25,
    Resume = 26,
    Rpc = 27,
    State = 28,
    Wait = 29,
    WaitEvent = 30,
    WaitNextToolCall = 31,
    WaitComplete = 32,
    Memory = 33,
}

internal sealed record SlashCommandClassification(
    SlashCommandKind Kind,
    string Command,
    string Rest);

internal static class SlashCommandClassifier
{
    public static SlashCommandClassification Classify(string line)
        => Classify(line, SlashCommandRegistry.Default);

    public static SlashCommandClassification Classify(string line, SlashCommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var commandLine = line.StartsWith("/", StringComparison.Ordinal)
            ? line[1..].Trim()
            : line.Trim();
        if (commandLine.Length == 0)
        {
            return new SlashCommandClassification(SlashCommandKind.Empty, string.Empty, string.Empty);
        }

        var command = ReadFirstToken(commandLine, out var rest).ToLowerInvariant();
        return new SlashCommandClassification(registry.ResolveKind(command), command, rest);
    }

    private static string ReadFirstToken(string value, out string rest)
    {
        var trimmed = value.TrimStart();
        if (trimmed.Length == 0)
        {
            rest = string.Empty;
            return string.Empty;
        }

        var index = trimmed.IndexOf(' ', StringComparison.Ordinal);
        if (index < 0)
        {
            rest = string.Empty;
            return trimmed;
        }

        rest = trimmed[(index + 1)..].Trim();
        return trimmed[..index];
    }
}
