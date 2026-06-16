using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools.Runtime.Diagnostics;
using TianShu.Contracts.Diagnostics;

namespace TianShu.AppHost.Tests;

public sealed class KernelDiagnosticsTraceQueryServiceTests
{
    [Fact]
    public async Task GetTraceAsync_WhenTurnLogHasProviderStatsAndBrokenPayload_ReturnsPartialTrace()
    {
        var root = Directory.CreateTempSubdirectory("tianshu-diagnostics-trace-");
        try
        {
            var store = new KernelStateSqliteStore(Path.Combine(root.FullName, "state.db"));
            await store.InitializeAsync(CancellationToken.None);
            await store.AppendTurnLogAsync(
                "thread-diag-1",
                "turn-diag-1",
                DiagnosticStatisticsEventNames.ProviderRequestContextStats,
                "info",
                "provider request stats",
                BuildProviderStatsEnvelope(),
                CancellationToken.None);
            await store.AppendTurnLogAsync(
                "thread-diag-1",
                "turn-diag-1",
                DiagnosticStatisticsEventNames.ContextSlicingReport,
                "info",
                "context slicing stats",
                BuildContextSlicingEnvelope(),
                CancellationToken.None);
            InsertBrokenTurnLog(store.DatabasePath);

            var service = new KernelDiagnosticsTraceQueryService(store);

            var trace = await service.GetTraceAsync("trace:turn-diag-1", null, null, null, CancellationToken.None);

            Assert.NotNull(trace);
            Assert.Equal("trace:turn-diag-1", trace.Id.Value);
            Assert.Single(trace.Attempts);
            Assert.Equal(3, trace.AuditTrail.Count);
            var provider = Assert.Single(trace.AuditTrail, static item => item.Category == "provider_request");
            Assert.Equal("provider-request-turn-diag-1-1-http.sanitized.json", provider.Metadata.GetString("artifactFileName"));
            Assert.Equal("128", provider.Metadata.GetString("estimatedPayloadTokens"));
            Assert.Equal("4096", provider.Metadata.GetString("serializedPayloadChars"));
            var slicing = Assert.Single(trace.AuditTrail, static item => item.Category == "context_slicing");
            Assert.Equal("50", slicing.Metadata.GetString("estimatedIncludedTokens"));
            Assert.Equal("120", slicing.Metadata.GetString("estimatedTotalTokens"));
            Assert.Contains(trace.AuditTrail, static item => item.Category == "diagnostic_payload_unreadable");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GetTraceAsync_WhenOperationIdProvided_FiltersTurnLogEvents()
    {
        var root = Directory.CreateTempSubdirectory("tianshu-diagnostics-trace-filter-");
        try
        {
            var store = new KernelStateSqliteStore(Path.Combine(root.FullName, "state.db"));
            await store.InitializeAsync(CancellationToken.None);
            await store.AppendTurnLogAsync(
                "thread-diag-2",
                "turn-diag-2",
                DiagnosticStatisticsEventNames.ProviderRequestContextStats,
                "info",
                "provider request stats",
                BuildProviderStatsEnvelope(),
                CancellationToken.None);
            await store.AppendTurnLogAsync(
                "thread-diag-2",
                "turn-diag-2",
                DiagnosticStatisticsEventNames.ContextSlicingReport,
                "info",
                "context slicing stats",
                BuildContextSlicingEnvelope(operationId: "diag-op-other"),
                CancellationToken.None);

            var service = new KernelDiagnosticsTraceQueryService(store);

            var trace = await service.GetTraceAsync("trace:turn-diag-2", null, null, "diag-op-provider", CancellationToken.None);

            Assert.NotNull(trace);
            var audit = Assert.Single(trace.AuditTrail);
            Assert.Equal("provider_request", audit.Category);
            Assert.Equal("diag-op-provider", audit.Metadata.GetString("operationId"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AppHostServer_DiagnosticsTraceRead_ReturnsArtifactBackedTrace()
    {
        var root = Directory.CreateTempSubdirectory("tianshu-diagnostics-trace-rpc-");
        try
        {
            var storePath = Path.Combine(root.FullName, "threads.json");
            var threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);
            await threadStore.StateStore.AppendTurnLogAsync(
                "thread-diag-rpc",
                "turn-diag-rpc",
                DiagnosticStatisticsEventNames.ProviderRequestContextStats,
                "info",
                "provider request stats",
                BuildProviderStatsEnvelope(),
                CancellationToken.None);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(
                """{"id":1,"method":"diagnostics/trace/read","params":{"traceId":"trace:turn-diag-rpc"}}"""));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var response = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .Single(static doc => IsResponseId(doc.RootElement, 1));
            var result = response.RootElement.GetProperty("result");
            Assert.Equal("trace:turn-diag-rpc", result.GetProperty("id").GetProperty("value").GetString());
            var audit = Assert.Single(result.GetProperty("auditTrail").EnumerateArray());
            Assert.Equal("provider_request", audit.GetProperty("category").GetString());
            Assert.Contains(
                "provider-request-turn-diag-1-1-http.sanitized.json",
                audit.GetProperty("message").GetString(),
                StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static object BuildProviderStatsEnvelope(string operationId = "diag-op-provider")
        => new
        {
            eventName = DiagnosticStatisticsEventNames.ProviderRequestContextStats,
            timestamp = DateTimeOffset.UtcNow,
            operation = new
            {
                operationId,
                operationName = "provider_request_context_stats",
                operationKind = "turn.provider_request",
                threadId = "thread-diag-1",
                turnId = "turn-diag-1",
                requestSequence = 1,
            },
            payload = new
            {
                threadId = "thread-diag-1",
                turnId = "turn-diag-1",
                requestSequence = 1,
                model = "gpt-test",
                provider = "openai",
                transport = "http",
                serializedPayloadChars = 4096,
                estimatedPayloadTokens = 128,
                input = new
                {
                    key = "input",
                    count = 3,
                    chars = 2048,
                    estimatedTokens = 64,
                },
                tools = new
                {
                    key = "tools",
                    count = 2,
                    chars = 512,
                    estimatedTokens = 16,
                },
                payloadArtifact = new
                {
                    artifactId = "diag-artifact-provider",
                    artifactKind = "provider_request_payload",
                    fileName = "provider-request-turn-diag-1-1-http.sanitized.json",
                    relativePath = "provider-requests/provider-request-turn-diag-1-1-http.sanitized.json",
                    mediaType = "application/json",
                    redactionStatus = "sanitized",
                    sha256 = "abc",
                    bytes = 1024,
                },
            },
        };

    private static object BuildContextSlicingEnvelope(string operationId = "diag-op-context")
        => new
        {
            eventName = DiagnosticStatisticsEventNames.ContextSlicingReport,
            timestamp = DateTimeOffset.UtcNow,
            operation = new
            {
                operationId,
                operationName = "context_slicing",
                operationKind = "turn.context_slicing",
                threadId = "thread-diag-1",
                turnId = "turn-diag-1",
            },
            payload = new
            {
                threadId = "thread-diag-1",
                turnId = "turn-diag-1",
                modelId = "gpt-test",
                providerId = "openai",
                tianShuBudgetTokens = 50,
                estimatedTotalTokens = 120,
                estimatedIncludedTokens = 50,
                includedSegments = new[] { new { segmentId = "user", estimatedTokens = 50 } },
                droppedSegments = new[] { new { segmentId = "history", estimatedTokens = 70 } },
            },
        };

    private static void InsertBrokenTurnLog(string databasePath)
    {
        using var connection = KernelNativeSqliteConnection.Open(databasePath);
        connection.Execute(
            "INSERT INTO turn_log(thread_id, turn_id, phase, status, summary, payload_json, created_at_unix_ms) VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7);",
            "thread-diag-1",
            "turn-diag-1",
            "broken/diagnostic",
            "info",
            "broken payload",
            """{"eventName":"broken" """,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private static bool IsResponseId(JsonElement json, long id)
        => json.TryGetProperty("id", out var idElement)
           && idElement.ValueKind == JsonValueKind.Number
           && idElement.TryGetInt64(out var numericId)
           && numericId == id;
}

internal static class MetadataBagTestExtensions
{
    public static string? GetString(this TianShu.Contracts.Primitives.MetadataBag metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value.GetString() : null;
}
