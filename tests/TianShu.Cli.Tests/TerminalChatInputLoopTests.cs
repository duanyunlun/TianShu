using TianShu.Cli.Interaction.Host;
using TianShu.Cli.Terminal;

namespace TianShu.Cli.Tests;

public sealed class TerminalChatInputLoopTests
{
    [Fact]
    public async Task RunAsync_WhenTextSubmitted_ForwardsLineAndCompletesPrompt()
    {
        var submitted = new List<(string Line, TerminalSubmitIntent Intent)>();
        var completed = new List<(bool Spacer, string? Text)>();
        var renderedTexts = new List<string>();
        var resetCount = 0;
        var loop = CreateLoop(
            TerminalInputKey.FromCharacter('h'),
            TerminalInputKey.FromCharacter('i'),
            new TerminalInputKey(TerminalKeyKind.Enter));

        var result = await loop.RunAsync(CreateContext(
            onRender: (composer, _, _, _, _) => renderedTexts.Add(composer.Text),
            onComplete: (_, spacer, text) => completed.Add((spacer, text)),
            onSubmit: (line, intent, _) =>
            {
                submitted.Add((line, intent));
                return Task.FromResult(true);
            },
            onReset: () => resetCount++), CancellationToken.None);

        Assert.True(result.ShouldExit);
        Assert.Equal(("hi", TerminalSubmitIntent.Standard), Assert.Single(submitted));
        Assert.Contains("hi", renderedTexts);
        Assert.Equal((true, "hi"), Assert.Single(completed));
        Assert.Equal(1, resetCount);
    }

    [Fact]
    public async Task RunAsync_WhenTabSuggestionSelected_SubmitsInsertedCommand()
    {
        string? submitted = null;
        var loop = CreateLoop(
            TerminalInputKey.FromCharacter('/'),
            TerminalInputKey.FromCharacter('f'),
            TerminalInputKey.FromCharacter('o'),
            new TerminalInputKey(TerminalKeyKind.Tab),
            new TerminalInputKey(TerminalKeyKind.Enter, Modifiers: TerminalKeyModifiers.Control));

        var result = await loop.RunAsync(CreateContext(
            onSubmit: (line, _, _) =>
            {
                submitted = line;
                return Task.FromResult(true);
            }), CancellationToken.None);

        Assert.True(result.ShouldExit);
        Assert.Equal("/follow-up", submitted);
    }

    [Fact]
    public async Task RunAsync_WhenPlainEnterOnSlashCommand_SubmitsCommand()
    {
        var submitted = new List<(string Line, TerminalSubmitIntent Intent)>();
        var loop = CreateLoop(
            TerminalInputKey.FromCharacter('/'),
            TerminalInputKey.FromCharacter('t'),
            TerminalInputKey.FromCharacter('h'),
            TerminalInputKey.FromCharacter('r'),
            TerminalInputKey.FromCharacter('e'),
            TerminalInputKey.FromCharacter('a'),
            TerminalInputKey.FromCharacter('d'),
            TerminalInputKey.FromCharacter(' '),
            TerminalInputKey.FromCharacter('c'),
            TerminalInputKey.FromCharacter('l'),
            TerminalInputKey.FromCharacter('e'),
            TerminalInputKey.FromCharacter('a'),
            TerminalInputKey.FromCharacter('r'),
            new TerminalInputKey(TerminalKeyKind.Enter));

        var result = await loop.RunAsync(CreateContext(
            onSubmit: (line, intent, _) =>
            {
                submitted.Add((line, intent));
                return Task.FromResult(true);
            }), CancellationToken.None);

        Assert.True(result.ShouldExit);
        Assert.Equal(("/thread clear", TerminalSubmitIntent.Standard), Assert.Single(submitted));
    }

