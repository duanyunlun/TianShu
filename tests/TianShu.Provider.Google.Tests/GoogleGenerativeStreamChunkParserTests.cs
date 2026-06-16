using System.Text.Json;
using TianShu.Provider.Abstractions;
using TianShu.Provider.Google;

namespace TianShu.Provider.Google.Tests;

public sealed class GoogleGenerativeStreamChunkParserTests
{
    [Fact]
    public void Resolve_ShouldReturnGoogleGenerativeStreamChunkParser_ForGoogleProtocol()
    {
        var parser = ProviderResponsesStreamChunkParsers.Resolve("google_generative");

        Assert.IsType<GoogleGenerativeStreamChunkParser>(parser);
    }

    [Fact]
    public void TryReadChunk_WhenTextAndFunctionCallPresent_ShouldProjectCanonicalChunk()
    {
        IProviderResponsesStreamChunkParser parser = new GoogleGenerativeStreamChunkParser();
        using var document = JsonDocument.Parse(
            """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      { "text": "准备读取文件。" },
                      {
                        "functionCall": {
                          "name": "read_file",
                          "args": { "path": "README.md" }
                        }
                      }
                    ]
                  },
                  "finishReason": "STOP"
                }
              ]
            }
            """);

        var parsed = parser.TryReadChunk(document.RootElement, out var chunk);

        Assert.True(parsed);
        Assert.Equal("准备读取文件。", chunk.TextDelta);
        Assert.True(chunk.Completed);
        var call = Assert.Single(chunk.FunctionCalls);
        Assert.Equal("function_call", call.GetProperty("type").GetString());
        Assert.Equal("read_file", call.GetProperty("call_id").GetString());
        Assert.Equal("read_file", call.GetProperty("name").GetString());
        using var arguments = JsonDocument.Parse(call.GetProperty("arguments").GetString()!);
        Assert.Equal("README.md", arguments.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public void TryReadChunk_WhenRetryableGoogleErrorPresent_ShouldThrowWithRetryHint()
    {
        IProviderResponsesStreamChunkParser parser = new GoogleGenerativeStreamChunkParser();
        using var document = JsonDocument.Parse(
            """
            {
              "error": {
                "code": 503,
                "message": "service unavailable",
                "status": "UNAVAILABLE"
              }
            }
            """);

        var error = Assert.Throws<ProviderResponsesStreamParseException>(
            () => parser.TryReadChunk(document.RootElement, out _));

        Assert.True(error.IsRetryable);
        Assert.Contains("service unavailable", error.Message, StringComparison.Ordinal);
        Assert.Contains("code=503", error.Message, StringComparison.Ordinal);
        Assert.Contains("status=UNAVAILABLE", error.Message, StringComparison.Ordinal);
    }
}
