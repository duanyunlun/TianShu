using System.Reflection;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

public sealed class KernelReviewOutputParityTests
{
    [Fact]
    public void ParseReviewOutput_ShouldSupportDirectJsonPayload()
    {
        var payload =
            """
            {
              "findings": [
                {
                  "title": "Prefer Stylize helpers",
                  "body": "Use .dim()/.bold() chaining instead of manual Style.",
                  "confidence_score": 0.9,
                  "priority": 1,
                  "code_location": {
                    "absolute_file_path": "/tmp/file.rs",
                    "line_range": {
                      "start": 10,
                      "end": 20
                    }
                  }
                }
              ],
              "overall_correctness": "good",
              "overall_explanation": "Looks solid overall with minor polish suggested.",
              "overall_confidence_score": 0.75
            }
            """;

        var output = KernelReviewOutputParity.ParseReviewOutput(payload);

        Assert.Equal("good", output.OverallCorrectness);
        Assert.Equal("Looks solid overall with minor polish suggested.", output.OverallExplanation);
        var finding = Assert.Single(output.Findings);
        Assert.Equal("Prefer Stylize helpers", finding.Title);
        Assert.Equal("/tmp/file.rs", finding.CodeLocation.AbsoluteFilePath);
        Assert.Equal(10, finding.CodeLocation.LineRange.Start);
        Assert.Equal(20, finding.CodeLocation.LineRange.End);
    }

    [Fact]
    public void ParseReviewOutput_ShouldExtractEmbeddedJsonObject()
    {
        var payload =
            """
            reviewer output:
            {"findings":[],"overall_correctness":"ok","overall_explanation":"embedded review","overall_confidence_score":0.5}
            trailing text
            """;

        var output = KernelReviewOutputParity.ParseReviewOutput(payload);

        Assert.Equal("ok", output.OverallCorrectness);
        Assert.Equal("embedded review", output.OverallExplanation);
        Assert.Empty(output.Findings);
    }

    [Fact]
    public void RenderReviewOutputText_ShouldMatchCodexFindingLayout()
    {
        var payload =
            """
            {
              "findings": [
                {
                  "title": "Prefer Stylize helpers",
                  "body": "Use .dim()/.bold() chaining instead of manual Style.",
                  "confidence_score": 0.9,
                  "priority": 1,
                  "code_location": {
                    "absolute_file_path": "/tmp/file.rs",
                    "line_range": {
                      "start": 10,
                      "end": 20
                    }
                  }
                }
              ],
              "overall_correctness": "good",
              "overall_explanation": "Looks solid overall with minor polish suggested.",
              "overall_confidence_score": 0.75
            }
            """;

        var rendered = KernelReviewOutputParity.RenderReviewOutputText(payload);

        Assert.Contains("Looks solid overall with minor polish suggested.", rendered, StringComparison.Ordinal);
        Assert.Contains("Review comment:", rendered, StringComparison.Ordinal);
        Assert.Contains("Prefer Stylize helpers", rendered, StringComparison.Ordinal);
        Assert.Contains("/tmp/file.rs:10-20", rendered, StringComparison.Ordinal);
        Assert.Contains("Use .dim()/.bold() chaining instead of manual Style.", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderReviewOutputText_ShouldFallbackWhenOutputIsMissing()
    {
        Assert.Equal(
            KernelReviewOutputParity.ReviewFallbackMessage,
            KernelReviewOutputParity.RenderReviewOutputText(string.Empty));
        Assert.Equal(
            KernelReviewOutputParity.ReviewFallbackMessage,
            KernelReviewOutputParity.RenderReviewOutputText(
                """{"findings":[],"overall_correctness":"","overall_explanation":"","overall_confidence_score":0}"""));
    }

    [Fact]
    public void BuildReviewTurnRequestContext_ShouldAttachStructuredReviewSchema()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_review_schema_001";

        try
        {
            var cwd = Path.Combine(root, "repo");
            Directory.CreateDirectory(cwd);

            var sandboxPolicy = JsonSerializer.SerializeToElement(new
            {
                type = "readOnly",
            });

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                new KernelThreadStore(storePath));
            var session = new KernelThreadSessionState(
                Model: "gpt-5",
                ModelProvider: "openai",
                ServiceTier: KernelServiceTier.Flex,
                Cwd: cwd,
                ApprovalPolicy: KernelApprovalPolicy.Untrusted,
                SandboxPolicy: sandboxPolicy,
                SandboxMode: "read-only",
                AllowLoginShell: false,
                ShellEnvironmentPolicy: new KernelShellEnvironmentPolicy(KernelShellEnvironmentPolicyInherit.Core),
                DynamicTools: null,
                ProviderBaseUrl: "https://example.test/v1",
                ProviderApiKeyEnvironmentVariable: "TEST_API_KEY",
                ProviderWireApi: "responses",
                ProviderRequestMaxRetries: 2,
                ProviderStreamMaxRetries: 3,
                ProviderStreamIdleTimeoutMs: 4000,
                ProviderSupportsWebsockets: false,
                WebSearchMode: "live",
                PersistExtendedHistory: true,
                WindowsSandboxLevel: KernelWindowsSandboxLevel.Unelevated,
                DefaultModeRequestUserInputEnabled: true);

            var method = typeof(AppHostServer).GetMethod(
                "BuildReviewTurnRequestContext",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(string), typeof(KernelThreadSessionState), typeof(string), typeof(string)],
                modifiers: null);
            Assert.NotNull(method);

            var context = method!.Invoke(server, [threadId, session, "o3", "Review current diff."]);
            Assert.NotNull(context);

            var contextType = context!.GetType();
            var outputSchema = Assert.IsType<KernelJsonSchemaPayload>(contextType.GetProperty("OutputSchema")!.GetValue(context)!);
            using var schemaJson = JsonDocument.Parse(outputSchema.ToJsonElement().GetRawText());
            var properties = schemaJson.RootElement.GetProperty("properties");
            Assert.True(properties.TryGetProperty("findings", out _));
            Assert.True(properties.TryGetProperty("overall_correctness", out _));
            Assert.True(properties.TryGetProperty("overall_explanation", out _));
            Assert.True(properties.TryGetProperty("overall_confidence_score", out _));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShuKernelTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
