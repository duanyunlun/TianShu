namespace TianShu.Cli.Terminal;

/// <summary>
/// Reads normalized terminal keys from the process console.
/// 从进程控制台读取归一化终端按键。
/// </summary>
internal sealed class ConsoleTerminalInput
{
    public static ConsoleTerminalInput Shared { get; } = new();

    private bool bufferedInputBurstActive;

    public async Task<TerminalInputKey?> ReadKeyAsync(CancellationToken cancellationToken)
    {
        var keyInfo = await Task.Run(
                static () =>
                {
                    var previousTreatControlCAsInput = Console.TreatControlCAsInput;
                    try
                    {
                        Console.TreatControlCAsInput = true;
                        return Console.ReadKey(intercept: true);
                    }
                    finally
                    {
                        Console.TreatControlCAsInput = previousTreatControlCAsInput;
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
        var hasBufferedInputAfterKey = SafeHasBufferedInput();
        var key = TerminalKeyMapper.FromConsoleKeyInfo(
            keyInfo,
            treatPlainEnterAsNewLine: hasBufferedInputAfterKey || bufferedInputBurstActive);
        bufferedInputBurstActive = hasBufferedInputAfterKey;
        return key;
    }

    private static bool SafeHasBufferedInput()
    {
        try
        {
            return Console.KeyAvailable;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
