using System.Text.Json;
using TianShu.AppHost.Tools;
using TianShu.Execution.Runtime;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelToolItemLifecycleHelpersTests
{
    [Fact]
    public void BuildDynamicToolContentItems_ShouldNormalizeTextAndImagePayloads()
    {
        var result = new KernelToolResult(
            success: true,
            outputText: string.Empty,
            outputContentItems:
            [
                new KernelToolOutputContentItem("input_text", Text: "hello"),
                new KernelToolOutputContentItem("input_image", ImageUrl: "data:image/png;base64,abc"),
            ]);

        var contentItems = KernelToolItemLifecycleHelpers.BuildDynamicToolContentItems(result);

        Assert.NotNull(contentItems);
        var payload = JsonSerializer.SerializeToElement(contentItems!);
        Assert.Equal("inputText", payload[0].GetProperty("type").GetString());
        Assert.Equal("hello", payload[0].GetProperty("text").GetString());
        Assert.Equal("inputImage", payload[1].GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64,abc", payload[1].GetProperty("imageUrl").GetString());
    }

    [Fact]
    public void BuildFileChangeChanges_WhenWriteUsesRelativePath_ShouldReturnAbsoluteAddChange()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            using var arguments = JsonDocument.Parse(
                """
                {
                  "path": "notes/output.txt",
                  "content": "hello",
                  "append": false
                }
                """);

            var changes = KernelToolItemLifecycleHelpers.BuildFileChangeChanges("write", arguments.RootElement, tempDirectory);

            var change = Assert.Single(changes);
            var payload = JsonSerializer.SerializeToElement(change);
            Assert.Equal(Path.GetFullPath(Path.Combine(tempDirectory, "notes", "output.txt")), payload.GetProperty("path").GetString());
            Assert.Equal("add", payload.GetProperty("kind").GetString());
            Assert.Equal("hello", payload.GetProperty("diff").GetString());
        }
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    [Fact]
    public void CommandExecutionHelpers_ShouldBuildStatusAggregateAndPayload()
    {
        var aggregated = KernelToolItemLifecycleHelpers.BuildCommandExecutionAggregatedOutput("stdout", "stderr");
        var payload = KernelToolItemLifecycleHelpers.BuildCommandExecutionItemPayload(
            itemId: "item_001",
            command: "dotnet test",
            cwd: "D:\\repo",
            processId: "42",
            status: KernelToolItemLifecycleHelpers.TryGetCommandExecutionStatusFromExitCode(1),
            aggregatedOutput: aggregated,
            exitCode: 1,
            durationMs: 12);
        var element = JsonSerializer.SerializeToElement(payload);

        Assert.Equal("failed", element.GetProperty("status").GetString());
        Assert.Equal("stdout" + Environment.NewLine + "stderr", element.GetProperty("aggregatedOutput").GetString());
        Assert.Equal("commandExecution", element.GetProperty("type").GetString());
        Assert.Equal("dotnet test", element.GetProperty("command").GetString());
        Assert.Equal(0, element.GetProperty("commandActions").GetArrayLength());
        Assert.Equal(1, element.GetProperty("exitCode").GetInt32());
        Assert.Equal(12, element.GetProperty("durationMs").GetInt64());
    }

    [Fact]
    public void McpToolLifecycleHelpers_ShouldBuildDescriptorAndResultPayload()
    {
        using var arguments = JsonDocument.Parse("""{ "query": "tianshu" }""");
        var dynamicTools = new[]
        {
            new KernelDynamicToolDescriptor(
                FullName: "search_docs",
                ShortName: "search_docs",
                Namespace: "docs",
                Description: null,
                Title: null,
                Server: "docs-server",
                ConnectorName: null,
                ConnectorDescription: null,
                ConnectorId: null,
                InputSchema: null,
                OutputSchema: null,
                Meta: null,
                Annotations: null),
        };
        var rawContent = JsonSerializer.SerializeToElement(new { type = "text", text = "ok" });
        var structuredOutput = JsonSerializer.SerializeToElement(new { count = 1 });
        var metadata = JsonSerializer.SerializeToElement(new { trace = "trace_001" });
        var result = new KernelToolResult(
            success: true,
            outputText: string.Empty,
            rawOutputContentItems: [rawContent],
            structuredOutput: structuredOutput,
            metadata: metadata);

        var created = KernelToolItemLifecycleHelpers.TryCreateMcpToolLifecycleDescriptor(
            dynamicTools,
            "search_docs",
            out var descriptor);
        var resultPayload = KernelToolItemLifecycleHelpers.CreateMcpToolCallResultPayload(result);
        var item = KernelToolItemLifecycleHelpers.CreateMcpToolCallItem(
            itemId: "item_mcp",
            server: descriptor.Server,
            tool: descriptor.Tool,
            status: "completed",
            arguments: arguments.RootElement,
            resultPayload: resultPayload,
            errorPayload: null,
            durationMs: 25);
        var payload = JsonSerializer.SerializeToElement(item);

        Assert.True(created);
        Assert.Equal("docs-server", descriptor.Server);
        Assert.Equal("search_docs", descriptor.Tool);
        Assert.Equal("mcpToolCall", payload.GetProperty("type").GetString());
        Assert.Equal("docs-server", payload.GetProperty("server").GetString());
        Assert.Equal("ok", payload.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal(1, payload.GetProperty("result").GetProperty("structuredContent").GetProperty("count").GetInt32());
        Assert.Equal("trace_001", payload.GetProperty("result").GetProperty("_meta").GetProperty("trace").GetString());
    }

    [Fact]
    public void CaptureWebSearchOutputItems_ShouldExtractQueryFromAction()
    {
        using var outputItems = JsonDocument.Parse(
            """
            [
              {
                "type": "web_search_call",
                "call_id": "call_web_001",
                "action": {
                  "url": "https://example.test/search?q=tianshu"
                }
              }
            ]
            """);

        var observations = KernelToolItemLifecycleHelpers
            .CaptureWebSearchOutputItems(outputItems.RootElement.EnumerateArray())
            .ToArray();
        var observation = Assert.Single(observations);
        var payload = JsonSerializer.SerializeToElement(
            KernelToolItemLifecycleHelpers.BuildWebSearchNotificationItem(observation));

        Assert.Equal("call_web_001", observation.CallId);
        Assert.Equal("https://example.test/search?q=tianshu", observation.Query);
        Assert.Equal("webSearch", payload.GetProperty("type").GetString());
        Assert.Equal("https://example.test/search?q=tianshu", payload.GetProperty("action").GetProperty("url").GetString());
    }

    [Fact]
    public async Task CaptureImageGenerationOutputItemsAsync_WhenCwdProvided_ShouldSaveResultAndCloneRawItem()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var result = Convert.ToBase64String([137, 80, 78, 71]);
            using var outputItems = JsonDocument.Parse(
                $$"""
                [
                  {
                    "type": "image_generation_call",
                    "call_id": "image_call_001",
                    "status": "completed",
                    "revised_prompt": "a small cat",
                    "result": "{{result}}"
                  }
                ]
                """);

            var observations = await KernelToolItemLifecycleHelpers.CaptureImageGenerationOutputItemsAsync(
                outputItems.RootElement.EnumerateArray(),
                tempDirectory,
                CancellationToken.None);

            var observation = Assert.Single(observations);
            Assert.Equal("image_call_001", observation.CallId);
            Assert.Equal("completed", observation.Status);
            Assert.Equal("a small cat", observation.RevisedPrompt);
            Assert.Equal(result, observation.Result);
            Assert.Equal(Path.Combine(tempDirectory, "image_call_001.png"), observation.SavedPath);
            Assert.NotNull(observation.SavedPath);
            Assert.True(File.Exists(observation.SavedPath!));
            Assert.Equal("image_generation_call", observation.RawItem.GetProperty("type").GetString());
        }
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-item-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
