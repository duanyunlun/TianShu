using TianShu.Cli.Interaction.Commands;

namespace TianShu.Cli.Tests;

public sealed class SlashCommandRegistryTests
{
    [Fact]
    public void Default_ContainsDescriptorForEveryConcreteKind()
    {
        var registeredKinds = SlashCommandRegistry.Default.Descriptors
            .Select(static descriptor => descriptor.Kind)
            .ToHashSet();

        foreach (var kind in Enum.GetValues<SlashCommandKind>())
        {
            if (kind is SlashCommandKind.Empty or SlashCommandKind.Unknown)
            {
                continue;
            }

            Assert.Contains(kind, registeredKinds);
        }
    }

    [Theory]
    [InlineData("quit", (int)SlashCommandKind.Exit)]
    [InlineData("followup", (int)SlashCommandKind.FollowUp)]
    [InlineData("approvesession", (int)SlashCommandKind.ApproveSession)]
    [InlineData("decline", (int)SlashCommandKind.Reject)]
    [InlineData("permission", (int)SlashCommandKind.Permissions)]
    [InlineData("waitcomplete", (int)SlashCommandKind.WaitComplete)]
    [InlineData("waitnexttoolcall", (int)SlashCommandKind.WaitNextToolCall)]
    public void ResolveKind_UsesRegisteredAliases(string alias, int expectedKind)
    {
        var kind = SlashCommandRegistry.Default.ResolveKind(alias);

        Assert.Equal((SlashCommandKind)expectedKind, kind);
    }

    [Fact]
    public void ResolveKind_DoesNotKeepOldModelAlias()
    {
        var kind = SlashCommandRegistry.Default.ResolveKind("model");

        Assert.Equal(SlashCommandKind.Unknown, kind);
    }

    [Fact]
    public void BuildHelpText_UsesVisibleDescriptorsAndPlainInputGuidance()
    {
        var helpText = SlashCommandRegistry.Default.BuildHelpText();

        Assert.Contains("交互命令：", helpText, StringComparison.Ordinal);
        Assert.Contains("/help", helpText, StringComparison.Ordinal);
        Assert.Contains("/thread delete --thread-id <id>", helpText, StringComparison.Ordinal);
        Assert.Contains("/thread clear", helpText, StringComparison.Ordinal);
        Assert.Contains("普通输入：", helpText, StringComparison.Ordinal);
        Assert.Contains("!<shell command>", helpText, StringComparison.Ordinal);
        Assert.DoesNotContain("codex", helpText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DangerousCommands_DeclareConfirmationPolicy()
    {
        var exit = SlashCommandRegistry.Default.GetRequired(SlashCommandKind.Exit);
        var approveAlways = SlashCommandRegistry.Default.GetRequired(SlashCommandKind.ApproveAlways);
        var thread = SlashCommandRegistry.Default.GetRequired(SlashCommandKind.Thread);

        Assert.Equal(SlashCommandConfirmationPolicy.EndsInteractiveSession, exit.ConfirmationPolicy);
        Assert.Equal(SlashCommandConfirmationPolicy.RequiresExplicitConfirmation, approveAlways.ConfirmationPolicy);
        Assert.Equal(SlashCommandConfirmationPolicy.SubcommandMayRequireConfirmation, thread.ConfirmationPolicy);
    }

    [Fact]
    public void RunningTurnCommands_DeclareAllowedWhileRunning()
    {
        Assert.True(SlashCommandRegistry.Default.GetRequired(SlashCommandKind.Interrupt).AllowedWhileRunning);
        Assert.True(SlashCommandRegistry.Default.GetRequired(SlashCommandKind.FollowUp).AllowedWhileRunning);
        Assert.True(SlashCommandRegistry.Default.GetRequired(SlashCommandKind.Approve).AllowedWhileRunning);
        Assert.True(SlashCommandRegistry.Default.GetRequired(SlashCommandKind.WaitComplete).AllowedWhileRunning);
        Assert.False(SlashCommandRegistry.Default.GetRequired(SlashCommandKind.New).AllowedWhileRunning);
    }

    [Fact]
    public void Default_DeclaresExplicitSubcommandsForCommandsWithSecondLevelActions()
    {
        Assert.Equal(["queue", "steer", "interrupt", "promote", "drop"], SlashCommandRegistry.Default.GetRequired(SlashCommandKind.FollowUp).Subcommands);
        Assert.Equal(["status"], SlashCommandRegistry.Default.GetRequired(SlashCommandKind.Model).Subcommands);
        Assert.Equal(["gui", "reload"], SlashCommandRegistry.Default.GetRequired(SlashCommandKind.Config).Subcommands);
        Assert.Equal(["delete", "clear"], SlashCommandRegistry.Default.GetRequired(SlashCommandKind.Thread).Subcommands);
        Assert.Equal(
            ["providers", "spaces", "overlay", "search", "filter", "add", "extract", "import", "export", "bind-provider", "forget", "delete", "supersede", "review", "feedback", "citation"],
            SlashCommandRegistry.Default.GetRequired(SlashCommandKind.Memory).Subcommands);
    }
}
