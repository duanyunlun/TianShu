using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelToolCallGateTests
{
    [Fact]
    public async Task ExecuteToolCallAsync_ShouldGateMutatingTools()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var cwd = Path.Combine(root, "repo");
        Directory.CreateDirectory(cwd);

        var output = new StringWriter();
        var server = new AppHostServer(new StringReader(string.Empty), output, new KernelThreadStore(storePath));

        var gate = new KernelReadinessFlag();
        var token = await gate.SubscribeAsync(CancellationToken.None);

        var targetPath = Path.Combine(cwd, "blocked.txt");
        var args = JsonSerializer.SerializeToElement(new
        {
            path = "blocked.txt",
            content = "hello",
        });

        var context = new TurnRequestContext(
            Model: null,
            ModelProvider: null,
            ServiceTier: null,
            ApprovalPolicy: "never",
            SandboxPolicy: null,
            SandboxMode: "workspaceWrite",
            Cwd: cwd,
            ProviderBaseUrl: null,
            ProviderApiKeyEnvironmentVariable: null,
            ProviderWireApi: null,
            IsReview: false,
            ReviewDisplayText: null);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var executeTask = server.ExecuteToolCallAsync(
            threadId: "thread_gate_001",
            turnId: "turn_gate_001",
            itemId: "tool_write_call_001",
            toolName: "write",
            arguments: args,
            context: context,
            toolCallGate: gate,
            cancellationToken: cts.Token);

        await Task.Delay(150, cts.Token);
        Assert.False(executeTask.IsCompleted);
        Assert.False(File.Exists(targetPath));

        Assert.True(await gate.MarkReadyAsync(token, CancellationToken.None));

        var result = await executeTask;
        Assert.True(result.Success);
        Assert.True(File.Exists(targetPath));

        DeleteDirectory(root);
    }

    [Fact]
    public async Task ExecuteToolCallAsync_ShouldNotGateReadOnlyTools()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var cwd = Path.Combine(root, "repo");
        Directory.CreateDirectory(cwd);

        var targetPath = Path.Combine(cwd, "note.txt");
        await File.WriteAllTextAsync(targetPath, "hello");

        var output = new StringWriter();
        var server = new AppHostServer(new StringReader(string.Empty), output, new KernelThreadStore(storePath));

        var gate = new KernelReadinessFlag();
        _ = await gate.SubscribeAsync(CancellationToken.None);

        var args = JsonSerializer.SerializeToElement(new
        {
            file_path = targetPath.Replace('\\', '/'),
        });

        var context = new TurnRequestContext(
            Model: null,
            ModelProvider: null,
            ServiceTier: null,
            ApprovalPolicy: "never",
            SandboxPolicy: null,
            SandboxMode: "workspaceWrite",
            Cwd: cwd,
            ProviderBaseUrl: null,
            ProviderApiKeyEnvironmentVariable: null,
            ProviderWireApi: null,
            IsReview: false,
            ReviewDisplayText: null);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await server.ExecuteToolCallAsync(
            threadId: "thread_gate_002",
            turnId: "turn_gate_002",
            itemId: "tool_read_call_002",
            toolName: "read_file",
            arguments: args,
            context: context,
            toolCallGate: gate,
            cancellationToken: cts.Token);

        Assert.True(result.Success);
        Assert.Contains("hello", result.OutputText, StringComparison.Ordinal);

        DeleteDirectory(root);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-gate-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                ResetReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(120);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(120);
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
}

