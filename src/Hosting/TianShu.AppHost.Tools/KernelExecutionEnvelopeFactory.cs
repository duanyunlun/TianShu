using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using ContractExecutionContext = TianShu.Contracts.Execution.ExecutionContext;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 将本地环境执行请求与结果投影为统一 execution envelope 的工厂。
/// Factory that projects local environment execution requests and results into the shared execution envelope model.
/// </summary>
internal static class KernelExecutionEnvelopeFactory
{
    private const string ExecutionKindMetadataKey = "executionKind";
    private const string ItemIdMetadataKey = "itemId";
    private const string RuntimeSurface = "kernel-app-server";

    private static readonly CollaborationSpaceRef KernelCollaborationSpaceRef =
        new(new CollaborationSpaceId("tianshu-runtime"), "tianshu-runtime", "TianShu Runtime");

    private static readonly ParticipantRef KernelParticipantRef =
        new(new ParticipantId("kernel-app-server"), ParticipantKind.Service, "Kernel AppServer");

    public static ExecutionRequest CreateArtifactsRequest(
        string threadId,
        string turnId,
        string? cwd,
        KernelArtifactsExecutionRequest request,
        InteractionEnvelopeRef? interactionEnvelope = null)
        => CreateRequest(
            kind: ExecutionKind.ArtifactProcessing,
            executionKindLabel: "Artifacts",
            action: "execute",
            threadId: threadId,
            turnId: turnId,
            itemId: null,
            cwd: cwd,
            interactionEnvelope: interactionEnvelope,
            input: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["source"] = request.Source,
                ["timeoutMs"] = request.TimeoutMs,
            });

    public static ExecutionRequest CreateCodeModeExecuteRequest(
        string threadId,
        string turnId,
        string? cwd,
        KernelCodeModeExecutionRequest request,
        InteractionEnvelopeRef? interactionEnvelope = null)
        => CreateRequest(
            kind: ExecutionKind.EnvironmentAction,
            executionKindLabel: "CodeMode",
            action: "execute",
            threadId: threadId,
            turnId: turnId,
            itemId: null,
            cwd: cwd,
            interactionEnvelope: interactionEnvelope,
            input: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = request.Code,
                ["yieldTimeMs"] = request.YieldTimeMs,
                ["maxOutputTokens"] = request.MaxOutputTokens,
            });

    public static ExecutionRequest CreateCodeModeWaitRequest(
        string threadId,
        string turnId,
        string? cwd,
        KernelCodeModeWaitRequest request,
        InteractionEnvelopeRef? interactionEnvelope = null)
        => CreateRequest(
            kind: ExecutionKind.EnvironmentAction,
            executionKindLabel: "CodeMode",
            action: request.Terminate ? "terminate" : "wait",
            threadId: threadId,
            turnId: turnId,
            itemId: null,
            cwd: cwd,
            interactionEnvelope: interactionEnvelope,
            input: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["cellId"] = request.CellId,
                ["yieldTimeMs"] = request.YieldTimeMs,
                ["maxTokens"] = request.MaxTokens,
                ["terminate"] = request.Terminate,
            });

    public static ExecutionRequest CreateManagedNetworkRequest(KernelManagedNetworkExecutionEnvelopeRequest request)
        => CreateRequest(
            kind: ExecutionKind.EnvironmentAction,
            executionKindLabel: "ManagedNetwork",
            action: "begin",
            threadId: request.ThreadId,
            turnId: request.TurnId,
            itemId: request.ItemId,
            cwd: request.Cwd,
            interactionEnvelope: request.InteractionEnvelope,
            input: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["command"] = request.Command,
                ["sandboxMode"] = request.SandboxMode,
                ["approvalPolicy"] = request.ApprovalPolicy?.ToPlainObject(),
                ["skillAllowedDomains"] = request.SkillAllowedDomains?.ToArray(),
                ["skillDeniedDomains"] = request.SkillDeniedDomains?.ToArray(),
                ["hasSandboxPolicy"] = request.SandboxPolicy is { ValueKind: not System.Text.Json.JsonValueKind.Null and not System.Text.Json.JsonValueKind.Undefined },
            });

    public static ExecutionRequest CreateJsReplRequest(
        string threadId,
        string turnId,
        string? cwd,
        KernelJsReplExecutionRequest request,
        InteractionEnvelopeRef? interactionEnvelope = null)
        => CreateRequest(
            kind: ExecutionKind.EnvironmentAction,
            executionKindLabel: "JsRepl",
            action: "execute",
            threadId: threadId,
            turnId: turnId,
            itemId: null,
            cwd: cwd,
            interactionEnvelope: interactionEnvelope,
            input: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = request.Code,
                ["timeoutMs"] = request.TimeoutMs,
            });

    public static ExecutionRequest CreateJsReplResetRequest(
        string threadId,
        string turnId,
        string? cwd,
        InteractionEnvelopeRef? interactionEnvelope = null)
        => CreateRequest(
            kind: ExecutionKind.EnvironmentAction,
            executionKindLabel: "JsRepl",
            action: "reset",
            threadId: threadId,
            turnId: turnId,
            itemId: null,
            cwd: cwd,
            interactionEnvelope: interactionEnvelope,
            input: new Dictionary<string, object?>(StringComparer.Ordinal));

    public static ExecutionStarted CreateStartedEvent(ExecutionRequest request, string? message = null, StructuredValue? data = null)
        => new(request.ExecutionId, 1, DateTimeOffset.UtcNow, message, data);

    public static ExecutionProgressed CreateProgressEvent(ExecutionRequest request, string? message = null, StructuredValue? data = null)
        => new(request.ExecutionId, request.Action, DateTimeOffset.UtcNow, message, data);

    public static ExecutionCompleted CreateCompletedEvent(ExecutionRequest request, string? message = null, StructuredValue? data = null)
        => new(request.ExecutionId, occurredAt: DateTimeOffset.UtcNow, message: message, data: data);

    public static ExecutionFailed CreateFailedEvent(ExecutionRequest request, string? message = null, StructuredValue? data = null)
    {
        var failureMessage = string.IsNullOrWhiteSpace(message)
            ? "execution failed"
            : message;
        return new ExecutionFailed(
            request.ExecutionId,
            new ExecutionFailure("kernel.execution_failed", failureMessage, details: data),
            1,
            DateTimeOffset.UtcNow);
    }

    public static ExecutionHandle CreateCodeModeHandle(ExecutionRequest request, KernelCodeModeOperationResult result)
    {
        var cellId = TryExtractCodeModeCellId(result.ContentItems);
        return CreateHandle(
            request,
            nativeHandleId: cellId,
            metadata: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["status"] = InferCodeModeStatus(result.ContentItems, result.Success),
            });
    }

    public static ExecutionHandle CreateCodeModeWaitHandle(ExecutionRequest request, KernelCodeModeWaitRequest waitRequest)
        => CreateHandle(
            request,
            nativeHandleId: waitRequest.CellId,
            metadata: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["terminate"] = waitRequest.Terminate,
            });

    public static StructuredValue CreateArtifactsData(KernelArtifactsExecutionResult result)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["success"] = result.Success,
            ["output"] = result.Output,
        });

    public static StructuredValue CreateCodeModeData(ExecutionHandle? handle, KernelCodeModeOperationResult result)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["success"] = result.Success,
            ["status"] = InferCodeModeStatus(result.ContentItems, result.Success),
            ["output"] = result.Output,
            ["handle"] = ToPlainHandle(handle),
            ["contentItems"] = result.ContentItems.Select(static item => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = item.Type,
                ["text"] = item.Text,
                ["imageUrl"] = item.ImageUrl,
                ["detail"] = item.Detail,
            }).ToArray(),
        });

    public static StructuredValue CreateManagedNetworkData(ExecutionRequest request, KernelManagedNetworkExecutionLeaseSnapshot lease)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["active"] = lease.IsActive,
            ["handle"] = ToPlainHandle(CreateManagedNetworkHandle(request, lease)),
            ["httpProxyUrl"] = lease.HttpProxyUrl,
            ["socksProxyUrl"] = lease.SocksProxyUrl,
            ["blockedRequestTotal"] = lease.BlockedRequestTotal,
        });

    public static StructuredValue CreateJsReplData(KernelJsReplExecutionResult result)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["success"] = result.Success,
            ["output"] = result.Output,
            ["contentItems"] = result.ContentItems.Select(static item => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = item.Type,
                ["text"] = item.Text,
                ["imageUrl"] = item.ImageUrl,
                ["detail"] = item.Detail,
            }).ToArray(),
        });

    public static StructuredValue CreateFailureData(string? message, string? details = null)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["message"] = message,
            ["details"] = details,
        });

    private static ExecutionRequest CreateRequest(
        ExecutionKind kind,
        string executionKindLabel,
        string action,
        string? threadId,
        string? turnId,
        string? itemId,
        string? cwd,
        InteractionEnvelopeRef? interactionEnvelope,
        IReadOnlyDictionary<string, object?> input)
    {
        var executionId = new ExecutionId(Guid.NewGuid().ToString("N"));
        var createdAt = DateTimeOffset.UtcNow;
        var context = CreateContext(executionId, threadId, turnId, itemId, cwd, executionKindLabel, createdAt, interactionEnvelope);
        return new ExecutionRequest(
            executionId,
            kind,
            action,
            context,
            StructuredValue.FromPlainObject(input),
            createdAt);
    }

    private static ContractExecutionContext CreateContext(
        ExecutionId executionId,
        string? threadId,
        string? turnId,
        string? itemId,
        string? cwd,
        string executionKindLabel,
        DateTimeOffset createdAt,
        InteractionEnvelopeRef? interactionEnvelope)
    {
        var metadata = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            [ExecutionKindMetadataKey] = StructuredValue.FromString(executionKindLabel),
        };
        if (!string.IsNullOrWhiteSpace(itemId))
        {
            metadata[ItemIdMetadataKey] = StructuredValue.FromString(itemId);
        }

        return new ContractExecutionContext(
            KernelCollaborationSpaceRef,
            interactionEnvelope
            ?? new InteractionEnvelopeRef(
                new InteractionEnvelopeId($"execution-{executionId}"),
                InteractionSourceKind.Host,
                RuntimeSurface,
                createdAt),
            KernelParticipantRef,
            threadId: ToThreadId(threadId),
            turnId: ToTurnId(turnId),
            workingDirectory: cwd,
            metadata: new MetadataBag(metadata));
    }

    private static ExecutionHandle CreateManagedNetworkHandle(ExecutionRequest request, KernelManagedNetworkExecutionLeaseSnapshot lease)
    {
        var nativeHandleId = lease.IsActive
            ? TryGetMetadataString(request.Context.Metadata, ItemIdMetadataKey) ?? request.ExecutionId.ToString()
            : null;
        return CreateHandle(
            request,
            nativeHandleId,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["active"] = lease.IsActive,
            });
    }

    private static ExecutionHandle CreateHandle(
        ExecutionRequest request,
        string? nativeHandleId,
        IReadOnlyDictionary<string, object?> metadata)
    {
        return new ExecutionHandle(
            request.ExecutionId,
            request.Kind,
            request.Context,
            new ExecutionAttempt(1, request.CreatedAt, nativeHandleId: nativeHandleId),
            CreateMetadataBag(metadata));
    }

    private static object? ToPlainHandle(ExecutionHandle? handle)
    {
        if (handle is null)
        {
            return null;
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["executionId"] = handle.ExecutionId.ToString(),
            ["kind"] = ResolveExecutionKind(handle.Context.Metadata, handle.Kind),
            ["threadId"] = handle.Context.ThreadId?.ToString(),
            ["turnId"] = handle.Context.TurnId?.ToString(),
            ["itemId"] = TryGetMetadataString(handle.Context.Metadata, ItemIdMetadataKey),
            ["nativeHandleId"] = handle.CurrentAttempt.NativeHandleId,
            ["metadata"] = ToPlainMetadata(handle.Metadata),
        };
    }

    private static object? ToPlainMetadata(MetadataBag metadata)
    {
        if (metadata.Count == 0)
        {
            return null;
        }

        return metadata.Entries.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToPlainObject(),
            StringComparer.Ordinal);
    }

    private static MetadataBag CreateMetadataBag(IReadOnlyDictionary<string, object?> values)
    {
        if (values.Count == 0)
        {
            return MetadataBag.Empty;
        }

        return new MetadataBag(values.ToDictionary(
            static pair => pair.Key,
            static pair => StructuredValue.FromPlainObject(pair.Value),
            StringComparer.Ordinal));
    }

    private static string ResolveExecutionKind(MetadataBag metadata, ExecutionKind kind)
        => TryGetMetadataString(metadata, ExecutionKindMetadataKey) ?? kind.ToString();

    private static string? TryGetMetadataString(MetadataBag metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.Kind == StructuredValueKind.String
            ? value.StringValue
            : value.ToPlainObject()?.ToString();
    }

    private static ThreadId? ToThreadId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new ThreadId(value);

    private static TurnId? ToTurnId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new TurnId(value);

    private static string InferCodeModeStatus(IReadOnlyList<KernelToolOutputContentItem> contentItems, bool success)
    {
        var header = contentItems
            .FirstOrDefault(static item => string.Equals(item.Type, "input_text", StringComparison.OrdinalIgnoreCase))
            ?.Text;
        if (!string.IsNullOrWhiteSpace(header))
        {
            if (header.StartsWith("Script running with cell ID ", StringComparison.Ordinal))
            {
                return "running";
            }

            if (header.StartsWith("Script completed", StringComparison.Ordinal))
            {
                return "completed";
            }

            if (header.StartsWith("Script terminated", StringComparison.Ordinal))
            {
                return "terminated";
            }

            if (header.StartsWith("Script failed", StringComparison.Ordinal))
            {
                return "failed";
            }
        }

        return success ? "completed" : "failed";
    }

    private static string? TryExtractCodeModeCellId(IReadOnlyList<KernelToolOutputContentItem> contentItems)
    {
        var header = contentItems
            .FirstOrDefault(static item => string.Equals(item.Type, "input_text", StringComparison.OrdinalIgnoreCase))
            ?.Text;
        const string prefix = "Script running with cell ID ";
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var remainder = header[prefix.Length..];
        var separatorIndex = remainder.IndexOf('.', StringComparison.Ordinal);
        return separatorIndex >= 0
            ? remainder[..separatorIndex]
            : remainder;
    }
}
