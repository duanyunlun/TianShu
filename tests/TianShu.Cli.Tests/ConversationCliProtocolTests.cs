using System.Linq;
using System.Reflection;

namespace TianShu.Cli.Tests;

public sealed class ConversationCliProtocolTests
{
    private static readonly Assembly ProbeAssembly = ReflectionTestHelper.LoadRequiredAssembly("TianShu.Cli");

    [Fact]
    public void PrimarySolution_ShouldIncludeCliTestsProject()
    {
        var solutionFile = Path.Combine(FindRepoRoot(), "TianShu.sln");
        var source = File.ReadAllText(solutionFile);

        Assert.Contains(
            "\"TianShu.Cli.Tests\", \"tests\\TianShu.Cli.Tests\\TianShu.Cli.Tests.csproj\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AgentRuntimeTests_Project_ShouldNotRetainConversationCliProtocolLock()
    {
        var oldFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime.Tests",
            "ConversationCliProtocolTests.cs");

        Assert.False(File.Exists(oldFile));
    }

    [Fact]
    public void AgentRuntimeTests_Project_ShouldNotRetainCliHostSpecificTests()
    {
        var repoRoot = FindRepoRoot();
        var oldFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.AgentRuntime.Tests", "TianShuCliProgramTests.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.AgentRuntime.Tests", "TianShuCliRealtimeRuntimeSmokeTests.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.AgentRuntime.Tests", "TianShuCliProcessAcceptanceTests.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.AgentRuntime.Tests", "TianShuCliExpansionTests.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.AgentRuntime.Tests", "TianShuCliIntegrationTests.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.AgentRuntime.Tests", "TianShuCliEndToEndConsumersTests.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.AgentRuntime.Tests", "ConsoleCaptureCollection.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.AgentRuntime.Tests", "EnvironmentVariablesCollection.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "TianShu.AgentRuntime.Tests", "WindowsConsoleProcess.cs"),
        };

