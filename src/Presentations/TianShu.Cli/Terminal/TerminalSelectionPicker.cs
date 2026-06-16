namespace TianShu.Cli.Terminal;

/// <summary>
/// Provides a small keyboard-driven selection overlay for TianShu terminal prompts.
/// 为 TianShu 终端提示提供一个轻量键盘选择 overlay。
/// </summary>
internal sealed class TerminalSelectionPicker(Func<IDisposable>? beginExclusiveFrameScope = null)
{
    private const int MinimumVisibleRowCount = 4;
    private const int FrameReservedLineCount = 6;
    private readonly Func<IDisposable>? beginExclusiveFrameScope = beginExclusiveFrameScope;

    public async Task<int?> SelectAsync(
        IReadOnlyList<string> rows,
        string title,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var selectedIndex = 0;
        var renderedLineCount = 0;
        using var exclusiveFrameScope = beginExclusiveFrameScope?.Invoke() ?? NoopDisposable.Instance;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                renderedLineCount = Render(rows, title, selectedIndex, renderedLineCount);
                var key = await ConsoleTerminalInput.Shared.ReadKeyAsync(cancellationToken).ConfigureAwait(false);
                if (key is null)
                {
                    continue;
                }

                switch (key.Value.Kind)
                {
                    case TerminalKeyKind.UpArrow:
                        selectedIndex = selectedIndex <= 0 ? rows.Count - 1 : selectedIndex - 1;
                        break;
                    case TerminalKeyKind.DownArrow:
                        selectedIndex = selectedIndex >= rows.Count - 1 ? 0 : selectedIndex + 1;
                        break;
                    case TerminalKeyKind.Home:
                        selectedIndex = 0;
                        break;
                    case TerminalKeyKind.End:
                        selectedIndex = rows.Count - 1;
                        break;
                    case TerminalKeyKind.Enter:
                        return selectedIndex;
                    case TerminalKeyKind.Escape:
                        return null;
                }
            }

            return null;
        }
        finally
        {
            Clear(renderedLineCount);
        }
    }

    internal static string BuildFrame(
        IReadOnlyList<string> rows,
        string title,
        int selectedIndex,
        int? visibleRowCount = null)
    {
        var clampedSelectedIndex = rows.Count == 0
            ? 0
            : Math.Clamp(selectedIndex, 0, rows.Count - 1);
        var resolvedVisibleRowCount = ResolveVisibleRowCount(rows.Count, visibleRowCount);
        var startIndex = ResolveStartIndex(rows.Count, clampedSelectedIndex, resolvedVisibleRowCount);
        var endIndex = Math.Min(rows.Count, startIndex + resolvedVisibleRowCount);
        var counter = rows.Count > resolvedVisibleRowCount
            ? $"  {clampedSelectedIndex + 1}/{rows.Count}"
            : string.Empty;
        var frameRows = new List<string>
        {
            $"{title}{counter}  ↑/↓ 选择  Enter 确认  Esc 取消",
            string.Empty,
        };
        for (var index = startIndex; index < endIndex; index++)
        {
            var marker = index == clampedSelectedIndex ? "> " : "  ";
            frameRows.Add(marker + rows[index]);
        }

        return string.Join(Environment.NewLine, frameRows);
    }

    private static int Render(IReadOnlyList<string> rows, string title, int selectedIndex, int previousLineCount)
    {
        Clear(previousLineCount);
        var frame = BuildFrame(
            rows,
            title,
            Math.Clamp(selectedIndex, 0, rows.Count - 1),
            GetTerminalVisibleRowCount(rows.Count));
        Console.WriteLine(frame);
        return frame.Split(Environment.NewLine, StringSplitOptions.None).Length + 1;
    }

    private static int GetTerminalVisibleRowCount(int rowCount)
    {
        if (rowCount <= 0)
        {
            return 0;
        }

        try
        {
            var availableRows = Console.WindowHeight - FrameReservedLineCount;
            return ResolveVisibleRowCount(rowCount, availableRows);
        }
        catch (IOException)
        {
            return rowCount;
        }
        catch (InvalidOperationException)
        {
            return rowCount;
        }
    }

    private static int ResolveVisibleRowCount(int rowCount, int? requestedVisibleRowCount)
    {
        if (rowCount <= 0)
        {
            return 0;
        }

        var requested = requestedVisibleRowCount.GetValueOrDefault(rowCount);
        var minimum = Math.Min(MinimumVisibleRowCount, rowCount);
        return Math.Clamp(requested, minimum, rowCount);
    }

    private static int ResolveStartIndex(int rowCount, int selectedIndex, int visibleRowCount)
    {
        if (rowCount <= 0 || visibleRowCount <= 0 || rowCount <= visibleRowCount)
        {
            return 0;
        }

        var centeredStartIndex = selectedIndex - (visibleRowCount / 2);
        return Math.Clamp(centeredStartIndex, 0, rowCount - visibleRowCount);
    }

    private static void Clear(int lineCount)
    {
        if (lineCount <= 0)
        {
            return;
        }

        Console.Write('\r');
        Console.Write("\u001b[2K");
        for (var index = 1; index < lineCount; index++)
        {
            Console.Write("\u001b[1A");
            Console.Write("\u001b[2K");
        }

        Console.Write('\r');
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
