namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Provides terminal cursor scopes used while repainting terminal frames.
/// 提供终端刷新期间隐藏和恢复真实光标的作用域。
/// </summary>
internal static class TerminalConsoleRefreshScope
{
    public static IDisposable HideCursorForRefresh()
    {
        if (Console.IsOutputRedirected)
        {
            return NoopDisposable.Instance;
        }

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                Console.Write("\u001b[?25l");
                return AnsiCursorVisibilityScope.Instance;
            }
            catch (IOException)
            {
                return NoopDisposable.Instance;
            }
            catch (InvalidOperationException)
            {
                return NoopDisposable.Instance;
            }
        }

        try
        {
            var previous = Console.CursorVisible;
            Console.CursorVisible = false;
            return new ConsoleCursorVisibilityScope(previous);
        }
        catch (IOException)
        {
            return NoopDisposable.Instance;
        }
        catch (InvalidOperationException)
        {
            return NoopDisposable.Instance;
        }
        catch (PlatformNotSupportedException)
        {
            return NoopDisposable.Instance;
        }
    }

    internal static IDisposable Noop()
        => NoopDisposable.Instance;

    private sealed class ConsoleCursorVisibilityScope(bool previous) : IDisposable
    {
        public void Dispose()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                Console.CursorVisible = previous;
            }
            catch (IOException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (PlatformNotSupportedException)
            {
            }
        }
    }

    private sealed class AnsiCursorVisibilityScope : IDisposable
    {
        public static readonly AnsiCursorVisibilityScope Instance = new();

        private AnsiCursorVisibilityScope()
        {
        }

        public void Dispose()
        {
            try
            {
                Console.Write("\u001b[?25h");
            }
            catch (IOException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        private NoopDisposable()
        {
        }

        public void Dispose()
        {
        }
    }
}
