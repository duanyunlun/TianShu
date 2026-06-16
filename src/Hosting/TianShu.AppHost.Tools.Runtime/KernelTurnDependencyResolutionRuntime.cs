using TianShu.Contracts.Catalog;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Turn dependency resolution 运行时，负责模型执行前的插件指令、技能注入和技能依赖解析。
/// Runtime that resolves plugin instructions, skill injections, and skill dependencies before model execution.
/// </summary>
internal sealed class KernelTurnDependencyResolutionRuntime
{
    private readonly Func<IReadOnlyList<KernelTurnInputItem>?, string, CancellationToken, Task<string?>> buildExplicitPluginInstructionsAsync;
    private readonly Func<TurnRequestContext, string, CancellationToken, Task<List<KernelSkillDescriptor>>> resolveMentionedSkillsAsync;
    private readonly Func<IReadOnlyList<KernelSkillDescriptor>, List<string>> buildSkillInjectionMessages;
    private readonly Func<TurnOperationState, TurnRequestContext, IReadOnlyList<KernelSkillDescriptor>, CancellationToken, Task> resolveSkillEnvironmentDependenciesAsync;
    private readonly Func<TurnOperationState, TurnRequestContext, IReadOnlyList<KernelSkillDescriptor>, CancellationToken, Task> resolveSkillMcpDependenciesAsync;

    public KernelTurnDependencyResolutionRuntime(
        Func<IReadOnlyList<KernelTurnInputItem>?, string, CancellationToken, Task<string?>> buildExplicitPluginInstructionsAsync,
        Func<TurnRequestContext, string, CancellationToken, Task<List<KernelSkillDescriptor>>> resolveMentionedSkillsAsync,
        Func<IReadOnlyList<KernelSkillDescriptor>, List<string>> buildSkillInjectionMessages,
        Func<TurnOperationState, TurnRequestContext, IReadOnlyList<KernelSkillDescriptor>, CancellationToken, Task> resolveSkillEnvironmentDependenciesAsync,
        Func<TurnOperationState, TurnRequestContext, IReadOnlyList<KernelSkillDescriptor>, CancellationToken, Task> resolveSkillMcpDependenciesAsync)
    {
        this.buildExplicitPluginInstructionsAsync = buildExplicitPluginInstructionsAsync
                                                    ?? throw new ArgumentNullException(nameof(buildExplicitPluginInstructionsAsync));
        this.resolveMentionedSkillsAsync = resolveMentionedSkillsAsync
                                           ?? throw new ArgumentNullException(nameof(resolveMentionedSkillsAsync));
        this.buildSkillInjectionMessages = buildSkillInjectionMessages
                                           ?? throw new ArgumentNullException(nameof(buildSkillInjectionMessages));
        this.resolveSkillEnvironmentDependenciesAsync = resolveSkillEnvironmentDependenciesAsync
                                                        ?? throw new ArgumentNullException(nameof(resolveSkillEnvironmentDependenciesAsync));
        this.resolveSkillMcpDependenciesAsync = resolveSkillMcpDependenciesAsync
                                                ?? throw new ArgumentNullException(nameof(resolveSkillMcpDependenciesAsync));
    }

    public Task<TurnRequestContext> ResolveAsync(
        TurnOperationState state,
        TurnRequestContext context,
        CancellationToken cancellationToken)
        => KernelTurnExecutionRuntimeHelpers.ResolveTurnDependenciesAsync(
            state,
            context,
            buildExplicitPluginInstructionsAsync,
            resolveMentionedSkillsAsync,
            buildSkillInjectionMessages,
            resolveSkillEnvironmentDependenciesAsync,
            resolveSkillMcpDependenciesAsync,
            cancellationToken);
}
