using TianShu.AppHost.Configuration;
using TianShu.Execution.Runtime;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Governance;
using TianShu.RuntimeComposition;

namespace TianShu.Cli;

internal sealed class SendCommandOptions
{
    public string Message { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;

    public string? AppHostProjectPath { get; init; }

    public string ConfigFilePath { get; init; } = RuntimeConfigurationComposition.ResolveDefaultPath();

    public string? ProfileName { get; init; }

    public IReadOnlyDictionary<string, string>? ConfigOverrides { get; init; }

    public string? ResumeThreadId { get; init; }

    public bool ResumeLatestThread { get; init; }

    public bool ResumeLatestMatchCwd { get; init; } = true;

    public bool ApproveAll { get; init; }

    public ControlPlaneApprovalDecision ApprovalDecision { get; init; } = ControlPlaneApprovalDecision.Approve;

    public string? PermissionsJsonPath { get; init; }

    public string? UserInputJsonPath { get; init; }

    public string? CollaborationMode { get; init; }

    public IReadOnlyList<ControlPlaneDynamicToolSpec>? DynamicTools { get; init; }

    public string ArtifactsRoot { get; init; } = Path.Combine(Environment.CurrentDirectory, ".tianshu-cli", "runs");

    public int TurnTimeoutSeconds { get; init; } = 300;

    public bool OutputJson { get; init; }

    public bool VerboseEvents { get; init; }

    public bool KernelRuntimeLoop { get; init; }

    public bool EnableSubAgents { get; init; }

    public static SendCommandParseResult Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return SendCommandParseResult.ShowHelpResult();
        }

