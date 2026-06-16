using System.Text;

namespace TianShu.Cli.Interaction.Projection;

internal sealed class AssistantTextAccumulator
{
    private readonly StringBuilder buffer = new();

    public bool HasText => buffer.Length > 0;

    public string Text => buffer.ToString();

    public void Append(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            buffer.Append(text);
        }
    }

    public AssistantMessageBlock? Commit(bool isComplete)
    {
        if (buffer.Length == 0)
        {
            return null;
        }

        var block = new AssistantMessageBlock(buffer.ToString(), isComplete);
        buffer.Clear();
        return block;
    }

    public void Clear() => buffer.Clear();
}
