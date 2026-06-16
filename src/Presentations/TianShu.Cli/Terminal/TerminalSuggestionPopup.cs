using TianShu.Cli.Interaction.Commands;

namespace TianShu.Cli.Terminal;

/// <summary>
/// Builds lightweight command and file suggestions for the TianShu terminal composer.
/// 为 TianShu 终端输入状态机构建轻量命令与文件候选。
/// </summary>
internal sealed class TerminalSuggestionPopup
{
    private const int MaxVisibleItems = 6;
    private const int MaxCandidateItems = 50;
    private const int MaxCachedFiles = 5000;

    private static readonly TerminalSlashCommand[] SlashCommands = BuildSlashCommands(SlashCommandRegistry.Default);

    private readonly string workingDirectory;
    private IReadOnlyList<string>? fileCache;

    public TerminalSuggestionPopup(string? workingDirectory)
    {
        this.workingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);
    }

    public TerminalSuggestionPopupFrame Build(string text, int cursor, int selectedIndex)
    {
        var normalizedText = text ?? string.Empty;
        var normalizedCursor = Math.Clamp(cursor, 0, normalizedText.Length);
        if (TryBuildSlashSuggestions(normalizedText, normalizedCursor, selectedIndex, out var slashFrame))
        {
            return slashFrame;
        }

        if (TryBuildFileSuggestions(normalizedText, normalizedCursor, selectedIndex, out var fileFrame))
        {
            return fileFrame;
        }

        return TerminalSuggestionPopupFrame.Empty;
    }

    private static bool TryBuildSlashSuggestions(
        string text,
        int cursor,
        int selectedIndex,
        out TerminalSuggestionPopupFrame frame)
    {
        frame = TerminalSuggestionPopupFrame.Empty;
        if (!TryGetSlashQuery(text, cursor, out var query, out var replaceLength))
        {
            return false;
        }

        var queryContainsSpace = query.Contains(' ', StringComparison.Ordinal);
        var items = SlashCommands
            .Where(command => queryContainsSpace || !command.Name.Contains(' ', StringComparison.Ordinal))
            .Where(command => command.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                              || command.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(command => command.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxCandidateItems)
            .Select(command => new TerminalSuggestionItem(
                "/" + command.Name.PadRight(18) + command.Description,
                "/" + command.Name + " ",
                0,
                replaceLength))
            .ToArray();

        frame = TerminalSuggestionPopupFrame.Create(items, selectedIndex);
        return frame.HasItems;
    }

    private static TerminalSlashCommand[] BuildSlashCommands(SlashCommandRegistry registry)
        => registry.Descriptors
            .Where(static descriptor => descriptor.VisibleInHelp)
            .SelectMany(CreateSlashCommands)
            .GroupBy(static command => command.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

    private static IEnumerable<TerminalSlashCommand> CreateSlashCommands(SlashCommandDescriptor descriptor)
    {
        foreach (var name in descriptor.AllNames())
        {
            yield return new TerminalSlashCommand(name, descriptor.Description);

            foreach (var subcommand in descriptor.Subcommands)
            {
                yield return new TerminalSlashCommand($"{name} {subcommand}", descriptor.Description);
            }
        }
    }

    private static bool TryGetSlashQuery(string text, int cursor, out string query, out int replaceLength)
    {
        query = string.Empty;
        replaceLength = 0;
        if (cursor < 1 || cursor > text.Length || text[0] != '/')
        {
            return false;
        }

        var beforeCursor = text[..cursor];
        if (beforeCursor.Contains(Environment.NewLine, StringComparison.Ordinal))
        {
            return false;
        }

        query = beforeCursor[1..];
        replaceLength = cursor;
        return !query.Contains('\t', StringComparison.Ordinal);
    }

    private bool TryBuildFileSuggestions(
        string text,
        int cursor,
        int selectedIndex,
        out TerminalSuggestionPopupFrame frame)
    {
        frame = TerminalSuggestionPopupFrame.Empty;
        if (!TryGetCurrentToken(text, cursor, out var token)
            || !token.Text.StartsWith("@", StringComparison.Ordinal)
            || token.Text.Length < 2)
        {
            return false;
        }

        var query = NormalizeFileQuery(token.Text[1..]);
        if (query.Length == 0)
        {
            return false;
        }

        var files = GetFileCache();
        var items = files
            .Select(path => new { Path = path, Score = ScoreFileCandidate(path, query) })
            .Where(candidate => candidate.Score < int.MaxValue)
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxCandidateItems)
            .Select(candidate => new TerminalSuggestionItem(
                "@ " + candidate.Path,
                candidate.Path,
                token.Start,
                token.Length))
            .ToArray();

        frame = TerminalSuggestionPopupFrame.Create(items, selectedIndex);
        return frame.HasItems;
    }

    private IReadOnlyList<string> GetFileCache()
        => fileCache ??= BuildFileCache(workingDirectory);

    private static IReadOnlyList<string> BuildFileCache(string root)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0 && files.Count < MaxCachedFiles)
        {
            var current = pending.Pop();
            IEnumerable<string> directories;
            IEnumerable<string> currentFiles;
            try
            {
                directories = Directory.EnumerateDirectories(current);
                currentFiles = Directory.EnumerateFiles(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                if (!ShouldSkipDirectory(directory))
                {
                    pending.Push(directory);
                }
            }

            foreach (var file in currentFiles)
            {
                files.Add(ToRelativePath(root, file));
                if (files.Count >= MaxCachedFiles)
                {
                    break;
                }
            }
        }

        return files;
    }

    private static bool ShouldSkipDirectory(string directory)
    {
        var name = Path.GetFileName(directory);
        return name is ".git" or ".vs" or "bin" or "obj" or "node_modules";
    }

    private static int ScoreFileCandidate(string path, string query)
    {
        if (path.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var fileName = Path.GetFileName(path);
        if (fileName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (path.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return int.MaxValue;
    }

    private static string NormalizeFileQuery(string query)
        => query.Replace('\\', '/').Trim();

    private static string ToRelativePath(string root, string file)
        => Path.GetRelativePath(root, file).Replace('\\', '/');

    private static bool TryGetCurrentToken(string text, int cursor, out TerminalToken token)
    {
        token = default;
        if (text.Length == 0 || cursor < 0 || cursor > text.Length)
        {
            return false;
        }

        var start = cursor;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }

        var end = cursor;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        if (start == end)
        {
            return false;
        }

        token = new TerminalToken(start, end - start, text[start..end]);
        return true;
    }

    private sealed record TerminalSlashCommand(string Name, string Description);

    private readonly record struct TerminalToken(int Start, int Length, string Text);
}

internal readonly record struct TerminalSuggestionItem(
    string DisplayText,
    string InsertText,
    int ReplaceStart,
    int ReplaceLength);

internal sealed class TerminalSuggestionPopupFrame
{
    private const int MaxVisibleItems = 6;

    private TerminalSuggestionPopupFrame(IReadOnlyList<TerminalSuggestionItem> items, int selectedIndex)
    {
        Items = items;
        SelectedIndex = items.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, items.Count - 1);
    }

    public static TerminalSuggestionPopupFrame Empty { get; } = new(Array.Empty<TerminalSuggestionItem>(), -1);

    public IReadOnlyList<TerminalSuggestionItem> Items { get; }

    public int SelectedIndex { get; }

    public bool HasItems => Items.Count > 0;

    public TerminalSuggestionItem? SelectedItem
        => HasItems ? Items[SelectedIndex] : null;

    public IReadOnlyList<string> RenderLines
    {
        get
        {
            if (!HasItems)
            {
                return Array.Empty<string>();
            }

            var startIndex = ResolveVisibleStartIndex();
            var endIndex = Math.Min(Items.Count, startIndex + MaxVisibleItems);
            return Enumerable.Range(startIndex, endIndex - startIndex)
                .Select(index => (index == SelectedIndex ? "> " : "  ") + Items[index].DisplayText)
                .ToArray();
        }
    }

    public static TerminalSuggestionPopupFrame Create(
        IReadOnlyList<TerminalSuggestionItem> items,
        int selectedIndex)
        => items.Count == 0 ? Empty : new TerminalSuggestionPopupFrame(items, selectedIndex);

    public TerminalSuggestionPopupFrame MoveSelection(int delta)
    {
        if (!HasItems)
        {
            return this;
        }

        var nextIndex = (SelectedIndex + delta + Items.Count) % Items.Count;
        return new TerminalSuggestionPopupFrame(Items, nextIndex);
    }

    private int ResolveVisibleStartIndex()
    {
        if (Items.Count <= MaxVisibleItems)
        {
            return 0;
        }

        var centered = SelectedIndex - (MaxVisibleItems / 2);
        return Math.Clamp(centered, 0, Items.Count - MaxVisibleItems);
    }
}
