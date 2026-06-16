using TianShu.Cli.Terminal;

namespace TianShu.Cli.Tests;

public sealed class TerminalChatComposerTests
{
    [Fact]
    public void HandleKey_WhenEnterAfterTyping_SubmitsStandardIntent()
    {
        var composer = new TerminalChatComposer();

        TypeText(composer, "  hello TianShu  ");
        var action = composer.HandleKey(new TerminalInputKey(TerminalKeyKind.Enter));

        Assert.Equal(TerminalComposerActionKind.Submit, action.Kind);
        Assert.Equal(TerminalSubmitIntent.Standard, action.SubmitIntent);
        Assert.Equal("hello TianShu", action.Text);
        Assert.True(composer.IsEmpty);
    }

    [Fact]
    public void HandleKey_WhenEnterOnSlashCommand_SubmitsStandardIntent()
    {
        var composer = new TerminalChatComposer();

        TypeText(composer, "  /thread clear");
        var action = composer.HandleKey(new TerminalInputKey(TerminalKeyKind.Enter));

        Assert.Equal(TerminalComposerActionKind.Submit, action.Kind);
        Assert.Equal(TerminalSubmitIntent.Standard, action.SubmitIntent);
        Assert.Equal("/thread clear", action.Text);
        Assert.True(composer.IsEmpty);
    }

    [Fact]
    public void HandleKey_WhenForcedNewLineOnSlashCommand_InsertsNewLine()
    {
        var composer = new TerminalChatComposer();

        TypeText(composer, "/thread clear");
        var action = composer.HandleKey(new TerminalInputKey(
            TerminalKeyKind.Enter,
            Modifiers: TerminalKeyModifiers.Shift));

        Assert.Equal(TerminalComposerActionKind.None, action.Kind);
        Assert.Equal("/thread clear" + Environment.NewLine, composer.Text);
    }

    [Fact]
    public void HandleKey_WhenEditingInMiddle_UpdatesBufferAndCursor()
    {
        var composer = new TerminalChatComposer();

        TypeText(composer, "helo");
        composer.HandleKey(new TerminalInputKey(TerminalKeyKind.LeftArrow));
        composer.HandleKey(TerminalInputKey.FromCharacter('l'));
        composer.HandleKey(new TerminalInputKey(TerminalKeyKind.End));
        composer.HandleKey(TerminalInputKey.FromCharacter('!'));
        composer.HandleKey(new TerminalInputKey(TerminalKeyKind.LeftArrow));
        composer.HandleKey(new TerminalInputKey(TerminalKeyKind.Backspace));

        Assert.Equal("hell!", composer.Text);
        Assert.Equal(4, composer.Cursor);
    }

    [Fact]
    public void HandleKey_WhenCtrlEnterOnSingleLine_SubmitsQueueIntent()
    {
        var composer = new TerminalChatComposer();

        TypeText(composer, "line 1");
        var action = composer.HandleKey(new TerminalInputKey(
            TerminalKeyKind.Enter,
            Modifiers: TerminalKeyModifiers.Control));

        Assert.Equal(TerminalComposerActionKind.Submit, action.Kind);
        Assert.Equal(TerminalSubmitIntent.Queue, action.SubmitIntent);
        Assert.Equal("line 1", action.Text);
        Assert.True(composer.IsEmpty);
    }

    [Fact]
    public void HandleKey_WhenShiftEnterOnMultiLine_InsertsNewLineWithoutSubmitting()
    {
        var composer = new TerminalChatComposer();

        TypeText(composer, "line 1");
        composer.HandleKey(new TerminalInputKey(
            TerminalKeyKind.Enter,
            Modifiers: TerminalKeyModifiers.Shift));
        TypeText(composer, "line 2");
        var action = composer.HandleKey(new TerminalInputKey(
            TerminalKeyKind.Enter,
            Modifiers: TerminalKeyModifiers.Shift));
        TypeText(composer, "line 3");

        Assert.Equal(TerminalComposerActionKind.None, action.Kind);
        Assert.Equal(
            "line 1" + Environment.NewLine + "line 2" + Environment.NewLine + "line 3",
            composer.Text);
    }

