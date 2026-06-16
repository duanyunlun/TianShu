using TianShu.Cli.Interaction.Commands;
using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Tests;

public sealed class ChatCommandLoopTests
{
    [Fact]
    public async Task ExecuteInputLineAsync_EmptyInput_DoesNotRecordOrDispatch()
    {
        var calls = new List<string>();
        var loop = new ChatCommandLoop(Context(calls));

        var shouldExit = await loop.ExecuteInputLineAsync("   ", CancellationToken.None);

        Assert.False(shouldExit);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task ExecuteInputLineAsync_SlashCommand_RecordsTrimmedInputAndReturnsSlashExitFlag()
    {
        var calls = new List<string>();
        var loop = new ChatCommandLoop(Context(
            calls,
            slash: (input, _) =>
            {
                calls.Add("slash:" + input);
                return Task.FromResult(true);
            }));

        var shouldExit = await loop.ExecuteInputLineAsync("  /quit  ", CancellationToken.None);

        Assert.True(shouldExit);
        Assert.Equal(["record:/quit", "slash:/quit"], calls);
    }

    [Fact]
    public async Task ExecuteInputLineAsync_UsesFirstHandledPlainTextRoute()
    {
        var calls = new List<string>();
        var loop = new ChatCommandLoop(Context(
            calls,
            plainThread: (input, _) =>
            {
                calls.Add("plain-thread:" + input);
                return Task.FromResult(false);
            },
            shell: (input, _) =>
            {
                calls.Add("shell:" + input);
                return Task.FromResult(true);
            },
            followUp: (input, mode, _) =>
            {
                calls.Add($"follow-up:{input}:{mode}");
                return Task.FromResult(true);
            }));

        var shouldExit = await loop.ExecuteInputLineAsync(" !Get-Location ", CancellationToken.None, ControlPlaneFollowUpMode.Queue);

        Assert.False(shouldExit);
        Assert.Equal(["record:!Get-Location", "plain-thread:!Get-Location", "shell:!Get-Location"], calls);
    }

    [Fact]
    public async Task ExecuteInputLineAsync_NewTurnRunsAfterUnhandledRoutes()
    {
        var calls = new List<string>();
        var loop = new ChatCommandLoop(Context(calls));

        var shouldExit = await loop.ExecuteInputLineAsync("hello", CancellationToken.None);

        Assert.False(shouldExit);
        Assert.Equal(
            [
                "record:hello",
                "plain-thread:hello",
                "shell:hello",
                "follow-up:hello:",
                "draft:hello",
                "user:hello",
                "new-turn:hello",
            ],
            calls);
    }

    private static ChatCommandExecutionContext Context(
        List<string> calls,
        Func<string, CancellationToken, Task<bool>>? slash = null,
        Func<string, CancellationToken, Task<bool>>? plainThread = null,
        Func<string, CancellationToken, Task<bool>>? shell = null,
        Func<string, ControlPlaneFollowUpMode?, CancellationToken, Task<bool>>? followUp = null,
        Func<string, CancellationToken, Task<bool>>? draft = null,
        Func<string, CancellationToken, Task>? newTurn = null)
        => new(
            input => calls.Add("record:" + input),
            input => calls.Add("user:" + input),
            slash ?? ((input, _) =>
            {
                calls.Add("slash:" + input);
                return Task.FromResult(false);
            }),
            plainThread ?? ((input, _) =>
            {
                calls.Add("plain-thread:" + input);
                return Task.FromResult(false);
            }),
            shell ?? ((input, _) =>
            {
                calls.Add("shell:" + input);
                return Task.FromResult(false);
            }),
            followUp ?? ((input, mode, _) =>
            {
                calls.Add($"follow-up:{input}:{mode}");
                return Task.FromResult(false);
            }),
            draft ?? ((input, _) =>
            {
                calls.Add("draft:" + input);
                return Task.FromResult(false);
            }),
            newTurn ?? ((input, _) =>
            {
                calls.Add("new-turn:" + input);
                return Task.CompletedTask;
            }));
}
