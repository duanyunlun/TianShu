using TianShu.AppHost.Configuration;
using TianShu.AppHost.State;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace TianShu.AppHost;

/// <summary>
/// TianShu 本地宿主入口。
/// Entry point for the local TianShu application host.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        if (!TryParseCommand(args[0], out var commandKind))
        {
            Console.Error.WriteLine($"未知命令：{args[0]}");
            PrintUsage();
            return 2;
        }

        if (!TryParseCliConfigOverrides(args, out var cliConfigOverrides, out var parseError))
        {
            Console.Error.WriteLine(parseError);
            return 2;
        }

        if (!TryGetConfigFilePath(args, out var configFilePath, out parseError))
        {
            Console.Error.WriteLine(parseError);
            return 2;
        }

        AppHostProviderAssemblyPreloader.TryLoadPackagedProviders();

        var storage = KernelStoragePaths.ResolveDefault();
        var threadStore = new KernelThreadStore(
            storage.ThreadStoreFilePath,
            storage.SessionsDirectory,
            storage.ArchivedSessionsDirectory);

        switch (commandKind)
        {
            case AppHostCommandKind.McpServer:
                {
                    var server = new AppHostMcpServer(Console.In, Console.Out, threadStore, cliConfigOverrides, configFilePath);
                    await server.RunAsync(CancellationToken.None).ConfigureAwait(false);
                    break;
                }
            case AppHostCommandKind.AppServer:
                {
                    if (!TryParseAppServerSubcommand(args, out var appServerSubcommand, out var subcommandParseError))
                    {
                        Console.Error.WriteLine(subcommandParseError);
                        return 2;
                    }

                    if (appServerSubcommand is not null)
                    {
                        if (!TryRunAppServerSubcommand(appServerSubcommand, out var runSubcommandError))
                        {
                            Console.Error.WriteLine(runSubcommandError);
                            return 2;
                        }

                        break;
                    }

                    var listenUrl = TryGetListenUrl(args);
                    if (!AppHostServerTransport.TryParse(listenUrl, out var transport, out var transportError))
                    {
                        Console.Error.WriteLine(transportError);
                        return 2;
                    }

                    switch (transport)
                    {
                        case AppHostServerTransport.Stdio:
                            {
                                var server = new AppHostServer(Console.In, Console.Out, threadStore, cliConfigOverrides, configFilePath);
                                await server.RunAsync(CancellationToken.None).ConfigureAwait(false);
                                break;
                            }
                        case AppHostServerTransport.WebSocket(var bindAddress):
                            await RunWebSocketAsync(bindAddress, threadStore, cliConfigOverrides, configFilePath).ConfigureAwait(false);
                            break;
                    }

                    break;
                }
        }

        return 0;
    }

    private static bool TryParseCommand(string arg, out AppHostCommandKind commandKind)
    {
        if (string.Equals(arg, "app-server", StringComparison.OrdinalIgnoreCase))
        {
            commandKind = AppHostCommandKind.AppServer;
            return true;
        }

        if (string.Equals(arg, "mcp-server", StringComparison.OrdinalIgnoreCase))
        {
            commandKind = AppHostCommandKind.McpServer;
            return true;
        }

        commandKind = default;
        return false;
    }

    private static bool IsHelp(string arg)
        => string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase)
           || string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
           || string.Equals(arg, "help", StringComparison.OrdinalIgnoreCase);

    private static string? TryGetListenUrl(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--listen", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return args[i + 1];
        }

        return null;
    }

    private static bool TryParseCliConfigOverrides(
        string[] args,
        out IReadOnlyDictionary<string, string> overrides,
        out string error)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            var current = args[i];
            if (!string.Equals(current, "-c", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(current, "--config", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                overrides = result;
                error = "参数 -c/--config 缺少 key=value。";
                return false;
            }

            var pair = args[++i];
            var index = pair.IndexOf('=', StringComparison.Ordinal);
            if (index <= 0)
            {
                overrides = result;
                error = $"无效配置覆盖参数：{pair}，应为 key=value。";
                return false;
            }

            var key = pair[..index].Trim();
            var value = pair[(index + 1)..];
            if (string.IsNullOrWhiteSpace(key))
            {
                overrides = result;
                error = $"无效配置覆盖参数：{pair}，key 不能为空。";
                return false;
            }

            result[key] = value;
        }

        overrides = result;
        error = string.Empty;
        return true;
    }

    private static bool TryGetConfigFilePath(string[] args, out string? configFilePath, out string error)
    {
        configFilePath = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--config-file", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                error = "参数 --config-file 缺少路径。";
                return false;
            }

            configFilePath = args[++i];
        }

        error = string.Empty;
        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("TianShu.AppHost");
        Console.WriteLine("用法：");
        Console.WriteLine("  TianShu.AppHost app-server --listen stdio:// [--analytics-default-enabled] [--config-file <path>] [-c key=value] [--config key=value]");
        Console.WriteLine("  TianShu.AppHost app-server --listen ws://IP:PORT [--analytics-default-enabled] [--config-file <path>] [-c key=value] [--config key=value]");
        Console.WriteLine("  TianShu.AppHost app-server generate-ts (--out|-o <dir>) [--prettier|-p <path>] [--experimental]");
        Console.WriteLine("  TianShu.AppHost app-server generate-json-schema (--out|-o <dir>) [--experimental]");
        Console.WriteLine("  TianShu.AppHost mcp-server [--config-file <path>] [-c key=value] [--config key=value]");
    }

    private static async Task RunWebSocketAsync(
        IPEndPoint bindAddress,
        KernelThreadStore threadStore,
        IReadOnlyDictionary<string, string> cliConfigOverrides,
        string? configFilePath,
        CancellationToken cancellationToken = default)
    {
        var host = new AppHostWebSocketTransportHost(bindAddress, threadStore, cliConfigOverrides, configFilePath);
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool TryParseAppServerSubcommand(
        string[] args,
        out AppHostAppServerSubcommandOptions? options,
        out string error)
    {
        options = null;
        error = string.Empty;

        if (args.Length < 2 || args[1].StartsWith("-", StringComparison.Ordinal))
        {
            return true;
        }

        return args[1].ToLowerInvariant() switch
        {
            "generate-ts" => TryParseAppServerGenerateOptions(args, "generate-ts", allowPrettier: true, out options, out error),
            "generate-json-schema" => TryParseAppServerGenerateOptions(args, "generate-json-schema", allowPrettier: false, out options, out error),
            _ => FailParseSubcommand($"不支持的 app-server 子命令：{args[1]}", out options, out error),
        };
    }

    private static bool TryParseAppServerGenerateOptions(
        string[] args,
        string subcommandName,
        bool allowPrettier,
        out AppHostAppServerSubcommandOptions? options,
        out string error)
    {
        options = null;
        error = string.Empty;
        string? outDirectory = null;
        string? prettierPath = null;
        var experimental = false;

        for (var i = 2; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--out":
                case "-o":
                    if (!TryReadOptionValue(args, ref i, arg, out var outValue, out error))
                    {
                        return false;
                    }

                    outDirectory = NormalizePath(outValue);
                    break;
                case "--prettier":
                case "-p":
                    if (!allowPrettier)
                    {
                        error = $"app-server {subcommandName} 不支持参数：{arg}";
                        return false;
                    }

                    if (!TryReadOptionValue(args, ref i, arg, out var prettierValue, out error))
                    {
                        return false;
                    }

                    prettierPath = NormalizePath(prettierValue);
                    break;
                case "--experimental":
                    experimental = true;
                    break;
                default:
                    error = $"不支持的 app-server {subcommandName} 参数：{arg}";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(outDirectory))
        {
            error = $"app-server {subcommandName} 缺少 --out <dir>（或 -o <dir>）。";
            return false;
        }

        options = new AppHostAppServerSubcommandOptions(
            subcommandName,
            outDirectory,
            prettierPath,
            experimental);
        return true;
    }

    private static bool TryRunAppServerSubcommand(AppHostAppServerSubcommandOptions options, [NotNullWhen(false)] out string? error)
    {
        error = null;

        if (!TryResolveAppServerProtocolSchemaRoot(out var schemaRoot))
        {
            error = "未找到 app-server protocol 产物目录，请确认 external protocol schema 参考源已同步。";
            return false;
        }

        var sourceDirectory = options.SubcommandName switch
        {
            "generate-ts" => Path.Combine(schemaRoot, "typescript"),
            "generate-json-schema" => Path.Combine(schemaRoot, "json"),
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            error = $"未找到 app-server protocol 子目录：{sourceDirectory}";
            return false;
        }

        try
        {
            CopyDirectoryRecursive(sourceDirectory, options.OutDirectory);
            return true;
        }
        catch (Exception ex)
        {
            error = $"生成 app-server 协议文件失败：{ex.Message}";
            return false;
        }
    }

    private static bool TryResolveAppServerProtocolSchemaRoot([NotNullWhen(true)] out string? schemaRoot)
    {
        schemaRoot = null;
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        };

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            var processDirectory = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrWhiteSpace(processDirectory))
            {
                candidates.Add(processDirectory);
            }
        }

        foreach (var candidate in candidates)
        {
            if (TryFindSchemaRootFrom(candidate, out schemaRoot))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindSchemaRootFrom(string startPath, [NotNullWhen(true)] out string? schemaRoot)
    {
        schemaRoot = null;
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return false;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "external",
                "codex",
                "codex-rs",
                "app-server-protocol",
                "schema");
            if (Directory.Exists(candidate))
            {
                schemaRoot = candidate;
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private static void CopyDirectoryRecursive(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(targetDirectory, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));

    private static bool TryReadOptionValue(
        string[] args,
        ref int index,
        string option,
        [NotNullWhen(true)] out string? value,
        out string error)
    {
        value = null;
        error = string.Empty;

        if (index + 1 >= args.Length)
        {
            error = $"参数 {option} 缺少值。";
            return false;
        }

        var next = args[index + 1];
        if (next.StartsWith("-", StringComparison.Ordinal))
        {
            error = $"参数 {option} 缺少值。";
            return false;
        }

        value = args[++index];
        return true;
    }

    private static bool FailParseSubcommand(string message, out AppHostAppServerSubcommandOptions? options, out string error)
    {
        options = null;
        error = message;
        return false;
    }
}

internal enum AppHostCommandKind
{
    AppServer,
    McpServer,
}

internal sealed record AppHostAppServerSubcommandOptions(
    string SubcommandName,
    string OutDirectory,
    string? PrettierPath,
    bool Experimental);
