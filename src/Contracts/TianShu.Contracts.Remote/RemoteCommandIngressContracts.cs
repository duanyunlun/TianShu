namespace TianShu.Contracts.Remote;

/// <summary>
/// 远程命令进入 TianShu 的唯一写入入口；实现必须转入 Host Gateway / Control Plane。
/// The single remote command ingress for TianShu writes; implementations must delegate into Host Gateway / Control Plane.
/// </summary>
public interface IRemoteCommandIngress
{
    /// <summary>
    /// 提交一条远程命令并返回受理结果。
    /// Submits one remote command and returns the admission result.
    /// </summary>
    ValueTask<RemoteCommandResult> SubmitCommandAsync<TPayload>(
        RemoteCommandEnvelope<TPayload> command,
        CancellationToken cancellationToken)
        where TPayload : IRemoteCommandPayload;
}