    [Fact]
    public async Task RunAsync_WhenSuggestionAvailable_RendersPopupLines()
    {
        using var cancellation = new CancellationTokenSource();
        var popupRenderCount = 0;
        var keys = new Queue<TerminalInputKey?>([
            TerminalInputKey.FromCharacter('/'),
            TerminalInputKey.FromCharacter('h'),
            null,
        ]);
        var loop = new TerminalChatInputLoop(_ =>
        {
            var next = keys.Dequeue();
            if (next is null)
            {
                cancellation.Cancel();
            }

            return Task.FromResult(next);
        });

        var result = await loop.RunAsync(CreateContext(
            onRender: (_, _, _, popupLines, _) =>
            {
                if (popupLines?.Any(line => line.Contains("/help", StringComparison.Ordinal)) == true)
                {
                    popupRenderCount++;
                }
            }), cancellation.Token);

        Assert.False(result.ShouldExit);
        Assert.True(popupRenderCount > 0);
    }

    [Fact]
    public async Task RunAsync_WhenControlArrowPressed_MovesQueuedFollowUpSelection()
    {
        using var cancellation = new CancellationTokenSource();
        var moves = new List<int>();
        var submitted = new List<string>();
        var keys = new Queue<TerminalInputKey?>([
            TerminalInputKey.FromCharacter('/'),
            TerminalInputKey.FromCharacter('h'),
            new TerminalInputKey(TerminalKeyKind.DownArrow, Modifiers: TerminalKeyModifiers.Control),
            null,
        ]);
        var loop = new TerminalChatInputLoop(_ =>
        {
            var next = keys.Dequeue();
            if (next is null)
            {
                cancellation.Cancel();
            }

            return Task.FromResult(next);
        });

        var result = await loop.RunAsync(CreateContext(
            onMoveQueuedFollowUpSelection: delta =>
            {
                moves.Add(delta);
                return true;
            },
            onSubmit: (line, _, _) =>
            {
                submitted.Add(line);
                return Task.FromResult(false);
            }), cancellation.Token);

        Assert.False(result.ShouldExit);
        Assert.Equal([1], moves);
        Assert.Empty(submitted);
    }

    [Fact]
    public async Task RunAsync_WhenEmptyInputEnterPressedWithQueuedSelection_PromotesSelectedFollowUp()
    {
        var moves = new List<int>();
        var promoteCount = 0;
        var submitted = new List<string>();
        var loop = CreateLoop(
            new TerminalInputKey(TerminalKeyKind.DownArrow, Modifiers: TerminalKeyModifiers.Control),
            new TerminalInputKey(TerminalKeyKind.Enter));

        var result = await loop.RunAsync(CreateContext(
            onMoveQueuedFollowUpSelection: delta =>
            {
                moves.Add(delta);
                return true;
            },
            onPromoteSelectedQueuedFollowUp: _ =>
            {
                promoteCount++;
                return Task.FromResult(true);
            },
            onSubmit: (line, _, _) =>
            {
                submitted.Add(line);
                return Task.FromResult(false);
            }), CancellationToken.None);

        Assert.True(result.ShouldExit);
        Assert.Equal([1], moves);
        Assert.Equal(1, promoteCount);
        Assert.Empty(submitted);
    }

    [Fact]
    public async Task RunAsync_WhenInputHasTextPlainEnter_SubmitsAndDoesNotPromoteQueuedFollowUp()
    {
        var promoteCount = 0;
        var submitted = new List<(string Line, TerminalSubmitIntent Intent)>();
        var loop = CreateLoop(
            TerminalInputKey.FromCharacter('a'),
            new TerminalInputKey(TerminalKeyKind.Enter));

        var result = await loop.RunAsync(CreateContext(
            onPromoteSelectedQueuedFollowUp: _ =>
            {
                promoteCount++;
                return Task.FromResult(true);
            },
            onSubmit: (line, intent, _) =>
            {
                submitted.Add((line, intent));
                return Task.FromResult(true);
            }), CancellationToken.None);

        Assert.True(result.ShouldExit);
        Assert.Equal(0, promoteCount);
        Assert.Equal(("a", TerminalSubmitIntent.Standard), Assert.Single(submitted));
    }

