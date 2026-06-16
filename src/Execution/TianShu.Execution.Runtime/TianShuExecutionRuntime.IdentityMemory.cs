using System.Runtime.InteropServices;
using TianShu.Execution.Protocol;
using TianShu.Contracts.Identity;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;
using TianShu.IdentityMemory;

namespace TianShu.Execution.Runtime;

public sealed partial class TianShuExecutionRuntime
{
    private readonly ITianShuIdentityMemoryPlane? identityMemoryPlane;

    public Task<Account?> GetAccountProfileAsync(GetAccountProfile query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InvokeIdentityMemorySurfaceAsync<GetAccountProfile, Account?>(
            "identity/accountProfile/read",
            query,
            static (plane, request, context, token) => plane.GetAccountProfileAsync(request, context, token),
            cancellationToken);
    }

    public Task<IReadOnlyList<DeviceBinding>> ListBoundDevicesAsync(ListBoundDevices query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InvokeIdentityMemorySurfaceAsync<ListBoundDevices, IReadOnlyList<DeviceBinding>>(
            "identity/devices/list",
            query,
            static (plane, request, context, token) => plane.ListBoundDevicesAsync(request, context, token),
            cancellationToken);
    }

    public Task<IReadOnlyList<MemoryProviderDescriptor>> ListMemoryProvidersAsync(ListMemoryProviders query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InvokeIdentityMemorySurfaceAsync<ListMemoryProviders, IReadOnlyList<MemoryProviderDescriptor>>(
            "memory/providers/list",
            query,
            static (plane, request, context, token) => plane.ListMemoryProvidersAsync(request, context, token),
            cancellationToken);
    }

