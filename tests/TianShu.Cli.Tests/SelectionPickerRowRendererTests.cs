using TianShu.Cli.Interaction.Rendering;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Tests;

public sealed class SelectionPickerRowRendererTests
{
    [Fact]
    public void BuildThreadListRow_HidesThreadIdWhenNameIsMissing()
    {
        var thread = new ControlPlaneThreadSummary
        {
            ThreadId = new ThreadId("thread_hidden_001"),
            UpdatedAt = new DateTimeOffset(2026, 5, 12, 8, 30, 15, TimeSpan.Zero),
        };

        var row = SelectionPickerRowRenderer.BuildThreadListRow(thread, includeCwd: false);

        Assert.Contains("未命名线程", row, StringComparison.Ordinal);
        Assert.DoesNotContain("thread_hidden_001", row, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildThreadListRow_WhenRequested_IncludesWorkingDirectory()
    {
        var thread = new ControlPlaneThreadSummary
        {
            ThreadId = new ThreadId("thread-a"),
            Name = "实现 CLI",
            WorkingDirectory = @"D:\Work\TianShu",
            UpdatedAt = new DateTimeOffset(2026, 5, 12, 8, 30, 15, TimeSpan.Zero),
        };

        var row = SelectionPickerRowRenderer.BuildThreadListRow(thread, includeCwd: true);

        Assert.Contains("实现 CLI", row, StringComparison.Ordinal);
        Assert.Contains(@"D:\Work\TianShu", row, StringComparison.Ordinal);
        Assert.Contains("  ", row, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildStartupThreadPickerRow_UsesTabSeparatedColumns()
    {
        var thread = new ControlPlaneThreadSummary
        {
            ThreadId = new ThreadId("thread-a"),
            Name = "恢复上下文",
            WorkingDirectory = @"D:\Work\TianShu",
            UpdatedAt = new DateTimeOffset(2026, 5, 12, 8, 30, 15, TimeSpan.Zero),
        };

        var row = SelectionPickerRowRenderer.BuildStartupThreadPickerRow(thread, includeCwd: true);

        Assert.Contains("\t恢复上下文\t", row, StringComparison.Ordinal);
        Assert.EndsWith(@"D:\Work\TianShu", row, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildModelSelectionRow_WhenDisplayNameDiffers_AppendsDisplayName()
    {
        var row = SelectionPickerRowRenderer.BuildModelSelectionRow(new ControlPlaneModelCatalogItem
        {
            Model = "gpt-5.4",
            DisplayName = "GPT 5.4",
            IsDefault = true,
        });

        Assert.Equal("gpt-5.4 - GPT 5.4  default", row);
    }
}
