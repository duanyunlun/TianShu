using TianShu.Execution.Runtime;

namespace TianShu.Cli.Interaction.Host;

internal interface IInteractiveChatHostFactory
{
    InteractiveChatSessionHost Create();
}

internal sealed class InteractiveChatHostFactory : IInteractiveChatHostFactory
{
    private readonly Func<IExecutionRuntime> runtimeFactory;

    public InteractiveChatHostFactory(Func<IExecutionRuntime> runtimeFactory)
    {
        this.runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
    }

    public InteractiveChatSessionHost Create()
        => new(runtimeFactory);
}