        Assert.All(oldFiles, static file => Assert.False(File.Exists(file), $"旧 CLI 测试文件仍存在：{file}"));
    }

    [Fact]
    public void Parse_NoArgs_EntersInteractiveChatMode()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)Array.Empty<string>());
        Assert.NotNull(result);

        Assert.Equal(false, ReflectionTestHelper.GetProperty(result!, "ShowHelp"));
        var command = ReflectionTestHelper.GetProperty(result, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);
        Assert.Equal(false, ReflectionTestHelper.GetProperty(command, "CreateThreadOnInitialize"));
    }

    [Fact]
    public void Parse_RpcCommand_SetsMethod_And_ParamsJson()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "rpc", "--method", "model/list", "--params-json", "{\"limit\":5}", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RpcCommandOptions", command!.GetType().Name);
        Assert.Equal("model/list", ReflectionTestHelper.GetProperty(command, "Method"));
        Assert.Equal("{\"limit\":5}", ReflectionTestHelper.GetProperty(command, "ParamsJson"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void Parse_ThreadRenameCommand_SetsThreadId_And_Name()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "rename", "--thread-id", "thread-1", "--name", "新的标题" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ThreadCommandOptions", command!.GetType().Name);
        Assert.Equal("thread-1", ReflectionTestHelper.GetProperty(command, "ThreadId"));
        Assert.Equal("新的标题", ReflectionTestHelper.GetProperty(command, "Name"));
        Assert.Equal("Rename", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
    }

    [Fact]
    public void Parse_ChatCommand_SetsApproveAll_And_InitialMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "chat", "--approve-all", "--message", "你好", "--verbose-events" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "ApproveAll"));
        Assert.Equal("你好", ReflectionTestHelper.GetProperty(command, "InitialMessage"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "VerboseEvents"));
        Assert.Equal(false, ReflectionTestHelper.GetProperty(command, "CreateThreadOnInitialize"));
    }

    [Fact]
    public void Parse_SendKernelRuntimeLoop_WithApproveAll_KeepsExplicitApprovalState()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "send", "--message", "写入文件", "--kernel-runtime-loop", "--approve-all", "--approval-decision", "session" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("SendCommandOptions", command!.GetType().Name);
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "KernelRuntimeLoop"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "ApproveAll"));
        Assert.Equal("ApproveForSession", ReflectionTestHelper.GetProperty(command, "ApprovalDecision")?.ToString());
    }

    [Fact]
    public void Parse_SendKernelRuntimeLoop_EnableSubAgents_WithApproveAll_Succeeds()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "send", "--message", "拆分审计任务", "--kernel-runtime-loop", "--enable-subagents", "--approve-all" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("SendCommandOptions", command!.GetType().Name);
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "KernelRuntimeLoop"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "EnableSubAgents"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "ApproveAll"));
    }

    [Fact]
    public void Parse_SendKernelRuntimeLoop_EnableSubAgents_WithoutApproveAll_FailsClosed()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "send", "--message", "拆分审计任务", "--kernel-runtime-loop", "--enable-subagents" });
        Assert.NotNull(result);

        Assert.Null(ReflectionTestHelper.GetProperty(result!, "Command"));
        Assert.Contains(
            "--enable-subagents 需要同时启用 --approve-all",
            Assert.IsType<string>(ReflectionTestHelper.GetProperty(result, "ErrorMessage")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_SendKernelRuntimeLoop_EnableV07Capabilities_WithExplicitFlags_Succeeds()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "send",
                "--message",
                "审计仓库并读取记忆",
                "--kernel-runtime-loop",
                "--approve-all",
                "--enable-shell",
                "--enable-mcp",
                "--enable-memory",
            });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("SendCommandOptions", command!.GetType().Name);
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "KernelRuntimeLoop"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "EnableShell"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "EnableMcp"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "EnableMemory"));
    }

    [Fact]
    public void Parse_SendKernelRuntimeLoop_EnableShell_WithoutApproveAll_FailsClosed()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "send", "--message", "运行命令", "--kernel-runtime-loop", "--enable-shell" });
        Assert.NotNull(result);

        Assert.Null(ReflectionTestHelper.GetProperty(result!, "Command"));
        Assert.Contains(
            "--enable-shell 需要同时启用 --approve-all",
            Assert.IsType<string>(ReflectionTestHelper.GetProperty(result, "ErrorMessage")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_SendAppHostControlPlane_ReturnsRemovedPathDiagnostic()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "send", "--message", "验证互斥", "--kernel-runtime-loop", "--apphost-control-plane" });
        Assert.NotNull(result);

        Assert.Null(ReflectionTestHelper.GetProperty(result!, "Command"));
        Assert.Contains(
            "--apphost-control-plane 已移除",
            Assert.IsType<string>(ReflectionTestHelper.GetProperty(result, "ErrorMessage")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RootPrompt_EntersInteractiveChatMode()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "请解释当前目录" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);
        Assert.Equal("请解释当前目录", ReflectionTestHelper.GetProperty(command, "InitialMessage"));
    }

    [Fact]
    public void Parse_RootInteractiveOptions_SetsPrompt_Images_And_RuntimeFlags()
    {
        using var tempDirectory = new TestTempDirectory();
        var imagePath = Path.Combine(tempDirectory.Path, "diagram.png");
        File.WriteAllText(imagePath, "stub");

        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "-m", "gpt-5.2-codex", "-p", "work", "-s", "workspace-write", "-a", "on-request", "-C", tempDirectory.Path, "-i", imagePath, "--search", "请描述图片" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);
        Assert.Equal("请描述图片", ReflectionTestHelper.GetProperty(command, "InitialMessage"));
        Assert.Equal("gpt-5.2-codex", ReflectionTestHelper.GetProperty(command, "RuntimeModel"));
        Assert.Equal("work", ReflectionTestHelper.GetProperty(command, "ProfileName"));
        Assert.Equal("workspace-write", ReflectionTestHelper.GetProperty(command, "RuntimeSandboxMode"));
        Assert.Equal("on-request", ReflectionTestHelper.GetProperty(command, "RuntimeApprovalPolicy")?.ToString());
        Assert.Equal("live", ReflectionTestHelper.GetProperty(command, "WebSearchMode"));
        Assert.Equal(tempDirectory.Path, ReflectionTestHelper.GetProperty(command, "WorkingDirectory"));

        var imagePaths = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(command, "ImagePaths"));
        Assert.Single(imagePaths.Cast<object>());
    }

    [Fact]
    public void Parse_ChatCommand_SetsApprovalDecision()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "chat", "--message", "你好", "--approval-decision", "always" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ApproveAndRemember", ReflectionTestHelper.GetProperty(command!, "ApprovalDecision")?.ToString());
    }

    [Fact]
    public void Parse_ChatCommand_PromptAndMessageConflict_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "chat", "--message", "你好", "继续" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("prompt 不能同时通过位置参数和 --message 提供", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TopLevelResumeCommand_SetsStartupResumeAction()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "resume", "--last", "--cwd", "D:\\Repo", "--config-file", ".\\config.toml" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);
        Assert.Equal("Resume", ReflectionTestHelper.GetProperty(command, "StartupThreadAction")?.ToString());
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "StartupThreadUseLast"));
        Assert.Equal(false, ReflectionTestHelper.GetProperty(command, "CreateThreadOnInitialize"));
    }

    [Fact]
    public void Parse_TopLevelResumeCommand_PassesMessage_And_RuntimeFlagsIntoChatOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "resume", "--last", "-m", "gpt-5.2-codex", "-s", "workspace-write", "-a", "on-request", "--message", "继续修复" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);
        Assert.Equal("Resume", ReflectionTestHelper.GetProperty(command, "StartupThreadAction")?.ToString());
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "StartupThreadUseLast"));
        Assert.Equal("继续修复", ReflectionTestHelper.GetProperty(command, "InitialMessage"));
        Assert.Equal("gpt-5.2-codex", ReflectionTestHelper.GetProperty(command, "RuntimeModel"));
        Assert.Equal("workspace-write", ReflectionTestHelper.GetProperty(command, "RuntimeSandboxMode"));
        Assert.Equal("on-request", ReflectionTestHelper.GetProperty(command, "RuntimeApprovalPolicy")?.ToString());
    }

    [Fact]
    public void Parse_TopLevelForkCommand_SetsStartupForkAction_AndExplicitTarget()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "fork", "thread-title-001", "--all", "--protocol", "jsonl" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);
        Assert.Equal("Fork", ReflectionTestHelper.GetProperty(command, "StartupThreadAction")?.ToString());
        Assert.Equal("thread-title-001", ReflectionTestHelper.GetProperty(command, "StartupThreadTarget"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "StartupThreadShowAll"));
        Assert.Equal("Jsonl", ReflectionTestHelper.GetProperty(command, "OutputProtocol")?.ToString());
    }

    [Fact]
    public void Parse_RootInteractiveFullAuto_KeepsWorkspaceWriteSandbox_AndOnRequestApproval_WhenExplicitSandboxAppearsLater()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "--full-auto", "--sandbox", "danger-full-access", "继续" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);
        Assert.Equal("workspace-write", ReflectionTestHelper.GetProperty(command, "RuntimeSandboxMode"));
        Assert.Equal("on-request", ReflectionTestHelper.GetProperty(command, "RuntimeApprovalPolicy")?.ToString());
    }

    [Fact]
    public void Parse_RootInteractiveDangerousMode_KeepsDangerFullAccess_WhenExplicitSandboxAppearsLater()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "--dangerously-bypass-approvals-and-sandbox", "--sandbox", "workspace-write", "继续" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);
        Assert.Equal("danger-full-access", ReflectionTestHelper.GetProperty(command, "RuntimeSandboxMode"));
        Assert.Equal("never", ReflectionTestHelper.GetProperty(command, "RuntimeApprovalPolicy")?.ToString());
    }

    [Fact]
    public void Parse_RootInteractiveFlagsBeforeExec_DispatchesExecCommand()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "-m", "gpt-5.2-codex", "exec", "2+2" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ExecCommandOptions", command!.GetType().Name);
        Assert.Equal("2+2", ReflectionTestHelper.GetProperty(command, "Prompt"));
        Assert.Equal("gpt-5.2-codex", ReflectionTestHelper.GetProperty(command, "RuntimeModel"));
    }

    [Fact]
    public void Parse_ExecPrompt_ReturnsExecCommandOptions()
    {
        using var tempDirectory = new TestTempDirectory();
        var imagePath = Path.Combine(tempDirectory.Path, "diagram.png");
        File.WriteAllText(imagePath, "stub");

        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "exec", "--json", "-o", ".\\last-message.md", "-i", imagePath, "-m", "gpt-5.2-codex", "-s", "workspace-write", "2+2" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ExecCommandOptions", command!.GetType().Name);
        Assert.Equal("UserTurn", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("2+2", ReflectionTestHelper.GetProperty(command, "Prompt"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
        Assert.Equal("gpt-5.2-codex", ReflectionTestHelper.GetProperty(command, "RuntimeModel"));
        Assert.Equal("workspace-write", ReflectionTestHelper.GetProperty(command, "RuntimeSandboxMode"));

        var imagePaths = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(command, "ImagePaths"));
        Assert.Single(imagePaths.Cast<object>());
    }

    [Fact]
    public void Parse_ExecPrompt_WithEphemeralAndOutputSchema_ReturnsExecCommandOptions()
    {
        using var tempDirectory = new TestTempDirectory();
        var schemaPath = Path.Combine(tempDirectory.Path, "schema.json");
        var writableRoot = Path.Combine(tempDirectory.Path, "workspace-b");
        File.WriteAllText(schemaPath, """{"type":"object","properties":{"answer":{"type":"string"}}}""");
        Directory.CreateDirectory(writableRoot);

        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "exec", "--ephemeral", "--output-schema", schemaPath, "--add-dir", writableRoot, "2+2" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ExecCommandOptions", command!.GetType().Name);
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "Ephemeral"));
        Assert.Equal(schemaPath, ReflectionTestHelper.GetProperty(command, "OutputSchemaFilePath"));
        var additionalWritableDirectories = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(command, "AdditionalWritableDirectories"));
        Assert.Equal([writableRoot], additionalWritableDirectories.Cast<object>());
    }

    [Fact]
    public void Parse_ExecResumeLast_ReinterpretsPositionalAsPrompt()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "exec", "resume", "--last", "--json", "--dangerously-bypass-approvals-and-sandbox", "继续修复" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ExecCommandOptions", command!.GetType().Name);
        Assert.Equal("Resume", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "UseLast"));
        Assert.Equal("继续修复", ReflectionTestHelper.GetProperty(command, "Prompt"));
        Assert.Null(ReflectionTestHelper.GetProperty(command, "ResumeTarget"));
        Assert.Equal("danger-full-access", ReflectionTestHelper.GetProperty(command, "RuntimeSandboxMode"));
    }

    [Fact]
    public void Parse_ExecFullAuto_KeepsWorkspaceWriteSandbox_WhenExplicitSandboxAppearsLater()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "exec", "--full-auto", "--sandbox", "danger-full-access", "继续" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ExecCommandOptions", command!.GetType().Name);
        Assert.Equal("workspace-write", ReflectionTestHelper.GetProperty(command, "RuntimeSandboxMode"));
    }

    [Fact]
    public void Parse_ExecDangerousMode_KeepsDangerFullAccess_WhenExplicitSandboxAppearsLater()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "exec", "--dangerously-bypass-approvals-and-sandbox", "--sandbox", "workspace-write", "继续" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ExecCommandOptions", command!.GetType().Name);
        Assert.Equal("danger-full-access", ReflectionTestHelper.GetProperty(command, "RuntimeSandboxMode"));
    }

    [Fact]
    public void Parse_ExecReviewUncommitted_ReturnsExecCommandOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "exec", "review", "--uncommitted" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ExecCommandOptions", command!.GetType().Name);
        Assert.Equal("Review", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "ReviewUncommitted"));
    }

    [Fact]
    public void Parse_ExecReviewBaseBranch_ReturnsExecCommandOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "exec", "review", "--base", "main" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ExecCommandOptions", command!.GetType().Name);
        Assert.Equal("Review", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("main", ReflectionTestHelper.GetProperty(command, "ReviewBaseBranch"));
    }

    [Fact]
    public void Parse_ExecReviewCommitWithTitle_ReturnsExecCommandOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "exec", "review", "--commit", "abc123", "--title", "Fix bug" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ExecCommandOptions", command!.GetType().Name);
        Assert.Equal("Review", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("abc123", ReflectionTestHelper.GetProperty(command, "ReviewCommit"));
        Assert.Equal("Fix bug", ReflectionTestHelper.GetProperty(command, "ReviewCommitTitle"));
    }

    [Fact]
    public void Parse_ExecReviewPrompt_ReturnsExecCommandOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "exec", "review", "请重点看回归风险" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ExecCommandOptions", command!.GetType().Name);
        Assert.Equal("Review", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("请重点看回归风险", ReflectionTestHelper.GetProperty(command, "ReviewPrompt"));
    }

    [Fact]
    public void Parse_TopLevelReviewUncommitted_ReturnsExecCommandOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "review", "--uncommitted" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ExecCommandOptions", command!.GetType().Name);
        Assert.Equal("Review", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "ReviewUncommitted"));
    }

    [Fact]
    public void Parse_TopLevelReviewStart_StillReturnsRuntimeSurfaceCommandOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "review", "start", "--thread-id", "thread-review-1", "--target", "custom", "--instructions", "检查 runner" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("ReviewStart", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("thread-review-1", ReflectionTestHelper.GetProperty(command, "ThreadId"));
        Assert.Equal("custom", ReflectionTestHelper.GetProperty(command, "ReviewTargetType"));
    }

    [Fact]
    public void Parse_CodeModeExec_Subcommand_StillReturnsCodeModeCommandOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "code-mode", "exec", "--input", "print('hi')" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("CodeModeCommandOptions", command!.GetType().Name);
        Assert.Equal("Exec", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
    }

    [Fact]
    public void Parse_RealtimeHandoffOutputCommand_SetsHandoffId_And_Output()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "realtime", "handoff-output", "--thread-id", "thread-rt-1", "--session-id", "session-rt-1", "--handoff-id", "call-rt-1", "--output", "delegated result" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RealtimeCommandOptions", command!.GetType().Name);
        Assert.Equal("HandoffOutput", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("thread-rt-1", ReflectionTestHelper.GetProperty(command, "ThreadId"));
        Assert.Equal("session-rt-1", ReflectionTestHelper.GetProperty(command, "SessionId"));
        Assert.Equal("call-rt-1", ReflectionTestHelper.GetProperty(command, "HandoffId"));
        Assert.Equal("delegated result", ReflectionTestHelper.GetProperty(command, "Output"));
    }

    [Fact]
    public void Parse_ChatCommand_SetsPermissionsJsonPath()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "chat", "--message", "你好", "--permissions-json", ".\\permissions.json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.EndsWith("permissions.json", Assert.IsType<string>(ReflectionTestHelper.GetProperty(command!, "PermissionsJsonPath")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ChatCommand_SetsWebSearchMode_And_ResumeLatest()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "chat", "--message", "你好", "--web-search", "disabled", "--resume-latest" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);
        Assert.Equal("disabled", ReflectionTestHelper.GetProperty(command, "WebSearchMode"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "ResumeLatestThread"));
    }

    [Fact]
    public void Parse_ChatCommand_SetsArtifactsRoot()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "chat", "--message", "你好", "--artifacts", ".\\chat-artifacts" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        var artifactsRoot = Assert.IsType<string>(ReflectionTestHelper.GetProperty(command!, "ArtifactsRoot"));
        Assert.EndsWith("chat-artifacts", artifactsRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_SendCommand_DefaultArtifactsRootUsesTianShuRuntimeWorkspaceBucket()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        using var environment = new EnvironmentVariableScope(("TIANSHU_HOME", Path.Combine(Path.GetTempPath(), $"tianshu-cli-artifacts-{Guid.NewGuid():N}")));
        var workspace = Path.Combine(Path.GetTempPath(), $"tianshu-cli-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);

        try
        {
            var result = ReflectionTestHelper.InvokeStaticMethod(
                parserType,
                "Parse",
                (object)new[] { "send", "--message", "你好", "--cwd", workspace });
            Assert.NotNull(result);

            var command = ReflectionTestHelper.GetProperty(result!, "Command");
            Assert.NotNull(command);
            var artifactsRoot = Assert.IsType<string>(ReflectionTestHelper.GetProperty(command!, "ArtifactsRoot"));
            var artifactsRootExplicit = Assert.IsType<bool>(ReflectionTestHelper.GetProperty(command!, "ArtifactsRootExplicit"));

            Assert.Contains(Path.Combine("runtime", "runs", "workspace-"), artifactsRoot, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(Path.Combine("send"), artifactsRoot, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".tianshu-cli", artifactsRoot, StringComparison.OrdinalIgnoreCase);
            Assert.False(artifactsRootExplicit);
            Assert.False(Directory.Exists(Path.Combine(workspace, ".tianshu-cli")));
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public void Parse_SendCommand_ExplicitArtifactsRootIsMarked()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var workspace = Path.Combine(Path.GetTempPath(), $"tianshu-cli-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);

        try
        {
            var result = ReflectionTestHelper.InvokeStaticMethod(
                parserType,
                "Parse",
                (object)new[] { "send", "--message", "你好", "--cwd", workspace, "--artifacts", ".\\send-artifacts" });
            Assert.NotNull(result);

            var command = ReflectionTestHelper.GetProperty(result!, "Command");
            Assert.NotNull(command);
            var artifactsRoot = Assert.IsType<string>(ReflectionTestHelper.GetProperty(command!, "ArtifactsRoot"));
            var artifactsRootExplicit = Assert.IsType<bool>(ReflectionTestHelper.GetProperty(command!, "ArtifactsRootExplicit"));

            Assert.EndsWith("send-artifacts", artifactsRoot, StringComparison.OrdinalIgnoreCase);
            Assert.True(artifactsRootExplicit);
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public void Parse_ChatCommand_SetsConfigOverrideAndConfigFile()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "chat", "--message", "你好", "-c", "model=gpt-5-mini", "--config-file", ".\\chat-config.toml" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        var configFilePath = Assert.IsType<string>(ReflectionTestHelper.GetProperty(command!, "ConfigFilePath"));
        Assert.EndsWith("chat-config.toml", configFilePath, StringComparison.OrdinalIgnoreCase);
        var configOverrides = Assert.IsAssignableFrom<System.Collections.IDictionary>(ReflectionTestHelper.GetProperty(command, "ConfigOverrides"));
        Assert.Equal("gpt-5-mini", configOverrides["model"]);
    }

    [Fact]
    public void Parse_FollowUpCommand_SetsMode_And_ResumeLatest()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "follow-up", "--message", "继续", "--mode", "queue", "--resume-latest" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("FollowUpCliCommandOptions", command!.GetType().Name);
        Assert.Equal("继续", ReflectionTestHelper.GetProperty(command, "Message"));
        Assert.Equal("Queue", ReflectionTestHelper.GetProperty(command, "Mode")?.ToString());
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "ResumeLatestThread"));
    }

    [Fact]
    public void Parse_FollowUpCommand_SetsKernelRuntimeHostControlOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "follow-up",
                "--message",
                "继续",
                "--mode",
                "steer",
                "--kernel-runtime-loop",
                "--resume-thread-id",
                "thread-1",
                "--turn-id",
                "turn-1",
                "--checkpoint-ref",
                "checkpoint://kernel-runtime/thread-1/turn-1/terminal",
                "--resume-token",
                "resume-token-1",
                "--turn-timeout-seconds",
                "17",
            });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command!, "KernelRuntimeLoop"));
        Assert.Equal("thread-1", ReflectionTestHelper.GetProperty(command, "ResumeThreadId"));
        Assert.Equal("turn-1", ReflectionTestHelper.GetProperty(command, "TurnId"));
        Assert.Equal("checkpoint://kernel-runtime/thread-1/turn-1/terminal", ReflectionTestHelper.GetProperty(command, "CheckpointRef"));
        Assert.Equal("resume-token-1", ReflectionTestHelper.GetProperty(command, "ResumeToken"));
        Assert.Equal(17, ReflectionTestHelper.GetProperty(command, "TurnTimeoutSeconds"));
    }

    [Fact]
    public void Parse_FollowUpKernelRuntimeSteer_EnableV07Capabilities_WithExplicitFlags_Succeeds()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "follow-up",
                "--message",
                "继续并检查工具",
                "--mode",
                "steer",
                "--kernel-runtime-loop",
                "--resume-thread-id",
                "thread-1",
                "--approve-all",
                "--enable-shell",
                "--enable-mcp",
                "--enable-memory",
            });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("FollowUpCliCommandOptions", command!.GetType().Name);
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "KernelRuntimeLoop"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "ApproveAll"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "EnableShell"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "EnableMcp"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "EnableMemory"));
    }

    [Fact]
    public void Parse_FollowUpKernelRuntimeSteer_EnableShell_WithoutApproveAll_FailsClosed()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "follow-up",
                "--message",
                "运行命令",
                "--mode",
                "steer",
                "--kernel-runtime-loop",
                "--resume-thread-id",
                "thread-1",
                "--enable-shell",
            });
        Assert.NotNull(result);

        Assert.Null(ReflectionTestHelper.GetProperty(result!, "Command"));
        Assert.Contains(
            "--enable-shell 需要同时启用 --approve-all",
            Assert.IsType<string>(ReflectionTestHelper.GetProperty(result, "ErrorMessage")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_FollowUpKernelRuntimeResume_AllowsCheckpointWithoutMessageOrMode()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "follow-up",
                "--kernel-runtime-loop",
                "--resume-thread-id",
                "thread-1",
                "--checkpoint-ref",
                "checkpoint://kernel-runtime/thread-1/turn-1/terminal",
                "--resume-token",
                "resume-token-1",
            });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command!, "KernelRuntimeLoop"));
        Assert.Equal("kernel-runtime.resume", ReflectionTestHelper.GetProperty(command, "Message"));
        Assert.Equal("Queue", ReflectionTestHelper.GetProperty(command, "Mode")?.ToString());
        Assert.Equal("checkpoint://kernel-runtime/thread-1/turn-1/terminal", ReflectionTestHelper.GetProperty(command, "CheckpointRef"));
    }

    [Fact]
    public void Parse_FollowUpCommand_SetsApprovalDecision()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "follow-up", "--message", "继续", "--mode", "queue", "--approval-decision", "session" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ApproveForSession", ReflectionTestHelper.GetProperty(command!, "ApprovalDecision")?.ToString());
    }

    [Fact]
    public void Parse_InvalidThreadVerb_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "prune", "--thread-id", "thread-1" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("不支持的 thread 子命令", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ChatCommand_InvalidProtocol_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "chat", "--message", "你好", "--protocol", "bad" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("--protocol 必须是 human 或 jsonl", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ChatCommand_ConflictingResumeOptions_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "chat", "--message", "你好", "--resume-thread-id", "thread-1", "--resume-latest" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("--resume-thread-id 与 --resume-latest 不能同时使用", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TopLevelResumeCommand_TargetAndLast_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "resume", "thread-1", "--last" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("resume 不能同时提供显式目标和 --last", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_FollowUpCommand_MissingMessage_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "follow-up", "--mode", "queue" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("缺少必填参数：--message <text>", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RpcCommand_MissingMethod_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "rpc", "--params-json", "{\"limit\":5}" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("缺少必填参数：--method <name>", errorMessage, StringComparison.Ordinal);
    }
    [Fact]
    public void Parse_FollowUpCommand_InvalidMode_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "follow-up", "--mode", "bad", "--message", "继续" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("--mode 必须是 queue、steer 或 interrupt", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ChatCommand_ResumeLatestAnyCwdWithoutResumeLatest_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "chat", "--message", "你好", "--resume-latest-any-cwd" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("--resume-latest-any-cwd 只能与 --resume-latest 一起使用", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ThreadCommand_WithoutSubcommand_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("thread 需要子命令", errorMessage, StringComparison.Ordinal);
    }
    [Fact]
    public void Parse_ThreadForkCommand_WithoutThreadId_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "fork" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("缺少必填参数：--thread-id <id>", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ThreadReadCommand_WithoutThreadId_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "read", "--include-turns" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("缺少必填参数：--thread-id <id>", errorMessage, StringComparison.Ordinal);
    }
    [Fact]
    public void Parse_ThreadRenameCommand_WithoutName_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "rename", "--thread-id", "thread-1" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("缺少必填参数：--name <name>", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ThreadCompactCommand_InvalidKeepRecentTurns_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "compact", "--thread-id", "thread-1", "--keep-recent-turns", "0" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("--keep-recent-turns 必须是大于 0 的整数", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ThreadRollbackCommand_WithoutNumTurns_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "rollback", "--thread-id", "thread-1" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("缺少必填参数：--num-turns <n>", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ThreadMetadataCommand_SetAndClearSameField_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "metadata", "--thread-id", "thread-1", "--git-sha", "abc", "--clear-git-sha" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("同一 git 字段不能同时设置值和清空", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ThreadMetadataCommand_WithoutGitChanges_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "metadata", "--thread-id", "thread-1" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("thread metadata 至少需要一个 git 字段变更", errorMessage, StringComparison.Ordinal);
    }
    [Fact]
    public void GetHelpText_IncludesChat_And_Rpc_Commands()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.CliCommandParser");
        var helpText = Assert.IsType<string>(ReflectionTestHelper.InvokeStaticMethod(parserType, "GetHelpText"));
        Assert.Contains("chat", helpText, StringComparison.Ordinal);
        Assert.Contains("resume", helpText, StringComparison.Ordinal);
        Assert.Contains("fork", helpText, StringComparison.Ordinal);
        Assert.Contains("rpc", helpText, StringComparison.Ordinal);
        Assert.Contains("thread", helpText, StringComparison.Ordinal);
        Assert.Contains("--web-search", helpText, StringComparison.Ordinal);
        Assert.Contains("--permissions-json", helpText, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 TianShu.sln。");
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> previousValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope(params (string Name, string? Value)[] values)
        {
            foreach (var (name, value) in values)
            {
                previousValues[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in previousValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
