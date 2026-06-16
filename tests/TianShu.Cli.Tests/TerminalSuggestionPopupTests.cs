using TianShu.Cli.Terminal;

namespace TianShu.Cli.Tests;

public sealed class TerminalSuggestionPopupTests
{
    [Fact]
    public void Build_WhenSlashCommandPrefix_ReturnsMatchingCommands()
    {
        var popup = new TerminalSuggestionPopup(Environment.CurrentDirectory);

        var frame = popup.Build("/fo", 3, 0);

        Assert.True(frame.HasItems);
        Assert.Contains(frame.Items, item => item.InsertText == "/follow-up ");
        Assert.All(frame.RenderLines, line => Assert.Contains("/", line, StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WhenMemorySlashCommandPrefix_ReturnsMemoryCommand()
    {
        var popup = new TerminalSuggestionPopup(Environment.CurrentDirectory);

        var frame = popup.Build("/mem", 4, 0);

        Assert.True(frame.HasItems);
        Assert.Contains(frame.Items, item => item.InsertText == "/memory ");
        Assert.Contains(frame.RenderLines, line => line.Contains("/memory", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WhenMemoryCommandHasTrailingSpace_ReturnsMemorySubcommands()
    {
        var popup = new TerminalSuggestionPopup(Environment.CurrentDirectory);

        var frame = popup.Build("/memory ", "/memory ".Length, 0);

        Assert.True(frame.HasItems);
        Assert.Contains(frame.Items, item => item.InsertText == "/memory add ");
        Assert.Contains(frame.Items, item => item.InsertText == "/memory search ");
        Assert.Contains(frame.Items, item => item.InsertText == "/memory delete ");
        Assert.Contains(frame.Items, item => item.InsertText == "/memory review ");
        Assert.All(frame.RenderLines, line => Assert.Contains("/memory", line, StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WhenMemorySubcommandPrefix_ReturnsMatchingSubcommand()
    {
        var popup = new TerminalSuggestionPopup(Environment.CurrentDirectory);

        var frame = popup.Build("/memory d", "/memory d".Length, 0);

        Assert.True(frame.HasItems);
        Assert.Contains(frame.Items, item => item.InsertText == "/memory delete ");
        Assert.DoesNotContain(frame.Items, item => item.InsertText == "/memory add ");
    }

    [Theory]
    [InlineData("/follow-up ", "/follow-up queue ", "/follow-up steer ", "/follow-up interrupt ")]
    [InlineData("/followup ", "/followup queue ", "/followup steer ", "/followup interrupt ")]
    [InlineData("/model-route ", "/model-route status ", null, null)]
    [InlineData("/config ", "/config gui ", "/config reload ", null)]
    public void Build_WhenCommandHasExplicitSubcommands_ReturnsThem(
        string input,
        string expectedFirst,
        string? expectedSecond,
        string? expectedThird)
    {
        var popup = new TerminalSuggestionPopup(Environment.CurrentDirectory);

        var frame = popup.Build(input, input.Length, 0);

        Assert.True(frame.HasItems);
        Assert.Contains(frame.Items, item => item.InsertText == expectedFirst);
        if (expectedSecond is not null)
        {
            Assert.Contains(frame.Items, item => item.InsertText == expectedSecond);
        }

        if (expectedThird is not null)
        {
            Assert.Contains(frame.Items, item => item.InsertText == expectedThird);
        }
    }

    [Fact]
    public void Build_WhenThreadCommandHasTrailingSpace_ReturnsThreadSubcommands()
    {
        var popup = new TerminalSuggestionPopup(Environment.CurrentDirectory);

        var frame = popup.Build("/thread ", "/thread ".Length, 0);

        Assert.True(frame.HasItems);
        Assert.Contains(frame.Items, item => item.InsertText == "/thread delete ");
        Assert.Contains(frame.Items, item => item.InsertText == "/thread clear ");
        Assert.All(frame.RenderLines, line => Assert.Contains("/thread", line, StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WhenThreadSubcommandPrefix_ReturnsMatchingSubcommand()
    {
        var popup = new TerminalSuggestionPopup(Environment.CurrentDirectory);

        var frame = popup.Build("/thread c", "/thread c".Length, 0);

        Assert.True(frame.HasItems);
        Assert.Contains(frame.Items, item => item.InsertText == "/thread clear ");
        Assert.DoesNotContain(frame.Items, item => item.InsertText == "/thread delete ");
    }

    [Fact]
    public void MoveSelection_WhenSuggestionsExist_WrapsSelection()
    {
        var popup = new TerminalSuggestionPopup(Environment.CurrentDirectory);
        var frame = popup.Build("/wait", 5, 0);

        var moved = frame.MoveSelection(-1);

        Assert.True(moved.HasItems);
        Assert.Equal(moved.Items.Count - 1, moved.SelectedIndex);
    }

    [Fact]
    public void Build_WhenSlashCandidatesExceedVisibleLimit_RendersScrollableWindow()
    {
        var popup = new TerminalSuggestionPopup(Environment.CurrentDirectory);

        var frame = popup.Build("/", 1, 0);

        Assert.True(frame.Items.Count > frame.RenderLines.Count);
        Assert.Equal(6, frame.RenderLines.Count);

        var moved = frame;
        for (var index = 0; index < 8; index++)
        {
            moved = moved.MoveSelection(1);
        }

        var selected = moved.SelectedItem;
        Assert.NotNull(selected);
        Assert.Contains(moved.RenderLines, line =>
            line.StartsWith("> ", StringComparison.Ordinal)
            && line.Contains(selected.Value.DisplayText, StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WhenAtFileToken_ReturnsRelativeFileCandidates()
    {
        using var workspace = new TestTempDirectory();
        var sourceFile = Path.Combine(workspace.Path, "src", "KernelHost.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllText(sourceFile, "internal sealed class KernelHost { }");
        File.WriteAllText(Path.Combine(workspace.Path, "README.md"), "# demo");
        var popup = new TerminalSuggestionPopup(workspace.Path);

        var frame = popup.Build("inspect @Kernel", "inspect @Kernel".Length, 0);

        Assert.True(frame.HasItems);
        var item = Assert.Single(frame.Items, item => item.InsertText == "src/KernelHost.cs");
        Assert.Equal("src/KernelHost.cs", item.InsertText);
        Assert.Equal("inspect ".Length, item.ReplaceStart);
        Assert.Equal("@Kernel".Length, item.ReplaceLength);
    }
}
