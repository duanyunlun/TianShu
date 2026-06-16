using TianShu.Cli.Interaction.Host;
using TianShu.Contracts.Conversations;
using TianShu.Execution.Runtime;

namespace TianShu.Cli;

internal sealed class InteractiveChatRunner
{
    private readonly IInteractiveChatHostFactory hostFactory;

    public InteractiveChatRunner()
        : this(new InteractiveChatHostFactory(TianShuAppHostRuntimeClientFactory.Create))
    {
    }

    internal InteractiveChatRunner(Func<IExecutionRuntime> runtimeFactory)
        : this(new InteractiveChatHostFactory(runtimeFactory))
    {
    }

    internal InteractiveChatRunner(IInteractiveChatHostFactory hostFactory)
    {
        this.hostFactory = hostFactory ?? throw new ArgumentNullException(nameof(hostFactory));
    }

    public Task<int> RunAsync(ChatCommandOptions options, CancellationToken cancellationToken)
        => hostFactory.Create().RunAsync(options, cancellationToken);

    private static IReadOnlyList<ControlPlaneInputItem> BuildStructuredUserInputsFromText(string? text)
        => InteractiveChatSessionHost.BuildStructuredUserInputsFromText(text);

    internal static bool ShouldUseTerminalChatTui(
        ChatCommandOptions options,
        bool hasScript,
        bool isInputRedirected,
        bool isOutputRedirected)
        => InteractiveChatSessionHost.ShouldUseTerminalChatTui(
            options,
            hasScript,
            isInputRedirected,
            isOutputRedirected);
}
