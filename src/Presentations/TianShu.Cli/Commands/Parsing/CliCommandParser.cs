using System.Text.Json;
using TianShu.Execution.Runtime;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Memory;

namespace TianShu.Cli;

internal enum CliCommandKind
{
    Completion,
    Init,
    Doctor,
    Send,
    FollowUp,
    Chat,
    Thread,
    Agent,
    Review,
    Rpc,
    Model,
    Tools,
    Skills,
    Plugin,
    App,
    Config,
    Command,
    Exec,
    CodeMode,
    Features,
    ExperimentalFeature,
    CollaborationMode,
    AppServer,
    Mcp,
    McpServer,
    FuzzyFileSearch,
    Feedback,
    WindowsSandbox,
    Realtime,
    Debug,
}

internal sealed class CliCommandParseResult
{
    private CliCommandParseResult(object? command, string? errorMessage, bool showHelp)
    {
        Command = command;
        ErrorMessage = errorMessage;
        ShowHelp = showHelp;
    }

    public object? Command { get; }

    public string? ErrorMessage { get; }

    public bool ShowHelp { get; }

    public static CliCommandParseResult Success(object command)
        => new(command, null, showHelp: false);

    public static CliCommandParseResult Failure(string errorMessage)
        => new(null, errorMessage, showHelp: true);

    public static CliCommandParseResult Help()
        => new(null, null, showHelp: true);
}

