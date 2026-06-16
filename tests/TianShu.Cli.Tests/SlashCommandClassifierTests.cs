using TianShu.Cli.Interaction.Commands;

namespace TianShu.Cli.Tests;

public sealed class SlashCommandClassifierTests
{
    [Theory]
    [InlineData("/help", (int)SlashCommandKind.Help, "help", "")]
    [InlineData("/quit", (int)SlashCommandKind.Exit, "quit", "")]
    [InlineData("/followup hello", (int)SlashCommandKind.FollowUp, "followup", "hello")]
    [InlineData("/approve-session call-1", (int)SlashCommandKind.ApproveSession, "approve-session", "call-1")]
    [InlineData("/approvesession call-1", (int)SlashCommandKind.ApproveSession, "approvesession", "call-1")]
    [InlineData("/waitcomplete --timeout 5", (int)SlashCommandKind.WaitComplete, "waitcomplete", "--timeout 5")]
    [InlineData("/wait-next-tool-call 10", (int)SlashCommandKind.WaitNextToolCall, "wait-next-tool-call", "10")]
    public void Classify_RecognizesAliasesAndPreservesRest(
        string line,
        int expectedKind,
        string expectedCommand,
        string expectedRest)
    {
        var classified = SlashCommandClassifier.Classify(line);

        Assert.Equal((SlashCommandKind)expectedKind, classified.Kind);
        Assert.Equal(expectedCommand, classified.Command);
        Assert.Equal(expectedRest, classified.Rest);
    }

    [Fact]
    public void Classify_EmptySlash_ReturnsEmpty()
    {
        var classified = SlashCommandClassifier.Classify("/   ");

        Assert.Equal(SlashCommandKind.Empty, classified.Kind);
        Assert.Equal(string.Empty, classified.Command);
        Assert.Equal(string.Empty, classified.Rest);
    }

    [Fact]
    public void Classify_UnknownCommand_PreservesCommandForErrorDisplay()
    {
        var classified = SlashCommandClassifier.Classify("/does-not-exist abc");

        Assert.Equal(SlashCommandKind.Unknown, classified.Kind);
        Assert.Equal("does-not-exist", classified.Command);
        Assert.Equal("abc", classified.Rest);
    }
}
