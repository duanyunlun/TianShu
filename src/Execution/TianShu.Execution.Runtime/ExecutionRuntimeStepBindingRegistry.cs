using TianShu.Contracts.Agents;
using TianShu.Contracts.Tools;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Runtime;

/// <summary>
/// RuntimeStep live bridge 绑定表，用于把已批准的 RuntimeStep 映射到受控 provider/tool 实现。
/// RuntimeStep live bridge binding registry that maps approved RuntimeSteps to controlled provider/tool implementations.
/// </summary>
public sealed class ExecutionRuntimeStepBindingRegistry : IAsyncDisposable
{
    private readonly IReadOnlyDictionary<string, IProviderModule> providers;
    private readonly IReadOnlyDictionary<string, ITianShuTool> tools;
    private readonly IReadOnlyDictionary<string, ISubAgentModule> subAgentModules;
    private bool disposed;

    public ExecutionRuntimeStepBindingRegistry(
        IReadOnlyDictionary<string, IProviderModule>? providers = null,
        IReadOnlyDictionary<string, ITianShuTool>? tools = null,
        IReadOnlyDictionary<string, ISubAgentModule>? subAgentModules = null)
    {
        this.providers = providers ?? new Dictionary<string, IProviderModule>(StringComparer.Ordinal);
        this.tools = tools ?? new Dictionary<string, ITianShuTool>(StringComparer.Ordinal);
        this.subAgentModules = subAgentModules ?? new Dictionary<string, ISubAgentModule>(StringComparer.Ordinal);
    }

    public bool TryGetProvider(string providerModuleId, out IProviderModule provider)
        => providers.TryGetValue(providerModuleId, out provider!);

    public bool TryGetTool(string capabilityToolId, out ITianShuTool tool)
        => tools.TryGetValue(capabilityToolId, out tool!);

    public bool TryGetSubAgentModule(string moduleId, out ISubAgentModule module)
        => subAgentModules.TryGetValue(moduleId, out module!);

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        List<object> disposableBindings = [];
        AddDisposableBindings(providers.Values, disposableBindings);
        AddDisposableBindings(tools.Values, disposableBindings);
        AddDisposableBindings(subAgentModules.Values, disposableBindings);

        foreach (var binding in disposableBindings)
        {
            switch (binding)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }

    private static void AddDisposableBindings<TBinding>(
        IEnumerable<TBinding> bindings,
        List<object> disposableBindings)
        where TBinding : class
    {
        foreach (var binding in bindings)
        {
            if (binding is not IAsyncDisposable and not IDisposable)
            {
                continue;
            }

            if (disposableBindings.Any(existing => ReferenceEquals(existing, binding)))
            {
                continue;
            }

            disposableBindings.Add(binding);
        }
    }

    public static ExecutionRuntimeStepBindingRegistry Empty { get; } = new();
}
