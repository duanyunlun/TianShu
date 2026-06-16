using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Identity;

/// <summary>
/// 查询账户画像。
/// Query that fetches an account profile.
/// </summary>
public sealed record GetAccountProfile(AccountId AccountId);

/// <summary>
/// 查询账户绑定设备列表。
/// Query that lists the devices bound to an account.
/// </summary>
public sealed record ListBoundDevices(AccountId AccountId);
