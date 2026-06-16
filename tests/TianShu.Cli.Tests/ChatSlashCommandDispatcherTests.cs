using TianShu.Cli.Interaction.Commands;
using TianShu.Contracts.Governance;

namespace TianShu.Cli.Tests;

public sealed class ChatSlashCommandDispatcherTests
{
    [Fact]
    public async Task ExecuteAsync_RoutesEveryRegisteredKind()
    {
        foreach (var kind in Enum.GetValues<SlashCommandKind>())
        {
            if (kind is SlashCommandKind.Empty or SlashCommandKind.Unknown)
            {
                continue;
            }

            var calls = new List<string>();
            var exit = await new ChatSlashCommandDispatcher().ExecuteAsync(
                BuildCommandLine(kind),
                CreateContext(calls),
                CancellationToken.None);

            Assert.Equal(kind == SlashCommandKind.Exit, exit);
            if (kind != SlashCommandKind.Exit)
            {
                Assert.Contains(ExpectedCall(kind), calls);
            }
        }
    }

    [Fact]
    public void HandlerRegistry_CoversEveryRegisteredSlashCommandKind()
    {
        var handlers = ChatSlashCommandHandlerRegistry.Default;

        foreach (var kind in Enum.GetValues<SlashCommandKind>())
        {
            if (kind is SlashCommandKind.Empty or SlashCommandKind.Unknown)
            {
                continue;
            }

            Assert.True(handlers.TryGetHandler(kind, out var handler));
            Assert.Equal(kind, handler.Kind);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenUnknownCommand_WritesErrorAndContinues()
    {
        var messages = new List<(string Message, bool IsError)>();

        var exit = await new ChatSlashCommandDispatcher().ExecuteAsync(
            "/does-not-exist abc",
            CreateContext(messages: messages),
            CancellationToken.None);

        Assert.False(exit);
        Assert.Contains(messages, static item => item.Message == "未知命令：/does-not-exist" && item.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmptyCommand_DoesNothing()
    {
        var calls = new List<string>();

        var exit = await new ChatSlashCommandDispatcher().ExecuteAsync(
            "/   ",
            CreateContext(calls),
            CancellationToken.None);

        Assert.False(exit);
        Assert.Empty(calls);
    }

    private static string BuildCommandLine(SlashCommandKind kind)
    {
        var descriptor = SlashCommandRegistry.Default.GetRequired(kind);
        return kind switch
        {
            SlashCommandKind.Help or SlashCommandKind.Exit or SlashCommandKind.Interrupt or SlashCommandKind.Init or SlashCommandKind.Draft
                or SlashCommandKind.SendRestored or SlashCommandKind.DropRestored or SlashCommandKind.New or SlashCommandKind.State
                => "/" + descriptor.Name,
            SlashCommandKind.Wait or SlashCommandKind.WaitNextToolCall or SlashCommandKind.WaitComplete
                => "/" + descriptor.Name + " 1",
            SlashCommandKind.WaitEvent
                => "/" + descriptor.Name + " ToolCallStarted 1",
            SlashCommandKind.Config
                => "/" + descriptor.Name + " reload",
            SlashCommandKind.Thread
                => "/" + descriptor.Name + " delete --thread-id thread-001",
            _ => "/" + descriptor.Name + " arg",
        };
    }

    private static string ExpectedCall(SlashCommandKind kind)
        => kind switch
        {
            SlashCommandKind.Approve => "Approval:Approve",
            SlashCommandKind.ApproveSession => "Approval:ApproveForSession",
            SlashCommandKind.ApproveAlways => "Approval:ApproveAndRemember",
            SlashCommandKind.Reject => "Approval:Decline",
            SlashCommandKind.CancelApproval => "Approval:Cancel",
            _ => kind.ToString(),
        };

    private static ChatSlashCommandContext CreateContext(
        List<string>? calls = null,
        List<(string Message, bool IsError)>? messages = null)
        => new(
            () => calls?.Add(nameof(SlashCommandKind.Help)),
            _ =>
            {
                calls?.Add(nameof(SlashCommandKind.Interrupt));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.FollowUp));
                return Task.CompletedTask;
            },
            _ =>
            {
                calls?.Add(nameof(SlashCommandKind.Init));
                return Task.CompletedTask;
            },
            () => calls?.Add(nameof(SlashCommandKind.Draft)),
            _ =>
            {
                calls?.Add(nameof(SlashCommandKind.SendRestored));
                return Task.CompletedTask;
            },
            () => calls?.Add(nameof(SlashCommandKind.DropRestored)),
            (_, decision, _) =>
            {
                calls?.Add("Approval:" + decision);
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.Permissions));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.Input));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.Threads));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.Thread));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.Model));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.Config));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.Reload));
                return Task.CompletedTask;
            },
            _ =>
            {
                calls?.Add(nameof(SlashCommandKind.New));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.Fork));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.Archive));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.Rename));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.Resume));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.Memory));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.Rpc));
                return Task.CompletedTask;
            },
            _ =>
            {
                calls?.Add(nameof(SlashCommandKind.State));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.Wait));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.WaitEvent));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.WaitNextToolCall));
                return Task.CompletedTask;
            },
            (rest, _) =>
            {
                calls?.Add(nameof(SlashCommandKind.WaitComplete));
                return Task.CompletedTask;
            },
            (message, isError) => messages?.Add((message, isError)));
}
