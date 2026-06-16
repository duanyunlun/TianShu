using System.Diagnostics;
using TianShu.Execution.Runtime;

namespace TianShu.Cli;

internal sealed class McpServerCommandRunner
{
    public async Task<int> RunAsync(McpServerCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var launchSpec = BuildLaunchSpec(options);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = launchSpec.ExecutablePath,
                WorkingDirectory = launchSpec.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            },
        };

        foreach (var argument in launchSpec.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("启动 TianShu mcp-server 失败。");
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            TryTerminateProcess(process);
            return 130;
        }
    }

    internal static AppServerLaunchSpec BuildLaunchSpec(McpServerCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Directory.Exists(options.WorkingDirectory))
        {
            throw new DirectoryNotFoundException($"工作目录不存在：{options.WorkingDirectory}");
        }

        var appHostResolution = CliAppHostLaunchResolver.Resolve(options.AppHostProjectPath, options.WorkingDirectory);
        if (!appHostResolution.IsResolved)
        {
            throw new FileNotFoundException("未找到 TianShu 宿主启动入口。", options.AppHostProjectPath);
        }

        var appHostProjectPath = appHostResolution.AppHostProjectPath;
        var executablePath = string.IsNullOrWhiteSpace(appHostProjectPath)
            ? appHostResolution.AppHostExecutablePath
            : RuntimeHostLaunchLocator.ResolveBuiltExecutablePath(appHostProjectPath);
        var arguments = BuildArguments(options, appHostProjectPath, string.IsNullOrWhiteSpace(executablePath));
        return new AppServerLaunchSpec(
            string.IsNullOrWhiteSpace(executablePath) ? "dotnet" : executablePath!,
            arguments,
            options.WorkingDirectory);
    }

    internal static IReadOnlyList<string> BuildArguments(
        McpServerCommandOptions options,
        string? appHostProjectPath,
        bool useDotNetProjectLauncher)
    {
        var arguments = new List<string>();
        if (useDotNetProjectLauncher)
        {
            if (string.IsNullOrWhiteSpace(appHostProjectPath))
            {
                throw new InvalidOperationException("未找到 TianShu 宿主项目文件。");
            }

            arguments.Add("run");
            arguments.Add("--project");
            arguments.Add(appHostProjectPath);
            arguments.Add("--");
        }

        arguments.Add("mcp-server");

        if (!string.IsNullOrWhiteSpace(options.ConfigFilePath))
        {
            arguments.Add("--config-file");
            arguments.Add(options.ConfigFilePath);
        }

        foreach (var pair in options.ConfigOverrides)
        {
            arguments.Add("-c");
            arguments.Add($"{pair.Key}={pair.Value}");
        }

        return arguments;
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }
}
