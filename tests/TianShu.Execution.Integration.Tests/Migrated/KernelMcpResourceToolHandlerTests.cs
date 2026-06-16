using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using TianShu.Tools.McpResources;

namespace TianShu.Execution.Integration.Tests;

public sealed class KernelMcpResourceToolHandlerTests
{
    [Fact]
    public async Task ListMcpResourcesToolHandler_ShouldSerializeRuntimePayload()
    {
        var handler = CreateHandler("list_mcp_resources");
        using var args = JsonDocument.Parse("""
            {
              "server": "docs",
              "cursor": "cursor-1"
            }
            """);

        var result = await handler.InvokeAsync(
            CreateRequest("list_mcp_resources", args.RootElement),
            CreateContext(new TestMcpResourceServices(
                ListResources: (server, cursor, _) => Task.FromResult(new TianShuMcpListResourcesResult(
                    server,
                    [new TianShuMcpResourceEntry(server!, JsonSerializer.SerializeToElement(new
                    {
                        uri = "file://docs/readme.md",
                        name = "README",
                    }))],
                    cursor)))),
            CancellationToken.None);

        using var output = ParseSuccessPayload(result);
        Assert.Equal("docs", output.RootElement.GetProperty("server").GetString());
        Assert.Equal("cursor-1", output.RootElement.GetProperty("nextCursor").GetString());
        var resource = Assert.Single(output.RootElement.GetProperty("resources").EnumerateArray());
        Assert.Equal("docs", resource.GetProperty("server").GetString());
        Assert.Equal("file://docs/readme.md", resource.GetProperty("uri").GetString());
        Assert.Equal("README", resource.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ListMcpResourcesToolHandler_ShouldRejectCursorWithoutServer()
    {
        var handler = CreateHandler("list_mcp_resources");
        using var args = JsonDocument.Parse("""{ "cursor": "cursor-1" }""");

        var result = await handler.InvokeAsync(
            CreateRequest("list_mcp_resources", args.RootElement),
            CreateContext(new TestMcpResourceServices(
                ListResources: (_, _, _) => throw new InvalidOperationException("should not run"))),
            CancellationToken.None);

        Assert.NotNull(result.Failure);
        Assert.Equal("cursor can only be used when a server is specified", result.Failure.Message);
    }

    [Fact]
    public async Task ListMcpResourceTemplatesToolHandler_ShouldSerializeRuntimePayload()
    {
        var handler = CreateHandler("list_mcp_resource_templates");
        using var args = JsonDocument.Parse("""
            {
              "server": "docs"
            }
            """);

        var result = await handler.InvokeAsync(
            CreateRequest("list_mcp_resource_templates", args.RootElement),
            CreateContext(new TestMcpResourceServices(
                ListResourceTemplates: (server, _, _) => Task.FromResult(new TianShuMcpListResourceTemplatesResult(
                    server,
                    [new TianShuMcpResourceTemplateEntry(server!, JsonSerializer.SerializeToElement(new
                    {
                        uriTemplate = "docs://{path}",
                        name = "Docs Template",
                    }))],
                    null)))),
            CancellationToken.None);

        using var output = ParseSuccessPayload(result);
        Assert.Equal("docs", output.RootElement.GetProperty("server").GetString());
        var template = Assert.Single(output.RootElement.GetProperty("resourceTemplates").EnumerateArray());
        Assert.Equal("docs", template.GetProperty("server").GetString());
        Assert.Equal("docs://{path}", template.GetProperty("uriTemplate").GetString());
        Assert.Equal("Docs Template", template.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ReadMcpResourceToolHandler_ShouldSerializeRuntimePayload()
    {
        var handler = CreateHandler("read_mcp_resource");
        using var args = JsonDocument.Parse("""
            {
              "server": "docs",
              "uri": "file://docs/readme.md"
            }
            """);

        var result = await handler.InvokeAsync(
            CreateRequest("read_mcp_resource", args.RootElement),
            CreateContext(new TestMcpResourceServices(
                ReadResource: (server, uri, _) => Task.FromResult(new TianShuMcpReadResourceResult(
                    server,
                    uri,
                    JsonSerializer.SerializeToElement(new
                    {
                        contents = new object[]
                        {
                            new
                            {
                                uri,
                                mimeType = "text/plain",
                                text = "hello",
                            },
                        },
                    }))))),
            CancellationToken.None);

        using var output = ParseSuccessPayload(result);
        Assert.Equal("docs", output.RootElement.GetProperty("server").GetString());
        Assert.Equal("file://docs/readme.md", output.RootElement.GetProperty("uri").GetString());
        var content = Assert.Single(output.RootElement.GetProperty("contents").EnumerateArray());
        Assert.Equal("text/plain", content.GetProperty("mimeType").GetString());
        Assert.Equal("hello", content.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ReadMcpResourceToolHandler_ShouldRequireServerAndUri()
    {
        var handler = CreateHandler("read_mcp_resource");
        using var args = JsonDocument.Parse("""{ "server": "docs" }""");

        var result = await handler.InvokeAsync(
            CreateRequest("read_mcp_resource", args.RootElement),
            CreateContext(new TestMcpResourceServices(
                ReadResource: (_, _, _) => throw new InvalidOperationException("should not run"))),
            CancellationToken.None);

        Assert.NotNull(result.Failure);
        Assert.Equal("uri is required", result.Failure.Message);
    }

    private static ITianShuToolHandler CreateHandler(string toolKey)
        => new McpResourceToolProvider().CreateHandler(toolKey, new TianShuToolActivationContext());

    private static ToolInvocationRequest CreateRequest(string toolName, JsonElement args)
        => new(new CallId("call_001"), toolName, "invoke", StructuredValue.FromJsonElement(args));

    private static TianShuToolInvocationContext CreateContext(ITianShuMcpResourceToolServices services)
        => new(
            ThreadId: "thread_001",
            TurnId: "turn_001",
            WorkingDirectory: Environment.CurrentDirectory,
            McpResourceServices: services);

    private static JsonDocument ParseSuccessPayload(ToolInvocationResult result)
    {
        Assert.Null(result.Failure);
        var streamItem = Assert.Single(result.StreamItems);
        return JsonDocument.Parse(JsonSerializer.Serialize(streamItem.Payload));
    }

    private sealed class TestMcpResourceServices(
        Func<string?, string?, CancellationToken, Task<TianShuMcpListResourcesResult>>? ListResources = null,
        Func<string?, string?, CancellationToken, Task<TianShuMcpListResourceTemplatesResult>>? ListResourceTemplates = null,
        Func<string, string, CancellationToken, Task<TianShuMcpReadResourceResult>>? ReadResource = null)
        : ITianShuMcpResourceToolServices
    {
        public Task<TianShuMcpListResourcesResult> ListResourcesAsync(string? server, string? cursor, CancellationToken cancellationToken)
            => ListResources is not null
                ? ListResources(server, cursor, cancellationToken)
                : throw new NotSupportedException();

        public Task<TianShuMcpListResourceTemplatesResult> ListResourceTemplatesAsync(string? server, string? cursor, CancellationToken cancellationToken)
            => ListResourceTemplates is not null
                ? ListResourceTemplates(server, cursor, cancellationToken)
                : throw new NotSupportedException();

        public Task<TianShuMcpReadResourceResult> ReadResourceAsync(string server, string uri, CancellationToken cancellationToken)
            => ReadResource is not null
                ? ReadResource(server, uri, cancellationToken)
                : throw new NotSupportedException();
    }
}
