using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelTestSyncRuntimeSupportTests
{
    [Fact]
    public async Task ExecuteAsync_BarrierTimesOutWhenMissingParticipants()
    {
        var barrierId = "barrier_" + Guid.NewGuid().ToString("N");
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            barrier = new
            {
                id = barrierId,
                participants = 2,
                timeout_ms = 80,
            },
        }));

        var result = await KernelTestSyncRuntimeSupport.ExecuteAsync(
            args.RootElement,
            new KernelToolCallContext("thread", "turn", Environment.CurrentDirectory),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("test_sync_tool barrier wait timed out", result.OutputText);
    }

    [Fact]
    public async Task ExecuteAsync_BarrierReleasesWhenAllParticipantsArrive()
    {
        var barrierId = "barrier_" + Guid.NewGuid().ToString("N");

        Task<KernelToolResult> WaitAsync()
        {
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                barrier = new
                {
                    id = barrierId,
                    participants = 2,
                    timeout_ms = 2000,
                },
            }));

            return KernelTestSyncRuntimeSupport.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", Environment.CurrentDirectory),
                CancellationToken.None);
        }

        var one = Task.Run(WaitAsync);
        var two = Task.Run(WaitAsync);
        var results = await Task.WhenAll(one, two);

        Assert.All(results, static result =>
        {
            Assert.True(result.Success);
            Assert.Equal("ok", result.OutputText);
        });
    }

    [Fact]
    public async Task ExecuteAsync_BarrierRejectsZeroParticipants()
    {
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            barrier = new
            {
                id = "x",
                participants = 0,
            },
        }));

        var result = await KernelTestSyncRuntimeSupport.ExecuteAsync(
            args.RootElement,
            new KernelToolCallContext("thread", "turn", Environment.CurrentDirectory),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("barrier participants must be greater than zero", result.OutputText);
    }

    [Fact]
    public async Task ExecuteAsync_BarrierRejectsZeroTimeout()
    {
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            barrier = new
            {
                id = "x",
                participants = 2,
                timeout_ms = 0,
            },
        }));

        var result = await KernelTestSyncRuntimeSupport.ExecuteAsync(
            args.RootElement,
            new KernelToolCallContext("thread", "turn", Environment.CurrentDirectory),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("barrier timeout must be greater than zero", result.OutputText);
    }
}
