using System.Text;

namespace TianShu.VSSDK.Sidecar;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!args.Any(static arg => string.Equals(arg, "--stdio", StringComparison.OrdinalIgnoreCase)))
        {
            await Console.Error.WriteLineAsync("缺少 --stdio 参数。").ConfigureAwait(false);
            return 2;
        }

        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            await using var host = new StdioSidecarHost(Console.In, Console.Out, Console.Error);
            return await host.RunAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[TianShu.VSSDK.Sidecar] 未处理异常：{ex}").ConfigureAwait(false);
            return 1;
        }
    }
}
