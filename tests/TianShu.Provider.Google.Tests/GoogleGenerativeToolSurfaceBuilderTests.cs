using System.Text.Json;
using TianShu.Provider.Abstractions;
using TianShu.Provider.Google;

namespace TianShu.Provider.Google.Tests;

public sealed class GoogleGenerativeToolSurfaceBuilderTests
{
    [Fact]
    public void Resolve_ShouldReturnGoogleGenerativeToolSurfaceBuilder_ForGoogleProtocol()
    {
        var builder = ProviderResponsesToolSurfaceBuilders.Resolve("google_generative", "test.providerWireApi");

        var typed = Assert.IsType<GoogleGenerativeToolSurfaceBuilder>(builder);
        Assert.Equal("google_generative", typed.WireApi);
    }

    [Fact]
    public void Build_WhenFunctionToolProvided_ShouldUseGeminiFunctionDeclarationsShape()
    {
        IProviderResponsesToolSurfaceBuilder builder = new GoogleGenerativeToolSurfaceBuilder();

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
        var declaration = Assert.Single(tool.GetProperty("functionDeclarations").EnumerateArray());
        Assert.Equal("read_file", declaration.GetProperty("name").GetString());
        Assert.Equal("Read a file.", declaration.GetProperty("description").GetString());
        Assert.Equal("object", declaration.GetProperty("parameters").GetProperty("type").GetString());
    }

    [Fact]
    public void Build_WhenHostedToolProvided_ShouldSkipUnsupportedTool()
    {
        IProviderResponsesToolSurfaceBuilder builder = new GoogleGenerativeToolSurfaceBuilder();

        var tools = builder.Build(new ProviderResponsesToolSurfaceBuilderContext(
            [
                new ProviderResponsesHostedToolDefinition("web_search_preview"),
            ]));

        Assert.Empty(tools);
    }
}