    [Fact]
    public async Task ReadConfirmationAsync_WhenEnterPressed_ReturnsTypedConfirmation()
    {
        var completed = new List<(bool Spacer, string? Text)>();
        var renderedTexts = new List<string>();
        var loop = CreateLoop(
            TerminalInputKey.FromCharacter('D'),
            TerminalInputKey.FromCharacter('E'),
            TerminalInputKey.FromCharacter('L'),
            TerminalInputKey.FromCharacter('E'),
            TerminalInputKey.FromCharacter('T'),
            TerminalInputKey.FromCharacter('E'),
            TerminalInputKey.FromCharacter(' '),
            TerminalInputKey.FromCharacter('A'),
            TerminalInputKey.FromCharacter('L'),
            TerminalInputKey.FromCharacter('L'),
            new TerminalInputKey(TerminalKeyKind.Enter));

        var result = await loop.ReadConfirmationAsync(CreateContext(
            onRender: (composer, _, _, _, _) => renderedTexts.Add(composer.Text),
            onComplete: (_, spacer, text) => completed.Add((spacer, text))), CancellationToken.None);

        Assert.Equal("DELETE ALL", result);
        Assert.Contains("DELETE ALL", renderedTexts);
        Assert.Equal((true, "DELETE ALL"), Assert.Single(completed));
    }

    [Fact]
    public async Task RunAsync_WhenInputEmptyAndUpPressed_RecallsSubmittedHistoryOldestFirst()
    {
        var submitted = new List<(string Line, TerminalSubmitIntent Intent)>();
        var renderedTexts = new List<string>();
        var loop = CreateLoop(
            TerminalInputKey.FromCharacter('f'),
            TerminalInputKey.FromCharacter('i'),
            TerminalInputKey.FromCharacter('r'),
            TerminalInputKey.FromCharacter('s'),
            TerminalInputKey.FromCharacter('t'),
            new TerminalInputKey(TerminalKeyKind.Enter, Modifiers: TerminalKeyModifiers.Control),
            TerminalInputKey.FromCharacter('s'),
            TerminalInputKey.FromCharacter('e'),
            TerminalInputKey.FromCharacter('c'),
            TerminalInputKey.FromCharacter('o'),
            TerminalInputKey.FromCharacter('n'),
            TerminalInputKey.FromCharacter('d'),
            new TerminalInputKey(TerminalKeyKind.Enter, Modifiers: TerminalKeyModifiers.Control | TerminalKeyModifiers.Shift),
            new TerminalInputKey(TerminalKeyKind.UpArrow),
            new TerminalInputKey(TerminalKeyKind.Enter, Modifiers: TerminalKeyModifiers.Control));

        var result = await loop.RunAsync(CreateContext(
            onRender: (composer, _, _, _, _) => renderedTexts.Add(composer.Text),
            onSubmit: (line, intent, _) =>
            {
                submitted.Add((line, intent));
                return Task.FromResult(submitted.Count == 3);
            }), CancellationToken.None);

        Assert.True(result.ShouldExit);
        Assert.Equal(
            [
                ("first", TerminalSubmitIntent.Queue),
                ("second", TerminalSubmitIntent.Steer),
                ("first", TerminalSubmitIntent.Queue),
            ],
            submitted);
        Assert.Contains("first", renderedTexts);
    }

