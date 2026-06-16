using System.Threading.Channels;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tests;

public sealed class KernelQueuePairTests
{
    [Fact]
    public async Task QueuePair_ShouldBehaveAsBoundedQueues()
    {
        var queues = new KernelQueuePair<int, string>(capacity: 1);

        Assert.True(queues.TryEnqueueSubmission(1));
        Assert.False(queues.TryEnqueueSubmission(2));

        Assert.True(queues.TryPublishEvent("a"));
        Assert.False(queues.TryPublishEvent("b"));

        queues.Complete();

        await Assert.ThrowsAsync<ChannelClosedException>(async () =>
        {
            await queues.EnqueueSubmissionAsync(3, CancellationToken.None);
        });
    }
}

