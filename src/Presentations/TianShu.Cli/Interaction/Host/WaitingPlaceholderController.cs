using TianShu.Cli.Terminal;

namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Owns the transient "Working..." placeholder shown before the first streamed event.
/// 持有首个流事件到达前的临时 Working 占位行状态与计时器。
/// </summary>
internal sealed class WaitingPlaceholderController : IDisposable
{
    private static readonly string[] Frames = ["Working", "Working.", "Working..", "Working..."];

    private readonly object syncRoot;
    private readonly Func<bool> isHumanOutput;
    private Timer? timer;
    private bool visible;
    private int frame;
    private long startedUtcTicks;

    public WaitingPlaceholderController(
        object syncRoot,
        Func<bool> isHumanOutput)
    {
        this.syncRoot = syncRoot ?? throw new ArgumentNullException(nameof(syncRoot));
        this.isHumanOutput = isHumanOutput ?? throw new ArgumentNullException(nameof(isHumanOutput));
    }

    public void Start()
    {
        if (!isHumanOutput())
        {
            return;
        }

        lock (syncRoot)
        {
            frame = 0;
            startedUtcTicks = DateTime.UtcNow.Ticks;
            visible = true;
            RenderUnsafe();
            timer?.Dispose();
            timer = new Timer(
                _ => Advance(),
                null,
                TimeSpan.FromMilliseconds(280),
                TimeSpan.FromMilliseconds(280));
        }
    }

    public void Stop(bool clearLine)
    {
        if (!isHumanOutput())
        {
            return;
        }

        lock (syncRoot)
        {
            StopUnsafe(clearLine);
        }
    }

    public void StopUnsafe(bool clearLine)
    {
        timer?.Dispose();
        timer = null;
        if (!visible)
        {
            return;
        }

        visible = false;

        if (clearLine)
        {
            Console.Write("\r\u001b[2K");
        }
    }

    public void Dispose()
        => timer?.Dispose();

    private void Advance()
    {
        lock (syncRoot)
        {
            if (!visible || !isHumanOutput())
            {
                return;
            }

            frame++;
            RenderUnsafe();
        }
    }

    private void RenderUnsafe()
    {
        var text = Frames[frame % Frames.Length];
        var elapsedSeconds = Math.Max(0, (int)Math.Round((DateTime.UtcNow - new DateTime(startedUtcTicks, DateTimeKind.Utc)).TotalSeconds));
        Console.Write("\r\u001b[2K");
        Console.Write(TerminalAnsi.DimText($"• {text} ({elapsedSeconds}s)"));
    }
}
