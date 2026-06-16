namespace TianShu.Cli;

internal sealed class ChatScriptCommandFile
{
    private ChatScriptCommandFile(string path, IReadOnlyList<string> commands)
    {
        Path = path;
        Commands = commands;
    }

    public string Path { get; }

    public IReadOnlyList<string> Commands { get; }

    public static ChatScriptCommandFile? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"chat 脚本不存在：{fullPath}", fullPath);
        }

        var commands = File
            .ReadLines(fullPath)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Where(static line => !line.StartsWith("#", StringComparison.Ordinal))
            .Where(static line => !line.StartsWith("//", StringComparison.Ordinal))
            .ToArray();

        return new ChatScriptCommandFile(fullPath, commands);
    }
}
