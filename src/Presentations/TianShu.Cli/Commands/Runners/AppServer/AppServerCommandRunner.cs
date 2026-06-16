using System.Diagnostics;
using TianShu.Execution.Runtime;

namespace TianShu.Cli;

internal sealed class AppServerCommandRunner
{
    public async Task<int> RunAsync(AppServerCommandOptions options, CancellationToken cancellationToken)
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
            throw new InvalidOperationException("启动 TianShu app-server 失败。");
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

    internal static AppServerLaunchSpec BuildLaunchSpec(AppServerCommandOptions options)
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
        AppServerCommandOptions options,
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

        arguments.Add("app-server");
        switch (options.CommandKind)
        {
            case AppServerCommandKind.RunServer:
                arguments.Add("--listen");
                arguments.Add(options.ListenUrl);
                if (options.AnalyticsDefaultEnabled)
                {
                    arguments.Add("--analytics-default-enabled");
                }

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

                break;
            case AppServerCommandKind.GenerateTs:
                arguments.Add("generate-ts");
                if (string.IsNullOrWhiteSpace(options.OutDirectory))
                {
                    throw new InvalidOperationException("app-server generate-ts 需要输出目录。");
                }

                arguments.Add("--out");
                arguments.Add(options.OutDirectory);
                if (!string.IsNullOrWhiteSpace(options.PrettierPath))
                {
                    arguments.Add("--prettier");
                    arguments.Add(options.PrettierPath);
                }

                if (options.Experimental)
                {
                    arguments.Add("--experimental");
                }

                break;
            case AppServerCommandKind.GenerateJsonSchema:
                arguments.Add("generate-json-schema");
                if (string.IsNullOrWhiteSpace(options.OutDirectory))
                {
                    throw new InvalidOperationException("app-server generate-json-schema 需要输出目录。");
                }

                arguments.Add("--out");
                arguments.Add(options.OutDirectory);
                if (options.Experimental)
                {
                    arguments.Add("--experimental");
                }

                break;
            default:
                throw new InvalidOperationException($"不支持的 app-server 子命令：{options.CommandKind}");
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

internal sealed record AppServerLaunchSpec(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);
