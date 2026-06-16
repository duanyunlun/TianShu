using System.Text.Json;
using TianShu.Provider.Abstractions;
using TianShu.Provider.Anthropic;

namespace TianShu.Provider.Anthropic.Tests;

public sealed class AnthropicMessagesToolSurfaceBuilderTests
{
    [Fact]
    public void Resolve_ShouldReturnAnthropicToolSurfaceBuilder_ForAnthropicProtocol()
    {
        var builder = ProviderResponsesToolSurfaceBuilders.Resolve("anthropic_messages", "test.providerWireApi");

        var typed = Assert.IsType<AnthropicMessagesToolSurfaceBuilder>(builder);
        Assert.Equal("anthropic_messages", typed.WireApi);
    }

    [Fact]
    public void Build_WhenFunctionToolProvided_ShouldUseAnthropicToolShape()
    {
        IProviderResponsesToolSurfaceBuilder builder = new AnthropicMessagesToolSurfaceBuilder();

        var tools = builder.Build(new ProviderResponsesToolSurfaceBuilderContext(
            [
                new ProviderResponsesFunctionToolDefinition(
                    "read_file",
                    "Read a file.",
                    JsonSerializer.SerializeToElement(new
                    {
                        type = "object",
                        properties = new
                        {
                            path = new { type = "string" },
                        },
                        required = new[] { "path" },
                    }),
                    strict: true),
            ]));

        var json = JsonSerializer.SerializeToElement(tools);
        var tool = Assert.Single(json.EnumerateArray());
        Assert.Equal("read_file", tool.GetProperty("name").GetString());
        Assert.Equal("Read a file.", tool.GetProperty("description").GetString());
        Assert.Equal("object", tool.GetProperty("input_schema").GetProperty("type").GetString());
        Assert.False(tool.TryGetProperty("type", out _));
    }

    [Fact]
    public void Build_WhenHostedToolProvided_ShouldSkipUnsupportedTool()
    {
        IProviderResponsesToolSurfaceBuilder builder = new AnthropicMessagesToolSurfaceBuilder();

        var tools = builder.Build(new ProviderResponsesToolSurfaceBuilderContext(
            [
                new ProviderResponsesHostedToolDefinition("web_search_preview"),
            ]));

        Assert.Empty(tools);
    }
}
