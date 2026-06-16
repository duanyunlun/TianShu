using System.Text.Encodings.Web;
using System.Text.Json;
using TianShu.Contracts.Diagnostics;
using TianShu.Diagnostics;

namespace TianShu.Diagnostics.Tests;

public sealed class ProviderRequestContextStatsBuilderTests
{
    [Fact]
    public void Build_ShouldExposeResponsesShapeWithoutRawTextOrSecrets()
    {
        var input = JsonSerializer.Deserialize<JsonElement[]>(
            """
            [
              {
                "role": "user",
                "content": [
                  { "type": "input_text", "text": "不要把这段用户原文放进统计事件" }
                ]
              }
            ]
            """)!;
        var tools = JsonSerializer.Deserialize<JsonElement[]>(
            """
            [
              { "type": "function", "name": "shell", "description": "run command" }
            ]
            """)!;
        var payload = new Dictionary<string, object?>
        {
            ["model"] = "gpt-test",
            ["instructions"] = "系统指令原文不应进入统计事件",
            ["authorization"] = "Bearer secret",
            ["tools"] = tools,
            ["input"] = input,
        };

        var stats = ProviderRequestContextStatsBuilder.Build(
            payload,
            new ProviderRequestContextStatsBuildOptions
            {
                ThreadId = "thread-1",
                TurnId = "turn-1",
                RequestSequence = 3,
                Model = "gpt-test",
                Provider = "openai",
                Transport = "http",
                InputPropertyName = "input",
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var json = JsonSerializer.Serialize(stats, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        Assert.Equal("turn/provider_request/context_stats", stats.EventName);
        Assert.Equal(3, stats.RequestSequence);
        Assert.Equal("http", stats.Transport);
        Assert.Equal("instructions", stats.Instructions?.Key);
        Assert.Equal("input", stats.Input?.Key);
        Assert.Equal("tools", stats.Tools?.Key);
        Assert.DoesNotContain("不要把这段用户原文放进统计事件", json, StringComparison.Ordinal);
        Assert.DoesNotContain("系统指令原文不应进入统计事件", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_ShouldRecognizeChatMessagesShape()
    {
        var messages = JsonSerializer.Deserialize<JsonElement[]>(
            """
            [
              { "role": "system", "content": "system text" },
              { "role": "user", "content": "user text" }
            ]
            """)!;
        var payload = new Dictionary<string, object?>
        {
            ["model"] = "chat-test",
            ["messages"] = messages,
            ["tools"] = Array.Empty<JsonElement>(),
        };

        var stats = ProviderRequestContextStatsBuilder.Build(
            payload,
            new ProviderRequestContextStatsBuildOptions
            {
                ThreadId = "thread-1",
                TurnId = "turn-1",
                RequestSequence = 1,
                Model = "chat-test",
                Provider = "openai-compatible",
                Transport = "websocket",
                InputPropertyName = "messages",
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var json = JsonSerializer.Serialize(stats, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        Assert.Equal("messages", stats.Input?.Key);
        Assert.Equal(2, stats.Input?.Count);
        Assert.Contains(stats.Input!.Items, static item => item.Role == "system");
        Assert.Contains(stats.Input.Items, static item => item.Role == "user");
        Assert.DoesNotContain("system text", json, StringComparison.Ordinal);
        Assert.DoesNotContain("user text", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ShouldRecognizeGeminiSystemInstructionContentsAndToolDeclarations()
    {
        var contents = JsonSerializer.Deserialize<JsonElement[]>(
            """
            [
              { "role": "model", "parts": [ { "text": "previous answer" } ] },
              { "role": "user", "parts": [ { "text": "hello gemini" } ] }
            ]
            """)!;
        var payload = new Dictionary<string, object?>
        {
            ["systemInstruction"] = new Dictionary<string, object?>
            {
                ["parts"] = new object[]
                {
                    new Dictionary<string, object?> { ["text"] = "system root" },
                    new Dictionary<string, object?> { ["text"] = "developer hint" },
                },
            },
            ["contents"] = contents,
            ["tools"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["functionDeclarations"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["name"] = "read_file",
                            ["description"] = "Read a file",
                            ["parameters"] = new Dictionary<string, object?>
                            {
                                ["type"] = "object",
                            },
                        },
                        new Dictionary<string, object?>
                        {
                            ["name"] = "write_file",
                            ["description"] = "Write a file",
                            ["parameters"] = new Dictionary<string, object?>
                            {
                                ["type"] = "object",
                            },
                        },
                    },
                },
            },
        };

        var stats = ProviderRequestContextStatsBuilder.Build(
            payload,
            new ProviderRequestContextStatsBuildOptions
            {
                ThreadId = "thread-1",
                TurnId = "turn-1",
                RequestSequence = 2,
                Model = "gemini-2.5-pro",
                Provider = "google",
                Transport = "http",
                InputPropertyName = null,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var json = JsonSerializer.Serialize(stats, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        Assert.Equal("systemInstruction", stats.Instructions?.Key);
        Assert.True(stats.Instructions?.Chars > 0);
        Assert.Equal("contents", stats.Input?.Key);
        Assert.Equal(2, stats.Input?.Count);
        Assert.Contains(stats.Input!.Items, static item => item.Role == "model");
        Assert.Contains(stats.Input.Items, static item => item.Role == "user");
        Assert.Equal("tools", stats.Tools?.Key);
        Assert.Equal(2, stats.Tools?.Count);
        Assert.DoesNotContain("system root", json, StringComparison.Ordinal);
        Assert.DoesNotContain("developer hint", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hello gemini", json, StringComparison.Ordinal);
        Assert.DoesNotContain("read_file", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ShouldExposePayloadArtifactManifestWithoutInliningPayload()
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = "gpt-test",
            ["input"] = Array.Empty<JsonElement>(),
        };
        var manifest = new DiagnosticArtifactManifest
        {
            ArtifactId = "diag-artifact-1",
            ArtifactKind = "provider_request_payload",
            FileName = "provider-request-turn-1-1-http.sanitized.json",
            RelativePath = "provider-request-turn-1-1-http.sanitized.json",
            MediaType = "application/json",
            RedactionStatus = "sanitized",
            Sha256 = new string('a', 64),
            Bytes = 42,
            SourceEventName = DiagnosticStatisticsEventNames.ProviderRequestContextStats,
        };

        var stats = ProviderRequestContextStatsBuilder.Build(
            payload,
            new ProviderRequestContextStatsBuildOptions
            {
                ThreadId = "thread-1",
                TurnId = "turn-1",
                RequestSequence = 1,
                Model = "gpt-test",
                Provider = "openai",
                Transport = "http",
                InputPropertyName = "input",
                PayloadArtifact = manifest,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var payloadArtifact = Assert.IsType<DiagnosticArtifactManifest>(stats.PayloadArtifact);
        Assert.Same(manifest, payloadArtifact);
        Assert.Equal("provider_request_payload", payloadArtifact.ArtifactKind);
        Assert.Equal("provider-request-turn-1-1-http.sanitized.json", payloadArtifact.FileName);
    }
}
