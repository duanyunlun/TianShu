using System.Text.Json;
using System.Collections.Concurrent;
using System.Collections;
using System.Reflection;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using TianShu.AppHost.Tools;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerTests
{
    [Fact]
    public async Task RunAsync_ShouldHandleInitializeAndThreadRequests()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var input = string.Join(
            Environment.NewLine,
            """{"id":1,"method":"initialize","params":{}}""",
            """{"id":2,"method":"thread/start","params":{"cwd":"D:/Repo","sessionSource":"cli"}}""",
            """{"id":3,"method":"thread/list","params":{"limit":10}}""");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            Assert.True(lines.Length >= 3);

            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.All(messages, static message => Assert.False(message.RootElement.TryGetProperty("jsonrpc", out _)));
                var init = messages.Single(x => IsResponseId(x.RootElement, 1));
                Assert.True(init.RootElement.TryGetProperty("result", out var initResult));
                Assert.Equal("tianshu-dotnet-kernel/0.1.0", initResult.GetProperty("userAgent").GetString());
                var initializeProperties = initResult.EnumerateObject().Select(static property => property.Name).ToArray();
                Assert.Equal(["userAgent", "tianShuHome", "platformFamily", "platformOs"], initializeProperties);

                var threadStart = messages.Single(x => IsResponseId(x.RootElement, 2));
                var threadId = threadStart.RootElement
                    .GetProperty("result")
                    .GetProperty("thread")
                    .GetProperty("id")
                    .GetString();
                Assert.False(string.IsNullOrWhiteSpace(threadId));

                var threadList = messages.Single(x => IsResponseId(x.RootElement, 3));
                var list = threadList.RootElement
                    .GetProperty("result")
                    .GetProperty("data");
                Assert.True(list.ValueKind == JsonValueKind.Array);
                Assert.True(list.GetArrayLength() >= 1);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReturnInitializeEnvironmentMetadata()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var input = """{"id":1,"method":"initialize","params":{"clientInfo":{"name":"tianshu_cli","title":"TianShu CLI","version":"0.1.0"}}}""";

        try
        {
            Directory.CreateDirectory(tianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var init = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal("tianshu_cli/0.1.0", init.GetProperty("userAgent").GetString());
                Assert.Equal(Path.GetFullPath(tianShuHome), init.GetProperty("tianShuHome").GetString());
                Assert.Equal(OperatingSystem.IsWindows() ? "windows" : "unix", init.GetProperty("platformFamily").GetString());
                Assert.Equal(
                    OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" : "unknown",
                    init.GetProperty("platformOs").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldUseClientInfoNameAsInitializeUserAgentOriginator()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var input = "{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"tianshu_cli\",\"title\":\"TianShu CLI\",\"version\":\"0.1.0\"}}}";

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var init = messages.Single(x => IsResponseId(x.RootElement, 1));
                Assert.Equal("tianshu_cli/0.1.0", init.RootElement.GetProperty("result").GetProperty("userAgent").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadStartReturnsFreshThread_ShouldKeepRolloutUnmaterialized()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var cwd = root.Replace("\\", "/");
        var input = string.Join(
            Environment.NewLine,
            """{"id":1,"method":"initialize","params":{}}""",
            $@"{{""id"":2,""method"":""thread/start"",""params"":{{""cwd"":""{cwd}"",""sessionSource"":""cli""}}}}");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var startThread = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread");
                var threadId = startThread.GetProperty("id").GetString();
                var rolloutPath = startThread.GetProperty("path").GetString();

                Assert.False(string.IsNullOrWhiteSpace(threadId));
                Assert.True(Path.IsPathRooted(rolloutPath));
                Assert.False(File.Exists(rolloutPath!));
                Assert.Equal("idle", startThread.GetProperty("status").GetProperty("type").GetString());

                var startedThread = messages.Single(x => IsNotificationMethod(x.RootElement, "thread/started")).RootElement
                    .GetProperty("params")
                    .GetProperty("thread");
                Assert.Equal(threadId, startedThread.GetProperty("id").GetString());
                Assert.Equal(rolloutPath, startedThread.GetProperty("path").GetString());
                Assert.False(File.Exists(rolloutPath!));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadReadWithoutTurnsForFreshLoadedThread_ShouldReturnPrecomputedPathWithoutMaterialization()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var cwd = root.Replace("\\", "/");
        Channel<string>? inputLines = null;
        ChannelTextReader? reader = null;
        ChannelTextWriter? writer = null;
        Task? runTask = null;

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            inputLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            reader = new ChannelTextReader(inputLines.Reader);
            writer = new ChannelTextWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            runTask = server.RunAsync(CancellationToken.None);

            await KernelAppServerTestProtocol.InitializeAsync(
                inputLines.Writer,
                writer.Lines,
                TimeSpan.FromSeconds(5));

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/start"",""params"":{{""cwd"":""{cwd}"",""sessionSource"":""cli""}}}}");
            var startResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));
            using var startMessage = JsonDocument.Parse(startResponse);
            var startThread = startMessage.RootElement.GetProperty("result").GetProperty("thread");
            var threadId = startThread.GetProperty("id").GetString();
            var rolloutPath = startThread.GetProperty("path").GetString();

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":2,""method"":""thread/read"",""params"":{{""threadId"":""{threadId}"",""includeTurns"":false}}}}");
            var readResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(5));
            using var readMessage = JsonDocument.Parse(readResponse);
            var readThread = readMessage.RootElement.GetProperty("result").GetProperty("thread");
            Assert.Equal(threadId, readThread.GetProperty("id").GetString());
            Assert.Equal(rolloutPath, readThread.GetProperty("path").GetString());
            Assert.Equal(string.Empty, readThread.GetProperty("preview").GetString());
            Assert.Equal("idle", readThread.GetProperty("status").GetProperty("type").GetString());
            Assert.Empty(readThread.GetProperty("turns").EnumerateArray());
            Assert.False(File.Exists(rolloutPath!));
        }
        finally
        {
            if (inputLines is not null)
            {
                inputLines.Writer.TryComplete();
            }

            if (runTask is not null)
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            writer?.Dispose();
            reader?.Dispose();
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadReadIncludeTurnsBeforeFirstUserMessage_ShouldRejectUnmaterializedLoadedThread()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var cwd = root.Replace("\\", "/");
        Channel<string>? inputLines = null;
        ChannelTextReader? reader = null;
        ChannelTextWriter? writer = null;
        Task? runTask = null;

        try
        {
            inputLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            var threadStore = new KernelThreadStore(storePath);
            reader = new ChannelTextReader(inputLines.Reader);
            writer = new ChannelTextWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            runTask = server.RunAsync(CancellationToken.None);

            await KernelAppServerTestProtocol.InitializeAsync(
                inputLines.Writer,
                writer.Lines,
                TimeSpan.FromSeconds(5));

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/start"",""params"":{{""cwd"":""{cwd}"",""sessionSource"":""cli""}}}}");
            var startResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));
            using var startMessage = JsonDocument.Parse(startResponse);
            var thread = startMessage.RootElement.GetProperty("result").GetProperty("thread");
            var threadId = thread.GetProperty("id").GetString();
            var rolloutPath = thread.GetProperty("path").GetString();

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":2,""method"":""thread/read"",""params"":{{""threadId"":""{threadId}"",""includeTurns"":true}}}}");
            var readResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(5));
            using var readMessage = JsonDocument.Parse(readResponse);
            var error = readMessage.RootElement.GetProperty("error");

            Assert.Equal(-32600, error.GetProperty("code").GetInt32());
            Assert.Equal(
                $"thread {threadId} is not materialized yet; includeTurns is unavailable before first user message",
                error.GetProperty("message").GetString());
            Assert.False(File.Exists(rolloutPath!));
        }
        finally
        {
            if (inputLines is not null)
            {
                inputLines.Writer.TryComplete();
            }

            if (runTask is not null)
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            writer?.Dispose();
            reader?.Dispose();
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadReadUsesSnakeCaseIncludeTurns_ShouldReturnTurns()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000999";
        KernelThreadStore? setupStore = null;

        try
        {
            setupStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                threadId,
                root,
                ("turn_snake_case_001", "用户问题", "助手回答"));
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);

            var input = """{"jsonrpc":"2.0","id":1,"method":"thread/read","params":{"threadId":"00000000-0000-7000-8000-000000000999","include_turns":true}}""";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var response = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .Single(static message => IsResponseId(message.RootElement, 1));
            try
            {
                var thread = response.RootElement.GetProperty("result").GetProperty("thread");
                var turns = thread.GetProperty("turns");
                Assert.NotEmpty(turns.EnumerateArray());
            }
            finally
            {
                response.Dispose();
            }
        }
        finally
        {
            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadReadUsesUnloadedThread_ShouldKeepStatusNotLoaded_AndNotAppearInLoadedList()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000998";
        KernelThreadStore? setupStore = null;

        try
        {
            setupStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                threadId,
                root,
                ("turn_unloaded_read_001", "用户问题", "助手回答"));
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/read"",""params"":{{""threadId"":""{threadId}"",""includeTurns"":false}}}}",
                """{"jsonrpc":"2.0","id":2,"method":"thread/loaded/list","params":{"limit":10}}""",
                """{"jsonrpc":"2.0","id":3,"method":"thread/list","params":{"limit":10,"modelProviders":[]}}""");
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var readThread = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread");
                Assert.Equal(threadId, readThread.GetProperty("id").GetString());
                Assert.Equal("notLoaded", readThread.GetProperty("status").GetProperty("type").GetString());

                var loadedThreadIds = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement
                    .GetProperty("result")
                    .GetProperty("data")
                    .EnumerateArray()
                    .Select(static item => item.GetString())
                    .ToArray();
                Assert.Empty(loadedThreadIds);

                var listedThread = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement
                    .GetProperty("result")
                    .GetProperty("data")
                    .EnumerateArray()
                    .Single();
                Assert.Equal(threadId, listedThread.GetProperty("id").GetString());
                Assert.Equal("notLoaded", listedThread.GetProperty("status").GetProperty("type").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadResumeRequiresMaterializedRollout_ShouldRejectFreshStartedThread()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var cwd = root.Replace("\\", "/");
        Channel<string>? inputLines = null;
        ChannelTextReader? reader = null;
        ChannelTextWriter? writer = null;
        Task? runTask = null;

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            inputLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            reader = new ChannelTextReader(inputLines.Reader);
            writer = new ChannelTextWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            runTask = server.RunAsync(CancellationToken.None);

            await KernelAppServerTestProtocol.InitializeAsync(
                inputLines.Writer,
                writer.Lines,
                TimeSpan.FromSeconds(5));

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/start"",""params"":{{""cwd"":""{cwd}"",""sessionSource"":""cli""}}}}");
            var startResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));
            using var startMessage = JsonDocument.Parse(startResponse);
            var startThread = startMessage.RootElement.GetProperty("result").GetProperty("thread");
            var threadId = startThread.GetProperty("id").GetString();
            var rolloutPath = threadStore.RolloutRecorder.GetRolloutPath(threadId!);

            Assert.False(string.IsNullOrWhiteSpace(threadId));
            Assert.False(File.Exists(rolloutPath));

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":2,""method"":""thread/resume"",""params"":{{""threadId"":""{threadId}""}}}}");
            var resumeResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(5));
            using var resumeMessage = JsonDocument.Parse(resumeResponse);
            var error = resumeMessage.RootElement.GetProperty("error");

            Assert.Equal(-32600, error.GetProperty("code").GetInt32());
            Assert.Equal($"no rollout found for thread id {threadId}", error.GetProperty("message").GetString());
            Assert.False(File.Exists(rolloutPath));
        }
        finally
        {
            if (inputLines is not null)
            {
                inputLines.Writer.TryComplete();
            }

            if (runTask is not null)
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            writer?.Dispose();
            reader?.Dispose();
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadForkRequiresMaterializedRollout_ShouldRejectFreshStartedThread()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var cwd = root.Replace("\\", "/");
        Channel<string>? inputLines = null;
        ChannelTextReader? reader = null;
        ChannelTextWriter? writer = null;
        Task? runTask = null;

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            inputLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            reader = new ChannelTextReader(inputLines.Reader);
            writer = new ChannelTextWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            runTask = server.RunAsync(CancellationToken.None);

            await KernelAppServerTestProtocol.InitializeAsync(
                inputLines.Writer,
                writer.Lines,
                TimeSpan.FromSeconds(5));

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/start"",""params"":{{""cwd"":""{cwd}"",""sessionSource"":""cli""}}}}");
            var startResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));
            using var startMessage = JsonDocument.Parse(startResponse);
            var startThread = startMessage.RootElement.GetProperty("result").GetProperty("thread");
            var threadId = startThread.GetProperty("id").GetString();
            var rolloutPath = threadStore.RolloutRecorder.GetRolloutPath(threadId!);

            Assert.False(string.IsNullOrWhiteSpace(threadId));
            Assert.False(File.Exists(rolloutPath));

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":2,""method"":""thread/fork"",""params"":{{""threadId"":""{threadId}""}}}}");
            var forkResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(5));
            using var forkMessage = JsonDocument.Parse(forkResponse);
            var error = forkMessage.RootElement.GetProperty("error");

            Assert.Equal(-32600, error.GetProperty("code").GetInt32());
            Assert.Equal($"no rollout found for thread id {threadId}", error.GetProperty("message").GetString());
            Assert.False(File.Exists(rolloutPath));
        }
        finally
        {
            if (inputLines is not null)
            {
                inputLines.Writer.TryComplete();
            }

            if (runTask is not null)
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            writer?.Dispose();
            reader?.Dispose();
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadListReadsFreshLoadedThread_ShouldNotMaterializeRollout()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var cwd = root.Replace("\\", "/");
        var input = string.Join(
            Environment.NewLine,
            """{"id":1,"method":"initialize","params":{}}""",
            $@"{{""id"":2,""method"":""thread/start"",""params"":{{""cwd"":""{cwd}"",""sessionSource"":""cli""}}}}",
            """{"id":3,"method":"thread/list","params":{"limit":10,"modelProviders":[]}}""");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var startThread = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread");
                var threadId = startThread.GetProperty("id").GetString();
                var rolloutPath = startThread.GetProperty("path").GetString();

                var listedThread = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement
                    .GetProperty("result")
                    .GetProperty("data")
                    .EnumerateArray()
                    .Single();
                Assert.Equal(threadId, listedThread.GetProperty("id").GetString());
                Assert.Equal(rolloutPath, listedThread.GetProperty("path").GetString());
                Assert.Equal("idle", listedThread.GetProperty("status").GetProperty("type").GetString());
                Assert.False(File.Exists(rolloutPath!));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenEphemeralThreadStartReturnsFreshThread_ShouldRemainPathless()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var cwd = root.Replace("\\", "/");
        var input = string.Join(
            Environment.NewLine,
            """{"id":1,"method":"initialize","params":{}}""",
            $@"{{""id"":2,""method"":""thread/start"",""params"":{{""cwd"":""{cwd}"",""sessionSource"":""cli"",""ephemeral"":true}}}}");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var startThread = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread");
                var threadId = startThread.GetProperty("id").GetString();
                var expectedRolloutPath = threadStore.RolloutRecorder.GetRolloutPath(threadId!);

                Assert.False(string.IsNullOrWhiteSpace(threadId));
                Assert.True(startThread.GetProperty("ephemeral").GetBoolean());
                Assert.Equal(JsonValueKind.Null, startThread.GetProperty("path").ValueKind);
                Assert.False(File.Exists(expectedRolloutPath));

                var startedThread = messages.Single(x => IsNotificationMethod(x.RootElement, "thread/started")).RootElement
                    .GetProperty("params")
                    .GetProperty("thread");
                Assert.Equal(threadId, startedThread.GetProperty("id").GetString());
                Assert.True(startedThread.GetProperty("ephemeral").GetBoolean());
                Assert.Equal(JsonValueKind.Null, startedThread.GetProperty("path").ValueKind);
                Assert.False(File.Exists(expectedRolloutPath));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenEphemeralThreadReadAndList_ShouldRemainPathless()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var cwd = root.Replace("\\", "/");
        Channel<string>? inputLines = null;
        ChannelTextReader? reader = null;
        ChannelTextWriter? writer = null;
        Task? runTask = null;

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            inputLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            reader = new ChannelTextReader(inputLines.Reader);
            writer = new ChannelTextWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            runTask = server.RunAsync(CancellationToken.None);

            await KernelAppServerTestProtocol.InitializeAsync(
                inputLines.Writer,
                writer.Lines,
                TimeSpan.FromSeconds(5));

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/start"",""params"":{{""cwd"":""{cwd}"",""sessionSource"":""cli"",""ephemeral"":true}}}}");
            var startResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));
            using var startMessage = JsonDocument.Parse(startResponse);
            var startThread = startMessage.RootElement.GetProperty("result").GetProperty("thread");
            var threadId = startThread.GetProperty("id").GetString();
            var expectedRolloutPath = threadStore.RolloutRecorder.GetRolloutPath(threadId!);

            Assert.False(string.IsNullOrWhiteSpace(threadId));
            Assert.True(startThread.GetProperty("ephemeral").GetBoolean());
            Assert.Equal(JsonValueKind.Null, startThread.GetProperty("path").ValueKind);
            Assert.False(File.Exists(expectedRolloutPath));

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":2,""method"":""thread/read"",""params"":{{""threadId"":""{threadId}"",""includeTurns"":false}}}}");
            var readResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(5));
            using var readMessage = JsonDocument.Parse(readResponse);
            var readThread = readMessage.RootElement.GetProperty("result").GetProperty("thread");
            Assert.Equal(threadId, readThread.GetProperty("id").GetString());
            Assert.True(readThread.GetProperty("ephemeral").GetBoolean());
            Assert.Equal(JsonValueKind.Null, readThread.GetProperty("path").ValueKind);

            inputLines.Writer.TryWrite(
                """{"jsonrpc":"2.0","id":3,"method":"thread/list","params":{"limit":10,"modelProviders":[]}}""");
            var listResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 3, TimeSpan.FromSeconds(5));
            using var listMessage = JsonDocument.Parse(listResponse);
            var listedThreads = listMessage.RootElement
                .GetProperty("result")
                .GetProperty("data")
                .EnumerateArray()
                .Select(static item => item.GetProperty("id").GetString())
                .ToArray();
            Assert.DoesNotContain(threadId, listedThreads);

            Assert.False(File.Exists(expectedRolloutPath));
            if (File.Exists(storePath))
            {
                var persistedJson = await File.ReadAllTextAsync(storePath);
                Assert.DoesNotContain(threadId, persistedJson, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (inputLines is not null)
            {
                inputLines.Writer.TryComplete();
            }

            if (runTask is not null)
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            writer?.Dispose();
            reader?.Dispose();
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenEphemeralThreadFork_ShouldRemainPathlessAndOmitListing()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string sourceThreadId = "00000000-0000-7000-8000-000000000301";
        KernelThreadStore? setupStore = null;
        Channel<string>? inputLines = null;
        ChannelTextReader? reader = null;
        ChannelTextWriter? writer = null;
        Task? runTask = null;

        try
        {
            setupStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                sourceThreadId,
                repoRoot,
                ("turn_001", "第一问", "第一答"));
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);

            var threadStore = new KernelThreadStore(storePath);
            inputLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            reader = new ChannelTextReader(inputLines.Reader);
            writer = new ChannelTextWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            runTask = server.RunAsync(CancellationToken.None);

            await KernelAppServerTestProtocol.InitializeAsync(
                inputLines.Writer,
                writer.Lines,
                TimeSpan.FromSeconds(5));

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/fork"",""params"":{{""threadId"":""{sourceThreadId}"",""ephemeral"":true}}}}");
            var forkResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));
            using var forkMessage = JsonDocument.Parse(forkResponse);
            var forkThread = forkMessage.RootElement.GetProperty("result").GetProperty("thread");
            var forkThreadId = forkThread.GetProperty("id").GetString();
            var expectedRolloutPath = threadStore.RolloutRecorder.GetRolloutPath(forkThreadId!);

            Assert.False(string.IsNullOrWhiteSpace(forkThreadId));
            Assert.True(forkThread.GetProperty("ephemeral").GetBoolean());
            Assert.Equal(JsonValueKind.Null, forkThread.GetProperty("path").ValueKind);
            Assert.NotEmpty(forkThread.GetProperty("turns").EnumerateArray());
            Assert.False(File.Exists(expectedRolloutPath));

            inputLines.Writer.TryWrite(
                """{"jsonrpc":"2.0","id":2,"method":"thread/list","params":{"limit":10,"modelProviders":[]}}""");
            var listResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(5));
            using var listMessage = JsonDocument.Parse(listResponse);
            var listedThreads = listMessage.RootElement
                .GetProperty("result")
                .GetProperty("data")
                .EnumerateArray()
                .Select(static item => item.GetProperty("id").GetString())
                .ToArray();
            Assert.DoesNotContain(forkThreadId, listedThreads);

            if (File.Exists(storePath))
            {
                var persistedJson = await File.ReadAllTextAsync(storePath);
                Assert.DoesNotContain(forkThreadId, persistedJson, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (inputLines is not null)
            {
                inputLines.Writer.TryComplete();
            }

            if (runTask is not null)
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            writer?.Dispose();
            reader?.Dispose();
            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectRepeatedInitializeRequests()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var input = string.Join(
            Environment.NewLine,
            """{"id":1,"method":"initialize","params":{"capabilities":{"experimentalApi":true}}}""",
            """{"id":2,"method":"initialize","params":{"capabilities":{"experimentalApi":false}}}""");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(input);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                Assert.True(messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.TryGetProperty("result", out _));
                var error = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.Equal("Already initialized", error.GetProperty("message").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectRequestsBeforeInitialize()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var input = """{"id":1,"method":"thread/start","params":{"cwd":"D:/Repo","sessionSource":"cli"}}""";

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(input);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var message = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .Single(static doc => IsResponseId(doc.RootElement, 1));

            using (message)
            {
                var error = message.RootElement.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.Equal("Not initialized", error.GetProperty("message").GetString());
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldPaginateLoadedThreadIdsWithUuidCursorAndRejectInvalidCursor()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var inputLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            var threadStore = new KernelThreadStore(storePath);
            using var reader = new ChannelTextReader(inputLines.Reader);
            using var writer = new ChannelTextWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            var runTask = server.RunAsync(CancellationToken.None);

            await KernelAppServerTestProtocol.InitializeAsync(
                inputLines.Writer,
                writer.Lines,
                TimeSpan.FromSeconds(5));

            inputLines.Writer.TryWrite("""{"jsonrpc":"2.0","id":1,"method":"thread/start","params":{"cwd":"D:/Repo/A","sessionSource":"cli"}}""");
            var firstStartResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));
            using var firstStartMessage = JsonDocument.Parse(firstStartResponse);
            var firstThreadId = firstStartMessage.RootElement
                .GetProperty("result")
                .GetProperty("thread")
                .GetProperty("id")
                .GetString();

            inputLines.Writer.TryWrite("""{"jsonrpc":"2.0","id":2,"method":"thread/start","params":{"cwd":"D:/Repo/B","sessionSource":"cli"}}""");
            var secondStartResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(5));
            using var secondStartMessage = JsonDocument.Parse(secondStartResponse);
            var secondThreadId = secondStartMessage.RootElement
                .GetProperty("result")
                .GetProperty("thread")
                .GetProperty("id")
                .GetString();

            AssertLooksLikeVersion7Guid(firstThreadId);
            AssertLooksLikeVersion7Guid(secondThreadId);

            inputLines.Writer.TryWrite("""{"jsonrpc":"2.0","id":3,"method":"thread/loaded/list","params":{"limit":1}}""");
            var firstPageResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 3, TimeSpan.FromSeconds(5));
            using var firstPageMessage = JsonDocument.Parse(firstPageResponse);
            var firstPage = firstPageMessage.RootElement.GetProperty("result");
            var firstPageData = firstPage.GetProperty("data").EnumerateArray().Select(static item => item.GetString()).ToArray();
            var nextCursor = firstPage.GetProperty("nextCursor").GetString();
            Assert.Single(firstPageData);
            AssertLooksLikeVersion7Guid(nextCursor);

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":4,""method"":""thread/loaded/list"",""params"":{{""cursor"":""{nextCursor}"",""limit"":1}}}}");
            var secondPageResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 4, TimeSpan.FromSeconds(5));
            using var secondPageMessage = JsonDocument.Parse(secondPageResponse);
            var secondPage = secondPageMessage.RootElement.GetProperty("result");
            var secondPageData = secondPage.GetProperty("data").EnumerateArray().Select(static item => item.GetString()).ToArray();

            var expectedOrder = new[] { firstThreadId!, secondThreadId! }
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(new[] { expectedOrder[0] }, firstPageData);
            Assert.Equal(expectedOrder[0], nextCursor);
            Assert.Equal(new[] { expectedOrder[1] }, secondPageData);
            Assert.Equal(JsonValueKind.Null, secondPage.GetProperty("nextCursor").ValueKind);

            inputLines.Writer.TryWrite("""{"jsonrpc":"2.0","id":5,"method":"thread/loaded/list","params":{"cursor":"not-a-uuid","limit":1}}""");
            var invalidCursorResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 5, TimeSpan.FromSeconds(5));
            using var invalidCursorMessage = JsonDocument.Parse(invalidCursorResponse);
            var error = invalidCursorMessage.RootElement.GetProperty("error");
            Assert.Equal(-32600, error.GetProperty("code").GetInt32());
            Assert.Equal("invalid cursor: not-a-uuid", error.GetProperty("message").GetString());

            inputLines.Writer.TryComplete();
            await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void AppHostServer_ShouldGenerateVersion7ThreadAndTurnIds()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore);
            var nextThreadIdMethod = typeof(AppHostServer).GetMethod("NextThreadId", BindingFlags.Instance | BindingFlags.NonPublic);
            var nextTurnIdMethod = typeof(AppHostServer).GetMethod("NextTurnId", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(nextThreadIdMethod);
            Assert.NotNull(nextTurnIdMethod);

            var firstThreadId = (string?)nextThreadIdMethod!.Invoke(server, null);
            var secondThreadId = (string?)nextThreadIdMethod.Invoke(server, null);
            var firstTurnId = (string?)nextTurnIdMethod!.Invoke(server, null);
            var secondTurnId = (string?)nextTurnIdMethod.Invoke(server, null);

            AssertLooksLikeVersion7Guid(firstThreadId);
            AssertLooksLikeVersion7Guid(secondThreadId);
            AssertLooksLikeVersion7Guid(firstTurnId);
            AssertLooksLikeVersion7Guid(secondTurnId);
            Assert.NotEqual(firstThreadId, secondThreadId);
            Assert.NotEqual(firstTurnId, secondTurnId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectInvalidClientInfoNameOnInitialize()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var input = "{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"bad\\rname\",\"title\":\"Bad Client\",\"version\":\"0.1.0\"}}}";

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.Equal(
                    "Invalid clientInfo.name: 'bad\rname'. Must be a valid HTTP header value.",
                    error.GetProperty("message").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRespectOriginatorOverrideEnvironmentVariableOnInitialize()
    {
        var originalOverride = Environment.GetEnvironmentVariable("TIANSHU_INTERNAL_ORIGINATOR_OVERRIDE");
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var input = "{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"tianshu_cli\",\"title\":\"TianShu CLI\",\"version\":\"0.1.0\"}}}";

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_INTERNAL_ORIGINATOR_OVERRIDE", "tianshu_originator_via_env_var");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var init = messages.Single(x => IsResponseId(x.RootElement, 1));
                Assert.Equal(
                    "tianshu_originator_via_env_var/0.1.0",
                    init.RootElement.GetProperty("result").GetProperty("userAgent").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_INTERNAL_ORIGINATOR_OVERRIDE", originalOverride);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRespectExperimentalApiCapabilityForThreadStartMockField()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(workspace);
        var normalizedWorkspace = workspace.Replace("\\", "/");
        var input = string.Join(
            Environment.NewLine,
            """{"id":1,"method":"initialize","params":{"capabilities":{"experimentalApi":false}}}""",
            $@"{{""id"":2,""method"":""thread/start"",""params"":{{""cwd"":""{normalizedWorkspace}"",""mockExperimentalField"":""mock""}}}}",
            $@"{{""id"":3,""method"":""thread/start"",""params"":{{""cwd"":""{normalizedWorkspace}""}}}}");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var gatedError = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("error");
                Assert.Equal(-32600, gatedError.GetProperty("code").GetInt32());
                Assert.Equal(
                    "thread/start.mockExperimentalField requires experimentalApi capability",
                    gatedError.GetProperty("message").GetString());

                var startedThread = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement.GetProperty("result");
                Assert.False(string.IsNullOrWhiteSpace(startedThread.GetProperty("thread").GetProperty("id").GetString()));
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "thread/started"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRespectExperimentalApiCapabilityForExperimentalMethods()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_experimental_gate_001";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"id":1,"method":"initialize","params":{"capabilities":{"experimentalApi":false}}}""",
                $@"{{""id"":2,""method"":""thread/increment_elicitation"",""params"":{{""threadId"":""{threadId}""}}}}");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.Equal(
                    "thread/increment_elicitation requires experimentalApi capability",
                    error.GetProperty("message").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadListOmitsSourceKinds_ShouldDefaultToInteractiveSources()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var input = string.Join(
            Environment.NewLine,
            """{"id":1,"method":"thread/start","params":{"cwd":"D:/Repo/Interactive","sessionSource":"cli"}}""",
            """{"id":2,"method":"thread/start","params":{"cwd":"D:/Repo/AppServer"}}""",
            """{"id":3,"method":"thread/list","params":{"limit":10}}""");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var interactiveThreadId = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread")
                    .GetProperty("id")
                    .GetString();
                var appServerThreadId = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread")
                    .GetProperty("id")
                    .GetString();
                var listedThreads = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement
                    .GetProperty("result")
                    .GetProperty("data")
                    .EnumerateArray()
                    .ToArray();

                var listedThread = Assert.Single(listedThreads);
                Assert.Equal(interactiveThreadId, listedThread.GetProperty("id").GetString());
                Assert.NotEqual(appServerThreadId, listedThread.GetProperty("id").GetString());
                Assert.Equal("cli", listedThread.GetProperty("source").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadListOmitsModelProviders_ShouldDefaultToCurrentProvider()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var input = string.Join(
            Environment.NewLine,
            """{"id":1,"method":"thread/start","params":{"cwd":"D:/Repo/Default","sessionSource":"cli"}}""",
            """{"id":2,"method":"thread/start","params":{"cwd":"D:/Repo/Other","sessionSource":"cli","modelProvider":"other_provider"}}""",
            """{"id":3,"method":"thread/list","params":{"limit":10}}""",
            """{"id":4,"method":"thread/list","params":{"limit":10,"modelProviders":[]}}""");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var defaultProviderThreadId = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread")
                    .GetProperty("id")
                    .GetString();
                var otherProviderThreadId = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread")
                    .GetProperty("id")
                    .GetString();

                var defaultFilteredThreadIds = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement
                    .GetProperty("result")
                    .GetProperty("data")
                    .EnumerateArray()
                    .Select(static item => item.GetProperty("id").GetString())
                    .ToArray();
                var allProviderThreadIds = messages.Single(x => IsResponseId(x.RootElement, 4)).RootElement
                    .GetProperty("result")
                    .GetProperty("data")
                    .EnumerateArray()
                    .Select(static item => item.GetProperty("id").GetString())
                    .ToArray();

                Assert.Equal(new[] { defaultProviderThreadId }, defaultFilteredThreadIds);
                Assert.Contains(defaultProviderThreadId, allProviderThreadIds);
                Assert.Contains(otherProviderThreadId, allProviderThreadIds);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadListSortsByUpdatedAt_ShouldUseRolloutMtime()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        Channel<string>? inputLines = null;
        ChannelTextReader? reader = null;
        ChannelTextWriter? writer = null;
        Task? runTask = null;

        try
        {
            inputLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            var threadStore = new KernelThreadStore(storePath);
            reader = new ChannelTextReader(inputLines.Reader);
            writer = new ChannelTextWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            runTask = server.RunAsync(CancellationToken.None);

            await KernelAppServerTestProtocol.InitializeAsync(
                inputLines.Writer,
                writer.Lines,
                TimeSpan.FromSeconds(5));

            async Task<(string ThreadId, string RolloutPath)> StartThreadAsync(int id, string cwd)
            {
                inputLines.Writer.TryWrite(
                    $@"{{""jsonrpc"":""2.0"",""id"":{id},""method"":""thread/start"",""params"":{{""cwd"":""{cwd}"",""sessionSource"":""cli""}}}}");
                var response = await WaitForJsonRpcResponseIdAsync(writer.Lines, id, TimeSpan.FromSeconds(5));
                using var document = JsonDocument.Parse(response);
                var thread = document.RootElement.GetProperty("result").GetProperty("thread");
                return (
                    thread.GetProperty("id").GetString()!,
                    thread.GetProperty("path").GetString()!);
            }

            var first = await StartThreadAsync(1, "D:/Repo/Updated/A");
            var second = await StartThreadAsync(2, "D:/Repo/Updated/B");
            var third = await StartThreadAsync(3, "D:/Repo/Updated/C");
            await MaterializeThreadRolloutAsync(threadStore, first.ThreadId);
            await MaterializeThreadRolloutAsync(threadStore, second.ThreadId);
            await MaterializeThreadRolloutAsync(threadStore, third.ThreadId);

            File.SetLastWriteTimeUtc(first.RolloutPath, new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(second.RolloutPath, new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(third.RolloutPath, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));

            inputLines.Writer.TryWrite("""{"jsonrpc":"2.0","id":4,"method":"thread/list","params":{"limit":10,"sortKey":"updated_at","modelProviders":[]}}""");
            var listResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 4, TimeSpan.FromSeconds(5));
            using var listMessage = JsonDocument.Parse(listResponse);
            var listedThreadIds = listMessage.RootElement
                .GetProperty("result")
                .GetProperty("data")
                .EnumerateArray()
                .Select(static item => item.GetProperty("id").GetString())
                .ToArray();

            Assert.Equal(new[] { first.ThreadId, second.ThreadId, third.ThreadId }, listedThreadIds);

        }
        finally
        {
            if (inputLines is not null)
            {
                inputLines.Writer.TryComplete();
            }

            if (runTask is not null)
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            writer?.Dispose();
            reader?.Dispose();
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadListSortsByUpdatedAt_ShouldUseRolloutMtimeForPayloadAndCursor()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        Channel<string>? inputLines = null;
        ChannelTextReader? reader = null;
        ChannelTextWriter? writer = null;
        Task? runTask = null;

        try
        {
            inputLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            var threadStore = new KernelThreadStore(storePath);
            reader = new ChannelTextReader(inputLines.Reader);
            writer = new ChannelTextWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            runTask = server.RunAsync(CancellationToken.None);

            await KernelAppServerTestProtocol.InitializeAsync(
                inputLines.Writer,
                writer.Lines,
                TimeSpan.FromSeconds(5));

            async Task<(string ThreadId, string RolloutPath)> StartThreadAsync(int id, string cwd)
            {
                inputLines.Writer.TryWrite(
                    $@"{{""jsonrpc"":""2.0"",""id"":{id},""method"":""thread/start"",""params"":{{""cwd"":""{cwd}"",""sessionSource"":""cli""}}}}");
                var response = await WaitForJsonRpcResponseIdAsync(writer.Lines, id, TimeSpan.FromSeconds(5));
                using var document = JsonDocument.Parse(response);
                var thread = document.RootElement.GetProperty("result").GetProperty("thread");
                return (
                    thread.GetProperty("id").GetString()!,
                    thread.GetProperty("path").GetString()!);
            }

            var first = await StartThreadAsync(1, "D:/Repo/Updated/PageA");
            var second = await StartThreadAsync(2, "D:/Repo/Updated/PageB");
            var third = await StartThreadAsync(3, "D:/Repo/Updated/PageC");
            await MaterializeThreadRolloutAsync(threadStore, first.ThreadId);
            await MaterializeThreadRolloutAsync(threadStore, second.ThreadId);
            await MaterializeThreadRolloutAsync(threadStore, third.ThreadId);

            var firstUpdatedAt = new DateTimeOffset(new DateTime(2026, 3, 13, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var secondUpdatedAt = new DateTimeOffset(new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var thirdUpdatedAt = new DateTimeOffset(new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();

            File.SetLastWriteTimeUtc(first.RolloutPath, DateTimeOffset.FromUnixTimeSeconds(firstUpdatedAt).UtcDateTime);
            File.SetLastWriteTimeUtc(second.RolloutPath, DateTimeOffset.FromUnixTimeSeconds(secondUpdatedAt).UtcDateTime);
            File.SetLastWriteTimeUtc(third.RolloutPath, DateTimeOffset.FromUnixTimeSeconds(thirdUpdatedAt).UtcDateTime);

            inputLines.Writer.TryWrite("""{"jsonrpc":"2.0","id":4,"method":"thread/list","params":{"limit":2,"sortKey":"updated_at","modelProviders":[]}}""");
            var firstPageResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 4, TimeSpan.FromSeconds(5));
            using var firstPageMessage = JsonDocument.Parse(firstPageResponse);
            var firstPageResult = firstPageMessage.RootElement.GetProperty("result");
            var firstPageThreads = firstPageResult
                .GetProperty("data")
                .EnumerateArray()
                .Select(static item => new
                {
                    Id = item.GetProperty("id").GetString(),
                    UpdatedAt = item.GetProperty("updatedAt").GetInt64(),
                })
                .ToArray();

            Assert.Equal(
                new[]
                {
                    (first.ThreadId, firstUpdatedAt),
                    (second.ThreadId, secondUpdatedAt),
                },
                firstPageThreads.Select(static item => (item.Id!, item.UpdatedAt)).ToArray());

            var nextCursor = firstPageResult.GetProperty("nextCursor").GetString();
            Assert.False(string.IsNullOrWhiteSpace(nextCursor));

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":5,""method"":""thread/list"",""params"":{{""cursor"":""{nextCursor}"",""limit"":2,""sortKey"":""updated_at"",""modelProviders"":[]}}}}");
            var secondPageResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 5, TimeSpan.FromSeconds(5));
            using var secondPageMessage = JsonDocument.Parse(secondPageResponse);
            var secondPageResult = secondPageMessage.RootElement.GetProperty("result");
            var secondPageThreads = secondPageResult
                .GetProperty("data")
                .EnumerateArray()
                .Select(static item => new
                {
                    Id = item.GetProperty("id").GetString(),
                    UpdatedAt = item.GetProperty("updatedAt").GetInt64(),
                })
                .ToArray();

            var secondPageThread = Assert.Single(secondPageThreads);
            Assert.Equal(third.ThreadId, secondPageThread.Id);
            Assert.Equal(thirdUpdatedAt, secondPageThread.UpdatedAt);
            Assert.Equal(JsonValueKind.Null, secondPageResult.GetProperty("nextCursor").ValueKind);
        }
        finally
        {
            if (inputLines is not null)
            {
                inputLines.Writer.TryComplete();
            }

            if (runTask is not null)
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            writer?.Dispose();
            reader?.Dispose();
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadArchiveRequiresMaterializedRollout_ShouldRejectFreshThread()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000103";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, "D:/Repo/Fresh", CancellationToken.None);

            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/archive"",""params"":{{""threadId"":""{threadId}""}}}}"));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.Equal($"no rollout found for thread id {threadId}", error.GetProperty("message").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadListArchivedFilterUsesRolloutLocation()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string activeThreadId = "00000000-0000-7000-8000-000000000104";
        const string archivedThreadId = "00000000-0000-7000-8000-000000000105";

        try
        {
            var activeStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                activeThreadId,
                Path.Combine(root, "repo-active"),
                ("turn_active", "active user", "active assistant"));
            await activeStore.RolloutRecorder.CloseAllThreadWritersAsync();

            var archivedStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                archivedThreadId,
                Path.Combine(root, "repo-archived"),
                ("turn_archived", "archived user", "archived assistant"));
            await archivedStore.RolloutRecorder.CloseAllThreadWritersAsync();

            var archivedSourcePath = Path.Combine(root, "sessions", $"{archivedThreadId}.jsonl");
            var archivedTargetPath = Path.Combine(root, "archived_sessions", $"{archivedThreadId}.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(archivedTargetPath)!);
            File.Move(archivedSourcePath, archivedTargetPath);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"thread/list","params":{"limit":10,"modelProviders":[]}}""",
                """{"jsonrpc":"2.0","id":2,"method":"thread/list","params":{"limit":10,"modelProviders":[],"archived":true}}""");
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var activeThreads = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement
                    .GetProperty("result")
                    .GetProperty("data")
                    .EnumerateArray()
                    .ToArray();
                var archivedThreads = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement
                    .GetProperty("result")
                    .GetProperty("data")
                    .EnumerateArray()
                    .ToArray();

                var activeThread = Assert.Single(activeThreads);
                Assert.Equal(activeThreadId, activeThread.GetProperty("id").GetString());

                var archivedThread = Assert.Single(archivedThreads);
                Assert.Equal(archivedThreadId, archivedThread.GetProperty("id").GetString());
                Assert.Equal("notLoaded", archivedThread.GetProperty("status").GetProperty("type").GetString());
                Assert.Equal(archivedTargetPath, archivedThread.GetProperty("path").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadUnarchiveShouldMoveRolloutBackAndBumpUpdatedAt()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000106";
        Channel<string>? inputLines = null;
        ChannelTextReader? reader = null;
        ChannelTextWriter? writer = null;
        Task? runTask = null;

        try
        {
            var setupStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                threadId,
                Path.Combine(root, "repo-roundtrip"),
                ("turn_archive", "archive me", "done"));
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync();

            inputLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            reader = new ChannelTextReader(inputLines.Reader);
            writer = new ChannelTextWriter();
            var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));
            runTask = server.RunAsync(CancellationToken.None);

            await KernelAppServerTestProtocol.InitializeAsync(
                inputLines.Writer,
                writer.Lines,
                TimeSpan.FromSeconds(5));

            var activePath = Path.Combine(root, "sessions", $"{threadId}.jsonl");
            var archivedPath = Path.Combine(root, "archived_sessions", $"{threadId}.jsonl");

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/archive"",""params"":{{""threadId"":""{threadId}""}}}}");
            _ = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));

            Assert.False(File.Exists(activePath));
            Assert.True(File.Exists(archivedPath));

            var oldUpdatedAt = new DateTimeOffset(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();
            File.SetLastWriteTimeUtc(archivedPath, DateTimeOffset.FromUnixTimeSeconds(oldUpdatedAt).UtcDateTime);

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":2,""method"":""thread/unarchive"",""params"":{{""threadId"":""{threadId}""}}}}");
            var unarchiveResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(5));
            using var unarchiveMessage = JsonDocument.Parse(unarchiveResponse);
            var thread = unarchiveMessage.RootElement
                .GetProperty("result")
                .GetProperty("thread");

            Assert.Equal("notLoaded", thread.GetProperty("status").GetProperty("type").GetString());
            Assert.True(thread.GetProperty("updatedAt").GetInt64() > oldUpdatedAt);
            Assert.Equal(JsonValueKind.Null, thread.GetProperty("name").ValueKind);
            Assert.Equal(activePath, thread.GetProperty("path").GetString());
            Assert.True(File.Exists(activePath));
            Assert.False(File.Exists(archivedPath));
        }
        finally
        {
            if (inputLines is not null)
            {
                inputLines.Writer.TryComplete();
            }

            if (runTask is not null)
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            writer?.Dispose();
            reader?.Dispose();
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadListUsesTypedSourceKinds_ShouldFilterSubAgentSources()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var input = string.Join(
            Environment.NewLine,
            """{"id":1,"method":"thread/start","params":{"cwd":"D:/Repo/Review","sessionSource":{"subAgent":"review"}}}""",
            """{"id":2,"method":"thread/start","params":{"cwd":"D:/Repo/Spawn","sessionSource":{"subAgent":{"thread_spawn":{"parent_thread_id":"thread_parent_1","depth":2,"reason":"review"}}}}}""",
            """{"id":3,"method":"thread/list","params":{"limit":10,"sourceKinds":["subAgentReview"]}}""");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var reviewThreadId = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread")
                    .GetProperty("id")
                    .GetString();
                var spawnThreadId = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread")
                    .GetProperty("id")
                    .GetString();
                var listedThreads = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement
                    .GetProperty("result")
                    .GetProperty("data")
                    .EnumerateArray()
                    .ToArray();

                var listedThread = Assert.Single(listedThreads);
                Assert.Equal(reviewThreadId, listedThread.GetProperty("id").GetString());
                Assert.NotEqual(spawnThreadId, listedThread.GetProperty("id").GetString());
                Assert.Equal("review", listedThread.GetProperty("source").GetProperty("subAgent").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadListUsesGenericSubAgentSourceKind_ShouldIncludeAllSubAgentSources()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var input = string.Join(
            Environment.NewLine,
            """{"id":1,"method":"thread/start","params":{"cwd":"D:/Repo/Review","sessionSource":{"subAgent":"review"}}}""",
            """{"id":2,"method":"thread/start","params":{"cwd":"D:/Repo/Spawn","sessionSource":{"subAgent":{"thread_spawn":{"parent_thread_id":"thread_parent_1","depth":2,"reason":"review"}}}}}""",
            """{"id":3,"method":"thread/start","params":{"cwd":"D:/Repo/Cli","sessionSource":"cli"}}""",
            """{"id":4,"method":"thread/list","params":{"limit":10,"sourceKinds":["subAgent"]}}""");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var reviewThreadId = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread")
                    .GetProperty("id")
                    .GetString();
                var spawnThreadId = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread")
                    .GetProperty("id")
                    .GetString();
                var cliThreadId = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread")
                    .GetProperty("id")
                    .GetString();
                var listedThreadIds = messages.Single(x => IsResponseId(x.RootElement, 4)).RootElement
                    .GetProperty("result")
                    .GetProperty("data")
                    .EnumerateArray()
                    .Select(static item => item.GetProperty("id").GetString())
                    .ToArray();

                Assert.Equal(2, listedThreadIds.Length);
                Assert.Contains(reviewThreadId, listedThreadIds);
                Assert.Contains(spawnThreadId, listedThreadIds);
                Assert.DoesNotContain(cliThreadId, listedThreadIds);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadReadMissingSessionSource_ShouldDefaultSourceToVsCode()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000101";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            var created = await setupStore.CreateThreadAsync(threadId, "D:/Repo", CancellationToken.None);
            var snapshot = KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder
                    .FromRecord(created, "gpt-5", "openai", "on-request")
                    .Build());
            created.ConfigSnapshot = snapshot with
            {
                SessionSource = null!,
            };
            _ = await setupStore.UpsertThreadAsync(created, CancellationToken.None);

            var input = $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/read"",""params"":{{""threadId"":""{threadId}"",""includeTurns"":false}}}}";
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var thread = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread");

                Assert.Equal("vscode", thread.GetProperty("source").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldHandleExtendedThreadMethods()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string knownThreadId = "00000000-0000-7000-8000-000000000102";
        Channel<string>? inputLines = null;
        ChannelTextReader? reader = null;
        ChannelTextWriter? writer = null;
        Task? runTask = null;

        try
        {
            var setupStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                knownThreadId,
                "D:/Repo",
                ("turn_extended", "hello", "world"));
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync();

            var threadStore = new KernelThreadStore(storePath);
            inputLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            reader = new ChannelTextReader(inputLines.Reader);
            writer = new ChannelTextWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            runTask = server.RunAsync(CancellationToken.None);

            await KernelAppServerTestProtocol.InitializeAsync(
                inputLines.Writer,
                writer.Lines,
                TimeSpan.FromSeconds(5));

            var observedNotifications = new List<string>();

            async Task<JsonDocument> SendRequestAsync(int id, string payload)
            {
                inputLines.Writer.TryWrite(payload);
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (await writer.Lines.WaitToReadAsync(timeoutCts.Token))
                {
                    while (writer.Lines.TryRead(out var line))
                    {
                        var document = JsonDocument.Parse(line);
                        if (IsResponseId(document.RootElement, id))
                        {
                            return document;
                        }

                        if (document.RootElement.TryGetProperty("method", out var methodElement)
                            && methodElement.ValueKind == JsonValueKind.String)
                        {
                            observedNotifications.Add(methodElement.GetString()!);
                        }

                        document.Dispose();
                    }
                }

                throw new TimeoutException($"未等到 id={id} 的 JSON-RPC response。");
            }

            using var readResponse = await SendRequestAsync(
                1,
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/read"",""params"":{{""threadId"":""{knownThreadId}"",""includeTurns"":true}}}}");
            Assert.True(readResponse.RootElement.TryGetProperty("result", out _), readResponse.RootElement.GetRawText());

            using var nameResponse = await SendRequestAsync(
                2,
                $@"{{""jsonrpc"":""2.0"",""id"":2,""method"":""thread/name/set"",""params"":{{""threadId"":""{knownThreadId}"",""name"":""主线程""}}}}");
            Assert.True(nameResponse.RootElement.TryGetProperty("result", out _), nameResponse.RootElement.GetRawText());

            using var metadataResponse = await SendRequestAsync(
                3,
                $@"{{""jsonrpc"":""2.0"",""id"":3,""method"":""thread/metadata/update"",""params"":{{""threadId"":""{knownThreadId}"",""gitInfo"":{{""sha"":""abc123"",""branch"":""main""}}}}}}");
            Assert.True(metadataResponse.RootElement.TryGetProperty("result", out _), metadataResponse.RootElement.GetRawText());

            var activePath = Path.Combine(root, "sessions", $"{knownThreadId}.jsonl");
            Assert.True(File.Exists(activePath));
            var recordBeforeArchive = await threadStore.GetThreadAsync(knownThreadId, CancellationToken.None);
            Assert.NotNull(recordBeforeArchive);
            Assert.NotEmpty(recordBeforeArchive!.Turns);

            using var archiveResponse = await SendRequestAsync(
                4,
                $@"{{""jsonrpc"":""2.0"",""id"":4,""method"":""thread/archive"",""params"":{{""threadId"":""{knownThreadId}""}}}}");
            Assert.True(archiveResponse.RootElement.TryGetProperty("result", out _), archiveResponse.RootElement.GetRawText());

            using var unarchiveResponse = await SendRequestAsync(
                5,
                $@"{{""jsonrpc"":""2.0"",""id"":5,""method"":""thread/unarchive"",""params"":{{""threadId"":""{knownThreadId}""}}}}");
            Assert.True(unarchiveResponse.RootElement.TryGetProperty("result", out _), unarchiveResponse.RootElement.GetRawText());

            using var forkResponse = await SendRequestAsync(
                6,
                $@"{{""jsonrpc"":""2.0"",""id"":6,""method"":""thread/fork"",""params"":{{""threadId"":""{knownThreadId}""}}}}");
            Assert.True(forkResponse.RootElement.TryGetProperty("result", out _), forkResponse.RootElement.GetRawText());
            var forkThread = forkResponse.RootElement
                .GetProperty("result")
                .GetProperty("thread");
            Assert.Equal(JsonValueKind.Null, forkThread.GetProperty("name").ValueKind);

            using var resumeForRollbackResponse = await SendRequestAsync(
                7,
                $@"{{""jsonrpc"":""2.0"",""id"":7,""method"":""thread/resume"",""params"":{{""threadId"":""{knownThreadId}""}}}}");
            Assert.True(resumeForRollbackResponse.RootElement.TryGetProperty("result", out _), resumeForRollbackResponse.RootElement.GetRawText());

            using var rollbackResponse = await SendRequestAsync(
                8,
                $@"{{""jsonrpc"":""2.0"",""id"":8,""method"":""thread/rollback"",""params"":{{""threadId"":""{knownThreadId}"",""numTurns"":1}}}}");
            Assert.True(rollbackResponse.RootElement.TryGetProperty("result", out _), rollbackResponse.RootElement.GetRawText());

            Assert.Contains("thread/name/updated", observedNotifications);
            Assert.Contains("thread/archived", observedNotifications);
            Assert.Contains("thread/unarchived", observedNotifications);
            Assert.Contains("thread/started", observedNotifications);
        }
        finally
        {
            if (inputLines is not null)
            {
                inputLines.Writer.TryComplete();
            }

            if (runTask is not null)
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            writer?.Dispose();
            reader?.Dispose();
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenEphemeralThreadMetadataUpdate_ShouldReject()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var cwd = root.Replace("\\", "/");
        Channel<string>? inputLines = null;
        ChannelTextReader? reader = null;
        ChannelTextWriter? writer = null;
        Task? runTask = null;

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            inputLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            reader = new ChannelTextReader(inputLines.Reader);
            writer = new ChannelTextWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            runTask = server.RunAsync(CancellationToken.None);

            await KernelAppServerTestProtocol.InitializeAsync(
                inputLines.Writer,
                writer.Lines,
                TimeSpan.FromSeconds(5));

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/start"",""params"":{{""cwd"":""{cwd}"",""sessionSource"":""cli"",""ephemeral"":true}}}}");
            var startResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 1, TimeSpan.FromSeconds(5));
            using var startMessage = JsonDocument.Parse(startResponse);
            var threadId = startMessage.RootElement
                .GetProperty("result")
                .GetProperty("thread")
                .GetProperty("id")
                .GetString();

            inputLines.Writer.TryWrite(
                $@"{{""jsonrpc"":""2.0"",""id"":2,""method"":""thread/metadata/update"",""params"":{{""threadId"":""{threadId}"",""gitInfo"":{{""branch"":""main""}}}}}}");
            var updateResponse = await WaitForJsonRpcResponseIdAsync(writer.Lines, 2, TimeSpan.FromSeconds(5));
            using var updateMessage = JsonDocument.Parse(updateResponse);
            var error = updateMessage.RootElement.GetProperty("error");
            Assert.Equal(-32600, error.GetProperty("code").GetInt32());
            Assert.Equal(
                $"ephemeral thread does not support metadata updates: {threadId}",
                error.GetProperty("message").GetString());
        }
        finally
        {
            if (inputLines is not null)
            {
                inputLines.Writer.TryComplete();
            }

            if (runTask is not null)
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            writer?.Dispose();
            reader?.Dispose();
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectInvalidThreadIdForTianShuParityThreadMethods()
    {
        var root = CreateTempDirectory();
        var input = string.Join(
            Environment.NewLine,
            """{"jsonrpc":"2.0","id":1,"method":"thread/read","params":{"threadId":"not-a-thread-id","includeTurns":true}}""",
            """{"jsonrpc":"2.0","id":2,"method":"thread/name/set","params":{"threadId":"not-a-thread-id","name":"主线程"}}""",
            """{"jsonrpc":"2.0","id":3,"method":"thread/archive","params":{"threadId":"not-a-thread-id"}}""",
            """{"jsonrpc":"2.0","id":4,"method":"thread/unarchive","params":{"threadId":"not-a-thread-id"}}""",
            """{"jsonrpc":"2.0","id":5,"method":"thread/fork","params":{"threadId":"not-a-thread-id"}}""",
            """{"jsonrpc":"2.0","id":6,"method":"thread/resume","params":{"threadId":"not-a-thread-id"}}""",
            """{"jsonrpc":"2.0","id":7,"method":"thread/increment_elicitation","params":{"threadId":"not-a-thread-id"}}""",
            """{"jsonrpc":"2.0","id":8,"method":"thread/decrement_elicitation","params":{"threadId":"not-a-thread-id"}}""",
            """{"jsonrpc":"2.0","id":9,"method":"thread/unsubscribe","params":{"threadId":"not-a-thread-id"}}""",
            """{"jsonrpc":"2.0","id":10,"method":"thread/compact/start","params":{"threadId":"not-a-thread-id"}}""",
            """{"jsonrpc":"2.0","id":11,"method":"thread/metadata/update","params":{"threadId":"not-a-thread-id","gitInfo":{"branch":"main"}}}""");

        var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
        var writer = new StringWriter();
        var server = new AppHostServer(reader, writer, new KernelThreadStore(Path.Combine(root, "threads.json")));

        try
        {
            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                for (var id = 1; id <= 11; id++)
                {
                    var error = messages.Single(x => IsResponseId(x.RootElement, id)).RootElement.GetProperty("error");
                    Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                    Assert.Contains("invalid thread id", error.GetProperty("message").GetString(), StringComparison.Ordinal);
                }
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldHandleThreadElicitationCounterMethods()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string knownThreadId = "00000000-0000-7000-8000-000000000103";
        const string missingThreadId = "00000000-0000-7000-8000-000000000104";

        try
        {
            var setupStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                knownThreadId,
                "D:/Repo",
                ("turn_extended", "hello", "world"));
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync();

            var input = string.Join(
                Environment.NewLine,
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/increment_elicitation"",""params"":{{""threadId"":""{knownThreadId}""}}}}",
                $@"{{""jsonrpc"":""2.0"",""id"":2,""method"":""thread/increment_elicitation"",""params"":{{""threadId"":""{knownThreadId}""}}}}",
                $@"{{""jsonrpc"":""2.0"",""id"":3,""method"":""thread/decrement_elicitation"",""params"":{{""threadId"":""{knownThreadId}""}}}}",
                $@"{{""jsonrpc"":""2.0"",""id"":4,""method"":""thread/decrement_elicitation"",""params"":{{""threadId"":""{knownThreadId}""}}}}",
                $@"{{""jsonrpc"":""2.0"",""id"":5,""method"":""thread/decrement_elicitation"",""params"":{{""threadId"":""{knownThreadId}""}}}}",
                $@"{{""jsonrpc"":""2.0"",""id"":6,""method"":""thread/increment_elicitation"",""params"":{{""threadId"":""{missingThreadId}""}}}}");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var incrementFirst = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal(1UL, incrementFirst.GetProperty("count").GetUInt64());
                Assert.True(incrementFirst.GetProperty("paused").GetBoolean());

                var incrementSecond = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("result");
                Assert.Equal(2UL, incrementSecond.GetProperty("count").GetUInt64());
                Assert.True(incrementSecond.GetProperty("paused").GetBoolean());

                var decrementFirst = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement.GetProperty("result");
                Assert.Equal(1UL, decrementFirst.GetProperty("count").GetUInt64());
                Assert.True(decrementFirst.GetProperty("paused").GetBoolean());

                var decrementSecond = messages.Single(x => IsResponseId(x.RootElement, 4)).RootElement.GetProperty("result");
                Assert.Equal(0UL, decrementSecond.GetProperty("count").GetUInt64());
                Assert.False(decrementSecond.GetProperty("paused").GetBoolean());

                var decrementError = messages.Single(x => IsResponseId(x.RootElement, 5)).RootElement.GetProperty("error");
                Assert.Equal(-32600, decrementError.GetProperty("code").GetInt32());
                Assert.Equal("out-of-band elicitation count is already zero", decrementError.GetProperty("message").GetString());

                var missingThreadError = messages.Single(x => IsResponseId(x.RootElement, 6)).RootElement.GetProperty("error");
                Assert.Equal(-32004, missingThreadError.GetProperty("code").GetInt32());
                Assert.Equal($"线程不存在：{missingThreadId}", missingThreadError.GetProperty("message").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldPersistThreadRollbackToRolloutForPathResume()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "00000000-0000-7000-8000-000000000441";
        KernelThreadStore? setupStore = null;
        KernelThreadStore? threadStore = null;

        try
        {
            setupStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                threadId,
                repoRoot,
                ("turn_001", "第一问", "第一答"),
                ("turn_002", "第二问", "第二答"));
            var rolloutPath = setupStore.RolloutRecorder.GetRolloutPath(threadId).Replace("\\", "/");
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/resume"",""params"":{{""threadId"":""{threadId}""}}}}",
                $@"{{""jsonrpc"":""2.0"",""id"":2,""method"":""thread/rollback"",""params"":{{""threadId"":""{threadId}"",""numTurns"":1}}}}",
                $@"{{""jsonrpc"":""2.0"",""id"":3,""method"":""thread/resume"",""params"":{{""threadId"":""{threadId}"",""path"":""{rolloutPath}""}}}}");

            threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var rollbackThread = messages.Single(x => IsResponseId(x.RootElement, 2))
                    .RootElement.GetProperty("result").GetProperty("thread");
                AssertThreadHasSingleConversationTurn(rollbackThread, "第一问", "第一答");

                var resumedThread = messages.Single(x => IsResponseId(x.RootElement, 3))
                    .RootElement.GetProperty("result").GetProperty("thread");
                AssertThreadHasSingleConversationTurn(resumedThread, "第一问", "第一答");
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldPersistThreadRollbackToRolloutForPathFork()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "00000000-0000-7000-8000-000000000442";
        KernelThreadStore? setupStore = null;
        KernelThreadStore? threadStore = null;

        try
        {
            setupStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                threadId,
                repoRoot,
                ("turn_001", "第一问", "第一答"),
                ("turn_002", "第二问", "第二答"));
            var rolloutPath = setupStore.RolloutRecorder.GetRolloutPath(threadId).Replace("\\", "/");
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/resume"",""params"":{{""threadId"":""{threadId}""}}}}",
                $@"{{""jsonrpc"":""2.0"",""id"":2,""method"":""thread/rollback"",""params"":{{""threadId"":""{threadId}"",""numTurns"":1}}}}",
                $@"{{""jsonrpc"":""2.0"",""id"":3,""method"":""thread/fork"",""params"":{{""threadId"":""{threadId}"",""path"":""{rolloutPath}""}}}}");

            threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var rollbackThread = messages.Single(x => IsResponseId(x.RootElement, 2))
                    .RootElement.GetProperty("result").GetProperty("thread");
                AssertThreadHasSingleConversationTurn(rollbackThread, "第一问", "第一答");

                var forkedThread = messages.Single(x => IsResponseId(x.RootElement, 3))
                    .RootElement.GetProperty("result").GetProperty("thread");
                AssertThreadHasSingleConversationTurn(forkedThread, "第一问", "第一答");
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ThreadRollback_ShouldRejectRequestWhenPendingRollbackExistsForSameThread()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "00000000-0000-7000-8000-000000000443";
        KernelThreadStore? setupStore = null;

        try
        {
            setupStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                threadId,
                repoRoot,
                ("turn_001", "第一问", "第一答"),
                ("turn_002", "第二问", "第二答"));
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);

            var threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);
            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var beginRollbackMethod = typeof(AppHostServer).GetMethod("TryBeginThreadRollback", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(beginRollbackMethod);
            await InvokeHandleThreadResumeAsync(
                server,
                100,
                $$"""
                {
                  "threadId": "{{threadId}}"
                }
                """);
            Assert.True((bool)beginRollbackMethod!.Invoke(server, [threadId])!);

            using var idDocument = JsonDocument.Parse("101");
            using var paramsDocument = JsonDocument.Parse(
                $$"""
                {
                  "threadId": "{{threadId}}",
                  "numTurns": 1
                }
                """);
            await InvokeHandleThreadRollbackAsync(server, idDocument.RootElement.Clone(), paramsDocument.RootElement.Clone());

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var response = messages.Single(x => IsResponseId(x.RootElement, 101)).RootElement;
                var error = response.GetProperty("error");
                Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                Assert.Equal("rollback already in progress for this thread", error.GetProperty("message").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldPreferRolloutSnapshotOverStaleStoreForThreadReadAndResume()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "00000000-0000-7000-8000-000000000218";
        KernelThreadStore? setupStore = null;
        KernelThreadStore? threadStore = null;

        try
        {
            setupStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                threadId,
                repoRoot,
                ("turn_001", "第一问", "第一答"),
                ("turn_002", "第二问", "第二答"));
            await RewriteRolloutToSingleTurnSnapshotAsync(setupStore, threadId);
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/read"",""params"":{{""threadId"":""{threadId}"",""includeTurns"":true}}}}",
                $@"{{""jsonrpc"":""2.0"",""id"":2,""method"":""thread/resume"",""params"":{{""threadId"":""{threadId}""}}}}");

            threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var readThread = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement.GetProperty("result").GetProperty("thread");
                AssertThreadHasSingleConversationTurn(readThread, "第一问", "第一答");

                var resumedThread = messages.Single(x => IsResponseId(x.RootElement, 2))
                    .RootElement.GetProperty("result").GetProperty("thread");
                AssertThreadHasSingleConversationTurn(resumedThread, "第一问", "第一答");
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldNormalizeStaleInProgressTurnAsInterruptedOnResumeAndRead()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "00000000-0000-7000-8000-000000000219";
        const string staleTurnId = "turn_stale_002";
        KernelThreadStore? setupStore = null;
        KernelThreadStore? threadStore = null;

        try
        {
            setupStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                threadId,
                repoRoot,
                ("turn_001", "第一问", "第一答"));

            var record = await setupStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(record);
            record!.StatusType = "active";
            record.ActiveFlags = [];
            record.Turns.Add(new KernelTurnRecord
            {
                Id = staleTurnId,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
                Status = "inProgress",
                UserMessage = "第二问",
                AssistantMessage = "仍在执行",
            });
            record.LastUserMessage = "第二问";
            record.LastAssistantMessage = "仍在执行";
            record.UpdatedAt = DateTimeOffset.UtcNow;
            _ = await setupStore.UpsertThreadAsync(record, CancellationToken.None);
            await setupStore.RolloutRecorder.RewriteThreadSnapshotAsync(
                KernelRolloutStateMapper.ToRolloutThreadRecord(record),
                CancellationToken.None);
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/resume"",""params"":{{""threadId"":""{threadId}""}}}}",
                $@"{{""jsonrpc"":""2.0"",""id"":2,""method"":""thread/resume"",""params"":{{""threadId"":""{threadId}""}}}}",
                $@"{{""jsonrpc"":""2.0"",""id"":3,""method"":""thread/read"",""params"":{{""threadId"":""{threadId}"",""includeTurns"":true}}}}");

            threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var resumedThread = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement.GetProperty("result").GetProperty("thread");
                Assert.Equal("idle", resumedThread.GetProperty("status").GetProperty("type").GetString());
                var resumedTurns = resumedThread.GetProperty("turns").EnumerateArray().ToArray();
                Assert.Equal("interrupted", resumedTurns.Single(turn =>
                    string.Equals(turn.GetProperty("id").GetString(), staleTurnId, StringComparison.Ordinal)).GetProperty("status").GetString());

                var resumedAgainThread = messages.Single(x => IsResponseId(x.RootElement, 2))
                    .RootElement.GetProperty("result").GetProperty("thread");
                Assert.Equal("idle", resumedAgainThread.GetProperty("status").GetProperty("type").GetString());
                var resumedAgainTurns = resumedAgainThread.GetProperty("turns").EnumerateArray().ToArray();
                Assert.Equal("interrupted", resumedAgainTurns.Single(turn =>
                    string.Equals(turn.GetProperty("id").GetString(), staleTurnId, StringComparison.Ordinal)).GetProperty("status").GetString());

                var readThread = messages.Single(x => IsResponseId(x.RootElement, 3))
                    .RootElement.GetProperty("result").GetProperty("thread");
                Assert.Equal("idle", readThread.GetProperty("status").GetProperty("type").GetString());
                var readTurns = readThread.GetProperty("turns").EnumerateArray().ToArray();
                Assert.Equal("interrupted", readTurns.Single(turn =>
                    string.Equals(turn.GetProperty("id").GetString(), staleTurnId, StringComparison.Ordinal)).GetProperty("status").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldPreferRolloutSnapshotOverStaleStoreForThreadFork()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "00000000-0000-7000-8000-000000000219";
        KernelThreadStore? setupStore = null;
        KernelThreadStore? threadStore = null;

        try
        {
            setupStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                threadId,
                repoRoot,
                ("turn_001", "第一问", "第一答"),
                ("turn_002", "第二问", "第二答"));
            await RewriteRolloutToSingleTurnSnapshotAsync(setupStore, threadId);
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);

            var input = $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/fork"",""params"":{{""threadId"":""{threadId}""}}}}";

            threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var forkedThread = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement.GetProperty("result").GetProperty("thread");
                AssertThreadHasSingleConversationTurn(forkedThread, "第一问", "第一答");
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldEnforceCodexGitInfoPatchRulesForThreadMetadataUpdate()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000109";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """
                {"jsonrpc":"2.0","id":1,"method":"thread/metadata/update","params":{"threadId":"__THREAD_ID__"}}
                """,
                """
                {"jsonrpc":"2.0","id":2,"method":"thread/metadata/update","params":{"threadId":"__THREAD_ID__","gitInfo":{}}}
                """,
                """
                {"jsonrpc":"2.0","id":3,"method":"thread/metadata/update","params":{"threadId":"__THREAD_ID__","gitInfo":{"branch":"   "}}}
                """).Replace("__THREAD_ID__", threadId);

            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var missingGitInfo = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32602, missingGitInfo.GetProperty("code").GetInt32());
                Assert.Equal("gitInfo must include at least one field", missingGitInfo.GetProperty("message").GetString());

                var emptyGitInfo = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("error");
                Assert.Equal(-32602, emptyGitInfo.GetProperty("code").GetInt32());
                Assert.Equal("gitInfo must include at least one field", emptyGitInfo.GetProperty("message").GetString());

                var emptyBranch = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement.GetProperty("error");
                Assert.Equal(-32602, emptyBranch.GetProperty("code").GetInt32());
                Assert.Equal("gitInfo.branch must not be empty", emptyBranch.GetProperty("message").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectPendingInputStateOnlyOnThreadMetadataUpdate()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000110";
        KernelThreadStore? threadStore = null;

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = """
                {"jsonrpc":"2.0","id":1,"method":"thread/metadata/update","params":{"threadId":"__THREAD_ID__","pendingInputState":{"entries":[{"correlationId":"corr-kernel-interrupt-001","requestedMode":"Interrupt","effectiveMode":"Interrupt","lifecycleState":"interrupt_requested","expectedTurnId":"turn-kernel-pending-001","turnId":"turn-kernel-interrupt-001","pendingBucket":"QueuedUserMessage","compareKey":{"message":"kernel interrupt message","imageCount":0}}],"pendingSteers":[{"correlationId":"corr-kernel-pending-001","requestedMode":"Steer","effectiveMode":"Steer","lifecycleState":"awaiting_commit","expectedTurnId":"turn-kernel-pending-001","turnId":null,"pendingBucket":"PendingSteer","compareKey":{"message":"kernel pending message","imageCount":0}}],"interruptRequestPending":true,"submitPendingSteersAfterInterrupt":true}}}
                """.Replace("__THREAD_ID__", threadId);

            threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var response = JsonDocument.Parse(writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Single(static line => line.Contains(@"""id"":1", StringComparison.Ordinal)));
            var error = response.RootElement.GetProperty("error");
            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
            Assert.Equal("gitInfo must include at least one field", error.GetProperty("message").GetString());

            var persisted = await threadStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.Null(persisted!.PendingInputState);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRoundTripPendingInputStateThroughTianShuPendingInputUpdateReadAndResume()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "00000000-0000-7000-8000-000000000111";
        KernelThreadStore? setupStore = null;
        KernelThreadStore? threadStore = null;

        try
        {
            setupStore = await CreateMaterializedThreadWithTurnsAsync(storePath, threadId, repoRoot);

            var input = string.Join(
                Environment.NewLine,
                """
                {"jsonrpc":"2.0","id":1,"method":"tianshu/thread/pending_input/update","params":{"threadId":"__THREAD_ID__","pendingInputState":{"entries":[{"correlationId":"corr-kernel-interrupt-001","requestedMode":"Interrupt","effectiveMode":"Interrupt","lifecycleState":"interrupt_requested","expectedTurnId":"turn-kernel-pending-001","turnId":"turn-kernel-interrupt-001","pendingBucket":"QueuedUserMessage","compareKey":{"message":"kernel interrupt message","imageCount":0}}],"pendingSteers":[{"correlationId":"corr-kernel-pending-001","requestedMode":"Steer","effectiveMode":"Steer","lifecycleState":"awaiting_commit","expectedTurnId":"turn-kernel-pending-001","turnId":null,"pendingBucket":"PendingSteer","compareKey":{"message":"kernel pending message","imageCount":0}}],"interruptRequestPending":true,"submitPendingSteersAfterInterrupt":true}}}
                """,
                """
                {"jsonrpc":"2.0","id":2,"method":"thread/read","params":{"threadId":"__THREAD_ID__","includeTurns":false}}
                """,
                """
                {"jsonrpc":"2.0","id":3,"method":"thread/resume","params":{"threadId":"__THREAD_ID__"}}
                """).Replace("__THREAD_ID__", threadId);

            threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Empty(messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement
                    .GetProperty("result")
                    .EnumerateObject());

                var threadRead = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread");
                Assert.False(threadRead.TryGetProperty("pendingInputState", out _));

                var threadResume = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement
                    .GetProperty("result")
                    .GetProperty("thread");
                Assert.False(threadResume.TryGetProperty("pendingInputState", out _));

                var persisted = await threadStore.GetThreadAsync(threadId, CancellationToken.None);
                Assert.NotNull(persisted);
                Assert.NotNull(persisted!.PendingInputState);
                Assert.True(persisted.PendingInputState!.InterruptRequestPending);
                Assert.True(persisted.PendingInputState.SubmitPendingSteersAfterInterrupt);
                Assert.Equal(
                    "corr-kernel-pending-001",
                    Assert.Single(persisted.PendingInputState.PendingSteers).CorrelationId);
                Assert.Equal(
                    "corr-kernel-interrupt-001",
                    Assert.Single(persisted.PendingInputState.Entries).CorrelationId);
                Assert.Equal(
                    "QueuedUserMessage",
                    Assert.Single(persisted.PendingInputState.Entries).PendingBucket);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            try
            {
                DeleteDirectory(root);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task WriteNotificationAsync_ShouldHonorClientOptOutNotificationMethods()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var updatePreferences = typeof(AppHostServer).GetMethod("UpdateClientCapabilities", BindingFlags.Instance | BindingFlags.NonPublic);
            var writeNotification = typeof(AppHostServer).GetMethod("WriteNotificationAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(updatePreferences);
            Assert.NotNull(writeNotification);

            using var json = JsonDocument.Parse("""
            {
              "capabilities": {
                "optOutNotificationMethods": [
                  "item/agentMessage/delta"
                ]
              }
            }
            """);

            _ = updatePreferences!.Invoke(server, [json.RootElement.Clone()]);
            var task = Assert.IsAssignableFrom<Task>(writeNotification!.Invoke(
                server,
                [
                    "item/agentMessage/delta",
                    new
                    {
                        threadId = "thread-1",
                        turnId = "turn-1",
                        itemId = "msg-1",
                        delta = "mirror",
                        item = new
                        {
                            id = "msg-1",
                            type = "agentMessage",
                            delta = "mirror",
                        },
                    },
                    CancellationToken.None,
                ]));

            await task;

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Empty(lines);

            var optedOut = GetPrivateField<HashSet<string>>(server, "optedOutNotificationMethods");
            Assert.Contains("item/agentMessage/delta", optedOut);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldHandleExtendedProtocolMethods()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        const string threadId = "00000000-0000-7000-8000-000000000216";
        var skillPath = Path.Combine(root, ".tianshu", "skills", "test-skill");
        var fuzzySessionId = "session_protocol_001";
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            Directory.CreateDirectory(skillPath);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"skills/list","params":{"cwds":["__ROOT__"]}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":2,"method":"model/list","params":{}}""",
                """{"jsonrpc":"2.0","id":3,"method":"skills/config/write","params":{"path":"__SKILL_PATH__","enabled":true,"cwd":"__ROOT__"}}""".Replace("__SKILL_PATH__", skillPath.Replace("\\", "/")).Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":4,"method":"config/value/write","params":{"key":"model","value":"gpt-5","cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":5,"method":"config/read","params":{"includeLayers":true}}""",
                """{"jsonrpc":"2.0","id":6,"method":"fuzzyFileSearch/sessionStart","params":{"sessionId":"__SESSION_ID__","roots":["__ROOT__"]}}""".Replace("__SESSION_ID__", fuzzySessionId).Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":7,"method":"fuzzyFileSearch/sessionUpdate","params":{"sessionId":"__SESSION_ID__","query":"threads"}}""".Replace("__SESSION_ID__", fuzzySessionId),
                """{"jsonrpc":"2.0","id":9,"method":"thread/loaded/list","params":{"limit":20}}""",
                """{"jsonrpc":"2.0","id":10,"method":"thread/compact/start","params":{"threadId":"00000000-0000-7000-8000-000000000216"}}""",
                """{"jsonrpc":"2.0","id":11,"method":"fuzzyFileSearch","params":{"query":"threads","roots":["__ROOT__"],"limit":5}}""".Replace("__ROOT__", root.Replace("\\", "/")));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var modelList = messages.Single(x => IsResponsePayload(x.RootElement, 2))
                    .RootElement
                    .GetProperty("result")
                    .GetProperty("data");
                Assert.True(modelList.ValueKind == JsonValueKind.Array);
                Assert.True(modelList.GetArrayLength() >= 1);

                var configWrite = messages.Single(x => IsResponsePayload(x.RootElement, 4))
                    .RootElement
                    .GetProperty("result");
                Assert.Contains(configWrite.GetProperty("status").GetString(), new[] { "ok", "okOverridden" });
                Assert.False(string.IsNullOrWhiteSpace(configWrite.GetProperty("version").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(configWrite.GetProperty("filePath").GetString()));

                var configRead = messages.Single(x => IsResponsePayload(x.RootElement, 5))
                    .RootElement
                    .GetProperty("result");
                Assert.True(configRead.TryGetProperty("config", out _));
                Assert.True(configRead.TryGetProperty("origins", out _));
                Assert.True(configRead.TryGetProperty("layers", out _));

                var fuzzyLegacy = messages.Single(x => IsResponsePayload(x.RootElement, 11))
                    .RootElement
                    .GetProperty("result");
                Assert.True(fuzzyLegacy.TryGetProperty("files", out var files));
                Assert.Equal(JsonValueKind.Array, files.ValueKind);


                var compactStartResponse = messages.Single(x => IsResponsePayload(x.RootElement, 10))
                    .RootElement
                    .GetProperty("result");
                Assert.Empty(compactStartResponse.EnumerateObject());

                var compactionStarted = Assert.Single(messages.Where(static x =>
                    IsNotificationMethod(x.RootElement, "item/started")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "contextCompaction"));
                var compactionCompleted = Assert.Single(messages.Where(static x =>
                    IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "contextCompaction"));
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "skills/changed"));
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "fuzzyFileSearch/sessionUpdated"));
                var compactedNotification = Assert.Single(messages.Where(static x => IsNotificationMethod(x.RootElement, "thread/compacted")));
                var compactedParams = compactedNotification.RootElement.GetProperty("params");
                Assert.Equal(threadId, compactedParams.GetProperty("threadId").GetString());
                Assert.Equal(JsonValueKind.String, compactedParams.GetProperty("turnId").ValueKind);
                Assert.Equal(threadId, compactionStarted.RootElement.GetProperty("params").GetProperty("threadId").GetString());
                Assert.Equal(threadId, compactionCompleted.RootElement.GetProperty("params").GetProperty("threadId").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task RunAsync_ShouldExposeCompactionAsSeedHistoryAndContextCompactionTurn()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000217";
        KernelThreadStore? setupStore = null;
        KernelThreadStore? threadStore = null;

        try
        {
            setupStore = await CreateMaterializedThreadWithTurnsAsync(storePath, threadId, root);
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            for (var index = 1; index <= 4; index++)
            {
                await setupStore.AppendCompletedTurnAsync(
                    threadId,
                    $"turn_compact_{index}",
                    $"旧问题{index}",
                    $"旧回答{index}",
                    "completed",
                    CancellationToken.None);
            }

            _ = await setupStore.CompactThreadAsync(threadId, keepRecentTurns: 2, CancellationToken.None);

            var input = """{"jsonrpc":"2.0","id":1,"method":"thread/read","params":{"threadId":"00000000-0000-7000-8000-000000000217","includeTurns":true}}""";
            threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            var line = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Single(static entry =>
                {
                    using var doc = JsonDocument.Parse(entry);
                    return IsResponseId(doc.RootElement, 1);
                });

            using var response = JsonDocument.Parse(line);

            var thread = response.RootElement.GetProperty("result").GetProperty("thread");
            Assert.False(thread.TryGetProperty("seedHistory", out _));
            var turns = thread.GetProperty("turns");
            Assert.True(turns.GetArrayLength() >= 3);
            var compactTurn = turns.EnumerateArray().Single(static turn =>
                turn.GetProperty("items").EnumerateArray().Any(static item =>
                    item.GetProperty("type").GetString() == "contextCompaction"));
            var compactItems = compactTurn.GetProperty("items").EnumerateArray().ToArray();
            var contextCompactionItem = Assert.Single(compactItems.Where(static item =>
                item.GetProperty("type").GetString() == "contextCompaction"));
            Assert.Equal(JsonValueKind.String, contextCompactionItem.GetProperty("id").ValueKind);
            Assert.False(contextCompactionItem.TryGetProperty("status", out _));
            Assert.DoesNotContain(compactItems, static item =>
                item.GetProperty("type").GetString() == "user_message"
                || item.GetProperty("type").GetString() == "assistant_message");
        }
        finally
        {
            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            try
            {
                DeleteDirectory(root);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAlignExperimentalAndParityProtocolMethods()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var originalCurrentDirectory = Environment.CurrentDirectory;
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.CurrentDirectory = root;
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", Path.Combine(root, "kernel-home"));
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
[collaboration_modes.builder]
mode = "primary"
model = "gpt-5"
""");

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"key":"mcp_servers.demo.url","value":"https://example.com/mcp","cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":2,"method":"config/value/write","params":{"key":"collaboration_modes.builder.mode","value":"primary","cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":3,"method":"config/value/write","params":{"key":"collaboration_modes.builder.model","value":"gpt-5","cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":4,"method":"model/list","params":{"limit":1}}""",
                """{"jsonrpc":"2.0","id":5,"method":"model/list","params":{"cursor":"1","limit":1}}""",
                """{"jsonrpc":"2.0","id":6,"method":"collaborationmode/list","params":{}}""",
                """{"jsonrpc":"2.0","id":7,"method":"mcpserverstatus/list","params":{}}""",
                """{"jsonrpc":"2.0","id":8,"method":"experimentalfeature/list","params":{"limit":1}}""",
                """{"jsonrpc":"2.0","id":9,"method":"experimentalfeature/list","params":{"cursor":"1","limit":10}}""",
                """{"jsonrpc":"2.0","id":10,"method":"windowsSandbox/setupStart","params":{"mode":"elevated"}}""",
                """{"jsonrpc":"2.0","id":11,"method":"config/mcpserver/reload","params":{}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"windowsSandbox/setupCompleted\"", TimeSpan.FromSeconds(5));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                for (var id = 1; id <= 11; id++)
                {
                    var response = messages.Single(x => IsResponseId(x.RootElement, id));
                    Assert.True(response.RootElement.TryGetProperty("result", out _));
                }

                var firstPage = messages.Single(x => IsResponseId(x.RootElement, 4))
                    .RootElement
                    .GetProperty("result");
                var firstData = firstPage.GetProperty("data");
                Assert.Equal(JsonValueKind.Array, firstData.ValueKind);
                Assert.Equal(1, firstData.GetArrayLength());
                Assert.False(string.IsNullOrWhiteSpace(firstPage.GetProperty("nextCursor").GetString()));

                var secondPage = messages.Single(x => IsResponseId(x.RootElement, 5))
                    .RootElement
                    .GetProperty("result")
                    .GetProperty("data");
                Assert.Equal(JsonValueKind.Array, secondPage.ValueKind);
                Assert.True(secondPage.GetArrayLength() >= 1);
                var modelItem = secondPage[0];
                Assert.True(modelItem.TryGetProperty("model", out _));
                Assert.True(modelItem.TryGetProperty("isDefault", out _));
                Assert.True(modelItem.TryGetProperty("supportedReasoningEfforts", out _));

                var collaborationData = messages.Single(x => IsResponseId(x.RootElement, 6))
                    .RootElement
                    .GetProperty("result")
                    .GetProperty("data");
                var collaborationModes = collaborationData.EnumerateArray().ToArray();
                Assert.Equal(2, collaborationModes.Length);
                Assert.Equal("Plan", collaborationModes[0].GetProperty("name").GetString());
                Assert.Equal("plan", collaborationModes[0].GetProperty("mode").GetString());
                Assert.Equal(JsonValueKind.Null, collaborationModes[0].GetProperty("model").ValueKind);
                Assert.Equal("medium", collaborationModes[0].GetProperty("reasoning_effort").GetString());
                Assert.Equal("Default", collaborationModes[1].GetProperty("name").GetString());
                Assert.Equal("default", collaborationModes[1].GetProperty("mode").GetString());
                Assert.Equal(JsonValueKind.Null, collaborationModes[1].GetProperty("model").ValueKind);
                Assert.Equal(JsonValueKind.Null, collaborationModes[1].GetProperty("reasoning_effort").ValueKind);
                Assert.DoesNotContain(
                    collaborationModes,
                    static item =>
                        item.TryGetProperty("name", out var name)
                        && string.Equals(name.GetString(), "builder", StringComparison.OrdinalIgnoreCase));

                var mcpData = messages.Single(x => IsResponseId(x.RootElement, 7))
                    .RootElement
                    .GetProperty("result")
                    .GetProperty("data");
                Assert.Contains(
                    mcpData.EnumerateArray(),
                    static item =>
                        item.TryGetProperty("name", out var name)
                        && string.Equals(name.GetString(), "demo", StringComparison.OrdinalIgnoreCase));
                var demoMcp = mcpData
                    .EnumerateArray()
                    .First(static item =>
                        item.TryGetProperty("name", out var name)
                        && string.Equals(name.GetString(), "demo", StringComparison.OrdinalIgnoreCase));
                Assert.Equal("not_logged_in", demoMcp.GetProperty("authStatus").GetString());

                var featuresFirstPage = messages.Single(x => IsResponseId(x.RootElement, 8))
                    .RootElement
                    .GetProperty("result");
                Assert.Equal(JsonValueKind.Array, featuresFirstPage.GetProperty("data").ValueKind);
                Assert.Equal(1, featuresFirstPage.GetProperty("data").GetArrayLength());
                Assert.False(string.IsNullOrWhiteSpace(featuresFirstPage.GetProperty("nextCursor").GetString()));

                var featuresSecondPage = messages.Single(x => IsResponseId(x.RootElement, 9))
                    .RootElement
                    .GetProperty("result")
                    .GetProperty("data");
                Assert.Equal(JsonValueKind.Array, featuresSecondPage.ValueKind);
                Assert.True(featuresSecondPage.GetArrayLength() >= 1);
                Assert.DoesNotContain(
                    featuresSecondPage.EnumerateArray(),
                    static item =>
                        item.TryGetProperty("name", out var name)
                        && string.Equals(name.GetString(), "mock/experimentalMethod", StringComparison.Ordinal));

                var sandboxResponse = messages.Single(x => IsResponseId(x.RootElement, 10))
                    .RootElement
                    .GetProperty("result");
                Assert.True(sandboxResponse.GetProperty("started").GetBoolean());

                Assert.Contains(messages, static x =>
                    IsNotificationMethod(x.RootElement, "windowsSandbox/setupCompleted")
                    && x.RootElement.TryGetProperty("params", out var @params)
                    && string.Equals(@params.GetProperty("mode").GetString(), "elevated", StringComparison.Ordinal)
                    && @params.TryGetProperty("success", out _));

                var setupCompleted = messages.Single(x => IsNotificationMethod(x.RootElement, "windowsSandbox/setupCompleted"))
                    .RootElement
                    .GetProperty("params");
                var setupSuccess = setupCompleted.GetProperty("success").GetBoolean();
                if (setupSuccess)
                {
                    if (setupCompleted.TryGetProperty("error", out var errorValue))
                    {
                        Assert.True(errorValue.ValueKind == JsonValueKind.Null
                            || string.IsNullOrWhiteSpace(errorValue.GetString()));
                    }
                }
                else
                {
                    Assert.True(
                        setupCompleted.TryGetProperty("error", out var errorValue)
                        && errorValue.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(errorValue.GetString()));
                }

                var reloadResponse = messages.Single(x => IsResponseId(x.RootElement, 11))
                    .RootElement
                    .GetProperty("result");
                Assert.True(reloadResponse.GetProperty("reloaded").GetBoolean());
                Assert.True(reloadResponse.GetProperty("serverCount").GetInt32() >= 1);

                Assert.Contains(messages, static x =>
                    IsNotificationMethod(x.RootElement, "mcpServerStatus/list/updated")
                    && x.RootElement.TryGetProperty("params", out var @params)
                    && @params.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Array
                    && data.EnumerateArray().Any(item =>
                        item.TryGetProperty("name", out var name)
                        && string.Equals(name.GetString(), "demo", StringComparison.OrdinalIgnoreCase)));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReturnCodexExperimentalFeatureMetadataSurface()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".tianshu"));
            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"key":"features.apps","value":true,"cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":2,"method":"experimentalfeature/list","params":{"limit":100}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                Assert.True(messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.TryGetProperty("result", out _));

                var result = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("result");
                Assert.Equal(JsonValueKind.Null, result.GetProperty("nextCursor").ValueKind);

                var features = result.GetProperty("data").EnumerateArray().ToArray();
                Assert.True(features.Length > 40);
                Assert.DoesNotContain(
                    features,
                    static item =>
                        item.TryGetProperty("name", out var name)
                        && string.Equals(name.GetString(), "mock/experimentalMethod", StringComparison.Ordinal));

                var jsRepl = features.First(static item =>
                    item.TryGetProperty("name", out var name)
                    && string.Equals(name.GetString(), "js_repl", StringComparison.Ordinal));
                Assert.Equal("beta", jsRepl.GetProperty("stage").GetString());
                Assert.Equal("JavaScript REPL", jsRepl.GetProperty("displayName").GetString());
                Assert.False(jsRepl.GetProperty("enabled").GetBoolean());
                Assert.False(jsRepl.GetProperty("defaultEnabled").GetBoolean());
                Assert.Contains("persistent Node-backed JavaScript REPL", jsRepl.GetProperty("description").GetString(), StringComparison.Ordinal);
                Assert.Contains("JavaScript REPL is now available", jsRepl.GetProperty("announcement").GetString(), StringComparison.Ordinal);

                var apps = features.First(static item =>
                    item.TryGetProperty("name", out var name)
                    && string.Equals(name.GetString(), "apps", StringComparison.Ordinal));
                Assert.Equal("beta", apps.GetProperty("stage").GetString());
                Assert.Equal("Apps", apps.GetProperty("displayName").GetString());
                Assert.True(apps.GetProperty("enabled").GetBoolean());
                Assert.False(apps.GetProperty("defaultEnabled").GetBoolean());

                var collaborationModes = features.First(static item =>
                    item.TryGetProperty("name", out var name)
                    && string.Equals(name.GetString(), "collaboration_modes", StringComparison.Ordinal));
                Assert.Equal("removed", collaborationModes.GetProperty("stage").GetString());
                Assert.True(collaborationModes.GetProperty("defaultEnabled").GetBoolean());

                var powershellUtf8 = features.First(static item =>
                    item.TryGetProperty("name", out var name)
                    && string.Equals(name.GetString(), "powershell_utf8", StringComparison.Ordinal));
                if (OperatingSystem.IsWindows())
                {
                    Assert.Equal("stable", powershellUtf8.GetProperty("stage").GetString());
                    Assert.True(powershellUtf8.GetProperty("defaultEnabled").GetBoolean());
                }
                else
                {
                    Assert.Equal("underDevelopment", powershellUtf8.GetProperty("stage").GetString());
                    Assert.False(powershellUtf8.GetProperty("defaultEnabled").GetBoolean());
                }

                var preventIdleSleep = features.First(static item =>
                    item.TryGetProperty("name", out var name)
                    && string.Equals(name.GetString(), "prevent_idle_sleep", StringComparison.Ordinal));
                if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    Assert.Equal("beta", preventIdleSleep.GetProperty("stage").GetString());
                    Assert.Equal("Prevent sleep while running", preventIdleSleep.GetProperty("displayName").GetString());
                    Assert.Contains("Keep your computer awake", preventIdleSleep.GetProperty("description").GetString(), StringComparison.Ordinal);
                }
                else
                {
                    Assert.Equal("underDevelopment", preventIdleSleep.GetProperty("stage").GetString());
                    Assert.Equal(JsonValueKind.Null, preventIdleSleep.GetProperty("displayName").ValueKind);
                }
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReturnCodexMcpAuthStatusEnumValues()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var originalCurrentDirectory = Environment.CurrentDirectory;
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.CurrentDirectory = root;
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", Path.Combine(root, "kernel-home"));
            Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));
            Directory.CreateDirectory(Path.Combine(root, ".tianshu"));

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"key":"mcp_servers.demo.url","value":"https://example.com/mcp","cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":2,"method":"mcpserverstatus/list","params":{}}""",
                """{"jsonrpc":"2.0","id":3,"method":"config/value/write","params":{"key":"mcp_servers.demo.api_key","value":"token-demo","cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":4,"method":"mcpserverstatus/list","params":{}}""",
                """{"jsonrpc":"2.0","id":5,"method":"config/value/write","params":{"key":"mcp_servers.demo.oauth_access_token","value":"oauth-demo","cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":6,"method":"mcpserverstatus/list","params":{}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                Assert.Equal("not_logged_in", ReadMcpAuthStatus(messages, 2, "demo"));
                Assert.Equal("bearer_token", ReadMcpAuthStatus(messages, 4, "demo"));
                Assert.Equal("oauth", ReadMcpAuthStatus(messages, 6, "demo"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_SkillsConfigWrite_ShouldPersistToUserLayer_WhenCwdIsNestedUnderRepo()
    {
        var root = CreateTempDirectory();
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "sandbox", "feature");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var rootConfigPath = Path.Combine(repoRoot, ".tianshu", "tianshu.toml");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var skillDocumentPath = Path.Combine(nestedWorkspace, ".tianshu", "skills", "nested-skill", "SKILL.md");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.GetDirectoryName(skillDocumentPath)!);
            await File.WriteAllTextAsync(skillDocumentPath, "nested skill");
            Directory.CreateDirectory(Path.GetDirectoryName(rootConfigPath)!);
            await File.WriteAllTextAsync(
                rootConfigPath,
                """
model = "gpt-5.4"

[[skills.config]]
path = "D:/upstream/skill/SKILL.md"
enabled = false
""");

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"skills/config/write","params":{"path":"__SKILL_PATH__","enabled":false,"cwd":"__CWD__"}}"""
                    .Replace("__SKILL_PATH__", skillDocumentPath.Replace("\\", "/"))
                    .Replace("__CWD__", nestedWorkspace.Replace("\\", "/")));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            Assert.True(File.Exists(userConfigPath));
            var userConfig = await File.ReadAllTextAsync(userConfigPath);
            Assert.Contains("[[skills.config]]", userConfig, StringComparison.Ordinal);
            Assert.Contains(skillDocumentPath.Replace("\\", "\\\\"), userConfig, StringComparison.Ordinal);
            Assert.Contains("enabled = false", userConfig, StringComparison.Ordinal);
            Assert.DoesNotContain("model = \"gpt-5.4\"", userConfig, StringComparison.Ordinal);
            Assert.DoesNotContain("D:/upstream/skill/SKILL.md", userConfig, StringComparison.Ordinal);

            var rootConfig = await File.ReadAllTextAsync(rootConfigPath);
            Assert.DoesNotContain(skillDocumentPath, rootConfig, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[[skills.config]]", rootConfig, StringComparison.Ordinal);
            Assert.Contains("D:/upstream/skill/SKILL.md", rootConfig, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_SkillsList_ShouldIgnoreProjectSkillsConfigOverrides_WhenEvaluatingEnabledState()
    {
        var root = CreateTempDirectory();
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "sandbox", "feature");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var projectConfigPath = Path.Combine(repoRoot, ".tianshu", "tianshu.toml");
        var skillDocumentPath = Path.Combine(repoRoot, ".agents", "skills", "repo-skill", "SKILL.md");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(nestedWorkspace);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.GetDirectoryName(projectConfigPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(skillDocumentPath)!);
            await File.WriteAllTextAsync(skillDocumentPath, "repo skill");
            await File.WriteAllTextAsync(
                projectConfigPath,
                $$"""
                [[skills.config]]
                path = "{{skillDocumentPath.Replace("\\", "/")}}"
                enabled = false
                """);

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"skills/list","params":{"cwds":["__CWD__"],"forceReload":true}}
                """
                    .Replace("__CWD__", nestedWorkspace.Replace("\\", "/"), StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var message = messages.Single(x => IsResponsePayload(x.RootElement, 1));
                var skills = message.RootElement.GetProperty("result").GetProperty("data")[0].GetProperty("skills");
                var repoSkill = skills.EnumerateArray().Single(skill =>
                    string.Equals(skill.GetProperty("name").GetString(), "repo-skill", StringComparison.OrdinalIgnoreCase));
                Assert.True(repoSkill.GetProperty("enabled").GetBoolean());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_SkillsList_ShouldResolveRelativeUserSkillsConfigPath()
    {
        var root = CreateTempDirectory();
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "sandbox", "feature");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var skillDocumentPath = Path.Combine(repoRoot, ".agents", "skills", "repo-skill", "SKILL.md");
        var relativeSkillPath = Path.GetRelativePath(tianShuHome, skillDocumentPath).Replace("\\", "/");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(nestedWorkspace);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.GetDirectoryName(skillDocumentPath)!);
            await File.WriteAllTextAsync(skillDocumentPath, "repo skill");
            await File.WriteAllTextAsync(
                userConfigPath,
                $$"""
                [[skills.config]]
                path = "{{relativeSkillPath}}"
                enabled = false
                """);

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"skills/list","params":{"cwds":["__CWD__"],"forceReload":true}}
                """
                    .Replace("__CWD__", nestedWorkspace.Replace("\\", "/"), StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var message = messages.Single(x => IsResponsePayload(x.RootElement, 1));
                var skills = message.RootElement.GetProperty("result").GetProperty("data")[0].GetProperty("skills");
                var repoSkill = skills.EnumerateArray().Single(skill =>
                    string.Equals(skill.GetProperty("name").GetString(), "repo-skill", StringComparison.OrdinalIgnoreCase));
                Assert.False(repoSkill.GetProperty("enabled").GetBoolean());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_SkillsList_ShouldResolveRelativeSkillDirectoryPathToCanonicalDocument()
    {
        var root = CreateTempDirectory();
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "sandbox", "feature");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var skillDocumentPath = Path.Combine(repoRoot, ".agents", "skills", "repo-skill", "SKILL.md");
        var relativeSkillDirectoryPath = Path.GetRelativePath(tianShuHome, Path.GetDirectoryName(skillDocumentPath)!).Replace("\\", "/");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(nestedWorkspace);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.GetDirectoryName(skillDocumentPath)!);
            await File.WriteAllTextAsync(skillDocumentPath, "repo skill");
            await File.WriteAllTextAsync(
                userConfigPath,
                $$"""
                [[skills.config]]
                path = "{{relativeSkillDirectoryPath}}"
                enabled = false
                """);

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"skills/list","params":{"cwds":["__CWD__"],"forceReload":true}}
                """
                    .Replace("__CWD__", nestedWorkspace.Replace("\\", "/"), StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var message = messages.Single(x => IsResponsePayload(x.RootElement, 1));
                var skills = message.RootElement.GetProperty("result").GetProperty("data")[0].GetProperty("skills");
                var repoSkill = skills.EnumerateArray().Single(skill =>
                    string.Equals(skill.GetProperty("name").GetString(), "repo-skill", StringComparison.OrdinalIgnoreCase));
                Assert.False(repoSkill.GetProperty("enabled").GetBoolean());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_SkillsList_ShouldExcludeSystemSkillsWhenBundledSkillsDisabled()
    {
        var root = CreateTempDirectory();
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "sandbox", "feature");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var bundledSkillPath = Path.Combine(tianShuHome, "modules", "skills", ".system", "bundled-skill", "SKILL.md");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(nestedWorkspace);
            Directory.CreateDirectory(Path.GetDirectoryName(bundledSkillPath)!);
            await File.WriteAllTextAsync(bundledSkillPath, "bundled skill");
            await File.WriteAllTextAsync(
                userConfigPath,
                """
                [skills.bundled]
                enabled = false
                """);

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"skills/list","params":{"cwds":["__CWD__"],"forceReload":true}}
                """
                    .Replace("__CWD__", nestedWorkspace.Replace("\\", "/"), StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var message = messages.Single(x => IsResponsePayload(x.RootElement, 1));
                var skills = message.RootElement.GetProperty("result").GetProperty("data")[0].GetProperty("skills");
                Assert.DoesNotContain(
                    skills.EnumerateArray(),
                    skill => string.Equals(skill.GetProperty("name").GetString(), "bundled-skill", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_SkillsList_ShouldMergePerCwdExtraUserRootsAcrossDuplicateEntries()
    {
        var root = CreateTempDirectory();
        var cwd = Path.Combine(root, "workspace");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var extraRootA = Path.Combine(root, "extra-root-a");
        var extraRootB = Path.Combine(root, "extra-root-b");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await WriteSkillFixtureAsync(extraRootA, "extra-a");
            await WriteSkillFixtureAsync(extraRootB, "extra-b");

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"skills/list","params":{"cwds":["__CWD__"],"forceReload":true,"perCwdExtraUserRoots":[{"cwd":"__CWD__","extraUserRoots":["__ROOT_A__"]},{"cwd":"__CWD__","extraUserRoots":["__ROOT_B__"]}]}}
                """
                    .Replace("__CWD__", cwd.Replace("\\", "/"), StringComparison.Ordinal)
                    .Replace("__ROOT_A__", extraRootA.Replace("\\", "/"), StringComparison.Ordinal)
                    .Replace("__ROOT_B__", extraRootB.Replace("\\", "/"), StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var message = messages.Single(x => IsResponsePayload(x.RootElement, 1));
                var skills = message.RootElement.GetProperty("result").GetProperty("data")[0].GetProperty("skills");
                Assert.Contains(skills.EnumerateArray(), skill => string.Equals(skill.GetProperty("name").GetString(), "extra-a", StringComparison.Ordinal));
                Assert.Contains(skills.EnumerateArray(), skill => string.Equals(skill.GetProperty("name").GetString(), "extra-b", StringComparison.Ordinal));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_SkillsList_ShouldRejectRelativePerCwdExtraUserRootsWithOffendingPath()
    {
        var root = CreateTempDirectory();
        var cwd = Path.Combine(root, "workspace");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"skills/list","params":{"cwds":["__CWD__"],"forceReload":true,"perCwdExtraUserRoots":[{"cwd":"__CWD__","extraUserRoots":["relative/skills"]}]}}
                """
                    .Replace("__CWD__", cwd.Replace("\\", "/"), StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.Equal(
                    "skills/list perCwdExtraUserRoots extraUserRoots paths must be absolute: relative/skills",
                    error.GetProperty("message").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_SkillsList_ShouldIgnorePerCwdExtraUserRootsForUnknownCwd()
    {
        var root = CreateTempDirectory();
        var requestedCwd = Path.Combine(root, "requested");
        var unknownCwd = Path.Combine(root, "unknown");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var extraRoot = Path.Combine(root, "extra-root");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(requestedCwd);
            Directory.CreateDirectory(unknownCwd);
            await WriteSkillFixtureAsync(extraRoot, "ignored-extra");

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"skills/list","params":{"cwds":["__REQUESTED_CWD__"],"forceReload":true,"perCwdExtraUserRoots":[{"cwd":"__UNKNOWN_CWD__","extraUserRoots":["__EXTRA_ROOT__"]}]}}
                """
                    .Replace("__REQUESTED_CWD__", requestedCwd.Replace("\\", "/"), StringComparison.Ordinal)
                    .Replace("__UNKNOWN_CWD__", unknownCwd.Replace("\\", "/"), StringComparison.Ordinal)
                    .Replace("__EXTRA_ROOT__", extraRoot.Replace("\\", "/"), StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var message = messages.Single(x => IsResponsePayload(x.RootElement, 1));
                var data = message.RootElement.GetProperty("result").GetProperty("data");
                Assert.Equal(1, data.GetArrayLength());
                Assert.Equal(Path.GetFullPath(requestedCwd), data[0].GetProperty("cwd").GetString());
                Assert.DoesNotContain(
                    data[0].GetProperty("skills").EnumerateArray(),
                    skill => string.Equals(skill.GetProperty("name").GetString(), "ignored-extra", StringComparison.Ordinal));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_SkillsList_ShouldUseCachedResultUntilForceReload()
    {
        var root = CreateTempDirectory();
        var cwd = Path.Combine(root, "workspace");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var extraRoot = Path.Combine(root, "extra-root");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await WriteSkillFixtureAsync(extraRoot, "late-extra");

            var input = string.Join(
                Environment.NewLine,
                """
                {"jsonrpc":"2.0","id":1,"method":"skills/list","params":{"cwds":["__CWD__"],"forceReload":false}}
                """
                    .Replace("__CWD__", cwd.Replace("\\", "/"), StringComparison.Ordinal),
                """
                {"jsonrpc":"2.0","id":2,"method":"skills/list","params":{"cwds":["__CWD__"],"forceReload":false,"perCwdExtraUserRoots":[{"cwd":"__CWD__","extraUserRoots":["__EXTRA_ROOT__"]}]}}
                """
                    .Replace("__CWD__", cwd.Replace("\\", "/"), StringComparison.Ordinal)
                    .Replace("__EXTRA_ROOT__", extraRoot.Replace("\\", "/"), StringComparison.Ordinal),
                """
                {"jsonrpc":"2.0","id":3,"method":"skills/list","params":{"cwds":["__CWD__"],"forceReload":true,"perCwdExtraUserRoots":[{"cwd":"__CWD__","extraUserRoots":["__EXTRA_ROOT__"]}]}}
                """
                    .Replace("__CWD__", cwd.Replace("\\", "/"), StringComparison.Ordinal)
                    .Replace("__EXTRA_ROOT__", extraRoot.Replace("\\", "/"), StringComparison.Ordinal));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var firstSkills = messages.Single(x => IsResponsePayload(x.RootElement, 1)).RootElement.GetProperty("result").GetProperty("data")[0].GetProperty("skills");
                var secondSkills = messages.Single(x => IsResponsePayload(x.RootElement, 2)).RootElement.GetProperty("result").GetProperty("data")[0].GetProperty("skills");
                var thirdSkills = messages.Single(x => IsResponsePayload(x.RootElement, 3)).RootElement.GetProperty("result").GetProperty("data")[0].GetProperty("skills");

                Assert.DoesNotContain(
                    firstSkills.EnumerateArray(),
                    skill => string.Equals(skill.GetProperty("name").GetString(), "late-extra", StringComparison.Ordinal));
                Assert.Contains(
                    secondSkills.EnumerateArray(),
                    skill => string.Equals(skill.GetProperty("name").GetString(), "late-extra", StringComparison.Ordinal));
                Assert.Contains(
                    thirdSkills.EnumerateArray(),
                    skill => string.Equals(skill.GetProperty("name").GetString(), "late-extra", StringComparison.Ordinal));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_SkillsConfigWrite_ShouldCanonicalizeExistingRelativeDirectoryEntryWithoutDuplication()
    {
        var root = CreateTempDirectory();
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "sandbox", "feature");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var skillDirectoryPath = Path.Combine(repoRoot, ".agents", "skills", "repo-skill");
        var skillDocumentPath = Path.Combine(skillDirectoryPath, "SKILL.md");
        var relativeSkillDirectoryPath = Path.GetRelativePath(tianShuHome, skillDirectoryPath).Replace("\\", "/");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(nestedWorkspace);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(skillDirectoryPath);
            await File.WriteAllTextAsync(skillDocumentPath, "repo skill");
            await File.WriteAllTextAsync(
                userConfigPath,
                $$"""
                [[skills.config]]
                path = "{{relativeSkillDirectoryPath}}"
                enabled = false
                """);

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"skills/config/write","params":{"path":"__SKILL_DIR__","enabled":false,"cwd":"__CWD__"}}
                """
                    .Replace("__SKILL_DIR__", skillDirectoryPath.Replace("\\", "/"), StringComparison.Ordinal)
                    .Replace("__CWD__", nestedWorkspace.Replace("\\", "/"), StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var saved = Toml.ToModel(await File.ReadAllTextAsync(userConfigPath, CancellationToken.None)) as TomlTable;
            Assert.NotNull(saved);
            var skills = Assert.IsType<TomlTable>(saved!["skills"]);
            var config = Assert.IsType<TomlTableArray>(skills["config"]);
            var entry = Assert.IsType<TomlTable>(Assert.Single(config));
            Assert.Equal(Path.GetFullPath(skillDocumentPath), entry["path"]?.ToString());
            Assert.Equal(false, entry["enabled"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigValueWrite_ShouldPersistOnlyCurrentWorkspaceLayer_WhenCwdIsNestedUnderRepo()
    {
        var root = CreateTempDirectory();
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "sandbox", "feature");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var rootConfigPath = Path.Combine(repoRoot, ".tianshu", "tianshu.toml");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(nestedWorkspace);
            Directory.CreateDirectory(Path.GetDirectoryName(rootConfigPath)!);
            await File.WriteAllTextAsync(rootConfigPath, "model = \"gpt-5.4\"\n");

            var input = """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"key":"review_model","value":"o3","cwd":"__CWD__"}}"""
                .Replace("__CWD__", nestedWorkspace.Replace("\\", "/"));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            Assert.True(File.Exists(userConfigPath));
            var userConfig = await File.ReadAllTextAsync(userConfigPath);
            Assert.Contains("review_model = \"o3\"", userConfig, StringComparison.Ordinal);
            Assert.DoesNotContain("model = \"gpt-5.4\"", userConfig, StringComparison.Ordinal);

            var rootConfig = await File.ReadAllTextAsync(rootConfigPath);
            Assert.Equal("model = \"gpt-5.4\"\n", rootConfig.Replace("\r\n", "\n"), StringComparer.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ModelList_ShouldExposeBundledCatalogEntriesWhenEndpointUnavailable()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var TianShuHome = Path.Combine(root, "tianshu-home");

        try
        {
            using var TianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", TianShuHome);
            using var openAiApiKeyScope = new EnvironmentVariableScope("OPENAI_API_KEY", null);
            Directory.CreateDirectory(TianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(TianShuHome, "tianshu.toml"),
                """
model = "gpt-5.4"
model_reasoning_effort = "xhigh"
""");

            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "model/list",
                @params = new
                {
                    limit = 50,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var models = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result")
                    .GetProperty("data")
                    .EnumerateArray()
                    .ToArray();

                Assert.True(models.Length > 1);

                var gpt54 = models.Single(x =>
                    string.Equals(x.GetProperty("model").GetString(), "gpt-5.4", StringComparison.OrdinalIgnoreCase));
                Assert.Equal("gpt-5.4", gpt54.GetProperty("id").GetString());
                Assert.Contains(
                    gpt54.GetProperty("supportedReasoningEfforts").EnumerateArray(),
                    static effort => string.Equals(
                        effort.GetProperty("reasoningEffort").GetString(),
                        "xhigh",
                        StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(
                    models,
                    static model => string.Equals(
                        model.GetProperty("model").GetString(),
                        "gpt-5",
                        StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ModelList_ShouldAlignResponseShapeWithCodexProtocol()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var input = """{"jsonrpc":"2.0","id":1,"method":"model/list","params":{"limit":1}}""";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var response = ParseResponseDocument(writer.ToString(), 1);
            var item = response.RootElement
                .GetProperty("result")
                .GetProperty("data")[0];

            var propertyNames = item.EnumerateObject().Select(static property => property.Name).ToArray();
            Assert.Equal(
                [
                    "id",
                    "model",
                    "upgrade",
                    "upgradeInfo",
                    "availabilityNux",
                    "displayName",
                    "description",
                    "hidden",
                    "supportedReasoningEfforts",
                    "defaultReasoningEffort",
                    "inputModalities",
                    "supportsPersonality",
                    "supportsParallelToolCalls",
                    "supportsReasoningSummaries",
                    "defaultReasoningSummary",
                    "supportsVerbosity",
                    "defaultVerbosity",
                    "preferWebsocketTransport",
                    "isDefault",
                ],
                propertyNames);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ModelList_ShouldUseEndpointModelsFromTianShuProviderConfig()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var TianShuHome = Path.Combine(root, "tianshu-home");
        var apiKeyEnv = $"TIANSHU_TEST_API_KEY_{Guid.NewGuid():N}";
        var originalCurrentDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = root;
            using var endpointServer = ModelCatalogEndpointServer.Start(
                """
                {
                  "object": "list",
                  "data": [
                    { "id": "z-endpoint-model", "object": "model" },
                    { "id": "a-endpoint-model", "object": "model" }
                  ]
                }
                """);
            using var TianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", TianShuHome);
            using var apiKeyScope = new EnvironmentVariableScope(apiKeyEnv, "test-api-key");
            Directory.CreateDirectory(TianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(TianShuHome, "tianshu.toml"),
                $$"""
model = "z-endpoint-model"
provider = "openai-compatible"

[providers.openai-compatible]
base_url = "{{endpointServer.BaseUrl}}"
api_key_env = "{{apiKeyEnv}}"
protocol = "openai_chat_completions"
""");

            var input = """{"jsonrpc":"2.0","id":1,"method":"model/list","params":{"includeHidden":true,"limit":50,"requireEndpoint":true}}""";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var appHostServer = new AppHostServer(reader, writer, threadStore);

            await appHostServer.RunAsync(CancellationToken.None);

            using var response = ParseResponseDocument(writer.ToString(), 1);
            var models = response.RootElement
                .GetProperty("result")
                .GetProperty("data")
                .EnumerateArray()
                .ToArray();

            Assert.Equal(["a-endpoint-model", "z-endpoint-model"], models.Select(static model => model.GetProperty("model").GetString() ?? string.Empty).ToArray());
            Assert.False(models[0].GetProperty("isDefault").GetBoolean());
            Assert.True(models[1].GetProperty("isDefault").GetBoolean());
            Assert.Equal("/v1/models", endpointServer.LastRequestPath);
            Assert.Equal("Bearer test-api-key", endpointServer.LastAuthorizationHeader);
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ModelList_ShouldReportEndpointDiagnosticWhenTianShuConfigMissing()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var TianShuHome = Path.Combine(root, "tianshu-home");

        try
        {
            using var TianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", TianShuHome);
            var input = """{"jsonrpc":"2.0","id":1,"method":"model/list","params":{"requireEndpoint":true}}""";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var response = ParseResponseDocument(writer.ToString(), 1);
            var error = response.RootElement.GetProperty("error");
            Assert.Equal(-32050, error.GetProperty("code").GetInt32());
            var message = error.GetProperty("message").GetString();
            Assert.Contains("base_url", message, StringComparison.Ordinal);
            Assert.Contains("tianshu.toml", message, StringComparison.Ordinal);
            Assert.DoesNotContain("gpt-5.3-codex", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ModelList_ShouldPreferProtocolModelsEndpointForRootBaseUrl()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var TianShuHome = Path.Combine(root, "tianshu-home");
        var apiKeyEnv = $"TIANSHU_TEST_API_KEY_{Guid.NewGuid():N}";
        var originalCurrentDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = root;
            using var endpointServer = ModelCatalogEndpointServer.Start(
                """
                {
                  "object": "list",
                  "data": [
                    { "id": "root-base-url-model", "object": "model" }
                  ]
                }
                """);
            using var TianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", TianShuHome);
            using var apiKeyScope = new EnvironmentVariableScope(apiKeyEnv, "test-api-key");
            Directory.CreateDirectory(TianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(TianShuHome, "tianshu.toml"),
                $$"""
model = "root-base-url-model"
provider = "openai-compatible"

[providers.openai-compatible]
base_url = "{{endpointServer.RootUrl}}"
api_key_env = "{{apiKeyEnv}}"
protocol = "openai_chat_completions"
""");

            var input = """{"jsonrpc":"2.0","id":1,"method":"model/list","params":{"requireEndpoint":true}}""";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var response = ParseResponseDocument(writer.ToString(), 1);
            var model = Assert.Single(response.RootElement.GetProperty("result").GetProperty("data").EnumerateArray());
            Assert.Equal("root-base-url-model", model.GetProperty("model").GetString());
            Assert.Equal(["/v1/models"], endpointServer.RequestPaths);
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ModelList_ShouldContinueAfterNonJsonEndpointResponse()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var TianShuHome = Path.Combine(root, "tianshu-home");
        var apiKeyEnv = $"TIANSHU_TEST_API_KEY_{Guid.NewGuid():N}";
        var originalCurrentDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = root;
            using var endpointServer = ModelCatalogEndpointServer.Start(
                """
                {
                  "object": "list",
                  "data": [
                    { "id": "fallback-model", "object": "model" }
                  ]
                }
                """,
                htmlPaths: ["/v1/models"]);
            using var TianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", TianShuHome);
            using var apiKeyScope = new EnvironmentVariableScope(apiKeyEnv, "test-api-key");
            Directory.CreateDirectory(TianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(TianShuHome, "tianshu.toml"),
                $$"""
model = "fallback-model"
provider = "openai-compatible"

[providers.openai-compatible]
base_url = "{{endpointServer.RootUrl}}"
api_key_env = "{{apiKeyEnv}}"
protocol = "openai_chat_completions"
""");

            var input = """{"jsonrpc":"2.0","id":1,"method":"model/list","params":{"requireEndpoint":true}}""";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var response = ParseResponseDocument(writer.ToString(), 1);
            var model = Assert.Single(response.RootElement.GetProperty("result").GetProperty("data").EnumerateArray());
            Assert.Equal("fallback-model", model.GetProperty("model").GetString());
            Assert.Equal(["/v1/models", "/models"], endpointServer.RequestPaths);
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldValidateConfigWritesAgainstConstraints()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var validationFilePath = Path.Combine(tianShuHome, "tianshu.toml").Replace("\\", "/");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"filePath":"__FILE__","key":"approvalPolicy","value":"invalid-policy"}}""",
                """{"jsonrpc":"2.0","id":2,"method":"config/value/write","params":{"filePath":"__FILE__","key":"sandbox.type","value":"invalid-mode"}}""",
                """{"jsonrpc":"2.0","id":3,"method":"config/value/write","params":{"filePath":"__FILE__","key":"mcp_servers.demo.url","value":"not-a-url"}}""",
                """{"jsonrpc":"2.0","id":4,"method":"config/value/write","params":{"filePath":"__FILE__","key":"apps.demo.enabled","value":"yes"}}""",
                """{"jsonrpc":"2.0","id":5,"method":"config/value/write","params":{"filePath":"__FILE__","key":"model","value":"gpt-5"}}""",
                """{"jsonrpc":"2.0","id":6,"method":"config/batchWrite","params":{"filePath":"__FILE__","edits":[{"keyPath":"review_model","value":"o3"},{"keyPath":"mcp_servers.demo.url","value":"still-not-a-url"}]}}"""
            ).Replace("__FILE__", validationFilePath, StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var invalidApproval = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                var invalidSandbox = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("error");
                var invalidUrl = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement.GetProperty("error");
                var invalidBool = messages.Single(x => IsResponseId(x.RootElement, 4)).RootElement.GetProperty("error");
                var invalidBatch = messages.Single(x => IsResponseId(x.RootElement, 6)).RootElement.GetProperty("error");

                Assert.Equal(-32602, invalidApproval.GetProperty("code").GetInt32());
                Assert.Equal(-32602, invalidSandbox.GetProperty("code").GetInt32());
                Assert.Equal(-32602, invalidUrl.GetProperty("code").GetInt32());
                Assert.Equal(-32602, invalidBool.GetProperty("code").GetInt32());
                Assert.Equal(-32602, invalidBatch.GetProperty("code").GetInt32());

                var success = messages.Single(x => IsResponsePayload(x.RootElement, 5)).RootElement.GetProperty("result");
                Assert.Contains(success.GetProperty("status").GetString(), new[] { "ok", "okOverridden" });
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            Assert.True(File.Exists(validationFilePath));
            var savedToml = await File.ReadAllTextAsync(validationFilePath, CancellationToken.None);
            var saved = Toml.ToModel(savedToml) as TomlTable;
            Assert.NotNull(saved);
            Assert.Equal("gpt-5", saved!["model"]?.ToString());
            Assert.False(saved.ContainsKey("review_model"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigValueWrite_ShouldPreserveCommentsAndOrder()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var filePath = userConfigPath.Replace("\\", "/");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                userConfigPath,
                """
                # TianShu user configuration
                model = "gpt-5"
                approval_policy = "on-request"

                [notice]
                # Preserve this comment
                hide_full_access_warning = true

                [features]
                unified_exec = true
                """);

            var input = """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"filePath":"__FILE__","key":"features.personality","value":true}}"""
                .Replace("__FILE__", filePath, StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var result = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal("ok", result.GetProperty("status").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var updated = (await File.ReadAllTextAsync(userConfigPath)).Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.Equal(
                """
                # TianShu user configuration
                model = "gpt-5"
                approval_policy = "on-request"

                [notice]
                # Preserve this comment
                hide_full_access_warning = true

                [features]
                unified_exec = true
                personality = true
                
                """
                .Replace("\r\n", "\n", StringComparison.Ordinal),
                updated);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigValueWrite_WhenEditIsNoOp_ShouldKeepFileTextAndVersion()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var filePath = userConfigPath.Replace("\\", "/");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                userConfigPath,
                """
                # keep comment
                model = "gpt-5"
                """);

            var readInput = """{"jsonrpc":"2.0","id":1,"method":"config/read","params":{"includeLayers":true}}""";
            var readStore = new KernelThreadStore(storePath);
            var readReader = new StringReader(KernelAppServerTestProtocol.WithInitialize(readInput));
            var readWriter = new StringWriter();
            var readServer = new AppHostServer(readReader, readWriter, readStore);
            await readServer.RunAsync(CancellationToken.None);

            var readMessages = readWriter
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            using var readMessage = readMessages.Single(x => IsResponseId(x.RootElement, 1));
            var expectedVersion = readMessage.RootElement
                .GetProperty("result")
                .GetProperty("origins")
                .GetProperty("model")
                .GetProperty("version")
                .GetString();
            Assert.False(string.IsNullOrWhiteSpace(expectedVersion));

            var before = await File.ReadAllTextAsync(userConfigPath);
            var input = """{"jsonrpc":"2.0","id":2,"method":"config/value/write","params":{"filePath":"__FILE__","key":"model","value":"gpt-5","expectedVersion":"__VERSION__"}}"""
                .Replace("__FILE__", filePath, StringComparison.Ordinal)
                .Replace("__VERSION__", expectedVersion, StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var result = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("result");
                Assert.Equal("ok", result.GetProperty("status").GetString());
                Assert.Equal(expectedVersion, result.GetProperty("version").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var after = await File.ReadAllTextAsync(userConfigPath);
            Assert.Equal(before, after);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigValueWrite_ShouldValidateMergedNestedObjects()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                userConfigPath,
                """
                [permissions]
                sandbox_mode = "workspace-write"
                """);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "requirements.toml"),
                """
                allowed_approval_policies = ["never"]
                allowed_sandbox_modes = ["workspace-write"]
                """);

            var before = await File.ReadAllTextAsync(userConfigPath, CancellationToken.None);
            var input = """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"key":"permissions","value":{"approval_policy":"invalid-policy"},"mergeStrategy":"upsert"}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                Assert.Contains("permissions.approval_policy", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var after = await File.ReadAllTextAsync(userConfigPath, CancellationToken.None);
            Assert.Equal(before, after);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigValueWrite_ShouldRejectFeatureRequirementConflict()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                userConfigPath,
                """
                [features]
                personality = true
                """);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "requirements.toml"),
                """
                [features]
                personality = true
                """);

            var before = await File.ReadAllTextAsync(userConfigPath, CancellationToken.None);
            var input = """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"key":"features.personality","value":false}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                Assert.Contains("features.personality", error.GetProperty("message").GetString(), StringComparison.Ordinal);
                Assert.Contains("requirements.toml", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var after = await File.ReadAllTextAsync(userConfigPath, CancellationToken.None);
            Assert.Equal(before, after);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigValueWrite_ShouldValidateAgainstMergedCloudAdminAndSystemRequirements()
    {
        const string systemRootOverrideEnvironmentVariable = "TIANSHU_SYSTEM_CONFIG_ROOT";
        const string cloudRequirementsTomlEnvironmentVariable = "TIANSHU_CLOUD_REQUIREMENTS_TOML";
        const string adminRequirementsTomlEnvironmentVariable = "TIANSHU_ADMIN_REQUIREMENTS_TOML";

        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var systemRoot = Path.Combine(root, "system-tianshu");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalSystemRoot = Environment.GetEnvironmentVariable(systemRootOverrideEnvironmentVariable);
        var originalCloudRequirements = Environment.GetEnvironmentVariable(cloudRequirementsTomlEnvironmentVariable);
        var originalAdminRequirements = Environment.GetEnvironmentVariable(adminRequirementsTomlEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Environment.SetEnvironmentVariable(systemRootOverrideEnvironmentVariable, systemRoot);
            Environment.SetEnvironmentVariable(
                cloudRequirementsTomlEnvironmentVariable,
                """
                allowed_approval_policies = ["never"]
                """);
            Environment.SetEnvironmentVariable(
                adminRequirementsTomlEnvironmentVariable,
                """
                allowed_approval_policies = ["on-request"]
                """);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(systemRoot);
            await File.WriteAllTextAsync(
                userConfigPath,
                """
                model = "gpt-5"
                approval_policy = "never"
                """);
            await File.WriteAllTextAsync(
                Path.Combine(systemRoot, "requirements.toml"),
                """
                allowed_approval_policies = ["always"]
                """);

            var before = await File.ReadAllTextAsync(userConfigPath, CancellationToken.None);
            var input = """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"key":"approval_policy","value":"on-request"}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                Assert.Contains("approval_policy", error.GetProperty("message").GetString(), StringComparison.Ordinal);
                Assert.Contains("never", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var after = await File.ReadAllTextAsync(userConfigPath, CancellationToken.None);
            Assert.Equal(before, after);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable(systemRootOverrideEnvironmentVariable, originalSystemRoot);
            Environment.SetEnvironmentVariable(cloudRequirementsTomlEnvironmentVariable, originalCloudRequirements);
            Environment.SetEnvironmentVariable(adminRequirementsTomlEnvironmentVariable, originalAdminRequirements);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadArchiveArchivesLoadedThread_ShouldRemoveLoadedRuntime()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000106";
        KernelThreadStore? threadStore = null;

        try
        {
            threadStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                threadId,
                Path.Combine(root, "repo-archive-loaded"),
                ("turn_archive_loaded", "archive loaded user", "archive loaded assistant"));
            await threadStore.RolloutRecorder.CloseAllThreadWritersAsync();

            var reader = new StringReader(string.Empty);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await InvokeHandleThreadResumeAsync(
                server,
                1,
                $$"""
                {
                  "threadId": "{{threadId}}"
                }
                """);

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.IsLoaded(threadId));

            await InvokeHandleThreadArchiveAsync(
                server,
                2,
                $$"""
                {
                  "threadId": "{{threadId}}"
                }
                """);

            Assert.False(threadManager.IsLoaded(threadId));
            Assert.False(threadManager.TryGetThread(threadId, out _));

            Assert.False(File.Exists(Path.Combine(root, "sessions", $"{threadId}.jsonl")));
            Assert.True(File.Exists(Path.Combine(root, "archived_sessions", $"{threadId}.jsonl")));

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                Assert.Contains(messages, x => IsResponsePayload(x.RootElement, 2));
                Assert.Contains(messages, x => IsNotificationMethod(x.RootElement, "thread/archived"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync();
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigValueWrite_ShouldFailWhenExpectedVersionStale()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var filePath = userConfigPath.Replace("\\", "/");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(userConfigPath, "model = \"gpt-5\"\n");

            var readInput = """{"jsonrpc":"2.0","id":1,"method":"config/read","params":{"includeLayers":true}}""";
            var readStore = new KernelThreadStore(storePath);
            var readReader = new StringReader(KernelAppServerTestProtocol.WithInitialize(readInput));
            var readWriter = new StringWriter();
            var readServer = new AppHostServer(readReader, readWriter, readStore);
            await readServer.RunAsync(CancellationToken.None);

            var readMessages = readWriter
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            using var readMessage = readMessages.Single(x => IsResponseId(x.RootElement, 1));
            var expectedVersion = readMessage.RootElement
                .GetProperty("result")
                .GetProperty("origins")
                .GetProperty("model")
                .GetProperty("version")
                .GetString();
            Assert.False(string.IsNullOrWhiteSpace(expectedVersion));

            await File.WriteAllTextAsync(
                userConfigPath,
                """
                model = "gpt-5"
                review_model = "o3"
                """);

            var input = """{"jsonrpc":"2.0","id":2,"method":"config/value/write","params":{"filePath":"__FILE__","key":"model","value":"gpt-5-mini","expectedVersion":"__VERSION__"}}"""
                .Replace("__FILE__", filePath, StringComparison.Ordinal)
                .Replace("__VERSION__", expectedVersion, StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.Equal("configVersionConflict", error.GetProperty("data").GetProperty("errorCode").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var after = await File.ReadAllTextAsync(userConfigPath, CancellationToken.None);
            Assert.Contains("review_model = \"o3\"", after, StringComparison.Ordinal);
            Assert.Contains("model = \"gpt-5\"", after, StringComparison.Ordinal);
            Assert.DoesNotContain("gpt-5-mini", after, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigValueWrite_ShouldRejectNonUserConfigFilePath()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var workspaceConfigPath = Path.Combine(root, "workspace", ".tianshu", "tianshu.toml").Replace("\\", "/");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(root, "workspace", ".tianshu"));

            var input = """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"filePath":"__FILE__","key":"model","value":"gpt-5"}}"""
                .Replace("__FILE__", workspaceConfigPath, StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.Contains("Only writes to the user config are allowed", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigValueWrite_ShouldAllowSymlinkToUserConfigFilePath()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);

            var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
            await File.WriteAllTextAsync(userConfigPath, string.Empty);

            var symlinkDirectory = Path.Combine(root, "config-links");
            Directory.CreateDirectory(symlinkDirectory);
            var symlinkPath = Path.Combine(symlinkDirectory, "user-config-link.toml");
            try
            {
                File.CreateSymbolicLink(symlinkPath, userConfigPath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException or NotSupportedException)
            {
                return;
            }

            var input = """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"filePath":"__FILE__","key":"model","value":"gpt-5"}}"""
                .Replace("__FILE__", symlinkPath.Replace("\\", "/"), StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var result = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal("ok", result.GetProperty("status").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var userConfig = await File.ReadAllTextAsync(userConfigPath, CancellationToken.None);
            Assert.Contains("model = \"gpt-5\"", userConfig, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigValueWrite_ShouldReturnOverriddenMetadata_WhenProjectConfigWins()
    {
        var root = CreateTempDirectory();
        var repoRoot = Path.Combine(root, "repo");
        var workspace = Path.Combine(repoRoot, "workspace");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var projectConfigPath = Path.Combine(repoRoot, ".tianshu", "tianshu.toml");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(Path.GetDirectoryName(projectConfigPath)!);
            await File.WriteAllTextAsync(
                userConfigPath,
                $$"""
[projects."{{repoRoot.Replace("\\", "/")}}"]
trust_level = "trusted"
""");
            await File.WriteAllTextAsync(projectConfigPath, "review_model = \"o3\"\n");

            var input = """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"key":"review_model","value":"gpt-5","cwd":"__CWD__"}}"""
                .Replace("__CWD__", workspace.Replace("\\", "/"), StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var result = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal("okOverridden", result.GetProperty("status").GetString());
                var metadata = result.GetProperty("overriddenMetadata");
                Assert.Contains("Overridden by project config", metadata.GetProperty("message").GetString(), StringComparison.Ordinal);
                Assert.Equal("o3", metadata.GetProperty("effectiveValue").GetString());
                Assert.Equal("project", metadata.GetProperty("overridingLayer").GetProperty("name").GetProperty("type").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
            Assert.True(File.Exists(userConfigPath));
            var userConfig = await File.ReadAllTextAsync(userConfigPath, CancellationToken.None);
            Assert.Contains("review_model = \"gpt-5\"", userConfig, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigValueWrite_ShouldApplyUpsertMergeStrategyToExistingObject()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                userConfigPath,
                """
[features]
existing = true
""");

            var input = """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"key":"features","value":{"added":true},"mergeStrategy":"upsert"}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var saved = Toml.ToModel(await File.ReadAllTextAsync(userConfigPath, CancellationToken.None)) as TomlTable;
            Assert.NotNull(saved);
            var features = Assert.IsType<TomlTable>(saved!["features"]);
            Assert.Equal(true, features["existing"]);
            Assert.Equal(true, features["added"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigBatchWrite_ShouldReloadLoadedThreadsUserConfig_WhenRequested()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        const string threadId = "00000000-0000-7000-8000-000000000201";

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(userConfigPath, "model = \"gpt-5\"\n");

            var setupStore = await CreateMaterializedThreadWithTurnsAsync(storePath, threadId, workspace);
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"thread/resume","params":{"threadId":"00000000-0000-7000-8000-000000000201"}}""",
                """{"jsonrpc":"2.0","id":2,"method":"config/batchWrite","params":{"items":[{"key":"model_route_set","value":"beta"}],"reloadUserConfig":false}}""",
                """{"jsonrpc":"2.0","id":3,"method":"thread/resume","params":{"threadId":"00000000-0000-7000-8000-000000000201"}}""",
                """{"jsonrpc":"2.0","id":4,"method":"config/batchWrite","params":{"items":[{"key":"model","value":"gpt-5-mini"},{"key":"model_route_set","value":"beta"}],"reloadUserConfig":true}}""",
                """{"jsonrpc":"2.0","id":5,"method":"thread/resume","params":{"threadId":"00000000-0000-7000-8000-000000000201"}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var resumed = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal("gpt-5", resumed.GetProperty("model").GetString());
                var resumedConfiguration = resumed.GetProperty("thread").GetProperty("sessionConfiguration");
                var resumedCatalog = resumedConfiguration.TryGetProperty("modelRouteSetId", out var resumedCatalogProperty)
                    ? resumedCatalogProperty.GetString()
                    : null;
                Assert.NotEqual("beta", resumedCatalog);
                var resumedSource = resumed.GetProperty("thread").GetProperty("source").GetString();
                Assert.False(string.IsNullOrWhiteSpace(resumedSource));

                var writeResult = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("result");
                Assert.Contains(writeResult.GetProperty("status").GetString(), new[] { "ok", "okOverridden" });

                var unchanged = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement.GetProperty("result");
                Assert.Equal("gpt-5", unchanged.GetProperty("model").GetString());
                var unchangedConfiguration = unchanged.GetProperty("thread").GetProperty("sessionConfiguration");
                var unchangedCatalog = unchangedConfiguration.TryGetProperty("modelRouteSetId", out var unchangedCatalogProperty)
                    ? unchangedCatalogProperty.GetString()
                    : null;
                Assert.NotEqual("beta", unchangedCatalog);

                var reloadWriteResult = messages.Single(x => IsResponseId(x.RootElement, 4)).RootElement.GetProperty("result");
                Assert.Contains(reloadWriteResult.GetProperty("status").GetString(), new[] { "ok", "okOverridden" });

                var reloaded = messages.Single(x => IsResponseId(x.RootElement, 5)).RootElement.GetProperty("result");
                Assert.Equal("gpt-5-mini", reloaded.GetProperty("model").GetString());
                Assert.Equal(
                    "beta",
                    reloaded.GetProperty("thread").GetProperty("sessionConfiguration").GetProperty("modelRouteSetId").GetString());
                Assert.Equal(resumedSource, reloaded.GetProperty("thread").GetProperty("source").GetString());
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "mcpServerStatus/list/updated"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var saved = await File.ReadAllTextAsync(userConfigPath, CancellationToken.None);
            Assert.Contains("model = \"gpt-5-mini\"", saved, StringComparison.Ordinal);
            Assert.Contains("model_route_set = \"beta\"", saved, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ResolveSkillMcpDependenciesAsync_ShouldWriteUserConfigAndReloadMcpServers()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");
        var storePath = Path.Combine(root, "threads.json");
        var kernelHome = Path.Combine(root, "kernel-home");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var projectConfigPath = Path.Combine(workspace, ".tianshu", "tianshu.toml");
        var originalCurrentDirectory = Environment.CurrentDirectory;
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(tianShuHome);
            Environment.CurrentDirectory = workspace;
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", kernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            await File.WriteAllTextAsync(userConfigPath, "model = \"gpt-5\"\n");

            var threadStore = new KernelThreadStore(storePath);
            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");

            var state = CreateTurnOperationState(
                "00000000-0000-7000-8000-000000000221",
                "turn_skill_mcp_dependency_001",
                "item_skill_mcp_dependency_001",
                "reason_skill_mcp_dependency_001",
                "install skill mcp dependency");
            var context = new TurnRequestContext(
                Model: null,
                ModelProvider: null,
                ServiceTier: null,
                ApprovalPolicy: null,
                SandboxPolicy: null,
                SandboxMode: null,
                Cwd: workspace);
            var skills = new[]
            {
                new KernelSkillDescriptor(
                    Name: "demo-skill",
                    Description: "Demo skill",
                    ShortDescription: "Demo skill",
                    Interface: null,
                    Dependencies: new KernelSkillDependencies(new[]
                    {
                        new KernelSkillToolDependency(
                            Type: "mcp",
                            Value: "demo",
                            Description: "Demo MCP dependency",
                            Transport: "stdio",
                            Command: "demo-mcp",
                            Url: null),
                    }),
                    PermissionProfile: null,
                    ManagedNetworkOverride: null,
                    PathToSkillsMd: Path.Combine(root, "skills", "demo", "SKILL.md"),
                    Path: Path.Combine(root, "skills", "demo"),
                    Scope: "user",
                    Enabled: true),
            };

            var method = typeof(AppHostServer).GetMethod("ResolveSkillMcpDependenciesAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var invocation = method!.Invoke(server, new object[] { state, context, skills, CancellationToken.None });
            var task = Assert.IsAssignableFrom<Task>(invocation);

            await WaitForWriterContainsAsync(writer, "\"method\":\"item/tool/requestUserInput\"", TimeSpan.FromSeconds(5));
            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(5));
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                answers = new Dictionary<string, object?>
                {
                    ["demo"] = new
                    {
                        answers = new[] { "立即安装 (Recommended)" },
                    },
                },
            }));

            await task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForWriterContainsAsync(writer, "\"method\":\"mcpServerStatus/list/updated\"", TimeSpan.FromSeconds(5));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var request = Assert.Single(messages.Where(static x => IsRequestMethod(x.RootElement, "item/tool/requestUserInput")));
                var question = request.RootElement
                    .GetProperty("params")
                    .GetProperty("questions")[0]
                    .GetProperty("question")
                    .GetString();
                Assert.NotNull(question);
                Assert.Contains(userConfigPath, question!, StringComparison.Ordinal);
                Assert.DoesNotContain(".tianshu/tianshu.toml", question!, StringComparison.Ordinal);

                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "serverRequest/resolved"));
                Assert.Contains(
                    messages,
                    x => IsNotificationMethod(x.RootElement, "mcpServerStatus/list/updated")
                         && x.RootElement.TryGetProperty("params", out var @params)
                         && @params.TryGetProperty("data", out var data)
                         && data.ValueKind == JsonValueKind.Array
                         && data.EnumerateArray().Any(item =>
                             item.TryGetProperty("name", out var name)
                             && string.Equals(name.GetString(), "demo", StringComparison.OrdinalIgnoreCase)));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            Assert.True(File.Exists(userConfigPath));
            Assert.False(File.Exists(projectConfigPath));

            var saved = Toml.ToModel(await File.ReadAllTextAsync(userConfigPath, CancellationToken.None)) as TomlTable;
            Assert.NotNull(saved);
            var mcpServers = Assert.IsType<TomlTable>(saved!["mcp_servers"]);
            var demo = Assert.IsType<TomlTable>(mcpServers["demo"]);
            Assert.Equal(true, demo["enabled"]);
                Assert.Equal("demo-mcp", demo["command"]?.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigBatchWrite_ShouldNormalizeLegacyMissingSessionSource_WhenReloadingLoadedThread()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        const string threadId = "00000000-0000-7000-8000-000000000220";

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(userConfigPath, "model = \"gpt-5\"\n");

            var setupStore = await CreateMaterializedThreadWithTurnsAsync(storePath, threadId, workspace);
            var created = await setupStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(created);
            var snapshot = KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder
                    .FromRecord(created!, "gpt-5", "openai", "on-request")
                    .Build());
            created!.ConfigSnapshot = snapshot with
            {
                SessionSource = null!,
            };
            _ = await setupStore.UpsertThreadAsync(created, CancellationToken.None);
            await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"thread/resume","params":{"threadId":"00000000-0000-7000-8000-000000000220"}}""",
                """{"jsonrpc":"2.0","id":2,"method":"config/batchWrite","params":{"items":[{"key":"model","value":"gpt-5-mini"}],"reloadUserConfig":true}}""",
                """{"jsonrpc":"2.0","id":3,"method":"thread/resume","params":{"threadId":"00000000-0000-7000-8000-000000000220"}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var documents = messages.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var initialResume = documents.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal("vscode", initialResume.GetProperty("thread").GetProperty("source").GetString());

                var reloadedResume = documents.Single(x => IsResponseId(x.RootElement, 3)).RootElement.GetProperty("result");
                Assert.Equal("vscode", reloadedResume.GetProperty("thread").GetProperty("source").GetString());
            }
            finally
            {
                foreach (var document in documents)
                {
                    document.Dispose();
                }
            }

            var persistedStore = new KernelThreadStore(storePath);
            await persistedStore.InitializeAsync(CancellationToken.None);
            var persistedThread = await persistedStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(persistedThread);
            Assert.NotNull(persistedThread!.ConfigSnapshot);
            Assert.Equal(KernelSessionSource.VsCode, persistedThread.ConfigSnapshot!.SessionSource);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ThreadStart_ShouldUseModelInstructionsFile_AsBaseInstructions()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var instructionsDirectory = Path.Combine(tianShuHome, "instructions");
        var instructionsPath = Path.Combine(instructionsDirectory, "base.md");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(instructionsDirectory);
            Directory.CreateDirectory(Path.Combine(workspace, ".git"));
            await File.WriteAllTextAsync(instructionsPath, "来自用户层说明文件");
            await File.WriteAllTextAsync(Path.Combine(tianShuHome, "AGENTS.md"), "home agents");
            await File.WriteAllTextAsync(Path.Combine(workspace, "AGENTS.md"), "workspace agents");
            await File.WriteAllTextAsync(
                userConfigPath,
                """
                model = "gpt-5"
                model_instructions_file = "instructions/base.md"
                """);

            var input = """{"jsonrpc":"2.0","id":1,"method":"thread/start","params":{"cwd":"__CWD__"}}"""
                .Replace("__CWD__", workspace.Replace("\\", "/"), StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var result = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal("gpt-5", result.GetProperty("model").GetString());

                var threadId = result.GetProperty("thread").GetProperty("id").GetString();
                Assert.False(string.IsNullOrWhiteSpace(threadId));

                var record = await threadStore.GetThreadAsync(threadId!, CancellationToken.None);
                Assert.NotNull(record);
                Assert.NotNull(record!.ConfigSnapshot);
                Assert.Equal("gpt-5", record.ConfigSnapshot!.Model);
                Assert.Equal("来自用户层说明文件", record.ConfigSnapshot.BaseInstructions);
                Assert.Contains("home agents", record.ConfigSnapshot.UserInstructions, StringComparison.Ordinal);
                Assert.Contains("workspace agents", record.ConfigSnapshot.UserInstructions, StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ThreadStart_ShouldEmitDeprecationNotice_ForExperimentalInstructionsFile()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                userConfigPath,
                """
                experimental_instructions_file = "legacy.md"
                """);

            var input = """{"jsonrpc":"2.0","id":1,"method":"thread/start","params":{"cwd":"__CWD__"}}"""
                .Replace("__CWD__", workspace.Replace("\\", "/"), StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var notice = messages.Single(x => IsNotificationMethod(x.RootElement, "deprecationNotice")).RootElement.GetProperty("params");
                Assert.Equal("`experimental_instructions_file` is deprecated and ignored. Use `model_instructions_file` instead.", notice.GetProperty("summary").GetString());
                Assert.Equal("Move the setting to `model_instructions_file` in tianshu.toml (or under a profile) to load instructions from a file.", notice.GetProperty("details").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ProcessFileWatcherEventsAsync_ShouldBroadcastSkillsChangedWithoutPathsPayload()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var reader = new StringReader(string.Empty);
            var writer = new StringWriter();
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(reader, writer, threadStore);
            var channel = Channel.CreateUnbounded<KernelFileWatcherEvent>();
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var method = typeof(AppHostServer).GetMethod(
                "ProcessFileWatcherEventsAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var processingTask = (Task?)method!.Invoke(server, [channel.Reader, cancellationTokenSource.Token]);
            Assert.NotNull(processingTask);

            await channel.Writer.WriteAsync(
                new KernelFileWatcherEvent(
                    KernelFileWatcherEventKind.SkillsChanged,
                    [Path.Combine(root, ".tianshu", "skills", "demo-skill", "SKILL.md")]),
                cancellationTokenSource.Token);
            channel.Writer.Complete();

            await processingTask!;

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var notification = Assert.Single(messages.Where(static x => IsNotificationMethod(x.RootElement, "skills/changed")));
                var payload = notification.RootElement.GetProperty("params");
                Assert.Equal(JsonValueKind.Object, payload.ValueKind);
                Assert.Empty(payload.EnumerateObject());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldApplyCliConfigFileOverUserConfig()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var sessionConfigPath = Path.Combine(root, "session-config.toml");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(Path.Combine(tianShuHome, "tianshu.toml"), "model = \"gpt-5\"\n");
            await File.WriteAllTextAsync(sessionConfigPath, "model = \"gpt-5-mini\"\n");

            var input = """{"jsonrpc":"2.0","id":1,"method":"config/read","params":{"includeLayers":true}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, cliConfigFilePath: sessionConfigPath);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var configRead = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result");
                Assert.Equal("gpt-5-mini", configRead.GetProperty("config").GetProperty("model").GetString());

                var userLayer = configRead.GetProperty("layers")
                    .EnumerateArray()
                    .Single(layer => layer.GetProperty("name").GetProperty("type").GetString() == "user");
                Assert.Equal(
                    Path.GetFullPath(sessionConfigPath),
                    Path.GetFullPath(userLayer.GetProperty("name").GetProperty("file").GetString()!),
                    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                Assert.DoesNotContain(
                    configRead.GetProperty("layers").EnumerateArray(),
                    static layer => layer.GetProperty("name").GetProperty("type").GetString() == "sessionFlags");
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldApplyCliConfigOverridesOverCliConfigFileAndUserConfig()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var sessionConfigPath = Path.Combine(root, "session-config.toml");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(Path.Combine(tianShuHome, "tianshu.toml"), "model = \"gpt-5\"\n");
            await File.WriteAllTextAsync(sessionConfigPath, "model = \"gpt-5-mini\"\n");

            var input = """{"jsonrpc":"2.0","id":1,"method":"config/read","params":{"includeLayers":true}}""";

            var cliOverrides = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["model"] = "gpt-4.1",
            };

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, cliOverrides, sessionConfigPath);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var configRead = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result");
                Assert.Equal("gpt-4.1", configRead.GetProperty("config").GetProperty("model").GetString());

                var layers = configRead.GetProperty("layers").EnumerateArray().ToArray();
                var userLayer = Assert.Single(layers.Where(static layer => layer.GetProperty("name").GetProperty("type").GetString() == "user"));
                Assert.Equal(
                    Path.GetFullPath(sessionConfigPath),
                    Path.GetFullPath(userLayer.GetProperty("name").GetProperty("file").GetString()!),
                    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                Assert.Contains(layers, static layer => layer.GetProperty("name").GetProperty("type").GetString() == "sessionFlags");
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRebaseCliPathOverridesAgainstConfigReadCwd()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(Path.Combine(tianShuHome, "tianshu.toml"), "model = \"gpt-5\"\n");

            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "config/read",
                @params = new
                {
                    includeLayers = true,
                    cwd,
                },
            });
            var cliOverrides = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["log_dir"] = "run-logs",
                ["mcp_servers.docs.cwd"] = "servers/docs",
                ["skills.config"] = """json:[{"path":"skills/repo-skill/SKILL.md","enabled":false}]""",
            };

            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath), cliOverrides);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var configRead = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result");

                Assert.Equal(
                    Path.GetFullPath(Path.Combine(cwd, "run-logs")),
                    configRead.GetProperty("config").GetProperty("log_dir").GetString());
                Assert.Equal(
                    Path.GetFullPath(Path.Combine(cwd, "servers", "docs")),
                    configRead.GetProperty("config").GetProperty("mcp_servers").GetProperty("docs").GetProperty("cwd").GetString());
                var skillConfigs = configRead.GetProperty("config").GetProperty("skills").GetProperty("config").EnumerateArray().ToArray();
                var skillConfig = Assert.Single(skillConfigs);
                Assert.Equal(
                    Path.GetFullPath(Path.Combine(cwd, "skills", "repo-skill", "SKILL.md")),
                    skillConfig.GetProperty("path").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReadModelFromConfigTomlInConfigRead()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var kernelHome = Path.Combine(root, "kernel-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", kernelHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(kernelHome);

            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
model = "gpt-5.4"
provider = "openai-compatible"
""");

            var input = """{"jsonrpc":"2.0","id":1,"method":"config/read","params":{"includeLayers":true}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var configRead = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result");
                var config = configRead.GetProperty("config");
                Assert.Equal("gpt-5.4", config.GetProperty("model").GetString());
                Assert.Equal("openai-compatible", config.GetProperty("provider").GetString());

                var layers = configRead.GetProperty("layers").EnumerateArray().ToArray();
                Assert.DoesNotContain(
                    layers,
                    static layer => string.Equals(
                        layer.GetProperty("name").GetProperty("type").GetString(),
                        "sessionFlags",
                        StringComparison.Ordinal));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigRead_ShouldIncludeSystemAndUserLayersEvenWhenUserFileIsMissing()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);

            var input = """{"jsonrpc":"2.0","id":1,"method":"config/read","params":{"includeLayers":true}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var configRead = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result");
                var layers = configRead.GetProperty("layers").EnumerateArray().ToArray();
                Assert.Contains(layers, static layer =>
                    layer.GetProperty("name").GetProperty("type").GetString() == "system");

                var userLayer = layers.Single(layer =>
                    layer.GetProperty("name").GetProperty("type").GetString() == "user");
                Assert.Equal(
                    Path.Combine(tianShuHome, "tianshu.toml"),
                    userLayer.GetProperty("name").GetProperty("file").GetString(),
                    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                Assert.False(userLayer.GetProperty("config").EnumerateObject().Any());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigRead_ShouldIncludeEmptyProjectLayerWhenDotTianShuDirectoryHasNoConfig()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoRoot = Path.Combine(root, "repo");
        var cwd = Path.Combine(repoRoot, "src", "module");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".tianshu"));
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(Path.Combine(tianShuHome, "tianshu.toml"), "model = \"gpt-5\"\n");

            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "config/read",
                @params = new
                {
                    includeLayers = true,
                    cwd,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var configRead = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result");
                Assert.Equal("gpt-5", configRead.GetProperty("config").GetProperty("model").GetString());

                var projectLayer = configRead.GetProperty("layers")
                    .EnumerateArray()
                    .Single(layer => layer.GetProperty("name").GetProperty("type").GetString() == "project");
                Assert.False(projectLayer.GetProperty("config").EnumerateObject().Any());
                Assert.Equal(JsonValueKind.Null, projectLayer.GetProperty("disabledReason").ValueKind);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigRead_ShouldFailWhenUserConfigTomlIsMalformed()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(Path.Combine(tianShuHome, "tianshu.toml"), "model = [\n");

            var input = """{"jsonrpc":"2.0","id":1,"method":"config/read","params":{"includeLayers":true}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32603, error.GetProperty("code").GetInt32());
                Assert.Contains("failed to parse config file", error.GetProperty("message").GetString(), StringComparison.Ordinal);
                Assert.Contains("tianshu.toml", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigRead_ShouldFailWhenTrustedProjectConfigIsMalformed()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoRoot = Path.Combine(root, "repo");
        var cwd = Path.Combine(repoRoot, "src", "module");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            var repoRootTomlPath = ToTomlPath(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".tianshu"));
            Directory.CreateDirectory(cwd);

            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
default_permissions = "safe"

[permissions.safe.filesystem]
":project_roots" = "read"

[projects."{{repoRootTomlPath}}"]
trust_level = "trusted"
""");
            await File.WriteAllTextAsync(Path.Combine(repoRoot, ".tianshu", "tianshu.toml"), "model = [\n");

            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "config/read",
                @params = new
                {
                    includeLayers = true,
                    cwd,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32603, error.GetProperty("code").GetInt32());
                Assert.Contains("failed to parse config file", error.GetProperty("message").GetString(), StringComparison.Ordinal);
                Assert.Contains(".tianshu", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigRead_ShouldFailWhenProjectRootMarkersAreInvalid()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoRoot = Path.Combine(root, "repo");
        var cwd = Path.Combine(repoRoot, "src", "module");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(cwd);

            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
model = "gpt-5"
project_root_markers = "not-an-array"
""");

            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "config/read",
                @params = new
                {
                    includeLayers = true,
                    cwd,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32603, error.GetProperty("code").GetInt32());
                Assert.Contains(
                    "project_root_markers must be an array of strings",
                    error.GetProperty("message").GetString(),
                    StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigRead_ShouldKeepDisabledProjectLayerWhenUntrustedProjectConfigIsMalformed()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoRoot = Path.Combine(root, "repo");
        var cwd = Path.Combine(repoRoot, "src", "module");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".tianshu"));
            Directory.CreateDirectory(cwd);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
model = "gpt-5"

[projects."{{ToTomlPath(repoRoot)}}"]
trust_level = "untrusted"
""");
            await File.WriteAllTextAsync(Path.Combine(repoRoot, ".tianshu", "tianshu.toml"), "model = [\n");

            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "config/read",
                @params = new
                {
                    includeLayers = true,
                    cwd,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var configRead = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result");
                Assert.Equal("gpt-5", configRead.GetProperty("config").GetProperty("model").GetString());

                var projectLayer = configRead.GetProperty("layers")
                    .EnumerateArray()
                    .Single(layer => layer.GetProperty("name").GetProperty("type").GetString() == "project");
                Assert.False(projectLayer.GetProperty("config").EnumerateObject().Any());
                Assert.Contains(
                    "To load tianshu.toml",
                    projectLayer.GetProperty("disabledReason").GetString(),
                    StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigRead_ShouldExposeLegacyManagedConfigAsDisabledMigrationOnlyLayer()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var sessionConfigPath = Path.Combine(root, "session-config.toml");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(Path.Combine(tianShuHome, "tianshu.toml"), "model = \"gpt-5\"\n");
            await File.WriteAllTextAsync(sessionConfigPath, "model = \"gpt-5-mini\"\n");
            await File.WriteAllTextAsync(Path.Combine(tianShuHome, "managed_config.toml"), "model = \"o3\"\n");

            var input = """{"jsonrpc":"2.0","id":1,"method":"config/read","params":{"includeLayers":true}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, cliConfigFilePath: sessionConfigPath);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var configRead = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result");
                Assert.Equal("gpt-5-mini", configRead.GetProperty("config").GetProperty("model").GetString());

                var origin = configRead.GetProperty("origins").GetProperty("model").GetProperty("name");
                Assert.Equal("user", origin.GetProperty("type").GetString());

                var layers = configRead.GetProperty("layers").EnumerateArray().ToArray();
                var userLayer = Assert.Single(layers, static layer =>
                    layer.GetProperty("name").GetProperty("type").GetString() == "user");
                Assert.Equal(
                    Path.GetFullPath(sessionConfigPath),
                    Path.GetFullPath(userLayer.GetProperty("name").GetProperty("file").GetString()!),
                    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                Assert.DoesNotContain(layers, static layer =>
                    layer.GetProperty("name").GetProperty("type").GetString() == "sessionFlags");
                var legacyLayer = Assert.Single(layers, static layer =>
                    layer.GetProperty("name").GetProperty("type").GetString() == "legacyManagedConfigTomlFromFile");
                Assert.Contains(
                    "迁移诊断层",
                    legacyLayer.GetProperty("disabledReason").GetString(),
                    StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigRead_ShouldIgnoreLegacyManagedConfigWhenDetectingPersistentConfig()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var systemRoot = Path.Combine(root, "system-root");
        var workspace = Path.Combine(root, "workspace");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalSystemRoot = Environment.GetEnvironmentVariable("TIANSHU_SYSTEM_CONFIG_ROOT");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_SYSTEM_CONFIG_ROOT", systemRoot);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(systemRoot);
            Directory.CreateDirectory(workspace);
            await File.WriteAllTextAsync(Path.Combine(systemRoot, "managed_config.toml"), "model = \"o3\"\n");

            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "config/read",
                @params = new
                {
                    includeLayers = true,
                    cwd = workspace,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var configRead = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result");
                Assert.False(configRead.GetProperty("config").TryGetProperty("model", out _));
                Assert.False(configRead.GetProperty("origins").TryGetProperty("model", out _));

                var legacyLayer = Assert.Single(
                    configRead.GetProperty("layers").EnumerateArray(),
                    static layer => layer.GetProperty("name").GetProperty("type").GetString() == "legacyManagedConfigTomlFromFile");
                Assert.Contains(
                    "迁移诊断层",
                    legacyLayer.GetProperty("disabledReason").GetString(),
                    StringComparison.Ordinal);

                var warning = messages.Single(x =>
                    x.RootElement.TryGetProperty("method", out var method)
                    && method.GetString() == "configWarning");
                Assert.Contains(
                    "未检测到本地配置文件",
                    warning.RootElement.GetProperty("params").GetProperty("summary").GetString(),
                    StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_SYSTEM_CONFIG_ROOT", originalSystemRoot);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldUseConfigTomlModelWhenThreadStarts()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var kernelHome = Path.Combine(root, "kernel-home");
        var cwd = Path.Combine(root, "workspace");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", kernelHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(kernelHome);
            Directory.CreateDirectory(cwd);

            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
model = "gpt-5.4"
provider = "openai-compatible"
approval_policy = "never"
default_permissions = "safe"

[permissions.safe.filesystem]
":project_roots" = "read"

[projects."{{repoRootTomlPath}}"]
trust_level = "untrusted"
""");

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"thread/start","params":{"cwd":"__CWD__"}}"""
                    .Replace("__CWD__", cwd.Replace("\\", "/")));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponsePayload(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result");
                Assert.Equal("gpt-5.4", response.GetProperty("model").GetString());
                Assert.Equal("openai-compatible", response.GetProperty("modelProvider").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldWriteTomlOwnedModelKeysToConfigToml()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);

            var input = """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"key":"model","value":"gpt-5","cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/"));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var result = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result");
                Assert.Contains(result.GetProperty("status").GetString(), new[] { "ok", "okOverridden" });
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var configPath = Path.Combine(tianShuHome, "tianshu.toml");
            Assert.True(File.Exists(configPath));
            var config = Toml.ToModel(await File.ReadAllTextAsync(configPath, CancellationToken.None)) as TomlTable;
            Assert.NotNull(config);
            Assert.Equal("gpt-5", config!["model"]?.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReadPermissionsProfilesFromUserConfigToml()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
default_permissions = "trusted"

[permissions.trusted.filesystem]
":project_roots" = "write"
":tmpdir" = "read"

[permissions.trusted.network]
enabled = true
allowed_domains = ["example.com"]
""");

            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "config/read",
                @params = new
                {
                    includeLayers = true,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var configRead = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result");
                var config = configRead.GetProperty("config");
                Assert.Equal("trusted", config.GetProperty("default_permissions").GetString());

                var trusted = config.GetProperty("permissions").GetProperty("trusted");
                var filesystem = trusted.GetProperty("filesystem");
                Assert.Equal("write", filesystem.GetProperty(":project_roots").GetString());
                Assert.Equal("read", filesystem.GetProperty(":tmpdir").GetString());

                var network = trusted.GetProperty("network");
                Assert.True(network.GetProperty("enabled").GetBoolean());
                Assert.Equal("example.com", network.GetProperty("allowed_domains")[0].GetString());

                var layers = configRead.GetProperty("layers");
                Assert.Contains(layers.EnumerateArray(), static layer =>
                    layer.GetProperty("name").GetProperty("type").GetString() == "user");
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldApplyProjectTianShuConfigWhenConfigReadReceivesCwd()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoRoot = Path.Combine(root, "repo");
        var cwd = Path.Combine(repoRoot, "src", "module");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            var repoRootTomlPath = ToTomlPath(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(cwd);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".tianshu"));

            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
default_permissions = "safe"

[permissions.safe.filesystem]
":project_roots" = "read"

[projects."{{repoRootTomlPath}}"]
trust_level = "trusted"
""");

            await File.WriteAllTextAsync(
                Path.Combine(repoRoot, ".tianshu", "tianshu.toml"),
                """
default_permissions = "trusted"

[permissions.trusted.filesystem]
":project_roots" = "write"

[permissions.trusted.network]
enabled = true
""");

            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "config/read",
                @params = new
                {
                    includeLayers = true,
                    cwd,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var configRead = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result");
                var config = configRead.GetProperty("config");
                Assert.Equal("trusted", config.GetProperty("default_permissions").GetString());
                Assert.Equal("write", config.GetProperty("permissions").GetProperty("trusted").GetProperty("filesystem").GetProperty(":project_roots").GetString());
                Assert.True(config.GetProperty("permissions").GetProperty("trusted").GetProperty("network").GetProperty("enabled").GetBoolean());

                var origin = configRead.GetProperty("origins").GetProperty("default_permissions").GetProperty("name");
                Assert.Equal("project", origin.GetProperty("type").GetString());

                var layers = configRead.GetProperty("layers");
                Assert.Contains(layers.EnumerateArray(), static layer =>
                    layer.GetProperty("name").GetProperty("type").GetString() == "project");
                Assert.Contains(layers.EnumerateArray(), static layer =>
                    layer.GetProperty("name").GetProperty("type").GetString() == "user");
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldDisableProjectTianShuConfigWhenProjectIsExplicitlyUntrusted()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoRoot = Path.Combine(root, "repo");
        var cwd = Path.Combine(repoRoot, "src", "module");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            var repoRootTomlPath = ToTomlPath(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(cwd);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".tianshu"));

            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
default_permissions = "safe"

[permissions.safe.filesystem]
":project_roots" = "read"

[projects."{{repoRootTomlPath}}"]
trust_level = "untrusted"
""");

            await File.WriteAllTextAsync(
                Path.Combine(repoRoot, ".tianshu", "tianshu.toml"),
                """
default_permissions = "trusted"

[permissions.trusted.filesystem]
":project_roots" = "write"
""");

            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "config/read",
                @params = new
                {
                    includeLayers = true,
                    cwd,
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var configRead = messages.Single(x => IsResponseId(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result");
                var config = configRead.GetProperty("config");
                Assert.Equal("safe", config.GetProperty("default_permissions").GetString());
                Assert.Equal("read", config.GetProperty("permissions").GetProperty("safe").GetProperty("filesystem").GetProperty(":project_roots").GetString());

                var origin = configRead.GetProperty("origins").GetProperty("default_permissions").GetProperty("name");
                Assert.Equal("user", origin.GetProperty("type").GetString());

                var projectLayer = configRead.GetProperty("layers")
                    .EnumerateArray()
                    .Single(layer => layer.GetProperty("name").GetProperty("type").GetString() == "project");
                Assert.True(projectLayer.TryGetProperty("disabledReason", out var disabledReason));
                Assert.Equal(JsonValueKind.String, disabledReason.ValueKind);
                Assert.Contains("To load tianshu.toml", disabledReason.GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldApplyConfiguredPermissionsProfileWhenThreadStarts()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);

            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
approval_policy = "never"
default_permissions = "safe"

[permissions.safe.filesystem]
":project_roots" = "read"
""");

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"thread/start","params":{"cwd":"__CWD__"}}"""
                    .Replace("__CWD__", cwd.Replace("\\", "/")));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponsePayload(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal("never", response.GetProperty("approvalPolicy").GetString());
                Assert.Equal("readOnly", response.GetProperty("sandbox").GetProperty("type").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldApplyConfiguredPermissionsProfileWhenThreadResumesStoredThread()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        const string threadId = "00000000-0000-7000-8000-000000000202";

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);

            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
approval_policy = "never"
default_permissions = "safe"

[permissions.safe.filesystem]
":project_roots" = "read"
""");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            var record = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);
            var rolloutPath = setupStore.RolloutRecorder.GetRolloutPath(threadId);
            Directory.CreateDirectory(Path.GetDirectoryName(rolloutPath)!);
            await File.WriteAllTextAsync(
                rolloutPath,
                JsonSerializer.Serialize(new
                {
                    type = "session_meta",
                    threadId = record.Id,
                    cwd = record.Cwd,
                    createdAtUnixMs = record.CreatedAt.ToUnixTimeMilliseconds(),
                    updatedAtUnixMs = record.UpdatedAt.ToUnixTimeMilliseconds(),
                }) + Environment.NewLine,
                CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"thread/resume","params":{"threadId":"00000000-0000-7000-8000-000000000202"}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponsePayload(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal("never", response.GetProperty("approvalPolicy").GetString());
                Assert.Equal("readOnly", response.GetProperty("sandbox").GetProperty("type").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("thread/start", """{"cwd":"__CWD__"}""")]
    [InlineData("thread/resume", """{"threadId":"00000000-0000-7000-8000-000000000203"}""")]
    [InlineData("thread/fork", """{"threadId":"00000000-0000-7000-8000-000000000203"}""")]
    public async Task RunAsync_ShouldReturnStructuredConfigLoadError_WhenThreadOperationCannotLoadConfiguration(
        string method,
        string paramsJson)
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        const string threadId = "00000000-0000-7000-8000-000000000203";

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);

            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
default_permissions = "missing"

[permissions.safe.filesystem]
":project_roots" = "read"
""");
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "requirements.toml"),
                """
allowed_approval_policies = ["never"]

[feature_requirements]
tool_search = true
""");

            if (!string.Equals(method, "thread/start", StringComparison.Ordinal))
            {
                var setupStore = new KernelThreadStore(storePath);
                await setupStore.InitializeAsync(CancellationToken.None);
                _ = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);
                await MaterializeThreadRolloutAsync(setupStore, threadId);
            }

            var input = string.Join(
                Environment.NewLine,
                $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{paramsJson.Replace("__CWD__", cwd.Replace("\\", "/"))}}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.StartsWith(
                    "failed to load configuration: default_permissions refers to undefined profile `missing`",
                    error.GetProperty("message").GetString(),
                    StringComparison.Ordinal);

                var data = error.GetProperty("data");
                Assert.Equal("cloudRequirements", data.GetProperty("reason").GetString());
                Assert.Contains("undefined profile `missing`", data.GetProperty("detail").GetString(), StringComparison.Ordinal);

                var requirements = data.GetProperty("requirements");
                Assert.Equal("never", requirements.GetProperty("allowedApprovalPolicies")[0].GetString());
                Assert.True(requirements.GetProperty("featureRequirements").GetProperty("tool_search").GetBoolean());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldFailThreadStart_WhenRequiredMcpServerCannotInitialize()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);

            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [mcp_servers.required_broken]
                command = "tianshu-definitely-not-a-real-binary"
                required = true
                """);

            var input = $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/start"",""params"":{{""cwd"":""{cwd.Replace("\\", "/")}""}}}}";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement;
                var error = response.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.StartsWith(
                    "failed to load configuration: required MCP servers failed to initialize",
                    error.GetProperty("message").GetString(),
                    StringComparison.Ordinal);
                Assert.Contains("required_broken", error.GetProperty("message").GetString(), StringComparison.Ordinal);
                Assert.False(error.TryGetProperty("data", out _));
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "thread/started"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldFailThreadResume_WhenRequiredMcpServerCannotInitialize()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        const string threadId = "00000000-0000-7000-8000-000000000204";

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);

            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [mcp_servers.required_broken]
                command = "tianshu-definitely-not-a-real-binary"
                required = true
                """);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);
            await MaterializeThreadRolloutAsync(setupStore, threadId);

            var input = $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""thread/resume"",""params"":{{""threadId"":""{threadId}""}}}}";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement;
                var error = response.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.StartsWith(
                    "failed to load configuration: required MCP servers failed to initialize",
                    error.GetProperty("message").GetString(),
                    StringComparison.Ordinal);
                Assert.Contains("required_broken", error.GetProperty("message").GetString(), StringComparison.Ordinal);
                Assert.False(error.TryGetProperty("data", out _));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("thread/start", """{"cwd":"__CWD__","userInstructions":"unexpected"}""", false)]
    [InlineData("thread/start", """{"cwd":"__CWD__","sandboxPolicy":{"mode":"read-only"}}""", false)]
    [InlineData("thread/start", """{"cwd":"__CWD__","collaborationMode":{"mode":"plan"}}""", false)]
    [InlineData("thread/start", """{"cwd":"__CWD__","providerBaseUrl":"https://example.invalid/v1"}""", false)]
    [InlineData("thread/start", """{"cwd":"__CWD__","webSearchMode":"disabled"}""", false)]
    [InlineData("thread/resume", """{"threadId":"00000000-0000-7000-8000-000000000204","dynamicTools":[]}""", true)]
    [InlineData("thread/resume", """{"threadId":"00000000-0000-7000-8000-000000000204","providerApiKeyEnvironmentVariable":"ANTHROPIC_API_KEY"}""", true)]
    [InlineData("thread/resume", """{"threadId":"00000000-0000-7000-8000-000000000204","webSearchMode":"disabled"}""", true)]
    [InlineData("thread/fork", """{"threadId":"00000000-0000-7000-8000-000000000204","dynamicTools":[]}""", true)]
    [InlineData("thread/fork", """{"threadId":"00000000-0000-7000-8000-000000000204","personality":"pragmatic"}""", true)]
    [InlineData("thread/fork", """{"threadId":"00000000-0000-7000-8000-000000000204","providerWireApi":"responses"}""", true)]
    public async Task RunAsync_ShouldRejectUnexpectedThreadTransportFields(
        string method,
        string paramsJson,
        bool createThread)
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000204";

        try
        {
            if (createThread)
            {
                var setupStore = new KernelThreadStore(storePath);
                await setupStore.InitializeAsync(CancellationToken.None);
                _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            }

            var input = string.Join(
                Environment.NewLine,
                $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{paramsJson.Replace("__CWD__", root.Replace("\\", "/"))}}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                Assert.StartsWith($"{method} 参数无效：", error.GetProperty("message").GetString(), StringComparison.Ordinal);
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "thread/started"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("thread/resume")]
    [InlineData("thread/fork")]
    public async Task RunAsync_ShouldRejectThreadResumeAndForkWithoutThreadIdEvenWhenPathProvided(string method)
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var input = $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""{method}"",""params"":{{""path"":""D:/rollouts/demo.jsonl""}}}}";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                Assert.Contains("threadId", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("thread/start", """{"cwd":"__CWD__","personality":"balanced"}""", false)]
    [InlineData("thread/resume", """{"threadId":"00000000-0000-7000-8000-000000000205","personality":"balanced"}""", true)]
    [InlineData("turn/start", """{"threadId":"00000000-0000-7000-8000-000000000205","personality":"balanced","input":[{"text":"hello"}]}""", true)]
    public async Task RunAsync_ShouldRejectUnsupportedPersonalityValues(
        string method,
        string paramsJson,
        bool createThread)
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000205";

        try
        {
            if (createThread)
            {
                var setupStore = new KernelThreadStore(storePath);
                await setupStore.InitializeAsync(CancellationToken.None);
                _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            }

            var input = $@"{{""jsonrpc"":""2.0"",""id"":1,""method"":""{method}"",""params"":{paramsJson.Replace("__CWD__", root.Replace("\\", "/"), StringComparison.Ordinal)}}}";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                Assert.Contains("personality", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("""{"threadId":"thread_turn_transport_reject_001","input":[{"text":"hello"}],"ephemeral":true}""")]
    [InlineData("""{"threadId":"thread_turn_transport_reject_001","input":[{"text":"hello"}],"history":[{"role":"user","content":"older"}]}""")]
    [InlineData("""{"threadId":"thread_turn_transport_reject_001","input":[{"text":"hello"}],"text":"legacy top-level text"}""")]
    [InlineData("""{"threadId":"thread_turn_transport_reject_001","input":[{"text":"hello"}],"userInstructions":"unexpected"}""")]
    [InlineData("""{"threadId":"thread_turn_transport_reject_001","input":[{"text":"hello"}],"modelVerbosity":"high"}""")]
    [InlineData("""{"threadId":"thread_turn_transport_reject_001","input":[{"text":"hello"}],"sandbox":{"mode":"read-only"}}""")]
    public async Task RunAsync_ShouldRejectUnexpectedTurnStartTransportFields(string paramsJson)
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_turn_transport_reject_001";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = $$"""{"jsonrpc":"2.0","id":1,"method":"turn/start","params":{{paramsJson}}}""";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                Assert.StartsWith("turn/start 参数无效：", error.GetProperty("message").GetString(), StringComparison.Ordinal);
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "turn/started"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldApplyConfiguredPermissionsProfileToThreadlessCommandExec()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var outputFile = Path.Combine(cwd, "profile.txt");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);

            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
approval_policy = "never"
default_permissions = "trusted"

[permissions.trusted.filesystem]
":project_roots" = "write"
""");

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"command/exec","params":{"command":["cmd.exe","/c","echo kernel_profile > profile.txt"],"cwd":"__CWD__"}}"""
                    .Replace("__CWD__", cwd.Replace("\\", "/")));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponsePayload(x.RootElement, 1)).RootElement.GetProperty("result");
                var exitCode = response.GetProperty("exitCode").GetInt32();
                var stderr = response.GetProperty("stderr").GetString();
                var commandStarted = Assert.Single(messages.Where(static x =>
                    IsNotificationMethod(x.RootElement, "item/started")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "commandExecution"));
                var commandStartedItem = commandStarted.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("inProgress", commandStartedItem.GetProperty("status").GetString());
                Assert.Equal(Path.GetFullPath(cwd), Path.GetFullPath(commandStartedItem.GetProperty("cwd").GetString()!.Replace("/", Path.DirectorySeparatorChar.ToString())));
                Assert.Contains("cmd.exe", commandStartedItem.GetProperty("command").GetString(), StringComparison.OrdinalIgnoreCase);

                var commandCompleted = Assert.Single(messages.Where(static x =>
                    IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "commandExecution"));
                var commandCompletedItem = commandCompleted.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("completed", commandCompletedItem.GetProperty("status").GetString());
                Assert.Equal(Path.GetFullPath(cwd), Path.GetFullPath(commandCompletedItem.GetProperty("cwd").GetString()!.Replace("/", Path.DirectorySeparatorChar.ToString())));
                Assert.Equal(0, commandCompletedItem.GetProperty("exitCode").GetInt32());

                Assert.True(exitCode == 0, $"stderr: {stderr}");
                Assert.True(File.Exists(outputFile));
                Assert.Contains("kernel_profile", await File.ReadAllTextAsync(outputFile), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldDefaultApprovalPolicyToUntrustedWhenProjectIsUntrusted()
    {
        var root = CreateTempDirectory();
        var repoRoot = Path.Combine(root, "repo");
        var cwd = Path.Combine(repoRoot, "src", "module");
        var repoRootTomlPath = ToTomlPath(repoRoot);

        try
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            var settings = ResolveConfiguredPermissionSettingsForTest(
                userConfigToml: $$"""
[projects."{{repoRootTomlPath}}"]
trust_level = "untrusted"
""",
                cwd: cwd,
                out _);

            var approvalPolicy = GetResolvedPermissionApprovalPolicy(settings);

            Assert.Equal(KernelApprovalPolicy.Untrusted, approvalPolicy);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldNotOverrideExplicitApprovalPolicyWhenProjectIsUntrusted()
    {
        var root = CreateTempDirectory();
        var repoRoot = Path.Combine(root, "repo");
        var cwd = Path.Combine(repoRoot, "src", "module");
        var repoRootTomlPath = ToTomlPath(repoRoot);

        try
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            var settings = ResolveConfiguredPermissionSettingsForTest(
                userConfigToml: $$"""
approval_policy = "never"

[projects."{{repoRootTomlPath}}"]
trust_level = "untrusted"
""",
                cwd: cwd,
                out _);

            var approvalPolicy = GetResolvedPermissionApprovalPolicy(settings);

            Assert.Equal(KernelApprovalPolicy.Never, approvalPolicy);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldNotOverrideExplicitProfileApprovalPolicyWhenProjectIsUntrusted()
    {
        var root = CreateTempDirectory();
        var repoRoot = Path.Combine(root, "repo");
        var cwd = Path.Combine(repoRoot, "src", "module");
        var repoRootTomlPath = ToTomlPath(repoRoot);

        try
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            var settings = ResolveConfiguredPermissionSettingsForTest(
                userConfigToml: $$"""
profile = "work"

[profiles.work]
approval_policy = "on-request"

[projects."{{repoRootTomlPath}}"]
trust_level = "untrusted"
""",
                cwd: cwd,
                out _);

            var approvalPolicy = GetResolvedPermissionApprovalPolicy(settings);

            Assert.Equal(KernelApprovalPolicy.OnRequest, approvalPolicy);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldRejectProfilesWithoutDefaultPermissions()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");

        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                ResolveConfiguredPermissionSettingsForTest(
                    userConfigToml: """
[permissions.workspace.filesystem]
":project_roots" = "read"
""",
                    cwd: workspace,
                    out _));

            Assert.Equal("config defines `[permissions]` profiles but does not set `default_permissions`", error.Message);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldReadLegacyCamelCasePermissionKeys()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");

        try
        {
            var settings = ResolveConfiguredPermissionSettingsForTest(
                userConfigToml: """
approvalPolicy = "never"
allowLoginShell = false
defaultPermissions = "safe"

[shellEnvironmentPolicy]
ignoreDefaultExcludes = false
includeOnly = ["PATH"]
experimentalUseProfile = true

[permissions.safe.filesystem]
":project_roots" = "read"
""",
                cwd: workspace,
                out _);

            Assert.Equal(KernelApprovalPolicy.Never, GetResolvedPermissionApprovalPolicy(settings));
            Assert.False(GetResolvedPermissionAllowLoginShell(settings));

            var shellEnvironmentPolicy = GetResolvedPermissionShellEnvironmentPolicy(settings);
            Assert.False(shellEnvironmentPolicy.IgnoreDefaultExcludes);
            Assert.Equal(["PATH"], shellEnvironmentPolicy.IncludeOnlyPatterns);
            Assert.True(shellEnvironmentPolicy.UseProfile);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldRejectDefaultPermissionsWithoutPermissionsTable()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");

        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                ResolveConfiguredPermissionSettingsForTest(
                    userConfigToml: """
default_permissions = "workspace"
""",
                    cwd: workspace,
                    out _));

            Assert.Equal("default_permissions requires a `[permissions]` table", error.Message);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldRejectNestedEntriesOutsideProjectRoots()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");

        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                ResolveConfiguredPermissionSettingsForTest(
                    userConfigToml: """
default_permissions = "workspace"

[permissions.workspace.filesystem.":minimal"]
"docs" = "read"
""",
                    cwd: workspace,
                    out _));

            Assert.Equal("filesystem path `:minimal` does not support nested entries", error.Message);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldRejectWritesOutsideWorkspaceRoot()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");
        var externalWritePath = ToTomlPath(Path.Combine(root, "outside"));

        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                ResolveConfiguredPermissionSettingsForTest(
                    userConfigToml: $"""
default_permissions = "workspace"

[permissions.workspace.filesystem]
"{externalWritePath}" = "write"
""",
                    cwd: workspace,
                    out _));

            Assert.Contains("filesystem writes outside the workspace root", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldMapRootWriteWithoutNetworkToExternalSandbox()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");

        try
        {
            var settings = ResolveConfiguredPermissionSettingsForTest(
                userConfigToml: """
default_permissions = "trusted"

[permissions.trusted.filesystem]
":root" = "write"
""",
                cwd: workspace,
                out _);
            var sandboxPolicy = GetResolvedPermissionSandboxPolicy(settings);
            var sandboxMode = GetResolvedPermissionSandboxMode(settings);

            Assert.Equal("externalSandbox", sandboxPolicy.GetProperty("type").GetString());
            Assert.Equal("restricted", sandboxPolicy.GetProperty("networkAccess").GetString());
            Assert.Equal("externalSandbox", sandboxMode);
            Assert.True(KernelSandboxEnforcer.EnsureWritePathAllowed(Path.Combine(root, "outside.txt"), workspace, sandboxPolicy, sandboxMode).Allowed);
            Assert.False(KernelSandboxEnforcer.EnsureNetworkCommandAllowed(["curl"], "curl https://example.com", sandboxPolicy, sandboxMode).Allowed);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldIgnoreUnknownSpecialPaths()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");

        try
        {
            var settings = ResolveConfiguredPermissionSettingsForTest(
                userConfigToml: """
default_permissions = "workspace"

[permissions.workspace.filesystem]
":future_special_path" = "write"
""",
                cwd: workspace,
                out _);
            var sandboxPolicy = GetResolvedPermissionSandboxPolicy(settings);

            Assert.Equal("readOnly", sandboxPolicy.GetProperty("type").GetString());
            Assert.Empty(sandboxPolicy.GetProperty("readOnlyAccess").GetProperty("readableRoots").EnumerateArray());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldRejectExplicitDenyEntries()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");
        var docsPath = ToTomlPath(Path.Combine(workspace, "docs"));

        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                ResolveConfiguredPermissionSettingsForTest(
                    userConfigToml: $"""
default_permissions = "workspace"

[permissions.workspace.filesystem]
":project_roots" = "write"
"{docsPath}" = "none"
""",
                    cwd: workspace,
                    out _));

            Assert.Contains("deny entries", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldRejectRootWriteNarrowingEntries()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");

        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                ResolveConfiguredPermissionSettingsForTest(
                    userConfigToml: """
default_permissions = "trusted"

[permissions.trusted.filesystem]
":root" = "write"
":project_roots" = "read"
""",
                    cwd: workspace,
                    out _));

            Assert.Contains("narrows `:root = write`", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldSupportCurrentWorkingDirectorySpecialPath()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");

        try
        {
            var settings = ResolveConfiguredPermissionSettingsForTest(
                userConfigToml: """
default_permissions = "workspace"

[permissions.workspace.filesystem]
":current_working_directory" = "write"
""",
                cwd: workspace,
                out var tianShuHome);
            var sandboxPolicy = GetResolvedPermissionSandboxPolicy(settings);
            var writableRoots = sandboxPolicy
                .GetProperty("writableRoots")
                .EnumerateArray()
                .Select(static item => item.GetString())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => Path.GetFullPath(item!))
                .ToArray();

            Assert.Equal("workspaceWrite", sandboxPolicy.GetProperty("type").GetString());
            Assert.Contains(Path.GetFullPath(workspace), writableRoots, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            Assert.Contains(Path.GetFullPath(Path.Combine(tianShuHome, "data", "memory")), writableRoots, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldMapSlashTmpWriteToWorkspacePolicy()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");

        try
        {
            var settings = ResolveConfiguredPermissionSettingsForTest(
                userConfigToml: """
default_permissions = "workspace"

[permissions.workspace.filesystem]
":project_roots" = "write"
":slash_tmp" = "write"
""",
                cwd: workspace,
                out _);
            var sandboxPolicy = GetResolvedPermissionSandboxPolicy(settings);

            Assert.Equal("workspaceWrite", sandboxPolicy.GetProperty("type").GetString());
            Assert.False(sandboxPolicy.GetProperty("excludeSlashTmp").GetBoolean());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldNotGrantTmpdirReadWhenTmpdirIsUnset()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");
        var originalTmpDir = Environment.GetEnvironmentVariable("TMPDIR");

        try
        {
            Environment.SetEnvironmentVariable("TMPDIR", null);
            var settings = ResolveConfiguredPermissionSettingsForTest(
                userConfigToml: """
default_permissions = "workspace"

[permissions.workspace.filesystem]
":tmpdir" = "read"
""",
                cwd: workspace,
                out _);
            var sandboxPolicy = GetResolvedPermissionSandboxPolicy(settings);
            var readableRoots = sandboxPolicy
                .GetProperty("readOnlyAccess")
                .GetProperty("readableRoots")
                .EnumerateArray()
                .ToArray();

            Assert.Equal("readOnly", sandboxPolicy.GetProperty("type").GetString());
            Assert.Empty(readableRoots);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMPDIR", originalTmpDir);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldNormalizeWindowsVerbatimPaths()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");
        var externalPath = Path.Combine(root, "docs");
        Directory.CreateDirectory(externalPath);
        var verbatimPath = @"\\?\" + Path.GetFullPath(externalPath);

        try
        {
            var settings = ResolveConfiguredPermissionSettingsForTest(
                userConfigToml: $"""
default_permissions = "workspace"

[permissions.workspace.filesystem]
"{ToTomlPath(verbatimPath)}" = "read"
""",
                cwd: workspace,
                out _);
            var sandboxPolicy = GetResolvedPermissionSandboxPolicy(settings);
            var readableRoots = sandboxPolicy
                .GetProperty("readOnlyAccess")
                .GetProperty("readableRoots")
                .EnumerateArray()
                .Select(static item => item.GetString())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => Path.GetFullPath(item!))
                .ToArray();

            Assert.Contains(Path.GetFullPath(externalPath), readableRoots, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredPermissionSettings_ShouldAppendMemoriesWritableRootForWorkspaceProfile()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");

        try
        {
            var settings = ResolveConfiguredPermissionSettingsForTest(
                userConfigToml: """
default_permissions = "trusted"

[permissions.trusted.filesystem]
":project_roots" = "write"
""",
                cwd: workspace,
                out var tianShuHome);
            var sandboxPolicy = GetResolvedPermissionSandboxPolicy(settings);
            var expectedMemoriesRoot = Path.GetFullPath(Path.Combine(tianShuHome, "data", "memory"));
            var expectedWorkspaceRoot = Path.GetFullPath(workspace);
            var writableRoots = sandboxPolicy
                .GetProperty("writableRoots")
                .EnumerateArray()
                .Select(static item => item.GetString())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => Path.GetFullPath(item!))
                .ToArray();

            Assert.Equal("workspaceWrite", sandboxPolicy.GetProperty("type").GetString());
            Assert.Contains(expectedMemoriesRoot, writableRoots, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            Assert.Contains(expectedWorkspaceRoot, writableRoots, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task RunAsync_ShouldRejectStringCommandLoginShellWhenDisabledByConfig()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);

            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
approval_policy = "never"
allow_login_shell = false
default_permissions = "trusted"

[permissions.trusted.filesystem]
":project_roots" = "write"
""");

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"command/exec","params":{"command":"Write-Output denied","login":true,"cwd":"__CWD__"}}"""
                    .Replace("__CWD__", cwd.Replace("\\", "/"), StringComparison.Ordinal));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Contains("login shell is disabled by config", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldApplyShellEnvironmentPolicyToStringCommandExec()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalTestEnv = Environment.GetEnvironmentVariable("TIANSHU_TEST_ENV");
        const string threadId = "thread_command_env_001";

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_TEST_ENV", "host-value");
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);

            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
approval_policy = "never"
allow_login_shell = false
default_permissions = "trusted"

[shell_environment_policy]
inherit = "all"

[shell_environment_policy.set]
TIANSHU_TEST_ENV = "present"

[permissions.trusted.filesystem]
":project_roots" = "write"
""");

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"command/exec","params":{"threadId":"__THREAD__","command":"Write-Output $env:TIANSHU_TEST_ENV; Write-Output $env:TIANSHU_THREAD_ID","cwd":"__CWD__"}}"""
                    .Replace("__THREAD__", threadId, StringComparison.Ordinal)
                    .Replace("__CWD__", cwd.Replace("\\", "/"), StringComparison.Ordinal));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponsePayload(x.RootElement, 1)).RootElement.GetProperty("result");
                var stdout = response.GetProperty("stdout").GetString() ?? string.Empty;
                Assert.Equal(0, response.GetProperty("exitCode").GetInt32());
                Assert.Contains("present", stdout, StringComparison.Ordinal);
                Assert.Contains(threadId, stdout, StringComparison.Ordinal);
                Assert.DoesNotContain("host-value", stdout, StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_TEST_ENV", originalTestEnv);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReturnReviewTurnUserMessagePayloadForInlineReview()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_review_inline_001";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"review/start","params":{"threadId":"thread_review_inline_001","target":{"type":"custom","instructions":"  请审查这次改动的风险  "}}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Where(x => IsResponsePayload(x.RootElement, 1)).Last().RootElement.GetProperty("result");
                Assert.Equal(threadId, response.GetProperty("reviewThreadId").GetString());

                var turn = response.GetProperty("turn");
                Assert.Equal("inProgress", turn.GetProperty("status").GetString());
                var turnId = turn.GetProperty("id").GetString();
                Assert.False(string.IsNullOrWhiteSpace(turnId));

                var items = turn.GetProperty("items");
                Assert.Equal(JsonValueKind.Array, items.ValueKind);
                Assert.Equal(1, items.GetArrayLength());

                var userMessage = items[0];
                Assert.Equal("userMessage", userMessage.GetProperty("type").GetString());
                Assert.Equal(turnId, userMessage.GetProperty("id").GetString());

                var content = userMessage.GetProperty("content");
                Assert.Equal(JsonValueKind.Array, content.ValueKind);
                Assert.Equal(1, content.GetArrayLength());
                Assert.Equal("text", content[0].GetProperty("type").GetString());
                Assert.Equal("请审查这次改动的风险", content[0].GetProperty("text").GetString());
                Assert.Equal(0, content[0].GetProperty("textElements").GetArrayLength());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldUseDetachedReviewModelOverrideWithoutModelRerouteNotification()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        const string sourceThreadId = "thread_review_detached_source";

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", Path.Combine(root, "kernel-home"));

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(sourceThreadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"key":"review_model","value":"o3","cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":2,"method":"review/start","params":{"threadId":"thread_review_detached_source","delivery":"detached","target":{"type":"uncommittedChanges"}}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);
            await Task.Delay(400);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("result");
                var reviewThreadId = response.GetProperty("reviewThreadId").GetString();
                Assert.False(string.IsNullOrWhiteSpace(reviewThreadId));
                Assert.NotEqual(sourceThreadId, reviewThreadId);
                Assert.Equal("inProgress", response.GetProperty("turn").GetProperty("status").GetString());

                var reviewTurnItems = response.GetProperty("turn").GetProperty("items");
                Assert.Equal(1, reviewTurnItems.GetArrayLength());
                Assert.Equal("current changes", reviewTurnItems[0].GetProperty("content")[0].GetProperty("text").GetString());

                var startedNotification = messages.Single(x =>
                    IsNotificationMethod(x.RootElement, "thread/started")
                    && x.RootElement.TryGetProperty("params", out var @params)
                    && string.Equals(@params.GetProperty("thread").GetProperty("id").GetString(), reviewThreadId, StringComparison.Ordinal));
                Assert.Equal(
                    "active",
                    startedNotification.RootElement
                        .GetProperty("params")
                        .GetProperty("thread")
                        .GetProperty("status")
                        .GetProperty("type")
                        .GetString());

                Assert.DoesNotContain(messages, x =>
                    IsNotificationMethod(x.RootElement, "thread/status/changed")
                    && x.RootElement.TryGetProperty("params", out var @params)
                    && string.Equals(@params.GetProperty("threadId").GetString(), reviewThreadId, StringComparison.Ordinal)
                    && string.Equals(
                        @params.GetProperty("status").GetProperty("type").GetString(),
                        "active",
                        StringComparison.Ordinal));

                Assert.DoesNotContain(messages, x =>
                    IsNotificationMethod(x.RootElement, "model/rerouted")
                    && x.RootElement.TryGetProperty("params", out var @params)
                    && string.Equals(@params.GetProperty("threadId").GetString(), reviewThreadId, StringComparison.Ordinal));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldEmitReviewLifecycleItemsForReviewStart()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        const string threadId = "thread_review_lifecycle_001";

        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"review/start","params":{"threadId":"thread_review_lifecycle_001","target":{"type":"custom","instructions":"检查潜在缺陷"}}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);
            await Task.Delay(900);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var turnId = messages
                    .Single(x => IsResponsePayload(x.RootElement, 1))
                    .RootElement
                    .GetProperty("result")
                    .GetProperty("turn")
                    .GetProperty("id")
                    .GetString();
                Assert.False(string.IsNullOrWhiteSpace(turnId));

                Assert.Contains(
                    messages,
                    x =>
                        IsNotificationMethod(x.RootElement, "item/started")
                        && x.RootElement.TryGetProperty("params", out var @params)
                        && @params.TryGetProperty("item", out var item)
                        && string.Equals(item.GetProperty("type").GetString(), "enteredReviewMode", StringComparison.Ordinal)
                        && string.Equals(item.GetProperty("id").GetString(), turnId, StringComparison.Ordinal));

                Assert.Contains(
                    messages,
                    x =>
                        IsNotificationMethod(x.RootElement, "item/completed")
                        && x.RootElement.TryGetProperty("params", out var @params)
                        && @params.TryGetProperty("item", out var item)
                        && string.Equals(item.GetProperty("type").GetString(), "enteredReviewMode", StringComparison.Ordinal)
                        && string.Equals(item.GetProperty("id").GetString(), turnId, StringComparison.Ordinal));

                Assert.Contains(
                    messages,
                    x =>
                        IsNotificationMethod(x.RootElement, "item/started")
                        && x.RootElement.TryGetProperty("params", out var @params)
                        && @params.TryGetProperty("item", out var item)
                        && string.Equals(item.GetProperty("type").GetString(), "exitedReviewMode", StringComparison.Ordinal)
                        && string.Equals(item.GetProperty("id").GetString(), turnId, StringComparison.Ordinal));

                Assert.Contains(
                    messages,
                    x =>
                        IsNotificationMethod(x.RootElement, "item/completed")
                        && x.RootElement.TryGetProperty("params", out var @params)
                        && @params.TryGetProperty("item", out var item)
                        && string.Equals(item.GetProperty("type").GetString(), "exitedReviewMode", StringComparison.Ordinal)
                        && string.Equals(item.GetProperty("id").GetString(), turnId, StringComparison.Ordinal));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldUseTianShuCommitReviewDisplayText()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_review_commit_display_001";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"review/start","params":{"threadId":"thread_review_commit_display_001","target":{"type":"commit","sha":"1234567deadbeef","title":"Tidy UI colors"}}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Contains(
                    messages,
                    static x =>
                        IsNotificationMethod(x.RootElement, "item/started")
                        && x.RootElement.TryGetProperty("params", out var @params)
                        && @params.TryGetProperty("item", out var item)
                        && string.Equals(item.GetProperty("type").GetString(), "enteredReviewMode", StringComparison.Ordinal)
                        && string.Equals(item.GetProperty("review").GetString(), "commit 1234567: Tidy UI colors", StringComparison.Ordinal));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldEnrichReviewPromptWithUncommittedGitContext()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var filePath = Path.Combine(repoRoot, "demo.txt");
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        const string threadId = "thread_review_git_context_001";

        try
        {
            Directory.CreateDirectory(repoRoot);
            RunGitCommand(repoRoot, "init");
            RunGitCommand(repoRoot, "config", "user.email", "tianshu-tests@example.com");
            RunGitCommand(repoRoot, "config", "user.name", "TianShu Tests");

            await File.WriteAllTextAsync(filePath, "line-1\nline-2\n");
            RunGitCommand(repoRoot, "add", ".");
            RunGitCommand(repoRoot, "commit", "-m", "init");

            await File.WriteAllTextAsync(filePath, "line-1\nline-2-modified\n");

            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"review/start","params":{"threadId":"thread_review_git_context_001","target":{"type":"uncommittedChanges"}}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);
            await Task.Delay(900);

            var verifyStore = new KernelThreadStore(storePath);
            await verifyStore.InitializeAsync(CancellationToken.None);
            var thread = await verifyStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(thread);
            Assert.NotEmpty(thread!.Turns);

            var latestTurn = thread.Turns[^1];
            Assert.Contains("以下是自动采集的代码差异上下文", latestTurn.UserMessage, StringComparison.Ordinal);
            Assert.Contains("[unstaged]", latestTurn.UserMessage, StringComparison.Ordinal);
            Assert.Contains("line-2-modified", latestTurn.UserMessage, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void BuildReviewTurnRequestContext_ShouldReuseSessionApprovalPolicySandboxAndCwd()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var cwd = Path.Combine(root, "repo");
        const string threadId = "thread_review_context_001";
        var sandboxPolicy = JsonSerializer.SerializeToElement(new
        {
            type = "readOnly",
        });
        var dynamicTools = new[]
        {
            new KernelDynamicToolDescriptor(
                FullName: "server__demo_tool",
                ShortName: "demo_tool",
                Namespace: null,
                Description: "demo",
                Title: "Demo Tool",
                Server: "server",
                ConnectorName: null,
                ConnectorDescription: null,
                ConnectorId: null,
                InputSchema: null,
                OutputSchema: null,
                Meta: null,
                Annotations: null),
        };

        try
        {
            Directory.CreateDirectory(cwd);
            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                new KernelThreadStore(storePath));
            var session = new KernelThreadSessionState(
                Model: "gpt-5",
                ModelProvider: "openai",
                ServiceTier: KernelServiceTier.Flex,
                Cwd: cwd,
                ApprovalPolicy: KernelApprovalPolicy.Untrusted,
                SandboxPolicy: sandboxPolicy,
                SandboxMode: "read-only",
                AllowLoginShell: false,
                ShellEnvironmentPolicy: new KernelShellEnvironmentPolicy(KernelShellEnvironmentPolicyInherit.Core),
                DynamicTools: dynamicTools,
                ProviderBaseUrl: "https://example.test/v1",
                ProviderApiKeyEnvironmentVariable: "TEST_API_KEY",
                ProviderWireApi: "responses",
                ProviderRequestMaxRetries: 2,
                ProviderStreamMaxRetries: 3,
                ProviderStreamIdleTimeoutMs: 4000,
                ProviderSupportsWebsockets: false,
                WebSearchMode: "live",
                PersistExtendedHistory: true,
                WindowsSandboxLevel: KernelWindowsSandboxLevel.Unelevated,
                DefaultModeRequestUserInputEnabled: true);

            var method = typeof(AppHostServer).GetMethod(
                "BuildReviewTurnRequestContext",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(string), typeof(KernelThreadSessionState), typeof(string), typeof(string)],
                modifiers: null);
            Assert.NotNull(method);

            var context = method!.Invoke(server, [threadId, session, "o3", "Review current diff."]);
            Assert.NotNull(context);

            var contextType = context!.GetType();
            Assert.Equal("o3", contextType.GetProperty("Model")!.GetValue(context));
            Assert.Equal(KernelApprovalPolicy.Untrusted, contextType.GetProperty("ApprovalPolicy")!.GetValue(context));
            Assert.Equal("read-only", contextType.GetProperty("SandboxMode")!.GetValue(context));
            Assert.Equal(cwd, contextType.GetProperty("Cwd")!.GetValue(context));
            Assert.Equal(false, contextType.GetProperty("AllowLoginShell")!.GetValue(context));
            Assert.Equal(true, contextType.GetProperty("IsReview")!.GetValue(context));
            Assert.Equal("Review current diff.", contextType.GetProperty("ReviewDisplayText")!.GetValue(context));
            Assert.Equal(KernelWindowsSandboxLevel.Unelevated, contextType.GetProperty("WindowsSandboxLevel")!.GetValue(context));
            Assert.Null(contextType.GetProperty("DynamicTools")!.GetValue(context));

            var contextSandboxPolicy = Assert.IsType<JsonElement>(contextType.GetProperty("SandboxPolicy")!.GetValue(context)!);
            Assert.Equal("readOnly", contextSandboxPolicy.GetProperty("type").GetString());

            var shellEnvironmentPolicy = Assert.IsType<KernelShellEnvironmentPolicy>(contextType.GetProperty("ShellEnvironmentPolicy")!.GetValue(context)!);
            Assert.Equal(KernelShellEnvironmentPolicyInherit.Core, shellEnvironmentPolicy.Inherit);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectWindowsSandboxSetupStartWhenModeInvalid()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"windowsSandbox/setupStart","params":{"mode":"invalid"}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.Contains("invalid mode", error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "windowsSandbox/setupCompleted"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldNormalizeWindowsSandboxSetupModeCaseInsensitive()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"windowsSandbox/setupStart","params":{"mode":"ElEvAtEd"}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"windowsSandbox/setupCompleted\"", TimeSpan.FromSeconds(5));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Where(x => IsResponsePayload(x.RootElement, 1)).Last().RootElement.GetProperty("result");
                Assert.True(response.GetProperty("started").GetBoolean());

                var setupCompleted = messages.Single(x => IsNotificationMethod(x.RootElement, "windowsSandbox/setupCompleted"))
                    .RootElement
                    .GetProperty("params");
                Assert.Equal("elevated", setupCompleted.GetProperty("mode").GetString());
                Assert.True(setupCompleted.TryGetProperty("success", out _));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldNotEmitLegacyServerRequestAndShouldReportTurnFailure()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_server_request_001";
        using var openAiApiKeyScope = new EnvironmentVariableScope("OPENAI_API_KEY", null);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"turn/start","params":{"threadId":"thread_server_request_001","input":[{"text":"test"}]}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await Task.Delay(800);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var turnStart = messages.Single(x => IsResponsePayload(x.RootElement, 1));
                Assert.True(turnStart.RootElement.TryGetProperty("result", out _));

                Assert.DoesNotContain(messages, static x => IsRequestMethod(x.RootElement, "item/commandExecution/requestApproval"));
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "error"));
                Assert.Contains(messages, static x => IsTurnCompletedWithStatus(x.RootElement, "failed"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldResolvePendingApprovalRequestByCallId()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const long requestId = 42;
        const string callId = "approval-call-42";

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(
                KernelAppServerTestProtocol.WithInitialize(
                    """{"jsonrpc":"2.0","id":1,"method":"turn/approval/respond","params":{"callId":"approval-call-42","approved":true}}"""));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var idsByCall = GetPrivateField<ConcurrentDictionary<string, long>>(server, "approvalRequestIdsByCallId");
            var callsById = GetPrivateField<ConcurrentDictionary<long, string>>(server, "approvalCallIdsByRequestId");

            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(pending.TryAdd(requestId, tcs));
            idsByCall[callId] = requestId;
            callsById[requestId] = callId;

            await server.RunAsync(CancellationToken.None);

            var resolved = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("accept", resolved.GetProperty("decision").GetString());

            Assert.False(pending.ContainsKey(requestId));
            Assert.False(idsByCall.ContainsKey(callId));
            Assert.False(callsById.ContainsKey(requestId));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponseId(x.RootElement, 1));
                var result = response.RootElement.GetProperty("result");
                Assert.True(result.GetProperty("ok").GetBoolean());
                Assert.Equal(requestId, result.GetProperty("requestId").GetInt64());
                Assert.Equal(callId, result.GetProperty("callId").GetString());
                Assert.Equal("accept", result.GetProperty("decision").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldResolveManagedNetworkApprovalByApprovalId()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const long requestId = 77;
        const string approvalId = "network#https#example.com#443";

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(
                KernelAppServerTestProtocol.WithInitialize(
                    """{"jsonrpc":"2.0","id":1,"method":"turn/approval/respond","params":{"approvalId":"network#https#example.com#443","decision":{"type":"applyNetworkPolicyAmendment","networkPolicyAmendment":{"host":"example.com","action":"allow"}},"applyProposedExecPolicyAmendment":true,"reason":"remember it"}}"""));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var idsByCall = GetPrivateField<ConcurrentDictionary<string, long>>(server, "approvalRequestIdsByCallId");
            var callsById = GetPrivateField<ConcurrentDictionary<long, string>>(server, "approvalCallIdsByRequestId");

            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(pending.TryAdd(requestId, tcs));
            idsByCall[approvalId] = requestId;
            callsById[requestId] = approvalId;

            await server.RunAsync(CancellationToken.None);

            var resolved = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("applyNetworkPolicyAmendment", resolved.GetProperty("decision").GetString());
            var amendment = resolved.GetProperty("networkPolicyAmendment");
            Assert.Equal("example.com", amendment.GetProperty("host").GetString());
            Assert.Equal("allow", amendment.GetProperty("action").GetString());
            Assert.True(resolved.GetProperty("applyProposedExecPolicyAmendment").GetBoolean());
            Assert.Equal("remember it", resolved.GetProperty("reason").GetString());

            Assert.False(pending.ContainsKey(requestId));
            Assert.False(idsByCall.ContainsKey(approvalId));
            Assert.False(callsById.ContainsKey(requestId));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponseId(x.RootElement, 1));
                var result = response.RootElement.GetProperty("result");
                Assert.True(result.GetProperty("ok").GetBoolean());
                Assert.Equal(requestId, result.GetProperty("requestId").GetInt64());
                Assert.Equal(approvalId, result.GetProperty("callId").GetString());
                Assert.Equal("applyNetworkPolicyAmendment", result.GetProperty("decision").GetString());
                Assert.False(result.TryGetProperty("networkPolicyAmendment", out _));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task RequestManagedNetworkApprovalAsync_ShouldEmitNetworkOnlyApprovalPayloadAndTrackApprovalId()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_network_request_001";
        const string turnId = "turn_network_request_001";
        const string itemId = "item_network_request_001";
        const string approvalId = "network#https#example.com#443";

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(string.Empty);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            var request = new KernelManagedNetworkApprovalRequest(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                ApprovalId: approvalId,
                Command: "network-access https://example.com:443",
                Cwd: root,
                Reason: "example.com is not in the allowed_domains",
                NetworkApprovalContext: new KernelManagedNetworkApprovalContext("example.com", KernelManagedNetworkProtocol.Https),
                ProposedNetworkPolicyAmendments: new[]
                {
                    new KernelManagedNetworkPolicyAmendment("example.com", KernelManagedNetworkRuleAction.Allow),
                    new KernelManagedNetworkPolicyAmendment("example.com", KernelManagedNetworkRuleAction.Deny),
                },
                AvailableDecisions: new object?[]
                {
                    "accept",
                    "acceptForSession",
                    new
                    {
                        applyNetworkPolicyAmendment = new
                        {
                            network_policy_amendment = new
                            {
                                host = "example.com",
                                action = "allow",
                            },
                        },
                    },
                    "cancel",
                });

            var runtime = GetPrivateField<KernelManagedNetworkAppHostRuntime>(server, "managedNetworkRuntime");
            var requestTask = runtime.RequestManagedNetworkApprovalAsync(request, CancellationToken.None);
            await Task.Delay(120);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var requestMessage = Assert.Single(messages, static x => IsRequestMethod(x.RootElement, "item/commandExecution/requestApproval"));
                var requestId = requestMessage.RootElement.GetProperty("id").GetInt64();
                var payload = requestMessage.RootElement.GetProperty("params");

                Assert.Equal(threadId, payload.GetProperty("threadId").GetString());
                Assert.Equal(turnId, payload.GetProperty("turnId").GetString());
                Assert.Equal(itemId, payload.GetProperty("itemId").GetString());
                Assert.Equal(approvalId, payload.GetProperty("approvalId").GetString());
                Assert.Equal("example.com is not in the allowed_domains", payload.GetProperty("reason").GetString());
                Assert.False(payload.TryGetProperty("callId", out _));
                Assert.False(payload.TryGetProperty("command", out _));
                Assert.False(payload.TryGetProperty("cwd", out _));

                var networkApprovalContext = payload.GetProperty("networkApprovalContext");
                Assert.Equal("example.com", networkApprovalContext.GetProperty("host").GetString());
                Assert.Equal("https", networkApprovalContext.GetProperty("protocol").GetString());

                var amendments = payload.GetProperty("proposedNetworkPolicyAmendments");
                Assert.Equal(2, amendments.GetArrayLength());
                Assert.Equal("allow", amendments[0].GetProperty("action").GetString());
                Assert.Equal("deny", amendments[1].GetProperty("action").GetString());

                var decisions = payload.GetProperty("availableDecisions");
                Assert.Equal(4, decisions.GetArrayLength());
                Assert.Equal("example.com", decisions[2]
                    .GetProperty("applyNetworkPolicyAmendment")
                    .GetProperty("network_policy_amendment")
                    .GetProperty("host")
                    .GetString());

                var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
                Assert.True(pending.TryGetValue(requestId, out var tcs));

                var idsByCall = GetPrivateField<ConcurrentDictionary<string, long>>(server, "approvalRequestIdsByCallId");
                var callsById = GetPrivateField<ConcurrentDictionary<long, string>>(server, "approvalCallIdsByRequestId");
                Assert.True(idsByCall.TryGetValue(approvalId, out var mappedRequestId));
                Assert.Equal(requestId, mappedRequestId);
                Assert.Equal(approvalId, callsById[requestId]);

                tcs!.TrySetResult(JsonSerializer.SerializeToElement(new
                {
                    decision = new
                    {
                        applyNetworkPolicyAmendment = new
                        {
                            network_policy_amendment = new
                            {
                                host = "example.com",
                                action = "allow",
                            },
                        },
                    },
                    networkPolicyAmendment = new
                    {
                        host = "example.com",
                        action = "allow",
                    },
                }));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var result = await requestTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("applyNetworkPolicyAmendment", result.Decision);
            Assert.NotNull(result.NetworkPolicyAmendment);
            Assert.Equal("example.com", result.NetworkPolicyAmendment!.Host);
            Assert.Equal(KernelManagedNetworkRuleAction.Allow, result.NetworkPolicyAmendment.Action);

            var remainingIdsByCall = GetPrivateField<ConcurrentDictionary<string, long>>(server, "approvalRequestIdsByCallId");
            var remainingCallsById = GetPrivateField<ConcurrentDictionary<long, string>>(server, "approvalCallIdsByRequestId");
            Assert.False(remainingIdsByCall.ContainsKey(approvalId));
            Assert.DoesNotContain(remainingCallsById, static pair => pair.Value == approvalId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RequestManagedNetworkApprovalAsync_WhenNetworkAmendmentActionInvalid_ShouldDiscardAmendment()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_network_request_invalid_action";
        const string turnId = "turn_network_request_invalid_action";
        const string itemId = "item_network_request_invalid_action";
        const string approvalId = "network#https#example.com#443";

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(string.Empty);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            var request = new KernelManagedNetworkApprovalRequest(
                ThreadId: threadId,
                TurnId: turnId,
                ItemId: itemId,
                ApprovalId: approvalId,
                Command: "network-access https://example.com:443",
                Cwd: root,
                Reason: "example.com is not in the allowed_domains",
                NetworkApprovalContext: new KernelManagedNetworkApprovalContext("example.com", KernelManagedNetworkProtocol.Https),
                ProposedNetworkPolicyAmendments: new[]
                {
                    new KernelManagedNetworkPolicyAmendment("example.com", KernelManagedNetworkRuleAction.Allow),
                },
                AvailableDecisions: new object?[] { "accept", "cancel" });

            var runtime = GetPrivateField<KernelManagedNetworkAppHostRuntime>(server, "managedNetworkRuntime");
            var requestTask = runtime.RequestManagedNetworkApprovalAsync(request, CancellationToken.None);
            await Task.Delay(120);

            var requestMessage = JsonDocument.Parse(writer.ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Single());
            try
            {
                var requestId = requestMessage.RootElement.GetProperty("id").GetInt64();
                var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
                Assert.True(pending.TryGetValue(requestId, out var tcs));

                tcs!.TrySetResult(JsonSerializer.SerializeToElement(new
                {
                    decision = new
                    {
                        applyNetworkPolicyAmendment = new
                        {
                            network_policy_amendment = new
                            {
                                host = "example.com",
                                action = "foobar",
                            },
                        },
                    },
                    networkPolicyAmendment = new
                    {
                        host = "example.com",
                        action = "foobar",
                    },
                }));
            }
            finally
            {
                requestMessage.Dispose();
            }

            var result = await requestTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("applyNetworkPolicyAmendment", result.Decision);
            Assert.Null(result.NetworkPolicyAmendment);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

        [Fact]
    public async Task EmitManagedNetworkSideEffectAsync_ShouldEmitDeveloperRawResponseItem()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var request = new KernelManagedNetworkExecutionRequest(
                ThreadId: "thread_network_side_effect_001",
                TurnId: "turn_network_side_effect_001",
                ItemId: "item_network_side_effect_001",
                Command: "network-access https://example.com:443",
                Cwd: root,
                SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly", networkAccess = false }),
                SandboxMode: "readOnly",
                ApprovalPolicy: "on-request");
            var sideEffect = new KernelManagedNetworkSideEffect(
                KernelManagedNetworkSideEffectKind.DeveloperMessage,
                "Allowed network rule saved in execpolicy (allowlist): example.com");

            var runtime = GetPrivateField<KernelManagedNetworkAppHostRuntime>(server, "managedNetworkRuntime");
            await runtime.EmitManagedNetworkSideEffectAsync(request, sideEffect, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var notification = Assert.Single(messages, static x => IsNotificationMethod(x.RootElement, "rawResponseItem/completed"));
                var payload = notification.RootElement.GetProperty("params");
                Assert.Equal(request.ThreadId, payload.GetProperty("threadId").GetString());
                Assert.Equal(request.TurnId, payload.GetProperty("turnId").GetString());

                var item = payload.GetProperty("item");
                Assert.Equal("message", item.GetProperty("type").GetString());
                Assert.Equal("developer", item.GetProperty("role").GetString());
                Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("id").GetString()));

                var content = item.GetProperty("content");
                Assert.Equal(1, content.GetArrayLength());
                Assert.Equal("input_text", content[0].GetProperty("type").GetString());
                Assert.Equal(sideEffect.Text, content[0].GetProperty("text").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
[Fact]
    public async Task RunAsync_ShouldReturnErrorWhenApprovalContextNotFound()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(
                KernelAppServerTestProtocol.WithInitialize(
                    """{"jsonrpc":"2.0","id":1,"method":"turn/approval/respond","params":{"callId":"missing-call","approved":false}}"""));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponseId(x.RootElement, 1));
                Assert.True(response.RootElement.TryGetProperty("error", out var error));
                Assert.Equal(-32004, error.GetProperty("code").GetInt32());
                Assert.Contains("审批请求", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRegisterAgentThreadMetadataAndExposeInThreadPayload()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000206";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"agent/thread/register","params":{"threadId":"00000000-0000-7000-8000-000000000206","agentNickname":"worker-a","agentRole":"reviewer"}}""",
                """{"jsonrpc":"2.0","id":2,"method":"thread/read","params":{"threadId":"00000000-0000-7000-8000-000000000206","includeTurns":false}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var registerResponse = messages.Single(x => IsResponsePayload(x.RootElement, 1)).RootElement.GetProperty("result").GetProperty("thread");
                Assert.Equal("worker-a", registerResponse.GetProperty("agentNickname").GetString());
                Assert.Equal("reviewer", registerResponse.GetProperty("agentRole").GetString());

                var readResponse = messages.Single(x => IsResponsePayload(x.RootElement, 2)).RootElement.GetProperty("result").GetProperty("thread");
                Assert.Equal("worker-a", readResponse.GetProperty("agentNickname").GetString());
                Assert.Equal("reviewer", readResponse.GetProperty("agentRole").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task RunAsync_ShouldCreateDispatchReportAndReadAgentJob()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"agent/job/create","params":{"jobId":"job_api_001","name":"demo-job","instruction":"process rows","items":[{"itemId":"item_001","value":1},{"itemId":"item_002","value":2}]}}""",
                """{"jsonrpc":"2.0","id":2,"method":"agent/job/dispatch","params":{"jobId":"job_api_001","threadIds":["thread_a","thread_b"]}}""",
                """{"jsonrpc":"2.0","id":3,"method":"agent/job/item/report","params":{"jobId":"job_api_001","itemId":"item_001","status":"completed","result":{"ok":true}}}""",
                """{"jsonrpc":"2.0","id":4,"method":"agent/job/item/report","params":{"jobId":"job_api_001","itemId":"item_002","status":"completed","result":{"ok":true}}}""",
                """{"jsonrpc":"2.0","id":5,"method":"agent/job/read","params":{"jobId":"job_api_001"}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var createResult = messages.Single(x => IsResponsePayload(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal("pending", createResult.GetProperty("job").GetProperty("status").GetString());

                var dispatchResult = messages.Single(x => IsResponsePayload(x.RootElement, 2)).RootElement.GetProperty("result");
                Assert.Equal(2, dispatchResult.GetProperty("items").GetArrayLength());

                var readResult = messages.Single(x => IsResponsePayload(x.RootElement, 5)).RootElement.GetProperty("result");
                Assert.Equal("completed", readResult.GetProperty("job").GetProperty("status").GetString());
                Assert.Equal(2, readResult.GetProperty("items").GetArrayLength());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredThreadDefaultsWithThreadError_ShouldReadNativeProviderKeys()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");

        try
        {
            var snapshot = ResolveConfiguredThreadDefaultsSnapshotForTest(
                userConfigToml: """
model = "gpt-5.4"
provider = "openai-compatible"
service_tier = "flex"
web_search = "disabled"
model_reasoning_effort = "high"
developer_instructions = "native prompt"

[providers.openai-compatible]
base_url = "https://example.invalid/v1"
api_key_env = "Example_KEY"
protocol = "responses"
request_max_retries = 2
stream_max_retries = 3
stream_idle_timeout_ms = 4000
websocket_connect_timeout_ms = 5000
supports_websockets = true
""",
                cwd: workspace,
                out _);

            Assert.Equal("gpt-5.4", snapshot.Model);
            Assert.Equal("openai-compatible", snapshot.ModelProviderId);
            Assert.Equal(KernelServiceTier.Flex, snapshot.ServiceTier);
            Assert.Equal("disabled", snapshot.WebSearchMode);
            Assert.Equal("high", snapshot.ReasoningEffort);
            Assert.Equal("native prompt", snapshot.DeveloperInstructions);
            Assert.Equal("https://example.invalid/v1", snapshot.ProviderBaseUrl);
            Assert.Equal("Example_KEY", snapshot.ProviderApiKeyEnvironmentVariable);
            Assert.Equal("responses", snapshot.ProviderWireApi);
            Assert.Equal(2, snapshot.ProviderRequestMaxRetries);
            Assert.Equal(3, snapshot.ProviderStreamMaxRetries);
            Assert.Equal(4000, snapshot.ProviderStreamIdleTimeoutMs);
            Assert.Equal(5000, snapshot.ProviderWebsocketConnectTimeoutMs);
            Assert.True(snapshot.ProviderSupportsWebsockets);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredThreadDefaultsWithThreadError_ShouldSnapshotActiveModelCatalog()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");

        try
        {
            var snapshot = ResolveConfiguredThreadDefaultsSnapshotForTest(
                userConfigToml: """
profile = "work"
model = "gpt-5"
provider = "openai"
model_route_set = "root-catalog"

[profiles.work]
execution = "coding"
agent = "architect"
session = "stable"
model_route_set = "profile-catalog"

[agents.architect]
model_route_set = "agent-catalog"

[execution_profiles.coding]
agent = "architect"
model_route_set = "execution-catalog"

[session_profiles.stable]
model_route_set = "session-catalog"

[providers.openai]
base_url = "https://api.openai.com"
""",
                cwd: workspace,
                out _);

            Assert.Equal("session-catalog", snapshot.ModelRouteSetId);

            var rootFallbackSnapshot = ResolveConfiguredThreadDefaultsSnapshotForTest(
                userConfigToml: """
model = "gpt-5"
provider = "openai"
model_route_set = "root-catalog"

[providers.openai]
base_url = "https://api.openai.com"
""",
                cwd: workspace,
                out _);

            Assert.Equal("root-catalog", rootFallbackSnapshot.ModelRouteSetId);

            var defaultFallbackSnapshot = ResolveConfiguredThreadDefaultsSnapshotForTest(
                userConfigToml: """
model = "gpt-5"
provider = "openai"

[providers.openai]
base_url = "https://api.openai.com"
""",
                cwd: workspace,
                out _);

            Assert.Equal("default", defaultFallbackSnapshot.ModelRouteSetId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldApplyThreadStartSessionOverrides()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        KernelThreadStore? threadStore = null;

        try
        {
            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"thread/start","params":{"cwd":"__ROOT__","model":"gpt-4.1-mini","modelProvider":"openai-custom","serviceTier":"flex","approvalPolicy":"never","sandbox":{"type":"readOnly","networkAccess":false},"ephemeral":true,"serviceName":"demo-service","baseInstructions":"base prompt","developerInstructions":"developer prompt","persistExtendedHistory":true,"dynamicTools":[{"name":"toolA","description":"demo","inputSchema":{"type":"object"}}]}}"""
                    .Replace("__ROOT__", root.Replace("\\", "/")));

            threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponsePayload(x.RootElement, 1)).RootElement.GetProperty("result");
                var thread = response.GetProperty("thread");
                Assert.Equal("gpt-4.1-mini", response.GetProperty("model").GetString());
                Assert.Equal("openai-custom", response.GetProperty("modelProvider").GetString());
                Assert.Equal("flex", response.GetProperty("serviceTier").GetString());
                Assert.Equal("never", response.GetProperty("approvalPolicy").GetString());
                Assert.Equal(root.Replace("\\", "/"), (response.GetProperty("cwd").GetString() ?? string.Empty).Replace("\\", "/"));
                Assert.Equal("readOnly", response.GetProperty("sandbox").GetProperty("type").GetString());
                var sessionConfiguration = response.GetProperty("sessionConfiguration");
                Assert.Equal("gpt-4.1-mini", sessionConfiguration.GetProperty("model").GetString());
                Assert.Equal("openai-custom", sessionConfiguration.GetProperty("modelProvider").GetString());
                Assert.Equal(root.Replace("\\", "/"), (sessionConfiguration.GetProperty("cwd").GetString() ?? string.Empty).Replace("\\", "/"));
                Assert.True(sessionConfiguration.GetProperty("ephemeral").GetBoolean());
                Assert.False(response.TryGetProperty("configSnapshot", out _));
                var threadSessionConfiguration = thread.GetProperty("sessionConfiguration");
                Assert.Equal("gpt-4.1-mini", threadSessionConfiguration.GetProperty("model").GetString());
                Assert.Equal(root.Replace("\\", "/"), (threadSessionConfiguration.GetProperty("cwd").GetString() ?? string.Empty).Replace("\\", "/"));

                var threadId = thread.GetProperty("id").GetString();
                Assert.False(string.IsNullOrWhiteSpace(threadId));

                var record = await threadStore.GetThreadAsync(threadId!, CancellationToken.None);
                Assert.NotNull(record);
                var snapshot = record!.ConfigSnapshot;
                Assert.NotNull(snapshot);
                Assert.Equal("gpt-4.1-mini", snapshot!.Model);
                Assert.Equal("openai-custom", snapshot.ModelProviderId);
                Assert.Equal("flex", snapshot.ServiceTier?.Value);
                Assert.Equal("never", snapshot.ApprovalPolicy.ScalarValue);
                Assert.Equal("readOnly", snapshot.SandboxMode);
                Assert.Equal(root.Replace("\\", "/"), snapshot.Cwd.Replace("\\", "/"));
                Assert.True(snapshot.Ephemeral);
                Assert.Equal("demo-service", snapshot.ServiceName);
                Assert.Equal("base prompt", snapshot.BaseInstructions);
                Assert.Equal("developer prompt", snapshot.DeveloperInstructions);
                Assert.True(snapshot.PersistExtendedHistory);
                Assert.Equal("toolA", Assert.Single(snapshot.DynamicTools!).FullName);
                Assert.Equal("appServer", snapshot.SessionSource);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldHandleSkillsRemoteMethodsAsAccountDisabled()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"skills/remote/list","params":{"hazelnutScope":"example","productSurface":"tianshu","enabled":true}}""",
                """{"jsonrpc":"2.0","id":2,"method":"skills/remote/export","params":{}}""",
                """{"jsonrpc":"2.0","id":3,"method":"skills/remote/export","params":{"hazelnutId":"hz_001"}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var listError = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32600, listError.GetProperty("code").GetInt32());

                var exportMissingId = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("error");
                Assert.Equal(-32602, exportMissingId.GetProperty("code").GetInt32());

                var exportDisabled = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement.GetProperty("error");
                Assert.Equal(-32600, exportDisabled.GetProperty("code").GetInt32());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldEmitMcpOauthLoginCompletedSuccessWhenTokenAlreadyConfigured()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var originalCurrentDirectory = Environment.CurrentDirectory;
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.CurrentDirectory = root;
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", Path.Combine(root, "kernel-home"));
            Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"key":"mcp_servers.demo.url","value":"https://example.com/mcp","cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":2,"method":"config/value/write","params":{"key":"mcp_servers.demo.api_key","value":"token-demo","cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":3,"method":"mcpServer/oauth/login","params":{"name":"demo","timeoutSecs":2}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);
            await Task.Delay(800);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var oauthResponse = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement.GetProperty("result");
                Assert.Equal("https://example.com/mcp", oauthResponse.GetProperty("authorizationUrl").GetString());

                Assert.Contains(
                    messages,
                    static x => IsNotificationMethod(x.RootElement, "mcpServer/oauthLogin/completed")
                                && x.RootElement.TryGetProperty("params", out var @params)
                                && string.Equals(@params.GetProperty("name").GetString(), "demo", StringComparison.Ordinal)
                                && @params.GetProperty("success").GetBoolean());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldEmitMcpOauthLoginCompletedFailureOnTimeout()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var originalCurrentDirectory = Environment.CurrentDirectory;
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.CurrentDirectory = root;
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", Path.Combine(root, "kernel-home"));
            Environment.SetEnvironmentVariable("TIANSHU_HOME", Path.Combine(root, "tianshu-home"));

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"key":"mcp_servers.demo.url","value":"https://example.com/mcp","cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":2,"method":"mcpServer/oauth/login","params":{"name":"demo","timeoutSecs":1}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);
            await Task.Delay(1500);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var oauthResponse = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("result");
                Assert.Equal("https://example.com/mcp", oauthResponse.GetProperty("authorizationUrl").GetString());

                Assert.Contains(
                    messages,
                    static x => IsNotificationMethod(x.RootElement, "mcpServer/oauthLogin/completed")
                                && x.RootElement.TryGetProperty("params", out var @params)
                                && string.Equals(@params.GetProperty("name").GetString(), "demo", StringComparison.Ordinal)
                                && !@params.GetProperty("success").GetBoolean()
                                && string.Equals(@params.GetProperty("error").GetString(), "oauth_login_timeout_or_not_completed", StringComparison.Ordinal));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldPersistTurnOverrideIntoThreadSessionResponse()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000207";
        using var openAiApiKeyScope = new EnvironmentVariableScope("OPENAI_API_KEY", null);
        KernelThreadStore? threadStore = null;

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"turn/start","params":{"threadId":"__THREAD_ID__","model":"gpt-4.1","approvalPolicy":"never","sandboxPolicy":{"type":"danger-full-access"},"cwd":"D:/Custom/Cwd","summary":"brief","effort":"high","personality":"pragmatic","collaborationMode":{"mode":"plan","settings":{"model":"gpt-5","reasoningEffort":"medium","developer_instructions":"plan prompt"}},"input":[{"text":"hello"}]}}""".Replace("__THREAD_ID__", threadId, StringComparison.Ordinal),
                """{"jsonrpc":"2.0","id":2,"method":"thread/resume","params":{"threadId":"__THREAD_ID__"}}""".Replace("__THREAD_ID__", threadId, StringComparison.Ordinal));

            threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);
            await Task.Delay(400);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var responseMessage = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement;
                Assert.True(
                    responseMessage.TryGetProperty("result", out var response),
                    responseMessage.TryGetProperty("error", out var error)
                        ? error.GetProperty("message").GetString()
                        : responseMessage.ToString());
                Assert.Equal("gpt-5", response.GetProperty("model").GetString());
                Assert.Equal("never", response.GetProperty("approvalPolicy").GetString());
                Assert.Equal("danger-full-access", response.GetProperty("sandbox").GetProperty("type").GetString());
                Assert.Equal("D:/Custom/Cwd", response.GetProperty("cwd").GetString());
                var sessionConfiguration = response.GetProperty("sessionConfiguration");
                Assert.Equal("gpt-5", sessionConfiguration.GetProperty("model").GetString());
                Assert.Equal("D:/Custom/Cwd", sessionConfiguration.GetProperty("cwd").GetString());
                Assert.Equal("never", sessionConfiguration.GetProperty("approvalPolicy").GetString());
                Assert.False(response.TryGetProperty("configSnapshot", out _));

                var record = await threadStore.GetThreadAsync(threadId, CancellationToken.None);
                Assert.NotNull(record);
                var snapshot = record!.ConfigSnapshot;
                Assert.NotNull(snapshot);
                Assert.Equal("gpt-5", snapshot!.Model);
                Assert.Equal("never", snapshot.ApprovalPolicy.ScalarValue);
                Assert.Equal("danger-full-access", snapshot.SandboxMode);
                Assert.Equal("D:/Custom/Cwd", snapshot.Cwd);
                Assert.Equal("brief", snapshot.ReasoningSummary);
                Assert.Equal("medium", snapshot.ReasoningEffort);
                Assert.Equal("pragmatic", snapshot.Personality);
                Assert.NotNull(snapshot.CollaborationMode);
                Assert.Equal("plan", snapshot.CollaborationMode!.Mode);
                Assert.Equal("gpt-5", snapshot.CollaborationMode.Settings.Model);
                Assert.Equal("medium", snapshot.CollaborationMode.Settings.ReasoningEffort);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldApplyThreadResumeOverridesAfterColdResume()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000208";
        using var openAiApiKeyScope = new EnvironmentVariableScope("OPENAI_API_KEY", null);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            await MaterializeThreadRolloutAsync(setupStore, threadId);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"thread/resume","params":{"threadId":"__THREAD_ID__","model":"gpt-4.1"}}"""
                    .Replace("__THREAD_ID__", threadId, StringComparison.Ordinal),
                """{"jsonrpc":"2.0","id":2,"method":"turn/start","params":{"threadId":"__THREAD_ID__","input":[{"text":"resume override validation"}]}}"""
                    .Replace("__THREAD_ID__", threadId, StringComparison.Ordinal));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var output = writer.ToString();
            var lines = output
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal("gpt-4.1", response.GetProperty("model").GetString());

                var errorNotification = Assert.Single(messages.Where(x => IsNotificationMethod(x.RootElement, "error"))).RootElement;
                Assert.False(string.IsNullOrWhiteSpace(errorNotification.GetProperty("params").GetProperty("message").GetString()));
                Assert.Contains(messages, static x => IsTurnCompletedWithStatus(x.RootElement, "failed"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldApplyThreadResumeOverridesWhenThreadIsLoaded()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var otherRoot = Path.Combine(root, "other");
        const string threadId = "00000000-0000-7000-8000-000000000209";
        const string originalModel = "gpt-5";
        var originalCwd = repoRoot.Replace("\\", "/");
        var overrideCwd = otherRoot.Replace("\\", "/");

        Task? runTask = null;
        KeyValuePair<long, TaskCompletionSource<JsonElement>> pendingRequest = default;
        KernelThreadStore? pendingTurnStore = null;

        try
        {
            Directory.CreateDirectory(otherRoot);

            var pendingTurn = await StartPendingWriteToolTurnAsync(
                storePath,
                threadId,
                repoRoot,
                originalModel,
                originalCwd,
                CancellationToken.None);
            var server = pendingTurn.Server;
            var writer = pendingTurn.Writer;
            var rolloutPath = pendingTurn.ThreadStore.RolloutRecorder.GetRolloutPath(threadId).Replace("\\", "/");
            runTask = pendingTurn.RunTask;
            pendingRequest = pendingTurn.PendingRequest;
            pendingTurnStore = pendingTurn.ThreadStore;

            await InvokeHandleThreadResumeAsync(
                server,
                101,
                $$"""
                {
                  "threadId": "{{threadId}}",
                  "model": "gpt-4.1",
                  "cwd": "{{overrideCwd}}",
                  "approvalPolicy": "never",
                  "sandbox": {
                    "type": "danger-full-access"
                  }
                }
                """);

            await InvokeHandleThreadResumeAsync(
                server,
                102,
                $$"""
                {
                  "threadId": "{{threadId}}",
                  "path": "{{rolloutPath}}",
                  "model": "o3",
                  "cwd": "{{overrideCwd}}",
                  "approvalPolicy": "never",
                  "sandbox": {
                    "type": "danger-full-access"
                  }
                }
                """);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var threadIdOnlyResume = messages.Single(x => IsResponsePayload(x.RootElement, 101)).RootElement.GetProperty("result");
                Assert.Equal("gpt-4.1", threadIdOnlyResume.GetProperty("model").GetString());
                Assert.Equal(overrideCwd, threadIdOnlyResume.GetProperty("cwd").GetString());
                Assert.Equal("never", threadIdOnlyResume.GetProperty("approvalPolicy").GetString());
                Assert.Equal("danger-full-access", threadIdOnlyResume.GetProperty("sandbox").GetProperty("type").GetString());
                Assert.Equal(overrideCwd, threadIdOnlyResume.GetProperty("thread").GetProperty("cwd").GetString());
                var threadIdOnlyResumeSessionConfiguration = threadIdOnlyResume.GetProperty("sessionConfiguration");
                Assert.Equal("gpt-4.1", threadIdOnlyResumeSessionConfiguration.GetProperty("model").GetString());
                Assert.Equal(overrideCwd, threadIdOnlyResumeSessionConfiguration.GetProperty("cwd").GetString());
                var threadIdOnlyResumeThreadSessionConfiguration = threadIdOnlyResume.GetProperty("thread").GetProperty("sessionConfiguration");
                Assert.Equal("gpt-4.1", threadIdOnlyResumeThreadSessionConfiguration.GetProperty("model").GetString());
                Assert.Equal(overrideCwd, threadIdOnlyResumeThreadSessionConfiguration.GetProperty("cwd").GetString());

                var pathResume = messages.Single(x => IsResponsePayload(x.RootElement, 102)).RootElement.GetProperty("result");
                Assert.Equal("o3", pathResume.GetProperty("model").GetString());
                Assert.Equal(overrideCwd, pathResume.GetProperty("cwd").GetString());
                Assert.Equal("never", pathResume.GetProperty("approvalPolicy").GetString());
                Assert.Equal("danger-full-access", pathResume.GetProperty("sandbox").GetProperty("type").GetString());
                Assert.Equal(overrideCwd, pathResume.GetProperty("thread").GetProperty("cwd").GetString());
                var pathResumeSessionConfiguration = pathResume.GetProperty("sessionConfiguration");
                Assert.Equal("o3", pathResumeSessionConfiguration.GetProperty("model").GetString());
                Assert.Equal(overrideCwd, pathResumeSessionConfiguration.GetProperty("cwd").GetString());
                var pathResumeThreadSessionConfiguration = pathResume.GetProperty("thread").GetProperty("sessionConfiguration");
                Assert.Equal("o3", pathResumeThreadSessionConfiguration.GetProperty("model").GetString());
                Assert.Equal(overrideCwd, pathResumeThreadSessionConfiguration.GetProperty("cwd").GetString());

                var record = await pendingTurnStore!.GetThreadAsync(threadId, CancellationToken.None);
                Assert.NotNull(record);
                Assert.NotNull(record!.ConfigSnapshot);
                Assert.Equal("o3", record.ConfigSnapshot!.Model);
                Assert.Equal("never", record.ConfigSnapshot.ApprovalPolicy.ScalarValue);
                Assert.Equal("danger-full-access", record.ConfigSnapshot.SandboxMode);
                Assert.Equal(overrideCwd, record.ConfigSnapshot.Cwd.Replace("\\", "/"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (pendingRequest.Value is not null)
            {
                pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
                {
                    decision = "decline",
                }));
            }

            if (runTask is not null)
            {
                await runTask;
            }

            if (pendingTurnStore is not null)
            {
                await pendingTurnStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRefreshProviderConnectionWhenLoadedThreadResumeChangesModelProvider()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var TianShuHome = Path.Combine(root, "tianshu-home");
        const string threadId = "00000000-0000-7000-8000-00000000020a";
        var originalCwd = repoRoot.Replace("\\", "/");

        Task? runTask = null;
        KeyValuePair<long, TaskCompletionSource<JsonElement>> pendingRequest = default;
        KernelThreadStore? pendingTurnStore = null;

        try
        {
            var pendingTurn = await StartPendingWriteToolTurnAsync(
                storePath,
                threadId,
                repoRoot,
                "gpt-5",
                originalCwd,
                CancellationToken.None);
            var server = pendingTurn.Server;
            var writer = pendingTurn.Writer;
            runTask = pendingTurn.RunTask;
            pendingRequest = pendingTurn.PendingRequest;
            pendingTurnStore = pendingTurn.ThreadStore;

            Directory.CreateDirectory(TianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(TianShuHome, "tianshu.toml"),
                """
model = "gpt-5.5"
provider = "openai-compatible"

[providers.openai-compatible]
base_url = "https://openai-compatible.example.invalid/v1"
api_key_env = "Example_KEY"
default_protocol = "responses"
request_max_retries = 2
stream_max_retries = 3
stream_idle_timeout_ms = 4000
websocket_connect_timeout_ms = 5000
supports_websockets = true
""");

            using var TianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", TianShuHome);

            await InvokeHandleThreadResumeAsync(
                server,
                101,
                $$"""
                {
                  "threadId": "{{threadId}}",
                  "model": "claude-opus-4.1",
                  "modelProvider": "openai-compatible"
                }
                """);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var response = messages.Single(x => IsResponsePayload(x.RootElement, 101)).RootElement.GetProperty("result");
                Assert.Equal("claude-opus-4.1", response.GetProperty("model").GetString());
                Assert.Equal("openai-compatible", response.GetProperty("modelProvider").GetString());

                var sessionConfiguration = response.GetProperty("sessionConfiguration");
                Assert.Equal("claude-opus-4.1", sessionConfiguration.GetProperty("model").GetString());
                Assert.Equal("openai-compatible", sessionConfiguration.GetProperty("modelProvider").GetString());
                Assert.Equal("https://openai-compatible.example.invalid/v1", sessionConfiguration.GetProperty("providerBaseUrl").GetString());
                Assert.Equal("Example_KEY", sessionConfiguration.GetProperty("providerApiKeyEnvironmentVariable").GetString());
                Assert.Equal("anthropic_messages", sessionConfiguration.GetProperty("providerWireApi").GetString());

                var record = await pendingTurnStore!.GetThreadAsync(threadId, CancellationToken.None);
                Assert.NotNull(record);
                Assert.NotNull(record!.ConfigSnapshot);
                Assert.Equal("claude-opus-4.1", record.ConfigSnapshot!.Model);
                Assert.Equal("openai-compatible", record.ConfigSnapshot.ModelProviderId);
                Assert.Equal("https://openai-compatible.example.invalid/v1", record.ConfigSnapshot.ProviderBaseUrl);
                Assert.Equal("Example_KEY", record.ConfigSnapshot.ProviderApiKeyEnvironmentVariable);
                Assert.Equal("anthropic_messages", record.ConfigSnapshot.ProviderWireApi);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (pendingRequest.Value is not null)
            {
                pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
                {
                    decision = "decline",
                }));
            }

            if (runTask is not null)
            {
                await runTask;
            }

            if (pendingTurnStore is not null)
            {
                await pendingTurnStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectHistoryResumeWhenThreadIsRunning()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "00000000-0000-7000-8000-000000000210";
        const string originalModel = "gpt-5";
        var originalCwd = repoRoot.Replace("\\", "/");

        Task? runTask = null;
        KeyValuePair<long, TaskCompletionSource<JsonElement>> pendingRequest = default;
        KernelThreadStore? pendingTurnStore = null;

        try
        {
            var pendingTurn = await StartPendingWriteToolTurnAsync(
                storePath,
                threadId,
                repoRoot,
                originalModel,
                originalCwd,
                CancellationToken.None);
            var server = pendingTurn.Server;
            var writer = pendingTurn.Writer;
            runTask = pendingTurn.RunTask;
            pendingRequest = pendingTurn.PendingRequest;
            pendingTurnStore = pendingTurn.ThreadStore;

            await InvokeHandleThreadResumeAsync(
                server,
                101,
                $$"""
                {
                  "threadId": "{{threadId}}",
                  "history": [
                    {
                      "type": "message",
                      "role": "user",
                      "content": [
                        {
                          "type": "input_text",
                          "text": "continue"
                        }
                      ]
                    }
                  ]
                }
                """);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 101)).RootElement.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.Equal("cannot resume thread with history while running", error.GetProperty("message").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (pendingRequest.Value is not null)
            {
                pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
                {
                    decision = "decline",
                }));
            }

            if (runTask is not null)
            {
                await runTask;
            }

            if (pendingTurnStore is not null)
            {
                await pendingTurnStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectMismatchedPathWhenThreadIsRunning()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "00000000-0000-7000-8000-000000000211";
        const string originalModel = "gpt-5";
        var originalCwd = repoRoot.Replace("\\", "/");
        var mismatchedPath = Path.Combine(root, "sessions", "other-thread.jsonl").Replace("\\", "/");

        Task? runTask = null;
        KeyValuePair<long, TaskCompletionSource<JsonElement>> pendingRequest = default;
        KernelThreadStore? pendingTurnStore = null;

        try
        {
            var pendingTurn = await StartPendingWriteToolTurnAsync(
                storePath,
                threadId,
                repoRoot,
                originalModel,
                originalCwd,
                CancellationToken.None);
            var server = pendingTurn.Server;
            var writer = pendingTurn.Writer;
            runTask = pendingTurn.RunTask;
            pendingRequest = pendingTurn.PendingRequest;
            pendingTurnStore = pendingTurn.ThreadStore;

            await InvokeHandleThreadResumeAsync(
                server,
                101,
                $$"""
                {
                  "threadId": "{{threadId}}",
                  "path": "{{mismatchedPath}}"
                }
                """);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 101)).RootElement.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.Equal("mismatched path for running thread", error.GetProperty("message").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (pendingRequest.Value is not null)
            {
                pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
                {
                    decision = "decline",
                }));
            }

            if (runTask is not null)
            {
                await runTask;
            }

            if (pendingTurnStore is not null)
            {
                await pendingTurnStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldIncludeActiveTurnSnapshotWhenThreadResumesRunningThread()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "00000000-0000-7000-8000-000000000212";
        const string originalModel = "gpt-5";
        var originalCwd = repoRoot.Replace("\\", "/");

        Task? runTask = null;
        KeyValuePair<long, TaskCompletionSource<JsonElement>> pendingRequest = default;
        KernelThreadStore? pendingTurnStore = null;

        try
        {
            var pendingTurn = await StartPendingWriteToolTurnAsync(
                storePath,
                threadId,
                repoRoot,
                originalModel,
                originalCwd,
                CancellationToken.None);
            var server = pendingTurn.Server;
            var writer = pendingTurn.Writer;
            runTask = pendingTurn.RunTask;
            pendingRequest = pendingTurn.PendingRequest;
            pendingTurnStore = pendingTurn.ThreadStore;

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(threadId, out var runtimeThread));
            var activeTurnId = Assert.IsType<string>(runtimeThread!.ActiveTurnId);

            await InvokeHandleThreadResumeAsync(
                server,
                101,
                $$"""
                {
                  "threadId": "{{threadId}}"
                }
                """);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var resumeResult = messages.Single(x => IsResponsePayload(x.RootElement, 101)).RootElement.GetProperty("result");
                var resumedTurns = resumeResult.GetProperty("thread").GetProperty("turns");
                var activeTurn = resumedTurns.EnumerateArray().Single(turn =>
                    string.Equals(turn.GetProperty("id").GetString(), activeTurnId, StringComparison.Ordinal));
                Assert.Equal("inProgress", activeTurn.GetProperty("status").GetString());
                var userMessage = activeTurn.GetProperty("items").EnumerateArray().Single(item =>
                    string.Equals(item.GetProperty("type").GetString(), "userMessage", StringComparison.Ordinal));
                var userContent = Assert.Single(userMessage.GetProperty("content").EnumerateArray());
                Assert.Equal("text", userContent.GetProperty("type").GetString());
                Assert.False(userContent.TryGetProperty("input_text", out _));
                Assert.Contains(
                    activeTurn.GetProperty("items").EnumerateArray(),
                    static item => string.Equals(item.GetProperty("type").GetString(), "fileChange", StringComparison.Ordinal));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (pendingRequest.Value is not null)
            {
                pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
                {
                    decision = "decline",
                }));
            }

            if (runTask is not null)
            {
                await runTask;
            }

            if (pendingTurnStore is not null)
            {
                await pendingTurnStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldPersistActiveTurnSnapshotForCrossSessionResume()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "00000000-0000-7000-8000-000000000213";
        const string originalModel = "gpt-5";
        var originalCwd = repoRoot.Replace("\\", "/");

        Task? runTask = null;
        KeyValuePair<long, TaskCompletionSource<JsonElement>> pendingRequest = default;
        KernelThreadStore? pendingTurnStore = null;

        try
        {
            var pendingTurn = await StartPendingWriteToolTurnAsync(
                storePath,
                threadId,
                repoRoot,
                originalModel,
                originalCwd,
                CancellationToken.None);
            var server = pendingTurn.Server;
            runTask = pendingTurn.RunTask;
            pendingRequest = pendingTurn.PendingRequest;
            pendingTurnStore = pendingTurn.ThreadStore;

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(threadId, out var runtimeThread));
            var activeTurnId = Assert.IsType<string>(runtimeThread!.ActiveTurnId);

            await pendingTurnStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);

            var persistedStore = new KernelThreadStore(storePath);
            await persistedStore.InitializeAsync(CancellationToken.None);
            var persistedThread = await persistedStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(persistedThread);
            Assert.Equal("active", persistedThread!.StatusType);
            var persistedTurn = persistedThread.Turns.Single(turn => string.Equals(turn.Id, activeTurnId, StringComparison.Ordinal));
            Assert.Equal("inProgress", persistedTurn.Status);
            Assert.Contains(
                persistedTurn.Items,
                static item => string.Equals(item.Type, "userMessage", StringComparison.Ordinal));

            var sessionPath = persistedStore.RolloutRecorder.GetRolloutPath(threadId);
            var sessionText = await File.ReadAllTextAsync(sessionPath, CancellationToken.None);
            Assert.Contains($"\"turnId\":\"{activeTurnId}\"", sessionText, StringComparison.Ordinal);
            Assert.Contains("\"status\":\"inProgress\"", sessionText, StringComparison.Ordinal);

            var resumeJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 101,
                method = "thread/resume",
                @params = new
                {
                    threadId,
                },
            });

            var resumeWriter = new StringWriter();
            var resumeServer = new AppHostServer(
                new StringReader(KernelAppServerTestProtocol.WithInitialize(resumeJson)),
                resumeWriter,
                new KernelThreadStore(storePath));
            await resumeServer.RunAsync(CancellationToken.None);

            using var resumeResponse = JsonDocument.Parse(
                resumeWriter
                    .ToString()
                    .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Single(static line => line.Contains("\"id\":101", StringComparison.Ordinal)));
            var resumedThread = resumeResponse.RootElement.GetProperty("result").GetProperty("thread");
            Assert.Equal("active", resumedThread.GetProperty("status").GetProperty("type").GetString());
            var resumedTurn = resumedThread.GetProperty("turns").EnumerateArray().Single(turn =>
                string.Equals(turn.GetProperty("id").GetString(), activeTurnId, StringComparison.Ordinal));
            Assert.Equal("inProgress", resumedTurn.GetProperty("status").GetString());
        }
        finally
        {
            if (pendingRequest.Value is not null)
            {
                pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
                {
                    decision = "decline",
                }));
            }

            if (runTask is not null)
            {
                await runTask;
            }

            if (pendingTurnStore is not null)
            {
                await pendingTurnStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldResolvePendingServerRequestBeforeCompletingInterruptedTurn()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "thread_interrupt_pending_request_001";
        const string originalModel = "gpt-5";
        var originalCwd = repoRoot.Replace("\\", "/");

        KeyValuePair<long, TaskCompletionSource<JsonElement>> pendingRequest = default;
        Task? runningTurnTask = null;
        KernelThreadStore? pendingTurnStore = null;

        try
        {
            var pendingTurn = await StartPendingWriteToolTurnAsync(
                storePath,
                threadId,
                repoRoot,
                originalModel,
                originalCwd,
                CancellationToken.None);
            var server = pendingTurn.Server;
            var writer = pendingTurn.Writer;
            pendingRequest = pendingTurn.PendingRequest;
            pendingTurnStore = pendingTurn.ThreadStore;

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(threadId, out var runtimeThread));
            var activeTurnId = Assert.IsType<string>(runtimeThread!.ActiveTurnId);

            var runningTurnTasks = GetPrivateField<ConcurrentDictionary<string, Task>>(server, "runningTurnTasks");
            Assert.True(runningTurnTasks.TryGetValue(activeTurnId, out runningTurnTask));

            await InvokeHandleTurnInterruptAsync(
                server,
                101,
                $$"""
                {
                  "threadId": "{{threadId}}",
                  "turnId": "{{activeTurnId}}"
                }
                """);

            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));
            await runningTurnTask!.WaitAsync(TimeSpan.FromSeconds(5));

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                Assert.Contains(messages, x => IsResponsePayload(x.RootElement, 101));

                var resolvedIndex = Array.FindIndex(messages, static x => IsNotificationMethod(x.RootElement, "serverRequest/resolved"));
                var responseIndex = Array.FindIndex(messages, static x => IsResponsePayload(x.RootElement, 101));
                var completedIndex = Array.FindIndex(messages, static x => IsNotificationMethod(x.RootElement, "turn/completed"));
                Assert.True(resolvedIndex >= 0 && responseIndex > resolvedIndex && completedIndex > responseIndex);

                var resolved = messages[resolvedIndex].RootElement.GetProperty("params");
                Assert.Equal(threadId, resolved.GetProperty("threadId").GetString());
                Assert.Equal(pendingRequest.Key, resolved.GetProperty("requestId").GetInt64());

                var turnCompleted = messages[completedIndex].RootElement.GetProperty("params").GetProperty("turn");
                Assert.Equal(activeTurnId, turnCompleted.GetProperty("id").GetString());
                Assert.Equal("interrupted", turnCompleted.GetProperty("status").GetString());

                var pendingResponses = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
                Assert.Empty(pendingResponses);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (pendingRequest.Value is not null)
            {
                pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
                {
                    decision = "decline",
                }));
            }

            if (runningTurnTask is not null)
            {
                await runningTurnTask;
            }

            if (pendingTurnStore is not null)
            {
                await pendingTurnStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadUnsubscribeDuringRunningTurn_ShouldUnloadRuntimeAndEmitThreadClosed()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "00000000-0000-7000-8000-000000000333";
        const string originalModel = "gpt-5";

        Task? runningTurnTask = null;
        KernelThreadStore? pendingTurnStore = null;

        try
        {
            var pendingTurn = await StartPendingWriteToolTurnAsync(
                storePath,
                threadId,
                repoRoot,
                originalModel,
                repoRoot.Replace("\\", "/"),
                CancellationToken.None);
            var server = pendingTurn.Server;
            var writer = pendingTurn.Writer;
            pendingTurnStore = pendingTurn.ThreadStore;

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(threadId, out var runtimeThread));
            var activeTurnId = Assert.IsType<string>(runtimeThread!.ActiveTurnId);

            var runningTurnTasks = GetPrivateField<ConcurrentDictionary<string, Task>>(server, "runningTurnTasks");
            Assert.True(runningTurnTasks.TryGetValue(activeTurnId, out runningTurnTask));

            await InvokeHandleThreadUnsubscribeAsync(
                server,
                102,
                $$"""
                {
                  "threadId": "{{threadId}}"
                }
                """);

            await WaitForWriterContainsAsync(writer, "\"method\":\"thread/closed\"", TimeSpan.FromSeconds(5));
            await runningTurnTask!.WaitAsync(TimeSpan.FromSeconds(5));

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var responseIndex = Array.FindIndex(messages, static x => IsResponsePayload(x.RootElement, 102));
                var statusChangedIndex = Array.FindIndex(messages, x =>
                    IsNotificationMethod(x.RootElement, "thread/status/changed")
                    && x.RootElement.TryGetProperty("params", out var @params)
                    && string.Equals(@params.GetProperty("threadId").GetString(), threadId, StringComparison.Ordinal)
                    && string.Equals(@params.GetProperty("status").GetProperty("type").GetString(), "notLoaded", StringComparison.Ordinal));
                var closedIndex = Array.FindIndex(messages, static x => IsNotificationMethod(x.RootElement, "thread/closed"));
                var turnCompletedIndex = Array.FindIndex(messages, static x => IsNotificationMethod(x.RootElement, "turn/completed"));
                Assert.True(responseIndex >= 0 && statusChangedIndex > responseIndex && closedIndex > statusChangedIndex);
                Assert.True(turnCompletedIndex >= 0);

                var unsubscribeResult = messages[responseIndex].RootElement.GetProperty("result");
                Assert.Equal("unsubscribed", unsubscribeResult.GetProperty("status").GetString());

                var statusChanged = messages[statusChangedIndex].RootElement.GetProperty("params");
                Assert.Equal(threadId, statusChanged.GetProperty("threadId").GetString());
                Assert.Equal("notLoaded", statusChanged.GetProperty("status").GetProperty("type").GetString());

                var turnCompleted = messages[turnCompletedIndex].RootElement.GetProperty("params").GetProperty("turn");
                Assert.Equal(activeTurnId, turnCompleted.GetProperty("id").GetString());
                Assert.Equal("interrupted", turnCompleted.GetProperty("status").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            Assert.False(threadManager.TryGetThread(threadId, out _));

            writer.GetStringBuilder().Clear();
            await InvokeHandleThreadUnsubscribeAsync(
                server,
                103,
                $$"""
                {
                  "threadId": "{{threadId}}"
                }
                """);

            using var secondUnsubscribe = JsonDocument.Parse(writer.ToString().Trim());
            var secondResult = secondUnsubscribe.RootElement.GetProperty("result");
            Assert.Equal("notLoaded", secondResult.GetProperty("status").GetString());
        }
        finally
        {
            if (runningTurnTask is not null)
            {
                await runningTurnTask;
            }

            if (pendingTurnStore is not null)
            {
                await pendingTurnStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenThreadUnsubscribeWithoutTrackedSubscription_ShouldReturnNotSubscribed()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        const string threadId = "00000000-0000-7000-8000-000000000334";
        KernelThreadStore? threadStore = null;

        try
        {
            threadStore = await CreateMaterializedThreadWithTurnsAsync(
                storePath,
                threadId,
                repoRoot,
                ("turn_001", "第一问", "第一答"));
            await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);

            var reader = new StringReader(string.Empty);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            var record = await threadStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(record);
            _ = threadManager.AttachThread(
                record!,
                new KernelThreadSessionState(
                    Model: "gpt-5",
                    ModelProvider: "openai",
                    ServiceTier: null,
                    Cwd: repoRoot,
                    ApprovalPolicy: KernelApprovalPolicy.OnRequest,
                    SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly" }),
                    SandboxMode: "readOnly"),
                loaded: true,
                publishCreated: false);

            await InvokeHandleThreadUnsubscribeAsync(
                server,
                101,
                $$"""
                {
                  "threadId": "{{threadId}}"
                }
                """);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var response = messages.Single(x => IsResponsePayload(x.RootElement, 101)).RootElement.GetProperty("result");
                Assert.Equal("notSubscribed", response.GetProperty("status").GetString());
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "thread/status/changed"));
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "thread/closed"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            Assert.True(threadManager.TryGetThread(threadId, out var loadedThread));
            Assert.True(loadedThread!.IsLoaded);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldTreatThreadAsRunningWhenActiveTurnTaskIsStillTracked()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000222";
        CancellationTokenSource? runningTurnCts = null;
        KernelThreadStore? setupStore = null;

        try
        {
            setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            await MaterializeThreadRolloutAsync(setupStore, threadId);

            var reader = new StringReader(string.Empty);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, setupStore);

            await InvokeHandleThreadResumeAsync(
                server,
                1,
                $$"""
                {
                  "threadId": "{{threadId}}",
                  "model": "gpt-5"
                }
                """);
            writer.GetStringBuilder().Clear();

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(threadId, out var runtimeThread));
            Assert.NotNull(runtimeThread);
            runtimeThread!.SetActiveTurn("turn-running-window-001");

            var runningTurnTasks = GetPrivateField<ConcurrentDictionary<string, Task>>(server, "runningTurnTasks");
            runningTurnCts = new CancellationTokenSource();
            runningTurnTasks["turn-running-window-001"] = Task.Delay(Timeout.Infinite, runningTurnCts.Token);

            await InvokeHandleThreadResumeAsync(
                server,
                101,
                $$"""
                {
                  "threadId": "{{threadId}}",
                  "model": "gpt-4.1"
                }
                """);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var resumeResult = messages.Single(x => IsResponsePayload(x.RootElement, 101)).RootElement.GetProperty("result");
                Assert.Equal("gpt-4.1", resumeResult.GetProperty("model").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            runningTurnCts?.Cancel();
            runningTurnCts?.Dispose();

            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            try
            {
                DeleteDirectory(root);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task RunAsync_ShouldBlockCommandExecWhenApprovalRequiredButNotApproved()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var otherRoot = Path.Combine(root, "other");
        Directory.CreateDirectory(otherRoot);

        try
        {
            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"command/exec","params":{"threadId":"thread_command_approval_001","itemId":"cmd_approval_001","command":["powershell.exe","-Command","Write-Output blocked"],"cwd":"__ROOT__","approvalPolicy":"on-request","sandboxPolicy":{"type":"workspaceWrite","writableRoots":["__OTHER__"],"networkAccess":false}}}"""
                    .Replace("__ROOT__", root.Replace("\\", "/"))
                    .Replace("__OTHER__", otherRoot.Replace("\\", "/")));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            var runTask = server.RunAsync(CancellationToken.None);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(2));
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                decision = "decline",
            }));

            await runTask;

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponsePayload(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal(-1, response.GetProperty("exitCode").GetInt32());
                Assert.Contains("沙箱策略阻止", response.GetProperty("stderr").GetString(), StringComparison.Ordinal);
                Assert.Contains(messages, static x => IsRequestMethod(x.RootElement, "item/commandExecution/requestApproval"));

                var commandCompleted = Assert.Single(messages.Where(static x =>
                    IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "commandExecution"));
                var commandCompletedItem = commandCompleted.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("declined", commandCompletedItem.GetProperty("status").GetString());
                Assert.Contains("powershell.exe", commandCompletedItem.GetProperty("command").GetString(), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldIncludeSkillMetadataInCommandApprovalRequest_WhenCommandTargetsSkillScript()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var skillRoot = Path.Combine(root, ".tianshu", "skills", "skill_exec_metadata");
        var scriptPath = Path.Combine(skillRoot, "scripts", "hello.cmd");
        var metadataPath = Path.Combine(skillRoot, "agents", "tianshu.yaml");
        var otherRoot = Path.Combine(root, "other");

        try
        {
            Directory.CreateDirectory(otherRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
            File.WriteAllText(scriptPath, "@echo off\r\necho hello\r\n");
            File.WriteAllText(metadataPath, "permissions: {}\n");
            File.WriteAllText(Path.Combine(skillRoot, "SKILL.md"), "# skill_exec_metadata\n");

            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "command/exec",
                @params = new
                {
                    threadId = "thread_command_skill_approval_001",
                    itemId = "cmd_skill_approval_001",
                    command = new[] { scriptPath },
                    cwd = root.Replace("\\", "/"),
                    approvalPolicy = "on-request",
                    sandboxPolicy = new
                    {
                        type = "workspaceWrite",
                        writableRoots = new[] { otherRoot.Replace("\\", "/") },
                        networkAccess = false,
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            var runTask = server.RunAsync(CancellationToken.None);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(2));
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                decision = "decline",
            }));

            await runTask;

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var approvalRequest = Assert.Single(messages, static x => IsRequestMethod(x.RootElement, "item/commandExecution/requestApproval"));
                var payload = approvalRequest.RootElement.GetProperty("params");
                var skillMetadata = payload.GetProperty("skillMetadata");
                Assert.Equal(Path.GetFullPath(Path.Combine(skillRoot, "SKILL.md")), skillMetadata.GetProperty("pathToSkillsMd").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldExecuteCommandWithinReadOnlySandboxWithoutApproval_WhenPolicyIsNever()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"command/exec","params":{"threadId":"thread_command_sandbox_001","itemId":"cmd_sandbox_001","command":["powershell.exe","-Command","Write-Output readonly-never"],"cwd":"__ROOT__","approvalPolicy":"never","sandboxPolicy":{"type":"readOnly"}}}"""
                    .Replace("__ROOT__", root.Replace("\\", "/")));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponsePayload(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal(0, response.GetProperty("exitCode").GetInt32());
                Assert.Contains("readonly-never", response.GetProperty("stdout").GetString(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(messages, static x => IsRequestMethod(x.RootElement, "item/commandExecution/requestApproval"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldPersistExecPolicyAmendmentAndAutoAllowSubsequentCommand()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");

        try
        {
            var repoRoot = Path.Combine(root, "repo");
            Directory.CreateDirectory(repoRoot);
            File.WriteAllText(Path.Combine(repoRoot, "note.txt"), "hello");
            using (var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "init",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }))
            {
                Assert.NotNull(process);
                process!.WaitForExit();
                Assert.Equal(0, process.ExitCode);
            }

            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", Path.Combine(root, "kernel-home"));

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"command/exec","params":{"threadId":"thread_command_policy_001","itemId":"cmd_policy_001","command":["git","add","note.txt"],"cwd":"__ROOT__","approvalPolicy":"on-request","sandboxPolicy":{"type":"readOnly"}}}""",
                """{"jsonrpc":"2.0","id":2,"method":"command/exec","params":{"threadId":"thread_command_policy_001","itemId":"cmd_policy_002","command":["git","add","note.txt"],"cwd":"__ROOT__","approvalPolicy":"on-request","sandboxPolicy":{"type":"readOnly"}}}""")
                .Replace("__ROOT__", repoRoot.Replace("\\", "/"));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            var runTask = server.RunAsync(CancellationToken.None);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(2));
            var linesBeforeResponse = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            using (var requestMessage = linesBeforeResponse
                       .Select(static line => JsonDocument.Parse(line))
                       .Single(static item => IsRequestMethod(item.RootElement, "item/commandExecution/requestApproval")))
            {
                var payload = requestMessage.RootElement.GetProperty("params");
                Assert.Equal(["git", "add"], payload.GetProperty("proposedExecpolicyAmendment").EnumerateArray().Select(static item => item.GetString()).ToArray());
                var decisions = payload.GetProperty("availableDecisions");
                Assert.Equal("git", decisions[2]
                    .GetProperty("acceptWithExecpolicyAmendment")
                    .GetProperty("execpolicy_amendment")[0]
                    .GetString());
            }
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                decision = new
                {
                    acceptWithExecpolicyAmendment = new
                    {
                        execpolicy_amendment = new[] { "git", "add" },
                    },
                },
                applyProposedExecPolicyAmendment = true,
            }));

            await runTask;

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var firstResponse = messages.Where(x => IsResponsePayload(x.RootElement, 1)).Last().RootElement.GetProperty("result");
                var secondResponse = messages.Where(x => IsResponsePayload(x.RootElement, 2)).Last().RootElement.GetProperty("result");
                Assert.Equal(0, firstResponse.GetProperty("exitCode").GetInt32());
                Assert.Equal(0, secondResponse.GetProperty("exitCode").GetInt32());

                var approvalRequests = messages.Count(static x => IsRequestMethod(x.RootElement, "item/commandExecution/requestApproval"));
                Assert.Equal(1, approvalRequests);

                var kernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
                var rulesPath = Path.Combine(kernelHome!, "exec-policy", "default.rules");
                Assert.True(File.Exists(rulesPath));
                Assert.Contains("git", File.ReadAllText(rulesPath), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task RunAsync_ShouldBlockCommandExecWhenWritableRootsDoNotContainWorkingDirectory()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var otherRoot = Path.Combine(root, "other");
        Directory.CreateDirectory(otherRoot);

        try
        {
            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"command/exec","params":{"threadId":"thread_command_roots_001","itemId":"cmd_roots_001","command":["mkdir","abc"],"cwd":"__ROOT__","approvalPolicy":"never","sandboxPolicy":{"type":"workspaceWrite","writableRoots":["__OTHER__"],"networkAccess":false}}}"""
                    .Replace("__ROOT__", root.Replace("\\", "/"))
                    .Replace("__OTHER__", otherRoot.Replace("\\", "/")));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponsePayload(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal(-1, response.GetProperty("exitCode").GetInt32());
                Assert.Contains("writableRoots", response.GetProperty("stderr").GetString(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(messages, static x => IsRequestMethod(x.RootElement, "item/commandExecution/requestApproval"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task RunAsync_ShouldReuseAcceptForSessionDecisionForSameCommandApprovalKey()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var otherRoot = Path.Combine(root, "other");
        Directory.CreateDirectory(otherRoot);

        try
        {
            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"command/exec","params":{"threadId":"thread_command_session_001","itemId":"cmd_session_001","command":["powershell.exe","-Command","Write-Output session-one"],"cwd":"__ROOT__","approvalPolicy":"on-request","sandboxPolicy":{"type":"workspaceWrite","writableRoots":["__OTHER__"],"networkAccess":false}}}""",
                """{"jsonrpc":"2.0","id":2,"method":"command/exec","params":{"threadId":"thread_command_session_001","itemId":"cmd_session_002","command":["powershell.exe","-Command","Write-Output session-one"],"cwd":"__ROOT__","approvalPolicy":"on-request","sandboxPolicy":{"type":"workspaceWrite","writableRoots":["__OTHER__"],"networkAccess":false}}}""")
                .Replace("__ROOT__", root.Replace("\\", "/"))
                .Replace("__OTHER__", otherRoot.Replace("\\", "/"));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            var runTask = server.RunAsync(CancellationToken.None);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(2));
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                decision = "acceptForSession",
            }));

            await runTask;

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var firstResponse = messages.Single(x => IsResponsePayload(x.RootElement, 1)).RootElement.GetProperty("result");
                var secondResponse = messages.Single(x => IsResponsePayload(x.RootElement, 2)).RootElement.GetProperty("result");
                Assert.Equal(0, firstResponse.GetProperty("exitCode").GetInt32());
                Assert.Equal(0, secondResponse.GetProperty("exitCode").GetInt32());
                Assert.Contains("session-one", firstResponse.GetProperty("stdout").GetString(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("session-one", secondResponse.GetProperty("stdout").GetString(), StringComparison.OrdinalIgnoreCase);

                var approvalRequests = messages.Count(static x => IsRequestMethod(x.RootElement, "item/commandExecution/requestApproval"));
                Assert.Equal(1, approvalRequests);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectTurnStartWhenInputExceedsMaxChars()
    {
        const int maxChars = 1 << 20;
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_turn_start_limit_001";
        var oversized = new string('x', maxChars + 1);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "turn/start",
                ["params"] = new Dictionary<string, object?>
                {
                    ["threadId"] = threadId,
                    ["input"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["text"] = oversized,
                        },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(payload));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                Assert.Equal(
                    $"Input exceeds the maximum length of {maxChars} characters.",
                    error.GetProperty("message").GetString());
                var data = error.GetProperty("data");
                Assert.Equal("input_too_large", data.GetProperty("input_error_code").GetString());
                Assert.Equal(maxChars, data.GetProperty("max_chars").GetInt32());
                Assert.Equal(maxChars + 1, data.GetProperty("actual_chars").GetInt32());

                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "turn/started"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectTurnSteerWhenInputExceedsMaxChars()
    {
        const int maxChars = 1 << 20;
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_turn_steer_limit_001";
        var oversized = new string('x', maxChars + 1);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "turn/steer",
                ["params"] = new Dictionary<string, object?>
                {
                    ["threadId"] = threadId,
                    ["expectedTurnId"] = "turn_any",
                    ["input"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["text"] = oversized,
                        },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(payload));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                Assert.Equal(
                    $"Input exceeds the maximum length of {maxChars} characters.",
                    error.GetProperty("message").GetString());
                var data = error.GetProperty("data");
                Assert.Equal("input_too_large", data.GetProperty("input_error_code").GetString());
                Assert.Equal(maxChars, data.GetProperty("max_chars").GetInt32());
                Assert.Equal(maxChars + 1, data.GetProperty("actual_chars").GetInt32());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task RunAsync_ShouldHandleRealtimeLifecycleStatefully()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_realtime_lifecycle_001";
        const string sessionId = "realtime_session_001";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"thread_realtime_lifecycle_001","sessionId":"realtime_session_001"}}""",
                """{"jsonrpc":"2.0","id":2,"method":"thread/realtime/appendAudio","params":{"threadId":"thread_realtime_lifecycle_001","sessionId":"realtime_session_001","audio":{"data":"AQID","sampleRate":24000,"numChannels":1}}}""",
                """{"jsonrpc":"2.0","id":3,"method":"thread/realtime/appendText","params":{"threadId":"thread_realtime_lifecycle_001","sessionId":"realtime_session_001","text":"hello realtime"}}""",
                """{"jsonrpc":"2.0","id":4,"method":"thread/realtime/stop","params":{"threadId":"thread_realtime_lifecycle_001","sessionId":"realtime_session_001"}}""",
                """{"jsonrpc":"2.0","id":5,"method":"thread/realtime/appendText","params":{"threadId":"thread_realtime_lifecycle_001","sessionId":"realtime_session_001","text":"after stop"}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                for (var requestId = 1; requestId <= 4; requestId++)
                {
                    var response = messages.Single(x => IsResponseId(x.RootElement, requestId));
                    Assert.True(response.RootElement.TryGetProperty("result", out _));
                }

                var appendAfterStop = messages.Single(x => IsResponseId(x.RootElement, 5)).RootElement.GetProperty("error");
                Assert.Equal(-32004, appendAfterStop.GetProperty("code").GetInt32());

                Assert.Contains(
                    messages,
                    x => IsNotificationMethod(x.RootElement, "thread/realtime/started")
                         && x.RootElement.TryGetProperty("params", out var @params)
                         && string.Equals(@params.GetProperty("threadId").GetString(), threadId, StringComparison.Ordinal)
                         && string.Equals(@params.GetProperty("sessionId").GetString(), sessionId, StringComparison.Ordinal));
                Assert.Contains(
                    messages,
                    static x => IsNotificationMethod(x.RootElement, "thread/realtime/itemAdded")
                                && x.RootElement.TryGetProperty("params", out var @params)
                                && @params.TryGetProperty("item", out var item)
                                && string.Equals(item.GetProperty("type").GetString(), "input_audio", StringComparison.Ordinal));
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "thread/realtime/outputAudio/delta"));
                Assert.Contains(
                    messages,
                    static x => IsNotificationMethod(x.RootElement, "thread/realtime/itemAdded")
                                && x.RootElement.TryGetProperty("params", out var @params)
                                && @params.TryGetProperty("item", out var item)
                                && string.Equals(item.GetProperty("type").GetString(), "input_text", StringComparison.Ordinal)
                                && string.Equals(item.GetProperty("text").GetString(), "hello realtime", StringComparison.Ordinal));
                Assert.Contains(
                    messages,
                    static x => IsNotificationMethod(x.RootElement, "thread/realtime/closed")
                                && x.RootElement.TryGetProperty("params", out var @params)
                                && string.Equals(@params.GetProperty("reason").GetString(), "requested", StringComparison.Ordinal));
                Assert.Contains(
                    messages,
                    static x => IsNotificationMethod(x.RootElement, "thread/realtime/error")
                                && x.RootElement.TryGetProperty("params", out var @params)
                                && (@params.GetProperty("message").GetString() ?? string.Empty).Contains("未启动实时会话", StringComparison.Ordinal));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectRealtimeStartWhenThreadNotFound()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var input = """{"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"missing-thread","sessionId":"session"}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32004, error.GetProperty("code").GetInt32());
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "thread/realtime/error"));
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "thread/realtime/started"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectRealtimeAppendAudioWhenSessionNotStarted()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_realtime_append_audio_001";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input =
                """{"jsonrpc":"2.0","id":1,"method":"thread/realtime/appendAudio","params":{"threadId":"thread_realtime_append_audio_001","audio":{"data":"AQID"}}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32004, error.GetProperty("code").GetInt32());
                Assert.Contains(messages, static x => IsNotificationMethod(x.RootElement, "thread/realtime/error"));
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "thread/realtime/outputAudio/delta"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectRealtimeAppendTextWhenSessionIdMismatch()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_realtime_append_text_001";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"thread/realtime/start","params":{"threadId":"thread_realtime_append_text_001","sessionId":"session_a"}}""",
                """{"jsonrpc":"2.0","id":2,"method":"thread/realtime/appendText","params":{"threadId":"thread_realtime_append_text_001","sessionId":"session_b","text":"hello"}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Contains(messages, static x => IsResponsePayload(x.RootElement, 1));
                var error = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("error");
                Assert.Equal(-32600, error.GetProperty("code").GetInt32());
                Assert.Contains(
                    messages,
                    static x => IsNotificationMethod(x.RootElement, "thread/realtime/error")
                                && x.RootElement.TryGetProperty("params", out var @params)
                                && (@params.GetProperty("message").GetString() ?? string.Empty).Contains("sessionId", StringComparison.Ordinal));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldValidateAndPersistFeedbackUpload()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_feedback_001";
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", Path.Combine(root, "kernel-home"));

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"feedback/upload","params":{"includeLogs":true}}""",
                """{"jsonrpc":"2.0","id":2,"method":"feedback/upload","params":{"classification":"bug"}}""",
                """{"jsonrpc":"2.0","id":3,"method":"feedback/upload","params":{"classification":"bug","includeLogs":false,"threadId":"missing-thread"}}""",
                """{"jsonrpc":"2.0","id":4,"method":"feedback/upload","params":{"classification":"bug","includeLogs":true,"threadId":"thread_feedback_001","reason":"unit-test","extraLogFiles":["./sample.log"]}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var missingClassification = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32602, missingClassification.GetProperty("code").GetInt32());

                var missingIncludeLogs = messages.Single(x => IsResponseId(x.RootElement, 2)).RootElement.GetProperty("error");
                Assert.Equal(-32602, missingIncludeLogs.GetProperty("code").GetInt32());

                var invalidThread = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement.GetProperty("error");
                Assert.Equal(-32600, invalidThread.GetProperty("code").GetInt32());

                var success = messages.Single(x => IsResponseId(x.RootElement, 4)).RootElement.GetProperty("result");
                Assert.Equal(threadId, success.GetProperty("threadId").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var feedbackRoot = Path.Combine(Path.Combine(root, "kernel-home"), "feedback");
            Assert.True(Directory.Exists(feedbackRoot));
            var reportFiles = Directory.GetFiles(feedbackRoot, "*.json", SearchOption.TopDirectoryOnly);
            Assert.NotEmpty(reportFiles);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReturnStructuredConversationSummaryByConversationId()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_summary_by_id_001";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input =
                """{"jsonrpc":"2.0","id":1,"method":"artifact/conversationsummary/read","params":{"threadId":"thread_summary_by_id_001"}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var result = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                var summary = result.GetProperty("summary");
                Assert.Equal(threadId, summary.GetProperty("conversationId").GetString());
                Assert.Equal("appServer", summary.GetProperty("source").GetString());
                Assert.True(summary.TryGetProperty("path", out _));
                Assert.True(summary.TryGetProperty("updatedAt", out _));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldSupportConversationSummaryByRolloutPath()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var rolloutPath = Path.Combine(root, "rollout.md");
        await File.WriteAllTextAsync(rolloutPath, "preview-line" + Environment.NewLine + "second");

        try
        {
            var input = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "artifact/conversationsummary/read",
                ["params"] = new Dictionary<string, object?>
                {
                    ["rolloutPath"] = rolloutPath.Replace("\\", "/"),
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var result = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                var summary = result.GetProperty("summary");
                Assert.Equal("preview-line", summary.GetProperty("preview").GetString());
                Assert.Equal(Path.GetFullPath(rolloutPath), summary.GetProperty("path").GetString());
                Assert.Equal("rollout", summary.GetProperty("source").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAcceptAppListThreadIdAndForceRefetchAndDeduplicateUpdateNotification()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_app_list_001";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"config/value/write","params":{"keyPath":"apps.demo.enabled","value":true,"cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":2,"method":"config/value/write","params":{"keyPath":"apps.demo.isAccessible","value":true,"cwd":"__ROOT__"}}""".Replace("__ROOT__", root.Replace("\\", "/")),
                """{"jsonrpc":"2.0","id":3,"method":"app/list","params":{"threadId":"thread_app_list_001","forceRefetch":true}}""",
                """{"jsonrpc":"2.0","id":4,"method":"app/list","params":{"threadId":"thread_app_list_001","forceRefetch":true}}""");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var firstList = messages.Single(x => IsResponseId(x.RootElement, 3)).RootElement.GetProperty("result");
                var firstItem = firstList.GetProperty("data")[0];
                Assert.Equal("demo", firstItem.GetProperty("id").GetString());
                Assert.True(firstItem.GetProperty("isEnabled").GetBoolean());
                Assert.True(firstItem.GetProperty("isAccessible").GetBoolean());

                var secondList = messages.Single(x => IsResponseId(x.RootElement, 4)).RootElement.GetProperty("result");
                Assert.Equal(1, secondList.GetProperty("data").GetArrayLength());

                var updatedNotifications = messages.Count(static x => IsNotificationMethod(x.RootElement, "app/list/updated"));
                Assert.Equal(0, updatedNotifications);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldStartBackgroundCommandAndCleanByThread()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_background_clean_001";
        var normalizedRoot = root.Replace("\\", "/");
        var commandArgs = OperatingSystem.IsWindows()
            ? new[] { "powershell", "-NoProfile", "-Command", "Start-Sleep -Seconds 30" }
            : new[] { "/bin/sh", "-lc", "sleep 30" };
        var commandJson = JsonSerializer.Serialize(commandArgs);
        var pid = -1;

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var commandRequest =
                """{"jsonrpc":"2.0","id":1,"method":"command/exec","params":{"threadId":"__THREAD__","command":__COMMAND__,"cwd":"__CWD__","background":true,"approvalPolicy":"never"}}"""
                    .Replace("__THREAD__", threadId, StringComparison.Ordinal)
                    .Replace("__COMMAND__", commandJson, StringComparison.Ordinal)
                    .Replace("__CWD__", normalizedRoot, StringComparison.Ordinal);
            var cleanRequest =
                """{"jsonrpc":"2.0","id":2,"method":"thread/backgroundTerminals/clean","params":{"threadId":"__THREAD__"}}"""
                    .Replace("__THREAD__", threadId, StringComparison.Ordinal);
            var input = string.Join(Environment.NewLine, commandRequest, cleanRequest);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var startResult = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.True(startResult.GetProperty("started").GetBoolean());
                pid = startResult.GetProperty("pid").GetInt32();
                Assert.True(pid > 0);

                Assert.Contains(messages, static x => IsResponsePayload(x.RootElement, 2));

                var processExecutionRuntime = GetPrivateField<object>(
                    server,
                    "processExecutionAppHostRuntime");
                var backgroundTerminals = GetPrivateField<ConcurrentDictionary<string, ConcurrentDictionary<string, Process>>>(
                    processExecutionRuntime,
                    "backgroundTerminalsByThread");
                Assert.False(backgroundTerminals.ContainsKey(threadId));
                Assert.True(WaitForProcessExit(pid, TimeSpan.FromSeconds(2)));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            TryKillProcessById(pid);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectBackgroundTerminalCleanWhenThreadNotFound()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var input = """{"jsonrpc":"2.0","id":1,"method":"thread/backgroundTerminals/clean","params":{"threadId":"missing-thread"}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32004, error.GetProperty("code").GetInt32());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SendServerRequestAsync_ShouldEmitRequestAndResolvePendingResponse()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(string.Empty);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            var method = typeof(AppHostServer)
                .GetMethod("SendServerRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var invokeResult = method!.Invoke(server, new object?[]
            {
                "item/tool/requestUserInput",
                new
                {
                    threadId = "thread_test",
                    turnId = "turn_test",
                    itemId = "call_test",
                    questions = new[]
                    {
                        new
                        {
                            id = "supplement",
                            header = "补充信息",
                            question = "请输入补充信息以继续。",
                            isOther = true,
                            isSecret = false,
                            options = new[]
                            {
                                new { label = "继续（推荐）", description = "继续当前任务。" },
                                new { label = "取消", description = "取消本次请求。" },
                            },
                        },
                    },
                },
                "thread_test",
                CancellationToken.None,
                TimeSpan.FromSeconds(30),
            });

            var requestTask = Assert.IsAssignableFrom<Task<JsonElement>>(invokeResult);
            await Task.Delay(120);

            var requestLines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var requestMessages = requestLines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var request = Assert.Single(requestMessages, static x => IsRequestMethod(x.RootElement, "item/tool/requestUserInput"));
                var requestId = request.RootElement.GetProperty("id").GetInt64();
                var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
                Assert.True(pending.TryGetValue(requestId, out var tcs));
                tcs!.TrySetResult(JsonSerializer.SerializeToElement(new
                {
                    answers = new Dictionary<string, object?>
                    {
                        ["supplement"] = new
                        {
                            answers = new[] { "补充输入" },
                        },
                    },
                }));
            }
            finally
            {
                foreach (var message in requestMessages)
                {
                    message.Dispose();
                }
            }

            var result = await requestTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(
                "补充输入",
                result.GetProperty("answers").GetProperty("supplement").GetProperty("answers")[0].GetString());

            var allLines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var allMessages = allLines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Contains(allMessages, static x => IsNotificationMethod(x.RootElement, "serverRequest/resolved"));
            }
            finally
            {
                foreach (var message in allMessages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RequestUserInputFromToolAsync_ShouldFallbackToEmptyAnswersWhenClientResponseInvalid()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(string.Empty);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            var method = typeof(AppHostServer)
                .GetMethod("RequestUserInputFromToolAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var request = new KernelRequestUserInputRequest(
                "call_test",
                [
                    new KernelRequestUserInputQuestion(
                        "confirm_path",
                        "Confirm",
                        "Proceed with the plan?",
                        [
                            new KernelRequestUserInputOption("Yes (Recommended)", "Continue the current plan."),
                            new KernelRequestUserInputOption("No", "Stop and revisit the approach."),
                        ],
                        IsOther: true,
                        IsSecret: false),
                ]);

            var invokeResult = method!.Invoke(server, ["thread_test", "turn_test", request, CancellationToken.None]);
            var requestTask = Assert.IsAssignableFrom<Task<KernelRequestUserInputResponse>>(invokeResult);
            await Task.Delay(120);

            var requestLines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var requestMessages = requestLines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var emittedRequest = Assert.Single(requestMessages, static x => IsRequestMethod(x.RootElement, "item/tool/requestUserInput"));
                var requestId = emittedRequest.RootElement.GetProperty("id").GetInt64();
                var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
                Assert.True(pending.TryGetValue(requestId, out var tcs));
                tcs!.TrySetResult(JsonSerializer.SerializeToElement(new
                {
                    invalid = true,
                }));
            }
            finally
            {
                foreach (var message in requestMessages)
                {
                    message.Dispose();
                }
            }

            var result = await requestTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(result.Answers);

            var allLines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var allMessages = allLines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Contains(allMessages, static x => IsNotificationMethod(x.RootElement, "serverRequest/resolved"));
            }
            finally
            {
                foreach (var message in allMessages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldExecuteSafeReadOnlyCommandWithoutApprovalRequest_WhenPolicyIsOnRequest()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var input = string.Join(
                Environment.NewLine,
                """{"jsonrpc":"2.0","id":1,"method":"command/exec","params":{"threadId":"thread_command_safe_001","itemId":"cmd_safe_001","command":["powershell.exe","-Command","Write-Output safe"],"cwd":"__ROOT__","approvalPolicy":"on-request","sandboxPolicy":{"type":"readOnly"}}}"""
                    .Replace("__ROOT__", root.Replace("\\", "/")));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var response = messages.Single(x => IsResponsePayload(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.Equal(0, response.GetProperty("exitCode").GetInt32());
                Assert.Contains("safe", response.GetProperty("stdout").GetString(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(messages, static x => IsRequestMethod(x.RootElement, "item/commandExecution/requestApproval"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SendServerRequestAsync_WhenLifecycleCleanupResolvesPendingUserInput_ShouldEmitResolvedAndCancelPendingResponse()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(string.Empty);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            var sendMethod = typeof(AppHostServer)
                .GetMethod("SendServerRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(sendMethod);

            var invokeResult = sendMethod!.Invoke(server, new object?[]
            {
                "item/tool/requestUserInput",
                new
                {
                    threadId = "thread_cleanup",
                    turnId = "turn_cleanup_old",
                    itemId = "call_cleanup",
                    questions = new[]
                    {
                        new
                        {
                            id = "supplement",
                            header = "补充信息",
                            question = "请输入补充信息以继续。",
                            isOther = true,
                            isSecret = false,
                            options = new[]
                            {
                                new { label = "继续（推荐）", description = "继续当前任务。" },
                                new { label = "取消", description = "取消本次请求。" },
                            },
                        },
                    },
                },
                "thread_cleanup",
                CancellationToken.None,
                TimeSpan.FromSeconds(30),
            });

            var requestTask = Assert.IsAssignableFrom<Task<JsonElement>>(invokeResult);
            await Task.Delay(120);

            var cleanupMethod = typeof(AppHostServer)
                .GetMethod("ResolvePendingUserInputRequestsForThreadLifecycleAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(cleanupMethod);
            var cleanupTask = Assert.IsAssignableFrom<Task>(cleanupMethod!.Invoke(server, new object?[]
            {
                "thread_cleanup",
                "turn_cleanup_new",
                "turn_started",
                CancellationToken.None,
                false,
            }));
            await cleanupTask;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await requestTask.WaitAsync(TimeSpan.FromSeconds(2)));

            var allLines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var allMessages = allLines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Contains(allMessages, static x => IsRequestMethod(x.RootElement, "item/tool/requestUserInput"));
                var resolved = Assert.Single(allMessages.Where(static x => IsNotificationMethod(x.RootElement, "serverRequest/resolved")));
                Assert.Equal("thread_cleanup", resolved.RootElement.GetProperty("params").GetProperty("threadId").GetString());
                Assert.Equal(1, resolved.RootElement.GetProperty("params").GetProperty("requestId").GetInt64());
            }
            finally
            {
                foreach (var message in allMessages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ThreadReadAndResume_ShouldExposePendingInteractiveRequests_AndServerRequestRespondShouldResolveThem()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000213";
        KernelThreadStore? setupStore = null;

        try
        {
            setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            await MaterializeThreadRolloutAsync(setupStore, threadId);

            var reader = new StringReader(string.Empty);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, setupStore);

            var sendMethod = typeof(AppHostServer)
                .GetMethod("SendServerRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            var readMethod = typeof(AppHostServer)
                .GetMethod("HandleThreadReadAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            var resumeMethod = typeof(AppHostServer)
                .GetMethod("HandleThreadResumeAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            var pendingInteractiveRuntime = GetPrivateField<KernelPendingInteractiveReplayAppHostRuntime>(server, "pendingInteractiveReplayAppHostRuntime");
            Assert.NotNull(sendMethod);
            Assert.NotNull(readMethod);
            Assert.NotNull(resumeMethod);

            var approvalTask = Assert.IsAssignableFrom<Task<JsonElement>>(sendMethod!.Invoke(server, new object?[]
            {
                "item/tool/requestApproval",
                new
                {
                    threadId,
                    turnId = "turn-pending-interactive-001",
                    approvalId = "approval-pending-001",
                    toolName = "shell",
                    command = "Get-ChildItem",
                    title = "需要批准 shell 调用",
                    message = "需要批准 shell 调用",
                },
                threadId,
                CancellationToken.None,
                TimeSpan.FromSeconds(30),
            }));
            var permissionTask = Assert.IsAssignableFrom<Task<JsonElement>>(sendMethod.Invoke(server, new object?[]
            {
                "item/permissions/requestApproval",
                new
                {
                    threadId,
                    turnId = "turn-pending-interactive-001",
                    approvalId = "permission-pending-001",
                    reason = "需要更高权限",
                    permissions = new
                    {
                        network = new
                        {
                            enabled = true,
                        },
                    },
                },
                threadId,
                CancellationToken.None,
                TimeSpan.FromSeconds(30),
            }));
            var userInputTask = Assert.IsAssignableFrom<Task<JsonElement>>(sendMethod.Invoke(server, new object?[]
            {
                "item/tool/requestUserInput",
                new
                {
                    threadId,
                    turnId = "turn-pending-interactive-001",
                    itemId = "input-pending-001",
                    questions = new[]
                    {
                        new
                        {
                            id = "config_path",
                            header = "配置文件",
                            question = "请选择配置文件",
                            isOther = true,
                            isSecret = false,
                            options = (object?)null,
                        },
                    },
                },
                threadId,
                CancellationToken.None,
                TimeSpan.FromSeconds(30),
            }));

            await Task.Delay(150);

            using var readIdDocument = JsonDocument.Parse("101");
            using var readParamsDocument = JsonDocument.Parse(
                $$"""
                {
                  "threadId": "{{threadId}}",
                  "includeTurns": false
                }
                """);
            var readTask = Assert.IsAssignableFrom<Task>(readMethod!.Invoke(server, new object?[]
            {
                readIdDocument.RootElement.Clone(),
                readParamsDocument.RootElement.Clone(),
                CancellationToken.None,
            }));
            await readTask;

            using var resumeIdDocument = JsonDocument.Parse("102");
            using var resumeParamsDocument = JsonDocument.Parse(
                $$"""
                {
                  "threadId": "{{threadId}}"
                }
                """);
            var resumeTask = Assert.IsAssignableFrom<Task>(resumeMethod!.Invoke(server, new object?[]
            {
                resumeIdDocument.RootElement.Clone(),
                resumeParamsDocument.RootElement.Clone(),
                CancellationToken.None,
            }));
            await resumeTask;

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var serializedOutput = writer.ToString();
                var readResponse = messages.Single(x => IsResponseId(x.RootElement, 101)).RootElement;
                Assert.True(readResponse.TryGetProperty("result", out var readResult), serializedOutput);
                Assert.True(readResult.TryGetProperty("thread", out var readThread), serializedOutput);
                Assert.True(readResult.TryGetProperty("sessionConfiguration", out var readSessionConfiguration), serializedOutput);
                Assert.Equal(root, readSessionConfiguration.GetProperty("cwd").GetString());
                Assert.True(readResult.TryGetProperty("messages", out var readMessages), serializedOutput);
                Assert.Empty(readMessages.EnumerateArray());
                Assert.True(readResult.TryGetProperty("pendingInteractiveRequests", out var readPendingInteractiveRequests), serializedOutput);
                Assert.Equal(3, readPendingInteractiveRequests.GetArrayLength());
                Assert.True(readThread.TryGetProperty("pendingInteractiveRequests", out var readThreadPendingInteractiveRequests), serializedOutput);
                Assert.Equal(3, readThreadPendingInteractiveRequests.GetArrayLength());

                var resumeResponse = messages.Single(x => IsResponseId(x.RootElement, 102)).RootElement;
                Assert.True(resumeResponse.TryGetProperty("result", out var resumeResult), serializedOutput);
                Assert.True(resumeResult.TryGetProperty("thread", out var resumeThread), serializedOutput);
                Assert.True(resumeResult.TryGetProperty("sessionConfiguration", out var resumeSessionConfiguration), serializedOutput);
                Assert.Equal(root, resumeSessionConfiguration.GetProperty("cwd").GetString());
                Assert.True(resumeResult.TryGetProperty("messages", out var resumeMessages), serializedOutput);
                Assert.Empty(resumeMessages.EnumerateArray());
                Assert.True(resumeResult.TryGetProperty("pendingInteractiveRequests", out var resumePendingInteractiveRequests), serializedOutput);
                Assert.Equal(3, resumePendingInteractiveRequests.GetArrayLength());
                Assert.True(resumeThread.TryGetProperty("pendingInteractiveRequests", out var resumeThreadPendingInteractiveRequests), serializedOutput);
                Assert.Equal(3, resumeThreadPendingInteractiveRequests.GetArrayLength());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            using var approvalRespondIdDocument = JsonDocument.Parse("201");
            using var approvalRespondParamsDocument = JsonDocument.Parse(
                """
                {
                  "requestId": 1,
                  "callId": "approval-pending-001",
                  "requestKind": "approval_requested",
                  "result": {
                    "decision": {
                      "type": "accept"
                    }
                  }
                }
                """);
            await pendingInteractiveRuntime.HandleServerRequestRespondAsync(
                approvalRespondIdDocument.RootElement.Clone(),
                approvalRespondParamsDocument.RootElement.Clone(),
                CancellationToken.None);

            using var permissionRespondIdDocument = JsonDocument.Parse("202");
            using var permissionRespondParamsDocument = JsonDocument.Parse(
                """
                {
                  "requestId": 2,
                  "callId": "permission-pending-001",
                  "requestKind": "permission_requested",
                  "result": {
                    "scope": "session",
                    "permissions": {
                      "network": {
                        "enabled": true
                      }
                    }
                  }
                }
                """);
            await pendingInteractiveRuntime.HandleServerRequestRespondAsync(
                permissionRespondIdDocument.RootElement.Clone(),
                permissionRespondParamsDocument.RootElement.Clone(),
                CancellationToken.None);

            using var userInputRespondIdDocument = JsonDocument.Parse("203");
            using var userInputRespondParamsDocument = JsonDocument.Parse(
                """
                {
                  "requestId": 3,
                  "callId": "input-pending-001",
                  "requestKind": "request_user_input",
                  "result": {
                    "answers": {
                      "config_path": {
                        "answers": [".tianshu/tianshu.toml"]
                      }
                    }
                  }
                }
                """);
            await pendingInteractiveRuntime.HandleServerRequestRespondAsync(
                userInputRespondIdDocument.RootElement.Clone(),
                userInputRespondParamsDocument.RootElement.Clone(),
                CancellationToken.None);

            var approvalResult = await approvalTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("accept", approvalResult.GetProperty("decision").GetProperty("type").GetString());

            var permissionResult = await permissionTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("session", permissionResult.GetProperty("scope").GetString());
            Assert.True(permissionResult.GetProperty("permissions").GetProperty("network").GetProperty("enabled").GetBoolean());

            var userInputResult = await userInputTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(
                ".tianshu/tianshu.toml",
                userInputResult.GetProperty("answers").GetProperty("config_path").GetProperty("answers")[0].GetString());

            var pendingInteractiveRuntimeField = server.GetType().GetField("pendingInteractiveReplayAppHostRuntime", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(pendingInteractiveRuntimeField);
            var pendingInteractiveRuntimeObject = pendingInteractiveRuntimeField!.GetValue(server);
            Assert.NotNull(pendingInteractiveRuntimeObject);
            var pendingInteractiveByRequestIdField = pendingInteractiveRuntimeObject!.GetType().GetField("pendingInteractiveRequestsByRequestId", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(pendingInteractiveByRequestIdField);
            var pendingInteractiveByRequestId = pendingInteractiveByRequestIdField!.GetValue(pendingInteractiveRuntime);
            Assert.NotNull(pendingInteractiveByRequestId);
            var pendingInteractiveCount = Assert.IsType<int>(
                pendingInteractiveByRequestId!.GetType().GetProperty("Count")!.GetValue(pendingInteractiveByRequestId));
            var pendingInteractiveIdsByCallIdField = pendingInteractiveRuntimeObject.GetType().GetField("pendingInteractiveRequestIdsByCallId", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(pendingInteractiveIdsByCallIdField);
            var pendingInteractiveIdsByCallId = Assert.IsType<ConcurrentDictionary<string, long>>(pendingInteractiveIdsByCallIdField!.GetValue(pendingInteractiveRuntime));
            Assert.Equal(0, pendingInteractiveCount);
            Assert.Empty(pendingInteractiveIdsByCallId);
        }
        finally
        {
            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            try
            {
                DeleteDirectory(root);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task ThreadResume_ShouldReplayPendingInteractiveApprovalRequests()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000214";
        KernelThreadStore? setupStore = null;
        CancellationTokenSource? runningTurnCts = null;

        try
        {
            setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            await MaterializeThreadRolloutAsync(setupStore, threadId);

            var reader = new StringReader(string.Empty);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, setupStore);

            var sendMethod = typeof(AppHostServer)
                .GetMethod("SendServerRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            var resumeMethod = typeof(AppHostServer)
                .GetMethod("HandleThreadResumeAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            var pendingInteractiveRuntime = GetPrivateField<KernelPendingInteractiveReplayAppHostRuntime>(server, "pendingInteractiveReplayAppHostRuntime");
            Assert.NotNull(sendMethod);
            Assert.NotNull(resumeMethod);

            await InvokeHandleThreadResumeAsync(
                server,
                100,
                $$"""
                {
                  "threadId": "{{threadId}}",
                  "model": "gpt-5"
                }
                """);
            writer.GetStringBuilder().Clear();

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(threadId, out var runtimeThread));
            Assert.NotNull(runtimeThread);
            runtimeThread!.SetActiveTurn("turn-pending-replay-001");
            runningTurnCts = new CancellationTokenSource();
            var runningTurns = GetPrivateField<ConcurrentDictionary<string, CancellationTokenSource>>(server, "runningTurns");
            Assert.True(runningTurns.TryAdd("turn-pending-replay-001", runningTurnCts));

            var commandApprovalTask = Assert.IsAssignableFrom<Task<JsonElement>>(sendMethod!.Invoke(server, new object?[]
            {
                "item/commandExecution/requestApproval",
                new
                {
                    threadId,
                    turnId = "turn-pending-replay-001",
                    approvalId = "command-approval-replay-001",
                    command = new[] { "python", "-c", "print(42)" },
                    title = "需要批准命令执行",
                    message = "需要批准命令执行",
                },
                threadId,
                CancellationToken.None,
                TimeSpan.FromSeconds(30),
            }));
            var fileChangeApprovalTask = Assert.IsAssignableFrom<Task<JsonElement>>(sendMethod.Invoke(server, new object?[]
            {
                "item/fileChange/requestApproval",
                new
                {
                    threadId,
                    turnId = "turn-pending-replay-001",
                    approvalId = "file-change-replay-001",
                    changes = new[]
                    {
                        new
                        {
                            path = "README.md",
                            kind = "add",
                            diff = "new line\n",
                        },
                    },
                    title = "需要批准文件变更",
                    message = "需要批准文件变更",
                },
                threadId,
                CancellationToken.None,
                TimeSpan.FromSeconds(30),
            }));

            await Task.Delay(150);
            writer.GetStringBuilder().Clear();

            using var resumeIdDocument = JsonDocument.Parse("101");
            using var resumeParamsDocument = JsonDocument.Parse(
                $$"""
                {
                  "threadId": "{{threadId}}"
                }
                """);
            var resumeTask = Assert.IsAssignableFrom<Task>(resumeMethod!.Invoke(server, new object?[]
            {
                resumeIdDocument.RootElement.Clone(),
                resumeParamsDocument.RootElement.Clone(),
                CancellationToken.None,
            }));
            await resumeTask;

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var resumeResponse = messages.Single(x => IsResponseId(x.RootElement, 101)).RootElement.GetProperty("result");
                Assert.Equal(threadId, resumeResponse.GetProperty("thread").GetProperty("id").GetString());
                var pendingInteractiveRequests = resumeResponse.GetProperty("pendingInteractiveRequests");
                Assert.Equal(2, pendingInteractiveRequests.GetArrayLength());
                Assert.Equal(
                    "item/commandExecution/requestApproval",
                    pendingInteractiveRequests[0].GetProperty("requestMethod").GetString());
                Assert.Equal(
                    "item/fileChange/requestApproval",
                    pendingInteractiveRequests[1].GetProperty("requestMethod").GetString());
                var threadPendingInteractiveRequests = resumeResponse.GetProperty("thread").GetProperty("pendingInteractiveRequests");
                Assert.Equal(2, threadPendingInteractiveRequests.GetArrayLength());

                var commandReplay = Assert.Single(messages.Where(static x => IsRequestMethod(x.RootElement, "item/commandExecution/requestApproval")));
                Assert.Equal(1, commandReplay.RootElement.GetProperty("id").GetInt64());
                Assert.Equal(
                    "command-approval-replay-001",
                    commandReplay.RootElement.GetProperty("params").GetProperty("approvalId").GetString());

                var fileChangeReplay = Assert.Single(messages.Where(static x => IsRequestMethod(x.RootElement, "item/fileChange/requestApproval")));
                Assert.Equal(2, fileChangeReplay.RootElement.GetProperty("id").GetInt64());
                Assert.Equal(
                    "file-change-replay-001",
                    fileChangeReplay.RootElement.GetProperty("params").GetProperty("approvalId").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            using var commandRespondIdDocument = JsonDocument.Parse("201");
            using var commandRespondParamsDocument = JsonDocument.Parse(
                """
                {
                  "requestId": 1,
                  "callId": "command-approval-replay-001",
                  "requestKind": "approval_requested",
                  "result": {
                    "decision": "accept"
                  }
                }
                """);
            await pendingInteractiveRuntime.HandleServerRequestRespondAsync(
                commandRespondIdDocument.RootElement.Clone(),
                commandRespondParamsDocument.RootElement.Clone(),
                CancellationToken.None);

            using var fileChangeRespondIdDocument = JsonDocument.Parse("202");
            using var fileChangeRespondParamsDocument = JsonDocument.Parse(
                """
                {
                  "requestId": 2,
                  "callId": "file-change-replay-001",
                  "requestKind": "approval_requested",
                  "result": {
                    "decision": "accept"
                  }
                }
                """);
            await pendingInteractiveRuntime.HandleServerRequestRespondAsync(
                fileChangeRespondIdDocument.RootElement.Clone(),
                fileChangeRespondParamsDocument.RootElement.Clone(),
                CancellationToken.None);

            var commandResult = await commandApprovalTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("accept", commandResult.GetProperty("decision").GetString());

            var fileChangeResult = await fileChangeApprovalTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("accept", fileChangeResult.GetProperty("decision").GetString());
        }
        finally
        {
            runningTurnCts?.Dispose();

            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            try
            {
                DeleteDirectory(root);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task ResolvePendingInteractiveRequestsForThreadLifecycleAsync_ShouldResolveAllPendingInteractiveKindsForTurn()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000215";
        const string turnId = "turn_pending_interactive_cleanup_001";
        KernelThreadStore? setupStore = null;

        try
        {
            setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var reader = new StringReader(string.Empty);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, setupStore);

            var sendMethod = typeof(AppHostServer)
                .GetMethod("SendServerRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            var readMethod = typeof(AppHostServer)
                .GetMethod("HandleThreadReadAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            var cleanupMethod = typeof(AppHostServer)
                .GetMethod("ResolvePendingInteractiveRequestsForThreadLifecycleAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(sendMethod);
            Assert.NotNull(readMethod);
            Assert.NotNull(cleanupMethod);

            var approvalTask = Assert.IsAssignableFrom<Task<JsonElement>>(sendMethod!.Invoke(server, new object?[]
            {
                "item/tool/requestApproval",
                new
                {
                    threadId,
                    turnId,
                    approvalId = "approval-cleanup-001",
                    toolName = "shell",
                    command = "Get-ChildItem",
                    title = "需要批准 shell 调用",
                    message = "需要批准 shell 调用",
                },
                threadId,
                CancellationToken.None,
                TimeSpan.FromSeconds(30),
            }));
            var permissionTask = Assert.IsAssignableFrom<Task<JsonElement>>(sendMethod.Invoke(server, new object?[]
            {
                "item/permissions/requestApproval",
                new
                {
                    threadId,
                    turnId,
                    approvalId = "permission-cleanup-001",
                    reason = "需要更高权限",
                    permissions = new
                    {
                        network = new
                        {
                            enabled = true,
                        },
                    },
                },
                threadId,
                CancellationToken.None,
                TimeSpan.FromSeconds(30),
            }));
            var userInputTask = Assert.IsAssignableFrom<Task<JsonElement>>(sendMethod.Invoke(server, new object?[]
            {
                "item/tool/requestUserInput",
                new
                {
                    threadId,
                    turnId,
                    itemId = "input-cleanup-001",
                    questions = new[]
                    {
                        new
                        {
                            id = "config_path",
                            header = "配置文件",
                            question = "请选择配置文件",
                            isOther = true,
                            isSecret = false,
                            options = (object?)null,
                        },
                    },
                },
                threadId,
                CancellationToken.None,
                TimeSpan.FromSeconds(30),
            }));

            await Task.Delay(150);

            var cleanupTask = Assert.IsAssignableFrom<Task>(cleanupMethod!.Invoke(server, new object?[]
            {
                threadId,
                turnId,
                "turn_completed",
                CancellationToken.None,
                true,
            }));
            await cleanupTask;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await approvalTask.WaitAsync(TimeSpan.FromSeconds(2)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await permissionTask.WaitAsync(TimeSpan.FromSeconds(2)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await userInputTask.WaitAsync(TimeSpan.FromSeconds(2)));

            using var readIdDocument = JsonDocument.Parse("301");
            using var readParamsDocument = JsonDocument.Parse(
                $$"""
                {
                  "threadId": "{{threadId}}",
                  "includeTurns": false
                }
                """);
            var readTask = Assert.IsAssignableFrom<Task>(readMethod!.Invoke(server, new object?[]
            {
                readIdDocument.RootElement.Clone(),
                readParamsDocument.RootElement.Clone(),
                CancellationToken.None,
            }));
            await readTask;

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var resolvedNotifications = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "serverRequest/resolved"))
                    .Select(x => x.RootElement.GetProperty("params").GetProperty("requestId").GetInt64())
                    .OrderBy(static x => x)
                    .ToArray();
                Assert.Equal([1L, 2L, 3L], resolvedNotifications);

                var readResponse = messages.Single(x => IsResponseId(x.RootElement, 301)).RootElement;
                Assert.True(readResponse.TryGetProperty("result", out var readResult));
                Assert.True(readResult.TryGetProperty("thread", out var readThread));
                var topLevelPendingInteractiveRequests = readResult.GetProperty("pendingInteractiveRequests");
                Assert.Equal(0, topLevelPendingInteractiveRequests.GetArrayLength());
                var threadPendingInteractiveRequests = readThread.GetProperty("pendingInteractiveRequests");
                Assert.Equal(0, threadPendingInteractiveRequests.GetArrayLength());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            try
            {
                DeleteDirectory(root);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReturnGitDiffForDeprecatedGitDiffToRemote()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_diff_001";
        var repoRoot = Path.Combine(root, "repo");

        try
        {
            Directory.CreateDirectory(repoRoot);
            await InitializeGitRepositoryAsync(repoRoot);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var input = """{"jsonrpc":"2.0","id":1,"method":"artifact/gitdifftoremote/read","params":{"threadId":"thread_diff_001"}}""";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var result = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");
                Assert.True(result.GetProperty("hasChanges").GetBoolean());
                Assert.Contains("diff --git", result.GetProperty("diff").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldEmitTurnDiffUpdatedWithActualDiffOnTurnFailure()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_turn_diff_001";
        var repoRoot = Path.Combine(root, "repo");
        using var openAiApiKeyScope = new EnvironmentVariableScope("OPENAI_API_KEY", null);

        try
        {
            Directory.CreateDirectory(repoRoot);
            await InitializeGitRepositoryAsync(repoRoot);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var input =
                """{"jsonrpc":"2.0","id":1,"method":"turn/start","params":{"threadId":"thread_turn_diff_001","input":[{"text":"检查 diff"}]}}""";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            await server.RunAsync(CancellationToken.None);
            await Task.Delay(700);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var diffNotification = Assert.Single(messages.Where(static x => IsNotificationMethod(x.RootElement, "turn/diff/updated")));
                var diff = diffNotification.RootElement.GetProperty("params").GetProperty("diff").GetString();
                Assert.Contains("diff --git", diff, StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldExecuteInlineReadToolCallAndCompleteTurn()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_inline_tool_read_001";
        var repoRoot = Path.Combine(root, "repo");
        var notePath = Path.Combine(repoRoot, "note.txt");

        try
        {
            Directory.CreateDirectory(repoRoot);
            await File.WriteAllTextAsync(notePath, "tool-read-ok");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var filePathJson = notePath.Replace('\\', '/');
            var toolArgsJson = JsonSerializer.Serialize(new
            {
                file_path = filePathJson,
            });
            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new
                        {
                            text = $"/tool read_file {toolArgsJson}",
                        },
                    },
                },
            });
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var toolCalls = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/tool/call"))
                    .Select(static x => x.RootElement.GetProperty("params").GetProperty("item"))
                    .ToArray();
                Assert.True(toolCalls.Length >= 2);
                Assert.Contains(toolCalls, static item => string.Equals(item.GetProperty("status").GetString(), "completed", StringComparison.Ordinal));
                Assert.Contains(toolCalls, static item =>
                    item.TryGetProperty("output", out var output)
                    && (output.GetString() ?? string.Empty).Contains("tool-read-ok", StringComparison.Ordinal));

                Assert.Contains(messages, static x => IsTurnCompletedWithStatus(x.RootElement, "completed"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldExecuteInlineListDirToolCallAndReturnEntries()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_inline_tool_list_dir_001";
        var repoRoot = Path.Combine(root, "repo");
        var nestedDir = Path.Combine(repoRoot, "nested");
        var deeperDir = Path.Combine(nestedDir, "deeper");
        var entryFile = Path.Combine(repoRoot, "entry.txt");
        var childFile = Path.Combine(nestedDir, "child.txt");
        var grandchildFile = Path.Combine(deeperDir, "grandchild.txt");

        try
        {
            Directory.CreateDirectory(deeperDir);
            await File.WriteAllTextAsync(entryFile, "root-entry");
            await File.WriteAllTextAsync(childFile, "child-entry");
            await File.WriteAllTextAsync(grandchildFile, "grandchild-entry");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var dirPathJson = repoRoot.Replace('\\', '/');
            var toolArgsJson = JsonSerializer.Serialize(new
            {
                dir_path = dirPathJson,
                offset = 1,
                limit = 50,
                depth = 3,
            });
            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new
                        {
                            text = $"/tool list_dir {toolArgsJson}",
                        },
                    },
                },
            });
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await Task.Delay(600);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var toolCalls = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/tool/call"))
                    .Select(static x => x.RootElement.GetProperty("params").GetProperty("item"))
                    .ToArray();
                Assert.True(toolCalls.Length >= 2);

                var completed = toolCalls
                    .Where(static item => string.Equals(item.GetProperty("status").GetString(), "completed", StringComparison.Ordinal))
                    .ToArray();
                Assert.NotEmpty(completed);

                var output = completed
                    .Select(static item => item.TryGetProperty("output", out var content) ? content.GetString() : null)
                    .FirstOrDefault(static content => !string.IsNullOrWhiteSpace(content));
                Assert.False(string.IsNullOrWhiteSpace(output));

                Assert.Contains($"Absolute path: {Path.GetFullPath(repoRoot)}", output, StringComparison.Ordinal);
                Assert.Contains("entry.txt", output, StringComparison.Ordinal);
                Assert.Contains("nested/", output, StringComparison.Ordinal);
                Assert.Contains("  child.txt", output, StringComparison.Ordinal);
                Assert.Contains("  deeper/", output, StringComparison.Ordinal);
                Assert.Contains("    grandchild.txt", output, StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldNotEmitLegacyTurnOperationChainNotifications()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_turn_op_chain_001";
        var repoRoot = Path.Combine(root, "repo");
        var notePath = Path.Combine(repoRoot, "note.txt");

        try
        {
            Directory.CreateDirectory(repoRoot);
            await File.WriteAllTextAsync(notePath, "op-chain-check");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var filePathJson = notePath.Replace('\\', '/');
            var toolArgsJson = JsonSerializer.Serialize(new
            {
                file_path = filePathJson,
            });
            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new
                        {
                            text = $"/tool read_file {toolArgsJson}",
                        },
                    },
                },
            });
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await Task.Delay(700);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "tool/op"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void TryMergeSteerInputsImmediately_ShouldMergeAndDrainQueuedInputs()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string turnId = "turn_merge_steer_001";

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore);

            var enqueueMethod = typeof(AppHostServer).GetMethod("EnqueueSteerInput", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(enqueueMethod);
            enqueueMethod!.Invoke(server, new object[] { turnId, "steer-one" });
            enqueueMethod.Invoke(server, new object[] { turnId, "steer-two" });

            var mergeMethod = typeof(AppHostServer).GetMethod("TryMergeSteerInputsImmediately", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(mergeMethod);

            var firstArgs = new object?[] { turnId, "base-input", null };
            var firstMerged = (bool)mergeMethod!.Invoke(server, firstArgs)!;
            Assert.True(firstMerged);

            var mergedText = Assert.IsType<string>(firstArgs[2]);
            Assert.Contains("base-input", mergedText, StringComparison.Ordinal);
            Assert.Contains("steer-one", mergedText, StringComparison.Ordinal);
            Assert.Contains("steer-two", mergedText, StringComparison.Ordinal);

            var secondArgs = new object?[] { turnId, mergedText, null };
            var secondMerged = (bool)mergeMethod.Invoke(server, secondArgs)!;
            Assert.False(secondMerged);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldEmitToolHookNotificationsForInlineToolCall()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_inline_tool_hook_001";
        var repoRoot = Path.Combine(root, "repo");
        var notePath = Path.Combine(repoRoot, "note.txt");

        try
        {
            Directory.CreateDirectory(repoRoot);
            await File.WriteAllTextAsync(notePath, "hook-check");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var filePathJson = notePath.Replace('\\', '/');
            var toolArgsJson = JsonSerializer.Serialize(new
            {
                file_path = filePathJson,
            });
            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new
                        {
                            text = $"/tool read_file {toolArgsJson}",
                        },
                    },
                },
            });
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await Task.Delay(600);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var hooks = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/tool/hook"))
                    .Select(static x => x.RootElement.GetProperty("params"))
                    .ToArray();

                Assert.Contains(
                    hooks,
                    static hook =>
                        string.Equals(hook.GetProperty("toolName").GetString(), "read_file", StringComparison.Ordinal)
                        && string.Equals(hook.GetProperty("phase").GetString(), "before", StringComparison.Ordinal));
                Assert.Contains(
                    hooks,
                    static hook =>
                        string.Equals(hook.GetProperty("toolName").GetString(), "read_file", StringComparison.Ordinal)
                        && string.Equals(hook.GetProperty("phase").GetString(), "after", StringComparison.Ordinal)
                        && string.Equals(hook.GetProperty("status").GetString(), "completed", StringComparison.Ordinal));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldFailTurnWhenInlineToolArgumentsInvalid()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_inline_tool_invalid_001";
        var repoRoot = Path.Combine(root, "repo");

        try
        {
            Directory.CreateDirectory(repoRoot);
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var input =
                """{"jsonrpc":"2.0","id":1,"method":"turn/start","params":{"threadId":"thread_inline_tool_invalid_001","input":[{"text":"/tool read_file {}"}]}}""";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var toolCalls = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/tool/call"))
                    .Select(static x => x.RootElement.GetProperty("params").GetProperty("item"))
                    .ToArray();
                Assert.True(toolCalls.Length >= 2);

                Assert.Contains(toolCalls, static item =>
                    string.Equals(item.GetProperty("status").GetString(), "failed", StringComparison.Ordinal)
                    && item.TryGetProperty("output", out var output)
                    && (output.GetString() ?? string.Empty).Contains("工具参数无效", StringComparison.Ordinal));

                Assert.Contains(messages, static x => IsTurnCompletedWithStatus(x.RootElement, "completed"));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldBlockWriteToolOutsideWritableRoots()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var repoRoot = Path.Combine(root, "repo");
        var otherRoot = Path.Combine(root, "other");
        const string threadId = "thread_inline_tool_roots_001";
        var blockedFile = Path.Combine(repoRoot, "blocked.txt");

        try
        {
            Directory.CreateDirectory(repoRoot);
            Directory.CreateDirectory(otherRoot);
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var input =
                """{"jsonrpc":"2.0","id":1,"method":"turn/start","params":{"threadId":"thread_inline_tool_roots_001","sandboxPolicy":{"type":"workspaceWrite","writableRoots":["__OTHER__"],"networkAccess":false},"approvalPolicy":"never","input":[{"text":"/tool write {\"path\":\"blocked.txt\",\"content\":\"x\"}"}]}}"""
                    .Replace("__OTHER__", otherRoot.Replace("\\", "/"));
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var toolCalls = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/tool/call"))
                    .Select(static x => x.RootElement.GetProperty("params").GetProperty("item"))
                    .ToArray();
                Assert.True(toolCalls.Length >= 2);
                Assert.Contains(toolCalls, static item =>
                    string.Equals(item.GetProperty("status").GetString(), "failed", StringComparison.Ordinal)
                    && item.TryGetProperty("output", out var output)
                    && (output.GetString() ?? string.Empty).Contains("写入路径", StringComparison.Ordinal));
                Assert.False(File.Exists(blockedFile));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task RunAsync_ShouldBlockMutatingInlineToolUnderReadOnlySandbox()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_inline_tool_write_001";
        var repoRoot = Path.Combine(root, "repo");
        var blockedFile = Path.Combine(repoRoot, "blocked.txt");

        try
        {
            Directory.CreateDirectory(repoRoot);
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var input =
                """{"jsonrpc":"2.0","id":1,"method":"turn/start","params":{"threadId":"thread_inline_tool_write_001","sandboxPolicy":{"type":"readOnly"},"approvalPolicy":"never","input":[{"text":"/tool write {\"path\":\"blocked.txt\",\"content\":\"x\"}"}]}}""";
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var toolCalls = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/tool/call"))
                    .Select(static x => x.RootElement.GetProperty("params").GetProperty("item"))
                    .ToArray();
                Assert.True(toolCalls.Length >= 2);

                Assert.Contains(toolCalls, static item =>
                    string.Equals(item.GetProperty("status").GetString(), "failed", StringComparison.Ordinal)
                    && item.TryGetProperty("output", out var output)
                    && (((output.GetString() ?? string.Empty).Contains("沙箱策略", StringComparison.Ordinal))
                        || ((output.GetString() ?? string.Empty).Contains("策略阻止", StringComparison.Ordinal))));

                Assert.Contains(messages, static x => IsTurnCompletedWithStatus(x.RootElement, "completed"));
                Assert.False(File.Exists(blockedFile));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRequestFileChangeApprovalForWriteToolAndWriteAfterAccept()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_inline_tool_file_change_accept_001";
        var repoRoot = Path.Combine(root, "repo");
        var approvedFile = Path.Combine(repoRoot, "approved.txt");

        try
        {
            Directory.CreateDirectory(repoRoot);
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var toolArgsJson = JsonSerializer.Serialize(new
            {
                path = "approved.txt",
                content = "approved",
            });
            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    approvalPolicy = "on-request",
                    sandboxPolicy = new
                    {
                        type = "workspaceWrite",
                    },
                    input = new[]
                    {
                        new
                        {
                            text = $"/tool write {toolArgsJson}",
                        },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            var runTask = server.RunAsync(CancellationToken.None);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(5));
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                decision = "accept",
            }));

            await runTask;
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Contains(messages, static x => IsRequestMethod(x.RootElement, "item/fileChange/requestApproval"));
                Assert.DoesNotContain(messages, static x => IsRequestMethod(x.RootElement, "item/tool/requestApproval"));
                var fileChangeStarted = Assert.Single(messages.Where(static x =>
                    IsNotificationMethod(x.RootElement, "item/started")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "fileChange"));
                var fileChangeStartedItem = fileChangeStarted.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("inProgress", fileChangeStartedItem.GetProperty("status").GetString());
                Assert.Equal(approvedFile, fileChangeStartedItem.GetProperty("changes")[0].GetProperty("path").GetString());

                var fileChangeCompleted = Assert.Single(messages.Where(static x =>
                    IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "fileChange"));
                var fileChangeCompletedItem = fileChangeCompleted.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("completed", fileChangeCompletedItem.GetProperty("status").GetString());
                Assert.Equal(approvedFile, fileChangeCompletedItem.GetProperty("changes")[0].GetProperty("path").GetString());


                var toolCalls = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/tool/call"))
                    .Select(static x => x.RootElement.GetProperty("params").GetProperty("item"))
                    .ToArray();
                Assert.Contains(toolCalls, static item =>
                    string.Equals(item.GetProperty("status").GetString(), "completed", StringComparison.Ordinal)
                    && item.TryGetProperty("output", out var output)
                    && (output.GetString() ?? string.Empty).Contains("写入成功", StringComparison.Ordinal));

                Assert.True(File.Exists(approvedFile));
                Assert.Equal("approved", await File.ReadAllTextAsync(approvedFile));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldDeclineFileChangeApprovalForWriteTool()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000223";
        var repoRoot = Path.Combine(root, "repo");
        var declinedFile = Path.Combine(repoRoot, "declined.txt");

        try
        {
            Directory.CreateDirectory(repoRoot);
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var toolArgsJson = JsonSerializer.Serialize(new
            {
                path = "declined.txt",
                content = "nope",
            });
            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    approvalPolicy = "on-request",
                    sandboxPolicy = new
                    {
                        type = "workspaceWrite",
                    },
                    input = new[]
                    {
                        new
                        {
                            text = $"/tool write {toolArgsJson}",
                        },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            var runTask = server.RunAsync(CancellationToken.None);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(5));
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                decision = "decline",
            }));

            await runTask;
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Contains(messages, static x => IsRequestMethod(x.RootElement, "item/fileChange/requestApproval"));
                var fileChangeStarted = Assert.Single(messages.Where(static x =>
                    IsNotificationMethod(x.RootElement, "item/started")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "fileChange"));
                var fileChangeStartedItem = fileChangeStarted.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("inProgress", fileChangeStartedItem.GetProperty("status").GetString());
                Assert.Equal(declinedFile, fileChangeStartedItem.GetProperty("changes")[0].GetProperty("path").GetString());

                var fileChangeCompleted = Assert.Single(messages.Where(static x =>
                    IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "fileChange"));
                var fileChangeCompletedItem = fileChangeCompleted.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal("declined", fileChangeCompletedItem.GetProperty("status").GetString());
                Assert.Equal(declinedFile, fileChangeCompletedItem.GetProperty("changes")[0].GetProperty("path").GetString());

                Assert.DoesNotContain(messages, static x => IsRequestMethod(x.RootElement, "item/tool/requestApproval"));

                var toolCalls = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/tool/call"))
                    .Select(static x => x.RootElement.GetProperty("params").GetProperty("item"))
                    .ToArray();
                Assert.Contains(toolCalls, static item =>
                    string.Equals(item.GetProperty("status").GetString(), "failed", StringComparison.Ordinal)
                    && item.TryGetProperty("output", out var output)
                    && (output.GetString() ?? string.Empty).Contains("文件变更未获批准", StringComparison.Ordinal));

                Assert.False(File.Exists(declinedFile));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReuseFileChangeAcceptForSessionDecisionForSameWriteTarget()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_inline_tool_file_change_session_001";
        var repoRoot = Path.Combine(root, "repo");
        var sessionFile = Path.Combine(repoRoot, "session.txt");

        try
        {
            Directory.CreateDirectory(repoRoot);
            await File.WriteAllTextAsync(sessionFile, "one");

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(string.Empty);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            var fileChangeApprovalSessionPathsByThread =
                GetPrivateField<ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>>(server, "fileChangeApprovalSessionPathsByThread");
            KernelToolRuntimeApprovalHelpers.MarkFileChangesApprovedForSession(
                fileChangeApprovalSessionPathsByThread,
                threadId,
                new[] { KernelFileChangeApprovalHelpers.NormalizeApprovalKey(sessionFile) });

            var result = await server.ExecuteToolCallAsync(
                threadId,
                turnId: "turn_file_change_session_001",
                itemId: "tool_write_session_001",
                toolName: "write",
                arguments: JsonSerializer.SerializeToElement(new
                {
                    path = "session.txt",
                    content = "two",
                    append = true,
                }),
                context: new TurnRequestContext(
                    Model: null,
                    ModelProvider: null,
                    ServiceTier: null,
                    ApprovalPolicy: "on-request",
                    SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly" }),
                    SandboxMode: null,
                    Cwd: repoRoot,
                    ProviderBaseUrl: null,
                    ProviderApiKeyEnvironmentVariable: null,
                    ProviderWireApi: null,
                    IsReview: false,
                    ReviewDisplayText: null),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("写入成功", result.OutputText, StringComparison.Ordinal);
            Assert.Equal("onetwo", await File.ReadAllTextAsync(sessionFile));

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            Assert.Empty(pending);
            Assert.DoesNotContain("item/fileChange/requestApproval", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldApplyPatchAfterFileChangeApprovalUnderReadOnlySandbox()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_inline_tool_file_change_patch_001";
        var repoRoot = Path.Combine(root, "repo");
        var patchedFile = Path.Combine(repoRoot, "patched.txt");

        try
        {
            Directory.CreateDirectory(repoRoot);
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var patch = string.Join(
                "\n",
                "*** Begin Patch",
                "*** Add File: patched.txt",
                "+patched-content",
                "*** End Patch");
            var toolArgsJson = JsonSerializer.Serialize(new
            {
                input = patch,
            });
            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    approvalPolicy = "on-request",
                    sandboxPolicy = new
                    {
                        type = "readOnly",
                    },
                    input = new[]
                    {
                        new
                        {
                            text = $"/tool apply_patch {toolArgsJson}",
                        },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            var runTask = server.RunAsync(CancellationToken.None);

            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(5));
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                decision = "accept",
            }));

            await runTask;
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Contains(messages, static x => IsRequestMethod(x.RootElement, "item/fileChange/requestApproval"));
                Assert.DoesNotContain(messages, static x => IsRequestMethod(x.RootElement, "item/tool/requestApproval"));

                var toolCalls = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/tool/call"))
                    .Select(static x => x.RootElement.GetProperty("params").GetProperty("item"))
                    .ToArray();
                var completedToolCall = Assert.Single(toolCalls
                    .Where(static item =>
                        string.Equals(item.GetProperty("status").GetString(), "completed", StringComparison.Ordinal)
                        && item.TryGetProperty("output", out var output)
                        && (output.GetString() ?? string.Empty).Contains("patched.txt", StringComparison.Ordinal))
                    .ToArray());
                var fileChangeOutputDelta = Assert.Single(messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/fileChange/outputDelta"))
                    .Select(static x => x.RootElement.GetProperty("params"))
                    .ToArray());
                Assert.Equal(completedToolCall.GetProperty("id").GetString(), fileChangeOutputDelta.GetProperty("itemId").GetString());
                Assert.Contains("patched.txt", fileChangeOutputDelta.GetProperty("delta").GetString() ?? string.Empty, StringComparison.Ordinal);

                var resolvedIndex = Array.FindIndex(messages, static x => IsNotificationMethod(x.RootElement, "serverRequest/resolved"));
                var deltaIndex = Array.FindIndex(messages, static x => IsNotificationMethod(x.RootElement, "item/fileChange/outputDelta"));
                Assert.True(resolvedIndex >= 0 && deltaIndex > resolvedIndex);


                Assert.True(File.Exists(patchedFile));
                Assert.Equal("patched-content", (await File.ReadAllTextAsync(patchedFile)).TrimEnd('\r', '\n'));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
    [Fact]
    public void ResolveManagedNetworkSettings_ShouldUseTianShuDefaultRuntimeValues()
    {
        var settings = ResolveManagedNetworkSettingsForTest(
            userConfigToml: """
default_permissions = "trusted"

[permissions.trusted.network]
enabled = true
""",
            requirementsToml: """
[experimental_network]
enabled = true
""");

        Assert.True(settings.RequirementsPresent);
        Assert.True(settings.Enabled);
        Assert.Equal("127.0.0.1", settings.HttpHost);
        Assert.Equal(3128, settings.HttpPort);
        Assert.Equal("127.0.0.1", settings.SocksHost);
        Assert.Equal(8081, settings.SocksPort);
        Assert.True(settings.EnableSocks5);
        Assert.True(settings.EnableSocks5Udp);
        Assert.True(settings.AllowUpstreamProxy);
        Assert.Equal("full", settings.Mode);
        Assert.True(settings.AllowLocalBinding);
    }

    [Fact]
    public void ResolveManagedNetworkSettings_ShouldApplyRequirementsOverridesWithoutResettingUnconstrainedValues()
    {
        var settings = ResolveManagedNetworkSettingsForTest(
            userConfigToml: """
default_permissions = "trusted"

[permissions.trusted.network]
enabled = false
proxy_url = "http://203.0.113.10:9999"
socks_url = "203.0.113.10:9998"
enable_socks5_udp = false
allow_upstream_proxy = true
allowed_domains = ["example.com"]
allow_local_binding = true
""",
            requirementsToml: """
[experimental_network]
enabled = true
http_port = 19000
socks_port = 19001
allow_upstream_proxy = false
allowed_domains = ["api.openai.com"]
allow_local_binding = false
""");

        Assert.True(settings.Enabled);
        Assert.Equal("127.0.0.1", settings.HttpHost);
        Assert.Equal(19000, settings.HttpPort);
        Assert.Equal("127.0.0.1", settings.SocksHost);
        Assert.Equal(19001, settings.SocksPort);
        Assert.False(settings.EnableSocks5Udp);
        Assert.False(settings.AllowUpstreamProxy);
        Assert.Equal(["api.openai.com"], settings.AllowedDomains);
        Assert.False(settings.AllowLocalBinding);
    }

    [Fact]
    public void ResolveManagedNetworkSettings_ShouldRejectDangerouslyAllowAllUnixSocketsWhenRequirementsOnlyAllowSpecificSockets()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ResolveManagedNetworkSettingsForTest(
                userConfigToml: """
default_permissions = "trusted"

[permissions.trusted.network]
enabled = true
dangerously_allow_all_unix_sockets = true
""",
                requirementsToml: """
[experimental_network]
enabled = true
allow_unix_sockets = ["/tmp/tianshu.sock"]
"""));

        Assert.Contains("network.dangerously_allow_all_unix_sockets", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveManagedNetworkSettings_ShouldRejectRelativeUnixSocketAllowlistEntries()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ResolveManagedNetworkSettingsForTest(
                userConfigToml: """
default_permissions = "trusted"

[permissions.trusted.network]
enabled = true
allow_unix_sockets = ["relative.sock"]
""",
                requirementsToml: """
[experimental_network]
enabled = true
"""));

        Assert.Contains("invalid network.allow_unix_sockets[0]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveManagedNetworkSettings_ShouldRejectInvalidProxyUrl()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ResolveManagedNetworkSettingsForTest(
                userConfigToml: """
default_permissions = "trusted"

[permissions.trusted.network]
enabled = true
proxy_url = "http://"
""",
                requirementsToml: """
[experimental_network]
enabled = true
"""));

        Assert.Contains("invalid network.proxy_url", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveManagedNetworkSettings_ShouldRejectGlobalWildcardAllowedDomains()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ResolveManagedNetworkSettingsForTest(
                userConfigToml: """
default_permissions = "trusted"

[permissions.trusted.network]
enabled = true
allowed_domains = ["*"]
""",
                requirementsToml: """
[experimental_network]
enabled = true
"""));

        Assert.Contains("network.allowed_domains", error.Message, StringComparison.Ordinal);
        Assert.Contains("scoped wildcards", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BeginManagedNetworkExecutionAsync_ShouldSkipProxyWhenSandboxPolicyAlreadyEnablesNetwork()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            File.WriteAllText(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
default_permissions = "trusted"

[permissions.trusted.filesystem]
":project_roots" = "read"

[permissions.trusted.network]
enabled = true
""");
            File.WriteAllText(
                Path.Combine(tianShuHome, "requirements.toml"),
                """
[experimental_network]
enabled = true
allowed_domains = ["example.com"]
""");

            threadStore = new KernelThreadStore(storePath);
            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore);
            var request = new KernelManagedNetworkExecutionRequest(
                ThreadId: "thread_network_enabled_001",
                TurnId: "turn_network_enabled_001",
                ItemId: "item_network_enabled_001",
                Command: "curl https://example.com",
                Cwd: cwd,
                SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "externalSandbox", networkAccess = "enabled" }),
                SandboxMode: "externalSandbox",
                ApprovalPolicy: "never");
            var method = typeof(AppHostServer).GetMethod(
                "BeginManagedNetworkExecutionAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            try
            {
                var invokeResult = method!.Invoke(server, [request, CancellationToken.None]);
                var leaseTask = Assert.IsAssignableFrom<Task<KernelManagedNetworkExecutionLease>>(invokeResult);
                await using var lease = await leaseTask;
                Assert.False(lease.IsActive);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldAbortTurnWhenAfterToolHookRequestsAbort()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_inline_tool_hook_abort_001";
        var repoRoot = Path.Combine(root, "repo");
        var notePath = Path.Combine(repoRoot, "note.txt");

        try
        {
            Directory.CreateDirectory(repoRoot);
            await File.WriteAllTextAsync(notePath, "hook-abort-check");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var filePathJson = notePath.Replace('\\', '/');
            var toolArgsJson = JsonSerializer.Serialize(new
            {
                file_path = filePathJson,
            });
            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new
                        {
                            text = $"/tool read_file {toolArgsJson}",
                        },
                    },
                },
            });
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(
                reader,
                writer,
                threadStore,
                toolExecutionHooks:
                [
                    new AbortAfterToolExecutionHook("abort_after_tool_use_test", "hook requested abort"),
                ]);

            await server.RunAsync(CancellationToken.None);
            await WaitForWriterContainsAsync(writer, "\"method\":\"turn/completed\"", TimeSpan.FromSeconds(5));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = Assert.Single(messages.Where(static x => IsNotificationMethod(x.RootElement, "error")))
                    .RootElement
                    .GetProperty("params");
                Assert.Contains(
                    "after_tool_use hook 'abort_after_tool_use_test' failed and aborted operation: hook requested abort",
                    error.GetProperty("message").GetString(),
                    StringComparison.Ordinal);

                var turnCompleted = Assert.Single(messages.Where(static x => IsNotificationMethod(x.RootElement, "turn/completed")))
                    .RootElement
                    .GetProperty("params")
                    .GetProperty("turn");
                Assert.Equal("failed", turnCompleted.GetProperty("status").GetString());
                var turnError = turnCompleted.GetProperty("error");
                Assert.Contains(
                    "after_tool_use hook 'abort_after_tool_use_test' failed and aborted operation: hook requested abort",
                    turnError.GetProperty("message").GetString(),
                    StringComparison.Ordinal);

                var toolCalls = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/tool/call"))
                    .Select(static x => x.RootElement.GetProperty("params").GetProperty("item"))
                    .ToArray();
                Assert.Single(toolCalls);
                Assert.Equal("inProgress", toolCalls[0].GetProperty("status").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldExecuteInlineListDirToolCallWithRelativePathAgainstThreadCwd()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_inline_tool_list_dir_relative_001";
        var repoRoot = Path.Combine(root, "repo");
        var nestedDir = Path.Combine(repoRoot, "nested");
        var deeperDir = Path.Combine(nestedDir, "deeper");
        var entryFile = Path.Combine(repoRoot, "entry.txt");
        var childFile = Path.Combine(nestedDir, "child.txt");
        var grandchildFile = Path.Combine(deeperDir, "grandchild.txt");

        try
        {
            Directory.CreateDirectory(deeperDir);
            await File.WriteAllTextAsync(entryFile, "root-entry");
            await File.WriteAllTextAsync(childFile, "child-entry");
            await File.WriteAllTextAsync(grandchildFile, "grandchild-entry");

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var toolArgsJson = JsonSerializer.Serialize(new
            {
                dir_path = ".",
                offset = 1,
                limit = 50,
                depth = 3,
            });
            var input = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new
                        {
                            text = $"/tool list_dir {toolArgsJson}",
                        },
                    },
                },
            });
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await Task.Delay(600);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var toolCalls = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/tool/call"))
                    .Select(static x => x.RootElement.GetProperty("params").GetProperty("item"))
                    .ToArray();
                Assert.True(toolCalls.Length >= 2);

                var completedCall = toolCalls.Last();
                Assert.Equal("list_dir", completedCall.GetProperty("toolName").GetString());
                Assert.Equal("completed", completedCall.GetProperty("status").GetString());

                var output = completedCall.GetProperty("output").GetString();
                Assert.NotNull(output);
                Assert.Contains("entry.txt", output, StringComparison.Ordinal);
                Assert.Contains("nested/", output, StringComparison.Ordinal);
                Assert.Contains("  child.txt", output, StringComparison.Ordinal);
                Assert.Contains("    grandchild.txt", output, StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task AppServerProcessHostedWithRedirectedStdin_ShouldReturnGitDiffForDeprecatedGitDiffToRemote()
    {
        var root = CreateTempDirectory();
        var repoRoot = Path.Combine(root, "repo");
        var homeRoot = Path.Combine(root, "home");
        var tianShuHome = Path.Combine(homeRoot, ".tianshu");
        var appHostProjectPath = Path.Combine(FindRepositoryRoot(), "src", "Hosting", "TianShu.AppHost", "TianShu.AppHost.csproj");

        try
        {
            Directory.CreateDirectory(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            await InitializeGitRepositoryAsync(repoRoot);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = repoRoot,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add("run");
            process.StartInfo.ArgumentList.Add("--project");
            process.StartInfo.ArgumentList.Add(appHostProjectPath);
            process.StartInfo.ArgumentList.Add("--");
            process.StartInfo.ArgumentList.Add("app-server");
            process.StartInfo.ArgumentList.Add("--listen");
            process.StartInfo.ArgumentList.Add("stdio://");
            process.StartInfo.Environment["HOME"] = homeRoot;
            process.StartInfo.Environment["USERPROFILE"] = homeRoot;
            process.StartInfo.Environment["TIANSHU_HOME"] = tianShuHome;

            process.Start();
            var stderrTask = process.StandardError.ReadToEndAsync();
            try
            {
                await WriteProcessLineAsync(process.StandardInput, """{"id":1,"method":"initialize","params":{}}""");
                using var initializeResponse = await ReadProcessJsonResponseAsync(process.StandardOutput, 1, TimeSpan.FromSeconds(30));
                Assert.True(initializeResponse.RootElement.TryGetProperty("result", out _));

                await WriteProcessLineAsync(process.StandardInput, """{"method":"initialized"}""");
                await WriteProcessLineAsync(
                    process.StandardInput,
                    JsonSerializer.Serialize(new
                    {
                        id = 2,
                        method = "thread/start",
                        @params = new
                        {
                            cwd = repoRoot,
                        },
                    }));
                using var threadStartResponse = await ReadProcessJsonResponseAsync(process.StandardOutput, 2, TimeSpan.FromSeconds(30));
                var threadId = threadStartResponse.RootElement
                    .GetProperty("result")
                    .GetProperty("thread")
                    .GetProperty("id")
                    .GetString();
                Assert.False(string.IsNullOrWhiteSpace(threadId));

                await WriteProcessLineAsync(
                    process.StandardInput,
                    JsonSerializer.Serialize(new
                    {
                        id = 3,
                        method = "artifact/gitdifftoremote/read",
                        @params = new
                        {
                            threadId,
                        },
                    }));
                using var gitDiffResponse = await ReadProcessJsonResponseAsync(process.StandardOutput, 3, TimeSpan.FromSeconds(30));
                var result = gitDiffResponse.RootElement.GetProperty("result");
                Assert.True(result.GetProperty("hasChanges").GetBoolean());
                Assert.Contains("diff --git", result.GetProperty("diff").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }

                _ = await stderrTask;
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectDirectExecWithoutThreadId()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var input = """{"jsonrpc":"2.0","id":1,"method":"exec","params":{"input":"text(\"hi\");"}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                Assert.Equal("threadId 不能为空。", error.GetProperty("message").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectDirectExecWaitWithoutCellId()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var input = """{"jsonrpc":"2.0","id":1,"method":"exec_wait","params":{"threadId":"thread_wait_missing_cell_001"}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                Assert.Equal("cellId 不能为空。", error.GetProperty("message").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectDirectExecWhenThreadDoesNotExist()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var input = """{"jsonrpc":"2.0","id":1,"method":"exec","params":{"threadId":"thread_missing_exec_001","input":"text(\"hi\");"}}""";

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var messages = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var error = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("error");
                Assert.Equal(-32004, error.GetProperty("code").GetInt32());
                Assert.Equal("线程不存在：thread_missing_exec_001", error.GetProperty("message").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DirectCodeModeHandlers_ShouldReturnRunningThenCompletedLifecycle()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, ".tianshu");
        const string threadId = "thread_direct_code_mode_001";
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        AppHostServer? server = null;
        KernelThreadStore? setupStore = null;

        try
        {
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [features]
                code_mode = true
                """);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

            setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var writer = new StringWriter();
            server = new AppHostServer(
                new StringReader(string.Empty),
                writer,
                setupStore);
            var codeModeProtocolRuntime = GetPrivateField<KernelCodeModeProtocolAppHostRuntime>(server, "codeModeProtocolAppHostRuntime");

            await InvokeJsonRpcHandlerAsync(
                codeModeProtocolRuntime,
                "HandleCodeModeExecAsync",
                1,
                JsonSerializer.Serialize(new
                {
                    threadId,
                    input = """
                            text("phase 1");
                            yield_control();
                            text("phase 2");
                            """,
                }));

            var firstMessages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            string? cellId = null;
            try
            {
                var firstResponse = firstMessages.Single(x => IsResponseId(x.RootElement, 1)).RootElement;
                var firstFailure = firstResponse.TryGetProperty("error", out var firstError)
                    ? firstError.GetRawText()
                    : firstResponse.GetRawText();
                Assert.True(firstResponse.TryGetProperty("result", out var firstResult), firstFailure);
                Assert.True(firstResult.GetProperty("success").GetBoolean());
                Assert.Equal("running", firstResult.GetProperty("status").GetString());
                Assert.Equal(threadId, firstResult.GetProperty("threadId").GetString());
                cellId = Assert.IsType<string>(firstResult.GetProperty("cellId").GetString());
                Assert.False(string.IsNullOrWhiteSpace(cellId));
                Assert.Contains("Script running with cell ID ", firstResult.GetProperty("output").GetString(), StringComparison.Ordinal);
                Assert.Contains("phase 1", firstResult.GetProperty("output").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in firstMessages)
                {
                    message.Dispose();
                }
            }

            await InvokeJsonRpcHandlerAsync(
                codeModeProtocolRuntime,
                "HandleCodeModeWaitAsync",
                2,
                JsonSerializer.Serialize(new
                {
                    threadId,
                    cellId = cellId!,
                    yieldTimeMs = KernelCodeModeManager.DefaultWaitYieldTimeMs,
                }));

            var secondMessages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var secondResponse = secondMessages.Single(x => IsResponseId(x.RootElement, 2)).RootElement;
                var secondFailure = secondResponse.TryGetProperty("error", out var secondError)
                    ? secondError.GetRawText()
                    : secondResponse.GetRawText();
                Assert.True(secondResponse.TryGetProperty("result", out var secondResult), secondFailure);
                Assert.True(secondResult.GetProperty("success").GetBoolean());
                Assert.Equal("completed", secondResult.GetProperty("status").GetString());
                Assert.Equal(threadId, secondResult.GetProperty("threadId").GetString());
                Assert.Equal(cellId, secondResult.GetProperty("cellId").GetString());
                Assert.Contains("Script completed", secondResult.GetProperty("output").GetString(), StringComparison.Ordinal);
                Assert.Contains("phase 2", secondResult.GetProperty("output").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in secondMessages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (server is not null)
            {
                await InvokePrivateTaskMethodAsync(server, "DisposeAllCodeModeManagersAsync");
            }

            if (setupStore is not null)
            {
                await setupStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    private static async Task InvokeJsonRpcHandlerAsync(
        object target,
        string methodName,
        int requestId,
        string paramsJson)
    {
        var method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(method);

        using var idDocument = JsonDocument.Parse(requestId.ToString());
        using var paramsDocument = JsonDocument.Parse(paramsJson);

        try
        {
            var invokeResult = method!.Invoke(target, [idDocument.RootElement.Clone(), paramsDocument.RootElement.Clone(), CancellationToken.None]);
            var task = Assert.IsAssignableFrom<Task>(invokeResult);
            await task;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static async Task InvokePrivateTaskMethodAsync(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        try
        {
            var invokeResult = method!.Invoke(instance, []);
            var task = Assert.IsAssignableFrom<Task>(invokeResult);
            await task;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static KernelManagedNetworkSettings ResolveManagedNetworkSettingsForTest(
        string? userConfigToml,
        string? requirementsToml,
        string? cwd = null)
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            if (userConfigToml is not null)
            {
                File.WriteAllText(Path.Combine(tianShuHome, "tianshu.toml"), userConfigToml);
            }

            if (requirementsToml is not null)
            {
                File.WriteAllText(Path.Combine(tianShuHome, "requirements.toml"), requirementsToml);
            }

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                new KernelThreadStore(storePath));
            var method = typeof(AppHostServer).GetMethod(
                "ResolveManagedNetworkSettings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            try
            {
                return Assert.IsType<KernelManagedNetworkSettings>(method!.Invoke(server, [cwd]));
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    private static bool IsResponseId(JsonElement json, int id)
    {
        if (!json.TryGetProperty("id", out var idElement))
        {
            return false;
        }

        return idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt32(out var value) && value == id;
    }

    private static async Task<KeyValuePair<long, TaskCompletionSource<JsonElement>>> WaitForSinglePendingServerRequestAsync(
        ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow <= deadline)
        {
            if (pending.Count == 1)
            {
                return pending.Single();
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("在指定时间内未观察到 pending server request。");
    }

    private static async Task<KeyValuePair<long, TaskCompletionSource<JsonElement>>> WaitForPendingServerRequestAsync(
        ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending,
        Func<KeyValuePair<long, TaskCompletionSource<JsonElement>>, bool> predicate,
        TimeSpan timeout,
        long? excludeRequestId = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow <= deadline)
        {
            foreach (var pendingRequest in pending)
            {
                if (excludeRequestId.HasValue && pendingRequest.Key == excludeRequestId.Value)
                {
                    continue;
                }

                if (predicate(pendingRequest))
                {
                    return pendingRequest;
                }
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("在指定时间内未观察到匹配的 pending server request。");
    }

    private static bool IsNotificationMethod(JsonElement json, string method)
    {
        if (!json.TryGetProperty("method", out var methodElement))
        {
            return false;
        }

        return methodElement.ValueKind == JsonValueKind.String
               && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal);
    }

    private static bool IsResponsePayload(JsonElement json, int id)
        => IsResponseId(json, id)
           && !json.TryGetProperty("method", out _)
           && json.TryGetProperty("result", out _);

    private static bool IsRequestMethod(JsonElement json, string method)
    {
        if (!json.TryGetProperty("method", out var methodElement)
            || !json.TryGetProperty("id", out _))
        {
            return false;
        }

        return methodElement.ValueKind == JsonValueKind.String
               && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal);
    }

    private static bool IsTurnCompletedWithStatus(JsonElement json, string status)
    {
        if (!json.TryGetProperty("method", out var methodElement)
            || methodElement.ValueKind != JsonValueKind.String
            || !string.Equals(methodElement.GetString(), "turn/completed", StringComparison.Ordinal))
        {
            return false;
        }

        if (!json.TryGetProperty("params", out var @params)
            || !@params.TryGetProperty("turn", out var turn))
        {
            return false;
        }

        return string.Equals(turn.GetProperty("status").GetString(), status, StringComparison.Ordinal);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(instance);
        Assert.NotNull(value);
        return Assert.IsAssignableFrom<T>(value);
    }

    private static bool WaitForProcessExit(int pid, TimeSpan timeout)
    {
        if (pid <= 0)
        {
            return true;
        }

        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (!IsProcessRunning(pid))
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return !IsProcessRunning(pid);
    }

    private static void RunGitCommand(string cwd, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} 失败：{stderr}{stdout}");
        }
    }

    private static async Task WaitForWriterContainsAsync(StringWriter writer, string expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (writer.ToString().Contains(expected, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(100);
        }
    }

    private static async Task<string> WaitForJsonRpcResponseIdAsync(
        ChannelReader<string> lines,
        long id,
        TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        while (await lines.WaitToReadAsync(timeoutCts.Token))
        {
            while (lines.TryRead(out var line))
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("id", out var idElement)
                    && idElement.ValueKind == JsonValueKind.Number
                    && idElement.TryGetInt64(out var parsed)
                    && parsed == id)
                {
                    return line;
                }
            }
        }

        throw new TimeoutException($"未等到 id={id} 的 JSON-RPC response。");
    }

    private static void AssertLooksLikeVersion7Guid(string? value)
    {
        Assert.False(string.IsNullOrWhiteSpace(value));
        Assert.True(Guid.TryParse(value, out _), $"期望 UUID，实际值：{value}");
        Assert.Equal('7', value![14]);
    }

    private static void AssertThreadHasSingleConversationTurn(JsonElement thread, string expectedUserText, string expectedAssistantText)
    {
        var turns = thread.GetProperty("turns");
        Assert.Equal(1, turns.GetArrayLength());

        var items = turns[0].GetProperty("items").EnumerateArray().ToArray();
        var userMessage = Assert.Single(items.Where(static item => item.GetProperty("type").GetString() == "userMessage"));
        Assert.Equal(expectedUserText, userMessage.GetProperty("content")[0].GetProperty("text").GetString());

        var assistantMessage = Assert.Single(items.Where(static item => item.GetProperty("type").GetString() == "agentMessage"));
        Assert.Equal(expectedAssistantText, assistantMessage.GetProperty("text").GetString());
    }

    private static async Task<KernelThreadStore> CreateMaterializedThreadWithTurnsAsync(
        string storePath,
        string threadId,
        string cwd,
        params (string TurnId, string UserMessage, string AssistantMessage)[] turns)
    {
        Directory.CreateDirectory(cwd);

        var threadStore = new KernelThreadStore(storePath);
        await threadStore.InitializeAsync(CancellationToken.None);

        var record = await threadStore.CreateThreadAsync(threadId, cwd, CancellationToken.None);
        var snapshot = BuildTestThreadConfigSnapshot(cwd);
        record.ConfigSnapshot = snapshot.DeepClone();
        record = await threadStore.UpsertThreadAsync(record, CancellationToken.None);
        await threadStore.RolloutRecorder.EnsureSessionMetaAsync(
            threadId,
            KernelRolloutStateMapper.ToRolloutThreadRecord(record, snapshot),
            CancellationToken.None);

        foreach (var (turnId, userMessage, assistantMessage) in turns)
        {
            record = await threadStore.AppendCompletedTurnAsync(
                         threadId,
                         turnId,
                         userMessage,
                         assistantMessage,
                         "completed",
                         CancellationToken.None)
                     ?? throw new InvalidOperationException($"线程不存在：{threadId}");
            await threadStore.RolloutRecorder.AppendTurnResultAsync(
                threadId,
                turnId,
                "completed",
                userMessage,
                assistantMessage,
                CancellationToken.None).ConfigureAwait(false);
        }

        return threadStore;
    }

    private static async Task RewriteRolloutToSingleTurnSnapshotAsync(KernelThreadStore threadStore, string threadId)
    {
        var record = await threadStore.GetThreadAsync(threadId, CancellationToken.None)
                     ?? throw new InvalidOperationException($"线程不存在：{threadId}");
        record.Turns = record.Turns.Take(1).ToList();
        var last = record.Turns.LastOrDefault();
        record.LastUserMessage = last?.UserMessage;
        record.LastAssistantMessage = last?.AssistantMessage;
        record.PendingInputState = null;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await threadStore.RolloutRecorder.RewriteThreadSnapshotAsync(
            KernelRolloutStateMapper.ToRolloutThreadRecord(record),
            CancellationToken.None);
    }

    private static async Task MaterializeThreadRolloutAsync(KernelThreadStore threadStore, string threadId)
    {
        var record = await threadStore.GetThreadAsync(threadId, CancellationToken.None)
                     ?? throw new InvalidOperationException($"线程不存在：{threadId}");
        var snapshot = record.ConfigSnapshot;
        if (snapshot is null)
        {
            snapshot = BuildTestThreadConfigSnapshot(record.Cwd ?? Environment.CurrentDirectory);
            record.ConfigSnapshot = snapshot.DeepClone();
            record = await threadStore.UpsertThreadAsync(record, CancellationToken.None);
        }

        await threadStore.RolloutRecorder.EnsureSessionMetaAsync(
            threadId,
            KernelRolloutStateMapper.ToRolloutThreadRecord(record, snapshot),
            CancellationToken.None);
        await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
    }

    private static KernelThreadConfigSnapshot BuildTestThreadConfigSnapshot(string cwd)
    {
        var session = new KernelThreadSessionState(
            Model: "gpt-5",
            ModelProvider: "openai",
            ServiceTier: null,
            Cwd: cwd,
            ApprovalPolicy: KernelApprovalPolicy.OnRequest,
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly" }),
            SandboxMode: "readOnly");
        return KernelThreadConfigSnapshotFactory.FromSession(session);
    }

    private static async Task<(AppHostServer Server, StringWriter Writer, Task RunTask, KeyValuePair<long, TaskCompletionSource<JsonElement>> PendingRequest, KernelThreadStore ThreadStore)> StartPendingWriteToolTurnAsync(
        string storePath,
        string threadId,
        string repoRoot,
        string model,
        string cwd,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(repoRoot);

        var threadStore = new KernelThreadStore(storePath);
        await threadStore.InitializeAsync(cancellationToken);
        _ = await threadStore.CreateThreadAsync(threadId, repoRoot, cancellationToken);
        await MaterializeThreadRolloutAsync(threadStore, threadId);

        var reader = new StringReader(string.Empty);
        var writer = new StringWriter();
        var server = new AppHostServer(reader, writer, threadStore);

        await InvokeHandleThreadResumeAsync(
            server,
            1,
            $$"""
            {
              "threadId": "{{threadId}}",
              "model": "{{model}}",
              "cwd": "{{cwd}}",
              "approvalPolicy": "on-request"
            }
            """);

        var toolArgsJson = JsonSerializer.Serialize(new
        {
            path = "pending.txt",
            content = "pending",
        });
        var turnStartMethod = typeof(AppHostServer).GetMethod("HandleTurnStartAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(turnStartMethod);
        using var turnStartIdDocument = JsonDocument.Parse("2");
        using var turnStartParamsDocument = JsonDocument.Parse(
            $$"""
            {
              "threadId": "{{threadId}}",
              "approvalPolicy": "on-request",
              "sandboxPolicy": {
                "type": "readOnly"
              },
              "input": [
                {
                  "text": {{JsonSerializer.Serialize($"/tool write {toolArgsJson}")}}
                }
              ]
            }
            """);
        var runTask = Assert.IsAssignableFrom<Task>(turnStartMethod!.Invoke(server, new object?[]
        {
            turnStartIdDocument.RootElement.Clone(),
            turnStartParamsDocument.RootElement.Clone(),
            cancellationToken,
        }));

        var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
        KeyValuePair<long, TaskCompletionSource<JsonElement>> pendingRequest;
        try
        {
            pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"未观察到 pending server request。writer={writer}", ex);
        }

        return (server, writer, runTask, pendingRequest, threadStore);
    }

    private static async Task InvokeHandleThreadResumeAsync(AppHostServer server, int id, string @params)
    {
        var resumeMethod = typeof(AppHostServer).GetMethod("HandleThreadResumeAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(resumeMethod);

        using var idDocument = JsonDocument.Parse(id.ToString());
        using var paramsDocument = JsonDocument.Parse(@params);
        var resumeTask = Assert.IsAssignableFrom<Task>(resumeMethod!.Invoke(server, new object?[]
        {
            idDocument.RootElement.Clone(),
            paramsDocument.RootElement.Clone(),
            CancellationToken.None,
        }));
        await resumeTask;
    }

    private static async Task InvokeHandleTurnInterruptAsync(AppHostServer server, int id, string @params)
    {
        var interruptMethod = typeof(AppHostServer).GetMethod("HandleTurnInterruptAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(interruptMethod);

        using var idDocument = JsonDocument.Parse(id.ToString());
        using var paramsDocument = JsonDocument.Parse(@params);
        var interruptTask = Assert.IsAssignableFrom<Task>(interruptMethod!.Invoke(server, new object?[]
        {
            idDocument.RootElement.Clone(),
            paramsDocument.RootElement.Clone(),
            CancellationToken.None,
        }));
        await interruptTask;
    }

    private static async Task InvokeHandleThreadArchiveAsync(AppHostServer server, int id, string @params)
    {
        var runtime = GetPrivateField<object>(server, "threadLifecycleAppHostRuntime");
        var archiveMethod = runtime.GetType().GetMethod("HandleThreadArchiveAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(archiveMethod);

        using var idDocument = JsonDocument.Parse(id.ToString());
        using var paramsDocument = JsonDocument.Parse(@params);
        var archiveTask = Assert.IsAssignableFrom<Task>(archiveMethod!.Invoke(runtime, new object?[]
        {
            idDocument.RootElement.Clone(),
            paramsDocument.RootElement.Clone(),
            CancellationToken.None,
        }));
        await archiveTask;
    }

    private static async Task InvokeHandleThreadRollbackAsync(AppHostServer server, JsonElement id, JsonElement @params)
    {
        var runtime = GetPrivateField<object>(server, "threadLifecycleAppHostRuntime");
        var rollbackMethod = runtime.GetType().GetMethod("HandleThreadRollbackAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(rollbackMethod);

        var rollbackTask = Assert.IsAssignableFrom<Task>(rollbackMethod!.Invoke(runtime, new object?[]
        {
            id,
            @params,
            CancellationToken.None,
        }));
        await rollbackTask;
    }

    private static async Task InvokeHandleThreadUnsubscribeAsync(AppHostServer server, int id, string @params)
    {
        var runtime = GetPrivateField<object>(server, "threadLifecycleAppHostRuntime");
        var unsubscribeMethod = runtime.GetType().GetMethod("HandleThreadUnsubscribeAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(unsubscribeMethod);

        using var idDocument = JsonDocument.Parse(id.ToString());
        using var paramsDocument = JsonDocument.Parse(@params);
        var unsubscribeTask = Assert.IsAssignableFrom<Task>(unsubscribeMethod!.Invoke(runtime, new object?[]
        {
            idDocument.RootElement.Clone(),
            paramsDocument.RootElement.Clone(),
            CancellationToken.None,
        }));
        await unsubscribeTask;
    }

    private static bool IsProcessRunning(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryKillProcessById(int pid)
    {
        if (pid <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task WriteSkillFixtureAsync(string root, string name)
    {
        var skillPath = Path.Combine(root, "skills", name, "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(skillPath)!);
        await File.WriteAllTextAsync(
            skillPath,
            $$"""
            ---
            name: {{name}}
            description: {{name}} description
            ---

            # Body
            """);
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                ResetReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 11)
            {
                Thread.Sleep(150 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 11)
            {
                Thread.Sleep(150 * (attempt + 1));
            }
        }

        ResetReadOnlyAttributes(path);
        Directory.Delete(path, recursive: true);
    }

    private static void ResetReadOnlyAttributes(string path)
    {
        foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(directory);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(directory, attrs & ~FileAttributes.ReadOnly);
            }
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
        }
    }


    private static object ResolveConfiguredPermissionSettingsForTest(
        string? userConfigToml,
        string? cwd,
        out string tianShuHome)
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        tianShuHome = Path.Combine(root, "tianshu-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            if (cwd is not null)
            {
                Directory.CreateDirectory(cwd);
            }

            if (userConfigToml is not null)
            {
                File.WriteAllText(Path.Combine(tianShuHome, "tianshu.toml"), userConfigToml);
            }

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                new KernelThreadStore(storePath));
            var method = typeof(AppHostServer).GetMethod(
                "ResolveConfiguredPermissionSettings",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(string)],
                modifiers: null);
            Assert.NotNull(method);

            try
            {
                return method!.Invoke(server, [cwd])!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    private static JsonElement GetResolvedPermissionSandboxPolicy(object settings)
    {
        var property = settings.GetType().GetProperty("SandboxPolicy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return ((JsonElement)property!.GetValue(settings)!).Clone();
    }

    private static KernelApprovalPolicy GetResolvedPermissionApprovalPolicy(object settings)
    {
        var property = settings.GetType().GetProperty("ApprovalPolicy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return Assert.IsType<KernelApprovalPolicy>(property!.GetValue(settings));
    }

    private static bool GetResolvedPermissionAllowLoginShell(object settings)
    {
        var property = settings.GetType().GetProperty("AllowLoginShell", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return Assert.IsType<bool>(property!.GetValue(settings));
    }

    private static KernelShellEnvironmentPolicy GetResolvedPermissionShellEnvironmentPolicy(object settings)
    {
        var property = settings.GetType().GetProperty("ShellEnvironmentPolicy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return Assert.IsType<KernelShellEnvironmentPolicy>(property!.GetValue(settings));
    }

    private static string GetResolvedPermissionSandboxMode(object settings)
    {
        var property = settings.GetType().GetProperty("SandboxMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return (string)property!.GetValue(settings)!;
    }

    private static KernelThreadConfigSnapshot ResolveConfiguredThreadDefaultsSnapshotForTest(
        string? userConfigToml,
        string? cwd,
        out string tianShuHome)
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        tianShuHome = Path.Combine(root, "tianshu-home");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            if (cwd is not null)
            {
                Directory.CreateDirectory(cwd);
            }

            if (userConfigToml is not null)
            {
                File.WriteAllText(Path.Combine(tianShuHome, "tianshu.toml"), userConfigToml);
            }

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                new KernelThreadStore(storePath));
            var method = typeof(AppHostServer).GetMethod(
                "ResolveConfiguredThreadDefaultsWithThreadError",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(string)],
                modifiers: null);
            Assert.NotNull(method);

            try
            {
                var result = method!.Invoke(server, [cwd]);
                Assert.NotNull(result);

                var property = result!.GetType().GetProperty("ConfigSnapshot", BindingFlags.Instance | BindingFlags.Public);
                Assert.NotNull(property);
                return Assert.IsType<KernelThreadConfigSnapshot>(property!.GetValue(result));
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ExtractFunctionCalls_WhenToolSearchAndNamespacedFunctionItemsArrive_ParsesThem()
    {
        using var outputItems = JsonDocument.Parse("""
            [
              {
                "type": "function_call",
                "call_id": "call-calendar-1",
                "name": "create_event",
                "namespace": "mcp__codex_apps__calendar",
                "arguments": "{\"title\":\"设计评审\"}"
              },
              {
                "type": "tool_search_call",
                "call_id": "search-tools-1",
                "execution": "client",
                "arguments": {
                  "query": "calendar event",
                  "limit": 1
                }
              }
            ]
            """);

        var calls = ExtractModelFunctionCalls(outputItems.RootElement);
        Assert.Equal(2, calls.Length);

        Assert.Equal("create_event", calls[0].Name);
        Assert.Equal("mcp__codex_apps__calendar", calls[0].Namespace);
        Assert.False(calls[0].IsToolSearch);

        Assert.Equal("tool_search", calls[1].Name);
        Assert.Equal("search-tools-1", calls[1].CallId);
        Assert.True(calls[1].IsToolSearch);
    }

    [Fact]
    public async Task ExecuteModelFunctionCallAsync_WhenToolSearchCallArrives_ReturnsToolSearchOutput()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore);

            using var outputItems = JsonDocument.Parse("""
                [
                  {
                    "type": "tool_search_call",
                    "call_id": "search-tools-1",
                    "execution": "client",
                    "arguments": {
                      "query": "calendar event",
                      "limit": 1
                    }
                  }
                ]
                """);
            var call = ExtractModelFunctionCalls(outputItems.RootElement).Single();
            var state = CreateTurnOperationState("thread-search-1", "turn-search-1", "item-search-1", "reason-search-1", "search tools");
            var dynamicTools = KernelDynamicToolResolver.Parse(JsonSerializer.SerializeToElement(new object[]
            {
                new
                {
                    name = "mcp__codex_apps__calendar__create_event",
                    server = "dynamic",
                    connectorName = "Calendar",
                    connectorDescription = "Calendar tools.",
                    description = "Create a calendar event.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            title = new { type = "string" },
                        },
                    },
                },
            }));
            var context = new TurnRequestContext(
                Model: null,
                ModelProvider: null,
                ServiceTier: null,
                ApprovalPolicy: null,
                SandboxPolicy: null,
                SandboxMode: null,
                Cwd: root,
                DynamicTools: dynamicTools);

            var runtime = GetPrivateField<KernelModelFunctionToolCallRuntime>(server, "modelFunctionToolCallRuntime");
            var result = await runtime.ExecuteAsync(call, state, context, CancellationToken.None);

            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.Equal("tool_search_output", json.RootElement.GetProperty("type").GetString());
            Assert.Equal("search-tools-1", json.RootElement.GetProperty("call_id").GetString());
            Assert.Equal("completed", json.RootElement.GetProperty("status").GetString());
            Assert.Equal("client", json.RootElement.GetProperty("execution").GetString());
            Assert.Single(json.RootElement.GetProperty("tools").EnumerateArray());
            Assert.Equal("namespace", json.RootElement.GetProperty("tools")[0].GetProperty("type").GetString());
            Assert.Equal("mcp__codex_apps__calendar", json.RootElement.GetProperty("tools")[0].GetProperty("name").GetString());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveModelFunctionToolName_WhenNamespacePresent_ReturnsDynamicToolFullName()
    {
        using var outputItems = JsonDocument.Parse("""
            [
              {
                "type": "function_call",
                "call_id": "call-calendar-1",
                "name": "create_event",
                "namespace": "mcp__codex_apps__calendar",
                "arguments": "{\"title\":\"设计评审\"}"
              }
            ]
            """);
        using var dynamicToolsDocument = JsonDocument.Parse("""
            [
              {
                "name": "mcp__codex_apps__calendar__create_event",
                "server": "dynamic",
                "connectorName": "Calendar",
                "description": "Create a calendar event.",
                "inputSchema": {
                  "type": "object"
                }
              }
            ]
            """);
        var dynamicTools = KernelDynamicToolResolver.Parse(dynamicToolsDocument.RootElement);

        var call = Assert.IsType<ModelFunctionCall>(ExtractModelFunctionCalls(outputItems.RootElement).Single());
        var resolved = KernelModelFunctionToolCallRuntime.ResolveModelFunctionToolName(call, dynamicTools);

        Assert.Equal("mcp__codex_apps__calendar__create_event", resolved);
    }

    [Fact]
    public async Task ExecuteModelFunctionCallAsync_ShouldPropagateOriginalDynamicToolCallIdToClientLifecycleAndModelOutput()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_model_dynamic_call_id_001";
        const string turnId = "turn_model_dynamic_call_id_001";
        const string callId = "dyn-call-1";

        try
        {
            var dynamicTools = KernelDynamicToolResolver.Parse(JsonSerializer.SerializeToElement(new object[]
            {
                new
                {
                    name = "toolA",
                    description = "demo",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            value = new { type = "string" },
                        },
                    },
                },
            }));

            var threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");

            var runtime = GetPrivateField<KernelModelFunctionToolCallRuntime>(server, "modelFunctionToolCallRuntime");
            var call = new ModelFunctionCall(
                "toolA",
                callId,
                JsonSerializer.Serialize(new { value = "hello" }),
                Input: null,
                IsCustom: false,
                Namespace: null,
                IsToolSearch: false);
            var state = new TurnOperationState(
                threadId,
                turnId,
                "assistant_item_001",
                "reasoning_item_001",
                "run toolA");

            var context = new TurnRequestContext(
                Model: null,
                ModelProvider: null,
                ServiceTier: null,
                ApprovalPolicy: "never",
                SandboxPolicy: null,
                SandboxMode: null,
                Cwd: root,
                DynamicTools: dynamicTools);

            var task = runtime.ExecuteAsync(call, state, context, CancellationToken.None);

            await WaitForWriterContainsAsync(writer, "\"method\":\"item/tool/call\"", TimeSpan.FromSeconds(5));
            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(5));

            var startedMessages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var toolCallRequest = Assert.Single(startedMessages.Where(static x => IsRequestMethod(x.RootElement, "item/tool/call")));
                var toolCallParams = toolCallRequest.RootElement.GetProperty("params");
                Assert.Equal(callId, toolCallParams.GetProperty("callId").GetString());
                Assert.Equal("toolA", toolCallParams.GetProperty("tool").GetString());
                Assert.Equal("hello", toolCallParams.GetProperty("arguments").GetProperty("value").GetString());

                var started = Assert.Single(startedMessages.Where(static x =>
                    IsNotificationMethod(x.RootElement, "item/started")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "dynamicToolCall"));
                var startedItem = started.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal(callId, startedItem.GetProperty("id").GetString());
                Assert.Equal("toolA", startedItem.GetProperty("tool").GetString());
                Assert.Equal("inProgress", startedItem.GetProperty("status").GetString());
            }
            finally
            {
                foreach (var message in startedMessages)
                {
                    message.Dispose();
                }
            }

            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                success = true,
                contentItems = new object[]
                {
                    new { type = "inputText", text = "dynamic-ok" },
                },
            }));

            var result = await task;

            using var resultJson = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.Equal("function_call_output", resultJson.RootElement.GetProperty("type").GetString());
            Assert.Equal(callId, resultJson.RootElement.GetProperty("call_id").GetString());
            Assert.Equal("input_text", resultJson.RootElement.GetProperty("output")[0].GetProperty("type").GetString());
            Assert.Equal("dynamic-ok", resultJson.RootElement.GetProperty("output")[0].GetProperty("text").GetString());

            var completedMessages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var completed = Assert.Single(completedMessages.Where(static x =>
                    IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "dynamicToolCall"));
                var completedItem = completed.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal(callId, completedItem.GetProperty("id").GetString());
                Assert.Equal("completed", completedItem.GetProperty("status").GetString());
                Assert.True(completedItem.GetProperty("success").GetBoolean());
                Assert.Equal("dynamic-ok", completedItem.GetProperty("contentItems")[0].GetProperty("text").GetString());
            }
            finally
            {
                foreach (var message in completedMessages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_ShouldTreatDynamicToolResponseWithoutSuccessAsFailure()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string callId = "dyn-invalid-1";

        try
        {
            var dynamicTools = KernelDynamicToolResolver.Parse(JsonSerializer.SerializeToElement(new object[]
            {
                new
                {
                    name = "toolA",
                    description = "demo",
                    inputSchema = new { type = "object" },
                },
            }));

            var threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");

            var task = server.ExecuteToolCallAsync(
                threadId: "thread_dynamic_invalid_001",
                turnId: "turn_dynamic_invalid_001",
                itemId: "item_dynamic_invalid_001",
                toolName: "toolA",
                arguments: JsonSerializer.SerializeToElement(new { value = "legacy" }),
                context: new TurnRequestContext(
                    Model: null,
                    ModelProvider: null,
                    ServiceTier: null,
                    ApprovalPolicy: "never",
                    SandboxPolicy: null,
                    SandboxMode: null,
                    Cwd: root,
                    DynamicTools: dynamicTools),
                toolCallGate: null,
                cancellationToken: CancellationToken.None,
                externalCallId: callId);

            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(5));
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                contentItems = new object[]
                {
                    new { type = "inputText", text = "legacy-output" },
                },
            }));

            var result = await task;
            Assert.False(result.Success);
            Assert.Equal("dynamic tool response was invalid", result.OutputText);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var toolCallRequest = Assert.Single(messages.Where(static x => IsRequestMethod(x.RootElement, "item/tool/call")));
                Assert.Equal(callId, toolCallRequest.RootElement.GetProperty("params").GetProperty("callId").GetString());

                var completed = Assert.Single(messages.Where(static x =>
                    IsNotificationMethod(x.RootElement, "item/completed")
                    && x.RootElement.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "dynamicToolCall"));
                var completedItem = completed.RootElement.GetProperty("params").GetProperty("item");
                Assert.Equal(callId, completedItem.GetProperty("id").GetString());
                Assert.Equal("failed", completedItem.GetProperty("status").GetString());
                Assert.False(completedItem.GetProperty("success").GetBoolean());
                Assert.Equal("dynamic tool response was invalid", completedItem.GetProperty("contentItems")[0].GetProperty("text").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_ShouldPersistDynamicToolApprovalAndReuseItAcrossExecutions()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var kernelHome = Path.Combine(root, "kernel-home");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var previousCurrentDirectory = Environment.CurrentDirectory;
        var previousKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        var previousTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.CurrentDirectory = root;
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", kernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            await File.WriteAllTextAsync(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
[projects."{{root.Replace("\\", "/")}}"]
trust_level = "trusted"
""");

            var dynamicTools = KernelDynamicToolResolver.Parse(JsonSerializer.SerializeToElement(new object[]
            {
                new
                {
                    name = "mcp__codex_apps__calendar__create_event",
                    server = "dynamic",
                    connectorName = "Calendar",
                    title = "Create Event",
                    description = "Create a calendar event.",
                    inputSchema = new { type = "object", additionalProperties = false },
                    annotations = new
                    {
                        destructive_hint = true,
                        read_only_hint = false,
                        open_world_hint = true,
                    },
                    _meta = new
                    {
                        connector_id = "calendar",
                        connector_name = "Calendar",
                        connector_description = "Calendar connector",
                    },
                },
            }));

            var threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);

            var firstReader = new StringReader(string.Empty);
            var firstWriter = new StringWriter();
            var firstServer = new AppHostServer(firstReader, firstWriter, threadStore);
            var firstPending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(firstServer, "pendingServerResponses");

            var firstTask = firstServer.ExecuteToolCallAsync(
                threadId: "thread_dynamic_approval_001",
                turnId: "turn_dynamic_approval_001",
                itemId: "item_dynamic_approval_001",
                toolName: "mcp__codex_apps__calendar__create_event",
                arguments: JsonSerializer.SerializeToElement(new
                {
                    title = "设计评审",
                }),
                context: new TurnRequestContext(
                    Model: null,
                    ModelProvider: null,
                    ServiceTier: null,
                    ApprovalPolicy: "on-request",
                    SandboxPolicy: null,
                    SandboxMode: null,
                    Cwd: root,
                    DynamicTools: dynamicTools),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            await WaitForWriterContainsAsync(firstWriter, "\"method\":\"item/tool/requestApproval\"", TimeSpan.FromSeconds(5));
            var approvalPending = await WaitForSinglePendingServerRequestAsync(firstPending, TimeSpan.FromSeconds(5));

            var firstMessages = firstWriter
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var approvalRequest = Assert.Single(firstMessages, static x => IsRequestMethod(x.RootElement, "item/tool/requestApproval"));
                var approvalParams = approvalRequest.RootElement.GetProperty("params");
                Assert.Equal("mcp_tool_call", approvalParams.GetProperty("approvalKind").GetString());
                Assert.Equal("codex_apps", approvalParams.GetProperty("serverName").GetString());

                var availableDecisions = approvalParams.GetProperty("availableDecisions").EnumerateArray().Select(static item => item.GetString()).ToArray();
                Assert.Contains("accept", availableDecisions);
                Assert.Contains("acceptForSession", availableDecisions);
                Assert.Contains("acceptAndRemember", availableDecisions);
                Assert.Contains("decline", availableDecisions);
                Assert.Contains("cancel", availableDecisions);

                var meta = approvalParams.GetProperty("_meta");
                Assert.Equal("calendar", meta.GetProperty("connector_id").GetString());
                Assert.Equal("Calendar", meta.GetProperty("connector_name").GetString());
                Assert.Equal("Calendar connector", meta.GetProperty("connector_description").GetString());
                Assert.Equal("create_event", meta.GetProperty("tool_name").GetString());
                Assert.Equal("mcp__codex_apps__calendar__create_event", meta.GetProperty("tool_full_name").GetString());
                Assert.Equal("Create Event", meta.GetProperty("tool_title").GetString());
                Assert.Equal("Create a calendar event.", meta.GetProperty("tool_description").GetString());
                Assert.Equal("设计评审", meta.GetProperty("tool_params").GetProperty("title").GetString());
            }
            finally
            {
                foreach (var message in firstMessages)
                {
                    message.Dispose();
                }
            }

            approvalPending.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                decision = "acceptAndRemember",
            }));

            var toolPending = await WaitForPendingServerRequestAsync(
                firstPending,
                static pendingRequest => pendingRequest.Key != 0,
                TimeSpan.FromSeconds(5),
                excludeRequestId: approvalPending.Key);
            toolPending.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                success = true,
                contentItems = new object[]
                {
                    new
                    {
                        type = "output_text",
                        text = "calendar ok",
                    },
                },
            }));

            var firstResult = await firstTask;
            Assert.True(firstResult.Success);
            Assert.Equal("calendar ok", firstResult.OutputText);

            var persistedPath = Path.Combine(root, ".tianshu", "tianshu.toml");
            Assert.True(File.Exists(persistedPath));
            var persistedConfig = Toml.ToModel(await File.ReadAllTextAsync(persistedPath, CancellationToken.None)) as TomlTable;
            Assert.NotNull(persistedConfig);
            var apps = Assert.IsType<TomlTable>(persistedConfig!["apps"]);
            var calendar = Assert.IsType<TomlTable>(apps["calendar"]);
            var tools = Assert.IsType<TomlTable>(calendar["tools"]);
            var createEvent = Assert.IsType<TomlTable>(tools["create_event"]);
            Assert.Equal("approve", createEvent["approval_mode"]?.ToString());

            var secondWriter = new StringWriter();
            var secondServer = new AppHostServer(new StringReader(string.Empty), secondWriter, threadStore);
            var secondPending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(secondServer, "pendingServerResponses");
            var secondTask = secondServer.ExecuteToolCallAsync(
                threadId: "thread_dynamic_approval_001",
                turnId: "turn_dynamic_approval_002",
                itemId: "item_dynamic_approval_002",
                toolName: "mcp__codex_apps__calendar__create_event",
                arguments: JsonSerializer.SerializeToElement(new { title = "阶段复盘" }),
                context: new TurnRequestContext(
                    Model: null,
                    ModelProvider: null,
                    ServiceTier: null,
                    ApprovalPolicy: "on-request",
                    SandboxPolicy: null,
                    SandboxMode: null,
                    Cwd: root,
                    DynamicTools: dynamicTools),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            var secondToolPending = await WaitForSinglePendingServerRequestAsync(secondPending, TimeSpan.FromSeconds(5));
            var secondMessages = secondWriter
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                Assert.DoesNotContain(secondMessages, static x => IsRequestMethod(x.RootElement, "item/tool/requestApproval"));
                Assert.Contains(secondMessages, static x => IsRequestMethod(x.RootElement, "item/tool/call"));
            }
            finally
            {
                foreach (var message in secondMessages)
                {
                    message.Dispose();
                }
            }

            secondToolPending.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                success = true,
                contentItems = new object[]
                {
                    new
                    {
                        type = "output_text",
                        text = "calendar persisted",
                    },
                },
            }));

            var secondResult = await secondTask;
            Assert.True(secondResult.Success);
            Assert.Equal("calendar persisted", secondResult.OutputText);
        }
        finally
        {
            Environment.CurrentDirectory = previousCurrentDirectory;
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", previousKernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", previousTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_ShouldDegradeCustomServerAcceptAndRememberToSessionOnly()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var kernelHome = Path.Combine(root, "kernel-home");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var previousKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        var previousTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", kernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

            var dynamicTools = KernelDynamicToolResolver.Parse(JsonSerializer.SerializeToElement(new object[]
            {
                new
                {
                    name = "mcp__custom_server__dangerous_tool",
                    server = "custom_server",
                    description = "Dangerous custom tool.",
                    inputSchema = new { type = "object", additionalProperties = false },
                    annotations = new
                    {
                        destructive_hint = true,
                        read_only_hint = false,
                        open_world_hint = true,
                    },
                },
            }));

            var threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);

            var firstWriter = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), firstWriter, threadStore);
            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");

            var firstTask = server.ExecuteToolCallAsync(
                threadId: "thread_custom_dynamic_001",
                turnId: "turn_custom_dynamic_001",
                itemId: "item_custom_dynamic_001",
                toolName: "mcp__custom_server__dangerous_tool",
                arguments: JsonSerializer.SerializeToElement(new { value = 1 }),
                context: new TurnRequestContext(
                    Model: null,
                    ModelProvider: null,
                    ServiceTier: null,
                    ApprovalPolicy: "on-request",
                    SandboxPolicy: null,
                    SandboxMode: null,
                    Cwd: root,
                    DynamicTools: dynamicTools),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            await WaitForWriterContainsAsync(firstWriter, "\"method\":\"item/tool/requestApproval\"", TimeSpan.FromSeconds(5));
            var firstApprovalPending = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(5));
            var firstMessages = firstWriter
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var approvalRequest = Assert.Single(firstMessages, static x => IsRequestMethod(x.RootElement, "item/tool/requestApproval"));
                var availableDecisions = approvalRequest.RootElement
                    .GetProperty("params")
                    .GetProperty("availableDecisions")
                    .EnumerateArray()
                    .Select(static item => item.GetString())
                    .ToArray();
                Assert.Contains("accept", availableDecisions);
                Assert.Contains("acceptForSession", availableDecisions);
                Assert.DoesNotContain("acceptAndRemember", availableDecisions);
            }
            finally
            {
                foreach (var message in firstMessages)
                {
                    message.Dispose();
                }
            }

            firstApprovalPending.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                decision = "acceptAndRemember",
            }));

            var firstToolPending = await WaitForPendingServerRequestAsync(
                pending,
                static pendingRequest => pendingRequest.Key != 0,
                TimeSpan.FromSeconds(5),
                excludeRequestId: firstApprovalPending.Key);
            firstToolPending.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                success = true,
                output = "custom ok",
            }));

            var firstResult = await firstTask;
            Assert.True(firstResult.Success);
            Assert.Equal("custom ok", firstResult.OutputText);
            Assert.False(File.Exists(Path.Combine(tianShuHome, "tianshu.toml")));

            var secondTask = server.ExecuteToolCallAsync(
                threadId: "thread_custom_dynamic_001",
                turnId: "turn_custom_dynamic_002",
                itemId: "item_custom_dynamic_002",
                toolName: "mcp__custom_server__dangerous_tool",
                arguments: JsonSerializer.SerializeToElement(new { value = 2 }),
                context: new TurnRequestContext(
                    Model: null,
                    ModelProvider: null,
                    ServiceTier: null,
                    ApprovalPolicy: "on-request",
                    SandboxPolicy: null,
                    SandboxMode: null,
                    Cwd: root,
                    DynamicTools: dynamicTools),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            var secondToolPending = await WaitForPendingServerRequestAsync(
                pending,
                pendingRequest => pendingRequest.Key != firstApprovalPending.Key
                    && pendingRequest.Key != firstToolPending.Key,
                TimeSpan.FromSeconds(5));
            var afterSessionMessages = firstWriter
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                Assert.Equal(1, afterSessionMessages.Count(static x => IsRequestMethod(x.RootElement, "item/tool/requestApproval")));
            }
            finally
            {
                foreach (var message in afterSessionMessages)
                {
                    message.Dispose();
                }
            }

            secondToolPending.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                success = true,
                output = "custom remembered in session",
            }));

            var secondResult = await secondTask;
            Assert.True(secondResult.Success);
            Assert.Equal("custom remembered in session", secondResult.OutputText);

            var freshWriter = new StringWriter();
            var freshServer = new AppHostServer(new StringReader(string.Empty), freshWriter, threadStore);
            var freshPending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(freshServer, "pendingServerResponses");
            var freshTask = freshServer.ExecuteToolCallAsync(
                threadId: "thread_custom_dynamic_001",
                turnId: "turn_custom_dynamic_003",
                itemId: "item_custom_dynamic_003",
                toolName: "mcp__custom_server__dangerous_tool",
                arguments: JsonSerializer.SerializeToElement(new { value = 3 }),
                context: new TurnRequestContext(
                    Model: null,
                    ModelProvider: null,
                    ServiceTier: null,
                    ApprovalPolicy: "on-request",
                    SandboxPolicy: null,
                    SandboxMode: null,
                    Cwd: root,
                    DynamicTools: dynamicTools),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            await WaitForWriterContainsAsync(freshWriter, "\"method\":\"item/tool/requestApproval\"", TimeSpan.FromSeconds(5));
            var freshApprovalPending = await WaitForSinglePendingServerRequestAsync(freshPending, TimeSpan.FromSeconds(5));
            freshApprovalPending.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                decision = "decline",
            }));

            var freshResult = await freshTask;
            Assert.False(freshResult.Success);
            Assert.Contains("工具调用未获批准", freshResult.OutputText, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", previousKernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", previousTianShuHome);
            DeleteDirectory(root);
        }
    }

    private static ModelFunctionCall[] ExtractModelFunctionCalls(JsonElement outputItems)
    {
        var runtime = new KernelResponsesToolContinuationRuntime(
            new KernelToolRegistry(),
            static (_, _, _, _, _, _) => Task.FromResult<object>(new Dictionary<string, object?>()));

        return runtime
            .ExtractFunctionCalls(outputItems.EnumerateArray().Select(static item => item.Clone()).ToArray())
            .ToArray();
    }

    private static TurnOperationState CreateTurnOperationState(string threadId, string turnId, string itemId, string reasoningItemId, string userText)
        => new(threadId, turnId, itemId, reasoningItemId, userText);

    private static string ToTomlPath(string path)
        => path.Replace("\\", "/", StringComparison.Ordinal);
    private static async Task InitializeGitRepositoryAsync(string repoRoot)
    {
        await RunProcessAsync("git", "init", repoRoot);
        await RunProcessAsync("git", "config user.email \"tianshu-test@example.com\"", repoRoot);
        await RunProcessAsync("git", "config user.name \"TianShu Test\"", repoRoot);

        var file = Path.Combine(repoRoot, "sample.txt");
        await File.WriteAllTextAsync(file, "line-1" + Environment.NewLine);
        await RunProcessAsync("git", "add .", repoRoot);
        await RunProcessAsync("git", "commit -m \"init\"", repoRoot);

        var parentDirectory = Directory.GetParent(repoRoot)?.FullName ?? repoRoot;
        var remoteRoot = Path.Combine(parentDirectory, "remote.git");
        await RunProcessAsync("git", $"init --bare \"{remoteRoot}\"", repoRoot);
        await RunProcessAsync("git", $"remote add origin \"{remoteRoot}\"", repoRoot);
        await RunProcessAsync("git", "push -u origin master", repoRoot);

        await File.WriteAllTextAsync(file, "line-1-modified" + Environment.NewLine);
    }

    private static async Task RunProcessAsync(string fileName, string arguments, string workingDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        Assert.True(
            process.ExitCode == 0,
            $"命令执行失败: {fileName} {arguments}{Environment.NewLine}stdout: {stdout}{Environment.NewLine}stderr: {stderr}");
    }

    [Fact]
    public void KernelTurnStartRequest_ShouldParseTypedInputItemsAndExtractUserText()
    {
        const string json = """
        {
          "threadId": "thread-input-typed-001",
          "interactionEnvelope": {
            "id": "interaction-input-typed-001",
            "sourceKind": 0,
            "surface": "cli",
            "createdAtUnixMs": 1746200000000
          },
          "input": [
            "first line",
            {
              "type": "input_text",
              "value": "second line"
            },
            {
              "type": "mention",
              "path": "skill://demo/skill",
              "targetPath": "D:/skills/demo/SKILL.md",
              "name": "demo-skill"
            },
            {
              "type": "container",
              "content": [
                {
                  "type": "text",
                  "text": "nested line"
                }
              ]
            }
          ]
        }
        """;

        var request = JsonSerializer.Deserialize<KernelTurnStartRequest>(json, CreateProtocolJsonOptions());
        Assert.NotNull(request);
        Assert.NotNull(request!.Input);
        Assert.Equal(4, request.Input!.Count);
        Assert.Equal("first line", request.Input[0].Text);
        Assert.Equal("second line", request.Input[1].Text);
        Assert.Equal("skill://demo/skill", request.Input[2].Path);
        Assert.Equal("D:/skills/demo/SKILL.md", request.Input[2].CanonicalPath);
        Assert.Single(request.Input[3].ContentItems);
        Assert.Equal("nested line", request.Input[3].ContentItems[0].Text);
        Assert.NotNull(request.InteractionEnvelope);

        var interactionEnvelope = request.InteractionEnvelope!.ToContract();
        Assert.NotNull(interactionEnvelope);
        Assert.Equal("interaction-input-typed-001", interactionEnvelope!.Id.Value);
        Assert.Equal(TianShu.Contracts.Interactions.InteractionSourceKind.Host, interactionEnvelope.SourceKind);
        Assert.Equal("cli", interactionEnvelope.Surface);

        var method = typeof(AppHostServer).GetMethod(
            "ExtractUserText",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(KernelTurnStartRequest)],
            modifiers: null);
        Assert.NotNull(method);

        var text = Assert.IsType<string>(method!.Invoke(null, [request]));
        Assert.Contains("first line", text, StringComparison.Ordinal);
        Assert.Contains("second line", text, StringComparison.Ordinal);
        Assert.Contains("nested line", text, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelThreadResumeRequest_HistoryOverrideShouldRespectNullEmptyAndMissing()
    {
        var missing = JsonSerializer.Deserialize<KernelThreadResumeRequest>("""{"threadId":"thread-history-001"}""", CreateProtocolJsonOptions());
        var explicitNull = JsonSerializer.Deserialize<KernelThreadResumeRequest>("""{"threadId":"thread-history-001","history":null}""", CreateProtocolJsonOptions());
        var emptyArray = JsonSerializer.Deserialize<KernelThreadResumeRequest>("""{"threadId":"thread-history-001","history":[]}""", CreateProtocolJsonOptions());

        Assert.NotNull(missing);
        Assert.NotNull(explicitNull);
        Assert.NotNull(emptyArray);
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<KernelThreadResumeRequest>(
                """{"threadId":"thread-history-001","history":{"unexpected":true}}""",
                CreateProtocolJsonOptions()));

        var method = typeof(AppHostServer)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(static candidate =>
            {
                if (!string.Equals(candidate.Name, "HasHistoryOverride", StringComparison.Ordinal))
                {
                    return false;
                }

                var parameters = candidate.GetParameters();
                return parameters.Length == 2
                       && parameters[0].ParameterType == typeof(KernelThreadResumeRequest);
            });

        var missingArgs = new object?[] { missing, null };
        var missingOverride = Assert.IsType<bool>(method.Invoke(null, missingArgs));
        Assert.False(missingOverride);

        var nullArgs = new object?[] { explicitNull, null };
        var nullOverride = Assert.IsType<bool>(method.Invoke(null, nullArgs));
        Assert.False(nullOverride);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<KernelConversationHistoryItem>>(nullArgs[1]));

        var emptyArgs = new object?[] { emptyArray, null };
        var emptyOverride = Assert.IsType<bool>(method.Invoke(null, emptyArgs));
        Assert.True(emptyOverride);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<KernelConversationHistoryItem>>(emptyArgs[1]));
    }

    [Fact]
    public async Task RunAsync_ShouldRejectEmptyHistoryOverride()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_resume_empty_history_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"thread/resume","params":{"threadId":"thread_resume_empty_history_001","history":[]}}
                """;

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var message = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .Single(static document =>
                    document.RootElement.TryGetProperty("id", out var idProperty)
                    && idProperty.ValueKind == JsonValueKind.Number
                    && idProperty.GetInt32() == 1);
            try
            {
                var error = message.RootElement.GetProperty("error");
                Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                Assert.Equal("history must not be empty", error.GetProperty("message").GetString());
            }
            finally
            {
                message.Dispose();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    private static string FindRepositoryRoot()
    {
        var candidates = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
        };

        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("未找到 TianShu.sln。");
    }

    private static async Task WriteProcessLineAsync(StreamWriter writer, string line)
    {
        await writer.WriteLineAsync(line);
        await writer.FlushAsync();
    }

    private static async Task<JsonDocument> ReadProcessJsonResponseAsync(StreamReader reader, int responseId, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        while (true)
        {
            var line = await reader.ReadLineAsync().WaitAsync(timeoutCts.Token);
            if (line is null)
            {
                throw new InvalidOperationException($"等待响应 id={responseId} 时进程已退出。");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (IsResponseId(document.RootElement, responseId))
            {
                return document;
            }

            document.Dispose();
        }
    }

    private static JsonDocument ParseResponseDocument(string output, int responseId)
    {
        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(line);
                if (IsResponseId(document.RootElement, responseId))
                {
                    return document;
                }
            }
            catch (JsonException)
            {
            }

            document?.Dispose();
        }

        throw new InvalidOperationException($"未找到响应 id={responseId}。");
    }

    private static string ReadMcpAuthStatus(JsonDocument[] messages, int responseId, string serverName)
    {
        var response = messages.Single(x => IsResponseId(x.RootElement, responseId)).RootElement;
        var data = response.GetProperty("result").GetProperty("data");
        var server = data.EnumerateArray().First(item =>
            item.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), serverName, StringComparison.OrdinalIgnoreCase));

        return server.GetProperty("authStatus").GetString() ?? string.Empty;
    }

    private sealed class ModelCatalogEndpointServer : IDisposable
    {
        private readonly System.Net.HttpListener listener;
        private readonly CancellationTokenSource shutdown = new();
        private readonly Task loopTask;
        private readonly string payload;
        private readonly HashSet<string> notFoundPaths;
        private readonly HashSet<string> htmlPaths;
        private readonly List<string> requestPaths = [];

        private ModelCatalogEndpointServer(
            System.Net.HttpListener listener,
            string rootUrl,
            string payload,
            IEnumerable<string>? notFoundPaths,
            IEnumerable<string>? htmlPaths)
        {
            this.listener = listener;
            RootUrl = rootUrl;
            BaseUrl = rootUrl.TrimEnd('/') + "/v1";
            this.payload = payload;
            this.notFoundPaths = notFoundPaths is null
                ? []
                : new HashSet<string>(notFoundPaths, StringComparer.OrdinalIgnoreCase);
            this.htmlPaths = htmlPaths is null
                ? []
                : new HashSet<string>(htmlPaths, StringComparer.OrdinalIgnoreCase);
            loopTask = Task.Run(HandleLoopAsync);
        }

        public string RootUrl { get; }

        public string BaseUrl { get; }

        public string? LastRequestPath { get; private set; }

        public string? LastAuthorizationHeader { get; private set; }

        public IReadOnlyList<string> RequestPaths => requestPaths;

        public static ModelCatalogEndpointServer Start(
            string payload,
            IEnumerable<string>? notFoundPaths = null,
            IEnumerable<string>? htmlPaths = null)
        {
            var port = GetFreeTcpPort();
            var listener = new System.Net.HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            return new ModelCatalogEndpointServer(listener, $"http://127.0.0.1:{port}", payload, notFoundPaths, htmlPaths);
        }

        public void Dispose()
        {
            shutdown.Cancel();
            listener.Close();
            try
            {
                loopTask.GetAwaiter().GetResult();
            }
            catch
            {
                // 测试桩关闭时可能打断 HttpListener 等待，不影响断言。
            }

            shutdown.Dispose();
        }

        private async Task HandleLoopAsync()
        {
            while (!shutdown.IsCancellationRequested)
            {
                System.Net.HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (System.Net.HttpListenerException) when (shutdown.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (shutdown.IsCancellationRequested)
                {
                    break;
                }

                await HandleContextAsync(context).ConfigureAwait(false);
            }
        }

        private async Task HandleContextAsync(System.Net.HttpListenerContext context)
        {
            LastRequestPath = context.Request.Url?.AbsolutePath;
            LastAuthorizationHeader = context.Request.Headers["Authorization"];
            if (LastRequestPath is not null)
            {
                requestPaths.Add(LastRequestPath);
            }

            if (LastRequestPath is not null && notFoundPaths.Contains(LastRequestPath))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            if (LastRequestPath is not null && htmlPaths.Contains(LastRequestPath))
            {
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/html";
                var htmlBytes = Encoding.UTF8.GetBytes("<html>not a model catalog</html>");
                context.Response.ContentLength64 = htmlBytes.Length;
                await context.Response.OutputStream.WriteAsync(htmlBytes).ConfigureAwait(false);
                context.Response.Close();
                return;
            }

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(payload);
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.Close();
        }

        private static int GetFreeTcpPort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }

    private static JsonSerializerOptions CreateProtocolJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new KernelOptionalJsonConverterFactory());
        return options;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string key;
        private readonly string? originalValue;

        public EnvironmentVariableScope(string key, string? value)
        {
            this.key = key;
            originalValue = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(key, originalValue);
        }
    }

    private sealed class AbortAfterToolExecutionHook(string name, string errorMessage) : IKernelToolExecutionHook
    {
        public string Name => name;

        public Task OnBeforeExecuteAsync(KernelToolExecutionHookContext context, CancellationToken cancellationToken)
        {
            _ = context;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<KernelToolExecutionHookAfterDecision> OnAfterExecuteAsync(
            KernelToolExecutionHookContext context,
            KernelToolResult result,
            TimeSpan duration,
            CancellationToken cancellationToken)
        {
            _ = context;
            _ = result;
            _ = duration;
            _ = cancellationToken;
            return Task.FromResult(KernelToolExecutionHookAfterDecision.Abort(errorMessage));
        }

        public Task OnExecuteErrorAsync(
            KernelToolExecutionHookContext context,
            string error,
            TimeSpan duration,
            CancellationToken cancellationToken)
        {
            _ = context;
            _ = error;
            _ = duration;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class ChannelTextReader(ChannelReader<string> source) : TextReader
    {
        public override async Task<string?> ReadLineAsync()
        {
            while (await source.WaitToReadAsync().ConfigureAwait(false))
            {
                if (source.TryRead(out var line))
                {
                    return line;
                }
            }

            return null;
        }
    }

    private sealed class ChannelTextWriter : TextWriter
    {
        private readonly Channel<string> lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        private bool disposed;
        private readonly StringBuilder capturedText = new();

        public ChannelReader<string> Lines => lines.Reader;

        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteLineAsync(string? value)
        {
            if (disposed)
            {
                return Task.CompletedTask;
            }

            var line = value ?? string.Empty;
            capturedText.AppendLine(line);
            lines.Writer.TryWrite(line);
            return Task.CompletedTask;
        }

        public override Task FlushAsync() => Task.CompletedTask;

        protected override void Dispose(bool disposing)
        {
            if (!disposing || disposed)
            {
                base.Dispose(disposing);
                return;
            }

            disposed = true;
            lines.Writer.TryComplete();
            base.Dispose(disposing);
        }
    }
}
