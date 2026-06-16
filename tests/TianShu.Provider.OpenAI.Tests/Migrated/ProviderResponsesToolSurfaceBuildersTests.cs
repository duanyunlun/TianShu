using System.Text.Json;
using TianShu.Provider.Abstractions;
using TianShu.Provider.OpenAI;
using TianShu.Provider.OpenAICompatible;

namespace TianShu.Provider.OpenAI.Tests;

public sealed class ProviderResponsesToolSurfaceBuildersTests
{
    [Fact]
    public void Resolve_ShouldReturnOpenAiResponsesToolSurfaceBuilder_ForResponsesWireApi()
    {
        var builder = ProviderResponsesToolSurfaceBuilders.Resolve("responses", "test.providerWireApi");

        var typed = Assert.IsType<OpenAiResponsesToolSurfaceBuilder>(builder);
        Assert.Equal("responses", typed.WireApi);
    }

    [Fact]
    public void Resolve_ShouldReturnChatCompletionsToolSurfaceBuilder_ForOpenAiChatCompletionsProtocol()
    {
        var builder = ProviderResponsesToolSurfaceBuilders.Resolve("openai_chat_completions", "test.providerWireApi");

        var typed = Assert.IsType<OpenAiChatCompletionsToolSurfaceBuilder>(builder);
        Assert.Equal("openai_chat_completions", typed.WireApi);
    }

    [Fact]
    public void Build_WhenChatCompletionsFunctionToolProvided_ShouldUseOfficialFunctionWrapper()
    {
        IProviderResponsesToolSurfaceBuilder builder = new OpenAiChatCompletionsToolSurfaceBuilder();

        var tools = builder.Build(new ProviderResponsesToolSurfaceBuilderContext(
            [
                new ProviderResponsesFunctionToolDefinition(
                    "search_files",
                    "Search files.",
                    JsonSerializer.SerializeToElement(new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new { type = "string" },
                        },
                    }),
                    strict: true),
            ]));

        var json = JsonSerializer.SerializeToElement(tools);
        var tool = json[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        var function = tool.GetProperty("function");
        Assert.Equal("search_files", function.GetProperty("name").GetString());
        Assert.Equal("Search files.", function.GetProperty("description").GetString());
        Assert.True(function.GetProperty("strict").GetBoolean());
        Assert.Equal("object", function.GetProperty("parameters").GetProperty("type").GetString());
    }

    [Fact]
    public void Build_WhenChatCompletionsReceivesHostedTool_ShouldSkipUnsupportedTool()
    {
        IProviderResponsesToolSurfaceBuilder builder = new OpenAiChatCompletionsToolSurfaceBuilder();

        var tools = builder.Build(new ProviderResponsesToolSurfaceBuilderContext(
            [
                new ProviderResponsesHostedToolDefinition("web_search_preview"),
            ]));

        Assert.Empty(tools);
    }

    [Fact]
    public void Build_WhenDefinitionsRequireProviderSpecificShapes_ShouldCompileOutsideKernel()
    {
        IProviderResponsesToolSurfaceBuilder builder = new OpenAiResponsesToolSurfaceBuilder();
        var toolDefinitions = new ProviderResponsesToolDefinition[]
        {
            new ProviderResponsesFunctionToolDefinition(
                "mcp__calendar__find_events",
                "Search calendar events.",
                JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new
                    {
                        query = new
                        {
                            type = new[] { "string", "null" },
                        },
                    },
                    additionalProperties = false,
                }),
                JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new
                    {
                        total = new
                        {
                            type = "integer",
                        },
                    },
                    additionalProperties = false,
                }),
                strict: false,
                outputShape: ProviderResponsesToolOutputShape.McpToolResultEnvelope),
            new ProviderResponsesCustomToolDefinition(
                "exec",
                "Runs raw JavaScript.",
                JsonSerializer.SerializeToElement(new
                {
                    type = "grammar",
                    syntax = "lark",
                    definition = "start: /.+/",
                })),
            new ProviderResponsesHostedToolDefinition(
                toolType: "web_search",
                externalWebAccess: true,
                searchContentTypes: ["text", "image"]),
            new ProviderResponsesHostedToolDefinition(
                toolType: "tool_search",
                description: "Search discoverable connector tools.",
                inputSchema: JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new
                    {
                        query = new
                        {
                            type = "string",
                        },
                    },
                    required = new[] { "query" },
                    additionalProperties = false,
                }),
                execution: "client"),
        };

        var tools = builder.Build(new ProviderResponsesToolSurfaceBuilderContext(toolDefinitions));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var functionTool = json.RootElement.EnumerateArray().Single(static tool =>
            tool.GetProperty("type").GetString() == "function"
            && tool.GetProperty("name").GetString() == "mcp__calendar__find_events");
        Assert.Equal("string", functionTool.GetProperty("parameters").GetProperty("properties").GetProperty("query").GetProperty("type").GetString());
        Assert.Equal("object", functionTool.GetProperty("output_schema").GetProperty("type").GetString());
        Assert.Equal("array", functionTool.GetProperty("output_schema").GetProperty("properties").GetProperty("content").GetProperty("type").GetString());
        Assert.Equal(
            "integer",
            functionTool.GetProperty("output_schema").GetProperty("properties").GetProperty("structuredContent").GetProperty("properties").GetProperty("total").GetProperty("type").GetString());

        var customTool = json.RootElement.EnumerateArray().Single(static tool =>
            tool.GetProperty("type").GetString() == "custom"
            && tool.GetProperty("name").GetString() == "exec");
        Assert.Equal("grammar", customTool.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal("lark", customTool.GetProperty("format").GetProperty("syntax").GetString());

        var webSearchTool = json.RootElement.EnumerateArray().Single(static tool =>
            tool.GetProperty("type").GetString() == "web_search");
        Assert.True(webSearchTool.GetProperty("external_web_access").GetBoolean());
        Assert.Equal(2, webSearchTool.GetProperty("search_content_types").GetArrayLength());

        var toolSearchTool = json.RootElement.EnumerateArray().Single(static tool =>
            tool.GetProperty("type").GetString() == "tool_search");
        Assert.Equal("client", toolSearchTool.GetProperty("execution").GetString());
        Assert.Equal("object", toolSearchTool.GetProperty("parameters").GetProperty("type").GetString());
    }
}
