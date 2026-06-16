using TianShu.ControlPlane.Abstractions;
using TianShu.ControlPlane.Abstractions.Agents;
using TianShu.ControlPlane.Abstractions.Artifacts;
using TianShu.ControlPlane.Abstractions.Catalog;
using TianShu.ControlPlane.Abstractions.Collaboration;
using TianShu.ControlPlane.Abstractions.Conversations;
using TianShu.ControlPlane.Abstractions.Diagnostics;
using TianShu.ControlPlane.Abstractions.Governance;
using TianShu.ControlPlane.Abstractions.Identity;
using TianShu.ControlPlane.Abstractions.Memory;
using TianShu.ControlPlane.Abstractions.Operations;
using TianShu.ControlPlane.Abstractions.Sessions;
using TianShu.ControlPlane.Abstractions.Subscriptions;
using TianShu.ControlPlane.Abstractions.Workflows;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.ControlPlane;

namespace TianShu.ControlPlane;

/// <summary>
/// TianShu 正式控制平面组合壳。
/// Formal TianShu control-plane composition shell.
/// </summary>
public sealed class TianShuControlPlane : ITianShuControlPlane
{
    private readonly ITianShuControlPlane inner;
    private readonly ControlOperationNormalizer operationNormalizer;

    /// <summary>
    /// 使用现成 control-plane 实例初始化正式组合壳。
    /// Initializes the formal control-plane shell with an existing implementation.
    /// </summary>
    public TianShuControlPlane(ITianShuControlPlane inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        operationNormalizer = ControlOperationNormalizer.Default;
    }

    /// <summary>
    /// 使用 execution runtime 创建最小 runtime-backed control-plane 实现。
    /// Creates the minimum runtime-backed control-plane implementation from an execution runtime.
    /// </summary>
    public TianShuControlPlane(IExecutionRuntime runtime)
        : this(new RuntimeControlPlaneAdapter(runtime))
    {
    }

    public ICollaborationControlPlane Collaboration => inner.Collaboration;

    public ISessionControlPlane Sessions => inner.Sessions;

    public IConversationControlPlane Conversations => inner.Conversations;

    public IWorkflowControlPlane Workflows => inner.Workflows;

    public IAgentControlPlane Agents => inner.Agents;

    public IGovernanceControlPlane Governance => inner.Governance;

    public ICatalogControlPlane Catalog => inner.Catalog;

    public IArtifactControlPlane Artifacts => inner.Artifacts;

    public IDiagnosticsControlPlane Diagnostics => inner.Diagnostics;

    public IIdentityControlPlane Identity => inner.Identity;

    public IMemoryControlPlane Memory => inner.Memory;

    public IProjectionSubscriptions Subscriptions => inner.Subscriptions;

    public Task<ControlOperationResult> ProcessAsync(ControlOperationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(operationNormalizer.Normalize(request));
    }
}
