using TianShu.Cli.Interaction.Host;
using TianShu.Cli.Interaction.Rendering;

namespace TianShu.Cli.Interaction.Commands.ModelStatus;

/// <summary>
/// Writes `/model-route status` rows to the terminal without leaking Console details into the runner.
/// 将 `/model-route status` 的终端行输出集中在此处，避免 runner 持有专用 Console 细节。
/// </summary>
internal sealed class ModelStatusConsoleWriter
{
    private readonly object syncRoot;
    private readonly ModelStatusTableRenderer renderer;
    private readonly Action<bool> stopWaitingPlaceholder;
    private readonly Action<string> writePlainControlLine;
    private readonly Func<Action?, IDisposable> beginCommandOverlay;
    private readonly Action<IReadOnlyList<string>> setCommandOverlayLines;
    private CommandOverlayScope? activeOverlayScope;

    public ModelStatusConsoleWriter(
        object syncRoot,
        ModelStatusTableRenderer renderer,
        Action<bool> stopWaitingPlaceholder,
        Action<string> writePlainControlLine,
        Func<Action?, IDisposable>? beginCommandOverlay = null,
        Action<IReadOnlyList<string>>? setCommandOverlayLines = null)
    {
        this.syncRoot = syncRoot ?? throw new ArgumentNullException(nameof(syncRoot));
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        this.stopWaitingPlaceholder = stopWaitingPlaceholder ?? throw new ArgumentNullException(nameof(stopWaitingPlaceholder));
        this.writePlainControlLine = writePlainControlLine ?? throw new ArgumentNullException(nameof(writePlainControlLine));
        this.beginCommandOverlay = beginCommandOverlay ?? (static _ => TerminalConsoleRefreshScope.Noop());
        this.setCommandOverlayLines = setCommandOverlayLines ?? (static _ => { });
    }

    public ModelStatusCommandOutput CreateOutput(bool styled, CancellationToken cancellationToken = default)
    {
        var overlayCancellation = styled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        var userCancellationRequested = false;
        return new ModelStatusCommandOutput(
            styled,
            row => WriteNoWrapLine(row, styled),
            TerminalConsoleRefreshScope.HideCursorForRefresh,
            (rows, rewriteExistingRows) => WriteLiveBatchRows(rows, rewriteExistingRows, styled),
            row => WriteFinalRow(row, styled),
            styled
                ? () => BeginCommandOverlayScope(
                    overlayCancellation!,
                    () =>
                    {
                        userCancellationRequested = true;
                        overlayCancellation!.Cancel();
                    })
                : TerminalConsoleRefreshScope.Noop,
            overlayCancellation?.Token ?? cancellationToken,
            () => userCancellationRequested);
    }

    internal void WriteLiveBatchRows(
        IReadOnlyList<string> rows,
        bool rewriteExistingRows,
        bool styled)
    {
        lock (syncRoot)
        {
            stopWaitingPlaceholder(true);
            if (styled)
            {
                WriteOverlayLiveRowsUnsafe(rows, rewriteExistingRows);
                return;
            }

            WritePlainLiveBatchRowsUnsafe(rows, rewriteExistingRows);
        }
    }

    internal void WriteNoWrapLine(string row, bool styled)
    {
        if (!styled)
        {
            writePlainControlLine(row);
            return;
        }

        lock (syncRoot)
        {
            stopWaitingPlaceholder(true);
            WriteOverlayLineUnsafe(row);
        }
    }

    internal void WriteFinalRow(string row, bool styled)
    {
        if (!styled)
        {
            writePlainControlLine(row);
            return;
        }

        lock (syncRoot)
        {
            stopWaitingPlaceholder(true);
            WriteOverlayLineUnsafe(row);
        }
    }

    private IDisposable BeginCommandOverlayScope(CancellationTokenSource overlayCancellation, Action onEscape)
    {
        var scope = new CommandOverlayScope(this, beginCommandOverlay(onEscape), overlayCancellation);
        lock (syncRoot)
        {
            activeOverlayScope = scope;
        }

        return scope;
    }

    private void WritePlainLiveBatchRowsUnsafe(IReadOnlyList<string> rows, bool rewriteExistingRows)
    {
        if (rewriteExistingRows && rows.Count > 0)
        {
            Console.Write($"\u001b[{rows.Count}F");
        }

        foreach (var row in rows)
        {
            Console.Write('\r');
            Console.Write("\u001b[2K");
            Console.WriteLine(renderer.FitTerminalRow(row, styled: false));
        }
    }

    private void WriteOverlayLineUnsafe(string row)
    {
        activeOverlayScope?.AppendLine(renderer.FitTerminalRow(row, styled: true));
        PublishOverlayLinesUnsafe();
    }

    private void WriteOverlayLiveRowsUnsafe(IReadOnlyList<string> rows, bool rewriteExistingRows)
    {
        var fittedRows = rows.Select(row => renderer.FitTerminalRow(row, styled: true)).ToArray();
        activeOverlayScope?.WriteLiveRows(fittedRows, rewriteExistingRows);
        PublishOverlayLinesUnsafe();
    }

    private void PublishOverlayLinesUnsafe()
        => setCommandOverlayLines(activeOverlayScope?.Lines is { } lines ? lines : Array.Empty<string>());

    private void EndCommandOverlayScope(CommandOverlayScope scope)
    {
        string[] finalLines;
        var persistFinalLines = false;
        lock (syncRoot)
        {
            if (ReferenceEquals(activeOverlayScope, scope))
            {
                activeOverlayScope = null;
                persistFinalLines = true;
            }

            finalLines = scope.Lines.ToArray();
            setCommandOverlayLines(Array.Empty<string>());
            scope.DisposeInnerScope();
            if (persistFinalLines && finalLines.Length > 0)
            {
                writePlainControlLine(string.Join(Environment.NewLine, finalLines));
            }
        }
    }

    private sealed class CommandOverlayScope(
        ModelStatusConsoleWriter owner,
        IDisposable innerScope,
        CancellationTokenSource overlayCancellation) : IDisposable
    {
        private ModelStatusConsoleWriter? owner = owner;
        private IDisposable? innerScope = innerScope;
        private CancellationTokenSource? overlayCancellation = overlayCancellation;
        private int liveRowStart = -1;
        private int liveRowCount;

        public List<string> Lines { get; } = [];

        public void AppendLine(string line)
            => Lines.Add(line);

        public void WriteLiveRows(IReadOnlyList<string> rows, bool rewriteExistingRows)
        {
            if (!rewriteExistingRows || liveRowStart < 0)
            {
                liveRowStart = Lines.Count;
                liveRowCount = rows.Count;
                Lines.AddRange(rows);
                return;
            }

            Lines.RemoveRange(liveRowStart, liveRowCount);
            Lines.InsertRange(liveRowStart, rows);
            liveRowCount = rows.Count;
        }

        public void Dispose()
        {
            var currentOwner = Interlocked.Exchange(ref owner, null);
            currentOwner?.EndCommandOverlayScope(this);
        }

        public void DisposeInnerScope()
        {
            var scope = Interlocked.Exchange(ref innerScope, null);
            scope?.Dispose();
            var cancellation = Interlocked.Exchange(ref overlayCancellation, null);
            cancellation?.Dispose();
        }
    }
}
