using TianShu.Contracts.Identity;

namespace TianShu.Execution.Runtime;

public interface IIdentityControlPlaneClient
{
    Task<Account?> GetAccountProfileAsync(GetAccountProfile query, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeviceBinding>> ListBoundDevicesAsync(ListBoundDevices query, CancellationToken cancellationToken);
}
