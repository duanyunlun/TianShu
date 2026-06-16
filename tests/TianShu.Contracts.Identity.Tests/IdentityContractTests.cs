using TianShu.Contracts.Identity;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Identity.Tests;

public sealed class IdentityContractTests
{
    [Fact]
    public void IdentityQueries_PreserveRequestedAccountIdentity()
    {
        var accountId = new AccountId("account-001");

        var profileQuery = new GetAccountProfile(accountId);
        var devicesQuery = new ListBoundDevices(accountId);

        Assert.Equal(accountId, profileQuery.AccountId);
        Assert.Equal(accountId, devicesQuery.AccountId);
    }

    [Fact]
    public void Account_RejectsBlankDisplayName()
    {
        Assert.Throws<ArgumentException>(() => new Account(new AccountId("account-001"), " "));
    }

    [Fact]
    public void DeviceBinding_PreservesPlatformAndAccount()
    {
        var binding = new DeviceBinding(
            new DeviceId("device-001"),
            new AccountId("account-001"),
            "Laptop",
            "windows");

        Assert.Equal("windows", binding.Platform);
        Assert.Equal("account-001", binding.AccountId.Value);
    }
}
