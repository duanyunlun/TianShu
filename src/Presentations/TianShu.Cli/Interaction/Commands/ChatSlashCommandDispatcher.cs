namespace TianShu.Cli.Interaction.Commands;

/// <summary>
/// Dispatches classified chat slash commands to typed host callbacks.
/// 将已分类的 chat slash command 分发到强类型宿主回调。
/// </summary>
internal sealed class ChatSlashCommandDispatcher
{
    private readonly ChatSlashCommandHandlerRegistry handlers;

    public ChatSlashCommandDispatcher()
        : this(ChatSlashCommandHandlerRegistry.Default)
    {
    }

    public ChatSlashCommandDispatcher(ChatSlashCommandHandlerRegistry handlers)
        => this.handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));

    public async Task<bool> ExecuteAsync(
        string line,
        ChatSlashCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var classified = SlashCommandClassifier.Classify(line);
        if (classified.Kind is SlashCommandKind.Empty)
        {
            return false;
        }

        var rest = classified.Rest;
        if (handlers.TryGetHandler(classified.Kind, out var handler))
        {
            return await handler.ExecuteAsync(rest, context, cancellationToken).ConfigureAwait(false);
        }

        context.WriteLine($"未知命令：/{classified.Command}", true);
        return false;
    }
}
