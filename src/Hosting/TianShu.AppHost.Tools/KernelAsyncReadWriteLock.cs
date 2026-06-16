namespace TianShu.AppHost.Tools;

internal sealed class KernelAsyncReadWriteLock
{
    private readonly SemaphoreSlim readersGate = new(1, 1);
    private readonly SemaphoreSlim writerGate = new(1, 1);
    private int readers;

    public async Task<IDisposable> AcquireReadAsync(CancellationToken cancellationToken)
    {
        await readersGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            readers++;
            if (readers == 1)
            {
                try
                {
                    await writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    readers--;
                    throw;
                }
            }
        }
        finally
        {
            readersGate.Release();
        }

        return new Releaser(this, isWrite: false);
    }

    public async Task<IDisposable> AcquireWriteAsync(CancellationToken cancellationToken)
    {
        await writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(this, isWrite: true);
    }

    private void ReleaseRead()
    {
        readersGate.Wait();
        try
        {
            readers = Math.Max(0, readers - 1);
            if (readers == 0)
            {
                writerGate.Release();
            }
        }
        finally
        {
            readersGate.Release();
        }
    }

    private void ReleaseWrite()
    {
        writerGate.Release();
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KernelAsyncReadWriteLock owner;
        private readonly bool isWrite;
        private int disposed;

        public Releaser(KernelAsyncReadWriteLock owner, bool isWrite)
        {
            this.owner = owner;
            this.isWrite = isWrite;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            if (isWrite)
            {
                owner.ReleaseWrite();
            }
            else
            {
                owner.ReleaseRead();
            }
        }
    }
}

