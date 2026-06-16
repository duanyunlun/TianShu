using System.Text;

namespace TianShu.Cli.Interaction.Commands;

internal sealed class SlashCommandRegistry
{
    public static SlashCommandRegistry Default { get; } = new(BuildDefaultDescriptors());

    private readonly IReadOnlyList<SlashCommandDescriptor> descriptors;
    private readonly Dictionary<string, SlashCommandDescriptor> descriptorsByName;

    public SlashCommandRegistry(IEnumerable<SlashCommandDescriptor> descriptors)
    {
        this.descriptors = descriptors.ToArray();
        descriptorsByName = new Dictionary<string, SlashCommandDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in this.descriptors)
        {
            foreach (var name in descriptor.AllNames())
            {
                descriptorsByName[name] = descriptor;
            }
        }
    }

    public IReadOnlyList<SlashCommandDescriptor> Descriptors => descriptors;

    public SlashCommandKind ResolveKind(string command)
        => TryGetDescriptor(command, out var descriptor) ? descriptor.Kind : SlashCommandKind.Unknown;

    public bool TryGetDescriptor(string command, out SlashCommandDescriptor descriptor)
        => descriptorsByName.TryGetValue(command, out descriptor!);

    public SlashCommandDescriptor GetRequired(SlashCommandKind kind)
    {
        foreach (var descriptor in descriptors)
        {
            if (descriptor.Kind == kind)
            {
                return descriptor;
            }
        }

        throw new InvalidOperationException($"Slash command descriptor not found: {kind}.");
    }

    public string BuildHelpText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("交互命令：");
        foreach (var descriptor in descriptors.Where(static descriptor => descriptor.VisibleInHelp))
        {
            AppendHelpLine(builder, descriptor.Usage, descriptor.Description);
            if (descriptor.Kind == SlashCommandKind.Thread)
            {
                AppendHelpLine(builder, "thread clear", "清空全部线程（需二次确认；也可不带斜杠输入）");
            }
        }

        builder.AppendLine("普通输入：");
        builder.AppendLine("  直接输入文本会作为用户消息发送。若当前存在恢复草稿，输入的新文本会覆盖该草稿后发送。");
        builder.AppendLine("  输入 !<shell command> 会执行本地 user shell；当前已有活动回合时会复用该回合。");
        builder.AppendLine("  支持 linked mention/skill 语法：[$name](app://id)、[@name](plugin://sample@test)、[$skill](C:/skills/demo/SKILL.md)。");
        builder.Append("  当前有运行中的回合时，直接输入会作为 steer follow-up；也可用 /follow-up 或 /interrupt。");
        return builder.ToString();
    }

    private static IReadOnlyList<SlashCommandDescriptor> BuildDefaultDescriptors()
        =>
        [
            Descriptor(SlashCommandKind.Help, "help", [], "help", "显示帮助", SlashCommandCategory.General),
            Descriptor(SlashCommandKind.Init, "init", [], "init", "发送 TianShu AGENTS.md 初始化提示", SlashCommandCategory.General),
            Descriptor(SlashCommandKind.Exit, "exit", ["quit"], "exit", "退出", SlashCommandCategory.General, SlashCommandConfirmationPolicy.EndsInteractiveSession),
            Descriptor(SlashCommandKind.Interrupt, "interrupt", [], "interrupt", "中断当前回合", SlashCommandCategory.TurnControl, allowedWhileRunning: true),
            Descriptor(SlashCommandKind.FollowUp, "follow-up", ["followup"], "follow-up <mode> <text>", "发送或治理 follow-up（queue / steer / interrupt / promote / drop）", SlashCommandCategory.TurnControl, allowedWhileRunning: true, requiresActiveThread: true, subcommands: ["queue", "steer", "interrupt", "promote", "drop"]),
            Descriptor(SlashCommandKind.Model, "model-route", [], "model-route [route-set|status [--matrix]]", "列出或切换模型路由方案、验收当前路由协议或查看协议矩阵", SlashCommandCategory.ModelAndConfig, subcommands: ["status"]),
            Descriptor(SlashCommandKind.Config, "config", [], "config [gui|reload]", "打开 ConfigGUI；reload 重新读取 tianshu.toml 并刷新当前会话模型/provider", SlashCommandCategory.ModelAndConfig, subcommands: ["gui", "reload"]),
            Descriptor(SlashCommandKind.Reload, "reload", [], "reload", "/config reload 的简写", SlashCommandCategory.ModelAndConfig),
            Descriptor(SlashCommandKind.Draft, "draft", [], "draft", "查看当前恢复草稿状态", SlashCommandCategory.TurnControl),
            Descriptor(SlashCommandKind.SendRestored, "send-restored", ["sendrestored"], "send-restored", "发送当前恢复草稿", SlashCommandCategory.TurnControl, requiresActiveThread: true),
            Descriptor(SlashCommandKind.DropRestored, "drop-restored", ["droprestored"], "drop-restored", "丢弃当前恢复草稿并切换到下一条", SlashCommandCategory.TurnControl),
            Descriptor(SlashCommandKind.Approve, "approve", [], "approve <callId> [note]", "提交 accept 审批响应", SlashCommandCategory.Approval, allowedWhileRunning: true, requiresActiveThread: true),
            Descriptor(SlashCommandKind.ApproveSession, "approve-session", ["approvesession"], "approve-session <callId> [note]", "提交 acceptForSession 审批响应", SlashCommandCategory.Approval, allowedWhileRunning: true, requiresActiveThread: true),
            Descriptor(SlashCommandKind.ApproveAlways, "approve-always", ["approvealways"], "approve-always <callId> [note]", "提交 acceptAndRemember 审批响应", SlashCommandCategory.Approval, SlashCommandConfirmationPolicy.RequiresExplicitConfirmation, allowedWhileRunning: true, requiresActiveThread: true),
            Descriptor(SlashCommandKind.Reject, "reject", ["decline"], "reject <callId> [note]", "提交 decline 审批响应", SlashCommandCategory.Approval, allowedWhileRunning: true, requiresActiveThread: true),
            Descriptor(SlashCommandKind.CancelApproval, "cancel-approval", ["cancelapproval", "cancel"], "cancel-approval <callId> [note]", "提交 cancel 审批响应", SlashCommandCategory.Approval, allowedWhileRunning: true, requiresActiveThread: true),
            Descriptor(SlashCommandKind.Permissions, "permissions", ["permission"], "permissions <callId> <json-object>", "提交权限申请响应", SlashCommandCategory.Approval, allowedWhileRunning: true, requiresActiveThread: true),
            Descriptor(SlashCommandKind.Input, "input", [], "input <callId> <json-object>", "提交用户补录答案", SlashCommandCategory.Approval, allowedWhileRunning: true, requiresActiveThread: true),
            Descriptor(SlashCommandKind.Threads, "threads", [], "threads [--archived] [--all]", "打开线程选择器；脚本模式仅列出线程标题", SlashCommandCategory.Thread),
            Descriptor(SlashCommandKind.Thread, "thread", [], "thread delete --thread-id <id>", "删除指定线程或清空全部线程（需二次确认；也可不带斜杠输入）", SlashCommandCategory.Thread, SlashCommandConfirmationPolicy.SubcommandMayRequireConfirmation, subcommands: ["delete", "clear"]),
            Descriptor(SlashCommandKind.New, "new", [], "new", "创建新线程", SlashCommandCategory.Thread),
            Descriptor(SlashCommandKind.Fork, "fork", [], "fork <threadId>", "分叉线程", SlashCommandCategory.Thread),
            Descriptor(SlashCommandKind.Archive, "archive", [], "archive <threadId>", "归档线程", SlashCommandCategory.Thread, requiresActiveThread: false),
            Descriptor(SlashCommandKind.Rename, "rename", [], "rename <threadId> <name>", "重命名线程", SlashCommandCategory.Thread),
            Descriptor(SlashCommandKind.Resume, "resume", [], "resume <threadId>", "按 threadId 恢复线程", SlashCommandCategory.Thread),
            Descriptor(SlashCommandKind.Memory, "memory", [], "memory providers|spaces|overlay|search|filter|add|extract|import|export|bind-provider|forget|delete|supersede|review|feedback|citation", "管理和查看记忆 plane", SlashCommandCategory.Diagnostics, subcommands: ["providers", "spaces", "overlay", "search", "filter", "add", "extract", "import", "export", "bind-provider", "forget", "delete", "supersede", "review", "feedback", "citation"]),
            Descriptor(SlashCommandKind.Rpc, "rpc", [], "rpc <method> [params-json]", "优先走 formal surface，必要时回退内核 request method", SlashCommandCategory.Diagnostics),
            Descriptor(SlashCommandKind.State, "state", [], "state", "查看当前会话状态", SlashCommandCategory.Diagnostics),
            Descriptor(SlashCommandKind.Wait, "wait", [], "wait [milliseconds]", "固定等待一段时间", SlashCommandCategory.Diagnostics, allowedWhileRunning: true),
            Descriptor(SlashCommandKind.WaitEvent, "wait-event", ["waitevent"], "wait-event <event-kind> [seconds]", "等待下一条匹配事件（例如 ToolCallStarted）", SlashCommandCategory.Diagnostics, allowedWhileRunning: true),
            Descriptor(SlashCommandKind.WaitNextToolCall, "wait-next-tool-call", ["waitnexttoolcall"], "wait-next-tool-call [seconds]", "等待下一次工具调用开始", SlashCommandCategory.Diagnostics, allowedWhileRunning: true),
            Descriptor(SlashCommandKind.WaitComplete, "wait-complete", ["waitcomplete"], "wait-complete [timeout-seconds]", "等待当前运行中的回合结束", SlashCommandCategory.Diagnostics, allowedWhileRunning: true),
        ];

    private static SlashCommandDescriptor Descriptor(
        SlashCommandKind kind,
        string name,
        IReadOnlyList<string> aliases,
        string usage,
        string description,
        SlashCommandCategory category,
        SlashCommandConfirmationPolicy confirmationPolicy = SlashCommandConfirmationPolicy.None,
        bool visibleInHelp = true,
        bool allowedWhileRunning = false,
        bool requiresActiveThread = false,
        IReadOnlyList<string>? subcommands = null)
        => new(
            kind,
            name,
            aliases,
            usage,
            description,
            category,
            confirmationPolicy,
            visibleInHelp,
            allowedWhileRunning,
            requiresActiveThread,
            subcommands ?? Array.Empty<string>());

    private static void AppendHelpLine(StringBuilder builder, string usage, string description)
    {
        builder.Append("  /");
        builder.Append(usage.PadRight(37));
        builder.Append(' ');
        builder.AppendLine(description);
    }
}
