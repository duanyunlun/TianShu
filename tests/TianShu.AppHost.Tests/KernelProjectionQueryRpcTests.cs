using System.Reflection;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelProjectionQueryRpcTests
{
    [Fact]
    public async Task GovernanceProjectionRpc_WhenPendingInteractiveRequestsTracked_ReturnsQueues()
    {
        var root = Directory.CreateTempSubdirectory("tianshu-governance-projection-rpc-");
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root.FullName, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(string.Join(
                Environment.NewLine,
                """{"id":1,"method":"governance/approvalQueue/read","params":{}}""",
                """{"id":2,"method":"governance/userInputs/list","params":{}}""")));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);
            var pendingRuntime = GetPendingInteractiveReplayRuntime(server);
            pendingRuntime.TryTrackPendingInteractiveRequest(
                "item/commandExecution/requestApproval",
                new
                {
                    approvalId = "approval-rpc-001",
                    threadId = "thread-governance-rpc",
                    turnId = "turn-governance-rpc",
                    command = "dotnet test",
                    reason = "需要执行测试。",
                },
                "thread-governance-rpc",
                requestId: 101);
            pendingRuntime.TryTrackPendingInteractiveRequest(
                "item/tool/requestUserInput",
                new
                {
                    itemId = "user-input-rpc-001",
                    threadId = "thread-governance-rpc",
                    turnId = "turn-governance-rpc",
                    questions = new[]
                    {
                        new
                        {
                            id = "target",
                            header = "目标环境",
                            question = "请选择目标环境。",
                        },
                    },
                },
                "thread-governance-rpc",
                requestId: 102);

            await server.RunAsync(CancellationToken.None);

            using var approvalResponse = ReadResponse(writer, 1);
            var approval = Assert.Single(approvalResponse.RootElement.GetProperty("result").GetProperty("items").EnumerateArray());
            Assert.Equal("approval-rpc-001", approval.GetProperty("approvalId").GetProperty("value").GetString());
            Assert.Contains("dotnet test", approval.GetProperty("reason").GetString(), StringComparison.Ordinal);
            Assert.Equal("tianshu-user", approval.GetProperty("requestedFrom").GetProperty("id").GetProperty("value").GetString());

            using var userInputResponse = ReadResponse(writer, 2);
            var userInput = Assert.Single(userInputResponse.RootElement.GetProperty("result").EnumerateArray());
            Assert.Equal("user-input-rpc-001", userInput.GetProperty("id").GetProperty("value").GetString());
            Assert.Contains("目标环境", userInput.GetProperty("prompt").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AgentJobsListRpc_WhenJobsPersisted_ReturnsActiveJobs()
    {
        var root = Directory.CreateTempSubdirectory("tianshu-agent-jobs-list-rpc-");
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root.FullName, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(string.Join(
                Environment.NewLine,
                """{"id":1,"method":"agent/job/create","params":{"jobId":"job-rpc-active","name":"active job","instruction":"处理数据","items":[{"itemId":"item-001","value":1}]}}""",
                """{"id":2,"method":"agent/job/create","params":{"jobId":"job-rpc-completed","name":"completed job","instruction":"空作业","items":[]}}""",
                """{"id":3,"method":"agent/jobs/list","params":{}}""")));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var response = ReadResponse(writer, 3);
            var jobs = response.RootElement.GetProperty("result").GetProperty("jobs").EnumerateArray().ToArray();
            var job = Assert.Single(jobs);
            Assert.Equal("job-rpc-active", job.GetProperty("id").GetProperty("value").GetString());
            Assert.Equal("pending", job.GetProperty("status").GetString());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static KernelPendingInteractiveReplayAppHostRuntime GetPendingInteractiveReplayRuntime(AppHostServer server)
    {
        var field = typeof(AppHostServer).GetField(
            "pendingInteractiveReplayAppHostRuntime",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (KernelPendingInteractiveReplayAppHostRuntime)(field?.GetValue(server)
            ?? throw new InvalidOperationException("pendingInteractiveReplayAppHostRuntime field not found."));
    }

    private static JsonDocument ReadResponse(StringWriter writer, long id)
        => writer
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => JsonDocument.Parse(line))
            .Single(document => IsResponseId(document.RootElement, id));

    private static bool IsResponseId(JsonElement json, long id)
        => json.TryGetProperty("id", out var idElement)
           && idElement.ValueKind == JsonValueKind.Number
           && idElement.TryGetInt64(out var numericId)
           && numericId == id;
}
