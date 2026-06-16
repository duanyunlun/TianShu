using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tests;

public sealed class KernelReadinessFlagTests
{
    [Fact]
    public async Task IsReady_ShouldAutoReady_WhenNoSubscriptions()
    {
        var flag = new KernelReadinessFlag();
        Assert.True(flag.IsReady());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await flag.WaitReadyAsync(cts.Token);
    }

    [Fact]
    public async Task WaitReadyAsync_ShouldBlock_UntilMarkedReady()
    {
        var flag = new KernelReadinessFlag();
        var token = await flag.SubscribeAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var waiter = flag.WaitReadyAsync(cts.Token);

        await Task.Delay(100, cts.Token);
        Assert.False(waiter.IsCompleted);

        Assert.True(await flag.MarkReadyAsync(token, CancellationToken.None));
        await waiter;
        Assert.True(flag.IsReady());
    }

    [Fact]
    public async Task MarkReadyAsync_ShouldReturnFalse_WhenTokenUnknown()
    {
        var flag = new KernelReadinessFlag();
        _ = await flag.SubscribeAsync(CancellationToken.None);

        Assert.False(await flag.MarkReadyAsync(new KernelReadinessToken(999), CancellationToken.None));
        Assert.False(flag.IsReady());
    }

    [Fact]
    public async Task SubscribeAsync_ShouldThrow_WhenAlreadyReady()
    {
        var flag = new KernelReadinessFlag();
        Assert.True(flag.IsReady());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => flag.SubscribeAsync(CancellationToken.None));
    }
}

