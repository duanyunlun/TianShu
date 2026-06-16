using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelToolRuntimeParsingHelpersTests
{
    [Fact]
    public void TryResolveDynamicToolSchema_WhenDescriptorSchemaMissing_ShouldFallbackToObjectSchema()
    {
        var tools = new[]
        {
            new KernelDynamicToolDescriptor(
                FullName: "mcp__demo__search",
                ShortName: "search",
                Namespace: "mcp__demo",
                Description: "Search demo content.",
                Title: "Search",
                Server: "demo",
                ConnectorName: null,
                ConnectorDescription: null,
                ConnectorId: null,
                InputSchema: null,
                OutputSchema: null,
                Meta: null,
                Annotations: null),
        };

        var resolved = KernelToolRuntimeParsingHelpers.TryResolveDynamicToolSchema(tools, "search", out var schema);

        Assert.True(resolved);
        Assert.True(schema.HasValue);
        Assert.Equal("object", schema.Value.GetProperty("type").GetString());
    }

    [Fact]
    public void ExtractDynamicToolOutput_ShouldPreferContentItemsText()
    {
        using var response = JsonDocument.Parse(
            """
            {
              "output": "fallback text",
              "contentItems": [
                { "type": "text", "text": "first line" },
                { "type": "text", "content": "second line" }
              ]
            }
            """);

        var output = KernelToolRuntimeParsingHelpers.ExtractDynamicToolOutput(response.RootElement);
        var contentItems = KernelToolRuntimeParsingHelpers.ReadDynamicToolOutputContentItems(response.RootElement);

        Assert.Equal($"first line{Environment.NewLine}second line", output);
        Assert.NotNull(contentItems);
        Assert.Equal(2, contentItems!.Count);
        Assert.All(contentItems, static item => Assert.Equal("input_text", item.Type));
    }

    [Fact]
    public void TryConvertDynamicToolContentItem_ShouldConvertImageShape()
    {
        using var item = JsonDocument.Parse(
            """
            {
              "type": "image",
              "image_url": "https://example.com/demo.png",
              "detail": "original"
            }
            """);

        var converted = KernelToolRuntimeParsingHelpers.TryConvertDynamicToolContentItem(item.RootElement);

        Assert.NotNull(converted);
        Assert.Equal("input_image", converted!.Type);
        Assert.Equal("https://example.com/demo.png", converted.ImageUrl);
        Assert.Equal("original", converted.Detail);
    }

    [Fact]
    public void TryParseInlineToolCall_ShouldParseToolNameAndArguments()
    {
        var parsed = KernelToolRuntimeParsingHelpers.TryParseInlineToolCall(
            " /tool read_file { \"path\": \"docs/tianshu-implementation-tracker.md\" } ",
            out var toolName,
            out var arguments);

        Assert.True(parsed);
        Assert.Equal("read_file", toolName);
        Assert.Equal("docs/tianshu-implementation-tracker.md", arguments.GetProperty("path").GetString());
    }
}