    [Fact]
    public async Task RunAsync_WhenHistoryScopeChanges_LoadsCurrentScopeHistory()
    {
        var scope = "thread-a";
        var submitted = new List<(string Line, TerminalSubmitIntent Intent)>();
        var loop = CreateLoop(
            new TerminalInputKey(TerminalKeyKind.UpArrow),
            new TerminalInputKey(TerminalKeyKind.Enter, Modifiers: TerminalKeyModifiers.Control),
            new TerminalInputKey(TerminalKeyKind.UpArrow),
            new TerminalInputKey(TerminalKeyKind.Enter, Modifiers: TerminalKeyModifiers.Control));

        var result = await loop.RunAsync(CreateContext(
            getInputHistoryScopeKey: () => scope,
            loadInputHistory: key => string.Equals(key, "thread-a", StringComparison.Ordinal)
                ? ["a-history"]
                : ["b-history"],
            onSubmit: (line, intent, _) =>
            {
                submitted.Add((line, intent));
                scope = "thread-b";
                return Task.FromResult(submitted.Count == 2);
            }), CancellationToken.None);

        Assert.True(result.ShouldExit);
        Assert.Equal(
            [
                ("a-history", TerminalSubmitIntent.Queue),
                ("b-history", TerminalSubmitIntent.Queue),
            ],
            submitted);
    }

    [Fact]
    public async Task RunAsync_WhenControlCOnEmptyInput_ExitsAndCompletesWithoutSpacer()
    {
        var completed = new List<(bool Spacer, string? Text)>();
        var loop = CreateLoop(new TerminalInputKey(TerminalKeyKind.ControlC));

        var result = await loop.RunAsync(CreateContext(
            onComplete: (_, spacer, text) => completed.Add((spacer, text))), CancellationToken.None);

        Assert.True(result.ShouldExit);
        Assert.Equal((false, null), Assert.Single(completed));
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_ResetsTerminalAndContinuesOuterChat()
    {
        using var cancellation = new CancellationTokenSource();
        var resetCount = 0;
        var loop = new TerminalChatInputLoop(_ =>
        {
            cancellation.Cancel();
            return Task.FromResult<TerminalInputKey?>(null);
        });

        var result = await loop.RunAsync(CreateContext(onReset: () => resetCount++), cancellation.Token);

        Assert.False(result.ShouldExit);
        Assert.Equal(1, resetCount);
    }

    private static TerminalChatInputLoop CreateLoop(params TerminalInputKey[] keys)
    {
        var queue = new Queue<TerminalInputKey>(keys);
        return new TerminalChatInputLoop(_ =>
        {
            if (queue.Count == 0)
            {
                return Task.FromResult<TerminalInputKey?>(new TerminalInputKey(TerminalKeyKind.ControlC));
            }

            return Task.FromResult<TerminalInputKey?>(queue.Dequeue());
        });
    }

    private static TerminalChatInputLoopContext CreateContext(
        Action<TerminalChatComposer, TerminalPromptRenderer, string, IReadOnlyList<string>?, string?>? onRender = null,
        Action<TerminalPromptRenderer?, bool, string?>? onComplete = null,
        Func<int, bool>? onMoveQueuedFollowUpSelection = null,
        Func<CancellationToken, Task<bool>>? onPromoteSelectedQueuedFollowUp = null,
        Func<string?>? getInputHistoryScopeKey = null,
        Func<string?, IReadOnlyList<string>>? loadInputHistory = null,
        Func<string, TerminalSubmitIntent, CancellationToken, Task<bool>>? onSubmit = null,
        Action? onReset = null)
        => new()
        {
            Options = new ChatCommandOptions { WorkingDirectory = Environment.CurrentDirectory },
            GetInputHistoryScopeKey = getInputHistoryScopeKey ?? (() => null),
            LoadInputHistory = loadInputHistory ?? (_ => []),
            BuildPrompt = () => "> ",
            RenderPrompt = onRender ?? ((_, _, _, _, _) => { }),
            CompleteInputLine = onComplete ?? ((_, _, _) => { }),
            MoveQueuedFollowUpSelection = onMoveQueuedFollowUpSelection ?? (_ => false),
            PromoteSelectedQueuedFollowUpAsync = onPromoteSelectedQueuedFollowUp ?? (_ => Task.FromResult(false)),
            SubmitLineAsync = onSubmit ?? ((_, _, _) => Task.FromResult(false)),
            ResetTerminal = onReset ?? (() => { }),
        };
}
