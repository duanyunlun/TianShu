using TianShu.Cli.Interaction.Commands.ModelStatus;
using TianShu.Cli.Interaction.Host;
using TianShu.Cli.Interaction.Rendering;

namespace TianShu.Cli.Tests;

public sealed class ModelStatusConsoleWriterTests
{
    [Fact]
    public void CreateOutput_PlainRows_UseControlPlaneLine()
    {
        var plainLines = new List<string>();
        var stopCount = 0;
        var writer = new ModelStatusConsoleWriter(
            new object(),
            new ModelStatusTableRenderer(static () => 120, static () => true),
            _ => stopCount++,
            plainLines.Add);

        var output = writer.CreateOutput(styled: false);
        output.WriteNoWrapLine("header");
        output.WriteFinalRow("final");

        Assert.Equal(["header", "final"], plainLines);
        Assert.Equal(0, stopCount);
    }

    [Fact]
    public void CreateOutput_StyledRows_UseCommandOverlayScope()
    {
        var events = new List<string>();
        var writer = new ModelStatusConsoleWriter(
            new object(),
            new ModelStatusTableRenderer(static () => 120, static () => true),
            _ => { },
            _ => { },
            _ =>
            {
                events.Add("begin");
                return new DisposeAction(() => events.Add("end"));
            },
            _ => { });

        var output = writer.CreateOutput(styled: true);
        using (output.BeginExclusiveFrameScope())
        {
            events.Add("body");
        }

        Assert.Equal(["begin", "body", "end"], events);
    }

    [Fact]
    public void CreateOutput_StyledRows_PublishOverlaySnapshotsAndPersistFinalRowsOnDispose()
    {
        var snapshots = new List<string[]>();
        var retainedLines = new List<string>();
        var writer = new ModelStatusConsoleWriter(
            new object(),
            new ModelStatusTableRenderer(static () => 120, static () => true),
            _ => { },
            retainedLines.Add,
            _ => TerminalConsoleRefreshScope.Noop(),
            lines => snapshots.Add(lines.ToArray()));

        var output = writer.CreateOutput(styled: true);
        var text = CaptureConsole(() =>
        {
            using (output.BeginExclusiveFrameScope())
            {
                output.WriteNoWrapLine("header");
                output.WriteLiveRows(["row-1", "row-2"], false);
                output.WriteLiveRows(["row-1 done", "row-2 done"], true);
                output.WriteNoWrapLine("summary");
            }
        });

        Assert.Equal(string.Empty, text);
        Assert.Contains(snapshots, snapshot => snapshot.Any(line => line.Contains("header", StringComparison.Ordinal)));
        Assert.Contains(snapshots, snapshot =>
            snapshot.Any(line => line.Contains("row-1 done", StringComparison.Ordinal))
            && snapshot.Any(line => line.Contains("row-2 done", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            snapshots.Last(snapshot => snapshot.Length > 0),
            line => line.TrimEnd().Equals("row-1", StringComparison.Ordinal));
        Assert.Contains(snapshots, snapshot => snapshot.Any(line => line.Contains("summary", StringComparison.Ordinal)));
        Assert.Empty(snapshots[^1]);
        var retained = Assert.Single(retainedLines);
        Assert.Contains("header", retained, StringComparison.Ordinal);
        Assert.Contains("row-1 done", retained, StringComparison.Ordinal);
        Assert.Contains("row-2 done", retained, StringComparison.Ordinal);
        Assert.Contains("summary", retained, StringComparison.Ordinal);
        Assert.DoesNotContain(
            retained.Split(Environment.NewLine, StringSplitOptions.None),
            line => line.TrimEnd().Equals("row-1", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateOutput_StyledRows_EscapeCallbackCancelsOverlayToken()
    {
        Action? escape = null;
        var writer = new ModelStatusConsoleWriter(
            new object(),
            new ModelStatusTableRenderer(static () => 120, static () => true),
            _ => { },
            _ => { },
            onEscape =>
            {
                escape = onEscape;
                return new DisposeAction(() => { });
            },
            _ => { });

        var output = writer.CreateOutput(styled: true, CancellationToken.None);
        using (output.BeginExclusiveFrameScope())
        {
            Assert.NotNull(escape);
            escape!();
        }

        Assert.True(output.IsUserCancellationRequested());
        Assert.True(output.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void WriteLiveBatchRows_WhenPlain_WritesRowsAndStopsWaitingPlaceholder()
    {
        var stopCount = 0;
        var writer = new ModelStatusConsoleWriter(
            new object(),
            new ModelStatusTableRenderer(static () => 120, static () => true),
            _ => stopCount++,
            _ => { });

        var output = CaptureConsole(() => writer.WriteLiveBatchRows(["row-1", "row-2"], rewriteExistingRows: false, styled: false));

        Assert.Equal(1, stopCount);
        Assert.Contains("\r\u001b[2Krow-1", output, StringComparison.Ordinal);
        Assert.Contains("\r\u001b[2Krow-2", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteLiveBatchRows_WhenPlainRewriting_MovesCursorUpByRowCount()
    {
        var writer = new ModelStatusConsoleWriter(
            new object(),
            new ModelStatusTableRenderer(static () => 120, static () => true),
            _ => { },
            _ => { });

        var output = CaptureConsole(() => writer.WriteLiveBatchRows(["row-1", "row-2"], rewriteExistingRows: true, styled: false));

        Assert.StartsWith("\u001b[2F", output, StringComparison.Ordinal);
    }

    private static string CaptureConsole(Action action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private sealed class DisposeAction(Action dispose) : IDisposable
    {
        public void Dispose()
            => dispose();
    }
}
