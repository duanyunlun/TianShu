using TianShu.Cli.Terminal;

namespace TianShu.Cli.Tests;

public sealed class TerminalInputHistoryStoreTests
{
    [Fact]
    public void AppendAndLoad_WhenThreadScoped_ReturnsOnlyRequestedThreadHistory()
    {
        using var tempDirectory = new TestTempDirectory();
        var store = new TerminalInputHistoryStore(tempDirectory.Path);

        store.Append("thread-a", " first ", TerminalSubmitIntent.Queue);
        store.Append("thread-b", "second", TerminalSubmitIntent.Steer);
        store.Append("thread-a", "third", TerminalSubmitIntent.Standard);

        Assert.Equal(["first", "third"], store.Load("thread-a"));
        Assert.Equal(["second"], store.Load("thread-b"));
    }

    [Fact]
    public void ClearThread_WhenThreadSpecified_RemovesOnlyThatThreadHistory()
    {
        using var tempDirectory = new TestTempDirectory();
        var store = new TerminalInputHistoryStore(tempDirectory.Path);

        store.Append("thread-a", "first", TerminalSubmitIntent.Queue);
        store.Append("thread-b", "second", TerminalSubmitIntent.Queue);

        store.ClearThread("thread-a");

        Assert.Empty(store.Load("thread-a"));
        Assert.Equal(["second"], store.Load("thread-b"));
    }

    [Fact]
    public void ClearAll_WhenHistoryExists_RemovesEveryThreadHistory()
    {
        using var tempDirectory = new TestTempDirectory();
        var store = new TerminalInputHistoryStore(tempDirectory.Path);

        store.Append("thread-a", "first", TerminalSubmitIntent.Queue);
        store.Append("thread-b", "second", TerminalSubmitIntent.Queue);

        store.ClearAll();

        Assert.Empty(store.Load("thread-a"));
        Assert.Empty(store.Load("thread-b"));
    }

    [Fact]
    public void Append_WhenThreadIdContainsPathCharacters_StoresInsideHistoryDirectory()
    {
        using var tempDirectory = new TestTempDirectory();
        var store = new TerminalInputHistoryStore(tempDirectory.Path);

        store.Append("thread:with\\path/slash", "first", TerminalSubmitIntent.Queue);

        var file = Assert.Single(Directory.EnumerateFiles(tempDirectory.Path, "*.jsonl"));
        Assert.Equal(tempDirectory.Path, Path.GetDirectoryName(file));
        Assert.Equal(["first"], store.Load("thread:with\\path/slash"));
    }
}
