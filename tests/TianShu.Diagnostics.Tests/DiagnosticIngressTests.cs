using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;
using TianShu.Diagnostics;

namespace TianShu.Diagnostics.Tests;

public sealed class DiagnosticIngressTests
{
    [Fact]
    public async Task OperationScope_ShouldEmitCompletionWithCorrelationContext()
    {
        var sink = new InMemoryDiagnosticEventSink();
        var now = new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero);
        var factory = new DefaultDiagnosticOperationScopeFactory(
            sink,
            () => now,
            () => "operation-fixed");

        await using var scope = factory.BeginOperation(new DiagnosticOperationStart
        {
            OperationName = "provider_request",
            OperationKind = "turn.provider_request",
            TraceId = "trace-1",
            ThreadId = "thread-1",
            TurnId = "turn-1",
            RequestSequence = 2,
            Producer = "test",
        });

        await scope.CompleteAsync(new DiagnosticOperationCompletion(), CancellationToken.None);

        var diagnosticEvent = Assert.Single(sink.Snapshot());
        Assert.Equal("diagnostics/operation/completed", diagnosticEvent.EventName);
        Assert.Equal("operation-fixed", diagnosticEvent.Operation?.OperationId);
        Assert.Equal("thread-1", diagnosticEvent.Operation?.ThreadId);
        Assert.Equal(2, diagnosticEvent.Operation?.RequestSequence);
    }

    [Fact]
    public void Redactor_ShouldRedactSensitiveKeysAndText()
    {
        var redactor = new DefaultDiagnosticRedactor();
        var value = StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["authorization"] = "Bearer should-not-leak",
            ["nested"] = new Dictionary<string, object?>
            {
                ["api_key"] = "sk-secret",
                ["safe"] = "visible",
            },
        });

        var redacted = redactor.RedactStructuredValue(value);
        var json = JsonSerializer.Serialize(redacted, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var text = redactor.RedactText(null, "Authorization: Bearer top-secret-token");

        Assert.Contains("[REDACTED]", json, StringComparison.Ordinal);
        Assert.DoesNotContain("should-not-leak", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-secret", json, StringComparison.Ordinal);
        Assert.Contains("visible", json, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret-token", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileArtifactWriter_ShouldPersistSanitizedContentAndReturnManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "tianshu-diagnostics-tests", Guid.NewGuid().ToString("N"));
        var writer = new FileDiagnosticArtifactWriter(root, new DefaultDiagnosticRedactor(), () => new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero));
        var operation = new DiagnosticOperationContext
        {
            OperationId = "op-1",
            OperationName = "provider_request_capture",
            OperationKind = "artifact",
            ThreadId = "thread-1",
            TurnId = "turn-1",
        };

        var manifest = await writer.WriteAsync(new DiagnosticArtifactWriteRequest
        {
            ArtifactKind = "provider_request",
            FileName = "../provider-request.json",
            MediaType = "application/json",
            Content = """{"authorization":"Bearer secret","message":"ok"}""",
            SourceEventName = DiagnosticStatisticsEventNames.ProviderRequestContextStats,
            Operation = operation,
        }, CancellationToken.None);

        var filePath = Path.Combine(root, manifest.RelativePath);
        var bytes = await File.ReadAllBytesAsync(filePath, CancellationToken.None);
        var content = await File.ReadAllTextAsync(filePath, CancellationToken.None);
        var fileHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.Equal("provider-request.json", manifest.FileName);
        Assert.Equal("sanitized", manifest.RedactionStatus);
        Assert.Equal(fileHash, manifest.Sha256);
        Assert.Equal(bytes.LongLength, manifest.Bytes);
        Assert.Contains("ok", content, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderStatsDiagnosticBuilder_ShouldUseOperationCorrelation()
    {
        var builder = new ProviderRequestContextStatsDiagnosticBuilder(
            new ProviderRequestContextStatsDiagnosticBuilderOptions
            {
                Model = "gpt-test",
                Provider = "openai",
                Transport = "http",
                InputPropertyName = "messages",
            });
        var context = new DiagnosticOperationContext
        {
            OperationId = "op-1",
            OperationName = "provider_request",
            OperationKind = "turn.provider_request",
            ThreadId = "thread-1",
            TurnId = "turn-1",
            RequestSequence = 4,
        };

        var stats = builder.Build(new Dictionary<string, object?>
        {
            ["messages"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = "hello",
                },
            },
        }, context);

        Assert.Equal("thread-1", stats.ThreadId);
        Assert.Equal("turn-1", stats.TurnId);
        Assert.Equal(4, stats.RequestSequence);
        Assert.Equal("messages", stats.Input?.Key);
    }

    [Fact]
    public void CollectionOptionsReader_ShouldReadModuleArtifactAndTelemetryOptions()
    {
        var options = DiagnosticCollectionOptionsReader.FromConfig(new Dictionary<string, object?>
        {
            ["diagnostics"] = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["default_level"] = "summary",
                ["modules"] = new Dictionary<string, object?>
                {
                    ["provider"] = new Dictionary<string, object?>
                    {
                        ["level"] = "artifact",
                        ["sample_rate"] = 0.25d,
                        ["max_items"] = 7,
                    },
                },
                ["artifacts"] = new Dictionary<string, object?>
                {
                    ["enabled"] = true,
                    ["max_bytes"] = 1024L,
                },
                ["telemetry"] = new Dictionary<string, object?>
                {
                    ["enabled"] = true,
                    ["sinks"] = new[] { "local", "otlp" },
                },
            },
        });

        Assert.True(options.Enabled);
        Assert.Equal(DiagnosticCollectionLevel.Summary, options.DefaultLevel);
        Assert.Equal(DiagnosticCollectionLevel.Artifact, options.Modules["provider"].Level);
        Assert.Equal(0.25d, options.Modules["provider"].SampleRate);
        Assert.Equal(7, options.Modules["provider"].MaxItems);
        Assert.True(options.Artifacts.Enabled);
        Assert.Equal(1024L, options.Artifacts.MaxBytes);
        Assert.True(options.Telemetry.Enabled);
        Assert.Equal(["local", "otlp"], options.Telemetry.Sinks);
    }

    [Fact]
    public async Task FilteringEventSink_ShouldDropEventsBelowConfiguredLevel()
    {
        var inner = new InMemoryDiagnosticEventSink();
        var sink = new FilteringDiagnosticEventSink(
            inner,
            new DefaultDiagnosticCollectionPolicy(() => new DiagnosticCollectionOptions
            {
                DefaultLevel = DiagnosticCollectionLevel.Summary,
            }));

        await sink.EmitAsync(new DiagnosticEventEnvelope
        {
            EventName = DiagnosticStatisticsEventNames.ProviderRequestContextStats,
            Payload = StructuredValue.FromPlainObject(new { ok = true }),
        }, CancellationToken.None);

        Assert.Empty(inner.Snapshot());
    }

    [Fact]
    public void CollectionPolicy_ShouldKeepFailureEventsWhenSampleRateIsZero()
    {
        var policy = new DefaultDiagnosticCollectionPolicy(() => new DiagnosticCollectionOptions
        {
            DefaultLevel = DiagnosticCollectionLevel.Stats,
            Modules = new Dictionary<string, DiagnosticModuleCollectionOptions>(StringComparer.OrdinalIgnoreCase)
            {
                [DiagnosticModuleNames.Provider] = new()
                {
                    Level = DiagnosticCollectionLevel.Stats,
                    SampleRate = 0.0d,
                },
            },
        });
        var metadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["status"] = StructuredValue.FromString("failed"),
        });

        var decision = policy.ShouldCollect(
            DiagnosticStatisticsEventNames.ProviderRequestContextStats,
            DiagnosticModuleNames.Provider,
            DiagnosticCollectionLevel.Stats,
            operation: null,
            metadata);

        Assert.True(decision.ShouldCollect);
        Assert.Equal(DiagnosticModuleNames.Provider, decision.ModuleName);
    }

    [Fact]
    public void CollectionPolicy_ShouldEnforceModuleMaxItems()
    {
        var policy = new DefaultDiagnosticCollectionPolicy(() => new DiagnosticCollectionOptions
        {
            DefaultLevel = DiagnosticCollectionLevel.Stats,
            Modules = new Dictionary<string, DiagnosticModuleCollectionOptions>(StringComparer.OrdinalIgnoreCase)
            {
                [DiagnosticModuleNames.Provider] = new()
                {
                    Level = DiagnosticCollectionLevel.Stats,
                    MaxItems = 2,
                },
            },
        });

        var first = policy.ShouldCollect(
            DiagnosticStatisticsEventNames.ProviderRequestContextStats,
            DiagnosticModuleNames.Provider,
            DiagnosticCollectionLevel.Stats,
            operation: null,
            MetadataBag.Empty);
        var second = policy.ShouldCollect(
            DiagnosticStatisticsEventNames.ProviderRequestContextStats,
            DiagnosticModuleNames.Provider,
            DiagnosticCollectionLevel.Stats,
            operation: null,
            MetadataBag.Empty);
        var third = policy.ShouldCollect(
            DiagnosticStatisticsEventNames.ProviderRequestContextStats,
            DiagnosticModuleNames.Provider,
            DiagnosticCollectionLevel.Stats,
            operation: null,
            MetadataBag.Empty);

        Assert.True(first.ShouldCollect);
        Assert.True(second.ShouldCollect);
        Assert.False(third.ShouldCollect);
        Assert.Equal(["diagnostics_max_items_exceeded"], third.ReasonCodes);
    }

    [Fact]
    public async Task FilteringEventSink_ShouldDropEventsAfterModuleMaxItems()
    {
        var inner = new InMemoryDiagnosticEventSink();
        var sink = new FilteringDiagnosticEventSink(
            inner,
            new DefaultDiagnosticCollectionPolicy(() => new DiagnosticCollectionOptions
            {
                DefaultLevel = DiagnosticCollectionLevel.Stats,
                Modules = new Dictionary<string, DiagnosticModuleCollectionOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    [DiagnosticModuleNames.Provider] = new()
                    {
                        Level = DiagnosticCollectionLevel.Stats,
                        MaxItems = 1,
                    },
                },
            }));

        await sink.EmitAsync(new DiagnosticEventEnvelope
        {
            EventName = DiagnosticStatisticsEventNames.ProviderRequestContextStats,
            Payload = StructuredValue.FromPlainObject(new { index = 1 }),
        }, CancellationToken.None);
        await sink.EmitAsync(new DiagnosticEventEnvelope
        {
            EventName = DiagnosticStatisticsEventNames.ProviderRequestContextStats,
            Payload = StructuredValue.FromPlainObject(new { index = 2 }),
        }, CancellationToken.None);

        Assert.Single(inner.Snapshot());
    }

    [Fact]
    public async Task FilteringArtifactWriter_ShouldSkipWhenArtifactsAreDisabled()
    {
        var inner = new FileDiagnosticArtifactWriter(Path.Combine(Path.GetTempPath(), "tianshu-diagnostics-tests", Guid.NewGuid().ToString("N")));
        var writer = new FilteringDiagnosticArtifactWriter(
            inner,
            new DefaultDiagnosticCollectionPolicy(() => new DiagnosticCollectionOptions
            {
                Artifacts = new DiagnosticArtifactCollectionOptions { Enabled = false },
            }));

        var manifest = await writer.WriteAsync(new DiagnosticArtifactWriteRequest
        {
            ArtifactKind = "provider_request",
            FileName = "request.json",
            MediaType = "application/json",
            Content = "{}",
            SourceEventName = DiagnosticStatisticsEventNames.ProviderRequestContextStats,
        }, CancellationToken.None);

        Assert.Equal("skipped_by_policy", manifest.RedactionStatus);
        Assert.Equal(0, manifest.Bytes);
        Assert.Equal(string.Empty, manifest.RelativePath);
    }

    [Fact]
    public async Task FilteringArtifactWriter_ShouldUseModuleLevelFromMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "tianshu-diagnostics-tests", Guid.NewGuid().ToString("N"));
        var writer = new FilteringDiagnosticArtifactWriter(
            new FileDiagnosticArtifactWriter(root),
            new DefaultDiagnosticCollectionPolicy(() => new DiagnosticCollectionOptions
            {
                DefaultLevel = DiagnosticCollectionLevel.Stats,
                Artifacts = new DiagnosticArtifactCollectionOptions { Enabled = true },
                Modules = new Dictionary<string, DiagnosticModuleCollectionOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    [DiagnosticModuleNames.Provider] = new()
                    {
                        Level = DiagnosticCollectionLevel.Artifact,
                    },
                },
            }));

        var manifest = await writer.WriteAsync(new DiagnosticArtifactWriteRequest
        {
            ArtifactKind = "provider_request_payload",
            FileName = "request.json",
            MediaType = "application/json",
            Content = """{"message":"ok"}""",
            SourceEventName = DiagnosticStatisticsEventNames.ProviderRequestContextStats,
            Metadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["diagnosticModule"] = StructuredValue.FromString(DiagnosticModuleNames.Provider),
            }),
        }, CancellationToken.None);

        Assert.Equal("sanitized", manifest.RedactionStatus);
        Assert.True(File.Exists(Path.Combine(root, manifest.RelativePath)));
    }

    [Fact]
    public async Task FilteringArtifactWriter_ShouldEnforceModuleMaxItems()
    {
        var root = Path.Combine(Path.GetTempPath(), "tianshu-diagnostics-tests", Guid.NewGuid().ToString("N"));
        var writer = new FilteringDiagnosticArtifactWriter(
            new FileDiagnosticArtifactWriter(root),
            new DefaultDiagnosticCollectionPolicy(() => new DiagnosticCollectionOptions
            {
                DefaultLevel = DiagnosticCollectionLevel.Artifact,
                Artifacts = new DiagnosticArtifactCollectionOptions { Enabled = true },
                Modules = new Dictionary<string, DiagnosticModuleCollectionOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    [DiagnosticModuleNames.Provider] = new()
                    {
                        Level = DiagnosticCollectionLevel.Artifact,
                        MaxItems = 1,
                    },
                },
            }));
        var metadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["diagnosticModule"] = StructuredValue.FromString(DiagnosticModuleNames.Provider),
        });

        var first = await writer.WriteAsync(new DiagnosticArtifactWriteRequest
        {
            ArtifactKind = "provider_request_payload",
            FileName = "request-1.json",
            MediaType = "application/json",
            Content = """{"message":"first"}""",
            SourceEventName = DiagnosticStatisticsEventNames.ProviderRequestContextStats,
            Metadata = metadata,
        }, CancellationToken.None);
        var second = await writer.WriteAsync(new DiagnosticArtifactWriteRequest
        {
            ArtifactKind = "provider_request_payload",
            FileName = "request-2.json",
            MediaType = "application/json",
            Content = """{"message":"second"}""",
            SourceEventName = DiagnosticStatisticsEventNames.ProviderRequestContextStats,
            Metadata = metadata,
        }, CancellationToken.None);

        Assert.Equal("sanitized", first.RedactionStatus);
        Assert.True(File.Exists(Path.Combine(root, first.RelativePath)));
        Assert.Equal("skipped_by_policy", second.RedactionStatus);
        Assert.Equal(0, second.Bytes);
        Assert.Equal(string.Empty, second.RelativePath);
    }

    [Fact]
    public async Task FilteringArtifactWriter_ShouldSkipWhenArtifactExceedsMaxBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "tianshu-diagnostics-tests", Guid.NewGuid().ToString("N"));
        var writer = new FilteringDiagnosticArtifactWriter(
            new FileDiagnosticArtifactWriter(root),
            new DefaultDiagnosticCollectionPolicy(() => new DiagnosticCollectionOptions
            {
                DefaultLevel = DiagnosticCollectionLevel.Artifact,
                Artifacts = new DiagnosticArtifactCollectionOptions
                {
                    Enabled = true,
                    MaxBytes = 8,
                },
            }));

        var manifest = await writer.WriteAsync(new DiagnosticArtifactWriteRequest
        {
            ArtifactKind = "provider_request_payload",
            FileName = "request.json",
            MediaType = "application/json",
            Content = """{"message":"too-large"}""",
            SourceEventName = DiagnosticStatisticsEventNames.ProviderRequestContextStats,
            Metadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["diagnosticModule"] = StructuredValue.FromString(DiagnosticModuleNames.Provider),
            }),
        }, CancellationToken.None);

        Assert.Equal("skipped_by_policy", manifest.RedactionStatus);
        Assert.Equal(0, manifest.Bytes);
        Assert.Equal(string.Empty, manifest.RelativePath);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void CollectionPolicy_ShouldClassifyApprovalAndUserInputAsGovernanceBeforeTool()
    {
        Assert.Equal(
            DiagnosticModuleNames.Governance,
            DefaultDiagnosticCollectionPolicy.InferModuleName("item/tool/requestUserInput"));
        Assert.Equal(
            DiagnosticModuleNames.Governance,
            DefaultDiagnosticCollectionPolicy.InferModuleName("item/commandExecution/requestApproval"));
    }

    [Fact]
    public void RuntimeNotificationStats_ShouldCarryDirectEventFieldsForModuleConsumers()
    {
        var stats = new RuntimeNotificationStats
        {
            ModuleName = DiagnosticModuleNames.Tool,
            Method = "item/tool/call",
            OperationCategory = "tool_call_started",
            ParameterSummary = "tool=shell_command",
            PermissionDecision = "approved",
            ExecutionResult = "completed",
            ArtifactReference = "artifacts/tool-output.txt",
            RiskSource = "policy_rule",
            PolicyRule = "shell.requires_approval",
            UserDecision = "approve",
            MemoryAuditId = "memory-audit-1",
            DiagnosticOperationId = "diagnostic-op-1",
            SerializedPayloadChars = 128,
            EstimatedPayloadTokens = 32,
        };

        Assert.Equal("tool_call_started", stats.OperationCategory);
        Assert.Equal("shell.requires_approval", stats.PolicyRule);
        Assert.Equal("diagnostic-op-1", stats.DiagnosticOperationId);
    }

    [Fact]
    public async Task DiagnosticsModuleAdapter_ShouldEmitFourEventKindsWithStandardRefs()
    {
        var sink = new InMemoryDiagnosticEventSink();
        var adapter = new DiagnosticsModuleAdapter(sink);

        await adapter.EmitAsync(CreateModuleEvent(DiagnosticsModuleEventKind.KernelTrace, "diagnostics/kernel/trace"), CancellationToken.None);
        await adapter.EmitAsync(CreateModuleEvent(DiagnosticsModuleEventKind.ExecutionRuntimeStep, "diagnostics/runtime/step"), CancellationToken.None);
        await adapter.EmitAsync(CreateModuleEvent(DiagnosticsModuleEventKind.ModuleCall, "diagnostics/module/call"), CancellationToken.None);
        await adapter.EmitAsync(CreateModuleEvent(DiagnosticsModuleEventKind.ValidationRejection, "diagnostics/validation/rejection"), CancellationToken.None);

        var events = sink.Snapshot();
        Assert.Equal(4, events.Count);
        Assert.All(events, diagnosticEvent =>
        {
            Assert.Equal("diagnostics", diagnosticEvent.Producer);
            Assert.True(diagnosticEvent.Metadata.TryGetValue("diagnosticsRef", out var diagnosticsRef));
            Assert.True(diagnosticEvent.Metadata.TryGetValue("traceRef", out var traceRef));
            Assert.StartsWith("diagnostics://", diagnosticsRef.GetString(), StringComparison.Ordinal);
            Assert.StartsWith("trace://", traceRef.GetString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task DiagnosticsModuleAdapter_ShouldRedactSecretsHeadersAndAbsolutePathsBeforeSink()
    {
        var sink = new InMemoryDiagnosticEventSink();
        var adapter = new DiagnosticsModuleAdapter(sink);
        var diagnosticEvent = new DiagnosticsModuleEvent(
            DiagnosticsModuleEventKind.KernelTrace,
            "diagnostics/kernel/trace",
            StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["authorization"] = "Bearer should-not-leak",
                ["raw_headers"] = "X-Api-Key: should-not-leak",
                ["workspacePath"] = @"C:\Users\Example\secret\file.txt",
                ["unixPath"] = "/home/semi/secret/file.txt",
            }),
            new DiagnosticsModuleEventContext(
                kernelRunId: new KernelRunId("kernel-run-001"),
                metadata: new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["http_headers"] = StructuredValue.FromString("Cookie: should-not-leak"),
                    ["failurePath"] = StructuredValue.FromString(@"D:\Work\TianShu\secret.txt"),
                })),
            failureMessage: @"failed at C:\Users\Example\secret\file.txt with token=should-not-leak");

        var result = await adapter.EmitAsync(diagnosticEvent, CancellationToken.None);
        var emitted = Assert.Single(sink.Snapshot());
        var payloadJson = JsonSerializer.Serialize(emitted.Payload, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var metadataJson = JsonSerializer.Serialize(emitted.Metadata.Entries, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        Assert.True(result.Success);
        Assert.Contains("[REDACTED]", payloadJson, StringComparison.Ordinal);
        Assert.Contains("[REDACTED_PATH]", payloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("should-not-leak", payloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Example", payloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("should-not-leak", metadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Example", metadataJson, StringComparison.OrdinalIgnoreCase);
    }

    private static DiagnosticsModuleEvent CreateModuleEvent(DiagnosticsModuleEventKind kind, string eventName)
    {
        var context = kind is DiagnosticsModuleEventKind.ExecutionRuntimeStep or DiagnosticsModuleEventKind.ModuleCall
            ? new DiagnosticsModuleEventContext(
                kernelRunId: new KernelRunId("kernel-run-001"),
                executionId: new ExecutionId("execution-001"),
                runtimeStepId: "diagnostics-step-001",
                sourceIntentId: new CoreIntentId("intent-001"),
                sourceGraphId: new StageGraphId("graph-001"),
                sourceStageId: new StageId("stage-001"),
                sourceKernelOperationId: new KernelOperationId("operation-001"),
                moduleId: kind is DiagnosticsModuleEventKind.ModuleCall ? "memory.identity" : null,
                capabilityId: kind is DiagnosticsModuleEventKind.ModuleCall ? "memory.identity.filter" : null)
            : new DiagnosticsModuleEventContext(
                kernelRunId: new KernelRunId("kernel-run-001"),
                sourceIntentId: new CoreIntentId("intent-001"),
                sourceGraphId: new StageGraphId("graph-001"),
                sourceStageId: new StageId("stage-001"),
                sourceKernelOperationId: new KernelOperationId("operation-001"));

        return new DiagnosticsModuleEvent(
            kind,
            eventName,
            StructuredValue.FromPlainObject(new { ok = true }),
            context);
    }
}