    [Fact]
    public void HandleKey_WhenCtrlEnterOnMultiLine_SubmitsQueueIntentAndClearsBuffer()
    {
        var composer = new TerminalChatComposer();

        TypeText(composer, "line 1");
        composer.HandleKey(new TerminalInputKey(
            TerminalKeyKind.Enter,
            Modifiers: TerminalKeyModifiers.Shift));
        TypeText(composer, "line 2");
        var action = composer.HandleKey(new TerminalInputKey(
            TerminalKeyKind.Enter,
            Modifiers: TerminalKeyModifiers.Control));

        Assert.Equal(TerminalComposerActionKind.Submit, action.Kind);
        Assert.Equal(TerminalSubmitIntent.Queue, action.SubmitIntent);
        Assert.Equal("line 1" + Environment.NewLine + "line 2", action.Text);
        Assert.True(composer.IsEmpty);
    }

    [Fact]
    public void HandleKey_WhenCtrlShiftEnterOnSingleLine_SubmitsSteerIntent()
    {
        var composer = new TerminalChatComposer();

        TypeText(composer, "send now");
        var action = composer.HandleKey(new TerminalInputKey(
            TerminalKeyKind.Enter,
            Modifiers: TerminalKeyModifiers.Control | TerminalKeyModifiers.Shift));

        Assert.Equal(TerminalComposerActionKind.Submit, action.Kind);
        Assert.Equal(TerminalSubmitIntent.Steer, action.SubmitIntent);
        Assert.Equal("send now", action.Text);
        Assert.True(composer.IsEmpty);
    }

    [Fact]
    public void HandleKey_WhenCtrlShiftEnterOnMultiLine_SubmitsSteerIntent()
    {
        var composer = new TerminalChatComposer();

        TypeText(composer, "line 1");
        composer.HandleKey(new TerminalInputKey(
            TerminalKeyKind.Enter,
            Modifiers: TerminalKeyModifiers.Shift));
        TypeText(composer, "line 2");
        var action = composer.HandleKey(new TerminalInputKey(
            TerminalKeyKind.Enter,
            Modifiers: TerminalKeyModifiers.Control | TerminalKeyModifiers.Shift));

        Assert.Equal(TerminalComposerActionKind.Submit, action.Kind);
        Assert.Equal(TerminalSubmitIntent.Steer, action.SubmitIntent);
        Assert.Equal("line 1" + Environment.NewLine + "line 2", action.Text);
        Assert.True(composer.IsEmpty);
    }

    [Fact]
    public void HandleKey_WhenForcedNewLineSignal_IsUsedForPasteProtection()
    {
        var composer = new TerminalChatComposer();

        TypeText(composer, "line 1");
        var action = composer.HandleKey(new TerminalInputKey(
            TerminalKeyKind.Enter,
            Modifiers: TerminalKeyModifiers.Shift));
        TypeText(composer, "line 2");

        Assert.Equal(TerminalComposerActionKind.None, action.Kind);
        Assert.Equal("line 1" + Environment.NewLine + "line 2", composer.Text);
    }

    [Fact]
    public void HandleKey_WhenEmptyInputNavigatesHistory_RecallsOldestToNewestAndRestoresBlankDraft()
    {
        var composer = new TerminalChatComposer();

        Submit(composer, "first");
        Submit(composer, "second");

        composer.HandleKey(new TerminalInputKey(TerminalKeyKind.UpArrow));
        Assert.Equal("first", composer.Text);
        Assert.Equal(5, composer.Cursor);

        composer.HandleKey(new TerminalInputKey(TerminalKeyKind.DownArrow));
        Assert.Equal("second", composer.Text);
        Assert.Equal(6, composer.Cursor);

        composer.HandleKey(new TerminalInputKey(TerminalKeyKind.DownArrow));
        Assert.True(composer.IsEmpty);
        Assert.Equal(0, composer.Cursor);
    }

    [Fact]
    public void HandleKey_WhenEmptyInputPressesDown_DoesNotEnterHistory()
    {
        var composer = new TerminalChatComposer();

        Submit(composer, "first");
        Submit(composer, "second");

        composer.HandleKey(new TerminalInputKey(TerminalKeyKind.DownArrow));
        Assert.True(composer.IsEmpty);
        Assert.Equal(0, composer.Cursor);
    }

