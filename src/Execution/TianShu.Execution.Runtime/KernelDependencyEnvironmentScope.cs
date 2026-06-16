using System.Threading;

namespace TianShu.Execution.Runtime;

internal static class KernelDependencyEnvironmentScope
{
    private static readonly AsyncLocal<IReadOnlyDictionary<string, string>?> CurrentOverrides = new();

    public static IReadOnlyDictionary<string, string>? Current => CurrentOverrides.Value;

    public static IDisposable Push(IReadOnlyDictionary<string, string>? environment)
    {
        var previous = CurrentOverrides.Value;
        CurrentOverrides.Value = environment;
        return new RestoreScope(previous);
    }

    public static string? ReadEnvironmentVariable(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var current = CurrentOverrides.Value;
        if (current is not null && current.TryGetValue(name, out var overrideValue))
        {
            return string.IsNullOrWhiteSpace(overrideValue)
                ? null
                : overrideValue;
        }

        return Environment.GetEnvironmentVariable(name);
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly IReadOnlyDictionary<string, string>? previous;
        private int disposed;

        public RestoreScope(IReadOnlyDictionary<string, string>? previous)
        {
            this.previous = previous;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            CurrentOverrides.Value = previous;
        }
    }
}