internal static class CliCommandParser
{
    public static CliCommandParseResult Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Success(new ChatCommandOptions());
        }

        var first = args[0];
        if (IsHelp(first))
        {
            return CliCommandParseResult.Help();
        }

        if (first.StartsWith("-", StringComparison.Ordinal))
        {
            if (TryParseLeadingInteractiveCommand(args, out var commandResult))
            {
                return commandResult;
            }

            return ParseChat(args);
        }

        var tail = args.Skip(1).ToArray();
        return first.ToLowerInvariant() switch
        {
            "completion" => ParseCompletion(tail),
            "init" => ParseInit(tail),
            "doctor" => ParseDoctor(tail),
            "send" => ParseSend(tail),
            "follow-up" => ParseFollowUp(tail),
            "followup" => ParseFollowUp(tail),
            "chat" => ParseChat(tail),
            "resume" => ParseStartupThreadCommand(tail, ChatStartupThreadActionKind.Resume),
            "fork" => ParseStartupThreadCommand(tail, ChatStartupThreadActionKind.Fork),
            "thread" => ParseThread(tail),
            "conversation" => ParseConversation(tail),
            "session" => ParseSession(tail),
            "governance" => ParseGovernance(tail),
            "workflow" => ParseWorkflow(tail),
            "collaboration" => ParseCollaboration(tail),
            "participant" => ParseParticipant(tail),
            "agent" => ParseAgent(tail),
            "artifact" => ParseArtifact(tail),
            "identity" => ParseIdentity(tail),
            "memory" => ParseMemory(tail),
            "diagnostics" => ParseDiagnostics(tail),
            "review" => ParseReview(tail),
            "rpc" => ParseRpc(tail),
            "model-route" => ParseModelRouteCommand(tail),
            "tools" => ParseTools(tail),
            "skills" => ParseSkills(tail),
            "plugin" => ParsePlugin(tail),
            "app" => ParseApp(tail),
            "config" => ParseConfig(tail),
            "command" => ParseCommand(tail),
            "exec" => ParseExec(tail),
            "e" => ParseExec(tail),
            "code-mode" => ParseCodeMode(tail),
            "codemode" => ParseCodeMode(tail),
            "code_mode" => ParseCodeMode(tail),
            "features" => ParseFeatures(tail),
            "experimental-feature" => ParseExperimentalFeature(tail),
            "experimental" => ParseExperimentalFeature(tail),
            "mode" => ParseCollaborationMode(tail),
            "collaboration-mode" => ParseCollaborationMode(tail),
            "collaborationmode" => ParseCollaborationMode(tail),
            "app-server" => ParseAppServer(tail),
            "mcp" => ParseMcp(tail),
            "mcp-server" => ParseMcpServer(tail),
            "conversation-summary" => ParseConversationSummary(tail),
            "summary" => ParseConversationSummary(tail),
            "git-diff" => ParseGitDiff(tail),
            "gitdiff" => ParseGitDiff(tail),
            "fuzzy-file-search" => ParseFuzzyFileSearch(tail),
            "fuzzy" => ParseFuzzyFileSearch(tail),
            "feedback" => ParseFeedback(tail),
            "windows-sandbox" => ParseWindowsSandbox(tail),
            "sandbox" => ParseWindowsSandbox(tail),
            "realtime" => ParseRealtime(tail),
            "debug" => ParseDebug(tail),
            _ => ParseChat(args),
        };
    }

    public static string GetHelpText()
        => string.Join(
            Environment.NewLine,
            [
                "天枢 TianShu CLI - 全功能 AI 协作代理",
                string.Empty,
                "用法：",
                "  tianshu [选项] [prompt]",
                "  tianshu resume [thread-id|name] [选项]",
                "  tianshu fork [thread-id|name] [选项]",
                "  tianshu <command> [选项]",
                "  tianshu -m gpt-5.2 -i .\\diagram.png \"请描述图片\"",
                string.Empty,
                "仓库开发入口：",
                "  dotnet run --project src/Presentations/TianShu.Cli -- [选项] [prompt]",
                string.Empty,
                "命令：",
                "  <none>      无参直接进入交互式会话 CLI",
                "  completion  生成 shell completion 脚本，默认 bash",
                "  init        初始化公开默认配置与 provider 模板，不写入 secret",
                "  doctor      离线检查 TianShu 首跑配置；--probe 才进行联网探测",
                "  send        单轮发送消息，沿用现有 send 链路",
                "  follow-up   发送 follow-up（queue / steer / interrupt）",
                "  chat        进入交互式会话 CLI，支持脚本化与 JSONL 协议输出",
                "  resume      顶层恢复线程并进入交互式会话；支持显式目标、--last、picker",
                "  fork        顶层分叉线程并进入交互式会话；支持显式目标、--last、picker",
                "  thread      线程管理：list / start / fork / archive / delete / clear / rename / resume / loaded-list / compact / clean-background-terminals / unsubscribe / increment-elicitation / decrement-elicitation / read / unarchive / metadata / rollback",
                "  conversation 会话线程 formal query：read",
                "  session     会话 formal query：snapshot / overview / list",
                "  governance  治理 formal query：approvals / user-inputs",
                "  workflow    工作流 formal surface：create / publish-plan / create-task / update-task-state / board / taskboard / plan",
                "  collaboration 协作空间 formal surface：create / configure / archive / overview / read / list",
                "  participant 参与者 formal surface：bind-session / bind-workflow / update-role / read / view / list",
                "  artifact    产物 formal query：read / list",
                "  review      代码审查能力：uncommitted / base / commit / start",
                "  rpc         通过执行运行时通用入口优先调用 formal surface，必要时回退 Kernel request method",
                "  model-route 模型路由能力：list / catalog / route / resolve",
                "  tools       工具能力：list / export-config",
                "  skills      技能能力：list / enable / disable / remote-list / remote-export",
                "  plugin      插件能力：list / read / install / uninstall",
                "  app         应用能力：list",
                "  config      配置能力：read / requirements / write / batch-write",
                "  exec        非交互 headless 会话执行：支持新会话与 resume",
                "  e           exec 的短别名",
                "  code-mode   代码执行能力：exec / wait",
                "  command     命令执行能力：exec / write / terminate / resize",
                "  features     实验特性能力：list / enable / disable",
                "  experimental-feature  实验特性能力：list",
                "  app-server  启动天枢 app-server，或执行 generate-ts / generate-json-schema",
                "  mcp         MCP 配置管理：list / get / add / remove",
                "  mcp-server  启动天枢 MCP Server",
                "  conversation-summary 会话摘要：按线程或回放文件读取",
                "  git-diff    获取线程对应的远端 git diff",
                "  fuzzy-file-search 模糊文件搜索：search / start / update / stop",
                "  feedback    typed feedback upload",
                "  windows-sandbox  typed Windows Sandbox setup",
                "  realtime    typed realtime session controls：start / append-text / append-audio / handoff-output / stop",
                "  debug       调试能力：clear-memories",
                "  mode        协作模式能力：list",
                "  agent       Agent 编排能力：list / roster / team / thread register / job create / job dispatch / job report-item / job read",
                "  identity    身份只读能力：account / devices",
                "  memory      记忆能力：providers / spaces / overlay / search / filter / add / extract / import / export / bind-provider / consolidate / forget / delete / supersede / review / feedback / citation",
                "  diagnostics 诊断只读能力：trace / attempts",
                string.Empty,
                "示例：",
                "  tianshu",
                "  tianshu completion",
                "  tianshu completion powershell",
                "  tianshu init --provider openai",
                "  tianshu doctor",
                "  tianshu doctor --probe",
                "  tianshu resume --last",
                "  tianshu fork thread_001",
                "  tianshu send --message \"当前目录是？\"",
                "  tianshu chat --script Test/chat-script.txt --protocol jsonl",
                "  tianshu thread delete --thread-id thread_001",
                "  tianshu thread clear",
                "  tianshu conversation read --thread-id thread_001 --json",
                "  tianshu session overview --session-id session_001 --json",
                "  tianshu governance approvals --participant-id participant_001",
                "  tianshu workflow create --workflow-id workflow_001 --space-id space_001 --display-name \"Workflow 001\"",
                "  tianshu workflow board --workflow-id workflow_001",
                "  tianshu collaboration create --space-id space_001 --key team-alpha --display-name \"Team Alpha\" --purpose \"跨仓库协作\" --json",
                "  tianshu collaboration read --space-id space_001 --json",
                "  tianshu participant bind-session --participant-id participant_001 --session-id session_001 --json",
                "  tianshu participant list --space-id space_001",
                "  tianshu artifact list --space-id space_001",
                "  tianshu agent roster --workflow-id workflow_001",
                "  tianshu identity account --account-id account_001 --json",
                "  tianshu memory overlay --space-id space_001 --json",
                "  tianshu memory consolidate --payload-json \"{\\\"memorySpaceId\\\":{\\\"value\\\":\\\"space_001\\\"},\\\"enableLease\\\":false}\" --json",
                "  tianshu memory review --json",
                "  tianshu diagnostics trace --trace-id trace_001 --json",
                "  tianshu model-route list --limit 10",
                "  tianshu model-route catalog --limit 20 --include-hidden",
                "  tianshu model-route route --route coding --json",
                "  tianshu model-route resolve --provider-key openai --model-key gpt-5 --reasoning-effort high",
                "  tianshu tools list --include-hidden",
                "  tianshu tools export-config --out ~/.tianshu/tool_profiles.builtin.toml",
                "  tianshu skills list --force-reload --extra-root D:\\Skills",
                "  tianshu skills enable --path C:\\skills\\demo",
                "  tianshu skills remote-export --hazelnut-id skill_001",
                "  tianshu app-server",
                "  tianshu app-server --listen ws://127.0.0.1:4222",
                "  tianshu app-server --analytics-default-enabled",
                "  tianshu app-server generate-ts --out .\\artifacts\\protocol-ts",
                "  tianshu app-server generate-json-schema --out .\\artifacts\\protocol-json",
                "  tianshu mcp list",
                "  tianshu mcp get demo --json",
                "  tianshu mcp add demo -- node server.js",
                "  tianshu mcp add demo-http --url https://example.com/mcp",
                "  tianshu mcp remove demo",
                "  tianshu mcp-server",
                "  tianshu agent list --include-primary-threads",
                "  tianshu agent job create --instruction \"分析这个任务\" --items-file Test/items.json",
                "  tianshu features list",
                "  tianshu features enable unified_exec",
                "  tianshu experimental-feature list --limit 20",
                "  tianshu conversation-summary --thread-id thread_001",
                "  tianshu git-diff --thread-id thread_001",
                "  tianshu review --uncommitted",
                "  tianshu review start --thread-id thread_001 --target uncommitted-changes",
                "  tianshu fuzzy-file-search search --query Program.cs --root src",
                "  tianshu feedback upload --classification bug --include-logs",
                "  tianshu windows-sandbox setup-start --mode elevated",
                "  tianshu realtime start --thread-id thread_001 --session-id session_001",
                "  tianshu realtime handoff-output --thread-id thread_001 --session-id session_001 --handoff-id call_001 --output \"delegated result\"",
                "  tianshu debug clear-memories --json",
                "  tianshu plugin read --marketplace-path D:\\marketplace --plugin-name sample",
                "  tianshu plugin install --marketplace-path D:\\marketplace --plugin-name sample",
                "  tianshu config read --include-layers --json",
                "  tianshu config write --key shell_environment_policy.inherit --value-json false",
                "  tianshu exec \"当前目录是？\"",
                "  tianshu exec resume --last \"继续完成剩余修复\"",
                "  tianshu code-mode exec --input \"console.log(process.cwd())\"",
                "  tianshu code-mode wait --cell-id cell_001 --terminate",
                "  tianshu command exec --command \"git status\"",
                string.Empty,
                "公共选项：",
                "  --cwd <path>                 工作目录，默认当前目录",
                "  --apphost-project <path>     宿主项目路径，默认解析 TianShu.AppHost.csproj",
                "  --config <path|key=value>    配置参数；不含 = 时视为 tianshu.toml 路径，含 = 时视为配置覆盖",
                "  --config-file <path>         显式指定 tianshu.toml 路径；默认自动层叠 ~/.tianshu 与 CWD .tianshu",
                "  -c <key=value>               追加天枢配置覆盖，可重复传入",
                "  --profile <name>             覆盖 tianshu.toml 中的 profile",
                "  --resume-thread-id <id>      恢复指定线程",
                "  --resume-latest              恢复当前 cwd 下最近线程",
                "  --resume-latest-any-cwd      与 --resume-latest 配合，允许跨 cwd 恢复",
                "  --collaboration-mode <mode>  覆盖 collaboration mode",
                "  --web-search <mode>         覆盖 web_search（disabled/cached/live）",
                "  --dynamic-tools-json <json>  传入 dynamic tools JSON 数组",
                "  --dynamic-tools-file <path>  从文件读取 dynamic tools JSON 数组",
                "  --json                       以 JSON 输出结果（chat 命令除外）",
                string.Empty,
                "completion 附加选项：",
                "  [bash|zsh|fish|powershell]   目标 shell，默认 bash",
                string.Empty,
                "chat / follow-up / thread resume 附加选项：",
                "  --approve-all                自动批准审批请求",
                "  --approval-decision <value>  自动审批决策：accept/session/always/decline/cancel",
                "  --permissions-json <path>    自动提交权限申请响应",
                "  --user-input-json <path>     自动提交用户补录答案",
                "  --verbose-events             打印详细事件流",
                "  --script <path>              从脚本文件执行 chat 命令/消息",
                "  --protocol <human|jsonl>     chat 输出协议，默认 human",
                "  --artifacts <path>           chat 产物输出根目录（summary/resolved-options/events/commands/transcript）",
                "  resume/fork 顶层附加选项：",
                "  --last                       直接选择最近线程，不打开 picker",
                "  --all                        取消 cwd 过滤；对 picker 与按名称解析生效",
                string.Empty,
                "typed 命令附加选项：",
                "  conversation read --thread-id <id>",
                "  session snapshot",
                "  session overview --session-id <id>",
                "  session list [--collaboration-space-id <id>] [--include-closed]",
                "  governance approvals [--participant-id <id>]",
                "  governance user-inputs [--participant-id <id>]",
                "  collaboration create --space-id <id> --key <key> --display-name <name> --purpose <text> [--default-workspace <path>] [--default-execution-profile <name>] [--policy-key <key>]",
                "  collaboration configure --space-id <id> [--display-name <name>] [--purpose <text>] [--default-workspace <path>] [--default-execution-profile <name>] [--policy-key <key>]",
                "  collaboration archive --space-id <id>",
                "  collaboration overview --space-id <id>",
                "  collaboration read --space-id <id>",
                "  collaboration list [--include-archived]",
                "  participant bind-session --participant-id <id> --session-id <id>",
                "  participant bind-workflow --participant-id <id> --workflow-id <id>",
                "  participant update-role --participant-id <id> --role <name>",
                "  participant read --participant-id <id>",
                "  participant view --participant-id <id>",
                "  participant list --space-id <id>",
                "  artifact read --artifact-id <id>",
                "  artifact list [--space-id <id>] [--participant-id <id>]",
                "  workflow create --workflow-id <id> --space-id <id> --display-name <name>",
                "  workflow publish-plan --workflow-id <id> --title <name> [--steps-json <json> | --steps-file <path>]",
                "  workflow create-task --task-id <id> --workflow-id <id> --title <name> [--state <todo|in-progress|blocked|done|cancelled>]",
                "  workflow update-task-state --task-id <id> --state <todo|in-progress|blocked|done|cancelled>",
                "  workflow board --workflow-id <id>",
                "  workflow taskboard --workflow-id <id>",
                "  workflow plan --workflow-id <id>",
                "  agent list [--limit <n>] [--cursor <cursor>] [--include-primary-threads]",
                "  agent roster [--workflow-id <id>]",
                "  agent team --team-id <id>",
                "  identity account --account-id <id>",
                "  identity devices --account-id <id>",
                "  memory providers [--payload-json <json>|--payload-file <path>]",
                "  memory spaces [--scope-kind <kind>]",
                "  memory overlay [--memory-space-id <id>] [--space-id <id>]",
                "  memory search [--payload-json <json>|--payload-file <path>]",
                "  memory filter [--payload-json <json>|--payload-file <path>]",
                "  memory add [--payload-json <json>|--payload-file <path>]",
                "  memory extract [--payload-json <json>|--payload-file <path>]",
                "  memory import [--payload-json <json>|--payload-file <path>]",
                "  memory export [--payload-json <json>|--payload-file <path>]",
                "  memory bind-provider [--payload-json <json>|--payload-file <path>]",
                "  memory consolidate [--payload-json <json>|--payload-file <path>]",
                "  memory forget [--payload-json <json>|--payload-file <path>]",
                "  memory delete [--payload-json <json>|--payload-file <path>]",
                "  memory supersede [--payload-json <json>|--payload-file <path>]",
                "  memory review [list|approve|demote|merge|restore] [--payload-json <json>|--payload-file <path>]",
                "  memory feedback [--payload-json <json>|--payload-file <path>]",
                "  memory citation [--payload-json <json>|--payload-file <path>]",
                "  diagnostics trace --trace-id <id>",
                "  diagnostics attempts --execution-id <id>",
                "  model-route list [--limit <n>] [--cursor <cursor>] [--include-hidden]",
                "  model-route catalog [--limit <n>] [--include-hidden]",
                "  model-route route [--route <kind>] [--route-set <id>] [--json]",
                "  model-route resolve [--provider-key <key>] [--model-key <key>] [--reasoning-effort <value>] [--reasoning-summary <value>] [--verbosity <value>] [--prefer-websocket-transport]",
                "  tools list [--include-hidden]",
                "  tools export-config [--out <path>] [--json]",
                "  skills list [--force-reload] [--extra-root <absolute-path>]",
                "  skills enable --path <path>",
                "  skills disable --path <path>",
                "  skills remote-list [--hazelnut-scope <scope>] [--product-surface <surface>] [--enabled <true|false>]",
                "  skills remote-export --hazelnut-id <id>",
                "  plugin list [--force-remote-sync]",
                "  plugin read --marketplace-path <path> --plugin-name <name>",
                "  plugin install --marketplace-path <path> --plugin-name <name>",
                "  plugin uninstall --plugin-id <plugin@marketplace>",
                "  app list [--limit <n>] [--cursor <cursor>] [--thread-id <id>] [--force-refetch]",
                "  config read [--include-layers]",
                "  config requirements",
                "  config write --key <path> --value-json <json> [--merge-strategy replace|upsert] [--file-path <path>] [--expected-version <version>]",
                "  config batch-write --items-json <json> | --items-file <path> [--merge-strategy replace|upsert]",
                "  exec [PROMPT|-] [--json] [-o <path>] [-i <path[,path...]>] [-m <model>] [-s <mode>] [-C <dir>] [--full-auto|--dangerously-bypass-approvals-and-sandbox] [--skip-git-repo-check]",
                "  exec resume [SESSION_ID|NAME] [PROMPT|-] [--last] [--all] [--json] [-o <path>]",
                "  exec review (--uncommitted | --base <branch> | --commit <sha> [--title <title>] | [PROMPT|-])",
                "  code-mode exec (--input <text> | --input-file <path>) [--thread-id <id>] [--yield-time-ms <n>] [--max-output-tokens <n>]",
                "  code-mode wait --cell-id <id> [--thread-id <id>] [--yield-time-ms <n>] [--max-tokens <n>] [--terminate]",
                "  command exec (--command <text> | --argv-json <json> | --argv-file <path>) [--tty] [--process-id <id>]",
                "  command write --process-id <id> [--text <text> | --stdin-file <path> | --base64 <text>] [--close-stdin]",
                "  command terminate --process-id <id>",
                "  command resize --process-id <id> --rows <n> --cols <n>",
                "  features list",
                "  features enable <name>",
                "  features disable <name>",
                "  experimental-feature list [--limit <n>] [--cursor <cursor>]",
                "  app-server [--listen <stdio://|ws://IP:PORT>] [--analytics-default-enabled] [--config-file <path>] [-c <key=value>]",
                "  app-server generate-ts (--out|-o <dir>) [--prettier|-p <path>] [--experimental]",
                "  app-server generate-json-schema (--out|-o <dir>) [--experimental]",
                "  mcp list [--json]",
                "  mcp get <name> [--json]",
                "  mcp add <name> [--env <KEY=VALUE>]... (--url <URL> [--bearer-token-env-var <ENV>] | -- <COMMAND>...)",
                "  mcp remove <name>",
                "  mcp-server",
                "  conversation-summary (--thread-id <id> | --rollout-path <path>)",
                "  git-diff --thread-id <id>",
                "  review start --thread-id <id> --target <uncommitted-changes|base-branch|commit|custom> [--delivery <inline|detached>]",
                "  fuzzy-file-search search --query <text> [--root <path>] [--limit <n>]",
                "  fuzzy-file-search start --session-id <id> [--root <path>]",
                "  fuzzy-file-search update --session-id <id> --query <text> [--root <path>]",
                "  fuzzy-file-search stop --session-id <id>",
                "  feedback upload --classification <name> [--include-logs] [--thread-id <id>] [--reason <text>] [--extra-log-file <path>]",
                "  windows-sandbox setup-start --mode <elevated|unelevated> [--cwd <absolute-path>]",
                "  realtime start --thread-id <id> [--session-id <id>] [--prompt <text>]",
                "  realtime append-text --thread-id <id> [--session-id <id>] --text <content>",
                "  realtime append-audio --thread-id <id> [--session-id <id>] (--audio-json <json> | --audio-file <path>)",
                "  realtime handoff-output --thread-id <id> [--session-id <id>] --handoff-id <id> --output <text>",
                "  realtime stop --thread-id <id> [--session-id <id>]",
                "  agent thread register --thread-id <id> [--agent-nickname <name>] [--agent-role <role>]",
                "  agent job create --instruction <text> [--job-id <id>] [--name <name>] [--items-json <json>|--items-file <path>]",
                "  agent job dispatch --job-id <id> --thread-id <id> [--thread-id <id> ...]",
                "  agent job report-item --job-id <id> --item-id <id> --status <status> [--result-json <json>|--result-file <path>]",
                "  agent job read --job-id <id>",
                "  mode list",
                string.Empty,
                "thread 子命令：",
                "  thread list [--limit <n>] [--cursor <cursor>] [--sort-key <created_at|updated_at>] [--model-provider <id>] [--source-kind <kind>] [--search-term <text>] [--archived] [--all-cwd]",
                "  thread start",
                "  thread fork --thread-id <id>",
                "  thread archive --thread-id <id>",
                "  thread rename --thread-id <id> --name <name>",
                "  thread resume --thread-id <id> [--approve-all] [--approval-decision <value>] [--permissions-json <path>] [--user-input-json <path>]",
                "  thread start/fork/resume 可附带请求覆盖参数：",
                "    --thread-cwd <path> --thread-model <name> --thread-model-provider <id> --thread-service-tier <tier>",
                "    --thread-approval-policy <policy> --thread-sandbox-mode <mode> --thread-config-json <json>|--thread-config-file <path>",
                "    --thread-service-name <name> --thread-base-instructions <text> --thread-developer-instructions <text>",
                "    --thread-persist-extended-history <true|false>；--thread-experimental-raw-events <true|false> 仅 thread start 可用",
                "  thread loaded-list [--limit <n>] [--cursor <cursor>]",
                "  thread compact --thread-id <id> [--keep-recent-turns <n>]",
                "  thread clean-background-terminals --thread-id <id>",
                "  thread unsubscribe --thread-id <id>",
                "  thread increment-elicitation --thread-id <id>",
                "  thread decrement-elicitation --thread-id <id>",
                "  thread read --thread-id <id> [--include-turns]",
                "  thread unarchive --thread-id <id>",
                "  thread metadata --thread-id <id> [--git-sha <sha>] [--git-branch <name>] [--git-origin-url <url>]",
                "  thread rollback --thread-id <id> --num-turns <n>",
            ]);

    private static CliCommandParseResult ParseInit(string[] args)
    {
        var options = new InitCommandOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (IsHelp(arg))
            {
                return CliCommandParseResult.Help();
            }

            if (string.Equals(arg, "--force", StringComparison.OrdinalIgnoreCase))
            {
                options.Force = true;
                continue;
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return CliCommandParseResult.Failure(error);
            }

            switch (arg)
            {
                case "--provider":
                    options.Provider = Normalize(value);
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }

                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(options.Provider)
            && !CliFirstRunBootstrapper.IsSupportedProvider(options.Provider))
        {
            return CliCommandParseResult.Failure("--provider 只能是 openai、anthropic 或 openai-compatible。");
        }

        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseDoctor(string[] args)
    {
        var options = new DoctorCommandOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (IsHelp(arg))
            {
                return CliCommandParseResult.Help();
            }

            if (string.Equals(arg, "--probe", StringComparison.OrdinalIgnoreCase))
            {
                options.Probe = true;
                continue;
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return CliCommandParseResult.Failure(error);
            }

            if (!TryApplyCommonOption(arg, value, options, out error))
            {
                return CliCommandParseResult.Failure(error);
            }
        }

        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseSend(string[] args)
    {
        var result = SendCommandOptions.Parse(args);
        if (result.ShowHelp && result.ErrorMessage is not null)
        {
            return CliCommandParseResult.Failure(result.ErrorMessage);
        }

        if (result.ShowHelp || result.Options is null)
        {
            return CliCommandParseResult.Help();
        }

        return CliCommandParseResult.Success(result.Options);
    }

    private static CliCommandParseResult ParseFollowUp(string[] args)
    {
        var options = new FollowUpCliCommandOptions();
        string? mode = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (IsHelp(arg))
            {
                return CliCommandParseResult.Help();
            }

            if (string.Equals(arg, "--approve-all", StringComparison.OrdinalIgnoreCase))
            {
                options.ApproveAll = true;
                continue;
            }

            if (string.Equals(arg, "--verbose-events", StringComparison.OrdinalIgnoreCase))
            {
                options.VerboseEvents = true;
                continue;
            }

            if (string.Equals(arg, "--kernel-runtime-loop", StringComparison.OrdinalIgnoreCase))
            {
                options.KernelRuntimeLoop = true;
                continue;
            }

            if (string.Equals(arg, "--enable-shell", StringComparison.OrdinalIgnoreCase))
            {
                options.EnableShell = true;
                continue;
            }

            if (string.Equals(arg, "--enable-mcp", StringComparison.OrdinalIgnoreCase))
            {
                options.EnableMcp = true;
                continue;
            }

            if (string.Equals(arg, "--enable-memory", StringComparison.OrdinalIgnoreCase))
            {
                options.EnableMemory = true;
                continue;
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return CliCommandParseResult.Failure(error);
            }

            switch (arg)
            {
                case "--message":
                    options.Message = value;
                    break;
                case "--approval-decision":
                    if (!CliApprovalResponseResolver.TryParseDecisionToken(value, out var followUpApprovalDecision))
                    {
                        return CliCommandParseResult.Failure("--approval-decision 必须是 accept、session、always、decline 或 cancel。");
                    }

                    options.ApprovalDecision = followUpApprovalDecision;
                    break;
                case "--mode":
                    mode = value;
                    break;
                case "--turn-id":
                    options.TurnId = Normalize(value);
                    break;
                case "--checkpoint-ref":
                    options.CheckpointRef = Normalize(value);
                    break;
                case "--resume-token":
                    options.ResumeToken = Normalize(value);
                    break;
                case "--turn-timeout-seconds":
                    if (!int.TryParse(value, out var parsedTurnTimeoutSeconds) || parsedTurnTimeoutSeconds <= 0)
                    {
                        return CliCommandParseResult.Failure("--turn-timeout-seconds 必须是大于 0 的整数。");
                    }

                    options.TurnTimeoutSeconds = parsedTurnTimeoutSeconds;
                    break;
                case "--permissions-json":
                    options.PermissionsJsonPath = NormalizePath(value);
                    break;
                case "--user-input-json":
                    options.UserInputJsonPath = NormalizePath(value);
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }
                    break;
            }
        }

        var isKernelRuntimeResume = options.KernelRuntimeLoop && !string.IsNullOrWhiteSpace(options.CheckpointRef);
        if (string.IsNullOrWhiteSpace(options.Message) && !isKernelRuntimeResume)
        {
            return CliCommandParseResult.Failure("缺少必填参数：--message <text>");
        }

        if (string.IsNullOrWhiteSpace(options.Message) && isKernelRuntimeResume)
        {
            options.Message = "kernel-runtime.resume";
        }

        if (string.IsNullOrWhiteSpace(mode) && isKernelRuntimeResume)
        {
            mode = ControlPlaneFollowUpMode.Queue.ToString();
        }

        if (!Enum.TryParse<ControlPlaneFollowUpMode>(mode, ignoreCase: true, out var parsedMode))
        {
            return CliCommandParseResult.Failure("--mode 必须是 queue、steer 或 interrupt。");
        }

        options.Mode = parsedMode;
        if (options.EnableShell && !options.ApproveAll)
        {
            return CliCommandParseResult.Failure("--enable-shell 需要同时启用 --approve-all，作为本轮 shell HostMutation 授权边界。");
        }

        if (!ValidateResumeOptions(options, out var validationError))
        {
            return CliCommandParseResult.Failure(validationError);
        }

        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseChat(string[] args)
    {
        var options = new ChatCommandOptions();
        string? promptFromPositional = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (IsHelp(arg))
            {
                return CliCommandParseResult.Help();
            }

            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                if (promptFromPositional is not null)
                {
                    return CliCommandParseResult.Failure("chat 最多只接受一个 prompt 位置参数。");
                }

                promptFromPositional = arg;
                continue;
            }

            if (string.Equals(arg, "--approve-all", StringComparison.OrdinalIgnoreCase))
            {
                options.ApproveAll = true;
                continue;
            }

            if (string.Equals(arg, "--verbose-events", StringComparison.OrdinalIgnoreCase))
            {
                options.VerboseEvents = true;
                continue;
            }

            if (string.Equals(arg, "--full-auto", StringComparison.OrdinalIgnoreCase))
            {
                options.FullAuto = true;
                continue;
            }

            if (string.Equals(arg, "--dangerously-bypass-approvals-and-sandbox", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--yolo", StringComparison.OrdinalIgnoreCase))
            {
                options.DangerouslyBypassApprovalsAndSandbox = true;
                continue;
            }

            if (string.Equals(arg, "--search", StringComparison.OrdinalIgnoreCase))
            {
                options.WebSearchMode = "live";
                continue;
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return CliCommandParseResult.Failure(error);
            }

            switch (arg)
            {
                case "--message":
                    options.InitialMessage = value;
                    break;
                case "--image":
                case "-i":
                    if (!TryAppendImagePaths(options.ImagePaths, value, out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }

                    break;
                case "--model":
                case "-m":
                    options.RuntimeModel = Normalize(value);
                    break;
                case "--profile":
                case "-p":
                    options.ProfileName = Normalize(value);
                    break;
                case "--sandbox":
                case "-s":
                    options.RuntimeSandboxMode = Normalize(value);
                    break;
                case "--ask-for-approval":
                case "-a":
                    if (!TryParseApprovalPolicy(value, out var runtimeApprovalPolicy, out error, "--ask-for-approval"))
                    {
                        return CliCommandParseResult.Failure(error);
                    }

                    options.RuntimeApprovalPolicy = runtimeApprovalPolicy;
                    break;
                case "--cd":
                case "-C":
                    options.WorkingDirectory = NormalizePath(value) ?? Environment.CurrentDirectory;
                    break;
                case "--approval-decision":
                    if (!CliApprovalResponseResolver.TryParseDecisionToken(value, out var chatApprovalDecision))
                    {
                        return CliCommandParseResult.Failure("--approval-decision 必须是 accept、session、always、decline 或 cancel。");
                    }

                    options.ApprovalDecision = chatApprovalDecision;
                    break;
                case "--permissions-json":
                    options.PermissionsJsonPath = NormalizePath(value);
                    break;
                case "--user-input-json":
                    options.UserInputJsonPath = NormalizePath(value);
                    break;
                case "--script":
                    options.ScriptPath = NormalizePath(value);
                    break;
                case "--protocol":
                    if (!Enum.TryParse<ChatOutputProtocol>(value, ignoreCase: true, out var protocol))
                    {
                        return CliCommandParseResult.Failure("--protocol 必须是 human 或 jsonl。");
                    }

                    options.OutputProtocol = protocol;
                    break;
                case "--artifacts":
                    options.ArtifactsRoot = NormalizePath(value);
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(options.InitialMessage) && promptFromPositional is not null)
        {
            return CliCommandParseResult.Failure("prompt 不能同时通过位置参数和 --message 提供。");
        }

        options.InitialMessage ??= promptFromPositional;
        ApplyChatDerivedRuntimeDefaults(options);

        if (!ValidateResumeOptions(options, out var validationError))
        {
            return CliCommandParseResult.Failure(validationError);
        }

        if (!ValidateChatOptions(options, out validationError))
        {
            return CliCommandParseResult.Failure(validationError);
        }

        return CliCommandParseResult.Success(options);
    }

    private static bool TryParseLeadingInteractiveCommand(string[] args, out CliCommandParseResult result)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                var tail = args.Skip(index + 1).ToArray();
                var leadingArgs = args.Take(index).ToArray();
                var mergedArgs = leadingArgs.Concat(tail).ToArray();

                switch (arg.ToLowerInvariant())
                {
                    case "chat":
                        result = ParseChat(mergedArgs);
                        return true;
                    case "resume":
                        result = ParseStartupThreadCommand(mergedArgs, ChatStartupThreadActionKind.Resume);
                        return true;
                    case "fork":
                        result = ParseStartupThreadCommand(mergedArgs, ChatStartupThreadActionKind.Fork);
                        return true;
                    case "exec":
                        result = ParseExec(mergedArgs);
                        return true;
                    default:
                        result = null!;
                        return false;
                }
            }

            if (!TrySkipInteractiveRootOption(args, ref index))
            {
                result = null!;
                return false;
            }
        }

        result = null!;
        return false;
    }

    private static bool TrySkipInteractiveRootOption(string[] args, ref int index)
    {
        var arg = args[index];
        if (string.Equals(arg, "--approve-all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--verbose-events", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--full-auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--dangerously-bypass-approvals-and-sandbox", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--yolo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--search", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(arg, "--message", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--image", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "-i", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--model", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "-m", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--profile", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "-p", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--sandbox", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "-s", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--ask-for-approval", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "-a", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--cd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "-C", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--approval-decision", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--permissions-json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--user-input-json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--script", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--protocol", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--artifacts", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--cwd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--apphost-project", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--config", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--config-file", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "-c", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--resume-thread-id", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--collaboration-mode", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--web-search", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--dynamic-tools-json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--dynamic-tools-file", StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= args.Length)
            {
                return false;
            }

            index++;
            return true;
        }

        if (string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--resume-latest", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--resume-latest-any-cwd", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static CliCommandParseResult ParseStartupThreadCommand(string[] args, ChatStartupThreadActionKind action)
    {
        string? target = null;
        var chatArgs = new List<string>();
        var useLast = false;
        var showAll = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (IsHelp(arg))
            {
                return CliCommandParseResult.Help();
            }

            if (string.Equals(arg, "--last", StringComparison.OrdinalIgnoreCase))
            {
                useLast = true;
                continue;
            }

            if (string.Equals(arg, "--all", StringComparison.OrdinalIgnoreCase))
            {
                showAll = true;
                continue;
            }

            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(target))
                {
                    target = Normalize(arg);
                    continue;
                }

                chatArgs.Add(arg);
                continue;
            }

            chatArgs.Add(arg);
            var optionIndex = index;
            if (TrySkipInteractiveRootOption(args, ref optionIndex) && optionIndex > index)
            {
                chatArgs.Add(args[optionIndex]);
                index = optionIndex;
            }
        }

        if (!string.IsNullOrWhiteSpace(target) && useLast)
        {
            var verb = action == ChatStartupThreadActionKind.Resume ? "resume" : "fork";
            return CliCommandParseResult.Failure($"{verb} 不能同时提供显式目标和 --last。");
        }

        var chatResult = ParseChat(chatArgs.ToArray());
        if (chatResult.Command is not ChatCommandOptions options || chatResult.ErrorMessage is not null || chatResult.ShowHelp)
        {
            return chatResult;
        }

        if (!ValidateResumeOptions(options, out var validationError))
        {
            return CliCommandParseResult.Failure(validationError);
        }

        if (!string.IsNullOrWhiteSpace(options.ResumeThreadId) || options.ResumeLatestThread)
        {
            return CliCommandParseResult.Failure("顶层 resume/fork 不接受 --resume-thread-id 或 --resume-latest；请直接使用 resume/fork 自身的目标选择。");
        }

        options.CreateThreadOnInitialize = false;
        options.StartupThreadAction = action;
        options.StartupThreadTarget = target;
        options.StartupThreadUseLast = useLast;
        options.StartupThreadShowAll = showAll;
        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseThread(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("thread 需要子命令：list/start/fork/archive/delete/clear/rename/resume/loaded-list/compact/clean-background-terminals/unsubscribe/increment-elicitation/decrement-elicitation/read/unarchive/metadata/rollback");
        }

        var subcommand = args[0].ToLowerInvariant();
        if (subcommand is not ("list" or "start" or "fork" or "archive" or "delete" or "clear" or "rename" or "resume" or "loaded-list" or "compact" or "clean-background-terminals" or "unsubscribe" or "increment-elicitation" or "decrement-elicitation" or "read" or "unarchive" or "metadata" or "rollback"))
        {
            return CliCommandParseResult.Failure($"不支持的 thread 子命令：{subcommand}");
        }

        var options = new ThreadCommandOptions
        {
            CommandKind = subcommand switch
            {
                "list" => ThreadCommandKind.List,
                "start" => ThreadCommandKind.Start,
                "fork" => ThreadCommandKind.Fork,
                "archive" => ThreadCommandKind.Archive,
                "delete" => ThreadCommandKind.Delete,
                "clear" => ThreadCommandKind.Clear,
                "rename" => ThreadCommandKind.Rename,
                "resume" => ThreadCommandKind.Resume,
                "loaded-list" => ThreadCommandKind.LoadedList,
                "compact" => ThreadCommandKind.Compact,
                "clean-background-terminals" => ThreadCommandKind.CleanBackgroundTerminals,
                "unsubscribe" => ThreadCommandKind.Unsubscribe,
                "increment-elicitation" => ThreadCommandKind.IncrementElicitation,
                "decrement-elicitation" => ThreadCommandKind.DecrementElicitation,
                "read" => ThreadCommandKind.Read,
                "unarchive" => ThreadCommandKind.Unarchive,
                "metadata" => ThreadCommandKind.Metadata,
                "rollback" => ThreadCommandKind.Rollback,
                _ => ThreadCommandKind.List,
            },
        };

        var tail = args.Skip(1).ToArray();

        for (var index = 0; index < tail.Length; index++)
        {
            var arg = tail[index];
            if (IsHelp(arg))
            {
                return CliCommandParseResult.Help();
            }

            if (string.Equals(arg, "--approve-all", StringComparison.OrdinalIgnoreCase))
            {
                options.ApproveAll = true;
                continue;
            }

            if (!TryReadValue(tail, ref index, arg, out var value, out var error))
            {
                return CliCommandParseResult.Failure(error);
            }

            switch (arg)
            {
                case "--limit":
                    if (!int.TryParse(value, out var limit) || limit <= 0)
                    {
                        return CliCommandParseResult.Failure("--limit 必须是大于 0 的整数。");
                    }

                    options.Limit = limit;
                    break;
                case "--cursor":
                    options.Cursor = Normalize(value);
                    break;
                case "--keep-recent-turns":
                case "--keep-recent":
                    if (!int.TryParse(value, out var keepRecentTurns) || keepRecentTurns <= 0)
                    {
                        return CliCommandParseResult.Failure("--keep-recent-turns 必须是大于 0 的整数。");
                    }

                    options.KeepRecentTurns = keepRecentTurns;
                    break;
                case "--num-turns":
                    if (!int.TryParse(value, out var numTurns) || numTurns <= 0)
                    {
                        return CliCommandParseResult.Failure("--num-turns 必须是大于 0 的整数。");
                    }

                    options.NumTurns = numTurns;
                    break;
                case "--thread-id":
                    options.ThreadId = Normalize(value);
                    break;
                case "--confirm":
                    options.Confirmation = Normalize(value);
                    break;
                case "--approval-decision":
                    if (!CliApprovalResponseResolver.TryParseDecisionToken(value, out var threadApprovalDecision))
                    {
                        return CliCommandParseResult.Failure("--approval-decision 必须是 accept、session、always、decline 或 cancel。");
                    }

                    options.ApprovalDecision = threadApprovalDecision;
                    break;
                case "--sort-key":
                    var sortKey = Normalize(value);
                    if (!string.Equals(sortKey, "created_at", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(sortKey, "updated_at", StringComparison.OrdinalIgnoreCase))
                    {
                        return CliCommandParseResult.Failure("--sort-key 只能是 created_at 或 updated_at。");
                    }

                    options.SortKey = sortKey!;
                    break;
                case "--model-provider":
                    options.ModelProviders.Add(value);
                    break;
                case "--source-kind":
                    if (!TryParseThreadSourceKind(value, out var sourceKind, out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }

                    options.SourceKinds.Add(sourceKind!);
                    break;
                case "--search-term":
                    options.SearchTerm = Normalize(value);
                    break;
                case "--permissions-json":
                    options.PermissionsJsonPath = NormalizePath(value);
                    break;
                case "--user-input-json":
                    options.UserInputJsonPath = NormalizePath(value);
                    break;
                case "--thread-path":
                    options.ThreadPath = Normalize(value);
                    break;
                case "--thread-cwd":
                    options.ThreadWorkingDirectory = NormalizePath(value) ?? Normalize(value);
                    break;
                case "--thread-model":
                    options.ThreadModel = Normalize(value);
                    break;
                case "--thread-model-provider":
                    options.ThreadModelProvider = Normalize(value);
                    break;
                case "--thread-service-tier":
                    if (!TryParseThreadServiceTier(value, out var serviceTier, out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }

                    options.ThreadServiceTier = serviceTier;
                    break;
                case "--thread-approval-policy":
                    if (!TryParseApprovalPolicy(value, out var approvalPolicy, out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }

                    options.ThreadApprovalPolicy = approvalPolicy;
                    break;
                case "--thread-sandbox-mode":
                    options.ThreadSandboxMode = Normalize(value);
                    break;
                case "--thread-config-json":
                    if (options.ThreadConfig is not null)
                    {
                        return CliCommandParseResult.Failure("--thread-config-json 与 --thread-config-file 只能提供其中之一。");
                    }

                    if (!CliStructuredPayloadReader.TryReadStructuredObjectPayload(
                            Normalize(value),
                            null,
                            "thread config",
                            out var configFromJson,
                            out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }

                    options.ThreadConfig = configFromJson;
                    break;
                case "--thread-config-file":
                    if (options.ThreadConfig is not null)
                    {
                        return CliCommandParseResult.Failure("--thread-config-json 与 --thread-config-file 只能提供其中之一。");
                    }

                    if (!CliStructuredPayloadReader.TryReadStructuredObjectPayload(
                            null,
                            NormalizePath(value),
                            "thread config",
                            out var configFromFile,
                            out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }

                    options.ThreadConfig = configFromFile;
                    break;
                case "--thread-service-name":
                    options.ThreadServiceName = Normalize(value);
                    break;
                case "--thread-base-instructions":
                    options.ThreadBaseInstructions = Normalize(value);
                    break;
                case "--thread-developer-instructions":
                    options.ThreadDeveloperInstructions = Normalize(value);
                    break;
                case "--thread-personality":
                    if (!TryParsePersonality(value, out var personality, out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }

                    options.ThreadPersonality = personality;
                    break;
                case "--thread-history-json":
                    if (options.ThreadHistory is not null)
                    {
                        return CliCommandParseResult.Failure("--thread-history-json 与 --thread-history-file 只能提供其中之一。");
                    }

                    if (!CliStructuredPayloadReader.TryReadStructuredArrayPayload(
                            Normalize(value),
                            null,
                            "thread history",
                            out var historyFromJson,
                            out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }

                    options.ThreadHistory = historyFromJson;
                    break;
                case "--thread-history-file":
                    if (options.ThreadHistory is not null)
                    {
                        return CliCommandParseResult.Failure("--thread-history-json 与 --thread-history-file 只能提供其中之一。");
                    }

                    if (!CliStructuredPayloadReader.TryReadStructuredArrayPayload(
                            null,
                            NormalizePath(value),
                            "thread history",
                            out var historyFromFile,
                            out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }

                    options.ThreadHistory = historyFromFile;
                    break;
                case "--thread-persist-extended-history":
                    if (!bool.TryParse(value, out var persistExtendedHistory))
                    {
                        return CliCommandParseResult.Failure("--thread-persist-extended-history 只能是 true 或 false。");
                    }

                    options.ThreadPersistExtendedHistory = persistExtendedHistory;
                    break;
                case "--thread-experimental-raw-events":
                    if (!bool.TryParse(value, out var experimentalRawEvents))
                    {
                        return CliCommandParseResult.Failure("--thread-experimental-raw-events 只能是 true 或 false。");
                    }

                    options.ThreadExperimentalRawEvents = experimentalRawEvents;
                    break;
                case "--thread-ephemeral":
                    if (!bool.TryParse(value, out var ephemeral))
                    {
                        return CliCommandParseResult.Failure("--thread-ephemeral 只能是 true 或 false。");
                    }

                    options.ThreadEphemeral = ephemeral;
                    break;
                case "--thread-dynamic-tools-json":
                    if (options.ThreadDynamicTools is not null)
                    {
                        return CliCommandParseResult.Failure("--thread-dynamic-tools-json 与 --thread-dynamic-tools-file 只能提供其中之一。");
                    }

                    if (!CliStructuredPayloadReader.TryReadTypedArrayPayload<ControlPlaneDynamicToolSpec>(
                            Normalize(value),
                            null,
                            "thread dynamic tools",
                            out var dynamicToolsFromJson,
                            out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }

                    options.ThreadDynamicTools = dynamicToolsFromJson;
                    break;
                case "--thread-dynamic-tools-file":
                    if (options.ThreadDynamicTools is not null)
                    {
                        return CliCommandParseResult.Failure("--thread-dynamic-tools-json 与 --thread-dynamic-tools-file 只能提供其中之一。");
                    }

                    if (!CliStructuredPayloadReader.TryReadTypedArrayPayload<ControlPlaneDynamicToolSpec>(
                            null,
                            NormalizePath(value),
                            "thread dynamic tools",
                            out var dynamicToolsFromFile,
                            out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }

                    options.ThreadDynamicTools = dynamicToolsFromFile;
                    break;
                case "--name":
                    options.Name = Normalize(value);
                    break;
                case "--git-sha":
                    options.GitSha = Normalize(value);
                    break;
                case "--git-branch":
                    options.GitBranch = Normalize(value);
                    break;
                case "--git-origin-url":
                    options.GitOriginUrl = Normalize(value);
                    break;
                default:
                    if (string.Equals(arg, "--archived", StringComparison.OrdinalIgnoreCase))
                    {
                        options.Archived = true;
                        break;
                    }

                    if (string.Equals(arg, "--all-cwd", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MatchCurrentCwd = false;
                        break;
                    }

                    if (string.Equals(arg, "--include-turns", StringComparison.OrdinalIgnoreCase))
                    {
                        options.IncludeTurns = true;
                        break;
                    }

                    if (string.Equals(arg, "--clear-git-sha", StringComparison.OrdinalIgnoreCase))
                    {
                        options.ClearGitSha = true;
                        break;
                    }

                    if (string.Equals(arg, "--clear-git-branch", StringComparison.OrdinalIgnoreCase))
                    {
                        options.ClearGitBranch = true;
                        break;
                    }

                    if (string.Equals(arg, "--clear-git-origin-url", StringComparison.OrdinalIgnoreCase))
                    {
                        options.ClearGitOriginUrl = true;
                        break;
                    }

                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }
                    break;
            }
        }

        if (!ValidateThreadOptions(options, out var threadError))
        {
            return CliCommandParseResult.Failure(threadError);
        }

        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseAgent(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("agent 需要二级命令：list / roster / team / thread register / job create / job dispatch / job report-item / job read");
        }

        var scope = args[0].ToLowerInvariant();
        if (scope is "list" or "roster" or "team")
        {
            return scope switch
            {
                "list" => ParseRuntimeSurfaceVerb(args, "agent", "list", RuntimeSurfaceCommandKind.AgentList, ParseAgentListOptions),
                "roster" => ParseRuntimeSurfaceVerb(args, "agent", "roster", RuntimeSurfaceCommandKind.AgentRoster, ParseAgentRosterOptions),
                "team" => ParseRuntimeSurfaceVerb(args, "agent", "team", RuntimeSurfaceCommandKind.AgentTeam, ParseAgentTeamOptions),
                _ => CliCommandParseResult.Failure($"不支持的 agent 命令：{scope}"),
            };
        }

        if (args.Length < 2)
        {
            return CliCommandParseResult.Failure("agent 需要二级命令：list / roster / team / thread register / job create / job dispatch / job report-item / job read");
        }

        var verb = args[1].ToLowerInvariant();
        return (scope, verb) switch
        {
            ("thread", "register") => ParseRuntimeSurfaceVerb(args, "agent", "thread", "register", RuntimeSurfaceCommandKind.AgentThreadRegister, ParseAgentThreadRegisterOptions),
            ("job", "create") => ParseRuntimeSurfaceVerb(args, "agent", "job", "create", RuntimeSurfaceCommandKind.AgentJobCreate, ParseAgentJobCreateOptions),
            ("job", "dispatch") => ParseRuntimeSurfaceVerb(args, "agent", "job", "dispatch", RuntimeSurfaceCommandKind.AgentJobDispatch, ParseAgentJobDispatchOptions),
            ("job", "report-item") => ParseRuntimeSurfaceVerb(args, "agent", "job", "report-item", RuntimeSurfaceCommandKind.AgentJobItemReport, ParseAgentJobItemReportOptions),
            ("job", "reportitem") => ParseRuntimeSurfaceVerb(args, "agent", "job", "reportitem", RuntimeSurfaceCommandKind.AgentJobItemReport, ParseAgentJobItemReportOptions),
            ("job", "read") => ParseRuntimeSurfaceVerb(args, "agent", "job", "read", RuntimeSurfaceCommandKind.AgentJobRead, ParseAgentJobReadOptions),
            _ => CliCommandParseResult.Failure($"不支持的 agent 命令：{scope} {verb}"),
        };
    }

    private static CliCommandParseResult ParseSession(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("session 需要子命令：snapshot / overview / list");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "snapshot" => ParseRuntimeSurfaceVerb(args, "session", "snapshot", RuntimeSurfaceCommandKind.SessionSnapshot, ParseSessionSnapshotOptions),
            "overview" => ParseRuntimeSurfaceVerb(args, "session", "overview", RuntimeSurfaceCommandKind.SessionOverview, ParseSessionOverviewOptions),
            "list" => ParseRuntimeSurfaceVerb(args, "session", "list", RuntimeSurfaceCommandKind.SessionList, ParseSessionListOptions),
            _ => CliCommandParseResult.Failure($"不支持的 session 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseConversation(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("conversation 需要子命令：read");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "read" => ParseRuntimeSurfaceVerb(args, "conversation", "read", RuntimeSurfaceCommandKind.ConversationThread, ParseConversationReadOptions),
            _ => CliCommandParseResult.Failure($"不支持的 conversation 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseGovernance(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("governance 需要子命令：approvals / user-inputs");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "approvals" => ParseRuntimeSurfaceVerb(args, "governance", "approvals", RuntimeSurfaceCommandKind.GovernanceApprovalQueue, ParseGovernanceApprovalQueueOptions),
            "user-inputs" => ParseRuntimeSurfaceVerb(args, "governance", "user-inputs", RuntimeSurfaceCommandKind.GovernanceUserInputList, ParseGovernanceUserInputListOptions),
            "userinputs" => ParseRuntimeSurfaceVerb(args, "governance", "userinputs", RuntimeSurfaceCommandKind.GovernanceUserInputList, ParseGovernanceUserInputListOptions),
            _ => CliCommandParseResult.Failure($"不支持的 governance 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseWorkflow(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("workflow 需要子命令：create / publish-plan / create-task / update-task-state / board / taskboard / plan");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "create" => ParseRuntimeSurfaceVerb(args, "workflow", "create", RuntimeSurfaceCommandKind.WorkflowCreate, ParseWorkflowCreateOptions),
            "publish-plan" => ParseRuntimeSurfaceVerb(args, "workflow", "publish-plan", RuntimeSurfaceCommandKind.WorkflowPublishPlan, ParseWorkflowPublishPlanOptions),
            "publishplan" => ParseRuntimeSurfaceVerb(args, "workflow", "publish-plan", RuntimeSurfaceCommandKind.WorkflowPublishPlan, ParseWorkflowPublishPlanOptions),
            "create-task" => ParseRuntimeSurfaceVerb(args, "workflow", "create-task", RuntimeSurfaceCommandKind.WorkflowCreateTask, ParseWorkflowCreateTaskOptions),
            "createtask" => ParseRuntimeSurfaceVerb(args, "workflow", "create-task", RuntimeSurfaceCommandKind.WorkflowCreateTask, ParseWorkflowCreateTaskOptions),
            "update-task-state" => ParseRuntimeSurfaceVerb(args, "workflow", "update-task-state", RuntimeSurfaceCommandKind.WorkflowUpdateTaskState, ParseWorkflowUpdateTaskStateOptions),
            "updatetaskstate" => ParseRuntimeSurfaceVerb(args, "workflow", "update-task-state", RuntimeSurfaceCommandKind.WorkflowUpdateTaskState, ParseWorkflowUpdateTaskStateOptions),
            "board" => ParseRuntimeSurfaceVerb(args, "workflow", "board", RuntimeSurfaceCommandKind.WorkflowBoard, ParseWorkflowBoardOptions),
            "taskboard" => ParseRuntimeSurfaceVerb(args, "workflow", "taskboard", RuntimeSurfaceCommandKind.WorkflowTaskBoard, ParseWorkflowTaskBoardOptions),
            "task-board" => ParseRuntimeSurfaceVerb(args, "workflow", "task-board", RuntimeSurfaceCommandKind.WorkflowTaskBoard, ParseWorkflowTaskBoardOptions),
            "plan" => ParseRuntimeSurfaceVerb(args, "workflow", "plan", RuntimeSurfaceCommandKind.WorkflowPlan, ParseWorkflowPlanOptions),
            _ => CliCommandParseResult.Failure($"不支持的 workflow 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseCollaboration(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("collaboration 需要子命令：create / configure / archive / overview / read / list");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "create" => ParseRuntimeSurfaceVerb(args, "collaboration", "create", RuntimeSurfaceCommandKind.CollaborationCreate, ParseCollaborationCreateOptions),
            "configure" => ParseRuntimeSurfaceVerb(args, "collaboration", "configure", RuntimeSurfaceCommandKind.CollaborationConfigure, ParseCollaborationConfigureOptions),
            "archive" => ParseRuntimeSurfaceVerb(args, "collaboration", "archive", RuntimeSurfaceCommandKind.CollaborationArchive, ParseCollaborationArchiveOptions),
            "overview" => ParseRuntimeSurfaceVerb(args, "collaboration", "overview", RuntimeSurfaceCommandKind.CollaborationOverview, ParseCollaborationOverviewOptions),
            "read" => ParseRuntimeSurfaceVerb(args, "collaboration", "read", RuntimeSurfaceCommandKind.CollaborationSpace, ParseCollaborationReadOptions),
            "list" => ParseRuntimeSurfaceVerb(args, "collaboration", "list", RuntimeSurfaceCommandKind.CollaborationList, ParseCollaborationListOptions),
            _ => CliCommandParseResult.Failure($"不支持的 collaboration 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseParticipant(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("participant 需要子命令：bind-session / bind-workflow / update-role / read / view / list");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "bind-session" => ParseRuntimeSurfaceVerb(args, "participant", "bind-session", RuntimeSurfaceCommandKind.ParticipantBindSession, ParseParticipantBindSessionOptions),
            "bindsession" => ParseRuntimeSurfaceVerb(args, "participant", "bind-session", RuntimeSurfaceCommandKind.ParticipantBindSession, ParseParticipantBindSessionOptions),
            "bind-workflow" => ParseRuntimeSurfaceVerb(args, "participant", "bind-workflow", RuntimeSurfaceCommandKind.ParticipantBindWorkflow, ParseParticipantBindWorkflowOptions),
            "bindworkflow" => ParseRuntimeSurfaceVerb(args, "participant", "bind-workflow", RuntimeSurfaceCommandKind.ParticipantBindWorkflow, ParseParticipantBindWorkflowOptions),
            "update-role" => ParseRuntimeSurfaceVerb(args, "participant", "update-role", RuntimeSurfaceCommandKind.ParticipantUpdateRole, ParseParticipantUpdateRoleOptions),
            "updaterole" => ParseRuntimeSurfaceVerb(args, "participant", "update-role", RuntimeSurfaceCommandKind.ParticipantUpdateRole, ParseParticipantUpdateRoleOptions),
            "read" => ParseRuntimeSurfaceVerb(args, "participant", "read", RuntimeSurfaceCommandKind.ParticipantRead, ParseParticipantReadOptions),
            "view" => ParseRuntimeSurfaceVerb(args, "participant", "view", RuntimeSurfaceCommandKind.ParticipantView, ParseParticipantViewOptions),
            "list" => ParseRuntimeSurfaceVerb(args, "participant", "list", RuntimeSurfaceCommandKind.ParticipantList, ParseParticipantListOptions),
            _ => CliCommandParseResult.Failure($"不支持的 participant 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseArtifact(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("artifact 需要子命令：read / list");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "read" => ParseRuntimeSurfaceVerb(args, "artifact", "read", RuntimeSurfaceCommandKind.ArtifactRead, ParseArtifactReadOptions),
            "list" => ParseRuntimeSurfaceVerb(args, "artifact", "list", RuntimeSurfaceCommandKind.ArtifactList, ParseArtifactListOptions),
            _ => CliCommandParseResult.Failure($"不支持的 artifact 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseIdentity(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("identity 需要子命令：account / devices");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "account" => ParseRuntimeSurfaceVerb(args, "identity", "account", RuntimeSurfaceCommandKind.IdentityAccount, ParseIdentityAccountOptions),
            "devices" => ParseRuntimeSurfaceVerb(args, "identity", "devices", RuntimeSurfaceCommandKind.IdentityDevices, ParseIdentityDevicesOptions),
            _ => CliCommandParseResult.Failure($"不支持的 identity 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseMemory(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("memory 需要子命令：providers / spaces / overlay / search / filter / add / extract / import / export / bind-provider / consolidate / forget / delete / supersede / review / feedback / citation");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "providers" => ParseRuntimeSurfaceVerb(args, "memory", "providers", RuntimeSurfaceCommandKind.MemoryProviders, ParseMemoryProvidersOptions),
            "spaces" => ParseRuntimeSurfaceVerb(args, "memory", "spaces", RuntimeSurfaceCommandKind.MemorySpaces, ParseMemorySpacesOptions),
            "overlay" => ParseRuntimeSurfaceVerb(args, "memory", "overlay", RuntimeSurfaceCommandKind.MemoryOverlay, ParseMemoryOverlayOptions),
            "search" => ParseRuntimeSurfaceVerb(WithFirstVerb(args, "filter"), "memory", "filter", RuntimeSurfaceCommandKind.MemoryFilter, ParseMemoryPayloadOptions),
            "filter" => ParseRuntimeSurfaceVerb(args, "memory", "filter", RuntimeSurfaceCommandKind.MemoryFilter, ParseMemoryPayloadOptions),
            "add" => ParseRuntimeSurfaceVerb(args, "memory", "add", RuntimeSurfaceCommandKind.MemoryAdd, ParseMemoryPayloadOptions),
            "extract" => ParseRuntimeSurfaceVerb(args, "memory", "extract", RuntimeSurfaceCommandKind.MemoryExtract, ParseMemoryPayloadOptions),
            "import" => ParseRuntimeSurfaceVerb(args, "memory", "import", RuntimeSurfaceCommandKind.MemoryImport, ParseMemoryPayloadOptions),
            "export" => ParseRuntimeSurfaceVerb(args, "memory", "export", RuntimeSurfaceCommandKind.MemoryExport, ParseMemoryPayloadOptions),
            "bind" => ParseRuntimeSurfaceVerb(WithFirstVerb(args, "bind-provider"), "memory", "bind-provider", RuntimeSurfaceCommandKind.MemoryBindProvider, ParseMemoryPayloadOptions),
            "bind-provider" => ParseRuntimeSurfaceVerb(args, "memory", "bind-provider", RuntimeSurfaceCommandKind.MemoryBindProvider, ParseMemoryPayloadOptions),
            "consolidate" or "consolidation" => ParseRuntimeSurfaceVerb(args, "memory", "consolidate", RuntimeSurfaceCommandKind.MemoryConsolidate, ParseMemoryPayloadOptions),
            "forget" => ParseRuntimeSurfaceVerb(args, "memory", "forget", RuntimeSurfaceCommandKind.MemoryForget, ParseMemoryPayloadOptions),
            "delete" => ParseRuntimeSurfaceVerb(args, "memory", "delete", RuntimeSurfaceCommandKind.MemoryDelete, ParseMemoryPayloadOptions),
            "supersede" => ParseRuntimeSurfaceVerb(args, "memory", "supersede", RuntimeSurfaceCommandKind.MemorySupersede, ParseMemoryPayloadOptions),
            "review" => ParseMemoryReview(args),
            "feedback" => ParseRuntimeSurfaceVerb(args, "memory", "feedback", RuntimeSurfaceCommandKind.MemoryFeedback, ParseMemoryPayloadOptions),
            "citation" => ParseRuntimeSurfaceVerb(args, "memory", "citation", RuntimeSurfaceCommandKind.MemoryCitation, ParseMemoryPayloadOptions),
            _ => CliCommandParseResult.Failure($"不支持的 memory 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseMemoryReview(string[] args)
    {
        if (args.Length <= 1)
        {
            return ParseRuntimeSurfaceCommand(Array.Empty<string>(), RuntimeSurfaceCommandKind.MemoryReviewList, ParseMemoryPayloadOptions);
        }

        if (args[1].StartsWith("--", StringComparison.Ordinal))
        {
            return ParseRuntimeSurfaceCommand(args.Skip(1).ToArray(), RuntimeSurfaceCommandKind.MemoryReviewList, ParseMemoryPayloadOptions);
        }

        var action = args[1].ToLowerInvariant();
        return action switch
        {
            "list" or "show" => ParseRuntimeSurfaceVerb(args, "memory", "review", action, RuntimeSurfaceCommandKind.MemoryReviewList, ParseMemoryPayloadOptions),
            "approve" => ParseRuntimeSurfaceVerb(args, "memory", "review", "approve", RuntimeSurfaceCommandKind.MemoryReviewApprove, ParseMemoryPayloadOptions),
            "demote" => ParseRuntimeSurfaceVerb(args, "memory", "review", "demote", RuntimeSurfaceCommandKind.MemoryReviewDemote, ParseMemoryPayloadOptions),
            "merge" => ParseRuntimeSurfaceVerb(args, "memory", "review", "merge", RuntimeSurfaceCommandKind.MemoryReviewMerge, ParseMemoryPayloadOptions),
            "restore" => ParseRuntimeSurfaceVerb(args, "memory", "review", "restore", RuntimeSurfaceCommandKind.MemoryReviewRestore, ParseMemoryPayloadOptions),
            _ => CliCommandParseResult.Failure($"不支持的 memory review 子命令：{action}"),
        };
    }

    private static CliCommandParseResult ParseDiagnostics(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("diagnostics 需要子命令：trace / attempts");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "trace" => ParseRuntimeSurfaceVerb(args, "diagnostics", "trace", RuntimeSurfaceCommandKind.DiagnosticsTrace, ParseDiagnosticsTraceOptions),
            "attempts" => ParseRuntimeSurfaceVerb(args, "diagnostics", "attempts", RuntimeSurfaceCommandKind.DiagnosticsAttemptList, ParseDiagnosticsAttemptListOptions),
            "attempt-summaries" => ParseRuntimeSurfaceVerb(args, "diagnostics", "attempt-summaries", RuntimeSurfaceCommandKind.DiagnosticsAttemptList, ParseDiagnosticsAttemptListOptions),
            "attemptsummaries" => ParseRuntimeSurfaceVerb(args, "diagnostics", "attemptsummaries", RuntimeSurfaceCommandKind.DiagnosticsAttemptList, ParseDiagnosticsAttemptListOptions),
            _ => CliCommandParseResult.Failure($"不支持的 diagnostics 子命令：{verb}"),
        };
    }

    private static string[] WithFirstVerb(string[] args, string verb)
    {
        var normalized = (string[])args.Clone();
        normalized[0] = verb;
        return normalized;
    }

    private static CliCommandParseResult ParseRpc(string[] args)
    {
        var options = new RpcCommandOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (IsHelp(arg))
            {
                return CliCommandParseResult.Help();
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return CliCommandParseResult.Failure(error);
            }

            switch (arg)
            {
                case "--method":
                    options.Method = value;
                    break;
                case "--params-json":
                    options.ParamsJson = value;
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(options.Method))
        {
            return CliCommandParseResult.Failure("缺少必填参数：--method <name>");
        }

        if (!ValidateResumeOptions(options, out var validationError))
        {
            return CliCommandParseResult.Failure(validationError);
        }

        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseModelRouteCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("model-route 需要子命令：list / catalog / route / resolve");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "list" => ParseRuntimeSurfaceVerb(args, "model", "list", RuntimeSurfaceCommandKind.ModelList, ParseModelListOptions),
            "catalog" => ParseRuntimeSurfaceVerb(args, "model", "catalog", RuntimeSurfaceCommandKind.ModelCatalog, ParseModelCatalogOptions),
            "route" => ParseModelRoute(args.Skip(1).ToArray()),
            "resolve" => ParseRuntimeSurfaceVerb(args, "model", "resolve", RuntimeSurfaceCommandKind.ModelResolve, ParseModelResolveOptions),
            _ => CliCommandParseResult.Failure($"不支持的 model-route 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseModelRoute(string[] args)
    {
        var options = new ModelRouteDiagnosticCommandOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return CliCommandParseResult.Failure(error);
            }

            switch (arg)
            {
                case "--route":
                case "--kind":
                    options.RouteKind = Normalize(value) ?? "default";
                    break;
                case "--route-set":
                case "--route-set-id":
                    options.RouteSetId = Normalize(value);
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return CliCommandParseResult.Failure(error);
                    }

                    break;
            }
        }

        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseTools(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("tools 需要子命令：list / export-config");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "list" => ParseRuntimeSurfaceVerb(args, "tools", "list", RuntimeSurfaceCommandKind.ToolCatalog, ParseToolCatalogOptions),
            "export-config" => ParseRuntimeSurfaceVerb(args, "tools", "export-config", RuntimeSurfaceCommandKind.ToolConfigExport, ParseToolConfigExportOptions),
            _ => CliCommandParseResult.Failure($"不支持的 tools 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseSkills(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("skills 需要子命令：list / enable / disable / remote-list / remote-export");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "list" => ParseRuntimeSurfaceVerb(args, "skills", "list", RuntimeSurfaceCommandKind.SkillsList, ParseSkillsListOptions),
            "enable" => ParseRuntimeSurfaceVerb(args, "skills", "enable", RuntimeSurfaceCommandKind.SkillsConfigWrite, ParseSkillsEnableOptions),
            "disable" => ParseRuntimeSurfaceVerb(args, "skills", "disable", RuntimeSurfaceCommandKind.SkillsConfigWrite, ParseSkillsDisableOptions),
            "remote-list" => ParseRuntimeSurfaceVerb(args, "skills", "remote-list", RuntimeSurfaceCommandKind.SkillsRemoteList, ParseSkillsRemoteListOptions),
            "remote-export" => ParseRuntimeSurfaceVerb(args, "skills", "remote-export", RuntimeSurfaceCommandKind.SkillsRemoteExport, ParseSkillsRemoteExportOptions),
            _ => CliCommandParseResult.Failure($"不支持的 skills 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParsePlugin(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("plugin 需要子命令：list/read/install/uninstall");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "list" => ParseRuntimeSurfaceVerb(args, "plugin", "list", RuntimeSurfaceCommandKind.PluginList, ParsePluginListOptions),
            "read" => ParseRuntimeSurfaceVerb(args, "plugin", "read", RuntimeSurfaceCommandKind.PluginRead, ParsePluginReadOptions),
            "install" => ParseRuntimeSurfaceVerb(args, "plugin", "install", RuntimeSurfaceCommandKind.PluginInstall, ParsePluginInstallOptions),
            "uninstall" => ParseRuntimeSurfaceVerb(args, "plugin", "uninstall", RuntimeSurfaceCommandKind.PluginUninstall, ParsePluginUninstallOptions),
            _ => CliCommandParseResult.Failure($"不支持的 plugin 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseApp(string[] args)
        => ParseRuntimeSurfaceVerb(args, "app", "list", RuntimeSurfaceCommandKind.AppList, ParseAppListOptions);

    private static CliCommandParseResult ParseConfig(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("config 需要子命令：read/requirements/write/batch-write");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "read" => ParseRuntimeSurfaceVerb(args, "config", "read", RuntimeSurfaceCommandKind.ConfigRead, ParseConfigReadOptions),
            "requirements" => ParseRuntimeSurfaceVerb(args, "config", "requirements", RuntimeSurfaceCommandKind.ConfigRequirementsRead, ParseConfigRequirementsReadOptions),
            "write" => ParseRuntimeSurfaceVerb(args, "config", "write", RuntimeSurfaceCommandKind.ConfigValueWrite, ParseConfigValueWriteOptions),
            "batch-write" => ParseRuntimeSurfaceVerb(args, "config", "batch-write", RuntimeSurfaceCommandKind.ConfigBatchWrite, ParseConfigBatchWriteOptions),
            _ => CliCommandParseResult.Failure($"不支持的 config 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseFeatures(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("features 需要子命令：list / enable / disable");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "list" => ParseRuntimeSurfaceVerb(args, "features", "list", RuntimeSurfaceCommandKind.FeatureList, ParseFeatureListOptions),
            "enable" => ParseRuntimeSurfaceVerb(args, "features", "enable", RuntimeSurfaceCommandKind.FeatureConfigWrite, ParseFeatureEnableOptions),
            "disable" => ParseRuntimeSurfaceVerb(args, "features", "disable", RuntimeSurfaceCommandKind.FeatureConfigWrite, ParseFeatureDisableOptions),
            _ => CliCommandParseResult.Failure($"不支持的 features 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("command 需要子命令：exec/write/terminate/resize");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "exec" => ParseCommandExecVerb(args.Skip(1).ToArray(), CommandExecCommandKind.Exec),
            "write" => ParseCommandExecVerb(args.Skip(1).ToArray(), CommandExecCommandKind.Write),
            "terminate" => ParseCommandExecVerb(args.Skip(1).ToArray(), CommandExecCommandKind.Terminate),
            "resize" => ParseCommandExecVerb(args.Skip(1).ToArray(), CommandExecCommandKind.Resize),
            _ => CliCommandParseResult.Failure($"不支持的 command 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseExec(string[] args)
    {
        if (args.Length > 0)
        {
            var verb = args[0].ToLowerInvariant();
            if (verb == "resume")
            {
                return ParseExecResume(args.Skip(1).ToArray());
            }

            if (verb == "review")
            {
                return ParseExecReview(args.Skip(1).ToArray());
            }
        }

        return ParseExecVerb(args, ExecCommandKind.UserTurn);
    }

    private static CliCommandParseResult ParseExecResume(string[] args)
        => ParseExecVerb(args, ExecCommandKind.Resume);

    private static CliCommandParseResult ParseExecReview(string[] args)
        => ParseExecVerb(args, ExecCommandKind.Review);

    private static CliCommandParseResult ParseExecVerb(string[] args, ExecCommandKind commandKind)
    {
        if (args.Any(IsHelp))
        {
            return CliCommandParseResult.Help();
        }

        var options = new ExecCommandOptions
        {
            CommandKind = commandKind,
        };

        var error = ParseExecOptions(args, options);
        if (!string.IsNullOrWhiteSpace(error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateResumeOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateExecOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseCodeMode(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("code-mode 需要子命令：exec / wait");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "exec" => ParseCodeModeExec(args.Skip(1).ToArray()),
            "wait" => ParseCodeModeWait(args.Skip(1).ToArray()),
            _ => CliCommandParseResult.Failure($"不支持的 code-mode 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseCodeModeExec(string[] args)
        => ParseCodeModeVerb(args, CodeModeCommandKind.Exec);

    private static CliCommandParseResult ParseCodeModeWait(string[] args)
        => ParseCodeModeVerb(args, CodeModeCommandKind.Wait);

    private static CliCommandParseResult ParseCodeModeVerb(string[] args, CodeModeCommandKind commandKind)
    {
        if (args.Any(static x => IsHelp(x)))
        {
            return CliCommandParseResult.Help();
        }

        var options = new CodeModeCommandOptions
        {
            CommandKind = commandKind,
        };

        var error = ParseCodeModeOptions(args, options);
        if (!string.IsNullOrWhiteSpace(error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateResumeOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateCodeModeOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseExperimentalFeature(string[] args)
        => ParseRuntimeSurfaceVerb(args, "experimental-feature", "list", RuntimeSurfaceCommandKind.ExperimentalFeatureList, ParseExperimentalFeatureListOptions);

    private static CliCommandParseResult ParseCompletion(string[] args)
    {
        if (args.Any(static x => IsHelp(x)))
        {
            return CliCommandParseResult.Help();
        }

        var options = new CompletionCommandOptions();
        if (args.Length == 0)
        {
            return CliCommandParseResult.Success(options);
        }

        if (args.Length > 1)
        {
            return CliCommandParseResult.Failure("completion 最多只接受一个 shell 参数：bash/zsh/fish/powershell");
        }

        if (!TryParseCompletionShell(args[0], out var shell))
        {
            return CliCommandParseResult.Failure($"不支持的 completion shell：{args[0]}。支持：bash / zsh / fish / powershell");
        }

        options.Shell = shell;
        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseAppServer(string[] args)
    {
        if (args.Any(static x => IsHelp(x)))
        {
            return CliCommandParseResult.Help();
        }

        var options = new AppServerCommandOptions();
        var error = ParseAppServerOptions(args, options);
        if (!string.IsNullOrWhiteSpace(error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateAppServerOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseMcp(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("mcp 需要子命令：list / get / add / remove");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "list" => ParseMcpVerb(args.Skip(1).ToArray(), McpCommandKind.List, ParseMcpListOptions),
            "get" => ParseMcpVerb(args.Skip(1).ToArray(), McpCommandKind.Get, ParseMcpGetOptions),
            "add" => ParseMcpVerb(args.Skip(1).ToArray(), McpCommandKind.Add, ParseMcpAddOptions),
            "remove" => ParseMcpVerb(args.Skip(1).ToArray(), McpCommandKind.Remove, ParseMcpRemoveOptions),
            _ => CliCommandParseResult.Failure($"不支持的 mcp 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseMcpServer(string[] args)
    {
        if (args.Any(static x => IsHelp(x)))
        {
            return CliCommandParseResult.Help();
        }

        var options = new McpServerCommandOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                return CliCommandParseResult.Failure($"不支持的 mcp-server 参数：{arg}");
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return CliCommandParseResult.Failure(error);
            }

            if (!TryApplyCommonOption(arg, value, options, out error))
            {
                return CliCommandParseResult.Failure(error);
            }
        }

        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseConversationSummary(string[] args)
        => ParseRuntimeSurfaceCommand(args, RuntimeSurfaceCommandKind.ConversationSummary, ParseConversationSummaryOptions);

    private static CliCommandParseResult ParseGitDiff(string[] args)
        => ParseRuntimeSurfaceCommand(args, RuntimeSurfaceCommandKind.GitDiffToRemote, ParseGitDiffOptions);

    private static CliCommandParseResult ParseFuzzyFileSearch(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("fuzzy-file-search 需要子命令：search/start/update/stop");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "search" => ParseFuzzyFileSearchVerb(args.Skip(1).ToArray(), FuzzyFileSearchCommandKind.Search, ParseFuzzyFileSearchSearchOptions),
            "start" => ParseFuzzyFileSearchVerb(args.Skip(1).ToArray(), FuzzyFileSearchCommandKind.Start, ParseFuzzyFileSearchStartOptions),
            "update" => ParseFuzzyFileSearchVerb(args.Skip(1).ToArray(), FuzzyFileSearchCommandKind.Update, ParseFuzzyFileSearchUpdateOptions),
            "stop" => ParseFuzzyFileSearchVerb(args.Skip(1).ToArray(), FuzzyFileSearchCommandKind.Stop, ParseFuzzyFileSearchStopOptions),
            _ => CliCommandParseResult.Failure($"不支持的 fuzzy-file-search 子命令：{verb}"),
        };
    }

    private static CliCommandParseResult ParseCollaborationMode(string[] args)
        => ParseRuntimeSurfaceVerb(args, "mode", "list", RuntimeSurfaceCommandKind.CollaborationModeList, ParseCollaborationModeListOptions);

    private static CliCommandParseResult ParseReview(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "start", StringComparison.OrdinalIgnoreCase))
        {
            return ParseRuntimeSurfaceVerb(args, "review", "start", RuntimeSurfaceCommandKind.ReviewStart, ParseReviewStartOptions);
        }

        return ParseExecReview(args);
    }

    private static CliCommandParseResult ParseFeedback(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("feedback \u9700\u8981\u5b50\u547d\u4ee4\uff1aupload");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "upload" => ParseFeedbackVerb(args.Skip(1).ToArray(), FeedbackCommandKind.Upload),
            _ => CliCommandParseResult.Failure($"\u4e0d\u652f\u6301\u7684 feedback \u5b50\u547d\u4ee4\uff1a{verb}"),
        };
    }

    private static CliCommandParseResult ParseWindowsSandbox(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("windows-sandbox \u9700\u8981\u5b50\u547d\u4ee4\uff1asetup-start");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "setup-start" => ParseWindowsSandboxVerb(args.Skip(1).ToArray(), WindowsSandboxCommandKind.SetupStart),
            "setupstart" => ParseWindowsSandboxVerb(args.Skip(1).ToArray(), WindowsSandboxCommandKind.SetupStart),
            _ => CliCommandParseResult.Failure($"\u4e0d\u652f\u6301\u7684 windows-sandbox \u5b50\u547d\u4ee4\uff1a{verb}"),
        };
    }

    private static CliCommandParseResult ParseRealtime(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("realtime \u9700\u8981\u5b50\u547d\u4ee4\uff1astart/append-text/append-audio/handoff-output/stop");
        }

        var verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "start" => ParseRealtimeVerb(args.Skip(1).ToArray(), RealtimeCommandKind.Start),
            "append-text" => ParseRealtimeVerb(args.Skip(1).ToArray(), RealtimeCommandKind.AppendText),
            "appendtext" => ParseRealtimeVerb(args.Skip(1).ToArray(), RealtimeCommandKind.AppendText),
            "append-audio" => ParseRealtimeVerb(args.Skip(1).ToArray(), RealtimeCommandKind.AppendAudio),
            "appendaudio" => ParseRealtimeVerb(args.Skip(1).ToArray(), RealtimeCommandKind.AppendAudio),
            "handoff-output" => ParseRealtimeVerb(args.Skip(1).ToArray(), RealtimeCommandKind.HandoffOutput),
            "handoffoutput" => ParseRealtimeVerb(args.Skip(1).ToArray(), RealtimeCommandKind.HandoffOutput),
            "stop" => ParseRealtimeVerb(args.Skip(1).ToArray(), RealtimeCommandKind.Stop),
            _ => CliCommandParseResult.Failure($"\u4e0d\u652f\u6301\u7684 realtime \u5b50\u547d\u4ee4\uff1a{verb}"),
        };
    }

    private static CliCommandParseResult ParseMcpVerb(
        string[] args,
        McpCommandKind commandKind,
        Func<string[], McpCommandOptions, string?> optionParser)
    {
        if (args.Any(IsHelp))
        {
            return CliCommandParseResult.Help();
        }

        var options = new McpCommandOptions
        {
            CommandKind = commandKind,
        };

        var error = optionParser(args, options);
        if (!string.IsNullOrWhiteSpace(error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateMcpOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseFeedbackVerb(string[] args, FeedbackCommandKind commandKind)
    {
        if (args.Any(IsHelp))
        {
            return CliCommandParseResult.Help();
        }

        var options = new FeedbackCommandOptions
        {
            CommandKind = commandKind,
        };

        var error = ParseFeedbackOptions(args, options);
        if (!string.IsNullOrWhiteSpace(error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateResumeOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateFeedbackOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseWindowsSandboxVerb(string[] args, WindowsSandboxCommandKind commandKind)
    {
        if (args.Any(IsHelp))
        {
            return CliCommandParseResult.Help();
        }

        var options = new WindowsSandboxCommandOptions
        {
            CommandKind = commandKind,
        };

        var error = ParseWindowsSandboxOptions(args, options);
        if (!string.IsNullOrWhiteSpace(error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateResumeOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateWindowsSandboxOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseRealtimeVerb(string[] args, RealtimeCommandKind commandKind)
    {
        if (args.Any(IsHelp))
        {
            return CliCommandParseResult.Help();
        }

        var options = new RealtimeCommandOptions
        {
            CommandKind = commandKind,
        };

        var error = ParseRealtimeOptions(args, options);
        if (!string.IsNullOrWhiteSpace(error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateResumeOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateRealtimeOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        return CliCommandParseResult.Success(options);
    }

    private static CliCommandParseResult ParseDebug(string[] args)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure("debug 需要子命令：clear-memories");
        }

        var verb = args[0].ToLowerInvariant();
        if (!string.Equals(verb, "clear-memories", StringComparison.Ordinal))
        {
            return CliCommandParseResult.Failure($"不支持的 debug 子命令：{verb}");
        }

        if (args.Skip(1).Any(IsHelp))
        {
            return CliCommandParseResult.Help();
        }

        var options = new DebugCommandOptions
        {
            CommandKind = DebugCommandKind.ClearMemories,
        };

        var error = ParseDebugOptions(args.Skip(1).ToArray(), options);
        if (!string.IsNullOrWhiteSpace(error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateResumeOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        return CliCommandParseResult.Success(options);
    }

    private static bool TryParseCompletionShell(string value, out CompletionShellKind shell)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "bash":
                shell = CompletionShellKind.Bash;
                return true;
            case "zsh":
                shell = CompletionShellKind.Zsh;
                return true;
            case "fish":
                shell = CompletionShellKind.Fish;
                return true;
            case "powershell":
            case "pwsh":
            case "ps":
                shell = CompletionShellKind.PowerShell;
                return true;
            default:
                shell = default;
                return false;
        }
    }

    private static CliCommandParseResult ParseRuntimeSurfaceVerb(
        string[] args,
        string groupName,
        string expectedVerb,
        RuntimeSurfaceCommandKind commandKind,
        Func<string[], RuntimeSurfaceCommandOptions, string?> optionParser)
    {
        if (args.Length == 0)
        {
            return CliCommandParseResult.Failure($"{groupName} 需要子命令：{expectedVerb}");
        }

        var verb = args[0].ToLowerInvariant();
        if (!string.Equals(verb, expectedVerb, StringComparison.OrdinalIgnoreCase))
        {
            return CliCommandParseResult.Failure($"不支持的 {groupName} 子命令：{verb}");
        }

        if (args.Skip(1).Any(static x => IsHelp(x)))
        {
            return CliCommandParseResult.Help();
        }

        var options = new RuntimeSurfaceCommandOptions
        {
            CommandKind = commandKind,
        };

        var error = optionParser(args.Skip(1).ToArray(), options);
        if (error is not null)
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateResumeOptions(options, out var validationError))
        {
            return CliCommandParseResult.Failure(validationError);
        }

        if (!ValidateRuntimeSurfaceOptions(options, out var surfaceError))
        {
            return CliCommandParseResult.Failure(surfaceError);
        }

        return CliCommandParseResult.Success(options);
    }


    private static CliCommandParseResult ParseRuntimeSurfaceVerb(
        string[] args,
        string groupName,
        string expectedVerb,
        string expectedSubVerb,
        RuntimeSurfaceCommandKind commandKind,
        Func<string[], RuntimeSurfaceCommandOptions, string?> optionParser)
    {
        if (args.Length < 2)
        {
            return CliCommandParseResult.Failure($"{groupName} 需要子命令：{expectedVerb} {expectedSubVerb}");
        }

        var verb = args[0].ToLowerInvariant();
        var subVerb = args[1].ToLowerInvariant();
        if (!string.Equals(verb, expectedVerb, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(subVerb, expectedSubVerb, StringComparison.OrdinalIgnoreCase))
        {
            return CliCommandParseResult.Failure($"不支持的 {groupName} 子命令：{verb} {subVerb}");
        }

        if (args.Skip(2).Any(static x => IsHelp(x)))
        {
            return CliCommandParseResult.Help();
        }

        var options = new RuntimeSurfaceCommandOptions
        {
            CommandKind = commandKind,
        };

        var error = optionParser(args.Skip(2).ToArray(), options);
        if (error is not null)
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateResumeOptions(options, out var validationError))
        {
            return CliCommandParseResult.Failure(validationError);
        }

        if (!ValidateRuntimeSurfaceOptions(options, out var surfaceError))
        {
            return CliCommandParseResult.Failure(surfaceError);
        }

        return CliCommandParseResult.Success(options);
    }
        private static CliCommandParseResult ParseRuntimeSurfaceCommand(
        string[] args,
        RuntimeSurfaceCommandKind commandKind,
        Func<string[], RuntimeSurfaceCommandOptions, string?> optionParser)
    {
        if (args.Any(IsHelp))
        {
            return CliCommandParseResult.Help();
        }

        var options = new RuntimeSurfaceCommandOptions
        {
            CommandKind = commandKind,
        };

        var error = optionParser(args, options);
        if (!string.IsNullOrWhiteSpace(error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateResumeOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateRuntimeSurfaceOptions(options, out error))
        {
            return CliCommandParseResult.Failure(error);
        }

        return CliCommandParseResult.Success(options);
    }

    private static string? ParseModelListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowLimit: true, allowCursor: true, allowIncludeHidden: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseModelCatalogOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowLimit: true, allowIncludeHidden: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseToolCatalogOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowIncludeHidden: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseToolConfigExportOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        options.IncludeHidden = true;
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowToolConfigOutputPath: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseModelResolveOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(
                     args,
                     options,
                     allowProviderKey: true,
                     allowModelKey: true,
                     allowReasoningEffort: true,
                     allowReasoningSummary: true,
                     allowVerbosity: true,
                     allowPreferWebsocketTransport: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseSkillsListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowForceReload: true, allowExtraRoot: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseSkillsEnableOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        options.Enabled = true;
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowPath: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseSkillsDisableOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        options.Enabled = false;
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowPath: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseSkillsRemoteListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowHazelnutScope: true, allowProductSurface: true, allowRemoteEnabled: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseSkillsRemoteExportOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowHazelnutId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParsePluginListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowForceRemoteSync: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParsePluginInstallOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowMarketplacePath: true, allowPluginName: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParsePluginReadOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowMarketplacePath: true, allowPluginName: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParsePluginUninstallOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowPluginId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseAppListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowLimit: true, allowCursor: true, allowThreadId: true, allowForceRefetch: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseSessionSnapshotOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseSessionOverviewOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowSessionId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseConversationReadOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowThreadId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseSessionListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowCollaborationSpaceId: true, allowIncludeClosed: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseGovernanceApprovalQueueOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowParticipantId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseGovernanceUserInputListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowParticipantId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseCollaborationOverviewOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowCollaborationSpaceId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseCollaborationCreateOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(
                     args,
                     options,
                     allowCollaborationSpaceId: true,
                     allowCollaborationSpaceKey: true,
                     allowDisplayName: true,
                     allowPurpose: true,
                     allowDefaultWorkspace: true,
                     allowDefaultExecutionProfile: true,
                     allowPolicyKey: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseCollaborationConfigureOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(
                     args,
                     options,
                     allowCollaborationSpaceId: true,
                     allowDisplayName: true,
                     allowPurpose: true,
                     allowDefaultWorkspace: true,
                     allowDefaultExecutionProfile: true,
                     allowPolicyKey: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseCollaborationArchiveOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowCollaborationSpaceId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseCollaborationReadOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowCollaborationSpaceId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseCollaborationListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowIncludeArchived: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseParticipantBindSessionOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowSessionId: true, allowParticipantId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseParticipantBindWorkflowOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowWorkflowId: true, allowParticipantId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseParticipantUpdateRoleOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowParticipantId: true, allowRole: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseParticipantReadOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowParticipantId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseParticipantViewOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowParticipantId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseParticipantListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowCollaborationSpaceId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseArtifactReadOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowArtifactId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseArtifactListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowCollaborationSpaceId: true, allowParticipantId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseDiagnosticsTraceOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowTraceId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseDiagnosticsAttemptListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowExecutionId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseIdentityAccountOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowAccountId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseIdentityDevicesOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowAccountId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseMemoryProvidersOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(
                     args,
                     options,
                     allowMemoryScopeKind: true,
                     allowPayloadJson: true,
                     allowPayloadFile: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseMemorySpacesOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowMemoryScopeKind: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseMemoryPayloadOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowPayloadJson: true, allowPayloadFile: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseMemoryOverlayOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowMemorySpaceId: true, allowCollaborationSpaceId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseWorkflowBoardOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowWorkflowId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseWorkflowCreateOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateWorkflowRuntimeOptionFailures(
                     args,
                     options,
                     allowWorkflowId: true,
                     allowCollaborationSpaceId: true,
                     allowDisplayName: true,
                     allowThreadId: true,
                     allowParticipantId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseWorkflowPublishPlanOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateWorkflowRuntimeOptionFailures(
                     args,
                     options,
                     allowWorkflowId: true,
                     allowTitle: true,
                     allowStepsJson: true,
                     allowStepsFile: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseWorkflowCreateTaskOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateWorkflowRuntimeOptionFailures(
                     args,
                     options,
                     allowWorkflowId: true,
                     allowTaskId: true,
                     allowTitle: true,
                     allowStatus: true,
                     allowParticipantId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseWorkflowUpdateTaskStateOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateWorkflowRuntimeOptionFailures(
                     args,
                     options,
                     allowTaskId: true,
                     allowStatus: true,
                     allowParticipantId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseWorkflowTaskBoardOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowWorkflowId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseWorkflowPlanOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowWorkflowId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseAgentRosterOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowWorkflowId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseAgentListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(
                     args,
                     options,
                     allowLimit: true,
                     allowCursor: true,
                     allowIncludePrimaryThreads: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseAgentTeamOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowTeamId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseAgentThreadRegisterOptions(string[] args, RuntimeSurfaceCommandOptions options)
        => ParseAgentRuntimeSurfaceOptions(args, options);

    private static string? ParseAgentJobCreateOptions(string[] args, RuntimeSurfaceCommandOptions options)
        => ParseAgentRuntimeSurfaceOptions(args, options);

    private static string? ParseAgentJobDispatchOptions(string[] args, RuntimeSurfaceCommandOptions options)
        => ParseAgentRuntimeSurfaceOptions(args, options);

    private static string? ParseAgentJobItemReportOptions(string[] args, RuntimeSurfaceCommandOptions options)
        => ParseAgentRuntimeSurfaceOptions(args, options);

    private static string? ParseAgentJobReadOptions(string[] args, RuntimeSurfaceCommandOptions options)
        => ParseAgentRuntimeSurfaceOptions(args, options);

    private static string? ParseAgentRuntimeSurfaceOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            switch (arg)
            {
                case "--thread-id":
                    if (options.CommandKind == RuntimeSurfaceCommandKind.AgentJobDispatch)
                    {
                        var threadId = Normalize(value);
                        if (!string.IsNullOrWhiteSpace(threadId))
                        {
                            options.DispatchThreadIds.Add(threadId);
                        }
                    }
                    else
                    {
                        options.ThreadId = Normalize(value);
                    }

                    break;
                case "--agent-nickname":
                case "--nickname":
                    options.AgentNickname = Normalize(value);
                    break;
                case "--agent-role":
                case "--role":
                    options.AgentRole = Normalize(value);
                    break;
                case "--job-id":
                    options.JobId = Normalize(value);
                    break;
                case "--name":
                    options.Name = Normalize(value);
                    break;
                case "--instruction":
                    options.Instruction = Normalize(value);
                    break;
                case "--input-headers-json":
                    options.InputHeadersJson = Normalize(value);
                    break;
                case "--input-headers-file":
                    options.InputHeadersFilePath = NormalizePath(value);
                    break;
                case "--input-csv-path":
                    options.InputCsvPath = NormalizePath(value) ?? Normalize(value);
                    break;
                case "--output-csv-path":
                    options.OutputCsvPath = NormalizePath(value) ?? Normalize(value);
                    break;
                case "--auto-export":
                    if (!bool.TryParse(value, out var autoExport))
                    {
                        return "--auto-export 只能是 true 或 false。";
                    }

                    options.AutoExport = autoExport;
                    break;
                case "--output-schema-json":
                    options.OutputSchemaJson = Normalize(value);
                    break;
                case "--output-schema-file":
                    options.OutputSchemaFilePath = NormalizePath(value);
                    break;
                case "--items-json":
                    options.ItemsJson = Normalize(value);
                    break;
                case "--items-file":
                    options.ItemsFilePath = NormalizePath(value);
                    break;
                case "--item-id":
                    options.ItemId = Normalize(value);
                    break;
                case "--status":
                    options.Status = Normalize(value);
                    break;
                case "--result-json":
                    options.ResultJson = Normalize(value);
                    break;
                case "--result-file":
                    options.ResultFilePath = NormalizePath(value);
                    break;
                case "--last-error":
                    options.LastError = Normalize(value);
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return error;
                    }

                    break;
            }
        }

        return null;
    }

    private static string? ParseConfigReadOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowIncludeLayers: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseConfigRequirementsReadOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseConfigValueWriteOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(
                     args,
                     options,
                     allowKeyPath: true,
                     allowValueJson: true,
                     allowValueFile: true,
                     allowFilePath: true,
                     allowExpectedVersion: true,
                     allowMergeStrategy: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseConfigBatchWriteOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(
                     args,
                     options,
                     allowItemsJson: true,
                     allowItemsFile: true,
                     allowFilePath: true,
                     allowExpectedVersion: true,
                     allowMergeStrategy: true,
                     allowReloadUserConfig: true))
        {
            return failure;
        }

        return null;
    }

    private static CliCommandParseResult ParseCommandExecVerb(string[] args, CommandExecCommandKind commandKind)
    {
        if (args.Any(static x => IsHelp(x)))
        {
            return CliCommandParseResult.Help();
        }

        var options = new CommandExecCommandOptions
        {
            CommandKind = commandKind,
        };

        var error = ParseCommandExecOptions(args, options);
        if (error is not null)
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateResumeOptions(options, out var validationError))
        {
            return CliCommandParseResult.Failure(validationError);
        }

        if (!ValidateCommandExecOptions(options, out var commandError))
        {
            return CliCommandParseResult.Failure(commandError);
        }

        return CliCommandParseResult.Success(options);
    }

    private static string? ParseCommandExecOptions(string[] args, CommandExecCommandOptions options)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            switch (arg)
            {
                case "--command":
                    options.CommandText = Normalize(value);
                    break;
                case "--argv-json":
                    options.CommandArgsJson = Normalize(value);
                    break;
                case "--argv-file":
                    options.CommandArgsFilePath = NormalizePath(value);
                    break;
                case "--process-id":
                    options.ProcessId = Normalize(value);
                    break;
                case "--rows":
                    if (!int.TryParse(value, out var rows) || rows <= 0)
                    {
                        return "--rows 必须是大于 0 的整数。";
                    }

                    options.Rows = rows;
                    break;
                case "--cols":
                    if (!int.TryParse(value, out var cols) || cols <= 0)
                    {
                        return "--cols 必须是大于 0 的整数。";
                    }

                    options.Cols = cols;
                    break;
                case "--timeout-ms":
                    if (!int.TryParse(value, out var timeoutMs) || timeoutMs < 0)
                    {
                        return "--timeout-ms 必须是大于等于 0 的整数。";
                    }

                    options.TimeoutMs = timeoutMs;
                    break;
                case "--output-bytes-cap":
                    if (!int.TryParse(value, out var outputBytesCap) || outputBytesCap < 0)
                    {
                        return "--output-bytes-cap 必须是大于等于 0 的整数。";
                    }

                    options.OutputBytesCap = outputBytesCap;
                    break;
                case "--thread-id":
                    options.ThreadId = Normalize(value);
                    break;
                case "--turn-id":
                    options.TurnId = Normalize(value);
                    break;
                case "--item-id":
                    options.ItemId = Normalize(value);
                    break;
                case "--approval-policy":
                    options.ApprovalPolicy = Normalize(value);
                    break;
                case "--env-json":
                    options.EnvJson = Normalize(value);
                    break;
                case "--env-file":
                    options.EnvFilePath = NormalizePath(value);
                    break;
                case "--sandbox-json":
                    options.SandboxJson = Normalize(value);
                    break;
                case "--sandbox-file":
                    options.SandboxFilePath = NormalizePath(value);
                    break;
                case "--text":
                    options.InputText = value;
                    break;
                case "--stdin-file":
                    options.InputFilePath = NormalizePath(value);
                    break;
                case "--base64":
                    options.InputBase64 = Normalize(value);
                    break;
                case "--tty":
                    options.Tty = true;
                    break;
                case "--stream-stdin":
                    options.StreamStdin = true;
                    break;
                case "--stream-stdout-stderr":
                    options.StreamStdoutStderr = true;
                    break;
                case "--background":
                    options.Background = true;
                    break;
                case "--disable-timeout":
                    options.DisableTimeout = true;
                    break;
                case "--disable-output-cap":
                    options.DisableOutputCap = true;
                    break;
                case "--approved":
                    options.Approved = true;
                    break;
                case "--login":
                    options.Login = true;
                    break;
                case "--no-login":
                    options.Login = false;
                    break;
                case "--close-stdin":
                    options.CloseStdin = true;
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return error;
                    }

                    break;
            }
        }

        return null;
    }

    private static string? ParseFeatureListOptions(string[] args, RuntimeSurfaceCommandOptions options)
        => ParseFeatureCommandOptions(args, options, requireFeatureName: false);

    private static string? ParseFeatureEnableOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        options.Enabled = true;
        return ParseFeatureCommandOptions(args, options, requireFeatureName: true);
    }

    private static string? ParseFeatureDisableOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        options.Enabled = false;
        return ParseFeatureCommandOptions(args, options, requireFeatureName: true);
    }

    private static string? ParseCodeModeOptions(string[] args, CodeModeCommandOptions options)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            switch (arg)
            {
                case "--thread-id":
                    options.ThreadId = Normalize(value);
                    break;
                case "--input":
                    options.Input = value;
                    break;
                case "--input-file":
                    options.InputFilePath = NormalizePath(value);
                    break;
                case "--yield-time-ms":
                    if (!int.TryParse(value, out var yieldTimeMs) || yieldTimeMs <= 0)
                    {
                        return "--yield-time-ms 必须是大于 0 的整数。";
                    }

                    options.YieldTimeMs = yieldTimeMs;
                    break;
                case "--max-output-tokens":
                    if (!int.TryParse(value, out var maxOutputTokens) || maxOutputTokens <= 0)
                    {
                        return "--max-output-tokens 必须是大于 0 的整数。";
                    }

                    options.MaxOutputTokens = maxOutputTokens;
                    break;
                case "--cell-id":
                    options.CellId = Normalize(value);
                    break;
                case "--max-tokens":
                    if (!int.TryParse(value, out var maxTokens) || maxTokens <= 0)
                    {
                        return "--max-tokens 必须是大于 0 的整数。";
                    }

                    options.MaxTokens = maxTokens;
                    break;
                case "--terminate":
                    options.Terminate = true;
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return error;
                    }

                    break;
            }
        }

        return null;
    }

    private static string? ParseExecOptions(string[] args, ExecCommandOptions options)
    {
        string? firstPositional = null;
        string? secondPositional = null;
        string? promptFromOption = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "-", StringComparison.Ordinal)
                || !arg.StartsWith("-", StringComparison.Ordinal))
            {
                if (firstPositional is null)
                {
                    firstPositional = arg;
                    continue;
                }

                if (secondPositional is null)
                {
                    secondPositional = arg;
                    continue;
                }

                return "exec 最多只接受两个位置参数。";
            }

            switch (arg)
            {
                case "--json":
                    options.OutputJson = true;
                    continue;
                case "--ephemeral":
                    options.Ephemeral = true;
                    continue;
                case "--full-auto":
                    options.FullAuto = true;
                    options.RuntimeSandboxMode = "workspace-write";
                    options.RuntimeApprovalPolicy = "never";
                    continue;
                case "--dangerously-bypass-approvals-and-sandbox":
                case "--yolo":
                    options.DangerouslyBypassApprovalsAndSandbox = true;
                    options.RuntimeSandboxMode = "danger-full-access";
                    options.RuntimeApprovalPolicy = "never";
                    continue;
                case "--skip-git-repo-check":
                    options.SkipGitRepoCheck = true;
                    continue;
                case "--last" when options.CommandKind == ExecCommandKind.Resume:
                    options.UseLast = true;
                    continue;
                case "--all" when options.CommandKind == ExecCommandKind.Resume:
                    options.ShowAll = true;
                    continue;
                case "--uncommitted" when options.CommandKind == ExecCommandKind.Review:
                    options.ReviewUncommitted = true;
                    continue;
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            switch (arg)
            {
                case "--message":
                    promptFromOption = value;
                    break;
                case "--image":
                case "-i":
                    if (!TryAppendExecImagePaths(options, value, out error))
                    {
                        return error;
                    }

                    break;
                case "--model":
                case "-m":
                    options.RuntimeModel = Normalize(value);
                    break;
                case "--sandbox":
                case "-s":
                    options.RuntimeSandboxMode = Normalize(value);
                    break;
                case "--profile":
                case "-p":
                    options.ProfileName = Normalize(value);
                    break;
                case "--cd":
                case "-C":
                    options.WorkingDirectory = NormalizePath(value) ?? Environment.CurrentDirectory;
                    break;
                case "--output-last-message":
                case "-o":
                    options.OutputLastMessageFilePath = NormalizePath(value);
                    break;
                case "--add-dir":
                    var additionalDirectory = NormalizePath(value) ?? Normalize(value);
                    if (string.IsNullOrWhiteSpace(additionalDirectory))
                    {
                        return "--add-dir 不能为空。";
                    }

                    options.AdditionalWritableDirectories.Add(additionalDirectory);
                    break;
                case "--output-schema":
                    options.OutputSchemaFilePath = NormalizePath(value);
                    break;
                case "--base" when options.CommandKind == ExecCommandKind.Review:
                    options.ReviewBaseBranch = Normalize(value);
                    break;
                case "--commit" when options.CommandKind == ExecCommandKind.Review:
                    options.ReviewCommit = Normalize(value);
                    break;
                case "--title" when options.CommandKind == ExecCommandKind.Review:
                    options.ReviewCommitTitle = value;
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return error;
                    }

                    break;
            }
        }

        switch (options.CommandKind)
        {
            case ExecCommandKind.UserTurn:
                if (secondPositional is not null)
                {
                    return "exec 只接受一个 prompt 位置参数。";
                }

                if (!string.IsNullOrWhiteSpace(promptFromOption) && firstPositional is not null)
                {
                    return "prompt 不能同时通过位置参数和 --message 提供。";
                }

                options.Prompt = promptFromOption ?? firstPositional;
                break;
            case ExecCommandKind.Resume:
                if (!string.IsNullOrWhiteSpace(promptFromOption) && secondPositional is not null)
                {
                    return "resume prompt 不能同时通过位置参数和 --message 提供。";
                }

                if (options.UseLast && string.IsNullOrWhiteSpace(promptFromOption) && secondPositional is null)
                {
                    options.Prompt = firstPositional;
                }
                else
                {
                    options.ResumeTarget = firstPositional;
                    options.Prompt = promptFromOption ?? secondPositional;
                }

                break;
            case ExecCommandKind.Review:
                if (secondPositional is not null)
                {
                    return "exec review 只接受一个自定义 instructions 位置参数。";
                }

                if (!string.IsNullOrWhiteSpace(promptFromOption) && firstPositional is not null)
                {
                    return "review instructions 不能同时通过位置参数和 --message 提供。";
                }

                options.ReviewPrompt = promptFromOption ?? firstPositional;
                break;
        }

        ApplyExecDerivedRuntimeDefaults(options);
        return null;
    }

    private static void ApplyExecDerivedRuntimeDefaults(ExecCommandOptions options)
    {
        if (options.DangerouslyBypassApprovalsAndSandbox)
        {
            options.RuntimeSandboxMode = "danger-full-access";
            options.RuntimeApprovalPolicy = "never";
            return;
        }

        if (options.FullAuto)
        {
            options.RuntimeSandboxMode = "workspace-write";
            options.RuntimeApprovalPolicy = "never";
        }
    }

    private static void ApplyChatDerivedRuntimeDefaults(ChatCommandOptions options)
    {
        if (options.DangerouslyBypassApprovalsAndSandbox)
        {
            options.RuntimeSandboxMode = "danger-full-access";
            options.RuntimeApprovalPolicy = "never";
            return;
        }

        if (options.FullAuto)
        {
            options.RuntimeSandboxMode = "workspace-write";
            options.RuntimeApprovalPolicy = "on-request";
        }
    }

    private static bool TryAppendExecImagePaths(ExecCommandOptions options, string value, out string error)
        => TryAppendImagePaths(options.ImagePaths, value, out error);

    private static bool TryAppendImagePaths(ICollection<string> imagePaths, string value, out string error)
    {
        foreach (var segment in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = NormalizePath(segment);
            if (normalized is null)
            {
                error = "--image 需要文件路径。";
                return false;
            }

            imagePaths.Add(normalized);
        }

        error = string.Empty;
        return true;
    }

    private static string? ParseFeedbackOptions(string[] args, FeedbackCommandOptions options)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            switch (arg)
            {
                case "--classification":
                    options.Classification = Normalize(value);
                    break;
                case "--include-logs":
                    options.IncludeLogs = true;
                    break;
                case "--thread-id":
                    options.ThreadId = Normalize(value);
                    break;
                case "--reason":
                    options.Reason = Normalize(value);
                    break;
                case "--extra-log-file":
                    var extraLogFile = NormalizePath(value);
                    if (extraLogFile is null)
                    {
                        return "--extra-log-file \u9700\u8981\u6587\u4ef6\u8def\u5f84\u3002";
                    }

                    options.ExtraLogFiles.Add(extraLogFile);
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return error;
                    }

                    break;
            }
        }

        return null;
    }

    private static string? ParseDebugOptions(string[] args, DebugCommandOptions options)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                return $"不支持的 debug 参数：{arg}";
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            if (!TryApplyCommonOption(arg, value, options, out error))
            {
                return error;
            }
        }

        return null;
    }

    private static string? ParseWindowsSandboxOptions(string[] args, WindowsSandboxCommandOptions options)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            switch (arg)
            {
                case "--mode":
                    options.Mode = Normalize(value)?.ToLowerInvariant();
                    break;
                case "--cwd":
                    var sandboxCwd = Normalize(value);
                    if (sandboxCwd is null)
                    {
                        return "--cwd \u9700\u8981\u8def\u5f84\u3002";
                    }

                    var expandedSandboxCwd = Environment.ExpandEnvironmentVariables(sandboxCwd);
                    if (!Path.IsPathRooted(expandedSandboxCwd))
                    {
                        return "windows-sandbox setup-start \u7684 --cwd \u5fc5\u987b\u662f\u7edd\u5bf9\u8def\u5f84\u3002";
                    }

                    options.SandboxCwd = Path.GetFullPath(expandedSandboxCwd);
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return error;
                    }

                    break;
            }
        }

        return null;
    }

    private static string? ParseRealtimeOptions(string[] args, RealtimeCommandOptions options)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            switch (arg)
            {
                case "--thread-id":
                    options.ThreadId = Normalize(value);
                    break;
                case "--session-id":
                    options.SessionId = Normalize(value);
                    break;
                case "--prompt":
                    options.Prompt = Normalize(value);
                    break;
                case "--text":
                    options.Text = Normalize(value);
                    break;
                case "--handoff-id":
                case "--call-id":
                    options.HandoffId = Normalize(value);
                    break;
                case "--output":
                    options.Output = Normalize(value);
                    break;
                case "--audio-json":
                    options.AudioJson = Normalize(value);
                    break;
                case "--audio-file":
                    options.AudioFilePath = NormalizePath(value);
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return error;
                    }

                    break;
            }
        }

        return null;
    }

    private static string? ParseExperimentalFeatureListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowLimit: true, allowCursor: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseAppServerOptions(string[] args, AppServerCommandOptions options)
    {
        var argumentStart = 0;
        if (args.Length > 0 && !args[0].StartsWith("-", StringComparison.Ordinal))
        {
            var subcommand = Normalize(args[0])?.ToLowerInvariant();
            switch (subcommand)
            {
                case "generate-ts":
                    options.CommandKind = AppServerCommandKind.GenerateTs;
                    argumentStart = 1;
                    break;
                case "generate-json-schema":
                    options.CommandKind = AppServerCommandKind.GenerateJsonSchema;
                    argumentStart = 1;
                    break;
                default:
                    return $"不支持的 app-server 子命令：{args[0]}";
            }
        }

        return options.CommandKind switch
        {
            AppServerCommandKind.RunServer => ParseAppServerRunOptions(args, argumentStart, options),
            AppServerCommandKind.GenerateTs => ParseAppServerGenerateTsOptions(args, argumentStart, options),
            AppServerCommandKind.GenerateJsonSchema => ParseAppServerGenerateJsonSchemaOptions(args, argumentStart, options),
            _ => "不支持的 app-server 子命令。",
        };
    }

    private static string? ParseAppServerRunOptions(string[] args, int startIndex, AppServerCommandOptions options)
    {
        for (var index = startIndex; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                return $"不支持的 app-server 参数：{arg}";
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            switch (arg)
            {
                case "--listen":
                    options.ListenUrl = Normalize(value) ?? options.ListenUrl;
                    break;
                default:
                    if (!TryApplyAppServerOption(arg, value, options, out error))
                    {
                        return error;
                    }

                    break;
            }
        }

        return null;
    }

    private static string? ParseAppServerGenerateTsOptions(string[] args, int startIndex, AppServerCommandOptions options)
    {
        for (var index = startIndex; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                return $"不支持的 app-server generate-ts 参数：{arg}";
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            switch (arg)
            {
                case "-o":
                case "--out":
                    options.OutDirectory = NormalizePath(value);
                    break;
                case "-p":
                case "--prettier":
                    options.PrettierPath = NormalizePath(value);
                    break;
                case "--experimental":
                    options.Experimental = true;
                    break;
                default:
                    if (!TryApplyAppServerOption(arg, value, options, out error))
                    {
                        return error;
                    }

                    break;
            }
        }

        return null;
    }

    private static string? ParseAppServerGenerateJsonSchemaOptions(string[] args, int startIndex, AppServerCommandOptions options)
    {
        for (var index = startIndex; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                return $"不支持的 app-server generate-json-schema 参数：{arg}";
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            switch (arg)
            {
                case "-o":
                case "--out":
                    options.OutDirectory = NormalizePath(value);
                    break;
                case "--experimental":
                    options.Experimental = true;
                    break;
                default:
                    if (!TryApplyAppServerOption(arg, value, options, out error))
                    {
                        return error;
                    }

                    break;
            }
        }

        return null;
    }

    private static string? ParseMcpListOptions(string[] args, McpCommandOptions options)
    {
        foreach (var failure in EnumerateMcpCommonOptionFailures(args, options))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseMcpGetOptions(string[] args, McpCommandOptions options)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(options.Name))
                {
                    return "mcp get 只能提供一个 server 名称。";
                }

                options.Name = Normalize(arg);
                continue;
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            if (!TryApplyCommonOption(arg, value, options, out error))
            {
                return error;
            }
        }

        return null;
    }

    private static string? ParseMcpAddOptions(string[] args, McpCommandOptions options)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return "mcp add 的 `--` 后必须提供启动命令。";
                }

                for (var commandIndex = index + 1; commandIndex < args.Length; commandIndex++)
                {
                    options.Command.Add(args[commandIndex]);
                }

                return null;
            }

            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(options.Name))
                {
                    return $"不支持的 mcp add 参数：{arg}";
                }

                options.Name = Normalize(arg);
                continue;
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            switch (arg)
            {
                case "--url":
                    options.Url = Normalize(value);
                    break;
                case "--bearer-token-env-var":
                    options.BearerTokenEnvVar = Normalize(value);
                    break;
                case "--env":
                    var separatorIndex = value.IndexOf('=', StringComparison.Ordinal);
                    if (separatorIndex <= 0)
                    {
                        return "--env 必须是 KEY=VALUE。";
                    }

                    var key = value[..separatorIndex].Trim();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        return "--env 的 KEY 不能为空。";
                    }

                    options.EnvironmentVariables[key] = value[(separatorIndex + 1)..];
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return error;
                    }

                    break;
            }
        }

        return null;
    }

    private static string? ParseMcpRemoveOptions(string[] args, McpCommandOptions options)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(options.Name))
                {
                    return "mcp remove 只能提供一个 server 名称。";
                }

                options.Name = Normalize(arg);
                continue;
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            if (!TryApplyCommonOption(arg, value, options, out error))
            {
                return error;
            }
        }

        return null;
    }

    private static string? ParseMcpServerListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowLimit: true, allowCursor: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseMcpServerReloadOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseMcpServerOauthLoginOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowMcpServerName: true, allowTimeoutSecs: true, allowWaitForCompletion: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseConversationSummaryOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (IsHelp(arg))
            {
                return null;
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            switch (arg)
            {
                case "--thread-id":
                    options.ThreadId = Normalize(value);
                    break;
                case "--rollout-path":
                    options.RolloutPath = NormalizePath(value) ?? Normalize(value);
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return error;
                    }

                    break;
            }
        }

        return null;
    }

    private static string? ParseGitDiffOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options, allowThreadId: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseReviewStartOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (IsHelp(arg))
            {
                return null;
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            switch (arg)
            {
                case "--thread-id":
                    options.ThreadId = Normalize(value);
                    break;
                case "--target":
                case "--target-type":
                    options.ReviewTargetType = NormalizeReviewTargetType(value) ?? Normalize(value);
                    break;
                case "--delivery":
                    options.Delivery = NormalizeReviewDelivery(value) ?? Normalize(value);
                    break;
                case "--branch":
                    options.ReviewBranch = Normalize(value);
                    break;
                case "--sha":
                    options.ReviewSha = Normalize(value);
                    break;
                case "--title":
                    options.ReviewTitle = Normalize(value);
                    break;
                case "--instructions":
                case "--prompt":
                    options.ReviewInstructions = Normalize(value);
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        return error;
                    }

                    break;
            }
        }

        return null;
    }

    private static string? ParseCollaborationModeListOptions(string[] args, RuntimeSurfaceCommandOptions options)
    {
        foreach (var failure in EnumerateRuntimeSurfaceOptionFailures(args, options))
        {
            return failure;
        }

        return null;
    }

    private static CliCommandParseResult ParseFuzzyFileSearchVerb(
        string[] args,
        FuzzyFileSearchCommandKind commandKind,
        Func<string[], FuzzyFileSearchCommandOptions, string?> optionParser)
    {
        if (args.Any(static x => IsHelp(x)))
        {
            return CliCommandParseResult.Help();
        }

        var options = new FuzzyFileSearchCommandOptions
        {
            CommandKind = commandKind,
        };

        var error = optionParser(args, options);
        if (error is not null)
        {
            return CliCommandParseResult.Failure(error);
        }

        if (!ValidateResumeOptions(options, out var resumeError))
        {
            return CliCommandParseResult.Failure(resumeError);
        }

        if (!ValidateFuzzyFileSearchOptions(options, out var validationError))
        {
            return CliCommandParseResult.Failure(validationError);
        }

        return CliCommandParseResult.Success(options);
    }

    private static string? ParseFuzzyFileSearchSearchOptions(string[] args, FuzzyFileSearchCommandOptions options)
    {
        foreach (var failure in EnumerateFuzzyFileSearchOptionFailures(args, options, allowQuery: true, allowLimit: true, allowRoot: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseFuzzyFileSearchStartOptions(string[] args, FuzzyFileSearchCommandOptions options)
    {
        foreach (var failure in EnumerateFuzzyFileSearchOptionFailures(args, options, allowSessionId: true, allowRoot: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseFuzzyFileSearchUpdateOptions(string[] args, FuzzyFileSearchCommandOptions options)
    {
        foreach (var failure in EnumerateFuzzyFileSearchOptionFailures(args, options, allowSessionId: true, allowQuery: true, allowRoot: true))
        {
            return failure;
        }

        return null;
    }

    private static string? ParseFuzzyFileSearchStopOptions(string[] args, FuzzyFileSearchCommandOptions options)
    {
        foreach (var failure in EnumerateFuzzyFileSearchOptionFailures(args, options, allowSessionId: true))
        {
            return failure;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateFuzzyFileSearchOptionFailures(
        string[] args,
        FuzzyFileSearchCommandOptions options,
        bool allowSessionId = false,
        bool allowQuery = false,
        bool allowLimit = false,
        bool allowRoot = false)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                yield return error;
                yield break;
            }

            switch (arg)
            {
                case "--session-id" when allowSessionId:
                    options.SessionId = Normalize(value);
                    break;
                case "--query" when allowQuery:
                    options.Query = Normalize(value);
                    break;
                case "--limit" when allowLimit:
                    if (!int.TryParse(value, out var limit) || limit <= 0)
                    {
                        yield return "--limit 必须是大于 0 的整数。";
                        yield break;
                    }

                    options.Limit = limit;
                    break;
                case "--root" when allowRoot:
                    var root = NormalizePath(value);
                    if (root is null)
                    {
                        yield return "--root 不能为空。";
                        yield break;
                    }

                    options.Roots.Add(root);
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        yield return error;
                        yield break;
                    }
                    break;
            }
        }
    }

    private static IEnumerable<string> EnumerateRuntimeSurfaceOptionFailures(
        string[] args,
        RuntimeSurfaceCommandOptions options,
        bool allowLimit = false,
        bool allowCursor = false,
        bool allowSessionId = false,
        bool allowWorkflowId = false,
        bool allowTaskId = false,
        bool allowTeamId = false,
        bool allowParticipantId = false,
        bool allowArtifactId = false,
        bool allowAccountId = false,
        bool allowMemorySpaceId = false,
        bool allowMemoryScopeKind = false,
        bool allowPayloadJson = false,
        bool allowPayloadFile = false,
        bool allowTraceId = false,
        bool allowExecutionId = false,
        bool allowCollaborationSpaceId = false,
        bool allowCollaborationSpaceKey = false,
        bool allowDisplayName = false,
        bool allowTitle = false,
        bool allowPurpose = false,
        bool allowDefaultWorkspace = false,
        bool allowDefaultExecutionProfile = false,
        bool allowPolicyKey = false,
        bool allowRole = false,
        bool allowStatus = false,
        bool allowIncludeClosed = false,
        bool allowIncludeArchived = false,
        bool allowIncludeHidden = false,
        bool allowIncludePrimaryThreads = false,
        bool allowIncludeLayers = false,
        bool allowForceReload = false,
        bool allowForceRefetch = false,
        bool allowForceRemoteSync = false,
        bool allowThreadId = false,
        bool allowMarketplacePath = false,
        bool allowPluginName = false,
        bool allowPluginId = false,
        bool allowExtraRoot = false,
        bool allowPath = false,
        bool allowItemsJson = false,
        bool allowItemsFile = false,
        bool allowKeyPath = false,
        bool allowValueJson = false,
        bool allowValueFile = false,
        bool allowFilePath = false,
        bool allowExpectedVersion = false,
        bool allowMergeStrategy = false,
        bool allowReloadUserConfig = false,
        bool allowHazelnutScope = false,
        bool allowProductSurface = false,
        bool allowRemoteEnabled = false,
        bool allowHazelnutId = false,
        bool allowProviderKey = false,
        bool allowModelKey = false,
        bool allowReasoningEffort = false,
        bool allowReasoningSummary = false,
        bool allowVerbosity = false,
        bool allowPreferWebsocketTransport = false,
        bool allowMcpServerName = false,
        bool allowTimeoutSecs = false,
        bool allowWaitForCompletion = false,
        bool allowToolConfigOutputPath = false)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                yield return error;
                yield break;
            }

            switch (arg)
            {
                case "--limit" when allowLimit:
                    if (!int.TryParse(value, out var limit) || limit <= 0)
                    {
                        yield return "--limit 必须是大于 0 的整数。";
                        yield break;
                    }

                    options.Limit = limit;
                    break;
                case "--cursor" when allowCursor:
                    options.Cursor = Normalize(value);
                    break;
                case "--session-id" when allowSessionId:
                    options.SessionId = Normalize(value);
                    break;
                case "--workflow-id" when allowWorkflowId:
                    options.WorkflowId = Normalize(value);
                    break;
                case "--task-id" when allowTaskId:
                    options.TaskId = Normalize(value);
                    break;
                case "--team-id" when allowTeamId:
                    options.TeamId = Normalize(value);
                    break;
                case "--participant-id" when allowParticipantId:
                case "--requested-from-participant-id" when allowParticipantId:
                case "--produced-by-participant-id" when allowParticipantId:
                    options.ParticipantId = Normalize(value);
                    break;
                case "--artifact-id" when allowArtifactId:
                    options.ArtifactId = Normalize(value);
                    break;
                case "--account-id" when allowAccountId:
                    options.AccountId = Normalize(value);
                    break;
                case "--memory-space-id" when allowMemorySpaceId:
                    options.MemorySpaceId = Normalize(value);
                    break;
                case "--scope-kind" when allowMemoryScopeKind:
                case "--memory-scope-kind" when allowMemoryScopeKind:
                    if (!TryParseMemoryScopeKind(value, out var memoryScopeKind, out error))
                    {
                        yield return error ?? "--scope-kind 无效。";
                        yield break;
                    }

                    options.MemoryScopeKind = memoryScopeKind;
                    break;
                case "--payload-json" when allowPayloadJson:
                    options.PayloadJson = Normalize(value);
                    break;
                case "--payload-file" when allowPayloadFile:
                    options.PayloadFilePath = NormalizePath(value);
                    break;
                case "--trace-id" when allowTraceId:
                    options.TraceId = Normalize(value);
                    break;
                case "--execution-id" when allowExecutionId:
                    options.ExecutionId = Normalize(value);
                    break;
                case "--collaboration-space-id" when allowCollaborationSpaceId:
                case "--collaboration-id" when allowCollaborationSpaceId:
                case "--space-id" when allowCollaborationSpaceId:
                    options.CollaborationSpaceId = Normalize(value);
                    break;
                case "--key" when allowCollaborationSpaceKey:
                case "--space-key" when allowCollaborationSpaceKey:
                    options.CollaborationSpaceKey = Normalize(value);
                    break;
                case "--display-name" when allowDisplayName:
                    options.DisplayName = Normalize(value);
                    break;
                case "--title" when allowTitle:
                    options.Title = Normalize(value);
                    break;
                case "--purpose" when allowPurpose:
                    options.Purpose = Normalize(value);
                    break;
                case "--default-workspace" when allowDefaultWorkspace:
                    options.DefaultWorkspace = NormalizePath(value) ?? Normalize(value);
                    break;
                case "--default-execution-profile" when allowDefaultExecutionProfile:
                    options.DefaultExecutionProfile = Normalize(value);
                    break;
                case "--policy-key" when allowPolicyKey:
                    options.PolicyKey = Normalize(value);
                    break;
                case "--role" when allowRole:
                    options.Role = Normalize(value);
                    break;
                case "--status" when allowStatus:
                case "--state" when allowStatus:
                    options.Status = Normalize(value);
                    break;
                case "--include-closed" when allowIncludeClosed:
                    options.IncludeClosed = true;
                    break;
                case "--include-archived" when allowIncludeArchived:
                    options.IncludeArchived = true;
                    break;
                case "--include-hidden" when allowIncludeHidden:
                    options.IncludeHidden = true;
                    break;
                case "--include-primary-threads" when allowIncludePrimaryThreads:
                    options.IncludePrimaryThreads = true;
                    break;
                case "--include-layers" when allowIncludeLayers:
                    options.IncludeLayers = true;
                    break;
                case "--force-reload" when allowForceReload:
                    options.ForceReload = true;
                    break;
                case "--force-refetch" when allowForceRefetch:
                    options.ForceRefetch = true;
                    break;
                case "--force-remote-sync" when allowForceRemoteSync:
                    options.ForceRemoteSync = true;
                    break;
                case "--thread-id" when allowThreadId:
                    options.ThreadId = Normalize(value);
                    break;
                case "--marketplace-path" when allowMarketplacePath:
                    options.MarketplacePath = NormalizePath(value);
                    break;
                case "--plugin-name" when allowPluginName:
                    options.PluginName = Normalize(value);
                    break;
                case "--plugin-id" when allowPluginId:
                    options.PluginId = Normalize(value);
                    break;
                case "--extra-root" when allowExtraRoot:
                    var extraRoot = NormalizePath(value);
                    if (extraRoot is null || !Path.IsPathRooted(extraRoot))
                    {
                        yield return "--extra-root 必须是绝对路径。";
                        yield break;
                    }

                    options.ExtraRoots.Add(extraRoot);
                    break;
                case "--path" when allowPath:
                    options.SkillPath = NormalizePath(value) ?? Normalize(value);
                    break;
                case "--key" when allowKeyPath:
                case "--key-path" when allowKeyPath:
                    options.KeyPath = Normalize(value);
                    break;
                case "--value-json" when allowValueJson:
                    options.ConfigValueJson = Normalize(value);
                    break;
                case "--value-file" when allowValueFile:
                    options.ConfigValueFilePath = NormalizePath(value);
                    break;
                case "--file-path" when allowFilePath:
                    options.ConfigEditFilePath = NormalizePath(value);
                    break;
                case "--expected-version" when allowExpectedVersion:
                    options.ExpectedVersion = Normalize(value);
                    break;
                case "--merge-strategy" when allowMergeStrategy:
                    var mergeStrategy = Normalize(value)?.ToLowerInvariant();
                    if (mergeStrategy is not ("replace" or "upsert"))
                    {
                        yield return "--merge-strategy 只支持 replace 或 upsert。";
                        yield break;
                    }

                    options.MergeStrategy = mergeStrategy;
                    break;
                case "--reload-user-config" when allowReloadUserConfig:
                    options.ReloadUserConfig = true;
                    break;
                case "--items-json" when allowItemsJson:
                    options.BatchItemsJson = Normalize(value);
                    break;
                case "--items-file" when allowItemsFile:
                    options.BatchItemsFilePath = NormalizePath(value);
                    break;
                case "--hazelnut-scope" when allowHazelnutScope:
                    options.HazelnutScope = Normalize(value);
                    break;
                case "--product-surface" when allowProductSurface:
                    options.ProductSurface = Normalize(value);
                    break;
                case "--enabled" when allowRemoteEnabled:
                    if (!bool.TryParse(value, out var enabled))
                    {
                        yield return "--enabled 只能是 true 或 false。";
                        yield break;
                    }

                    options.RemoteEnabled = enabled;
                    break;
                case "--hazelnut-id" when allowHazelnutId:
                    options.HazelnutId = Normalize(value);
                    break;
                case "--provider-key" when allowProviderKey:
                    options.ProviderKey = Normalize(value);
                    break;
                case "--model-key" when allowModelKey:
                    options.ModelKey = Normalize(value);
                    break;
                case "--reasoning-effort" when allowReasoningEffort:
                    options.ReasoningEffort = Normalize(value);
                    break;
                case "--reasoning-summary" when allowReasoningSummary:
                    options.ReasoningSummary = Normalize(value);
                    break;
                case "--verbosity" when allowVerbosity:
                    options.Verbosity = Normalize(value);
                    break;
                case "--prefer-websocket-transport" when allowPreferWebsocketTransport:
                    options.PreferWebsocketTransport = true;
                    break;
                case "--out" when allowToolConfigOutputPath:
                case "-o" when allowToolConfigOutputPath:
                    options.ToolConfigOutputPath = NormalizePath(value) ?? Normalize(value);
                    break;
                case "--name" when allowMcpServerName:
                    options.McpServerName = Normalize(value);
                    break;
                case "--timeout-secs" when allowTimeoutSecs:
                    if (!long.TryParse(value, out var timeoutSecs) || timeoutSecs <= 0)
                    {
                        yield return "--timeout-secs 必须大于 0 秒。";
                        yield break;
                    }

                    options.TimeoutSecs = timeoutSecs;
                    break;
                default:
                    if (allowWaitForCompletion && string.Equals(arg, "--wait", StringComparison.OrdinalIgnoreCase))
                    {
                        options.WaitForCompletion = true;
                        break;
                    }
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        yield return error;
                        yield break;
                    }
                    break;
            }
        }
    }

    private static IEnumerable<string> EnumerateWorkflowRuntimeOptionFailures(
        string[] args,
        RuntimeSurfaceCommandOptions options,
        bool allowWorkflowId = false,
        bool allowTaskId = false,
        bool allowCollaborationSpaceId = false,
        bool allowDisplayName = false,
        bool allowTitle = false,
        bool allowThreadId = false,
        bool allowParticipantId = false,
        bool allowStatus = false,
        bool allowStepsJson = false,
        bool allowStepsFile = false)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                yield return error;
                yield break;
            }

            switch (arg)
            {
                case "--workflow-id" when allowWorkflowId:
                    options.WorkflowId = Normalize(value);
                    break;
                case "--task-id" when allowTaskId:
                    options.TaskId = Normalize(value);
                    break;
                case "--collaboration-space-id" when allowCollaborationSpaceId:
                case "--collaboration-id" when allowCollaborationSpaceId:
                case "--space-id" when allowCollaborationSpaceId:
                    options.CollaborationSpaceId = Normalize(value);
                    break;
                case "--display-name" when allowDisplayName:
                    options.DisplayName = Normalize(value);
                    break;
                case "--title" when allowTitle:
                    options.Title = Normalize(value);
                    break;
                case "--thread-id" when allowThreadId:
                    options.ThreadId = Normalize(value);
                    break;
                case "--participant-id" when allowParticipantId:
                    options.ParticipantId = Normalize(value);
                    break;
                case "--status" when allowStatus:
                case "--state" when allowStatus:
                    options.Status = Normalize(value);
                    break;
                case "--steps-json" when allowStepsJson:
                    options.ItemsJson = Normalize(value);
                    break;
                case "--steps-file" when allowStepsFile:
                    options.ItemsFilePath = NormalizePath(value);
                    break;
                default:
                    if (!TryApplyCommonOption(arg, value, options, out error))
                    {
                        yield return error;
                        yield break;
                    }

                    break;
            }
        }
    }

    private static bool TryParseMemoryScopeKind(string? raw, out MemoryScopeKind scopeKind, out string? error)
    {
        if (Enum.TryParse<MemoryScopeKind>(Normalize(raw), ignoreCase: true, out scopeKind))
        {
            error = null;
            return true;
        }

        error = "--scope-kind 必须是 user/workspace/team/session/agent/collaboration 之一。";
        return false;
    }

    private static IEnumerable<string> EnumerateMcpCommonOptionFailures(string[] args, McpCommandOptions options)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                yield return error;
                yield break;
            }

            if (!TryApplyCommonOption(arg, value, options, out error))
            {
                yield return error;
                yield break;
            }
        }
    }

    private static bool TryApplyCommonOption(string arg, string value, CliRuntimeCommandOptions options, out string error)
    {
        error = string.Empty;
        switch (arg)
        {
            case "--cwd":
                options.WorkingDirectory = NormalizePath(value) ?? Environment.CurrentDirectory;
                return true;
            case "--apphost-project":
                options.AppHostProjectPath = Normalize(value);
                return true;
            case "--config":
                if (LooksLikeConfigOverride(value))
                {
                    return TryApplyConfigOverride(options, value, out error);
                }

                options.ConfigFilePath = NormalizePath(value) ?? options.ConfigFilePath;
                return true;
            case "--config-file":
                options.ConfigFilePath = NormalizePath(value) ?? options.ConfigFilePath;
                return true;
            case "-c":
                return TryApplyConfigOverride(options, value, out error);
            case "--profile":
                options.ProfileName = Normalize(value);
                return true;
            case "--resume-thread-id":
                options.ResumeThreadId = Normalize(value);
                return true;
            case "--collaboration-mode":
                options.CollaborationMode = Normalize(value);
                return true;
            case "--web-search":
                options.WebSearchMode = Normalize(value);
                return true;
            case "--enable":
                return TryApplyFeatureOverride(options, value, enabled: true, out error);
            case "--disable":
                return TryApplyFeatureOverride(options, value, enabled: false, out error);
            case "--dynamic-tools-json":
                if (options.DynamicTools is not null)
                {
                    error = "--dynamic-tools-json 与 --dynamic-tools-file 不能重复或同时提供。";
                    return false;
                }

                if (!CliStructuredPayloadReader.TryReadTypedArrayPayload<ControlPlaneDynamicToolSpec>(
                        Normalize(value),
                        null,
                        "dynamic tools",
                        out var dynamicToolsFromJson,
                        out error))
                {
                    return false;
                }

                options.DynamicTools = dynamicToolsFromJson;
                return true;
            case "--dynamic-tools-file":
                if (options.DynamicTools is not null)
                {
                    error = "--dynamic-tools-json 与 --dynamic-tools-file 不能重复或同时提供。";
                    return false;
                }

                if (!CliStructuredPayloadReader.TryReadTypedArrayPayload<ControlPlaneDynamicToolSpec>(
                        null,
                        NormalizePath(value),
                        "dynamic tools",
                        out var dynamicToolsFromFile,
                        out error))
                {
                    return false;
                }

                options.DynamicTools = dynamicToolsFromFile;
                return true;
            default:
                if (string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase))
                {
                    options.OutputJson = true;
                    return true;
                }

                if (string.Equals(arg, "--resume-latest", StringComparison.OrdinalIgnoreCase))
                {
                    options.ResumeLatestThread = true;
                    return true;
                }

                if (string.Equals(arg, "--resume-latest-any-cwd", StringComparison.OrdinalIgnoreCase))
                {
                    options.ResumeLatestMatchCwd = false;
                    return true;
                }

                error = $"不支持的参数：{arg}";
                return false;
        }
    }

    private static bool TryApplyAppServerOption(string arg, string value, AppServerCommandOptions options, out string error)
    {
        error = string.Empty;
        switch (arg)
        {
            case "--cwd":
                options.WorkingDirectory = NormalizePath(value) ?? Environment.CurrentDirectory;
                return true;
            case "--apphost-project":
                options.AppHostProjectPath = Normalize(value);
                return true;
            case "--config":
                if (LooksLikeConfigOverride(value))
                {
                    return TryApplyConfigOverride(options, value, out error);
                }

                options.ConfigFilePath = NormalizePath(value) ?? options.ConfigFilePath;
                return true;
            case "--config-file":
                options.ConfigFilePath = NormalizePath(value) ?? options.ConfigFilePath;
                return true;
            case "-c":
                return TryApplyConfigOverride(options, value, out error);
            case "--analytics-default-enabled":
                options.AnalyticsDefaultEnabled = true;
                return true;
            default:
                error = $"不支持的参数：{arg}";
                return false;
        }
    }

    private static string? ParseFeatureCommandOptions(
        string[] args,
        RuntimeSurfaceCommandOptions options,
        bool requireFeatureName)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                if (!requireFeatureName)
                {
                    return $"不支持的 features 参数：{arg}";
                }

                if (!string.IsNullOrWhiteSpace(options.FeatureName))
                {
                    return "features enable/disable 只能提供一个 feature 名称。";
                }

                options.FeatureName = Normalize(arg);
                if (string.IsNullOrWhiteSpace(options.FeatureName))
                {
                    return "feature 名称不能为空。";
                }

                continue;
            }

            if (!TryReadValue(args, ref index, arg, out var value, out var error))
            {
                return error;
            }

            if (!TryApplyCommonOption(arg, value, options, out error))
            {
                return error;
            }
        }

        return null;
    }

    private static bool LooksLikeConfigOverride(string value)
        => value.Contains('=', StringComparison.Ordinal);

    private static bool TryApplyConfigOverride(CliRuntimeCommandOptions options, string rawPair, out string error)
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

        options.ConfigOverrides[key!] = rawPair[(separatorIndex + 1)..];
        error = string.Empty;
        return true;
    }

    private static bool TryApplyFeatureOverride(CliRuntimeCommandOptions options, string rawValue, bool enabled, out string error)
    {
        var featureName = Normalize(rawValue);
        if (string.IsNullOrWhiteSpace(featureName))
        {
            error = enabled ? "--enable 需要 feature 名称。" : "--disable 需要 feature 名称。";
            return false;
        }

        options.ConfigOverrides[$"features.{featureName}"] = enabled ? "true" : "false";
        error = string.Empty;
        return true;
    }

    private static bool ValidateResumeOptions(CliRuntimeCommandOptions options, out string error)
    {
        if (!string.IsNullOrWhiteSpace(options.ResumeThreadId) && options.ResumeLatestThread)
        {
            error = "--resume-thread-id 与 --resume-latest 不能同时使用。";
            return false;
        }

        if (!options.ResumeLatestThread && !options.ResumeLatestMatchCwd)
        {
            error = "--resume-latest-any-cwd 只能与 --resume-latest 一起使用。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateThreadOptions(ThreadCommandOptions options, out string error)
    {
        if (!ValidateResumeOptions(options, out error))
        {
            return false;
        }

        var threadOverridesUsed =
            !string.IsNullOrWhiteSpace(options.ThreadPath)
            || options.ThreadEphemeral.HasValue
            || options.ThreadHistory is not null
            || !string.IsNullOrWhiteSpace(options.ThreadModel)
            || !string.IsNullOrWhiteSpace(options.ThreadModelProvider)
            || options.ThreadServiceTier.IsSpecified
            || options.ThreadApprovalPolicy is not null
            || !string.IsNullOrWhiteSpace(options.ThreadSandboxMode)
            || options.ThreadConfig is not null
            || !string.IsNullOrWhiteSpace(options.ThreadServiceName)
            || !string.IsNullOrWhiteSpace(options.ThreadBaseInstructions)
            || !string.IsNullOrWhiteSpace(options.ThreadDeveloperInstructions)
            || options.ThreadPersonality is not null
            || !string.IsNullOrWhiteSpace(options.ThreadWorkingDirectory)
            || options.ThreadDynamicTools is not null
            || options.ThreadPersistExtendedHistory.HasValue
            || options.ThreadExperimentalRawEvents.HasValue;

        if (threadOverridesUsed && options.CommandKind is not (ThreadCommandKind.Start or ThreadCommandKind.Resume or ThreadCommandKind.Fork))
        {
            error = "thread request 参数仅适用于 start/fork/resume 子命令。";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.Confirmation)
            && options.CommandKind is not (ThreadCommandKind.Delete or ThreadCommandKind.Clear))
        {
            error = "--confirm 只能与 delete 或 clear 一起使用。";
            return false;
        }

        if (options.ThreadHistory is not null && options.CommandKind is not ThreadCommandKind.Resume)
        {
            error = "--thread-history-json/--thread-history-file 只能与 resume 一起使用。";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.ThreadPath)
            && options.CommandKind is not (ThreadCommandKind.Resume or ThreadCommandKind.Fork))
        {
            error = "--thread-path 只能与 resume 或 fork 一起使用。";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.ThreadServiceName) && options.CommandKind is not ThreadCommandKind.Start)
        {
            error = "--thread-service-name 只能与 start 一起使用。";
            return false;
        }

        if (options.ThreadExperimentalRawEvents.HasValue && options.CommandKind is not ThreadCommandKind.Start)
        {
            error = "--thread-experimental-raw-events 只能与 start 一起使用。";
            return false;
        }

        if (options.ThreadEphemeral.HasValue && options.CommandKind is not (ThreadCommandKind.Start or ThreadCommandKind.Fork))
        {
            error = "--thread-ephemeral 只能与 start 或 fork 一起使用。";
            return false;
        }

        if (options.ThreadPersonality is not null && options.CommandKind is ThreadCommandKind.Fork)
        {
            error = "--thread-personality 不能与 fork 一起使用。";
            return false;
        }

        if (options.ThreadDynamicTools is not null && options.CommandKind is not ThreadCommandKind.Start)
        {
            error = "--thread-dynamic-tools-json/--thread-dynamic-tools-file 只能与 start 一起使用。";
            return false;
        }

        switch (options.CommandKind)
        {
            case ThreadCommandKind.Fork:
            case ThreadCommandKind.Archive:
            case ThreadCommandKind.Delete:
            case ThreadCommandKind.Resume:
            case ThreadCommandKind.CleanBackgroundTerminals:
            case ThreadCommandKind.Unsubscribe:
            case ThreadCommandKind.IncrementElicitation:
            case ThreadCommandKind.DecrementElicitation:
            case ThreadCommandKind.Read:
            case ThreadCommandKind.Unarchive:
                if (string.IsNullOrWhiteSpace(options.ThreadId))
                {
                    error = "缺少必填参数：--thread-id <id>";
                    return false;
                }
                break;
            case ThreadCommandKind.Rename:
                if (string.IsNullOrWhiteSpace(options.ThreadId))
                {
                    error = "缺少必填参数：--thread-id <id>";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(options.Name))
                {
                    error = "缺少必填参数：--name <name>";
                    return false;
                }
                break;
            case ThreadCommandKind.Compact:
                if (string.IsNullOrWhiteSpace(options.ThreadId))
                {
                    error = "缺少必填参数：--thread-id <id>";
                    return false;
                }

                if (options.KeepRecentTurns <= 0)
                {
                    error = "--keep-recent-turns 必须是大于 0 的整数。";
                    return false;
                }
                break;
            case ThreadCommandKind.Rollback:
                if (string.IsNullOrWhiteSpace(options.ThreadId))
                {
                    error = "缺少必填参数：--thread-id <id>";
                    return false;
                }

                if (options.NumTurns is null || options.NumTurns <= 0)
                {
                    error = "缺少必填参数：--num-turns <n>";
                    return false;
                }
                break;
            case ThreadCommandKind.Metadata:
                if (string.IsNullOrWhiteSpace(options.ThreadId))
                {
                    error = "缺少必填参数：--thread-id <id>";
                    return false;
                }

                if ((!string.IsNullOrWhiteSpace(options.GitSha) && options.ClearGitSha)
                    || (!string.IsNullOrWhiteSpace(options.GitBranch) && options.ClearGitBranch)
                    || (!string.IsNullOrWhiteSpace(options.GitOriginUrl) && options.ClearGitOriginUrl))
                {
                    error = "同一 git 字段不能同时设置值和清空。";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(options.GitSha)
                    && string.IsNullOrWhiteSpace(options.GitBranch)
                    && string.IsNullOrWhiteSpace(options.GitOriginUrl)
                    && !options.ClearGitSha
                    && !options.ClearGitBranch
                    && !options.ClearGitOriginUrl)
                {
                    error = "thread metadata 至少需要一个 git 字段变更。";
                    return false;
                }
                break;
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateRuntimeSurfaceOptions(RuntimeSurfaceCommandOptions options, out string error)
    {
        switch (options.CommandKind)
        {
            case RuntimeSurfaceCommandKind.ConversationThread when string.IsNullOrWhiteSpace(options.ThreadId):
                error = "缺少必填参数：--thread-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.SessionOverview when string.IsNullOrWhiteSpace(options.SessionId):
                error = "缺少必填参数：--session-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.CollaborationCreate when string.IsNullOrWhiteSpace(options.CollaborationSpaceId):
                error = "缺少必填参数：--space-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.CollaborationCreate when string.IsNullOrWhiteSpace(options.CollaborationSpaceKey):
                error = "缺少必填参数：--key <key>";
                return false;
            case RuntimeSurfaceCommandKind.CollaborationCreate when string.IsNullOrWhiteSpace(options.DisplayName):
                error = "缺少必填参数：--display-name <name>";
                return false;
            case RuntimeSurfaceCommandKind.CollaborationCreate when string.IsNullOrWhiteSpace(options.Purpose):
                error = "缺少必填参数：--purpose <text>";
                return false;
            case RuntimeSurfaceCommandKind.CollaborationConfigure when string.IsNullOrWhiteSpace(options.CollaborationSpaceId):
                error = "缺少必填参数：--space-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.CollaborationConfigure
                when string.IsNullOrWhiteSpace(options.DisplayName)
                     && string.IsNullOrWhiteSpace(options.Purpose)
                     && string.IsNullOrWhiteSpace(options.DefaultWorkspace)
                     && string.IsNullOrWhiteSpace(options.DefaultExecutionProfile)
                     && string.IsNullOrWhiteSpace(options.PolicyKey):
                error = "collaboration configure 至少需要 --display-name、--purpose、--default-workspace、--default-execution-profile 或 --policy-key 之一。";
                return false;
            case RuntimeSurfaceCommandKind.CollaborationArchive when string.IsNullOrWhiteSpace(options.CollaborationSpaceId):
                error = "缺少必填参数：--space-id <id>";
                return false;
            case (RuntimeSurfaceCommandKind.CollaborationOverview or RuntimeSurfaceCommandKind.CollaborationSpace) when string.IsNullOrWhiteSpace(options.CollaborationSpaceId):
                error = "缺少必填参数：--space-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.WorkflowCreate when string.IsNullOrWhiteSpace(options.WorkflowId):
                error = "缺少必填参数：--workflow-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.WorkflowCreate when string.IsNullOrWhiteSpace(options.CollaborationSpaceId):
                error = "缺少必填参数：--space-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.WorkflowCreate when string.IsNullOrWhiteSpace(options.DisplayName):
                error = "缺少必填参数：--display-name <name>";
                return false;
            case RuntimeSurfaceCommandKind.WorkflowPublishPlan when string.IsNullOrWhiteSpace(options.WorkflowId):
                error = "缺少必填参数：--workflow-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.WorkflowPublishPlan when string.IsNullOrWhiteSpace(options.Title):
                error = "缺少必填参数：--title <name>";
                return false;
            case RuntimeSurfaceCommandKind.WorkflowPublishPlan when !ValidateJsonInputPair(options.ItemsJson, options.ItemsFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryProviders when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryFilter when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryAdd when string.IsNullOrWhiteSpace(options.PayloadJson) && string.IsNullOrWhiteSpace(options.PayloadFilePath):
                error = "缺少必填参数：--payload-json <json> 或 --payload-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.MemoryAdd when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryExtract when string.IsNullOrWhiteSpace(options.PayloadJson) && string.IsNullOrWhiteSpace(options.PayloadFilePath):
                error = "缺少必填参数：--payload-json <json> 或 --payload-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.MemoryExtract when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryImport when string.IsNullOrWhiteSpace(options.PayloadJson) && string.IsNullOrWhiteSpace(options.PayloadFilePath):
                error = "缺少必填参数：--payload-json <json> 或 --payload-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.MemoryImport when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryExport when string.IsNullOrWhiteSpace(options.PayloadJson) && string.IsNullOrWhiteSpace(options.PayloadFilePath):
                error = "缺少必填参数：--payload-json <json> 或 --payload-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.MemoryExport when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryBindProvider when string.IsNullOrWhiteSpace(options.PayloadJson) && string.IsNullOrWhiteSpace(options.PayloadFilePath):
                error = "缺少必填参数：--payload-json <json> 或 --payload-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.MemoryBindProvider when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryConsolidate when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryForget when string.IsNullOrWhiteSpace(options.PayloadJson) && string.IsNullOrWhiteSpace(options.PayloadFilePath):
                error = "缺少必填参数：--payload-json <json> 或 --payload-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.MemoryForget when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryDelete when string.IsNullOrWhiteSpace(options.PayloadJson) && string.IsNullOrWhiteSpace(options.PayloadFilePath):
                error = "缺少必填参数：--payload-json <json> 或 --payload-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.MemoryDelete when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemorySupersede when string.IsNullOrWhiteSpace(options.PayloadJson) && string.IsNullOrWhiteSpace(options.PayloadFilePath):
                error = "缺少必填参数：--payload-json <json> 或 --payload-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.MemorySupersede when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryReviewList when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryReviewApprove when string.IsNullOrWhiteSpace(options.PayloadJson) && string.IsNullOrWhiteSpace(options.PayloadFilePath):
                error = "缺少必填参数：--payload-json <json> 或 --payload-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.MemoryReviewApprove when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryReviewDemote when string.IsNullOrWhiteSpace(options.PayloadJson) && string.IsNullOrWhiteSpace(options.PayloadFilePath):
                error = "缺少必填参数：--payload-json <json> 或 --payload-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.MemoryReviewDemote when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryReviewMerge when string.IsNullOrWhiteSpace(options.PayloadJson) && string.IsNullOrWhiteSpace(options.PayloadFilePath):
                error = "缺少必填参数：--payload-json <json> 或 --payload-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.MemoryReviewMerge when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryReviewRestore when string.IsNullOrWhiteSpace(options.PayloadJson) && string.IsNullOrWhiteSpace(options.PayloadFilePath):
                error = "缺少必填参数：--payload-json <json> 或 --payload-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.MemoryReviewRestore when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryFeedback when string.IsNullOrWhiteSpace(options.PayloadJson) && string.IsNullOrWhiteSpace(options.PayloadFilePath):
                error = "缺少必填参数：--payload-json <json> 或 --payload-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.MemoryFeedback when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.MemoryCitation when string.IsNullOrWhiteSpace(options.PayloadJson) && string.IsNullOrWhiteSpace(options.PayloadFilePath):
                error = "缺少必填参数：--payload-json <json> 或 --payload-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.MemoryCitation when !ValidateJsonInputPair(options.PayloadJson, options.PayloadFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.WorkflowCreateTask when string.IsNullOrWhiteSpace(options.TaskId):
                error = "缺少必填参数：--task-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.WorkflowCreateTask when string.IsNullOrWhiteSpace(options.WorkflowId):
                error = "缺少必填参数：--workflow-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.WorkflowCreateTask when string.IsNullOrWhiteSpace(options.Title):
                error = "缺少必填参数：--title <name>";
                return false;
            case RuntimeSurfaceCommandKind.WorkflowCreateTask when !TryValidateWorkflowTaskState(options.Status, out error):
                return false;
            case RuntimeSurfaceCommandKind.WorkflowUpdateTaskState when string.IsNullOrWhiteSpace(options.TaskId):
                error = "缺少必填参数：--task-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.WorkflowUpdateTaskState when !TryValidateWorkflowTaskState(options.Status, out error):
                return false;
            case RuntimeSurfaceCommandKind.ParticipantBindSession when string.IsNullOrWhiteSpace(options.SessionId):
                error = "缺少必填参数：--session-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.ParticipantBindSession when string.IsNullOrWhiteSpace(options.ParticipantId):
                error = "缺少必填参数：--participant-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.ParticipantBindWorkflow when string.IsNullOrWhiteSpace(options.WorkflowId):
                error = "缺少必填参数：--workflow-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.ParticipantBindWorkflow when string.IsNullOrWhiteSpace(options.ParticipantId):
                error = "缺少必填参数：--participant-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.ParticipantUpdateRole when string.IsNullOrWhiteSpace(options.ParticipantId):
                error = "缺少必填参数：--participant-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.ParticipantUpdateRole when string.IsNullOrWhiteSpace(options.Role):
                error = "缺少必填参数：--role <name>";
                return false;
            case (RuntimeSurfaceCommandKind.ParticipantRead or RuntimeSurfaceCommandKind.ParticipantView) when string.IsNullOrWhiteSpace(options.ParticipantId):
                error = "缺少必填参数：--participant-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.ParticipantList when string.IsNullOrWhiteSpace(options.CollaborationSpaceId):
                error = "缺少必填参数：--space-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.ArtifactRead when string.IsNullOrWhiteSpace(options.ArtifactId):
                error = "缺少必填参数：--artifact-id <id>";
                return false;
            case (RuntimeSurfaceCommandKind.WorkflowBoard or RuntimeSurfaceCommandKind.WorkflowTaskBoard or RuntimeSurfaceCommandKind.WorkflowPlan) when string.IsNullOrWhiteSpace(options.WorkflowId):
                error = "缺少必填参数：--workflow-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.AgentThreadRegister when string.IsNullOrWhiteSpace(options.ThreadId):
                error = "缺少必填参数：--thread-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.AgentJobCreate when string.IsNullOrWhiteSpace(options.Instruction):
                error = "缺少必填参数：--instruction <text>";
                return false;
            case RuntimeSurfaceCommandKind.AgentJobCreate when !ValidateJsonInputPair(options.InputHeadersJson, options.InputHeadersFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.AgentJobCreate when !ValidateJsonInputPair(options.OutputSchemaJson, options.OutputSchemaFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.AgentJobCreate when !ValidateJsonInputPair(options.ItemsJson, options.ItemsFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.AgentJobDispatch when string.IsNullOrWhiteSpace(options.JobId):
                error = "缺少必填参数：--job-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.AgentJobDispatch when options.DispatchThreadIds.Count == 0:
                error = "缺少必填参数：--thread-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.AgentJobItemReport when string.IsNullOrWhiteSpace(options.JobId):
                error = "缺少必填参数：--job-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.AgentJobItemReport when string.IsNullOrWhiteSpace(options.ItemId):
                error = "缺少必填参数：--item-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.AgentJobItemReport when string.IsNullOrWhiteSpace(options.Status):
                error = "缺少必填参数：--status <status>";
                return false;
            case RuntimeSurfaceCommandKind.AgentJobItemReport when !ValidateJsonInputPair(options.ResultJson, options.ResultFilePath, out error):
                return false;
            case RuntimeSurfaceCommandKind.AgentJobRead when string.IsNullOrWhiteSpace(options.JobId):
                error = "缺少必填参数：--job-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.AgentTeam when string.IsNullOrWhiteSpace(options.TeamId):
                error = "缺少必填参数：--team-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.ReviewStart when string.IsNullOrWhiteSpace(options.ThreadId):
                error = "缺少必填参数：--thread-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.ReviewStart when string.IsNullOrWhiteSpace(options.ReviewTargetType):
                error = "缺少必填参数：--target <uncommitted-changes|base-branch|commit|custom>";
                return false;
            case RuntimeSurfaceCommandKind.ReviewStart when !string.IsNullOrWhiteSpace(options.Delivery)
                && !string.Equals(options.Delivery, "inline", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(options.Delivery, "detached", StringComparison.OrdinalIgnoreCase):
                error = "--delivery 只支持 inline 或 detached。";
                return false;
            case RuntimeSurfaceCommandKind.ReviewStart when string.Equals(options.ReviewTargetType, "baseBranch", StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(options.ReviewBranch):
                error = "base-branch review 需要 --branch <name>";
                return false;
            case RuntimeSurfaceCommandKind.ReviewStart when string.Equals(options.ReviewTargetType, "commit", StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(options.ReviewSha):
                error = "commit review 需要 --sha <commit>";
                return false;
            case RuntimeSurfaceCommandKind.ReviewStart when string.Equals(options.ReviewTargetType, "custom", StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(options.ReviewInstructions):
                error = "custom review 需要 --instructions <text>";
                return false;
            case RuntimeSurfaceCommandKind.ReviewStart when !string.Equals(options.ReviewTargetType, "uncommittedChanges", StringComparison.Ordinal)
                && !string.Equals(options.ReviewTargetType, "baseBranch", StringComparison.Ordinal)
                && !string.Equals(options.ReviewTargetType, "commit", StringComparison.Ordinal)
                && !string.Equals(options.ReviewTargetType, "custom", StringComparison.Ordinal):
                error = "--target 只支持 uncommitted-changes、base-branch、commit、custom。";
                return false;
            case RuntimeSurfaceCommandKind.FeatureConfigWrite when string.IsNullOrWhiteSpace(options.FeatureName):
                error = "缺少必填参数：<feature>";
                return false;
            case RuntimeSurfaceCommandKind.FeatureConfigWrite when options.Enabled is null:
                error = "features enable/disable 需要 enabled 状态。";
                return false;
            case RuntimeSurfaceCommandKind.ConversationSummary when string.IsNullOrWhiteSpace(options.ThreadId) && string.IsNullOrWhiteSpace(options.RolloutPath):
                error = "缺少必填参数：--thread-id <id> 或 --rollout-path <path>";
                return false;
            case RuntimeSurfaceCommandKind.ConversationSummary when !string.IsNullOrWhiteSpace(options.ThreadId) && !string.IsNullOrWhiteSpace(options.RolloutPath):
                error = "--thread-id 与 --rollout-path 不能同时使用。";
                return false;
            case RuntimeSurfaceCommandKind.GitDiffToRemote when string.IsNullOrWhiteSpace(options.ThreadId):
                error = "缺少必填参数：--thread-id <id>";
                return false;
            case (RuntimeSurfaceCommandKind.PluginInstall or RuntimeSurfaceCommandKind.PluginRead) when string.IsNullOrWhiteSpace(options.MarketplacePath):
                error = "缺少必填参数：--marketplace-path <path>";
                return false;
            case (RuntimeSurfaceCommandKind.PluginInstall or RuntimeSurfaceCommandKind.PluginRead) when string.IsNullOrWhiteSpace(options.PluginName):
                error = "缺少必填参数：--plugin-name <name>";
                return false;
            case RuntimeSurfaceCommandKind.PluginUninstall when string.IsNullOrWhiteSpace(options.PluginId):
                error = "缺少必填参数：--plugin-id <plugin@marketplace>";
                return false;
            case RuntimeSurfaceCommandKind.SkillsConfigWrite when string.IsNullOrWhiteSpace(options.SkillPath):
                error = "缺少必填参数：--path <path>";
                return false;
            case RuntimeSurfaceCommandKind.SkillsConfigWrite when options.Enabled is null:
                error = "skills enable/disable 需要 enabled 状态。";
                return false;
            case RuntimeSurfaceCommandKind.SkillsRemoteExport when string.IsNullOrWhiteSpace(options.HazelnutId):
                error = "缺少必填参数：--hazelnut-id <id>";
                return false;
            case RuntimeSurfaceCommandKind.McpServerOauthLogin when string.IsNullOrWhiteSpace(options.McpServerName):
                error = "缺少必填参数：--name <server>";
                return false;
            case RuntimeSurfaceCommandKind.ConfigValueWrite when string.IsNullOrWhiteSpace(options.KeyPath):
                error = "缺少必填参数：--key <path>";
                return false;
            case RuntimeSurfaceCommandKind.ConfigValueWrite when string.IsNullOrWhiteSpace(options.ConfigValueJson) && string.IsNullOrWhiteSpace(options.ConfigValueFilePath):
                error = "缺少必填参数：--value-json <json> 或 --value-file <path>";
                return false;
            case RuntimeSurfaceCommandKind.ConfigBatchWrite when string.IsNullOrWhiteSpace(options.BatchItemsJson) && string.IsNullOrWhiteSpace(options.BatchItemsFilePath):
                error = "缺少必填参数：--items-json <json> 或 --items-file <path>";
                return false;
            default:
                error = string.Empty;
                return true;
        }
    }

    private static bool ValidateJsonInputPair(string? inlineJson, string? filePath, out string error)
    {
        if (!string.IsNullOrWhiteSpace(inlineJson) && !string.IsNullOrWhiteSpace(filePath))
        {
            error = "JSON 内联参数与 JSON 文件路径不能同时提供。";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filePath) && !File.Exists(filePath))
        {
            error = $"JSON 文件不存在：{filePath}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateWorkflowTaskState(string? value, out string error)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "缺少必填参数：--state <todo|in-progress|blocked|done|cancelled>";
            return false;
        }

        switch (normalized.Trim().ToLowerInvariant())
        {
            case "todo":
            case "in-progress":
            case "inprogress":
            case "blocked":
            case "done":
            case "cancelled":
            case "canceled":
                error = string.Empty;
                return true;
            default:
                error = "--state 只支持 todo、in-progress、blocked、done、cancelled。";
                return false;
        }
    }

    private static bool ValidateFeedbackOptions(FeedbackCommandOptions options, out string error)
    {
        if (string.IsNullOrWhiteSpace(options.Classification))
        {
            error = "\u7f3a\u5c11\u5fc5\u586b\u53c2\u6570\uff1a--classification <name>";
            return false;
        }

        foreach (var extraLogFile in options.ExtraLogFiles)
        {
            if (!File.Exists(extraLogFile))
            {
                error = $"--extra-log-file \u6307\u5411\u7684\u6587\u4ef6\u4e0d\u5b58\u5728\uff1a{extraLogFile}";
                return false;
            }

            if ((File.GetAttributes(extraLogFile) & FileAttributes.Directory) != 0)
            {
                error = $"--extra-log-file \u5fc5\u987b\u6307\u5411\u6587\u4ef6\uff1a{extraLogFile}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateWindowsSandboxOptions(WindowsSandboxCommandOptions options, out string error)
    {
        if (string.IsNullOrWhiteSpace(options.Mode))
        {
            error = "\u7f3a\u5c11\u5fc5\u586b\u53c2\u6570\uff1a--mode <elevated|unelevated>";
            return false;
        }

        if (!string.Equals(options.Mode, "elevated", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.Mode, "unelevated", StringComparison.OrdinalIgnoreCase))
        {
            error = "--mode \u53ea\u80fd\u662f elevated \u6216 unelevated\u3002";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.SandboxCwd) && !Directory.Exists(options.SandboxCwd))
        {
            error = $"--cwd \u6307\u5411\u7684\u76ee\u5f55\u4e0d\u5b58\u5728\uff1a{options.SandboxCwd}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateRealtimeOptions(RealtimeCommandOptions options, out string error)
    {
        if (string.IsNullOrWhiteSpace(options.ThreadId))
        {
            error = "\u7f3a\u5c11\u5fc5\u586b\u53c2\u6570\uff1a--thread-id <id>";
            return false;
        }

        switch (options.CommandKind)
        {
            case RealtimeCommandKind.Start when !string.IsNullOrWhiteSpace(options.Text) || !string.IsNullOrWhiteSpace(options.HandoffId) || options.Output is not null || !string.IsNullOrWhiteSpace(options.AudioJson) || !string.IsNullOrWhiteSpace(options.AudioFilePath):
                error = "realtime start \u53ea\u63a5\u53d7 --thread-id [--session-id] [--prompt]\u3002";
                return false;
            case RealtimeCommandKind.AppendText when string.IsNullOrWhiteSpace(options.Text):
                error = "append-text \u9700\u8981 --text <content>";
                return false;
            case RealtimeCommandKind.AppendText when !string.IsNullOrWhiteSpace(options.Prompt) || !string.IsNullOrWhiteSpace(options.HandoffId) || !string.IsNullOrWhiteSpace(options.Output) || !string.IsNullOrWhiteSpace(options.AudioJson) || !string.IsNullOrWhiteSpace(options.AudioFilePath):
                error = "append-text \u53ea\u63a5\u53d7 --thread-id [--session-id] --text\u3002";
                return false;
            case RealtimeCommandKind.AppendAudio when string.IsNullOrWhiteSpace(options.AudioJson) && string.IsNullOrWhiteSpace(options.AudioFilePath):
                error = "append-audio \u5fc5\u987b\u63d0\u4f9b --audio-json \u6216 --audio-file \u5176\u4e2d\u4e4b\u4e00\u3002";
                return false;
            case RealtimeCommandKind.AppendAudio when !string.IsNullOrWhiteSpace(options.AudioJson) && !string.IsNullOrWhiteSpace(options.AudioFilePath):
                error = "--audio-json \u4e0e --audio-file \u4e0d\u80fd\u540c\u65f6\u63d0\u4f9b\u3002";
                return false;
            case RealtimeCommandKind.AppendAudio when !string.IsNullOrWhiteSpace(options.Prompt) || !string.IsNullOrWhiteSpace(options.Text) || !string.IsNullOrWhiteSpace(options.HandoffId) || !string.IsNullOrWhiteSpace(options.Output):
                error = "append-audio \u53ea\u63a5\u53d7 --thread-id [--session-id] \u4e0e\u97f3\u9891\u8d1f\u8f7d\u3002";
                return false;
            case RealtimeCommandKind.AppendAudio when !string.IsNullOrWhiteSpace(options.AudioFilePath) && !File.Exists(options.AudioFilePath):
                error = $"--audio-file \u6307\u5411\u7684\u6587\u4ef6\u4e0d\u5b58\u5728\uff1a{options.AudioFilePath}";
                return false;
            case RealtimeCommandKind.HandoffOutput when string.IsNullOrWhiteSpace(options.HandoffId):
                error = "handoff-output \u9700\u8981 --handoff-id <id>\u3002";
                return false;
            case RealtimeCommandKind.HandoffOutput when options.Output is null:
                error = "handoff-output \u9700\u8981 --output <text>\u3002";
                return false;
            case RealtimeCommandKind.HandoffOutput when !string.IsNullOrWhiteSpace(options.Prompt) || !string.IsNullOrWhiteSpace(options.Text) || !string.IsNullOrWhiteSpace(options.AudioJson) || !string.IsNullOrWhiteSpace(options.AudioFilePath):
                error = "handoff-output \u53ea\u63a5\u53d7 --thread-id [--session-id] --handoff-id --output\u3002";
                return false;
            case RealtimeCommandKind.Stop when !string.IsNullOrWhiteSpace(options.Prompt) || !string.IsNullOrWhiteSpace(options.Text) || !string.IsNullOrWhiteSpace(options.HandoffId) || !string.IsNullOrWhiteSpace(options.Output) || !string.IsNullOrWhiteSpace(options.AudioJson) || !string.IsNullOrWhiteSpace(options.AudioFilePath):
                error = "realtime stop \u53ea\u63a5\u53d7 --thread-id [--session-id]\u3002";
                return false;
            default:
                error = string.Empty;
                return true;
        }
    }

    private static string? NormalizeReviewTargetType(string? value)
    {
        var normalized = Normalize(value)?.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "uncommittedchanges" or "uncommitted" => "uncommittedChanges",
            "basebranch" => "baseBranch",
            "commit" => "commit",
            "custom" => "custom",
            _ => null,
        };
    }

    private static string? NormalizeReviewDelivery(string? value)
    {
        var normalized = Normalize(value)?.ToLowerInvariant();
        return normalized switch
        {
            "inline" => "inline",
            "detached" => "detached",
            _ => null,
        };
    }

    private static bool ValidateFuzzyFileSearchOptions(FuzzyFileSearchCommandOptions options, out string error)
    {
        switch (options.CommandKind)
        {
            case FuzzyFileSearchCommandKind.Search when string.IsNullOrWhiteSpace(options.Query):
                error = "缺少必填参数：--query <text>";
                return false;
            case FuzzyFileSearchCommandKind.Start when string.IsNullOrWhiteSpace(options.SessionId):
            case FuzzyFileSearchCommandKind.Update when string.IsNullOrWhiteSpace(options.SessionId):
            case FuzzyFileSearchCommandKind.Stop when string.IsNullOrWhiteSpace(options.SessionId):
                error = "缺少必填参数：--session-id <id>";
                return false;
            case FuzzyFileSearchCommandKind.Update when string.IsNullOrWhiteSpace(options.Query):
                error = "缺少必填参数：--query <text>";
                return false;
            default:
                error = string.Empty;
                return true;
        }
    }

    private static bool ValidateCommandExecOptions(CommandExecCommandOptions options, out string error)
    {
        var hasCommandText = !string.IsNullOrWhiteSpace(options.CommandText);
        var hasCommandArgs = !string.IsNullOrWhiteSpace(options.CommandArgsJson) || !string.IsNullOrWhiteSpace(options.CommandArgsFilePath);
        var inputSourceCount = 0;
        inputSourceCount += string.IsNullOrWhiteSpace(options.InputText) ? 0 : 1;
        inputSourceCount += string.IsNullOrWhiteSpace(options.InputFilePath) ? 0 : 1;
        inputSourceCount += string.IsNullOrWhiteSpace(options.InputBase64) ? 0 : 1;

        switch (options.CommandKind)
        {
            case CommandExecCommandKind.Exec:
                if (!hasCommandText && !hasCommandArgs)
                {
                    error = "缺少必填参数：--command <text>、--argv-json <json> 或 --argv-file <path>";
                    return false;
                }

                if (hasCommandText && hasCommandArgs)
                {
                    error = "--command 不能与 --argv-json/--argv-file 同时使用。";
                    return false;
                }

                if ((options.Rows.HasValue && !options.Cols.HasValue) || (!options.Rows.HasValue && options.Cols.HasValue))
                {
                    error = "--rows 与 --cols 必须同时提供。";
                    return false;
                }

                if ((options.Rows.HasValue || options.Cols.HasValue) && !options.Tty)
                {
                    error = "--rows/--cols 只能与 --tty 一起使用。";
                    return false;
                }

                if (options.DisableTimeout && options.TimeoutMs.HasValue)
                {
                    error = "--disable-timeout 与 --timeout-ms 不能同时使用。";
                    return false;
                }

                if (options.DisableOutputCap && options.OutputBytesCap.HasValue)
                {
                    error = "--disable-output-cap 与 --output-bytes-cap 不能同时使用。";
                    return false;
                }

                if ((!string.IsNullOrWhiteSpace(options.EnvJson) && !string.IsNullOrWhiteSpace(options.EnvFilePath))
                    || (!string.IsNullOrWhiteSpace(options.SandboxJson) && !string.IsNullOrWhiteSpace(options.SandboxFilePath)))
                {
                    error = "--env-json 与 --env-file、--sandbox-json 与 --sandbox-file 不能同时成对混用。";
                    return false;
                }

                break;
            case CommandExecCommandKind.Write:
                if (string.IsNullOrWhiteSpace(options.ProcessId))
                {
                    error = "缺少必填参数：--process-id <id>";
                    return false;
                }

                if (inputSourceCount > 1)
                {
                    error = "--text、--stdin-file、--base64 只能选择一种。";
                    return false;
                }

                if (inputSourceCount == 0 && !options.CloseStdin)
                {
                    error = "command write 需要 --text、--stdin-file、--base64 或 --close-stdin。";
                    return false;
                }

                break;
            case CommandExecCommandKind.Terminate:
                if (string.IsNullOrWhiteSpace(options.ProcessId))
                {
                    error = "缺少必填参数：--process-id <id>";
                    return false;
                }

                break;
            case CommandExecCommandKind.Resize:
                if (string.IsNullOrWhiteSpace(options.ProcessId))
                {
                    error = "缺少必填参数：--process-id <id>";
                    return false;
                }

                if (!options.Rows.HasValue || !options.Cols.HasValue)
                {
                    error = "command resize 需要 --rows <n> 与 --cols <n>。";
                    return false;
                }

                break;
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateCodeModeOptions(CodeModeCommandOptions options, out string error)
    {
        var hasInlineInput = !string.IsNullOrWhiteSpace(options.Input);
        var hasInputFile = !string.IsNullOrWhiteSpace(options.InputFilePath);

        switch (options.CommandKind)
        {
            case CodeModeCommandKind.Exec:
                if (!hasInlineInput && !hasInputFile)
                {
                    error = "缺少必填参数：--input <text> 或 --input-file <path>";
                    return false;
                }

                if (hasInlineInput && hasInputFile)
                {
                    error = "--input 与 --input-file 不能同时提供。";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(options.InputFilePath) && !File.Exists(options.InputFilePath))
                {
                    error = $"--input-file 指向的文件不存在：{options.InputFilePath}";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(options.CellId) || options.MaxTokens.HasValue || options.Terminate)
                {
                    error = "exec 只接受输入与执行预算参数，不接受 --cell-id / --max-tokens / --terminate。";
                    return false;
                }

                break;
            case CodeModeCommandKind.Wait:
                if (string.IsNullOrWhiteSpace(options.CellId))
                {
                    error = "缺少必填参数：--cell-id <id>";
                    return false;
                }

                if (hasInlineInput || hasInputFile || options.MaxOutputTokens.HasValue)
                {
                    error = "exec-wait 只接受 --cell-id、等待预算与 --terminate，不接受 exec 输入参数。";
                    return false;
                }

                break;
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateExecOptions(ExecCommandOptions options, out string error)
    {
        if (options.FullAuto && options.DangerouslyBypassApprovalsAndSandbox)
        {
            error = "--full-auto 与 --dangerously-bypass-approvals-and-sandbox 不能同时使用。";
            return false;
        }

        if (options.ImagePaths.Any(static path => !File.Exists(path)))
        {
            error = $"--image 指向的文件不存在：{options.ImagePaths.First(path => !File.Exists(path))}";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.OutputSchemaFilePath) && !File.Exists(options.OutputSchemaFilePath))
        {
            error = $"--output-schema 指向的文件不存在：{options.OutputSchemaFilePath}";
            return false;
        }

        if (options.AdditionalWritableDirectories.Any(static path => !Directory.Exists(path)))
        {
            error = $"--add-dir 指向的目录不存在：{options.AdditionalWritableDirectories.First(path => !Directory.Exists(path))}";
            return false;
        }

        switch (options.CommandKind)
        {
            case ExecCommandKind.UserTurn:
            case ExecCommandKind.Resume:
                error = string.Empty;
                return true;
            case ExecCommandKind.Review:
                var targetCount = 0;
                targetCount += options.ReviewUncommitted ? 1 : 0;
                targetCount += string.IsNullOrWhiteSpace(options.ReviewBaseBranch) ? 0 : 1;
                targetCount += string.IsNullOrWhiteSpace(options.ReviewCommit) ? 0 : 1;
                targetCount += string.IsNullOrWhiteSpace(options.ReviewPrompt) ? 0 : 1;

                if (targetCount == 0)
                {
                    error = "exec review 需要 --uncommitted、--base、--commit 或自定义 instructions。";
                    return false;
                }

                if (targetCount > 1)
                {
                    error = "exec review 的 target 参数彼此互斥。";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(options.ReviewCommitTitle) && string.IsNullOrWhiteSpace(options.ReviewCommit))
                {
                    error = "--title 只能与 --commit 一起使用。";
                    return false;
                }

                error = string.Empty;
                return true;
            default:
                error = "不支持的 exec 命令。";
                return false;
        }
    }

    private static bool ValidateChatOptions(ChatCommandOptions options, out string error)
    {
        if (options.FullAuto && options.DangerouslyBypassApprovalsAndSandbox)
        {
            error = "--full-auto 与 --dangerously-bypass-approvals-and-sandbox 不能同时使用。";
            return false;
        }

        if (options.ImagePaths.Any(static path => !File.Exists(path)))
        {
            error = $"--image 指向的文件不存在：{options.ImagePaths.First(path => !File.Exists(path))}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateAppServerOptions(AppServerCommandOptions options, out string error)
    {
        switch (options.CommandKind)
        {
            case AppServerCommandKind.RunServer:
                var listenUrl = Normalize(options.ListenUrl);
                if (string.IsNullOrWhiteSpace(listenUrl))
                {
                    error = "app-server 需要有效的 --listen 值。";
                    return false;
                }

                if (!string.Equals(listenUrl, "stdio://", StringComparison.OrdinalIgnoreCase)
                    && !listenUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
                {
                    error = "app-server 仅支持 --listen stdio:// 或 ws://IP:PORT。";
                    return false;
                }

                error = string.Empty;
                return true;
            case AppServerCommandKind.GenerateTs:
            case AppServerCommandKind.GenerateJsonSchema:
                if (string.IsNullOrWhiteSpace(options.OutDirectory))
                {
                    error = "app-server 生成协议文件需要 --out <dir>（或 -o <dir>）。";
                    return false;
                }

                error = string.Empty;
                return true;
            default:
                error = "不支持的 app-server 子命令。";
                return false;
        }
    }

    private static bool ValidateMcpOptions(McpCommandOptions options, out string error)
    {
        switch (options.CommandKind)
        {
            case McpCommandKind.List:
                error = string.Empty;
                return true;
            case McpCommandKind.Get:
            case McpCommandKind.Remove:
                if (string.IsNullOrWhiteSpace(options.Name))
                {
                    error = options.CommandKind == McpCommandKind.Get
                        ? "mcp get 需要 <name>。"
                        : "mcp remove 需要 <name>。";
                    return false;
                }

                if (!IsValidMcpServerName(options.Name))
                {
                    error = $"无效的 MCP server 名称：{options.Name}。名称只能包含字母、数字、'-'、'_'.";
                    return false;
                }

                error = string.Empty;
                return true;
            case McpCommandKind.Add:
                if (string.IsNullOrWhiteSpace(options.Name))
                {
                    error = "mcp add 需要 <name>。";
                    return false;
                }

                if (!IsValidMcpServerName(options.Name))
                {
                    error = $"无效的 MCP server 名称：{options.Name}。名称只能包含字母、数字、'-'、'_'.";
                    return false;
                }

                var usesUrl = !string.IsNullOrWhiteSpace(options.Url);
                var usesCommand = options.Command.Count > 0;
                if (usesUrl == usesCommand)
                {
                    error = "mcp add 必须二选一：--url <URL> 或 `-- <COMMAND>...`。";
                    return false;
                }

                if (!usesUrl && string.IsNullOrWhiteSpace(options.Command[0]))
                {
                    error = "mcp add 的命令不能为空。";
                    return false;
                }

                if (usesUrl && options.EnvironmentVariables.Count > 0)
                {
                    error = "--env 只能和 `-- <COMMAND>...` 一起使用。";
                    return false;
                }

                if (!usesUrl && !string.IsNullOrWhiteSpace(options.BearerTokenEnvVar))
                {
                    error = "--bearer-token-env-var 只能和 --url 一起使用。";
                    return false;
                }

                error = string.Empty;
                return true;
            default:
                error = "不支持的 mcp 命令。";
                return false;
        }
    }

    private static bool IsValidMcpServerName(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && name.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool TryReadValue(string[] args, ref int index, string option, out string value, out string error)
    {
        if (string.Equals(option, "--json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--resume-latest", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--resume-latest-any-cwd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--approve-all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--verbose-events", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--archived", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--all-cwd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--include-hidden", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--include-primary-threads", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--include-layers", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--force-reload", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--force-refetch", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--force-remote-sync", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--prefer-websocket-transport", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--include-closed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--include-archived", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--include-home", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--reload-user-config", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--tty", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--stream-stdin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--stream-stdout-stderr", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--background", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--disable-timeout", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--disable-output-cap", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--approved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--include-logs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--login", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--no-login", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--close-stdin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--terminate", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--include-turns", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--clear-git-sha", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--clear-git-branch", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--clear-git-origin-url", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--force", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--probe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--wait", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--analytics-default-enabled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option, "--experimental", StringComparison.OrdinalIgnoreCase))
        {
            value = string.Empty;
            error = string.Empty;
            return true;
        }

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
            error = $"\u53c2\u6570 {option} \u7f3a\u5c11\u503c\u3002";
            return false;
        }

        value = args[++index];
        error = string.Empty;
        return true;
    }

    private static bool IsHelp(string arg)
        => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
           || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase)
           || string.Equals(arg, "help", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool TryParseThreadServiceTier(
        string? value,
        out CliServiceTierOverride serviceTier,
        out string error)
    {
        error = string.Empty;
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            serviceTier = CliServiceTierOverride.Unspecified;
            return true;
        }

        if (string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase))
        {
            serviceTier = CliServiceTierOverride.Clear;
            return true;
        }

        if (string.Equals(normalized, "fast", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "flex", StringComparison.OrdinalIgnoreCase))
        {
            serviceTier = CliServiceTierOverride.FromValue(normalized!);
            return true;
        }

        serviceTier = CliServiceTierOverride.Unspecified;
        error = "--thread-service-tier 只能是 fast、flex 或 null。";
        return false;
    }

    private static bool TryParseThreadSourceKind(
        string? value,
        out ControlPlaneThreadSourceKind? sourceKind,
        out string error)
    {
        error = string.Empty;
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            sourceKind = null;
            error = "--source-kind 不能为空。";
            return false;
        }

        if (ControlPlaneThreadSourceKind.TryParse(normalized, out sourceKind))
        {
            return true;
        }

        error = "--source-kind 只能是 cli、vscode、exec、appServer、subAgent、subAgentReview、subAgentCompact、subAgentThreadSpawn、subAgentOther 或 unknown。";
        return false;
    }

    private static bool TryParseApprovalPolicy(
        string? value,
        out string? approvalPolicy,
        out string error,
        string optionName = "--thread-approval-policy")
    {
        error = string.Empty;
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            approvalPolicy = null;
            return true;
        }

        if (normalized.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(normalized);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    approvalPolicy = document.RootElement.GetRawText();
                    return true;
                }
            }
            catch (JsonException ex)
            {
                error = $"{optionName} JSON 解析失败：{ex.Message}";
                approvalPolicy = null;
                return false;
            }

            error = $"{optionName} JSON 解析失败。";
            approvalPolicy = null;
            return false;
        }

        if (normalized is "untrusted" or "on-failure" or "on-request" or "never")
        {
            approvalPolicy = normalized;
            return true;
        }

        error = $"{optionName} 只能是 untrusted、on-failure、on-request、never 或 granular JSON。";
        approvalPolicy = null;
        return false;
    }

    private static bool TryParsePersonality(
        string? value,
        out string? personality,
        out string error)
    {
        error = string.Empty;
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            personality = null;
            return true;
        }

        if (normalized is "none" or "friendly" or "pragmatic")
        {
            personality = normalized;
            return true;
        }

        error = "--thread-personality 只能是 none、friendly 或 pragmatic。";
        personality = null;
        return false;
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