    [Fact]
    public void HandleKey_WhenHistoryAtNewestAndDownPressed_ClearsInput()
    {
        var composer = new TerminalChatComposer();

        Submit(composer, "first");
        Submit(composer, "second");

        composer.HandleKey(new TerminalInputKey(TerminalKeyKind.UpArrow));
        Assert.Equal("first", composer.Text);

        composer.HandleKey(new TerminalInputKey(TerminalKeyKind.UpArrow));
        Assert.Equal("second", composer.Text);

        composer.HandleKey(new TerminalInputKey(TerminalKeyKind.DownArrow));
        Assert.True(composer.IsEmpty);
        Assert.Equal(0, composer.Cursor);
    }

    [Fact]
    public void HandleKey_WhenDraftIsNotEmpty_DoesNotReplaceDraftWithHistory()
    {
        var composer = new TerminalChatComposer();

        Submit(composer, "sent");
        TypeText(composer, "draft");
        composer.HandleKey(new TerminalInputKey(TerminalKeyKind.UpArrow));

        Assert.Equal("draft", composer.Text);
        Assert.Equal("draft".Length, composer.Cursor);
    }

    [Fact]
    public void HandleKey_WhenQueueAndSteerSubmitted_AddsBothToHistory()
    {
        var composer = new TerminalChatComposer();

        TypeText(composer, "queue item");
        var queueAction = composer.HandleKey(new TerminalInputKey(
            TerminalKeyKind.Enter,
            Modifiers: TerminalKeyModifiers.Control));
        TypeText(composer, "steer item");
        var steerAction = composer.HandleKey(new TerminalInputKey(
            TerminalKeyKind.Enter,
            Modifiers: TerminalKeyModifiers.Control | TerminalKeyModifiers.Shift));

        Assert.Equal(TerminalSubmitIntent.Queue, queueAction.SubmitIntent);
        Assert.Equal(TerminalSubmitIntent.Steer, steerAction.SubmitIntent);
        Assert.Equal(2, composer.HistoryCount);

        composer.HandleKey(new TerminalInputKey(TerminalKeyKind.UpArrow));
        Assert.Equal("queue item", composer.Text);

        composer.HandleKey(new TerminalInputKey(TerminalKeyKind.UpArrow));
        Assert.Equal("steer item", composer.Text);
    }

    [Fact]
    public void HandleKey_WhenSubmittingSameTextTwice_DoesNotDuplicateAdjacentHistory()
    {
        var composer = new TerminalChatComposer();

        Submit(composer, "repeat");
        Submit(composer, "repeat");

        Assert.Equal(1, composer.HistoryCount);
    }

    [Fact]
    public void HandleKey_WhenControlCWithDraft_ClearsDraftWithoutExiting()
    {
        var composer = new TerminalChatComposer();

        TypeText(composer, "draft");
        var action = composer.HandleKey(new TerminalInputKey(TerminalKeyKind.ControlC));

        Assert.Equal(TerminalComposerActionKind.None, action.Kind);
        Assert.True(composer.IsEmpty);
    }

    [Fact]
    public void HandleKey_WhenControlCWithoutDraft_RequestsExit()
    {
        var composer = new TerminalChatComposer();

        var action = composer.HandleKey(new TerminalInputKey(TerminalKeyKind.ControlC));

        Assert.Equal(TerminalComposerActionKind.Exit, action.Kind);
    }

    [Fact]
    public void ReplaceRange_WhenSuggestionAccepted_ReplacesTokenAndMovesCursor()
    {
        var composer = new TerminalChatComposer();
        TypeText(composer, "open @ker now");

        composer.ReplaceRange(5, 4, "src/TianShu.Kernel");

        Assert.Equal("open src/TianShu.Kernel now", composer.Text);
        Assert.Equal("open src/TianShu.Kernel".Length, composer.Cursor);
    }

    private static void TypeText(TerminalChatComposer composer, string text)
    {
        foreach (var character in text)
        {
            composer.HandleKey(TerminalInputKey.FromCharacter(character));
        }
    }

    private static void Submit(TerminalChatComposer composer, string text)
    {
        TypeText(composer, text);
        var action = composer.HandleKey(new TerminalInputKey(
            TerminalKeyKind.Enter,
            Modifiers: TerminalKeyModifiers.Control));
        Assert.Equal(TerminalComposerActionKind.Submit, action.Kind);
    }
}
