using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tests;

public sealed class KernelAsyncReadWriteLockTests
{
    [Fact]
    public async Task AcquireReadAsync_ShouldAllowConcurrentReaders()
    {
        var rw = new KernelAsyncReadWriteLock();

        using var read1 = await rw.AcquireReadAsync(CancellationToken.None);

        var read2Task = rw.AcquireReadAsync(CancellationToken.None);
        using var read2 = await read2Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.NotNull(read1);
        Assert.NotNull(read2);
    }

    [Fact]
    public async Task AcquireWriteAsync_ShouldBlockReadersUntilReleased()
    {
        var rw = new KernelAsyncReadWriteLock();

        var write = await rw.AcquireWriteAsync(CancellationToken.None);
        Task<IDisposable>? readTask = null;
        try
        {
            readTask = rw.AcquireReadAsync(CancellationToken.None);
            await Task.Delay(50);
            Assert.False(readTask.IsCompleted);
        }
        finally
        {
            write.Dispose();
        }

        using var read = await readTask!.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(read);
    }

    [Fact]
    public async Task AcquireWriteAsync_ShouldBlockWhenReaderActive()
    {
        var rw = new KernelAsyncReadWriteLock();

        var read = await rw.AcquireReadAsync(CancellationToken.None);
        var writeTask = rw.AcquireWriteAsync(CancellationToken.None);

        await Task.Delay(50);
        Assert.False(writeTask.IsCompleted);

        read.Dispose();

        using var write = await writeTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(write);
    }
}

