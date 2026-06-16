using TianShu.Contracts.Identity;

namespace TianShu.ControlPlane.Abstractions.Identity;

/// <summary>
/// 身份平面 northbound 抽象。
/// Northbound abstraction for the identity plane.
/// </summary>
public interface IIdentityControlPlane
{
    /// <summary>
    /// 读取账户画像。
    /// Gets an account profile.
    /// </summary>
    Task<Account?> GetAccountProfileAsync(GetAccountProfile query, CancellationToken cancellationToken);

    /// <summary>
    /// 读取账户绑定设备列表。
    /// Gets the devices bound to an account.
    /// </summary>
    Task<IReadOnlyList<DeviceBinding>> ListBoundDevicesAsync(ListBoundDevices query, CancellationToken cancellationToken);
}
