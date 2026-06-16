using TianShu.Cli.Terminal;

namespace TianShu.Cli.Tests;

public sealed class TerminalSelectionPickerTests
{
    [Fact]
    public async Task SelectAsync_WhenCancelled_UsesExclusiveFrameScope()
    {
        var events = new List<string>();
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        var result = await new TerminalSelectionPicker(() =>
            {
                events.Add("begin");
                return new DisposeAction(() => events.Add("end"));
            })
            .SelectAsync(["first"], "选择线程", cancellationTokenSource.Token);

        Assert.Null(result);
        Assert.Equal(["begin", "end"], events);
    }

    [Fact]
    public void BuildFrame_MarksSelectedRowAndKeepsKeyboardHint()
    {
        var frame = TerminalSelectionPicker.BuildFrame(["first", "second"], "选择线程", selectedIndex: 1);

        Assert.Contains("选择线程", frame, StringComparison.Ordinal);
        Assert.Contains("↑/↓ 选择  Enter 确认  Esc 取消", frame, StringComparison.Ordinal);
        Assert.Contains("  first", frame, StringComparison.Ordinal);
        Assert.Contains("> second", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFrame_WhenRowsExceedVisibleWindow_KeepsSelectedRowVisible()
    {
        var rows = Enumerable.Range(0, 20)
            .Select(index => $"model-{index:00}")
            .ToArray();

        var frame = TerminalSelectionPicker.BuildFrame(rows, "选择模型", selectedIndex: 12, visibleRowCount: 5);

        Assert.Contains("选择模型  13/20", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("model-00", frame, StringComparison.Ordinal);
        Assert.Contains("  model-10", frame, StringComparison.Ordinal);
        Assert.Contains("> model-12", frame, StringComparison.Ordinal);
        Assert.Contains("  model-14", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFrame_WhenSelectedRowIsLast_RendersBottomWindow()
    {
        var rows = Enumerable.Range(0, 20)
            .Select(index => $"model-{index:00}")
            .ToArray();

        var frame = TerminalSelectionPicker.BuildFrame(rows, "选择模型", selectedIndex: 19, visibleRowCount: 5);

        Assert.DoesNotContain("model-14", frame, StringComparison.Ordinal);
        Assert.Contains("  model-15", frame, StringComparison.Ordinal);
        Assert.Contains("> model-19", frame, StringComparison.Ordinal);
    }

    private sealed class DisposeAction(Action dispose) : IDisposable
    {
        public void Dispose()
            => dispose();
    }
}
