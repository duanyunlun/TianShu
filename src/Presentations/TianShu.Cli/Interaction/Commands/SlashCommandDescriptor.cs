namespace TianShu.Cli.Interaction.Commands;

internal enum SlashCommandCategory
{
    General = 0,
    TurnControl = 1,
    Approval = 2,
    Thread = 3,
    ModelAndConfig = 4,
    Diagnostics = 5,
}

internal enum SlashCommandConfirmationPolicy
{
    None = 0,
    EndsInteractiveSession = 1,
    RequiresExplicitConfirmation = 2,
    SubcommandMayRequireConfirmation = 3,
}

internal sealed record SlashCommandDescriptor(
    SlashCommandKind Kind,
    string Name,
    IReadOnlyList<string> Aliases,
    string Usage,
    string Description,
    SlashCommandCategory Category,
    SlashCommandConfirmationPolicy ConfirmationPolicy,
    bool VisibleInHelp,
    bool AllowedWhileRunning,
    bool RequiresActiveThread,
    IReadOnlyList<string> Subcommands)
{
    public IEnumerable<string> AllNames()
    {
        yield return Name;
        foreach (var alias in Aliases)
        {
            yield return alias;
        }
    }
}