    public Task<IReadOnlyList<MemorySpace>> ListMemorySpacesAsync(ListMemorySpaces query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InvokeIdentityMemorySurfaceAsync<ListMemorySpaces, IReadOnlyList<MemorySpace>>(
            "memory/spaces/list",
            query,
            static (plane, request, context, token) => plane.ListMemorySpacesAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryOverlay> ResolveMemoryOverlayAsync(ResolveMemoryOverlay query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InvokeIdentityMemorySurfaceAsync<ResolveMemoryOverlay, MemoryOverlay>(
            "memory/overlay/read",
            query,
            static (plane, request, context, token) => plane.ResolveMemoryOverlayAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryQueryResult> FilterMemoryAsync(FilterMemory query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InvokeIdentityMemorySurfaceAsync<FilterMemory, MemoryQueryResult>(
            "memory/filter",
            query,
            static (plane, request, context, token) => plane.FilterMemoryAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryReviewQueryResult> ListMemoryReviewsAsync(ListMemoryReviews query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InvokeIdentityMemorySurfaceAsync<ListMemoryReviews, MemoryReviewQueryResult>(
            "memory/review/list",
            query,
            static (plane, request, context, token) => plane.ListMemoryReviewsAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryMutationResult> AddMemoryAsync(AddMemory command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<AddMemory, MemoryMutationResult>(
            "memory/add",
            command,
            static (plane, request, context, token) => plane.AddMemoryAsync(request, context, token),
            cancellationToken);
    }

    public Task<IReadOnlyList<MemoryCandidate>> ExtractMemoryAsync(ExtractMemory command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<ExtractMemory, IReadOnlyList<MemoryCandidate>>(
            "memory/extract",
            command,
            static (plane, request, context, token) => plane.ExtractMemoryAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryMutationResult> ImportMemoryAsync(ImportMemory command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<ImportMemory, MemoryMutationResult>(
            "memory/import",
            command,
            static (plane, request, context, token) => plane.ImportMemoryAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryQueryResult> ExportMemoryAsync(ExportMemory command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<ExportMemory, MemoryQueryResult>(
            "memory/export",
            command,
            static (plane, request, context, token) => plane.ExportMemoryAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryMutationResult> BindMemoryProviderAsync(BindMemoryProvider command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<BindMemoryProvider, MemoryMutationResult>(
            "memory/provider/bind",
            command,
            static (plane, request, context, token) => plane.BindMemoryProviderAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryConsolidationRunResult> RunMemoryConsolidationAsync(RunMemoryConsolidation command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<RunMemoryConsolidation, MemoryConsolidationRunResult>(
            "memory/consolidation/run",
            command,
            static (plane, request, context, token) => plane.RunMemoryConsolidationAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryMutationResult> ForgetMemoryAsync(ForgetMemory command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<ForgetMemory, MemoryMutationResult>(
            "memory/forget",
            command,
            static (plane, request, context, token) => plane.ForgetMemoryAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryMutationResult> DeleteMemoryAsync(DeleteMemory command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<DeleteMemory, MemoryMutationResult>(
            "memory/delete",
            command,
            static (plane, request, context, token) => plane.DeleteMemoryAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryMutationResult> SupersedeMemoryAsync(SupersedeMemory command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<SupersedeMemory, MemoryMutationResult>(
            "memory/supersede",
            command,
            static (plane, request, context, token) => plane.SupersedeMemoryAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryMutationResult> ApproveMemoryReviewAsync(ApproveMemoryReview command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<ApproveMemoryReview, MemoryMutationResult>(
            "memory/review/approve",
            command,
            static (plane, request, context, token) => plane.ApproveMemoryReviewAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryMutationResult> DemoteMemoryReviewAsync(DemoteMemoryReview command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<DemoteMemoryReview, MemoryMutationResult>(
            "memory/review/demote",
            command,
            static (plane, request, context, token) => plane.DemoteMemoryReviewAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryMutationResult> MergeMemoryReviewAsync(MergeMemoryReview command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<MergeMemoryReview, MemoryMutationResult>(
            "memory/review/merge",
            command,
            static (plane, request, context, token) => plane.MergeMemoryReviewAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryMutationResult> RestoreMemoryReviewAsync(RestoreMemoryReview command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<RestoreMemoryReview, MemoryMutationResult>(
            "memory/review/restore",
            command,
            static (plane, request, context, token) => plane.RestoreMemoryReviewAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryMutationResult> RecordMemoryFeedbackAsync(RecordMemoryFeedback command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<RecordMemoryFeedback, MemoryMutationResult>(
            "memory/feedback/record",
            command,
            static (plane, request, context, token) => plane.RecordMemoryFeedbackAsync(request, context, token),
            cancellationToken);
    }

    public Task<MemoryMutationResult> RecordMemoryCitationAsync(RecordMemoryCitation command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeIdentityMemorySurfaceAsync<RecordMemoryCitation, MemoryMutationResult>(
            "memory/citation/record",
            command,
            static (plane, request, context, token) => plane.RecordMemoryCitationAsync(request, context, token),
            cancellationToken);
    }

    private async Task<TResult> InvokeIdentityMemorySurfaceAsync<TRequest, TResult>(
        string method,
        TRequest request,
        Func<ITianShuIdentityMemoryPlane, TRequest, TianShuIdentityMemoryContext, CancellationToken, Task<TResult>> fallback,
        CancellationToken cancellationToken)
    {
        if (process is not null && stdin is not null)
        {
            var result = await ExecuteRuntimeSurfaceAsync(method, BuildIdentityMemorySurfacePayload(request), cancellationToken).ConfigureAwait(false);
            return AppServerJsonHelpers.Deserialize<TResult>(result)
                   ?? throw new InvalidOperationException($"AppHost runtime surface `{method}` 返回空结果。");
        }

        if (identityMemoryPlane is not null)
        {
            return await fallback(identityMemoryPlane, request, BuildIdentityMemoryContext(), cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("运行时尚未初始化，无法访问 identity / memory AppHost surface。");
    }

    private Dictionary<string, object?> BuildIdentityMemorySurfacePayload<TRequest>(TRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["request"] = request,
        };
        AddIfPresent(payload, "cwd", options.WorkingDirectory);
        AddIfPresent(payload, "workingDirectory", options.WorkingDirectory);
        AddIfPresent(payload, "threadId", activeThreadId);
        AddIfPresent(payload, "preferredVerbosity", options.ModelVerbosity);
        return payload;
    }

    private TianShuIdentityMemoryContext BuildIdentityMemoryContext()
    {
        var userName = ResolveLocalUserName();
        var accountId = new AccountId($"local-account:{NormalizeIdentityMemorySegment(userName)}");
        return new TianShuIdentityMemoryContext(
            runtimeName: RuntimeName,
            accountId: accountId,
            accountDisplayName: userName,
            deviceName: ResolveDeviceName(),
            platform: RuntimeInformation.OSDescription.Trim(),
            workingDirectory: Normalize(options.WorkingDirectory),
            activeThreadId: Normalize(activeThreadId),
            teamKey: Normalize(ReadTianShuEnvironment("TIANSHU_TEAM_KEY")),
            collaborationSpaceId: Normalize(ReadTianShuEnvironment("TIANSHU_COLLABORATION_SPACE_ID")),
            preferredVerbosity: Normalize(options.ModelVerbosity)
                                 ?? Normalize(ReadTianShuEnvironment("TIANSHU_MEMORY_PREFERRED_VERBOSITY")),
            preferredTools: ResolvePreferredTools(),
            snapshotTime: DateTimeOffset.UtcNow);
    }

    private static string ResolveLocalUserName()
        => Normalize(ReadTianShuEnvironment("TIANSHU_IDENTITY_DISPLAY_NAME"))
           ?? Normalize(Environment.UserName)
           ?? Normalize(Environment.GetEnvironmentVariable("USERNAME"))
           ?? Normalize(Environment.GetEnvironmentVariable("USER"))
           ?? "local-user";

    private static string ResolveDeviceName()
        => Normalize(ReadTianShuEnvironment("TIANSHU_DEVICE_NAME"))
           ?? Normalize(Environment.MachineName)
           ?? "local-device";

    private static IReadOnlyList<string> ResolvePreferredTools()
    {
        var configured = Normalize(ReadTianShuEnvironment("TIANSHU_MEMORY_PREFERRED_TOOLS"));
        if (string.IsNullOrWhiteSpace(configured))
        {
            return ["shell_command", "apply_patch"];
        }

        return configured
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeIdentityMemorySegment(string value)
        => value
            .Trim()
            .Replace('\\', '/')
            .Replace(':', '_')
            .Replace(' ', '-')
            .ToLowerInvariant();

    private static string? ReadTianShuEnvironment(string name)
        => Environment.GetEnvironmentVariable(name);
}
