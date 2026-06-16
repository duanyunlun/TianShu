using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Primitives;
using TianShu.Diagnostics;
using TianShu.RuntimeComposition;

namespace TianShu.AppHost.Tests;

public sealed class DiagnosticSinkRuntimeResolverTests
{
    [Fact]
    public async Task Create_WhenManifestExists_UsesArtifactTargetFromDiagnosticSinkPackage()
    {
        using var temp = TempTianShuHome.Create();
        WriteManifest(Path.Combine(temp.Root, "modules", "diagnostics", "sinks", "builtin", "sink.toml"));
        var turnLogEvents = new List<string>();
        var notifications = new List<string>();

        var sinkSet = DiagnosticRuntimeComposition.CreateDiagnosticSinks(
            temp.Root,
            new DefaultDiagnosticCollectionPolicy(() => new DiagnosticCollectionOptions
            {
                DefaultLevel = DiagnosticCollectionLevel.Artifact,
                Artifacts = new DiagnosticArtifactCollectionOptions { Enabled = true },
            }),
            (threadId, turnId, kind, status, summary, payload, cancellationToken) =>
            {
                turnLogEvents.Add($"{threadId}/{turnId}/{kind}/{status}/{summary}");
                return Task.CompletedTask;
            },
            (method, payload, cancellationToken) =>
            {
                notifications.Add(method);
                return Task.CompletedTask;
            },
            static diagnosticEvent => DiagnosticModuleNames.Provider,
            static _ => DiagnosticCollectionLevel.Stats);

        var operation = new DiagnosticOperationContext
        {
            OperationId = "op-1",
            OperationName = "provider_request",
            OperationKind = "turn.provider_request",
            ThreadId = "thread-1",
            TurnId = "turn-1",
        };

        await sinkSet.EventSink.EmitAsync(new DiagnosticEventEnvelope
        {
            EventName = "turn/provider_request/context_stats",
            Payload = StructuredValue.FromPlainObject(new { ok = true }),
            Operation = operation,
        }, CancellationToken.None);
        var manifest = await sinkSet.ProviderRequestPayloadArtifactWriter.WriteAsync(new DiagnosticArtifactWriteRequest
        {
            ArtifactKind = "provider_request_payload",
            FileName = "request.json",
            MediaType = "application/json",
            Content = """{"message":"ok"}""",
            Operation = operation,
            Metadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["diagnosticModule"] = StructuredValue.FromString(DiagnosticModuleNames.Provider),
            }),
        }, CancellationToken.None);

        Assert.Contains("builtin/turn-log", sinkSet.EnabledSinkIds);
        Assert.Contains("builtin/provider-request-artifacts", sinkSet.EnabledSinkIds);
        Assert.Single(turnLogEvents);
        Assert.Equal(["turn/provider_request/context_stats"], notifications);
        Assert.Equal("sanitized", manifest.RedactionStatus);
        Assert.True(File.Exists(Path.Combine(temp.Root, "modules", "diagnostics", "sinks", "builtin", "artifacts", "provider-requests", manifest.RelativePath)));
    }

    [Fact]
    public async Task Create_WhenNoManifestExists_UsesLocalFallback()
    {
        using var temp = TempTianShuHome.Create();
        var notifications = new List<string>();

        var sinkSet = DiagnosticRuntimeComposition.CreateDiagnosticSinks(
            temp.Root,
            new DefaultDiagnosticCollectionPolicy(() => new DiagnosticCollectionOptions
            {
                DefaultLevel = DiagnosticCollectionLevel.Artifact,
                Artifacts = new DiagnosticArtifactCollectionOptions { Enabled = true },
            }),
            (_, _, _, _, _, _, _) => Task.CompletedTask,
            (method, _, _) =>
            {
                notifications.Add(method);
                return Task.CompletedTask;
            },
            static diagnosticEvent => DiagnosticModuleNames.Provider,
            static _ => DiagnosticCollectionLevel.Stats);

        var operation = new DiagnosticOperationContext
        {
            OperationId = "op-1",
            OperationName = "provider_request",
            OperationKind = "turn.provider_request",
            ThreadId = "thread-1",
            TurnId = "turn-1",
        };

        await sinkSet.EventSink.EmitAsync(new DiagnosticEventEnvelope
        {
            EventName = "turn/provider_request/context_stats",
            Payload = StructuredValue.FromPlainObject(new { ok = true }),
            Operation = operation,
        }, CancellationToken.None);
        var manifest = await sinkSet.ProviderRequestPayloadArtifactWriter.WriteAsync(new DiagnosticArtifactWriteRequest
        {
            ArtifactKind = "provider_request_payload",
            FileName = "request.json",
            MediaType = "application/json",
            Content = "{}",
            Operation = operation,
            Metadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["diagnosticModule"] = StructuredValue.FromString(DiagnosticModuleNames.Provider),
            }),
        }, CancellationToken.None);

        Assert.Equal("fallback", sinkSet.SourceType);
        Assert.Equal(["turn/provider_request/context_stats"], notifications);
        Assert.True(File.Exists(Path.Combine(temp.Root, "data", "artifacts", "diagnostics", "provider-requests", manifest.RelativePath)));
    }

    private static void WriteManifest(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            """
            id = "builtin"
            display_name = "TianShu Builtin Diagnostics"
            enabled = true
            type = "builtin"
            priority = 0

            [[sinks]]
            id = "turn-log"
            enabled = true
            type = "turn-log"
            level = "stats"
            priority = 0

            [[sinks]]
            id = "provider-request-artifacts"
            enabled = true
            type = "artifact-file"
            target = "./artifacts/provider-requests"
            level = "artifact"
            priority = 10
            """);
    }

    private sealed class TempTianShuHome : IDisposable
    {
        private TempTianShuHome(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TempTianShuHome Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"tianshu-diagnostic-sink-runtime-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new TempTianShuHome(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

