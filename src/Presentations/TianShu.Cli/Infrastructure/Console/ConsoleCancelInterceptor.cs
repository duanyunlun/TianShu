namespace TianShu.Cli;

internal static class ConsoleCancelInterceptor
{
    private static readonly object Gate = new();
    private static Func<bool>? callback;

    public static IDisposable Register(Func<bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (Gate)
        {
            var previous = callback;
            callback = handler;
            return new RestoreScope(previous, handler);
        }
    }

    public static bool TryHandle()
    {
        Func<bool>? handler;
        lock (Gate)
        {
            handler = callback;
        }

        return handler is not null && handler();
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly Func<bool>? previous;
        private readonly Func<bool> current;
        private bool disposed;

        public RestoreScope(Func<bool>? previous, Func<bool> current)
        {
            this.previous = previous;
            this.current = current;
        }

        public void Dispose()
        {
            lock (Gate)
            {
                if (disposed)
                {
                    return;
                }

                if (ReferenceEquals(callback, current))
                {
                    callback = previous;
                }

                disposed = true;
            }
        }
    }
}
