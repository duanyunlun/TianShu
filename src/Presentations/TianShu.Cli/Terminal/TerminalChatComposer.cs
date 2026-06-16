using System.Text;

namespace TianShu.Cli.Terminal;

/// <summary>
/// Maintains TianShu-style terminal input state before it is submitted to the existing chat runner.
/// 维护 TianShu 式终端输入状态；提交后的动作仍交给既有 chat runner 处理。
/// </summary>
internal sealed class TerminalChatComposer
{
    private readonly List<string> history = [];
    private readonly StringBuilder buffer = new();
    private int cursor;
    private int? historyIndex;

    public TerminalChatComposer(IEnumerable<string>? initialHistory = null)
    {
        if (initialHistory is null)
        {
            return;
        }

        foreach (var entry in initialHistory)
        {
            AddHistory(entry.Trim());
        }
    }

    public string Text => buffer.ToString();

    public int Cursor => cursor;

    public int HistoryCount => history.Count;

    public bool IsEmpty => buffer.Length == 0;

    public TerminalComposerAction HandleKey(TerminalInputKey key)
        => key.Kind switch
        {
            TerminalKeyKind.Character => InsertCharacter(key.Character),
            TerminalKeyKind.Enter => HandleEnter(key.Modifiers),
            TerminalKeyKind.Backspace => Backspace(),
            TerminalKeyKind.Delete => Delete(),
            TerminalKeyKind.LeftArrow => MoveCursor(-1),
            TerminalKeyKind.RightArrow => MoveCursor(1),
            TerminalKeyKind.UpArrow => RecallPreviousHistory(),
            TerminalKeyKind.DownArrow => RecallNextHistory(),
            TerminalKeyKind.Home => MoveCursorTo(0),
            TerminalKeyKind.End => MoveCursorTo(buffer.Length),
            TerminalKeyKind.Escape => Clear(),
            TerminalKeyKind.ControlC => InterruptOrExit(),
            _ => TerminalComposerAction.None,
        };

    public void SetText(string text)
    {
        buffer.Clear();
        buffer.Append(text);
        cursor = buffer.Length;
        historyIndex = null;
    }

    public void ReplaceHistory(IEnumerable<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        history.Clear();
        foreach (var entry in entries)
        {
            AddHistory(entry.Trim());
        }

        historyIndex = null;
    }

    public void ReplaceRange(int start, int length, string replacement)
    {
        if (start < 0 || start > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (length < 0 || start + length > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        ArgumentNullException.ThrowIfNull(replacement);
        buffer.Remove(start, length);
        buffer.Insert(start, replacement);
        cursor = start + replacement.Length;
        historyIndex = null;
    }

    private TerminalComposerAction InsertCharacter(char? character)
    {
        if (character is null || char.IsControl(character.Value))
        {
            return TerminalComposerAction.None;
        }

        buffer.Insert(cursor, character.Value);
        cursor++;
        historyIndex = null;
        return TerminalComposerAction.None;
    }

    private TerminalComposerAction HandleEnter(TerminalKeyModifiers modifiers)
    {
        var control = (modifiers & TerminalKeyModifiers.Control) == TerminalKeyModifiers.Control;
        var shift = (modifiers & TerminalKeyModifiers.Shift) == TerminalKeyModifiers.Shift;
        if (control && shift)
        {
            return Submit(TerminalSubmitIntent.Steer);
        }

        if (!control && !shift && IsSlashCommandDraft())
        {
            return Submit(TerminalSubmitIntent.Standard);
        }

        if (shift)
        {
            return InsertNewLine();
        }

        return Submit(control ? TerminalSubmitIntent.Queue : TerminalSubmitIntent.Standard);
    }

    private bool IsSlashCommandDraft()
    {
        for (var index = 0; index < buffer.Length; index++)
        {
            if (char.IsWhiteSpace(buffer[index]))
            {
                continue;
            }

            return buffer[index] == '/';
        }

        return false;
    }

    private TerminalComposerAction InsertNewLine()
    {
        buffer.Insert(cursor, Environment.NewLine);
        cursor += Environment.NewLine.Length;
        historyIndex = null;
        return TerminalComposerAction.None;
    }

    private TerminalComposerAction Submit(TerminalSubmitIntent intent)
    {
        var submitted = Text.Trim();
        if (submitted.Length == 0)
        {
            return TerminalComposerAction.None;
        }

        AddHistory(submitted);
        buffer.Clear();
        cursor = 0;
        historyIndex = null;
        return TerminalComposerAction.Submit(submitted, intent);
    }

    private TerminalComposerAction Backspace()
    {
        if (cursor <= 0)
        {
            return TerminalComposerAction.None;
        }

        buffer.Remove(cursor - 1, 1);
        cursor--;
        historyIndex = null;
        return TerminalComposerAction.None;
    }

    private TerminalComposerAction Delete()
    {
        if (cursor >= buffer.Length)
        {
            return TerminalComposerAction.None;
        }

        buffer.Remove(cursor, 1);
        historyIndex = null;
        return TerminalComposerAction.None;
    }

    private TerminalComposerAction MoveCursor(int delta)
    {
        cursor = Math.Clamp(cursor + delta, 0, buffer.Length);
        return TerminalComposerAction.None;
    }

    private TerminalComposerAction MoveCursorTo(int position)
    {
        cursor = Math.Clamp(position, 0, buffer.Length);
        return TerminalComposerAction.None;
    }

    private TerminalComposerAction RecallPreviousHistory()
    {
        if (history.Count == 0)
        {
            return TerminalComposerAction.None;
        }

        if (historyIndex is null && buffer.Length > 0)
        {
            return TerminalComposerAction.None;
        }

        historyIndex = historyIndex is null
            ? 0
            : Math.Min(history.Count - 1, historyIndex.Value + 1);
        SetHistoryText(history[historyIndex.Value]);
        return TerminalComposerAction.None;
    }

    private TerminalComposerAction RecallNextHistory()
    {
        if (history.Count == 0 || historyIndex is null)
        {
            return TerminalComposerAction.None;
        }

        if (historyIndex.Value >= history.Count - 1)
        {
            buffer.Clear();
            cursor = 0;
            historyIndex = null;
            return TerminalComposerAction.None;
        }

        historyIndex++;
        SetHistoryText(history[historyIndex.Value]);
        return TerminalComposerAction.None;
    }

    private TerminalComposerAction Clear()
    {
        buffer.Clear();
        cursor = 0;
        historyIndex = null;
        return TerminalComposerAction.None;
    }

    private TerminalComposerAction InterruptOrExit()
    {
        if (buffer.Length > 0)
        {
            return Clear();
        }

        return new TerminalComposerAction(TerminalComposerActionKind.Exit, null);
    }

    private void SetHistoryText(string text)
    {
        buffer.Clear();
        buffer.Append(text);
        cursor = buffer.Length;
    }

    private void AddHistory(string text)
    {
        if (history.Count == 0 || !string.Equals(history[^1], text, StringComparison.Ordinal))
        {
            history.Add(text);
        }
    }
}
