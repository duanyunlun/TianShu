using System.Reflection;
using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;

namespace TianShu.Cli.Tests;

public sealed class TianShuCliProgramTests
{
    private static readonly Assembly CliAssembly = ReflectionTestHelper.LoadRequiredAssembly("TianShu.Cli");

    [Fact]
    public async Task Main_WhenHelpRequested_ReturnsZero_And_PrintsHelp()
    {
        var (exitCode, stdout, stderr) = await InvokeMainAsync("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("天枢 TianShu CLI", stdout, StringComparison.Ordinal);
        Assert.Contains("用法：", stdout, StringComparison.Ordinal);
        Assert.Contains("tianshu [选项] [prompt]", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("兼容别名", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("codex", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tianshu completion", stdout, StringComparison.Ordinal);
        Assert.Contains("memory      记忆能力", stdout, StringComparison.Ordinal);
        Assert.Contains("tianshu memory consolidate", stdout, StringComparison.Ordinal);
        Assert.Contains("memory review [list|approve|demote|merge|restore]", stdout, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
    }

    [Fact]
    public async Task Main_WhenArgumentsInvalid_ReturnsInvalidArguments_And_PrintsHelpToStderr()
    {
        var (exitCode, stdout, stderr) = await InvokeMainAsync("thread", "prune", "--thread-id", "thread-1");

        Assert.Equal(2, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stdout));
        Assert.Contains("不支持的 thread 子命令", stderr, StringComparison.Ordinal);
        Assert.Contains("天枢 TianShu CLI", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Main_WhenBootstrapperThrowsDirectoryNotFound_ReturnsOne()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), $"tianshu-missing-{Guid.NewGuid():N}");

        var (exitCode, stdout, stderr) = await InvokeMainAsync(
            "thread",
            "read",
            "--thread-id",
            "thread-main-missing",
            "--cwd",
            missingDirectory);

        Assert.Equal(1, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stdout));
        Assert.Contains("工作目录不存在", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TryWriteStructuredFailure_WhenCommandRequestsJson_WritesErrorEnvelopeToStdout()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var writerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandFailureWriter");
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "start", "--json" });
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);

        var exception = new AppServerRpcException(
            -32600,
            "failed to load configuration: boom",
            StructuredJson("""{"reason":"cloudRequirements","detail":"boom"}"""));

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var written = ReflectionTestHelper.InvokeStaticMethod(writerType, "TryWriteStructuredFailure", command, exception);
            Assert.True(Assert.IsType<bool>(written));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }

        Assert.True(string.IsNullOrWhiteSpace(stderr.ToString()));
        using var document = JsonDocument.Parse(stdout.ToString());
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(-32600, error.GetProperty("code").GetInt32());
        Assert.Equal("failed to load configuration: boom", error.GetProperty("message").GetString());
        Assert.Equal("cloudRequirements", error.GetProperty("data").GetProperty("reason").GetString());
    }

    [Fact]
    public void TryWriteStructuredFailure_WhenCommandIsHumanReadable_WritesDataToStderr()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var writerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandFailureWriter");
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "start" });
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);

        var exception = new AppServerRpcException(
            -32600,
            "failed to load configuration: boom",
            StructuredJson("""{"reason":"cloudRequirements","detail":"boom"}"""));

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var written = ReflectionTestHelper.InvokeStaticMethod(writerType, "TryWriteStructuredFailure", command, exception);
            Assert.True(Assert.IsType<bool>(written));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }

        Assert.True(string.IsNullOrWhiteSpace(stdout.ToString()));
        Assert.Contains("app-server 返回错误：failed to load configuration: boom", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"reason\": \"cloudRequirements\"", stderr.ToString(), StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> InvokeMainAsync(params string[] args)
    {
        var programType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.Program");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var task = ReflectionTestHelper.InvokeStaticMethod(programType, "Main", (object)args)!;
            var result = await ReflectionTestHelper.AwaitTaskResultAsync(task).ConfigureAwait(false);
            return (Assert.IsType<int>(result), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    private static StructuredValue StructuredJson(string json)
        => StructuredValue.FromJsonElement(ReflectionTestHelper.ParseJsonElement(json));
}