        string? message = null;
        string? workingDirectory = null;
        string? appHostProjectPath = null;
        string? configFilePath = null;
        string? profileName = null;
        var configOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        string? resumeThreadId = null;
        var approvalDecision = ControlPlaneApprovalDecision.Approve;
        string? permissionsJsonPath = null;
        string? userInputJsonPath = null;
        string? collaborationMode = null;
        IReadOnlyList<ControlPlaneDynamicToolSpec>? dynamicTools = null;
        string? artifactsRoot = null;
        int? turnTimeoutSeconds = null;
        var outputJson = false;
        var verboseEvents = false;
        var resumeLatestThread = false;
        var resumeLatestMatchCwd = true;
        var approveAll = false;
        var kernelRuntimeLoop = false;
        var enableSubAgents = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase))
            {
                return SendCommandParseResult.ShowHelpResult();
            }

            if (string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase))
            {
                outputJson = true;
                continue;
            }

            if (string.Equals(arg, "--verbose-events", StringComparison.OrdinalIgnoreCase))
            {
                verboseEvents = true;
                continue;
            }

            if (string.Equals(arg, "--resume-latest", StringComparison.OrdinalIgnoreCase))
            {
                resumeLatestThread = true;
                continue;
            }

            if (string.Equals(arg, "--resume-latest-any-cwd", StringComparison.OrdinalIgnoreCase))
            {
                resumeLatestMatchCwd = false;
                continue;
            }

            if (string.Equals(arg, "--approve-all", StringComparison.OrdinalIgnoreCase))
            {
                approveAll = true;
                continue;
            }

            if (string.Equals(arg, "--kernel-runtime-loop", StringComparison.OrdinalIgnoreCase))
            {
                kernelRuntimeLoop = true;
                continue;
            }

            if (string.Equals(arg, "--enable-subagents", StringComparison.OrdinalIgnoreCase))
            {
                enableSubAgents = true;
                continue;
            }

            if (string.Equals(arg, "--apphost-control-plane", StringComparison.OrdinalIgnoreCase))
            {
                return SendCommandParseResult.Failure("--apphost-control-plane 已移除；CLI send 只能使用 Kernel→Runtime loop。");
            }

            if (!TryReadValue(args, ref i, arg, out var value, out var error))
            {
                return SendCommandParseResult.Failure(error);
            }

            switch (arg)
            {
                case "--message":
                    message = value;
                    break;
                case "--cwd":
                    workingDirectory = value;
                    break;
                case "--apphost-project":
                    appHostProjectPath = value;
                    break;
                case "--config":
                    if (LooksLikeConfigOverride(value))
                    {
                        if (!TryApplyConfigOverride(configOverrides, value, out var configOverrideError))
                        {
                            return SendCommandParseResult.Failure(configOverrideError);
                        }
                    }
                    else
                    {
                        configFilePath = value;
                    }
                    break;
                case "--config-file":
                    configFilePath = value;
                    break;
                case "-c":
                    if (!TryApplyConfigOverride(configOverrides, value, out var shortConfigOverrideError))
                    {
                        return SendCommandParseResult.Failure(shortConfigOverrideError);
                    }
                    break;
                case "--profile":
                    profileName = value;
                    break;
                case "--resume-thread-id":
                    resumeThreadId = value;
                    break;
                case "--approval-decision":
                    if (!CliApprovalResponseResolver.TryParseDecisionToken(value, out approvalDecision))
                    {
                        return SendCommandParseResult.Failure("--approval-decision 必须是 accept、session、always、decline 或 cancel。");
                    }

                    break;
                case "--permissions-json":
                    permissionsJsonPath = value;
                    break;
                case "--user-input-json":
                    userInputJsonPath = value;
                    break;
                case "--collaboration-mode":
                    collaborationMode = value;
                    break;
                case "--dynamic-tools-json":
                    if (dynamicTools is not null)
                    {
                        return SendCommandParseResult.Failure("--dynamic-tools-json 与 --dynamic-tools-file 不能重复或同时提供。");
                    }

                    if (!CliStructuredPayloadReader.TryReadTypedArrayPayload(
                            Normalize(value),
                            null,
                            "dynamic tools",
                            out dynamicTools,
                            out var dynamicToolsJsonError))
                    {
                        return SendCommandParseResult.Failure(dynamicToolsJsonError);
                    }

                    break;
                case "--dynamic-tools-file":
                    if (dynamicTools is not null)
                    {
                        return SendCommandParseResult.Failure("--dynamic-tools-json 与 --dynamic-tools-file 不能重复或同时提供。");
                    }

                    if (!CliStructuredPayloadReader.TryReadTypedArrayPayload(
                            null,
                            NormalizePath(value),
                            "dynamic tools",
                            out dynamicTools,
                            out var dynamicToolsFileError))
                    {
                        return SendCommandParseResult.Failure(dynamicToolsFileError);
                    }

                    break;
                case "--artifacts":
                    artifactsRoot = value;
                    break;
                case "--turn-timeout-seconds":
                    if (!int.TryParse(value, out var parsedTurnTimeoutSeconds) || parsedTurnTimeoutSeconds <= 0)
                    {
                        return SendCommandParseResult.Failure("--turn-timeout-seconds 必须是大于 0 的整数。");
                    }

                    turnTimeoutSeconds = parsedTurnTimeoutSeconds;
                    break;
                default:
                    return SendCommandParseResult.Failure($"不支持的参数：{arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return SendCommandParseResult.Failure("缺少必填参数：--message <text>");
        }

        if (!string.IsNullOrWhiteSpace(resumeThreadId) && resumeLatestThread)
        {
            return SendCommandParseResult.Failure("--resume-thread-id 与 --resume-latest 不能同时使用。");
        }

        if (!resumeLatestThread && !resumeLatestMatchCwd)
        {
            return SendCommandParseResult.Failure("--resume-latest-any-cwd 只能和 --resume-latest 一起使用。");
        }

        if (enableSubAgents && !approveAll)
        {
            return SendCommandParseResult.Failure("--enable-subagents 需要同时启用 --approve-all，作为本轮 sub-agent HostMutation 授权边界。");
        }

        return SendCommandParseResult.Success(
            new SendCommandOptions
            {
                Message = message.Trim(),
                WorkingDirectory = NormalizePath(workingDirectory) ?? Environment.CurrentDirectory,
                AppHostProjectPath = Normalize(appHostProjectPath),
                ConfigFilePath = NormalizePath(configFilePath) ?? RuntimeConfigurationComposition.ResolveDefaultPath(),
                ProfileName = Normalize(profileName),
                ConfigOverrides = configOverrides.Count == 0
                    ? null
                    : new Dictionary<string, string>(configOverrides, StringComparer.Ordinal),
                ResumeThreadId = Normalize(resumeThreadId),
                ResumeLatestThread = resumeLatestThread,
                ResumeLatestMatchCwd = resumeLatestMatchCwd,
                ApproveAll = approveAll,
                ApprovalDecision = approvalDecision,
                PermissionsJsonPath = NormalizePath(permissionsJsonPath),
                UserInputJsonPath = NormalizePath(userInputJsonPath),
                CollaborationMode = Normalize(collaborationMode),
                DynamicTools = dynamicTools,
                ArtifactsRoot = NormalizePath(artifactsRoot) ?? Path.Combine(Environment.CurrentDirectory, ".tianshu-cli", "runs"),
                TurnTimeoutSeconds = turnTimeoutSeconds ?? 300,
                OutputJson = outputJson,
                VerboseEvents = verboseEvents,
                KernelRuntimeLoop = kernelRuntimeLoop,
                EnableSubAgents = enableSubAgents,
            });
    }

    public static string GetHelpText()
        => string.Join(
            Environment.NewLine,
            [
                "天枢 TianShu CLI - send 单轮发送命令",
                string.Empty,
                "用法：",
                "  dotnet run --project src/Presentations/TianShu.Cli -- --message \"当前目录是？\" [选项]",
                string.Empty,
                "选项：",
                "  --message <text>             必填，本轮发送给宿主的消息",
                "  --cwd <path>                 可选，工作目录，默认当前目录",
                "  --apphost-project <path>     可选，宿主项目路径，默认解析 TianShu.AppHost.csproj",
                "  --config <path|key=value>    可选，配置参数；不含 = 时视为 tianshu.toml 路径，含 = 时视为配置覆盖",
                "  --config-file <path>         可选，显式指定 tianshu.toml 路径，默认 ~/.tianshu/tianshu.toml",
                "  -c <key=value>               可选，追加天枢配置覆盖，可重复传入",
                "  --profile <name>             可选，覆盖 tianshu.toml 中的 profile",
                "  --resume-thread-id <id>      可选，恢复指定线程并继续发送",
                "  --resume-latest              可选，恢复当前工作目录下最近线程",
                "  --resume-latest-any-cwd      可选，和 --resume-latest 配合，允许跨 cwd 恢复最近线程",
                "  --approve-all                可选，自动批准审批请求",
                "  --approval-decision <value>  可选，自动审批决策：accept/session/always/decline/cancel",
                "  --permissions-json <path>    可选，收到权限申请请求时自动提交 JSON 授权结果",
                "  --user-input-json <path>     可选，收到用户补录请求时自动提交 JSON 答案",
                "  --collaboration-mode <mode>  可选，透传到 runtime turn/start 的 collaborationMode.mode",
                "  --dynamic-tools-json <json>  可选，传入 dynamic tools JSON 数组",
                "  --dynamic-tools-file <path>  可选，从文件读取 dynamic tools JSON 数组",
                "  --artifacts <path>           可选，产物输出根目录",
                "  --turn-timeout-seconds <n>   可选，等待回合完成的超时时间（秒），默认 300",
                "  --json                       可选，以 JSON 输出摘要",
                "  --verbose-events             可选，实时打印事件流",
                "  --kernel-runtime-loop         可选，显式使用新 Kernel→Runtime 反应式 loop 验证入口",
                "  --enable-subagents           可选，配合 --approve-all 显式开放模型自主 spawn_agent / module.sub_agent",
                "  --help                       显示帮助",
            ]);

    private static bool TryReadValue(string[] args, ref int index, string option, out string value, out string error)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            error = $"参数 {option} 缺少值。";
            return false;
        }

        var next = args[index + 1];
        if (next.StartsWith("--", StringComparison.Ordinal) || IsHelp(next))
        {
            value = string.Empty;
            error = $"参数 {option} 缺少值。";
            return false;
        }

        value = args[++index];
        error = string.Empty;
        return true;
    }

    private static bool LooksLikeConfigOverride(string value)
        => value.Contains('=', StringComparison.Ordinal);

    private static bool TryApplyConfigOverride(
        IDictionary<string, string> configOverrides,
        string rawPair,
        out string error)
    {
        var separatorIndex = rawPair.IndexOf('=', StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            error = $"无效配置覆盖参数：{rawPair}，应为 key=value。";
            return false;
        }

        var key = Normalize(rawPair[..separatorIndex]);
        if (string.IsNullOrWhiteSpace(key))
        {
            error = $"无效配置覆盖参数：{rawPair}，key 不能为空。";
            return false;
        }

        configOverrides[key!] = rawPair[(separatorIndex + 1)..];
        error = string.Empty;
        return true;
    }

    private static bool IsHelp(string arg)
        => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
           || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? NormalizePath(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(normalized));
    }
}

internal sealed class SendCommandParseResult
{
    private SendCommandParseResult(SendCommandOptions? options, string? errorMessage, bool showHelp)
    {
        Options = options;
        ErrorMessage = errorMessage;
        ShowHelp = showHelp;
    }

    public SendCommandOptions? Options { get; }

    public string? ErrorMessage { get; }

    public bool ShowHelp { get; }

    public static SendCommandParseResult Success(SendCommandOptions options)
        => new(options, null, showHelp: false);

    public static SendCommandParseResult Failure(string errorMessage)
        => new(null, errorMessage, showHelp: true);

    public static SendCommandParseResult ShowHelpResult()
        => new(null, null, showHelp: true);
}

internal enum SendCommandExitCode
{
    Success = 0,
    InvalidArguments = 2,
    InvalidConfig = 3,
    KernelProjectNotFound = 4,
    InitializeFailed = 5,
    SendFailed = 6,
    TurnFailed = 7,
    ApprovalOrInputRequired = 8,
}
