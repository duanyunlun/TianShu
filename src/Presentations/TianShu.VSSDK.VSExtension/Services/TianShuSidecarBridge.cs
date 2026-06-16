using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TianShu.Provider.Abstractions;

namespace TianShu.VSSDK.VSExtension.Services;

[SuppressMessage("Usage", "VSTHRD010:Invoke single-threaded types on Main thread", Justification = "sidecar 路径解析本身不访问 VS COM 对象。")]
internal sealed class TianShuSidecarBridge : IAsyncDisposable
{
    private static readonly JsonSerializerOptions StaticJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<HashSet<string>> TypedConfigTopLevelKeys = new(CreateTypedConfigTopLevelKeys);
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<SidecarProtocolResponse>> pendingResponses = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> stderrLines = new();
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);

    private TianShuSidecarLaunchOptions? lastLaunchOptions;
    private Process? process;
    private EventHandler? processExitedHandler;
    private StreamWriter? stdin;
    private Task? stdoutLoop;
    private Task? stderrLoop;
    private CancellationTokenSource? runtimeCts;
    private int runtimeGeneration;

    public event EventHandler<TianShuSidecarEvent>? EventReceived;

    public bool IsRunning => process is { HasExited: false };

    public int RuntimeGeneration => Volatile.Read(ref runtimeGeneration);

    public async Task InitializeAsync(TianShuSidecarLaunchOptions options, CancellationToken cancellationToken)
    {
        var launchOptions = CloneLaunchOptions(options);
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureProcessStartedAsync(launchOptions, cancellationToken).ConfigureAwait(false);
            await SendInitializeRequestAsync(launchOptions, cancellationToken).ConfigureAwait(false);
            lastLaunchOptions = launchOptions;
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task ForceInterruptAsync(CancellationToken cancellationToken)
    {
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (lastLaunchOptions is null)
            {
                throw new InvalidOperationException("sidecar 尚未初始化。");
            }

            await AbortActiveProcessAsync("sidecar 已被强制中断。").ConfigureAwait(false);
            await EnsureProcessStartedAsync(lastLaunchOptions, cancellationToken).ConfigureAwait(false);
            await SendInitializeRequestAsync(lastLaunchOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public Task SendAsync(string message, CancellationToken cancellationToken)
        => SendAsync(CreateTextUserInputs(role: "user", message), historyMessages: null, cancellationToken);

    public async Task SendAsync(
        string message,
        IReadOnlyList<TianShuSidecarHistoryMessage>? historyMessages,
        CancellationToken cancellationToken)
        => await SendAsync(CreateTextUserInputs(role: "user", message), historyMessages, cancellationToken).ConfigureAwait(false);

    public async Task SendAsync(
        IReadOnlyList<TianShuSidecarUserInputPayload> inputs,
        IReadOnlyList<TianShuSidecarHistoryMessage>? historyMessages,
        CancellationToken cancellationToken)
    {
        var normalizedInputs = CloneUserInputs(inputs);
        var payload = new
        {
            message = ExtractConversationText(normalizedInputs),
            inputs = normalizedInputs,
            historyMessages = historyMessages?.Select(static item => new
            {
                role = item.Role,
                content = item.Content,
                inputs = item.Inputs,
            }),
        };

        var response = await SendRequestAsync("send", payload, cancellationToken, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        if (!response.Success)
        {
            throw CreateProtocolException(response.Message);
        }
    }

    public async Task<TianShuSidecarFollowUpAcceptedResult> SendFollowUpAsync(string message, TianShuSidecarFollowUpMode mode, CancellationToken cancellationToken)
        => await SendFollowUpAsync(CreateTextUserInputs(role: "user", message), mode, cancellationToken).ConfigureAwait(false);

    public async Task<TianShuSidecarFollowUpAcceptedResult> SendFollowUpAsync(
        IReadOnlyList<TianShuSidecarUserInputPayload> inputs,
        TianShuSidecarFollowUpMode mode,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var normalizedInputs = CloneUserInputs(inputs);
        return await SendTypedRequestAsync(
                "followUp",
                new
                {
                    message = ExtractConversationText(normalizedInputs),
                    inputs = normalizedInputs,
                    mode = mode.ToProtocolValue(),
                    correlationId,
                },
                (_, data) => ParseFollowUpAcceptedResult(data, mode, correlationId),
                cancellationToken,
                TimeSpan.FromSeconds(30))
            .ConfigureAwait(false);
    }

    public async Task<TianShuSidecarThreadListResult> ListThreadsAsync(TianShuSidecarThreadListRequest request, CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "listThreads",
                new
                {
                    limit = Math.Max(1, request.Limit),
                    cursor = request.Cursor,
                    archived = request.Archived,
                    cwd = request.Cwd,
                    sortKey = request.SortKey,
                    modelProviders = request.ModelProviders,
                    sourceKinds = request.SourceKinds.Select(static kind => kind.ToProtocolString()).ToArray(),
                    searchTerm = request.SearchTerm,
                    matchCurrentCwd = request.MatchCurrentCwd,
                },
                static (_, data) => ParseThreadListResult(data),
                cancellationToken,
                TimeSpan.FromSeconds(30))
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<TianShuSidecarThreadItem>> ListThreadsAsync(int limit, bool archived, bool matchCurrentCwd, CancellationToken cancellationToken)
    {
        var result = await ListThreadsAsync(
                new TianShuSidecarThreadListRequest
                {
                    Limit = limit,
                    Archived = archived,
                    MatchCurrentCwd = matchCurrentCwd,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return result.Items;
    }

    public Task<TianShuSidecarThreadSession?> ResumeThreadAsync(string threadId, CancellationToken cancellationToken)
        => ResumeThreadAsync(
            new TianShuSidecarThreadResumeRequest
            {
                ThreadId = threadId,
            },
            cancellationToken);

    public async Task<TianShuSidecarThreadSession?> ResumeThreadAsync(
        TianShuSidecarThreadResumeRequest request,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "resumeThread",
                BuildThreadResumePayload(request),
                static (_, data) => ParseThreadSession(data),
                cancellationToken,
                TimeSpan.FromSeconds(30))
            .ConfigureAwait(false);

    public Task<TianShuSidecarThreadItem?> StartNewThreadAsync(CancellationToken cancellationToken)
        => StartNewThreadAsync(new TianShuSidecarThreadStartRequest(), cancellationToken);

    public async Task<TianShuSidecarThreadItem?> StartNewThreadAsync(
        TianShuSidecarThreadStartRequest request,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "startNewThread",
                BuildThreadStartPayload(request),
                static (_, data) => ParseThreadItem(data),
                cancellationToken,
                TimeSpan.FromSeconds(30))
            .ConfigureAwait(false);

    public Task<TianShuSidecarThreadItem?> ForkThreadAsync(string threadId, CancellationToken cancellationToken)
        => ForkThreadAsync(
            new TianShuSidecarThreadForkRequest
            {
                ThreadId = threadId,
            },
            cancellationToken);

    public async Task<TianShuSidecarThreadItem?> ForkThreadAsync(
        TianShuSidecarThreadForkRequest request,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "forkThread",
                BuildThreadForkPayload(request),
                static (_, data) => ParseThreadItem(data),
                cancellationToken,
                TimeSpan.FromSeconds(30))
            .ConfigureAwait(false);

    public async Task RenameThreadAsync(string threadId, string name, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
                "renameThread",
                new
                {
                    threadId,
                    name,
                },
                cancellationToken,
                TimeSpan.FromSeconds(30))
            .ConfigureAwait(false);
        if (!response.Success)
        {
            throw CreateProtocolException(response.Message);
        }
    }

    public async Task ArchiveThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
                "archiveThread",
                new
                {
                    threadId,
                },
                cancellationToken,
                TimeSpan.FromSeconds(30))
            .ConfigureAwait(false);
        if (!response.Success)
        {
            throw CreateProtocolException(response.Message);
        }
    }

    public async Task RespondApprovalAsync(string callId, bool approved, string? note, CancellationToken cancellationToken)
    {
        var option = new TianShuSidecarApprovalDecisionOptionPayload
        {
            Decision = approved
                ? TianShuApprovalDecision.Accept
                : TianShuApprovalDecision.Decline,
        };
        await RespondApprovalAsync(callId, option, note, cancellationToken).ConfigureAwait(false);
    }

    public async Task RespondApprovalAsync(
        string callId,
        TianShuApprovalDecision decision,
        string? note,
        CancellationToken cancellationToken)
        => await RespondApprovalAsync(
                callId,
                new TianShuSidecarApprovalDecisionOptionPayload
                {
                    Decision = decision,
                },
                note,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task RespondApprovalAsync(
        string callId,
        TianShuSidecarApprovalDecisionOptionPayload option,
        string? note,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
                "respondApproval",
                new
                {
                    callId,
                    decision = option.Decision.ToProtocolValue(),
                    approved = option.IsApproved(),
                    execPolicyAmendment = option.ExecPolicyAmendment is { CommandPrefix.Length: > 0 }
                        ? new
                        {
                            commandPrefix = option.ExecPolicyAmendment.CommandPrefix,
                        }
                        : null,
                    networkPolicyAmendment = option.NetworkPolicyAmendment is { } networkPolicyAmendment
                        ? new
                        {
                            host = networkPolicyAmendment.Host,
                            action = networkPolicyAmendment.Action,
                        }
                        : null,
                    note,
                },
                cancellationToken,
                TimeSpan.FromSeconds(15))
            .ConfigureAwait(false);
        if (!response.Success)
        {
            throw CreateProtocolException(response.Message);
        }
    }

    public async Task RespondUserInputAsync(
        string callId,
        IReadOnlyDictionary<string, TianShuSidecarStructuredValue> answers,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
                "respondUserInput",
                new
                {
                    callId,
                    answers,
                },
                cancellationToken,
                TimeSpan.FromSeconds(15))
            .ConfigureAwait(false);
        if (!response.Success)
        {
            throw CreateProtocolException(response.Message);
        }
    }

    public async Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
                "deleteThread",
                new
                {
                    threadId,
                },
                cancellationToken,
                TimeSpan.FromSeconds(30))
            .ConfigureAwait(false);
        if (!response.Success)
        {
            throw CreateProtocolException(response.Message);
        }
    }

    public async Task<TianShuSidecarThreadOperationResult> ReadThreadAsync(
        string threadId,
        bool includeTurns,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "readThread",
                new
                {
                    threadId,
                    includeTurns,
                },
                ParseThreadOperationResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarThreadOperationResult> UnarchiveThreadAsync(
        string threadId,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "unarchiveThread",
                new
                {
                    threadId,
                },
                ParseThreadOperationResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarThreadOperationResult> RollbackThreadAsync(
        string threadId,
        int numTurns,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "rollbackThread",
                new
                {
                    threadId,
                    numTurns,
                },
                ParseThreadOperationResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarThreadOperationResult> UpdateThreadMetadataAsync(
        TianShuSidecarThreadMetadataUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return await SendTypedRequestAsync(
                "updateThreadMetadata",
                new
                {
                    threadId = request.ThreadId,
                    gitInfo = request.GitInfo is null
                        ? null
                        : new
                        {
                            branch = request.GitInfo.Branch,
                            sha = request.GitInfo.Sha,
                            originUrl = request.GitInfo.OriginUrl,
                        },
                },
                ParseThreadOperationResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);
    }

    public async Task RespondPermissionAsync(
        string callId,
        IReadOnlyDictionary<string, TianShuSidecarStructuredValue> permissions,
        TianShuPermissionGrantScope scope,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
                "respondPermission",
                new
                {
                    callId,
                    permissions,
                    scope = scope.ToProtocolValue(),
                },
                cancellationToken,
                TimeSpan.FromSeconds(15))
            .ConfigureAwait(false);
        if (!response.Success)
        {
            throw CreateProtocolException(response.Message);
        }
    }

    public async Task<TianShuSidecarResponse> InvokeCapabilityAsync(
        TianShuSidecarCapability capability,
        string? method,
        string? parametersJson,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
                "invokeCapability",
                new
                {
                    capability = capability.ToProtocolValue(),
                    method,
                    parametersJson,
                },
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);
        if (!response.Success)
        {
            throw CreateProtocolException(response.Message);
        }

        return new TianShuSidecarResponse
        {
            RequestId = response.RequestId,
            Success = response.Success,
            Message = response.Message,
            PayloadJson = response.PayloadData.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? null
                : response.PayloadData.GetRawText(),
        };
    }

    public async Task<TianShuSidecarConfigReadResult> ReadConfigAsync(
        string? cwd,
        bool includeLayers,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "readConfig",
                new
                {
                    workingDirectory = cwd,
                    includeLayers,
                },
                ParseConfigReadResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarModelCatalogResult> ListModelsAsync(
        int limit,
        bool includeHidden,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "listModels",
                new
                {
                    limit,
                    includeHidden,
                },
                ParseModelCatalogResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarConfigWriteResult> WriteConfigValueAsync(
        TianShuSidecarConfigValueWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return await SendTypedRequestAsync(
                "writeConfigValue",
                new
                {
                    keyPath = request.KeyPath,
                    value = request.Value,
                    mergeStrategy = request.MergeStrategy,
                    workingDirectory = request.WorkingDirectory,
                    filePath = request.FilePath,
                    expectedVersion = request.ExpectedVersion,
                    reloadUserConfig = request.ReloadUserConfig,
                },
                ParseConfigWriteResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);
    }

    public async Task<TianShuSidecarConfigWriteResult> WriteConfigBatchAsync(
        TianShuSidecarConfigBatchWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return await SendTypedRequestAsync(
                "writeConfigBatch",
                new
                {
                    items = request.Items.Select(static item => new
                    {
                        keyPath = item.KeyPath,
                        value = item.Value,
                        mergeStrategy = item.MergeStrategy,
                    }),
                    mergeStrategy = request.MergeStrategy,
                    workingDirectory = request.WorkingDirectory,
                    filePath = request.FilePath,
                    expectedVersion = request.ExpectedVersion,
                    reloadUserConfig = request.ReloadUserConfig,
                },
                ParseConfigWriteResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);
    }

    public async Task<TianShuSidecarConfigRequirementsReadResult> ReadConfigRequirementsAsync(
        string? cwd,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "readConfigRequirements",
                new
                {
                    workingDirectory = cwd,
                },
                ParseConfigRequirementsReadResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarExperimentalFeatureListResult> ListExperimentalFeaturesAsync(
        int? limit,
        string? cursor,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "listExperimentalFeatures",
                new
                {
                    limit,
                    cursor,
                },
                ParseExperimentalFeatureListResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarCollaborationModeListResult> ListCollaborationModesAsync(
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "listCollaborationModes",
                new { },
                ParseCollaborationModeListResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarMcpServerStatusListResult> ListMcpServerStatusAsync(
        int? limit,
        string? cursor,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "listMcpServerStatus",
                new
                {
                    limit,
                    cursor,
                },
                ParseMcpServerStatusListResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarResponse> ReloadMcpServersAsync(CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync("reloadMcpServers", new { }, cancellationToken, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        if (!response.Success)
        {
            throw CreateProtocolException(response.Message);
        }

        return new TianShuSidecarResponse
        {
            RequestId = response.RequestId,
            Success = response.Success,
            Message = response.Message,
            PayloadJson = response.PayloadData.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? null
                : response.PayloadData.GetRawText(),
        };
    }

    public async Task<TianShuSidecarSkillsListResult> ListSkillsAsync(
        IReadOnlyList<string> workingDirectories,
        bool forceReload,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "listSkills",
                new
                {
                    workingDirectories,
                    forceReload,
                },
                ParseSkillsListResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarSkillsRemoteListResult> ListRemoteSkillsAsync(
        string? hazelnutScope,
        string? productSurface,
        bool? enabled,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "listRemoteSkills",
                new
                {
                    hazelnutScope,
                    productSurface,
                    enabled,
                },
                ParseSkillsRemoteListResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarSkillsRemoteExportResult> ExportRemoteSkillAsync(
        string hazelnutId,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "exportRemoteSkill",
                new
                {
                    hazelnutId,
                },
                ParseSkillsRemoteExportResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarPluginListResult> ListPluginsAsync(
        IReadOnlyList<string> workingDirectories,
        bool forceRemoteSync,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "listPlugins",
                new
                {
                    workingDirectories,
                    forceRemoteSync,
                },
                ParsePluginListResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarPluginReadResult> ReadPluginAsync(
        string marketplacePath,
        string pluginName,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "readPlugin",
                new
                {
                    marketplacePath,
                    pluginName,
                },
                ParsePluginReadResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarPluginInstallResult> InstallPluginAsync(
        string marketplacePath,
        string pluginName,
        string? workingDirectory,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "installPlugin",
                new
                {
                    marketplacePath,
                    pluginName,
                    workingDirectory,
                },
                ParsePluginInstallResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarAppListResult> ListAppsAsync(
        int? limit,
        string? cursor,
        string? threadId,
        bool forceRefetch,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "listApps",
                new
                {
                    limit,
                    cursor,
                    threadId,
                    forceRefetch,
                },
                ParseAppListResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarReviewStartResult> StartReviewAsync(
        string threadId,
        string? delivery,
        string targetType,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "startReview",
                new
                {
                    threadId,
                    delivery,
                    targetType,
                },
                ParseReviewStartResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarMcpServerOauthLoginStartResult> StartMcpServerOauthLoginAsync(
        string name,
        long? timeoutSecs,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "startMcpServerOauthLogin",
                new
                {
                    name,
                    timeoutSecs,
                },
                ParseMcpServerOauthLoginStartResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarConversationSummaryResult> GetConversationSummaryAsync(
        string? threadId,
        string? rolloutPath,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "artifact/conversationSummary/read",
                new
                {
                    threadId,
                    rolloutPath,
                },
                ParseConversationSummaryResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarGitDiffToRemoteResult> GetGitDiffToRemoteAsync(
        string threadId,
        CancellationToken cancellationToken)
        => await SendTypedRequestAsync(
                "artifact/gitDiffToRemote/read",
                new
                {
                    threadId,
                },
                ParseGitDiffToRemoteResult,
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);

    public async Task<TianShuSidecarResponse> InvokeRuntimeSurfaceAsync(
        string method,
        string? parametersJson,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
                "invokeRuntimeSurface",
                new
                {
                    method,
                    parametersJson,
                },
                cancellationToken,
                TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);
        if (!response.Success)
        {
            throw CreateProtocolException(response.Message);
        }

        return new TianShuSidecarResponse
        {
            RequestId = response.RequestId,
            Success = response.Success,
            Message = response.Message,
            PayloadJson = response.PayloadData.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? null
                : response.PayloadData.GetRawText(),
            DiagnosticJson = response.PayloadData.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? null
                : response.PayloadData.GetRawText(),
        };
    }

    public async Task InterruptAsync(CancellationToken cancellationToken)
    {
        if (!IsRunning)
        {
            return;
        }

        var response = await SendRequestAsync("interrupt", new { }, cancellationToken, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        if (!response.Success)
        {
            throw CreateProtocolException(response.Message);
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsRunning)
            {
                await DisposeProcessAsync().ConfigureAwait(false);
                return;
            }

            try
            {
                _ = await SendRequestAsync("shutdown", new { }, cancellationToken, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch
            {
                // 关闭阶段优先退出进程，失败时由后续 Kill 兜底。
            }

            if (process is { HasExited: false })
            {
                if (!process.WaitForExit(3000))
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
            }

            await DisposeProcessAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await ShutdownAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
            await DisposeProcessAsync().ConfigureAwait(false);
        }

        lifecycleGate.Dispose();
        writeGate.Dispose();
    }

    private async Task EnsureProcessStartedAsync(TianShuSidecarLaunchOptions options, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return;
        }

        await DisposeProcessAsync().ConfigureAwait(false);

        var preferredRoot = string.IsNullOrWhiteSpace(options.WorkingDirectory) ? null : options.WorkingDirectory;
        var sidecarProjectPath = TianShuDevPathLocator.ResolveSidecarProjectPath(preferredRoot);
        var builtSidecarDll = sidecarProjectPath is null ? null : TryResolveBuiltSidecarDll(sidecarProjectPath);
        if (string.IsNullOrWhiteSpace(builtSidecarDll) && string.IsNullOrWhiteSpace(sidecarProjectPath))
        {
            throw new FileNotFoundException("未找到 TianShu.VSSDK.Sidecar 项目或已构建 DLL。", preferredRoot ?? string.Empty);
        }

        var launchPath = !string.IsNullOrWhiteSpace(builtSidecarDll) ? builtSidecarDll! : sidecarProjectPath!;
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = !string.IsNullOrWhiteSpace(builtSidecarDll)
                ? $"\"{builtSidecarDll}\" --stdio"
                : $"run --no-launch-profile --verbosity quiet --project \"{sidecarProjectPath}\" -- --stdio",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(launchPath)!,
        };

        startInfo.EnvironmentVariables["DOTNET_CLI_UI_LANGUAGE"] = "en";
        startInfo.EnvironmentVariables["DOTNET_NOLOGO"] = "1";
        startInfo.EnvironmentVariables["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

        runtimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var startedProcess = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        var generation = Interlocked.Increment(ref runtimeGeneration);
        processExitedHandler = (_, _) => OnProcessExited(startedProcess, generation);
        startedProcess.Exited += processExitedHandler;

        if (!startedProcess.Start())
        {
            throw new InvalidOperationException("无法启动 TianShu sidecar 进程。 ");
        }

        process = startedProcess;
        stdin = startedProcess.StandardInput;
        stdin.AutoFlush = true;

        stdoutLoop = Task.Run(() => ReadStdoutLoopAsync(startedProcess, generation, startedProcess.StandardOutput, runtimeCts.Token));
        stderrLoop = Task.Run(() => ReadStderrLoopAsync(startedProcess, generation, startedProcess.StandardError, runtimeCts.Token));

        RaiseEvent(new TianShuSidecarEvent
        {
            BridgeGeneration = generation,
            EventType = "runtime_state",
            State = "starting",
            Message = !string.IsNullOrWhiteSpace(builtSidecarDll)
                ? "sidecar 已按已构建 DLL 模式启动。"
                : "sidecar 已按 dotnet run 模式启动。",
        });
    }

    private async Task SendInitializeRequestAsync(TianShuSidecarLaunchOptions options, CancellationToken cancellationToken)
    {
        var payload = new
        {
            workingDirectory = options.WorkingDirectory,
            configPath = options.ConfigPath,
            profileName = options.ProfileName,
            appHostProjectPath = options.AppHostProjectPath,
            createThreadOnInitialize = options.CreateThreadOnInitialize,
            model = options.Model,
            modelProvider = options.ModelProvider,
            approvalPolicy = options.ApprovalPolicy,
            sandboxMode = options.SandboxMode,
            webSearchMode = options.WebSearchMode,
            serviceTier = options.ServiceTier,
            collaborationMode = options.CollaborationMode,
        };

        var response = await SendRequestAsync("initialize", payload, cancellationToken, TimeSpan.FromSeconds(120)).ConfigureAwait(false);
        if (!response.Success)
        {
            throw CreateProtocolException(response.Message);
        }
    }

    private async Task<SidecarProtocolResponse> SendRequestAsync(string command, object payload, CancellationToken cancellationToken, TimeSpan timeout)
    {
        if (!IsRunning || stdin is null)
        {
            throw new InvalidOperationException("sidecar 未启动。 ");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var request = new
        {
            requestId,
            command,
            payload,
        };

        var pending = new TaskCompletionSource<SidecarProtocolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingResponses[requestId] = pending;

        try
        {
            await WriteJsonLineAsync(request, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            using var registration = timeoutCts.Token.Register(
                static state => ((TaskCompletionSource<SidecarProtocolResponse>)state!).TrySetCanceled(),
                pending);

            return await pending.Task.ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"等待 sidecar 响应超时。command={command}", ex);
        }
        finally
        {
            pendingResponses.TryRemove(requestId, out _);
        }
    }

    private async Task<TPayload> SendTypedRequestAsync<TPayload>(
        string command,
        object payload,
        Func<string, JsonElement, TPayload> responseParser,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var response = await SendRequestAsync(command, payload, cancellationToken, timeout).ConfigureAwait(false);
        if (!response.Success)
        {
            throw CreateProtocolException(response.Message);
        }

        try
        {
            return responseParser(response.Message, response.PayloadData);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw CreateProtocolException($"sidecar 响应解析失败。command={command}，{ex.Message}");
        }
    }

    private static object BuildThreadStartPayload(TianShuSidecarThreadStartRequest request)
    {
        var payload = BuildThreadRequestPayloadBase(request);
        AddIfPresent(payload, "serviceName", request.ServiceName);
        AddPersonalityIfPresent(payload, "personality", request.Personality);
        AddIfPresent(payload, "ephemeral", request.Ephemeral);
        AddListIfPresent(payload, "dynamicTools", request.DynamicTools);
        AddIfPresent(payload, "persistExtendedHistory", request.PersistExtendedHistory);
        AddIfPresent(payload, "experimentalRawEvents", request.ExperimentalRawEvents);
        return payload;
    }

    private static object BuildThreadResumePayload(TianShuSidecarThreadResumeRequest request)
    {
        var payload = BuildThreadRequestPayloadBase(request);
        payload["threadId"] = request.ThreadId;
        AddIfPresent(payload, "path", request.Path);
        AddListIfPresent(payload, "history", request.History);
        AddPersonalityIfPresent(payload, "personality", request.Personality);
        AddIfPresent(payload, "persistExtendedHistory", request.PersistExtendedHistory);
        return payload;
    }

    private static object BuildThreadForkPayload(TianShuSidecarThreadForkRequest request)
    {
        var payload = BuildThreadRequestPayloadBase(request);
        payload["threadId"] = request.ThreadId;
        AddIfPresent(payload, "path", request.Path);
        AddIfPresent(payload, "ephemeral", request.Ephemeral);
        AddIfPresent(payload, "persistExtendedHistory", request.PersistExtendedHistory);
        return payload;
    }

    private static Dictionary<string, object?> BuildThreadRequestPayloadBase(TianShuSidecarThreadRequestBase request)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model"] = request.Model,
            ["modelProvider"] = request.ModelProvider,
            ["workingDirectory"] = request.WorkingDirectory,
            ["approvalPolicy"] = request.ApprovalPolicy,
            ["sandboxMode"] = request.SandboxMode,
            ["config"] = request.Config,
            ["baseInstructions"] = request.BaseInstructions,
            ["developerInstructions"] = request.DeveloperInstructions,
        };

        if (!request.ServiceTier.IsSpecified)
        {
            payload.Remove("serviceTier");
        }
        else
        {
            payload["serviceTier"] = request.ServiceTier.Value?.Value;
        }

        RemoveNullEntries(payload, "serviceTier");
        return payload;
    }

    private static void AddPersonalityIfPresent(
        IDictionary<string, object?> payload,
        string key,
        TianShuSidecarPersonality? value)
    {
        if (value is not null)
        {
            payload[key] = value;
        }
    }

    private static void AddListIfPresent<T>(IDictionary<string, object?> payload, string key, IReadOnlyList<T>? values)
    {
        if (values is { Count: > 0 })
        {
            payload[key] = values;
        }
    }

    private static void AddIfPresent<T>(IDictionary<string, object?> payload, string key, T? value)
    {
        if (value is not null)
        {
            payload[key] = value;
        }
    }

    private static void RemoveNullEntries(IDictionary<string, object?> payload, params string[] keysToKeepWhenNull)
    {
        var keepNullKeys = keysToKeepWhenNull.Length == 0
            ? null
            : new HashSet<string>(keysToKeepWhenNull, StringComparer.Ordinal);
        var keys = payload
            .Where(pair => pair.Value is null && (keepNullKeys is null || !keepNullKeys.Contains(pair.Key)))
            .Select(static pair => pair.Key)
            .ToArray();
        foreach (var key in keys)
        {
            payload.Remove(key);
        }
    }

    private Task<TPayload> InvokeTypedCapabilityAsync<TPayload>(
        TianShuSidecarCapability capability,
        string? method,
        string? parametersJson,
        Func<string, JsonElement, TPayload> responseParser,
        CancellationToken cancellationToken)
        => SendTypedRequestAsync(
            "invokeCapability",
            new
            {
                capability = capability.ToProtocolValue(),
                method,
                parametersJson,
            },
            responseParser,
            cancellationToken,
            TimeSpan.FromSeconds(60));

    private async Task WriteJsonLineAsync(object payload, CancellationToken cancellationToken)
    {
        if (stdin is null)
        {
            throw new InvalidOperationException("sidecar stdin 不可用。 ");
        }

        var json = JsonSerializer.Serialize(payload, jsonOptions);

        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stdin.WriteLineAsync(json).ConfigureAwait(false);
            await stdin.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task ReadStdoutLoopAsync(Process ownerProcess, int generation, StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                ProcessOutputLine(ownerProcess, generation, line);
            }
        }
        catch (Exception ex)
        {
            if (!IsCurrentProcess(ownerProcess, generation))
            {
                return;
            }

            RaiseEvent(new TianShuSidecarEvent
            {
                BridgeGeneration = generation,
                EventType = "error",
                Message = $"读取 sidecar stdout 失败：{ex.Message}",
            });
        }
    }

    private async Task ReadStderrLoopAsync(Process ownerProcess, int generation, StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (!IsCurrentProcess(ownerProcess, generation))
                {
                    continue;
                }

                stderrLines.Enqueue(line);
                while (stderrLines.Count > 20 && stderrLines.TryDequeue(out _))
                {
                }
            }
        }
        catch
        {
            // stderr 只做诊断缓存，不向上抛。
        }
    }

    private void ProcessOutputLine(Process ownerProcess, int generation, string line)
    {
        if (!IsCurrentProcess(ownerProcess, generation))
        {
            return;
        }

        line = (line ?? string.Empty).TrimStart('﻿', '\0').Trim();
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (!line.StartsWith("{", StringComparison.Ordinal))
        {
            stderrLines.Enqueue($"[stdout] {line}");
            while (stderrLines.Count > 20 && stderrLines.TryDequeue(out _))
            {
            }

            return;
        }

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var messageType = ReadString(root, "messageType");

        if (string.Equals(messageType, "response", StringComparison.Ordinal))
        {
            var response = new SidecarProtocolResponse
            {
                RequestId = ReadString(root, "requestId") ?? string.Empty,
                Success = ReadBoolean(root, "success"),
                Message = ReadString(root, "message") ?? string.Empty,
                PayloadData = root.TryGetProperty("data", out var responseData) ? responseData.Clone() : default,
            };

            if (pendingResponses.TryGetValue(response.RequestId, out var pending))
            {
                pending.TrySetResult(response);
            }

            return;
        }

        if (!string.Equals(messageType, "event", StringComparison.Ordinal))
        {
            return;
        }

        var eventData = root.TryGetProperty("data", out var eventDataProperty) ? eventDataProperty.Clone() : default;

        RaiseEvent(new TianShuSidecarEvent
        {
            BridgeGeneration = generation,
            EventType = ReadString(root, "eventType") ?? string.Empty,
            Message = ReadString(root, "message"),
            Text = ReadString(root, "text"),
            ThreadId = ReadString(root, "threadId"),
            TurnId = ReadString(root, "turnId"),
            CallId = ReadString(root, "callId"),
            ToolName = ReadString(root, "toolName"),
            State = ReadString(root, "state"),
            ItemId = ReadString(eventData, "itemId"),
            Status = ReadString(eventData, "status"),
            Phase = ReadString(eventData, "phase"),
            SourceMethod = ReadString(eventData, "sourceMethod"),
            TaskType = ReadString(eventData, "taskType"),
            OperationName = ReadString(eventData, "operationName"),
            ServerName = ReadString(eventData, "serverName"),
            RequiresApproval = TryReadBoolean(eventData, "requiresApproval"),
            WillRetry = TryReadBoolean(eventData, "willRetry"),
            DiagnosticJson = ExtractEventDiagnosticJson(eventData),
            Plan = TryDeserializeEventPayload<TianShuSidecarPlanPayload>(eventData, "plan"),
            ToolCall = TryDeserializeEventPayload<TianShuSidecarToolCallPayload>(eventData, "toolCall"),
            ApprovalRequest = TryDeserializeApprovalRequestPayload(eventData, "approvalRequest"),
            PermissionRequest = TryDeserializeEventPayload<TianShuSidecarPermissionRequestPayload>(eventData, "permissionRequest"),
            UserInputRequest = TryDeserializeEventPayload<TianShuSidecarUserInputRequestPayload>(eventData, "userInputRequest"),
            ServerRequestResolved = TryDeserializeEventPayload<TianShuSidecarServerRequestResolvedPayload>(eventData, "serverRequestResolved"),
            Task = TryDeserializeEventPayload<TianShuSidecarTaskPayload>(eventData, "task"),
            Operation = TryDeserializeEventPayload<TianShuSidecarOperationPayload>(eventData, "operation"),
            Reasoning = TryDeserializeEventPayload<TianShuSidecarReasoningPayload>(eventData, "reasoning"),
            McpServerStatus = TryDeserializeEventPayload<TianShuSidecarMcpServerStatusPayload>(eventData, "mcpServerStatus"),
            Item = TryDeserializeEventPayload<TianShuSidecarItemPayload>(eventData, "item"),
            CommittedUserMessage = TryDeserializeEventPayload<TianShuSidecarCommittedUserMessagePayload>(eventData, "committedUserMessage"),
            PendingFollowUp = TryDeserializeEventPayload<TianShuSidecarPendingFollowUpPayload>(eventData, "pendingFollowUp"),
            PendingInputState = TryDeserializeEventPayload<TianShuSidecarPendingInputStatePayload>(eventData, "pendingInputState"),
            TurnError = TryGetObject(eventData, "turnError") is { } turnError ? ParseThreadTurnError(turnError) : null,
            AgentJobProgress = TryDeserializeEventPayload<TianShuSidecarAgentJobProgressPayload>(eventData, "agentJobProgress"),
            DeprecationNotice = TryDeserializeEventPayload<TianShuSidecarDeprecationNoticePayload>(eventData, "deprecationNotice"),
            ConfigWarning = TryDeserializeEventPayload<TianShuSidecarConfigWarningPayload>(eventData, "configWarning"),
            ThreadStatusChanged = TryDeserializeEventPayload<TianShuSidecarThreadStatusChangedPayload>(eventData, "threadStatusChanged"),
            ThreadNameUpdated = TryDeserializeEventPayload<TianShuSidecarThreadNameUpdatedPayload>(eventData, "threadNameUpdated"),
            ThreadTokenUsage = TryDeserializeEventPayload<TianShuSidecarThreadTokenUsagePayload>(eventData, "threadTokenUsage"),
            CommandExecOutputDelta = TryDeserializeEventPayload<TianShuSidecarCommandExecOutputDeltaPayload>(eventData, "commandExecOutputDelta"),
            AppListUpdated = TryDeserializeEventPayload<TianShuSidecarAppListUpdatedPayload>(eventData, "appListUpdated"),
            WindowsSandboxSetup = TryDeserializeEventPayload<TianShuSidecarWindowsSandboxSetupPayload>(eventData, "windowsSandboxSetup"),
            McpServerOauthLogin = TryDeserializeEventPayload<TianShuSidecarMcpServerOauthLoginPayload>(eventData, "mcpServerOauthLogin"),
            RealtimeSession = TryDeserializeEventPayload<TianShuSidecarRealtimeSessionPayload>(eventData, "realtimeSession"),
            FuzzyFileSearchSession = TryDeserializeEventPayload<TianShuSidecarFuzzyFileSearchSessionPayload>(eventData, "fuzzyFileSearchSession"),
            ThreadRealtimeItemAdded = TryDeserializeEventPayload<TianShuSidecarThreadRealtimeItemAddedPayload>(eventData, "threadRealtimeItemAdded"),
            ThreadRealtimeOutputAudioDelta = TryDeserializeEventPayload<TianShuSidecarThreadRealtimeOutputAudioDeltaPayload>(eventData, "threadRealtimeOutputAudioDelta"),
            ThreadRealtimeError = TryDeserializeEventPayload<TianShuSidecarThreadRealtimeErrorPayload>(eventData, "threadRealtimeError"),
            ThreadRealtimeClosed = TryDeserializeEventPayload<TianShuSidecarThreadRealtimeClosedPayload>(eventData, "threadRealtimeClosed"),
        });
    }

    private static string? ExtractEventDiagnosticJson(JsonElement eventData)
    {
        if (eventData.ValueKind == JsonValueKind.Object
            && eventData.TryGetProperty("diagnostics", out var diagnostics)
            && diagnostics.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            return diagnostics.GetRawText();
        }

        return null;
    }

    private TPayload? TryDeserializeEventPayload<TPayload>(JsonElement data, string propertyName)
        where TPayload : class
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty(propertyName, out var property)
            || property.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TPayload>(property.GetRawText(), jsonOptions);
        }
        catch (JsonException ex)
        {
            stderrLines.Enqueue($"[event-payload:{propertyName}] {ex.Message}");
            while (stderrLines.Count > 20 && stderrLines.TryDequeue(out _))
            {
            }

            return null;
        }
    }

    private TianShuSidecarApprovalRequestPayload? TryDeserializeApprovalRequestPayload(JsonElement data, string propertyName)
    {
        var payload = TryDeserializeEventPayload<TianShuSidecarApprovalRequestDto>(data, propertyName);
        if (payload is null)
        {
            return null;
        }

        return new TianShuSidecarApprovalRequestPayload
        {
            ToolName = payload.ToolName,
            ApprovalKind = payload.ApprovalKind,
            AvailableDecisions = payload.AvailableDecisions,
            AvailableDecisionOptions = payload.AvailableDecisionOptions?
                .Select(MapApprovalDecisionOption)
                .Where(static item => item is not null)
                .Cast<TianShuSidecarApprovalDecisionOptionPayload>()
                .ToArray(),
            Summary = payload.Summary,
            MetadataFields = payload.MetadataFields ?? [],
        };
    }

    private static TianShuSidecarApprovalDecisionOptionPayload? MapApprovalDecisionOption(TianShuSidecarApprovalDecisionOptionDto dto)
    {
        if (!TryMapApprovalDecision(dto.Type ?? dto.Decision, out var decision))
        {
            return null;
        }

        return new TianShuSidecarApprovalDecisionOptionPayload
        {
            Decision = decision,
            ExecPolicyAmendment = dto.ExecPolicyAmendment is { CommandPrefix.Length: > 0 } execPolicyAmendment
                ? new TianShuSidecarExecPolicyAmendmentPayload
                {
                    CommandPrefix = execPolicyAmendment.CommandPrefix,
                }
                : null,
            NetworkPolicyAmendment = dto.NetworkPolicyAmendment is { } networkPolicyAmendment
                ? new TianShuSidecarNetworkPolicyAmendmentPayload
                {
                    Host = networkPolicyAmendment.Host,
                    Action = networkPolicyAmendment.Action,
                }
                : null,
        };
    }

    private static string? TryResolveBuiltSidecarDll(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return null;
        }

        var dllPath = Path.Combine(projectDirectory, "bin", "Debug", "net10.0", "TianShu.VSSDK.Sidecar.dll");
        return File.Exists(dllPath) ? dllPath : null;
    }
    private void RaiseEvent(TianShuSidecarEvent sidecarEvent)
    {
        if (sidecarEvent.BridgeGeneration <= 0)
        {
            sidecarEvent.BridgeGeneration = RuntimeGeneration;
        }

        EventReceived?.Invoke(this, sidecarEvent);
    }

    private void OnProcessExited(Process exitedProcess, int generation)
    {
        if (!IsCurrentProcess(exitedProcess, generation))
        {
            return;
        }

        var stderr = string.Join(Environment.NewLine, stderrLines.ToArray());
        var message = string.IsNullOrWhiteSpace(stderr)
            ? "sidecar 进程已退出。"
            : $"sidecar 进程已退出。最近 stderr：{stderr}";

        FailPendingResponses(message);
        RaiseEvent(new TianShuSidecarEvent
        {
            BridgeGeneration = generation,
            EventType = "runtime_state",
            State = "stopped",
            Message = message,
        });
    }

    private async Task DisposeProcessAsync()
    {
        runtimeCts?.Cancel();
        runtimeCts?.Dispose();
        runtimeCts = null;

        if (stdoutLoop is not null)
        {
            try
            {
                await stdoutLoop.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (stderrLoop is not null)
        {
            try
            {
                await stderrLoop.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        stdoutLoop = null;
        stderrLoop = null;

        if (stdin is not null)
        {
            stdin.Dispose();
            stdin = null;
        }

        if (process is not null)
        {
            if (processExitedHandler is not null)
            {
                process.Exited -= processExitedHandler;
                processExitedHandler = null;
            }

            process.Dispose();
            process = null;
        }

        while (stderrLines.TryDequeue(out _))
        {
        }
    }

    private async Task AbortActiveProcessAsync(string failureMessage)
    {
        FailPendingResponses(failureMessage);

        if (process is { } currentProcess)
        {
            if (processExitedHandler is not null)
            {
                currentProcess.Exited -= processExitedHandler;
                processExitedHandler = null;
            }

            if (!currentProcess.HasExited)
            {
                try
                {
                    currentProcess.Kill();
                }
                catch (InvalidOperationException)
                {
                }

                try
                {
                    currentProcess.WaitForExit(3000);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        await DisposeProcessAsync().ConfigureAwait(false);
    }

    private void FailPendingResponses(string message)
    {
        foreach (var pair in pendingResponses)
        {
            pair.Value.TrySetException(new InvalidOperationException(message));
        }
    }

    private bool IsCurrentProcess(Process ownerProcess, int generation)
        => ReferenceEquals(ownerProcess, process) && generation == RuntimeGeneration;

    private static TianShuSidecarLaunchOptions CloneLaunchOptions(TianShuSidecarLaunchOptions options)
        => new()
        {
            WorkingDirectory = options.WorkingDirectory,
            ConfigPath = options.ConfigPath,
            ProfileName = options.ProfileName,
            AppHostProjectPath = options.AppHostProjectPath,
            CreateThreadOnInitialize = options.CreateThreadOnInitialize,
            Model = options.Model,
            ModelProvider = options.ModelProvider,
            ApprovalPolicy = options.ApprovalPolicy,
            SandboxMode = options.SandboxMode,
            WebSearchMode = options.WebSearchMode,
            ServiceTier = options.ServiceTier,
            CollaborationMode = options.CollaborationMode,
        };

    private Exception CreateProtocolException(string message)
    {
        var stderr = string.Join(Environment.NewLine, stderrLines.ToArray());
        return string.IsNullOrWhiteSpace(stderr)
            ? new InvalidOperationException(message)
            : new InvalidOperationException($"{message}{Environment.NewLine}{stderr}");
    }

    internal static TianShuSidecarFollowUpAcceptedResult ParseFollowUpAcceptedResult(
        JsonElement data,
        TianShuSidecarFollowUpMode requestedMode,
        string fallbackCorrelationId)
        => new()
        {
            CorrelationId = ReadString(data, "correlationId") ?? fallbackCorrelationId,
            RequestedMode = requestedMode,
        };

    private static TianShuSidecarThreadListResult ParseThreadListResult(JsonElement data)
    {
        if (!data.TryGetProperty("threads", out var threadsElement) || threadsElement.ValueKind != JsonValueKind.Array)
        {
            return new TianShuSidecarThreadListResult();
        }

        var list = new List<TianShuSidecarThreadItem>();
        foreach (var threadElement in threadsElement.EnumerateArray())
        {
            var thread = ParseThreadItem(threadElement);
            if (thread is not null)
            {
                list.Add(thread);
            }
        }

        return new TianShuSidecarThreadListResult
        {
            Items = list,
            NextCursor = ReadString(data, "nextCursor"),
        };
    }

    private static TianShuSidecarThreadSession? ParseThreadSession(JsonElement data)
    {
        var threadId = ReadString(data, "threadId");
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        var session = new TianShuSidecarThreadSession
        {
            ThreadId = threadId,
            Preview = ReadString(data, "preview") ?? string.Empty,
            Name = ReadString(data, "name"),
            Cwd = ReadString(data, "cwd"),
            Path = ReadString(data, "path"),
            ModelProvider = ReadString(data, "modelProvider"),
            Source = ReadSessionSource(data, "source"),
            CliVersion = ReadString(data, "cliVersion"),
            AgentNickname = ReadString(data, "agentNickname"),
            AgentRole = ReadString(data, "agentRole"),
            CreatedAt = ReadUnixTime(data, "createdAt"),
            UpdatedAt = ReadUnixTime(data, "updatedAt") ?? DateTimeOffset.Now,
            IsEphemeral = TryReadBoolean(data, "ephemeral") ?? false,
            MessagesAreAuthoritative = TryReadBoolean(data, "messagesAreAuthoritative") ?? false,
            SessionConfiguration = TryGetObject(data, "sessionConfiguration") is { } sessionConfiguration
                ? ParseThreadSessionConfiguration(sessionConfiguration)
                : null,
            Status = TryGetObject(data, "status") is { } status ? ParseThreadStatus(status) : null,
            GitInfo = TryGetObject(data, "gitInfo") is { } gitInfo ? ParseGitInfo(gitInfo) : null,
            Turns = ParseThreadTurns(data),
            SeedHistory = ParseSeedHistory(data),
            PendingInputState = ParsePendingInputStatePayload(data, "pendingInputState"),
            PendingInteractiveRequests = ParsePendingInteractiveRequests(data, "pendingInteractiveRequests"),
        };

        if ((session.MessagesAreAuthoritative || ShouldHydrateLegacyMessages(session.SeedHistory, session.Turns))
            && data.TryGetProperty("messages", out var messagesElement)
            && messagesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var messageElement in messagesElement.EnumerateArray())
            {
                var message = ParseConversationMessage(messageElement);
                if (message is not null)
                {
                    session.Messages.Add(message);
                }
            }
        }

        return session;
    }

    internal static TianShuSidecarThreadOperationResult ParseThreadOperationResult(string message, JsonElement data)
    {
        var threadPayload = TryGetObject(data, "thread") ?? (data.ValueKind == JsonValueKind.Object ? data : (JsonElement?)null);
        return new TianShuSidecarThreadOperationResult
        {
            Message = message,
            Thread = threadPayload.HasValue ? ParseThreadItem(threadPayload.Value) : null,
        };
    }

    private static TianShuSidecarThreadItem? ParseThreadItem(JsonElement data)
    {
        var threadId = ReadString(data, "threadId") ?? ReadString(data, "id");
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        return new TianShuSidecarThreadItem
        {
            ThreadId = threadId,
            Preview = ReadString(data, "preview") ?? string.Empty,
            Name = ReadString(data, "name"),
            Cwd = ReadString(data, "cwd"),
            Path = ReadString(data, "path"),
            ModelProvider = ReadString(data, "modelProvider"),
            Source = ReadSessionSource(data, "source"),
            CliVersion = ReadString(data, "cliVersion"),
            AgentNickname = ReadString(data, "agentNickname"),
            AgentRole = ReadString(data, "agentRole"),
            CreatedAt = ReadUnixTime(data, "createdAt"),
            UpdatedAt = ReadUnixTime(data, "updatedAt") ?? DateTimeOffset.Now,
            IsEphemeral = TryReadBoolean(data, "ephemeral") ?? false,
            Status = TryGetObject(data, "status") is { } status ? ParseThreadStatus(status) : null,
            GitInfo = TryGetObject(data, "gitInfo") is { } gitInfo ? ParseGitInfo(gitInfo) : null,
            SessionConfiguration = TryGetObject(data, "sessionConfiguration") is { } sessionConfiguration
                ? ParseThreadSessionConfiguration(sessionConfiguration)
                : null,
            Turns = ParseThreadTurns(data),
            SeedHistory = ParseSeedHistory(data),
            PendingInputState = ParsePendingInputStatePayload(data, "pendingInputState"),
            PendingInteractiveRequests = ParsePendingInteractiveRequests(data, "pendingInteractiveRequests"),
        };
    }

    private static bool ShouldHydrateLegacyMessages(
        IReadOnlyList<TianShuSidecarSeedHistoryItem> seedHistory,
        IReadOnlyList<TianShuSidecarThreadTurn> turns)
        => seedHistory.Count == 0 && turns.Count == 0;

    private static IReadOnlyList<TianShuSidecarPendingInteractiveRequestReplayPayload> ParsePendingInteractiveRequests(
        JsonElement data,
        string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<TianShuSidecarPendingInteractiveRequestReplayPayload>();
        }

        var requests = new List<TianShuSidecarPendingInteractiveRequestReplayPayload>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            long requestId = 0;
            string? requestIdRaw = null;
            if (item.TryGetProperty("requestId", out var requestIdElement))
            {
                switch (requestIdElement.ValueKind)
                {
                    case JsonValueKind.Number when requestIdElement.TryGetInt64(out var numericRequestId):
                        requestId = numericRequestId;
                        break;
                    case JsonValueKind.String:
                        requestIdRaw = Normalize(requestIdElement.GetString());
                        if (!string.IsNullOrWhiteSpace(requestIdRaw)
                            && long.TryParse(requestIdRaw, out var parsedRequestId))
                        {
                            requestId = parsedRequestId;
                        }

                        break;
                }
            }

            requests.Add(new TianShuSidecarPendingInteractiveRequestReplayPayload
            {
                RequestId = requestId,
                RequestIdRaw = requestIdRaw,
                RequestKind = ReadString(item, "requestKind") ?? string.Empty,
                RequestMethod = ReadString(item, "requestMethod"),
                CallId = ReadString(item, "callId") ?? string.Empty,
                ThreadId = ReadString(item, "threadId"),
                TurnId = ReadString(item, "turnId"),
                ToolName = ReadString(item, "toolName"),
                ServerName = ReadString(item, "serverName"),
                Text = ReadString(item, "text"),
                Status = ReadString(item, "status"),
                Phase = ReadString(item, "phase"),
                RequiresApproval = TryReadBoolean(item, "requiresApproval"),
                ApprovalKind = ReadString(item, "approvalKind"),
                AvailableDecisions = ReadStringArray(item, "availableDecisions"),
                AvailableDecisionOptions = ParseApprovalDecisionOptions(item, "availableDecisionOptions"),
                ApprovalRequest = ParseApprovalRequestPayload(item, "approvalRequest"),
                PermissionRequest = ParseStructuredPayload<TianShuSidecarPermissionRequestPayload>(item, "permissionRequest"),
                UserInputRequest = ParseStructuredPayload<TianShuSidecarUserInputRequestPayload>(item, "userInputRequest"),
            });
        }

        return requests;
    }

    private static TianShuSidecarPendingInputStatePayload? ParsePendingInputStatePayload(JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var entries = ParsePendingInputStateEntries(property, "entries") ?? Array.Empty<TianShuSidecarPendingInputStateEntryPayload>();
        var queuedUserMessages = ParsePendingInputStateEntries(
            property,
            "queuedUserMessages",
            forcedPendingBucket: "QueuedUserMessage");
        var pendingSteers = ParsePendingInputStateEntries(
            property,
            "pendingSteers",
            forcedPendingBucket: "PendingSteer");

        return new TianShuSidecarPendingInputStatePayload
        {
            Entries = FilterSupplementalPendingInputStateEntries(entries),
            QueuedUserMessages = queuedUserMessages ?? Array.Empty<TianShuSidecarPendingInputStateEntryPayload>(),
            PendingSteers = pendingSteers ?? Array.Empty<TianShuSidecarPendingInputStateEntryPayload>(),
            InterruptRequestPending = TryReadBoolean(property, "interruptRequestPending") ?? false,
            SubmitPendingSteersAfterInterrupt = TryReadBoolean(property, "submitPendingSteersAfterInterrupt") ?? false,
        };
    }

    private static bool IsInterruptPendingInputStateEntry(TianShuSidecarPendingInputStateEntryPayload entry)
        => string.Equals(entry.RequestedMode, "Interrupt", StringComparison.OrdinalIgnoreCase)
           && (entry.LifecycleState is "queued" or "interrupt_requested"
               || string.Equals(entry.LifecycleState, "interrupt_completed", StringComparison.OrdinalIgnoreCase));

    private static bool IsQueuedUserMessagePendingInputStateEntry(TianShuSidecarPendingInputStateEntryPayload entry)
        => string.Equals(entry.PendingBucket, "QueuedUserMessage", StringComparison.OrdinalIgnoreCase)
           && !IsInterruptPendingInputStateEntry(entry);

    private static bool IsPendingSteerPendingInputStateEntry(TianShuSidecarPendingInputStateEntryPayload entry)
        => string.Equals(entry.PendingBucket, "PendingSteer", StringComparison.OrdinalIgnoreCase);

    private static TianShuSidecarPendingInputStateEntryPayload[] FilterSupplementalPendingInputStateEntries(
        IReadOnlyList<TianShuSidecarPendingInputStateEntryPayload> entries)
        => entries
            .Where(static entry =>
                !IsQueuedUserMessagePendingInputStateEntry(entry)
                && !IsPendingSteerPendingInputStateEntry(entry))
            .ToArray();

    private static IReadOnlyList<TianShuSidecarPendingInputStateEntryPayload>? ParsePendingInputStateEntries(
        JsonElement property,
        string propertyName,
        string? forcedPendingBucket = null)
    {
        var entries = new List<TianShuSidecarPendingInputStateEntryPayload>();
        if (!property.TryGetProperty(propertyName, out var entriesElement) || entriesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in entriesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            entries.Add(new TianShuSidecarPendingInputStateEntryPayload
            {
                CorrelationId = ReadString(item, "correlationId") ?? string.Empty,
                RequestedMode = ReadString(item, "requestedMode") ?? string.Empty,
                EffectiveMode = ReadString(item, "effectiveMode") ?? string.Empty,
                LifecycleState = ReadString(item, "lifecycleState") ?? string.Empty,
                ExpectedTurnId = ReadString(item, "expectedTurnId"),
                TurnId = ReadString(item, "turnId"),
                PendingBucket = forcedPendingBucket ?? ReadString(item, "pendingBucket") ?? "QueuedUserMessage",
                CompareKey = TryGetObject(item, "compareKey") is { } compareKey
                    ? new TianShuSidecarPendingFollowUpCompareKeyPayload
                    {
                        Message = ReadString(compareKey, "message"),
                        ImageCount = TryGetInt32(compareKey, "imageCount") ?? 0,
                    }
                    : null,
                Inputs = ParseUserInputs(item, "inputs") ?? Array.Empty<TianShuSidecarUserInputPayload>(),
            });
        }

        return entries;
    }

    private static IReadOnlyList<TianShuSidecarUserInputPayload>? ParseUserInputs(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var inputsElement) || inputsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var inputs = new List<TianShuSidecarUserInputPayload>();
        foreach (var item in inputsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var textElements = new List<TianShuSidecarTextElementPayload>();
            if (item.TryGetProperty("textElements", out var textElementsArray)
                && textElementsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var textElement in textElementsArray.EnumerateArray())
                {
                    if (textElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var byteRange = TryGetObject(textElement, "byteRange") is { } byteRangeObject
                        ? new TianShuSidecarByteRangePayload
                        {
                            Start = TryGetInt32(byteRangeObject, "start") ?? 0,
                            End = TryGetInt32(byteRangeObject, "end") ?? 0,
                        }
                        : null;

                    textElements.Add(new TianShuSidecarTextElementPayload
                    {
                        ByteRange = byteRange,
                        Placeholder = ReadString(textElement, "placeholder"),
                    });
                }
            }

            inputs.Add(new TianShuSidecarUserInputPayload
            {
                Type = ReadString(item, "type") ?? string.Empty,
                Text = ReadString(item, "text"),
                Url = ReadString(item, "url"),
                Path = ReadString(item, "path"),
                Name = ReadString(item, "name"),
                TextElements = textElements,
            });
        }

        return inputs;
    }

    private static TianShuSidecarThreadStatus ParseThreadStatus(JsonElement data)
        => new()
        {
            Type = ReadString(data, "type") ?? string.Empty,
            ActiveFlags = ReadStringArray(data, "activeFlags"),
        };

    private static TianShuSidecarGitInfo ParseGitInfo(JsonElement data)
        => new()
        {
            Sha = ReadString(data, "sha"),
            Branch = ReadString(data, "branch"),
            OriginUrl = ReadString(data, "originUrl"),
        };

    private static TianShuSidecarThreadSessionConfiguration ParseThreadSessionConfiguration(JsonElement data)
        => new()
        {
            Model = ReadString(data, "model"),
            ModelProvider = ReadString(data, "modelProvider") ?? ReadString(data, "modelProviderId"),
            ModelProviderId = ReadString(data, "modelProviderId") ?? ReadString(data, "modelProvider"),
            ServiceTier = ReadServiceTier(data, "serviceTier"),
            ApprovalPolicy = ReadApprovalPolicy(data, "approvalPolicy"),
            SandboxPolicy = ReadSandboxMode(data, "sandboxPolicy") ?? ReadSandboxMode(data, "sandbox"),
            SandboxPolicyPayload = ReadStructuredValue(data, "sandboxPolicyPayload") ?? ReadStructuredValue(data, "sandboxPolicy"),
            ReasoningEffort = ReadString(data, "reasoningEffort") ?? ReadString(data, "reasoning_effort"),
            HistoryLogId = ReadString(data, "historyLogId"),
            HistoryEntryCount = TryGetInt32(data, "historyEntryCount"),
            RolloutPath = ReadString(data, "rolloutPath"),
            ForkedFromId = ReadString(data, "forkedFromId"),
            Cwd = ReadString(data, "cwd"),
            Ephemeral = TryReadBoolean(data, "ephemeral"),
            AllowLoginShell = TryReadBoolean(data, "allowLoginShell"),
            ShellEnvironmentPolicy = ReadStructuredValue(data, "shellEnvironmentPolicy"),
            ProviderBaseUrl = ReadString(data, "providerBaseUrl"),
            ProviderApiKeyEnvironmentVariable = ReadString(data, "providerApiKeyEnvironmentVariable"),
            ProviderWireApi = ReadString(data, "providerWireApi"),
            ProviderRequestMaxRetries = TryGetInt32(data, "providerRequestMaxRetries"),
            ProviderStreamMaxRetries = TryGetInt32(data, "providerStreamMaxRetries"),
            ProviderStreamIdleTimeoutMs = TryGetInt64(data, "providerStreamIdleTimeoutMs"),
            ProviderSupportsWebsockets = TryReadBoolean(data, "providerSupportsWebsockets"),
            WebSearchMode = ReadString(data, "webSearchMode"),
            ServiceName = ReadString(data, "serviceName"),
            BaseInstructions = ReadString(data, "baseInstructions"),
            DeveloperInstructions = ReadString(data, "developerInstructions"),
            UserInstructions = ReadString(data, "userInstructions"),
            ReasoningSummary = ReadString(data, "reasoningSummary"),
            Verbosity = ReadString(data, "verbosity"),
            Personality = ReadString(data, "personality"),
            DynamicTools = ReadStructuredValueArray(data, "dynamicTools"),
            CollaborationMode = ReadStructuredValue(data, "collaborationMode"),
            PersistExtendedHistory = TryReadBoolean(data, "persistExtendedHistory"),
            SessionSource = ReadSessionSource(data, "sessionSource"),
            WindowsSandboxLevel = ReadString(data, "windowsSandboxLevel"),
            DefaultModeRequestUserInputEnabled = TryReadBoolean(data, "defaultModeRequestUserInputEnabled"),
        };

    private static IReadOnlyList<TianShuSidecarThreadTurn> ParseThreadTurns(JsonElement data)
    {
        if (!data.TryGetProperty("turns", out var turnsElement) || turnsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<TianShuSidecarThreadTurn>();
        }

        var list = new List<TianShuSidecarThreadTurn>();
        foreach (var turnElement in turnsElement.EnumerateArray())
        {
            if (turnElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            list.Add(new TianShuSidecarThreadTurn
            {
                Id = ReadString(turnElement, "id") ?? string.Empty,
                Status = ReadString(turnElement, "status") ?? string.Empty,
                Error = TryGetObject(turnElement, "error") is { } error ? ParseThreadTurnError(error) : null,
                Items = ParseThreadTurnItems(turnElement),
            });
        }

        return list;
    }

    private static TianShuSidecarThreadTurnError ParseThreadTurnError(JsonElement data)
        => new()
        {
            Message = ReadString(data, "message"),
            AdditionalDetails = ReadString(data, "additionalDetails"),
        };

    private static IReadOnlyList<TianShuSidecarThreadTurnItem> ParseThreadTurnItems(JsonElement data)
    {
        if (!data.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<TianShuSidecarThreadTurnItem>();
        }

        var list = new List<TianShuSidecarThreadTurnItem>();
        foreach (var itemElement in itemsElement.EnumerateArray())
        {
            if (itemElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = ReadString(itemElement, "type") ?? string.Empty;

            list.Add(new TianShuSidecarThreadTurnItem
            {
                Id = ReadString(itemElement, "id") ?? string.Empty,
                Type = type,
                Phase = ReadString(itemElement, "phase"),
                Text = ReadThreadTurnItemText(itemElement),
                Inputs = IsUserMessageThreadTurnItem(type)
                    ? ParseUserInputs(itemElement, "content") ?? Array.Empty<TianShuSidecarUserInputPayload>()
                    : Array.Empty<TianShuSidecarUserInputPayload>(),
            });
        }

        return list;
    }

    private static string? ReadThreadTurnItemText(JsonElement data)
    {
        var directText = ReadString(data, "text");
        if (!string.IsNullOrWhiteSpace(directText))
        {
            return directText;
        }

        if (!data.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var textParts = new List<string>();
        foreach (var item in contentElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var text = ReadString(item, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                textParts.Add(text!);
            }
        }

        return textParts.Count == 0 ? null : string.Join(Environment.NewLine, textParts);
    }

    private static IReadOnlyList<TianShuSidecarSeedHistoryItem> ParseSeedHistory(JsonElement data)
    {
        if (!data.TryGetProperty("seedHistory", out var seedHistoryElement) || seedHistoryElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<TianShuSidecarSeedHistoryItem>();
        }

        var list = new List<TianShuSidecarSeedHistoryItem>();
        foreach (var itemElement in seedHistoryElement.EnumerateArray())
        {
            if (itemElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var role = ReadString(itemElement, "role") ?? string.Empty;
            var content = ReadString(itemElement, "content") ?? string.Empty;
            var inputs = ParseUserInputs(itemElement, "inputs")
                ?? Array.Empty<TianShuSidecarUserInputPayload>();

            list.Add(new TianShuSidecarSeedHistoryItem
            {
                Role = role,
                Content = content,
                Inputs = inputs,
            });
        }

        return list;
    }

    private static bool IsUserMessageThreadTurnItem(string? type)
    {
        var normalized = Normalize(type);
        return string.Equals(normalized, "usermessage", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "user_message", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<TianShuSidecarUserInputPayload> CreateTextUserInputs(string? role, string? content)
    {
        if (!string.Equals(Normalize(role), "user", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<TianShuSidecarUserInputPayload>();
        }

        var normalizedContent = Normalize(content);
        return string.IsNullOrWhiteSpace(normalizedContent)
            ? Array.Empty<TianShuSidecarUserInputPayload>()
            : new[]
            {
                new TianShuSidecarUserInputPayload
                {
                    Type = "text",
                    Text = normalizedContent,
                },
            };
    }

    private static string? ExtractConversationText(IReadOnlyList<TianShuSidecarUserInputPayload>? inputs)
    {
        if (inputs is not { Count: > 0 })
        {
            return null;
        }

        var parts = inputs
            .Where(static item => string.Equals(item.Type, "text", StringComparison.OrdinalIgnoreCase))
            .Select(static item => Normalize(item.Text))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
        return parts.Length == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    private static IReadOnlyList<TianShuSidecarUserInputPayload> CloneUserInputs(IReadOnlyList<TianShuSidecarUserInputPayload>? inputs)
    {
        if (inputs is not { Count: > 0 })
        {
            return Array.Empty<TianShuSidecarUserInputPayload>();
        }

        return inputs
            .Select(static item => new TianShuSidecarUserInputPayload
            {
                Type = item.Type,
                Text = item.Text,
                Url = item.Url,
                Path = item.Path,
                Name = item.Name,
                TextElements = item.TextElements
                    .Select(static element => new TianShuSidecarTextElementPayload
                    {
                        Placeholder = element.Placeholder,
                        ByteRange = element.ByteRange is null
                            ? null
                            : new TianShuSidecarByteRangePayload
                            {
                                Start = element.ByteRange.Start,
                                End = element.ByteRange.End,
                            },
                    })
                    .ToArray(),
            })
            .ToArray();
    }

    private static TianShuSidecarConversationMessage? ParseConversationMessage(JsonElement data)
    {
        var role = ReadString(data, "role");
        var content = ReadString(data, "content");
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return new TianShuSidecarConversationMessage
        {
            Role = role,
            Content = content,
            Timestamp = ReadUnixTime(data, "timestamp") ?? DateTimeOffset.Now,
            Inputs = ParseUserInputs(data, "inputs") ?? Array.Empty<TianShuSidecarUserInputPayload>(),
        };
    }

    internal static TianShuSidecarConfigReadResult ParseConfigReadResult(string message, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return new TianShuSidecarConfigReadResult
            {
                Message = message,
            };
        }

        var parsedConfig = ParseConfigSnapshot(data);
        var parsedOrigins = ParseConfigOrigins(data);

        if (data.TryGetProperty("fields", out var typedFields) && typedFields.ValueKind == JsonValueKind.Array)
        {
            return new TianShuSidecarConfigReadResult
            {
                Message = message,
                Config = parsedConfig,
                Origins = parsedOrigins,
                Fields = ParseConfigFields(typedFields),
                Layers = data.TryGetProperty("layers", out var typedLayers) && typedLayers.ValueKind == JsonValueKind.Array
                    ? ParseConfigLayers(typedLayers)
                    : Array.Empty<TianShuSidecarConfigLayer>(),
            };
        }

        var fields = new List<TianShuSidecarConfigField>();
        var config = TryGetObject(data, "config");
        if (config.HasValue)
        {
            foreach (var property in config.Value.EnumerateObject())
            {
                if (IsTypedConfigTopLevelKey(property.Name))
                {
                    continue;
                }

                var sourceType = string.Empty;
                var sourcePath = string.Empty;
                var sourceText = "来源未知";
                if (parsedOrigins.TryGetValue(property.Name, out var originEntry)
                    && originEntry.Name is { } originName)
                {
                    sourceType = string.IsNullOrWhiteSpace(originName.Type)
                        ? string.Empty
                        : originName.Type.Trim();
                    sourcePath = !string.IsNullOrWhiteSpace(originName.File)
                        ? originName.File.Trim()
                        : !string.IsNullOrWhiteSpace(originName.DotTianShuFolder)
                            ? originName.DotTianShuFolder.Trim()
                            : string.Empty;
                    sourceText = string.IsNullOrWhiteSpace(sourcePath)
                        ? (string.IsNullOrWhiteSpace(sourceType) ? "来源未知" : sourceType)
                        : $"{sourceType} · {sourcePath}";
                }

                fields.Add(new TianShuSidecarConfigField
                {
                    KeyPath = property.Name,
                    ValueKind = property.Value.ValueKind.ToString(),
                    ValueText = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText(),
                    ValueJson = property.Value.GetRawText(),
                    SourceType = sourceType,
                    SourcePath = sourcePath,
                    SourceText = sourceText,
                });
            }
        }

        var layers = new List<TianShuSidecarConfigLayer>();
        if (data.TryGetProperty("layers", out var layersElement) && layersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var layer in layersElement.EnumerateArray())
            {
                if (layer.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var layerName = layer.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetRawText()
                    : string.Empty;
                var layerVersion = ReadString(layer, "version") ?? string.Empty;
                var layerConfig = layer.TryGetProperty("config", out var layerConfigElement) && layerConfigElement.ValueKind == JsonValueKind.Object
                    ? layerConfigElement.GetRawText()
                    : "{}";

                layers.Add(new TianShuSidecarConfigLayer
                {
                    NameJson = layerName,
                    Version = layerVersion,
                    ConfigJson = layerConfig,
                });
            }
        }

        return new TianShuSidecarConfigReadResult
        {
            Message = message,
            Config = parsedConfig,
            Origins = parsedOrigins,
            Fields = fields,
            Layers = layers,
        };
    }

    private static TianShuSidecarConfigSnapshot? ParseConfigSnapshot(JsonElement data)
    {
        var config = TryGetObject(data, "config");
        if (!config.HasValue)
        {
            return null;
        }

        return JsonSerializer.Deserialize<TianShuSidecarConfigSnapshot>(
            config.Value.GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static bool IsTypedConfigTopLevelKey(string keyPath)
    {
        return !string.IsNullOrWhiteSpace(keyPath) && TypedConfigTopLevelKeys.Value.Contains(keyPath);
    }

    private static HashSet<string> CreateTypedConfigTopLevelKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in typeof(TianShuSidecarConfigSnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
            if (!string.IsNullOrWhiteSpace(jsonName))
            {
                keys.Add(jsonName);
            }
        }

        return keys;
    }

    private static IReadOnlyDictionary<string, TianShuSidecarConfigOrigin> ParseConfigOrigins(JsonElement data)
    {
        var origins = TryGetObject(data, "origins");
        if (!origins.HasValue)
        {
            return new Dictionary<string, TianShuSidecarConfigOrigin>(StringComparer.Ordinal);
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, TianShuSidecarConfigOrigin>>(
                         origins.Value.GetRawText(),
                         new JsonSerializerOptions(JsonSerializerDefaults.Web))
                     ?? new Dictionary<string, TianShuSidecarConfigOrigin>(StringComparer.Ordinal);

        if (parsed.Count == 0)
        {
            return new Dictionary<string, TianShuSidecarConfigOrigin>(StringComparer.Ordinal);
        }

        return parsed.Count == 0
            ? new Dictionary<string, TianShuSidecarConfigOrigin>(StringComparer.Ordinal)
            : parsed;
    }

    internal static TianShuSidecarModelCatalogResult ParseModelCatalogResult(string message, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return new TianShuSidecarModelCatalogResult
            {
                Message = message,
            };
        }

        if (data.TryGetProperty("items", out var typedItems) && typedItems.ValueKind == JsonValueKind.Array)
        {
            return new TianShuSidecarModelCatalogResult
            {
                Message = message,
                NextCursor = ReadString(data, "nextCursor"),
                Items = ParseModelCatalogItems(typedItems),
            };
        }

        var items = new List<TianShuSidecarModelCatalogItem>();
        if (data.TryGetProperty("data", out var itemArray) && itemArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                items.Add(new TianShuSidecarModelCatalogItem
                {
                    Id = ReadString(item, "id") ?? string.Empty,
                    Model = ReadString(item, "model") ?? string.Empty,
                    DisplayName = ReadString(item, "displayName") ?? string.Empty,
                    DefaultReasoningEffort = ReadString(item, "defaultReasoningEffort") ?? "medium",
                    Description = ReadString(item, "description") ?? string.Empty,
                    SupportedReasoningEfforts = ReadObjectStringArray(item, "supportedReasoningEfforts", "reasoningEffort"),
                    InputModalities = ReadStringArray(item, "inputModalities"),
                    SupportsPersonality = TryReadBoolean(item, "supportsPersonality") ?? false,
                });
            }
        }

        return new TianShuSidecarModelCatalogResult
        {
            Message = message,
            NextCursor = ReadString(data, "nextCursor"),
            Items = items,
        };
    }

    private static TianShuSidecarConfigWriteResult ParseConfigWriteResult(string message, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return new TianShuSidecarConfigWriteResult
            {
                Message = message,
            };
        }

        var status = ReadString(data, "status") ?? string.Empty;
        return new TianShuSidecarConfigWriteResult
        {
            Message = message,
            Status = status,
            Version = ReadString(data, "version") ?? string.Empty,
            FilePath = ReadString(data, "filePath") ?? string.Empty,
            IsOverridden = string.Equals(status, "okOverridden", StringComparison.OrdinalIgnoreCase),
            OverriddenMetadata = ParseConfigWriteOverriddenMetadata(data),
        };
    }

    private static TianShuSidecarConfigWriteOverriddenMetadata? ParseConfigWriteOverriddenMetadata(JsonElement data)
    {
        if (!data.TryGetProperty("overriddenMetadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new TianShuSidecarConfigWriteOverriddenMetadata
        {
            Message = ReadString(metadata, "message") ?? string.Empty,
            EffectiveValue = metadata.TryGetProperty("effectiveValue", out var effectiveValue)
                ? TianShuSidecarStructuredValue.FromJsonElement(effectiveValue)
                : null,
            OverridingLayerType = metadata.TryGetProperty("overridingLayer", out var overridingLayer)
                ? ReadString(overridingLayer, "type") ?? ReadString(overridingLayer, "name")
                : null,
            OverridingLayerFile = metadata.TryGetProperty("overridingLayer", out overridingLayer)
                ? ReadString(overridingLayer, "file")
                : null,
            OverridingLayerDotTianShuFolder = metadata.TryGetProperty("overridingLayer", out overridingLayer)
                ? ReadString(overridingLayer, "dotTianShuFolder")
                : null,
            OverridingLayerVersion = metadata.TryGetProperty("overridingLayer", out overridingLayer)
                ? ReadString(overridingLayer, "version")
                : null,
        };
    }

    private static TianShuSidecarConfigRequirementsReadResult ParseConfigRequirementsReadResult(string message, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return new TianShuSidecarConfigRequirementsReadResult
            {
                Message = message,
            };
        }

        if (data.TryGetProperty("requirements", out var requirementsProperty)
            && requirementsProperty.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new TianShuSidecarConfigRequirementsReadResult
            {
                Message = message,
                IsDefined = false,
            };
        }

        JsonElement? requirements = TryGetObject(data, "requirements");
        if (!requirements.HasValue)
        {
            requirements = data;
        }

        var isDefined = TryReadBoolean(data, "isDefined");
        if (isDefined == false)
        {
            return new TianShuSidecarConfigRequirementsReadResult
            {
                Message = message,
                IsDefined = false,
            };
        }

        return new TianShuSidecarConfigRequirementsReadResult
        {
            Message = message,
            IsDefined = true,
            AllowedApprovalPolicies = ReadStringArray(requirements.Value, "allowedApprovalPolicies"),
            AllowedSandboxModes = ReadStringArray(requirements.Value, "allowedSandboxModes"),
            AllowedWebSearchModes = ReadStringArray(requirements.Value, "allowedWebSearchModes"),
            FeatureRequirements = ReadBooleanDictionary(requirements.Value, "featureRequirements"),
            EnforceResidency = ReadString(requirements.Value, "enforceResidency"),
            Network = ParseConfigRequirementsNetwork(requirements.Value),
        };
    }

    private static TianShuSidecarExperimentalFeatureListResult ParseExperimentalFeatureListResult(string message, JsonElement data)
    {
        var result = DeserializeTypedResult<TianShuSidecarExperimentalFeatureListResult>(data);
        result.Message = message;
        return result;
    }

    private static TianShuSidecarCollaborationModeListResult ParseCollaborationModeListResult(string message, JsonElement data)
    {
        var result = DeserializeTypedResult<TianShuSidecarCollaborationModeListResult>(data);
        result.Message = message;
        return result;
    }

    private static TianShuSidecarMcpServerStatusListResult ParseMcpServerStatusListResult(string message, JsonElement data)
    {
        var result = DeserializeTypedResult<TianShuSidecarMcpServerStatusListResult>(data);
        result.Message = message;
        return result;
    }

    private static TianShuSidecarSkillsListResult ParseSkillsListResult(string message, JsonElement data)
    {
        var result = DeserializeTypedResult<TianShuSidecarSkillsListResult>(data);
        result.Message = message;
        return result;
    }

    private static TianShuSidecarSkillsRemoteListResult ParseSkillsRemoteListResult(string message, JsonElement data)
    {
        var result = DeserializeTypedResult<TianShuSidecarSkillsRemoteListResult>(data);
        result.Message = message;
        return result;
    }

    private static TianShuSidecarSkillsRemoteExportResult ParseSkillsRemoteExportResult(string message, JsonElement data)
    {
        var result = DeserializeTypedResult<TianShuSidecarSkillsRemoteExportResult>(data);
        result.Message = message;
        return result;
    }

    private static TianShuSidecarPluginListResult ParsePluginListResult(string message, JsonElement data)
    {
        var result = DeserializeTypedResult<TianShuSidecarPluginListResult>(data);
        result.Message = message;
        return result;
    }

    private static TianShuSidecarPluginReadResult ParsePluginReadResult(string message, JsonElement data)
    {
        var result = DeserializeTypedResult<TianShuSidecarPluginReadResult>(data);
        result.Message = message;
        return result;
    }

    private static TianShuSidecarPluginInstallResult ParsePluginInstallResult(string message, JsonElement data)
    {
        var result = DeserializeTypedResult<TianShuSidecarPluginInstallResult>(data);
        result.Message = message;
        return result;
    }

    private static TianShuSidecarAppListResult ParseAppListResult(string message, JsonElement data)
    {
        var result = DeserializeTypedResult<TianShuSidecarAppListResult>(data);
        result.Message = message;
        return result;
    }

    private static TianShuSidecarReviewStartResult ParseReviewStartResult(string message, JsonElement data)
    {
        var result = DeserializeTypedResult<TianShuSidecarReviewStartResult>(data);
        result.Message = message;
        return result;
    }

    private static TianShuSidecarMcpServerOauthLoginStartResult ParseMcpServerOauthLoginStartResult(string message, JsonElement data)
    {
        var result = DeserializeTypedResult<TianShuSidecarMcpServerOauthLoginStartResult>(data);
        result.Message = message;
        return result;
    }

    private static TianShuSidecarConversationSummaryResult ParseConversationSummaryResult(string message, JsonElement data)
    {
        var result = DeserializeTypedResult<TianShuSidecarConversationSummaryResult>(data);
        result.Message = message;
        return result;
    }

    private static TianShuSidecarGitDiffToRemoteResult ParseGitDiffToRemoteResult(string message, JsonElement data)
    {
        var result = DeserializeTypedResult<TianShuSidecarGitDiffToRemoteResult>(data);
        result.Message = message;
        return result;
    }

    private static TPayload DeserializeTypedResult<TPayload>(JsonElement data)
        where TPayload : new()
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return new TPayload();
        }

        return JsonSerializer.Deserialize<TPayload>(data.GetRawText(), StaticJsonOptions) ?? new TPayload();
    }

    private static TianShuSidecarConfigRequirementsNetwork? ParseConfigRequirementsNetwork(JsonElement requirements)
    {
        var network = TryGetObject(requirements, "network");
        if (!network.HasValue)
        {
            return null;
        }

        return new TianShuSidecarConfigRequirementsNetwork
        {
            Enabled = TryReadBoolean(network.Value, "enabled"),
            HttpPort = TryReadUInt16(network.Value, "httpPort"),
            SocksPort = TryReadUInt16(network.Value, "socksPort"),
            AllowUpstreamProxy = TryReadBoolean(network.Value, "allowUpstreamProxy"),
            DangerouslyAllowNonLoopbackProxy = TryReadBoolean(network.Value, "dangerouslyAllowNonLoopbackProxy"),
            DangerouslyAllowNonLoopbackAdmin = TryReadBoolean(network.Value, "dangerouslyAllowNonLoopbackAdmin"),
            DangerouslyAllowAllUnixSockets = TryReadBoolean(network.Value, "dangerouslyAllowAllUnixSockets"),
            AllowedDomains = ReadStringArray(network.Value, "allowedDomains"),
            DeniedDomains = ReadStringArray(network.Value, "deniedDomains"),
            AllowUnixSockets = ReadStringArray(network.Value, "allowUnixSockets"),
            AllowLocalBinding = TryReadBoolean(network.Value, "allowLocalBinding"),
        };
    }

    private static IReadOnlyList<TianShuSidecarConfigField> ParseConfigFields(JsonElement fieldsElement)
    {
        var fields = new List<TianShuSidecarConfigField>();
        foreach (var field in fieldsElement.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            fields.Add(new TianShuSidecarConfigField
            {
                KeyPath = ReadString(field, "keyPath") ?? string.Empty,
                ValueKind = ReadString(field, "valueKind") ?? string.Empty,
                ValueText = ReadString(field, "valueText") ?? string.Empty,
                ValueJson = ReadString(field, "valueJson") ?? string.Empty,
                SourceType = ReadString(field, "sourceType") ?? string.Empty,
                SourcePath = ReadString(field, "sourcePath") ?? string.Empty,
                SourceText = ReadString(field, "sourceText") ?? "来源未知",
            });
        }

        return fields;
    }

    private static IReadOnlyList<TianShuSidecarConfigLayer> ParseConfigLayers(JsonElement layersElement)
    {
        var layers = new List<TianShuSidecarConfigLayer>();
        foreach (var layer in layersElement.EnumerateArray())
        {
            if (layer.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            layers.Add(new TianShuSidecarConfigLayer
            {
                NameJson = ReadString(layer, "nameJson") ?? string.Empty,
                Version = ReadString(layer, "version") ?? string.Empty,
                ConfigJson = ReadString(layer, "configJson") ?? "{}",
                DisabledReason = ReadString(layer, "disabledReason"),
            });
        }

        return layers;
    }

    private static IReadOnlyList<TianShuSidecarModelCatalogItem> ParseModelCatalogItems(JsonElement itemsElement)
    {
        var items = new List<TianShuSidecarModelCatalogItem>();
        foreach (var item in itemsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new TianShuSidecarModelCatalogItem
            {
                Id = ReadString(item, "id") ?? string.Empty,
                Model = ReadString(item, "model") ?? string.Empty,
                DisplayName = ReadString(item, "displayName") ?? string.Empty,
                DefaultReasoningEffort = ReadString(item, "defaultReasoningEffort") ?? "medium",
                Description = ReadString(item, "description") ?? string.Empty,
                SupportedReasoningEfforts = ReadStringArray(item, "supportedReasoningEfforts"),
                InputModalities = ReadStringArray(item, "inputModalities"),
                SupportsPersonality = TryReadBoolean(item, "supportsPersonality") ?? false,
            });
        }

        return items;
    }

    private static JsonElement? TryGetObject(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return property;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadObjectStringArray(JsonElement element, string propertyName, string valuePropertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var value = ReadString(item, valuePropertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static TianShuSidecarSessionSource? ReadSessionSource(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return TianShuSidecarSessionSource.FromJsonElement(property);
    }

    private static TianShuSidecarStructuredValue? ReadStructuredValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return TianShuSidecarStructuredValue.FromJsonElement(property);
    }

    private static IReadOnlyList<TianShuSidecarStructuredValue>? ReadStructuredValueArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return property.EnumerateArray().Select(TianShuSidecarStructuredValue.FromJsonElement).ToArray();
    }

    private static string? ReadSandboxMode(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Object when property.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                => type.GetString(),
            _ => property.ToString(),
        };
    }

    private static TianShuSidecarServiceTier? ReadServiceTier(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return TianShuSidecarServiceTier.TryParse(value, out var tier) ? tier : null;
    }

    private static TianShuSidecarApprovalPolicy? ReadApprovalPolicy(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        try
        {
            return property.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => TianShuSidecarApprovalPolicy.TryParse(property.GetString(), out var policy) ? policy : null,
                JsonValueKind.Object => JsonSerializer.Deserialize<TianShuSidecarApprovalPolicy>(property.GetRawText(), StaticJsonOptions),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0
            ? null
            : trimmed.Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
    }

    private static bool TryMapApprovalDecision(string? rawValue, out TianShuApprovalDecision decision)
    {
        decision = TianShuApprovalDecision.Decline;
        var normalized = Normalize(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized switch
        {
            "accept" => TryAssign(TianShuApprovalDecision.Accept, out decision),
            "acceptforsession" => TryAssign(TianShuApprovalDecision.AcceptForSession, out decision),
            "acceptandremember" => TryAssign(TianShuApprovalDecision.AcceptAndRemember, out decision),
            "acceptwithexecpolicyamendment" => TryAssign(TianShuApprovalDecision.AcceptWithExecPolicyAmendment, out decision),
            "applynetworkpolicyamendment" => TryAssign(TianShuApprovalDecision.ApplyNetworkPolicyAmendment, out decision),
            "cancel" => TryAssign(TianShuApprovalDecision.Cancel, out decision),
            "decline" => TryAssign(TianShuApprovalDecision.Decline, out decision),
            _ => false,
        };
    }

    private static bool TryAssign(TianShuApprovalDecision value, out TianShuApprovalDecision decision)
    {
        decision = value;
        return true;
    }

    private static TPayload? ParseStructuredPayload<TPayload>(JsonElement data, string propertyName)
        where TPayload : class
    {
        if (!data.TryGetProperty(propertyName, out var property)
            || property.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TPayload>(property.GetRawText(), StaticJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TianShuSidecarApprovalRequestPayload? ParseApprovalRequestPayload(JsonElement data, string propertyName)
    {
        var payload = ParseStructuredPayload<TianShuSidecarApprovalRequestDto>(data, propertyName);
        if (payload is null)
        {
            return null;
        }

        return new TianShuSidecarApprovalRequestPayload
        {
            ToolName = payload.ToolName,
            ApprovalKind = payload.ApprovalKind,
            AvailableDecisions = payload.AvailableDecisions,
            AvailableDecisionOptions = payload.AvailableDecisionOptions?
                .Select(MapApprovalDecisionOption)
                .Where(static item => item is not null)
                .Cast<TianShuSidecarApprovalDecisionOptionPayload>()
                .ToArray(),
            Summary = payload.Summary,
            MetadataFields = payload.MetadataFields ?? [],
        };
    }

    private static IReadOnlyList<TianShuSidecarApprovalDecisionOptionPayload> ParseApprovalDecisionOptions(
        JsonElement data,
        string propertyName)
    {
        var options = ParseStructuredPayload<TianShuSidecarApprovalDecisionOptionDto[]>(data, propertyName);
        return options is not { Length: > 0 }
            ? Array.Empty<TianShuSidecarApprovalDecisionOptionPayload>()
            : options
                .Select(MapApprovalDecisionOption)
                .Where(static item => item is not null)
                .Cast<TianShuSidecarApprovalDecisionOptionPayload>()
                .ToArray();
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.True
            || (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var value) && value);
    }

    private static bool? TryReadBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
            _ => null,
        };
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), out var value) => value,
            _ => null,
        };
    }

    private static long? TryGetInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(property.GetString(), out var value) => value,
            _ => null,
        };
    }

    private static ushort? TryReadUInt16(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetUInt16(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String && ushort.TryParse(property.GetString(), out value))
        {
            return value;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, bool> ReadBooleanDictionary(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, bool>();
        }

        var values = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in property.EnumerateObject())
        {
            var value = item.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(item.Value.GetString(), out var parsed) => parsed,
                _ => (bool?)null,
            };

            if (value.HasValue)
            {
                values[item.Name] = value.Value;
            }
        }

        return values;
    }

    private static DateTimeOffset? ReadUnixTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
        {
            return DateTimeOffset.FromUnixTimeSeconds(number);
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var text = property.GetString();
            if (long.TryParse(text, out var parsed))
            {
                return DateTimeOffset.FromUnixTimeSeconds(parsed);
            }

            if (DateTimeOffset.TryParse(text, out var timestamp))
            {
                return timestamp;
            }
        }

        return null;
    }
}

internal sealed class TianShuSidecarLaunchOptions
{
    public string WorkingDirectory { get; set; } = string.Empty;

    public string ConfigPath { get; set; } = string.Empty;

    public string ProfileName { get; set; } = string.Empty;

    public string AppHostProjectPath { get; set; } = string.Empty;

    public bool CreateThreadOnInitialize { get; set; } = true;

    public string? Model { get; set; }

    public string? ModelProvider { get; set; }

    public string? ApprovalPolicy { get; set; }

    public string? SandboxMode { get; set; }

    public string? WebSearchMode { get; set; }

    public string? ServiceTier { get; set; }

    public string? CollaborationMode { get; set; }
}


public enum TianShuSidecarFollowUpMode
{
    Queue,
    Steer,
    Interrupt,
}

public enum TianShuSidecarCapability
{
    RuntimeSurface,
    CommandExecution,
    CodeModeExec,
    CodeModeWait,
    FuzzyFileSearch,
    ThreadOperation,
    Realtime,
    AgentOperation,
    Feedback,
    WindowsSandbox,
}

public enum TianShuApprovalDecision
{
    Accept,
    AcceptForSession,
    AcceptAndRemember,
    AcceptWithExecPolicyAmendment,
    ApplyNetworkPolicyAmendment,
    Decline,
    Cancel,
}

public enum TianShuPermissionGrantScope
{
    Turn,
    Session,
}

internal static class TianShuSidecarFollowUpModeExtensions
{
    public static string ToProtocolValue(this TianShuSidecarFollowUpMode mode)
        => mode switch
        {
            TianShuSidecarFollowUpMode.Steer => "steer",
            TianShuSidecarFollowUpMode.Interrupt => "interrupt",
            _ => "queue",
        };
}

internal static class TianShuSidecarCapabilityExtensions
{
    public static string ToProtocolValue(this TianShuSidecarCapability capability)
        => capability switch
        {
            TianShuSidecarCapability.CommandExecution => "commandExecution",
            TianShuSidecarCapability.CodeModeExec => "codeModeExec",
            TianShuSidecarCapability.CodeModeWait => "codeModeWait",
            TianShuSidecarCapability.FuzzyFileSearch => "fuzzyFileSearch",
            TianShuSidecarCapability.ThreadOperation => "threadOperation",
            TianShuSidecarCapability.AgentOperation => "agentOperation",
            TianShuSidecarCapability.WindowsSandbox => "windowsSandbox",
            _ => capability.ToString(),
        };
}

internal static class TianShuApprovalDecisionExtensions
{
    public static bool IsApproved(this TianShuApprovalDecision decision)
        => decision is TianShuApprovalDecision.Accept
            or TianShuApprovalDecision.AcceptForSession
            or TianShuApprovalDecision.AcceptAndRemember
            or TianShuApprovalDecision.AcceptWithExecPolicyAmendment
            or TianShuApprovalDecision.ApplyNetworkPolicyAmendment;

    public static string ToProtocolValue(this TianShuApprovalDecision decision)
        => decision switch
        {
            TianShuApprovalDecision.Accept => "Accept",
            TianShuApprovalDecision.AcceptForSession => "AcceptForSession",
            TianShuApprovalDecision.AcceptAndRemember => "AcceptAndRemember",
            TianShuApprovalDecision.AcceptWithExecPolicyAmendment => "AcceptWithExecPolicyAmendment",
            TianShuApprovalDecision.ApplyNetworkPolicyAmendment => "ApplyNetworkPolicyAmendment",
            TianShuApprovalDecision.Cancel => "Cancel",
            _ => "Decline",
        };
}

internal static class TianShuPermissionGrantScopeExtensions
{
    public static string ToProtocolValue(this TianShuPermissionGrantScope scope)
        => scope == TianShuPermissionGrantScope.Session ? "session" : "turn";
}

internal sealed class TianShuSidecarResponse
{
    public string RequestId { get; set; } = string.Empty;

    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? PayloadJson { get; set; }

    public string? DiagnosticJson { get; set; }
}

internal sealed class SidecarProtocolResponse
{
    public string RequestId { get; set; } = string.Empty;

    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public JsonElement PayloadData { get; set; }
}

internal sealed class TianShuSidecarEvent : EventArgs
{
    public int BridgeGeneration { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string? State { get; set; }

    public string? ThreadId { get; set; }

    public string? Message { get; set; }

    public string? Text { get; set; }

    public string? TurnId { get; set; }

    public string? CallId { get; set; }

    public string? ToolName { get; set; }

    public string? ItemId { get; set; }

    public string? Status { get; set; }

    public string? Phase { get; set; }

    public string? SourceMethod { get; set; }

    public string? TaskType { get; set; }

    public string? OperationName { get; set; }

    public string? ServerName { get; set; }

    public bool? RequiresApproval { get; set; }

    public bool? WillRetry { get; set; }

    public TianShuSidecarPlanPayload? Plan { get; set; }

    public TianShuSidecarToolCallPayload? ToolCall { get; set; }

    public TianShuSidecarApprovalRequestPayload? ApprovalRequest { get; set; }

    public TianShuSidecarPermissionRequestPayload? PermissionRequest { get; set; }

    public TianShuSidecarUserInputRequestPayload? UserInputRequest { get; set; }

    public TianShuSidecarServerRequestResolvedPayload? ServerRequestResolved { get; set; }

    public TianShuSidecarTaskPayload? Task { get; set; }

    public TianShuSidecarOperationPayload? Operation { get; set; }

    public TianShuSidecarReasoningPayload? Reasoning { get; set; }

    public TianShuSidecarMcpServerStatusPayload? McpServerStatus { get; set; }

    public TianShuSidecarItemPayload? Item { get; set; }

    public TianShuSidecarCommittedUserMessagePayload? CommittedUserMessage { get; set; }

    public TianShuSidecarPendingFollowUpPayload? PendingFollowUp { get; set; }

    public TianShuSidecarPendingInputStatePayload? PendingInputState { get; set; }

    public TianShuSidecarThreadTurnError? TurnError { get; set; }

    public TianShuSidecarAgentJobProgressPayload? AgentJobProgress { get; set; }

    public TianShuSidecarDeprecationNoticePayload? DeprecationNotice { get; set; }

    public TianShuSidecarConfigWarningPayload? ConfigWarning { get; set; }

    public TianShuSidecarThreadStatusChangedPayload? ThreadStatusChanged { get; set; }

    public TianShuSidecarThreadNameUpdatedPayload? ThreadNameUpdated { get; set; }

    public TianShuSidecarThreadTokenUsagePayload? ThreadTokenUsage { get; set; }

    public TianShuSidecarCommandExecOutputDeltaPayload? CommandExecOutputDelta { get; set; }

    public TianShuSidecarAppListUpdatedPayload? AppListUpdated { get; set; }

    public TianShuSidecarWindowsSandboxSetupPayload? WindowsSandboxSetup { get; set; }

    public TianShuSidecarMcpServerOauthLoginPayload? McpServerOauthLogin { get; set; }

    public TianShuSidecarRealtimeSessionPayload? RealtimeSession { get; set; }

    public TianShuSidecarFuzzyFileSearchSessionPayload? FuzzyFileSearchSession { get; set; }

    public TianShuSidecarThreadRealtimeItemAddedPayload? ThreadRealtimeItemAdded { get; set; }

    public TianShuSidecarThreadRealtimeOutputAudioDeltaPayload? ThreadRealtimeOutputAudioDelta { get; set; }

    public TianShuSidecarThreadRealtimeErrorPayload? ThreadRealtimeError { get; set; }

    public TianShuSidecarThreadRealtimeClosedPayload? ThreadRealtimeClosed { get; set; }

    public string? DiagnosticJson { get; set; }
}

internal sealed class TianShuSidecarConfigReadResult
{
    public string Message { get; set; } = string.Empty;

    public TianShuSidecarConfigSnapshot? Config { get; set; }

    public IReadOnlyDictionary<string, TianShuSidecarConfigOrigin> Origins { get; set; } =
        new Dictionary<string, TianShuSidecarConfigOrigin>(StringComparer.Ordinal);

    public IReadOnlyList<TianShuSidecarConfigField> Fields { get; set; } = Array.Empty<TianShuSidecarConfigField>();

    public IReadOnlyList<TianShuSidecarConfigLayer> Layers { get; set; } = Array.Empty<TianShuSidecarConfigLayer>();
}

internal sealed class TianShuSidecarConfigSnapshot
{
    [JsonPropertyName("analytics")]
    public TianShuSidecarConfigAnalytics? Analytics { get; set; }

    [JsonPropertyName("approval_policy")]
    public string? ApprovalPolicy { get; set; }

    [JsonPropertyName("compact_prompt")]
    public string? CompactPrompt { get; set; }

    [JsonPropertyName("default_permissions")]
    public string? DefaultPermissions { get; set; }

    [JsonPropertyName("developer_instructions")]
    public string? DeveloperInstructions { get; set; }

    [JsonPropertyName("disable_paste_burst")]
    public bool? DisablePasteBurst { get; set; }

    [JsonPropertyName("experimental_compact_prompt_file")]
    public string? ExperimentalCompactPromptFile { get; set; }

    [JsonPropertyName("experimental_realtime_start_instructions")]
    public string? ExperimentalRealtimeStartInstructions { get; set; }

    [JsonPropertyName("experimental_realtime_ws_backend_prompt")]
    public string? ExperimentalRealtimeWsBackendPrompt { get; set; }

    [JsonPropertyName("experimental_realtime_ws_base_url")]
    public string? ExperimentalRealtimeWsBaseUrl { get; set; }

    [JsonPropertyName("experimental_realtime_ws_mode")]
    public string? ExperimentalRealtimeWsMode { get; set; }

    [JsonPropertyName("experimental_realtime_ws_model")]
    public string? ExperimentalRealtimeWsModel { get; set; }

    [JsonPropertyName("experimental_realtime_ws_startup_context")]
    public string? ExperimentalRealtimeWsStartupContext { get; set; }

    [JsonPropertyName("experimental_use_freeform_apply_patch")]
    public bool? ExperimentalUseFreeformApplyPatch { get; set; }

    [JsonPropertyName("experimental_use_unified_exec_tool")]
    public bool? ExperimentalUseUnifiedExecTool { get; set; }

    [JsonPropertyName("feedback")]
    public TianShuSidecarConfigFeedback? Feedback { get; set; }

    [JsonPropertyName("features")]
    public IReadOnlyDictionary<string, bool>? Features { get; set; }

    [JsonPropertyName("file_opener")]
    public string? FileOpener { get; set; }

    [JsonPropertyName(OpenAiAppCatalogCompatibilityKeys.ForcedChatGptWorkspaceIdConfigKey)]
    public string? ForcedChatGptWorkspaceId { get; set; }

    [JsonPropertyName(OpenAiAppCatalogCompatibilityKeys.ForcedLoginMethodConfigKey)]
    public string? ForcedLoginMethod { get; set; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    [JsonPropertyName("history")]
    public TianShuSidecarConfigHistory? History { get; set; }

    [JsonPropertyName("hide_agent_reasoning")]
    public bool? HideAgentReasoning { get; set; }

    [JsonPropertyName("js_repl_node_module_dirs")]
    public IReadOnlyList<string>? JsReplNodeModuleDirs { get; set; }

    [JsonPropertyName("js_repl_node_path")]
    public string? JsReplNodePath { get; set; }

    [JsonPropertyName("log_dir")]
    public string? LogDir { get; set; }

    [JsonPropertyName("memories")]
    public TianShuSidecarConfigMemories? Memories { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("model_auto_compact_token_limit")]
    public int? ModelAutoCompactTokenLimit { get; set; }

    [JsonPropertyName("model_context_window")]
    public int? ModelContextWindow { get; set; }

    [JsonPropertyName("model_instructions_file")]
    public string? ModelInstructionsFile { get; set; }

    [JsonPropertyName("experimental_instructions_file")]
    public string? ExperimentalInstructionsFile { get; set; }

    [JsonPropertyName("model_route_set_json")]
    public string? ModelCatalogJson { get; set; }

    [JsonPropertyName("provider")]
    public string? ModelProvider { get; set; }

    [JsonPropertyName("providers")]
    public IReadOnlyDictionary<string, TianShuSidecarConfigModelProvider>? ModelProviders { get; set; }

    [JsonPropertyName("model_reasoning_effort")]
    public string? ModelReasoningEffort { get; set; }

    [JsonPropertyName("model_reasoning_summary")]
    public string? ModelReasoningSummary { get; set; }

    [JsonPropertyName("model_verbosity")]
    public string? ModelVerbosity { get; set; }

    [JsonPropertyName("model_supports_reasoning_summaries")]
    public bool? ModelSupportsReasoningSummaries { get; set; }

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("personality")]
    public string? Personality { get; set; }

    [JsonPropertyName("plan_mode_reasoning_effort")]
    public string? PlanModeReasoningEffort { get; set; }

    [JsonPropertyName("plugins")]
    public IReadOnlyDictionary<string, TianShuSidecarConfigPlugin>? Plugins { get; set; }

    [JsonPropertyName("profiles")]
    public IReadOnlyDictionary<string, TianShuSidecarConfigProfile>? Profiles { get; set; }

    [JsonPropertyName("project_doc_fallback_filenames")]
    public IReadOnlyList<string>? ProjectDocFallbackFilenames { get; set; }

    [JsonPropertyName("project_doc_max_bytes")]
    public long? ProjectDocMaxBytes { get; set; }

    [JsonPropertyName("project_root_markers")]
    public IReadOnlyList<string>? ProjectRootMarkers { get; set; }

    [JsonPropertyName("projects")]
    public IReadOnlyDictionary<string, TianShuSidecarConfigProject>? Projects { get; set; }

    [JsonPropertyName("review_model")]
    public string? ReviewModel { get; set; }

    [JsonPropertyName("sandbox_mode")]
    public string? SandboxMode { get; set; }

    [JsonPropertyName("sandbox_workspace_write")]
    public TianShuSidecarConfigSandboxWorkspaceWrite? SandboxWorkspaceWrite { get; set; }

    [JsonPropertyName("shell_environment_policy")]
    public TianShuSidecarConfigShellEnvironmentPolicy? ShellEnvironmentPolicy { get; set; }

    [JsonPropertyName("service_tier")]
    public string? ServiceTier { get; set; }

    [JsonPropertyName("show_raw_agent_reasoning")]
    public bool? ShowRawAgentReasoning { get; set; }

    [JsonPropertyName("skills")]
    public TianShuSidecarConfigSkills? Skills { get; set; }

    [JsonPropertyName("sqlite_home")]
    public string? SqliteHome { get; set; }

    [JsonPropertyName("tools")]
    public TianShuSidecarConfigTools? Tools { get; set; }

    [JsonPropertyName("tool_output_token_limit")]
    public long? ToolOutputTokenLimit { get; set; }

    [JsonPropertyName("tui")]
    public TianShuSidecarConfigTui? Tui { get; set; }

    [JsonPropertyName("agents")]
    public TianShuSidecarConfigAgents? Agents { get; set; }

    [JsonPropertyName("web_search")]
    public string? WebSearch { get; set; }

    [JsonPropertyName("allow_login_shell")]
    public bool? AllowLoginShell { get; set; }

    [JsonPropertyName("permissions")]
    public IReadOnlyDictionary<string, TianShuSidecarConfigPermissionProfile>? Permissions { get; set; }

    [JsonPropertyName("notify")]
    public IReadOnlyList<string>? Notify { get; set; }

    [JsonPropertyName("commit_attribution")]
    public string? CommitAttribution { get; set; }

    [JsonPropertyName("cli_auth_credentials_store")]
    public string? CliAuthCredentialsStore { get; set; }

    [JsonPropertyName("mcp_oauth_credentials_store")]
    public string? McpOauthCredentialsStore { get; set; }

    [JsonPropertyName("mcp_oauth_callback_port")]
    public int? McpOauthCallbackPort { get; set; }

    [JsonPropertyName("mcp_oauth_callback_url")]
    public string? McpOauthCallbackUrl { get; set; }

    [JsonPropertyName("mcp_servers")]
    public IReadOnlyDictionary<string, TianShuSidecarConfigMcpServer>? McpServers { get; set; }

    [JsonPropertyName("apps")]
    public IReadOnlyDictionary<string, TianShuSidecarConfigApp>? Apps { get; set; }

    [JsonPropertyName(OpenAiAppCatalogCompatibilityKeys.ChatGptBaseUrlConfigKey)]
    public string? ChatGptBaseUrl { get; set; }

    [JsonPropertyName("audio")]
    public TianShuSidecarConfigAudio? Audio { get; set; }

    [JsonPropertyName("background_terminal_max_timeout")]
    public long? BackgroundTerminalMaxTimeout { get; set; }

    [JsonPropertyName("check_for_update_on_startup")]
    public bool? CheckForUpdateOnStartup { get; set; }

    [JsonPropertyName("ghost_snapshot")]
    public TianShuSidecarConfigGhostSnapshot? GhostSnapshot { get; set; }

    [JsonPropertyName("notice")]
    public TianShuSidecarConfigNotice? Notice { get; set; }

    [JsonPropertyName("oss_provider")]
    public string? OssProvider { get; set; }

    [JsonPropertyName("otel")]
    public TianShuSidecarConfigOtel? Otel { get; set; }

    [JsonPropertyName("suppress_unstable_features_warning")]
    public bool? SuppressUnstableFeaturesWarning { get; set; }

    [JsonPropertyName("windows")]
    public TianShuSidecarConfigWindows? Windows { get; set; }

    [JsonPropertyName("windows_wsl_setup_acknowledged")]
    public bool? WindowsWslSetupAcknowledged { get; set; }

    [JsonPropertyName("zsh_path")]
    public string? ZshPath { get; set; }
}

internal sealed class TianShuSidecarConfigAnalytics
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
}

internal sealed class TianShuSidecarConfigPlugin
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
}

internal sealed class TianShuSidecarConfigModelProvider
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("base_url")]
    public string? BaseUrl { get; set; }

    [JsonPropertyName("api_key_env")]
    public string? EnvKey { get; set; }

    [JsonPropertyName("api_key_env_instructions")]
    public string? EnvKeyInstructions { get; set; }

    [JsonPropertyName("experimental_bearer_token")]
    public string? ExperimentalBearerToken { get; set; }

    [JsonPropertyName("default_protocol")]
    public string? WireApi { get; set; }

    [JsonPropertyName("query_params")]
    public IReadOnlyDictionary<string, string>? QueryParams { get; set; }

    [JsonPropertyName("http_headers")]
    public IReadOnlyDictionary<string, string>? HttpHeaders { get; set; }

    [JsonPropertyName("env_http_headers")]
    public IReadOnlyDictionary<string, string>? EnvHttpHeaders { get; set; }

    [JsonPropertyName("request_max_retries")]
    public long? RequestMaxRetries { get; set; }

    [JsonPropertyName("stream_max_retries")]
    public long? StreamMaxRetries { get; set; }

    [JsonPropertyName("stream_idle_timeout_ms")]
    public long? StreamIdleTimeoutMs { get; set; }

    [JsonPropertyName(OpenAiAppCatalogCompatibilityKeys.RequiresOpenAiAuthConfigKey)]
    public bool? RequiresOpenAiAuth { get; set; }

    [JsonPropertyName("supports_websockets")]
    public bool? SupportsWebsockets { get; set; }
}

internal sealed class TianShuSidecarConfigFeedback
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
}

internal sealed class TianShuSidecarConfigMemories
{
    [JsonPropertyName("no_memories_if_mcp_or_web_search")]
    public bool? NoMemoriesIfMcpOrWebSearch { get; set; }

    [JsonPropertyName("generate_memories")]
    public bool? GenerateMemories { get; set; }

    [JsonPropertyName("use_memories")]
    public bool? UseMemories { get; set; }

    [JsonPropertyName("max_raw_memories_for_consolidation")]
    public long? MaxRawMemoriesForConsolidation { get; set; }

    [JsonPropertyName("max_unused_days")]
    public long? MaxUnusedDays { get; set; }

    [JsonPropertyName("max_rollout_age_days")]
    public long? MaxRolloutAgeDays { get; set; }

    [JsonPropertyName("max_rollouts_per_startup")]
    public long? MaxRolloutsPerStartup { get; set; }

    [JsonPropertyName("min_rollout_idle_hours")]
    public long? MinRolloutIdleHours { get; set; }

    [JsonPropertyName("extract_model")]
    public string? ExtractModel { get; set; }

    [JsonPropertyName("consolidation_model")]
    public string? ConsolidationModel { get; set; }
}

internal sealed class TianShuSidecarConfigProject
{
    [JsonPropertyName("trust_level")]
    public string? TrustLevel { get; set; }
}

internal sealed class TianShuSidecarConfigSkills
{
    [JsonPropertyName("bundled")]
    public TianShuSidecarConfigBundledSkills? Bundled { get; set; }

    [JsonPropertyName("config")]
    public IReadOnlyList<TianShuSidecarConfigSkillEntry>? Config { get; set; }
}

internal sealed class TianShuSidecarConfigBundledSkills
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
}

internal sealed class TianShuSidecarConfigSkillEntry
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
}

internal sealed class TianShuSidecarConfigHistory
{
    [JsonPropertyName("persistence")]
    public string? Persistence { get; set; }

    [JsonPropertyName("max_bytes")]
    public long? MaxBytes { get; set; }
}

internal sealed class TianShuSidecarConfigShellEnvironmentPolicy
{
    [JsonPropertyName("inherit")]
    public string? Inherit { get; set; }

    [JsonPropertyName("ignore_default_excludes")]
    public bool? IgnoreDefaultExcludes { get; set; }

    [JsonPropertyName("exclude")]
    public IReadOnlyList<string>? Exclude { get; set; }

    [JsonPropertyName("set")]
    public IReadOnlyDictionary<string, string>? Set { get; set; }

    [JsonPropertyName("include_only")]
    public IReadOnlyList<string>? IncludeOnly { get; set; }

    [JsonPropertyName("experimental_use_profile")]
    public bool? ExperimentalUseProfile { get; set; }
}

internal sealed class TianShuSidecarConfigWindows
{
    [JsonPropertyName("sandbox")]
    public string? Sandbox { get; set; }
}

internal sealed class TianShuSidecarConfigPermissionProfile
{
    [JsonPropertyName("filesystem")]
    public IReadOnlyDictionary<string, TianShuSidecarConfigFilesystemPermission>? Filesystem { get; set; }

    [JsonPropertyName("network")]
    public TianShuSidecarConfigPermissionNetwork? Network { get; set; }
}

internal sealed class TianShuSidecarConfigPermissionNetwork
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("proxy_url")]
    public string? ProxyUrl { get; set; }

    [JsonPropertyName("enable_socks5")]
    public bool? EnableSocks5 { get; set; }

    [JsonPropertyName("socks_url")]
    public string? SocksUrl { get; set; }

    [JsonPropertyName("enable_socks5_udp")]
    public bool? EnableSocks5Udp { get; set; }

    [JsonPropertyName("allow_upstream_proxy")]
    public bool? AllowUpstreamProxy { get; set; }

    [JsonPropertyName("dangerously_allow_non_loopback_proxy")]
    public bool? DangerouslyAllowNonLoopbackProxy { get; set; }

    [JsonPropertyName("dangerously_allow_all_unix_sockets")]
    public bool? DangerouslyAllowAllUnixSockets { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("allowed_domains")]
    public IReadOnlyList<string>? AllowedDomains { get; set; }

    [JsonPropertyName("denied_domains")]
    public IReadOnlyList<string>? DeniedDomains { get; set; }

    [JsonPropertyName("allow_unix_sockets")]
    public IReadOnlyList<string>? AllowUnixSockets { get; set; }

    [JsonPropertyName("allow_local_binding")]
    public bool? AllowLocalBinding { get; set; }
}

[JsonConverter(typeof(TianShuSidecarConfigFilesystemPermissionJsonConverter))]
internal sealed class TianShuSidecarConfigFilesystemPermission
{
    private TianShuSidecarConfigFilesystemPermission(
        TianShuSidecarConfigFilesystemPermissionKind kind,
        string? access = null,
        IReadOnlyDictionary<string, string>? scopedEntries = null)
    {
        Kind = kind;
        Access = access;
        ScopedEntries = scopedEntries ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public TianShuSidecarConfigFilesystemPermissionKind Kind { get; }

    public string? Access { get; }

    public IReadOnlyDictionary<string, string> ScopedEntries { get; }

    public static TianShuSidecarConfigFilesystemPermission FromAccess(string value)
        => new(TianShuSidecarConfigFilesystemPermissionKind.Access, access: value);

    public static TianShuSidecarConfigFilesystemPermission FromScoped(IReadOnlyDictionary<string, string>? entries)
        => new(TianShuSidecarConfigFilesystemPermissionKind.Scoped, scopedEntries: entries);
}

internal enum TianShuSidecarConfigFilesystemPermissionKind
{
    Access,
    Scoped,
}

internal sealed class TianShuSidecarConfigMcpServer
{
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("args")]
    public IReadOnlyList<string>? Args { get; set; }

    [JsonPropertyName("env")]
    public IReadOnlyDictionary<string, string>? Env { get; set; }

    [JsonPropertyName("env_vars")]
    public IReadOnlyList<string>? EnvVars { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("http_headers")]
    public IReadOnlyDictionary<string, string>? HttpHeaders { get; set; }

    [JsonPropertyName("env_http_headers")]
    public IReadOnlyDictionary<string, string>? EnvHttpHeaders { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("bearer_token_env_var")]
    public string? BearerTokenEnvVar { get; set; }

    [JsonPropertyName("startup_timeout_sec")]
    public double? StartupTimeoutSec { get; set; }

    [JsonPropertyName("startup_timeout_ms")]
    public long? StartupTimeoutMs { get; set; }

    [JsonPropertyName("tool_timeout_sec")]
    public double? ToolTimeoutSec { get; set; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("required")]
    public bool? Required { get; set; }

    [JsonPropertyName("enabled_tools")]
    public IReadOnlyList<string>? EnabledTools { get; set; }

    [JsonPropertyName("disabled_tools")]
    public IReadOnlyList<string>? DisabledTools { get; set; }

    [JsonPropertyName("scopes")]
    public IReadOnlyList<string>? Scopes { get; set; }

    [JsonPropertyName("oauth_resource")]
    public string? OauthResource { get; set; }
}

[JsonConverter(typeof(TianShuSidecarConfigAgentsJsonConverter))]
internal sealed class TianShuSidecarConfigAgents
{
    public long? MaxThreads { get; set; }

    public int? MaxDepth { get; set; }

    public long? JobMaxRuntimeSeconds { get; set; }

    public IReadOnlyDictionary<string, TianShuSidecarConfigAgentRole> Roles { get; set; } =
        new Dictionary<string, TianShuSidecarConfigAgentRole>(StringComparer.Ordinal);
}

internal sealed class TianShuSidecarConfigAgentRole
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("config_file")]
    public string? ConfigFile { get; set; }

    [JsonPropertyName("nickname_candidates")]
    public IReadOnlyList<string>? NicknameCandidates { get; set; }
}

internal sealed class TianShuSidecarConfigApp
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("destructive_enabled")]
    public bool? DestructiveEnabled { get; set; }

    [JsonPropertyName("open_world_enabled")]
    public bool? OpenWorldEnabled { get; set; }

    [JsonPropertyName("isAccessible")]
    public bool? IsAccessible { get; set; }

    [JsonPropertyName("default_tools_approval_mode")]
    public string? DefaultToolsApprovalMode { get; set; }

    [JsonPropertyName("default_tools_enabled")]
    public bool? DefaultToolsEnabled { get; set; }

    [JsonPropertyName("tools")]
    public IReadOnlyDictionary<string, TianShuSidecarConfigAppTool>? Tools { get; set; }
}

internal sealed class TianShuSidecarConfigAppTool
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("approval_mode")]
    public string? ApprovalMode { get; set; }
}

internal sealed class TianShuSidecarConfigAudio
{
    [JsonPropertyName("microphone")]
    public string? Microphone { get; set; }

    [JsonPropertyName("speaker")]
    public string? Speaker { get; set; }
}

internal sealed class TianShuSidecarConfigGhostSnapshot
{
    [JsonPropertyName("ignore_large_untracked_files")]
    public long? IgnoreLargeUntrackedFiles { get; set; }

    [JsonPropertyName("ignore_large_untracked_dirs")]
    public long? IgnoreLargeUntrackedDirs { get; set; }

    [JsonPropertyName("disable_warnings")]
    public bool? DisableWarnings { get; set; }
}

[JsonConverter(typeof(TianShuSidecarConfigNotificationsJsonConverter))]
internal sealed class TianShuSidecarConfigNotifications
{
    private TianShuSidecarConfigNotifications(
        TianShuSidecarConfigNotificationsKind kind,
        bool? enabled = null,
        IReadOnlyList<string>? custom = null)
    {
        Kind = kind;
        Enabled = enabled;
        Custom = custom ?? Array.Empty<string>();
    }

    public TianShuSidecarConfigNotificationsKind Kind { get; }

    public bool? Enabled { get; }

    public IReadOnlyList<string> Custom { get; }

    public static TianShuSidecarConfigNotifications FromEnabled(bool enabled)
        => new(TianShuSidecarConfigNotificationsKind.Enabled, enabled: enabled);

    public static TianShuSidecarConfigNotifications FromCustom(IReadOnlyList<string>? items)
        => new(TianShuSidecarConfigNotificationsKind.Custom, custom: items);
}

internal enum TianShuSidecarConfigNotificationsKind
{
    Enabled,
    Custom,
}

[JsonConverter(typeof(TianShuSidecarConfigModelAvailabilityNuxJsonConverter))]
internal sealed class TianShuSidecarConfigModelAvailabilityNux
{
    public IReadOnlyDictionary<string, long> ShownCount { get; set; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
}

internal sealed class TianShuSidecarConfigTui
{
    [JsonPropertyName("notifications")]
    public TianShuSidecarConfigNotifications? Notifications { get; set; }

    [JsonPropertyName("notification_method")]
    public string? NotificationMethod { get; set; }

    [JsonPropertyName("animations")]
    public bool? Animations { get; set; }

    [JsonPropertyName("show_tooltips")]
    public bool? ShowTooltips { get; set; }

    [JsonPropertyName("alternate_screen")]
    public string? AlternateScreen { get; set; }

    [JsonPropertyName("status_line")]
    public IReadOnlyList<string>? StatusLine { get; set; }

    [JsonPropertyName("theme")]
    public string? Theme { get; set; }

    [JsonPropertyName("model_availability_nux")]
    public TianShuSidecarConfigModelAvailabilityNux? ModelAvailabilityNux { get; set; }
}

internal sealed class TianShuSidecarConfigNotice
{
    [JsonPropertyName("hide_full_access_warning")]
    public bool? HideFullAccessWarning { get; set; }

    [JsonPropertyName("hide_world_writable_warning")]
    public bool? HideWorldWritableWarning { get; set; }

    [JsonPropertyName("hide_rate_limit_model_nudge")]
    public bool? HideRateLimitModelNudge { get; set; }

    [JsonPropertyName("hide_gpt5_1_migration_prompt")]
    public bool? HideGpt5_1MigrationPrompt { get; set; }

    [JsonPropertyName("hide_gpt-5.1-codex-max_migration_prompt")]
    public bool? HideGpt51CodexMaxMigrationPrompt { get; set; }

    [JsonPropertyName("model_migrations")]
    public IReadOnlyDictionary<string, string>? ModelMigrations { get; set; }
}

[JsonConverter(typeof(TianShuSidecarConfigOtelExporterJsonConverter))]
internal sealed class TianShuSidecarConfigOtelExporter
{
    private TianShuSidecarConfigOtelExporter(
        string kind,
        string? endpoint = null,
        IReadOnlyDictionary<string, string>? headers = null,
        string? protocol = null,
        TianShuSidecarConfigOtelTls? tls = null)
    {
        Kind = kind;
        Endpoint = endpoint;
        Headers = headers ?? new Dictionary<string, string>(StringComparer.Ordinal);
        Protocol = protocol;
        Tls = tls;
    }

    public string Kind { get; }

    public string? Endpoint { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public string? Protocol { get; }

    public TianShuSidecarConfigOtelTls? Tls { get; }

    public static TianShuSidecarConfigOtelExporter FromNamed(string kind)
        => new(kind);

    public static TianShuSidecarConfigOtelExporter FromOtlpHttp(
        string endpoint,
        IReadOnlyDictionary<string, string>? headers,
        string? protocol,
        TianShuSidecarConfigOtelTls? tls)
        => new("otlp-http", endpoint, headers, protocol, tls);

    public static TianShuSidecarConfigOtelExporter FromOtlpGrpc(
        string endpoint,
        IReadOnlyDictionary<string, string>? headers,
        TianShuSidecarConfigOtelTls? tls)
        => new("otlp-grpc", endpoint, headers, null, tls);
}

internal sealed class TianShuSidecarConfigOtelTls
{
    [JsonPropertyName("ca_certificate")]
    public string? CaCertificate { get; set; }

    [JsonPropertyName("client_certificate")]
    public string? ClientCertificate { get; set; }

    [JsonPropertyName("client_private_key")]
    public string? ClientPrivateKey { get; set; }
}

internal sealed class TianShuSidecarConfigOtel
{
    [JsonPropertyName("log_user_prompt")]
    public bool? LogUserPrompt { get; set; }

    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    [JsonPropertyName("exporter")]
    public TianShuSidecarConfigOtelExporter? Exporter { get; set; }

    [JsonPropertyName("trace_exporter")]
    public TianShuSidecarConfigOtelExporter? TraceExporter { get; set; }

    [JsonPropertyName("metrics_exporter")]
    public TianShuSidecarConfigOtelExporter? MetricsExporter { get; set; }
}

internal sealed class TianShuSidecarConfigProfile
{
    [JsonPropertyName("approval_policy")]
    public string? ApprovalPolicy { get; set; }

    [JsonPropertyName("analytics")]
    public TianShuSidecarConfigAnalytics? Analytics { get; set; }

    [JsonPropertyName(OpenAiAppCatalogCompatibilityKeys.ChatGptBaseUrlConfigKey)]
    public string? ChatGptBaseUrl { get; set; }

    [JsonPropertyName("experimental_compact_prompt_file")]
    public string? ExperimentalCompactPromptFile { get; set; }

    [JsonPropertyName("experimental_use_freeform_apply_patch")]
    public bool? ExperimentalUseFreeformApplyPatch { get; set; }

    [JsonPropertyName("experimental_use_unified_exec_tool")]
    public bool? ExperimentalUseUnifiedExecTool { get; set; }

    [JsonPropertyName("features")]
    public IReadOnlyDictionary<string, bool>? Features { get; set; }

    [JsonPropertyName("include_apply_patch_tool")]
    public bool? IncludeApplyPatchTool { get; set; }

    [JsonPropertyName("js_repl_node_module_dirs")]
    public IReadOnlyList<string>? JsReplNodeModuleDirs { get; set; }

    [JsonPropertyName("js_repl_node_path")]
    public string? JsReplNodePath { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("model_route_set_json")]
    public string? ModelCatalogJson { get; set; }

    [JsonPropertyName("provider")]
    public string? ModelProvider { get; set; }

    [JsonPropertyName("model_instructions_file")]
    public string? ModelInstructionsFile { get; set; }

    [JsonPropertyName("experimental_instructions_file")]
    public string? ExperimentalInstructionsFile { get; set; }

    [JsonPropertyName("model_reasoning_effort")]
    public string? ModelReasoningEffort { get; set; }

    [JsonPropertyName("model_reasoning_summary")]
    public string? ModelReasoningSummary { get; set; }

    [JsonPropertyName("model_verbosity")]
    public string? ModelVerbosity { get; set; }

    [JsonPropertyName("oss_provider")]
    public string? OssProvider { get; set; }

    [JsonPropertyName("personality")]
    public string? Personality { get; set; }

    [JsonPropertyName("plan_mode_reasoning_effort")]
    public string? PlanModeReasoningEffort { get; set; }

    [JsonPropertyName("sandbox_mode")]
    public string? SandboxMode { get; set; }

    [JsonPropertyName("service_tier")]
    public string? ServiceTier { get; set; }

    [JsonPropertyName("tools")]
    public TianShuSidecarConfigTools? Tools { get; set; }

    [JsonPropertyName("tools_view_image")]
    public bool? ToolsViewImage { get; set; }

    [JsonPropertyName("web_search")]
    public string? WebSearch { get; set; }

    [JsonPropertyName("windows")]
    public TianShuSidecarConfigWindows? Windows { get; set; }

    [JsonPropertyName("zsh_path")]
    public string? ZshPath { get; set; }
}

internal sealed class TianShuSidecarConfigSandboxWorkspaceWrite
{
    [JsonPropertyName("exclude_slash_tmp")]
    public bool? ExcludeSlashTmp { get; set; }

    [JsonPropertyName("exclude_tmpdir_env_var")]
    public bool? ExcludeTmpdirEnvVar { get; set; }

    [JsonPropertyName("network_access")]
    public bool? NetworkAccess { get; set; }

    [JsonPropertyName("writable_roots")]
    public IReadOnlyList<string>? WritableRoots { get; set; }
}

internal sealed class TianShuSidecarConfigTools
{
    [JsonPropertyName("view_image")]
    public bool? ViewImage { get; set; }

    [JsonPropertyName("web_search")]
    public TianShuSidecarConfigWebSearchTool? WebSearch { get; set; }
}

internal sealed class TianShuSidecarConfigWebSearchTool
{
    [JsonPropertyName("allowed_domains")]
    public IReadOnlyList<string>? AllowedDomains { get; set; }

    [JsonPropertyName("context_size")]
    public string? ContextSize { get; set; }

    [JsonPropertyName("location")]
    public TianShuSidecarConfigWebSearchLocation? Location { get; set; }
}

internal sealed class TianShuSidecarConfigWebSearchLocation
{
    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }
}

internal sealed class TianShuSidecarConfigOrigin
{
    [JsonPropertyName("name")]
    public TianShuSidecarConfigOriginName? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

internal sealed class TianShuSidecarConfigOriginName
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("dotTianShuFolder")]
    public string? DotTianShuFolder { get; set; }
}

internal sealed class TianShuSidecarConfigFilesystemPermissionJsonConverter
    : JsonConverter<TianShuSidecarConfigFilesystemPermission>
{
    public override TianShuSidecarConfigFilesystemPermission? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.String => TianShuSidecarConfigFilesystemPermission.FromAccess(
                document.RootElement.GetString() ?? string.Empty),
            JsonValueKind.Object => TianShuSidecarConfigFilesystemPermission.FromScoped(
                document.RootElement.EnumerateObject().ToDictionary(
                    static property => property.Name,
                    static property => property.Value.GetString() ?? property.Value.GetRawText(),
                    StringComparer.Ordinal)),
            _ => throw new JsonException("filesystem permission 必须是字符串或对象。"),
        };
    }

    public override void Write(Utf8JsonWriter writer, TianShuSidecarConfigFilesystemPermission value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case TianShuSidecarConfigFilesystemPermissionKind.Access:
                writer.WriteStringValue(value.Access);
                break;
            case TianShuSidecarConfigFilesystemPermissionKind.Scoped:
                writer.WriteStartObject();
                foreach (var pair in value.ScopedEntries)
                {
                    writer.WriteString(pair.Key, pair.Value);
                }

                writer.WriteEndObject();
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}

internal sealed class TianShuSidecarConfigNotificationsJsonConverter
    : JsonConverter<TianShuSidecarConfigNotifications>
{
    public override TianShuSidecarConfigNotifications? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.True => TianShuSidecarConfigNotifications.FromEnabled(true),
            JsonValueKind.False => TianShuSidecarConfigNotifications.FromEnabled(false),
            JsonValueKind.Array => TianShuSidecarConfigNotifications.FromCustom(
                document.RootElement.EnumerateArray().Select(static item => item.GetString() ?? string.Empty).ToArray()),
            _ => throw new JsonException("notifications 必须是布尔值或字符串数组。"),
        };
    }

    public override void Write(Utf8JsonWriter writer, TianShuSidecarConfigNotifications value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case TianShuSidecarConfigNotificationsKind.Enabled:
                writer.WriteBooleanValue(value.Enabled ?? false);
                break;
            case TianShuSidecarConfigNotificationsKind.Custom:
                writer.WriteStartArray();
                foreach (var item in value.Custom)
                {
                    writer.WriteStringValue(item);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}

internal sealed class TianShuSidecarConfigModelAvailabilityNuxJsonConverter
    : JsonConverter<TianShuSidecarConfigModelAvailabilityNux>
{
    public override TianShuSidecarConfigModelAvailabilityNux? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("model_availability_nux 必须是对象。");
        }

        var shownCount = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            shownCount[property.Name] = property.Value.GetInt64();
        }

        return new TianShuSidecarConfigModelAvailabilityNux
        {
            ShownCount = shownCount,
        };
    }

    public override void Write(Utf8JsonWriter writer, TianShuSidecarConfigModelAvailabilityNux value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var pair in value.ShownCount)
        {
            writer.WriteNumber(pair.Key, pair.Value);
        }

        writer.WriteEndObject();
    }
}

internal sealed class TianShuSidecarConfigAgentsJsonConverter
    : JsonConverter<TianShuSidecarConfigAgents>
{
    public override TianShuSidecarConfigAgents? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("agents 必须是对象。");
        }

        long? maxThreads = null;
        int? maxDepth = null;
        long? jobMaxRuntimeSeconds = null;
        var roles = new Dictionary<string, TianShuSidecarConfigAgentRole>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            switch (property.Name)
            {
                case "max_threads":
                    maxThreads = property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetInt64();
                    break;
                case "max_depth":
                    maxDepth = property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetInt32();
                    break;
                case "job_max_runtime_seconds":
                    jobMaxRuntimeSeconds = property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetInt64();
                    break;
                default:
                    roles[property.Name] = property.Value.Deserialize<TianShuSidecarConfigAgentRole>(options)
                        ?? new TianShuSidecarConfigAgentRole();
                    break;
            }
        }

        return new TianShuSidecarConfigAgents
        {
            MaxThreads = maxThreads,
            MaxDepth = maxDepth,
            JobMaxRuntimeSeconds = jobMaxRuntimeSeconds,
            Roles = roles,
        };
    }

    public override void Write(Utf8JsonWriter writer, TianShuSidecarConfigAgents value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.MaxThreads.HasValue)
        {
            writer.WriteNumber("max_threads", value.MaxThreads.Value);
        }

        if (value.MaxDepth.HasValue)
        {
            writer.WriteNumber("max_depth", value.MaxDepth.Value);
        }

        if (value.JobMaxRuntimeSeconds.HasValue)
        {
            writer.WriteNumber("job_max_runtime_seconds", value.JobMaxRuntimeSeconds.Value);
        }

        foreach (var pair in value.Roles)
        {
            writer.WritePropertyName(pair.Key);
            JsonSerializer.Serialize(writer, pair.Value, options);
        }

        writer.WriteEndObject();
    }
}

internal sealed class TianShuSidecarConfigOtelExporterJsonConverter
    : JsonConverter<TianShuSidecarConfigOtelExporter>
{
    public override TianShuSidecarConfigOtelExporter? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.String => TianShuSidecarConfigOtelExporter.FromNamed(
                document.RootElement.GetString() ?? string.Empty),
            JsonValueKind.Object => ReadOtelExporterObject(document.RootElement, options),
            _ => throw new JsonException("otel exporter 必须是字符串或对象。"),
        };
    }

    public override void Write(Utf8JsonWriter writer, TianShuSidecarConfigOtelExporter value, JsonSerializerOptions options)
    {
        if (string.Equals(value.Kind, "none", StringComparison.Ordinal) ||
            string.Equals(value.Kind, "statsig", StringComparison.Ordinal))
        {
            writer.WriteStringValue(value.Kind);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName(value.Kind);
        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(value.Endpoint))
        {
            writer.WriteString("endpoint", value.Endpoint);
        }

        if (value.Headers.Count > 0)
        {
            writer.WritePropertyName("headers");
            JsonSerializer.Serialize(writer, value.Headers, options);
        }

        if (!string.IsNullOrWhiteSpace(value.Protocol))
        {
            writer.WriteString("protocol", value.Protocol);
        }

        if (value.Tls is not null)
        {
            writer.WritePropertyName("tls");
            JsonSerializer.Serialize(writer, value.Tls, options);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static TianShuSidecarConfigOtelExporter ReadOtelExporterObject(JsonElement element, JsonSerializerOptions options)
    {
        var property = element.EnumerateObject().SingleOrDefault();
        if (property.Equals(default(JsonProperty)))
        {
            throw new JsonException("otel exporter 对象缺少变体名称。");
        }

        var payload = property.Value;
        var endpoint = payload.TryGetProperty("endpoint", out var endpointElement)
            ? endpointElement.GetString()
            : null;
        var headers = payload.TryGetProperty("headers", out var headersElement)
            ? headersElement.Deserialize<Dictionary<string, string>>(options)
            : null;
        var tls = payload.TryGetProperty("tls", out var tlsElement)
            ? tlsElement.Deserialize<TianShuSidecarConfigOtelTls>(options)
            : null;

        return property.Name switch
        {
            "otlp-http" => TianShuSidecarConfigOtelExporter.FromOtlpHttp(
                endpoint ?? string.Empty,
                headers,
                payload.TryGetProperty("protocol", out var protocolElement) ? protocolElement.GetString() : null,
                tls),
            "otlp-grpc" => TianShuSidecarConfigOtelExporter.FromOtlpGrpc(endpoint ?? string.Empty, headers, tls),
            _ => throw new JsonException($"未知的 otel exporter：{property.Name}"),
        };
    }
}

[JsonConverter(typeof(TianShuSidecarStructuredValueJsonConverter))]
internal sealed class TianShuSidecarStructuredValue
{
    private TianShuSidecarStructuredValue(
        TianShuSidecarStructuredValueKind kind,
        string? stringValue = null,
        string? numberValue = null,
        bool? booleanValue = null,
        IReadOnlyDictionary<string, TianShuSidecarStructuredValue>? properties = null,
        IReadOnlyList<TianShuSidecarStructuredValue>? items = null)
    {
        Kind = kind;
        StringValue = stringValue;
        NumberValue = numberValue;
        BooleanValue = booleanValue;
        Properties = properties ?? new Dictionary<string, TianShuSidecarStructuredValue>(StringComparer.Ordinal);
        Items = items ?? Array.Empty<TianShuSidecarStructuredValue>();
    }

    public TianShuSidecarStructuredValueKind Kind { get; }

    public string? StringValue { get; }

    public string? NumberValue { get; }

    public bool? BooleanValue { get; }

    public IReadOnlyDictionary<string, TianShuSidecarStructuredValue> Properties { get; }

    public IReadOnlyList<TianShuSidecarStructuredValue> Items { get; }

    public static TianShuSidecarStructuredValue Null { get; } = new(TianShuSidecarStructuredValueKind.Null);

    public static TianShuSidecarStructuredValue FromString(string value)
        => new(TianShuSidecarStructuredValueKind.String, stringValue: value);

    public static TianShuSidecarStructuredValue FromNumber(string value)
        => new(TianShuSidecarStructuredValueKind.Number, numberValue: value);

    public static TianShuSidecarStructuredValue FromBoolean(bool value)
        => new(TianShuSidecarStructuredValueKind.Boolean, booleanValue: value);

    public static TianShuSidecarStructuredValue FromObject(IReadOnlyDictionary<string, TianShuSidecarStructuredValue>? properties)
        => new(TianShuSidecarStructuredValueKind.Object, properties: properties);

    public static TianShuSidecarStructuredValue FromArray(IReadOnlyList<TianShuSidecarStructuredValue>? items)
        => new(TianShuSidecarStructuredValueKind.Array, items: items);

    public static TianShuSidecarStructuredValue FromJsonElement(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => FromObject(
                element.EnumerateObject()
                    .ToDictionary(
                        static property => property.Name,
                        static property => FromJsonElement(property.Value),
                        StringComparer.Ordinal)),
            JsonValueKind.Array => FromArray(element.EnumerateArray().Select(FromJsonElement).ToArray()),
            JsonValueKind.String => FromString(element.GetString() ?? string.Empty),
            JsonValueKind.Number => FromNumber(element.GetRawText()),
            JsonValueKind.True => FromBoolean(true),
            JsonValueKind.False => FromBoolean(false),
            JsonValueKind.Null => Null,
            _ => FromString(element.GetRawText()),
        };
}

internal enum TianShuSidecarStructuredValueKind
{
    Null,
    Object,
    Array,
    String,
    Number,
    Boolean,
}

internal sealed class TianShuSidecarStructuredValueJsonConverter : JsonConverter<TianShuSidecarStructuredValue>
{
    public override TianShuSidecarStructuredValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return TianShuSidecarStructuredValue.FromJsonElement(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, TianShuSidecarStructuredValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case TianShuSidecarStructuredValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.Properties)
                {
                    writer.WritePropertyName(property.Key);
                    Write(writer, property.Value, options);
                }
                writer.WriteEndObject();
                break;
            case TianShuSidecarStructuredValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.Items)
                {
                    Write(writer, item, options);
                }
                writer.WriteEndArray();
                break;
            case TianShuSidecarStructuredValueKind.String:
                writer.WriteStringValue(value.StringValue);
                break;
            case TianShuSidecarStructuredValueKind.Number:
                writer.WriteRawValue(value.NumberValue ?? "null");
                break;
            case TianShuSidecarStructuredValueKind.Boolean:
                writer.WriteBooleanValue(value.BooleanValue ?? false);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}

/// <summary>
/// VSIX 本地线程 history 项，仅用于 sidecar resume 请求载荷。
/// Local VSIX thread history item used for sidecar resume payloads.
/// </summary>
internal sealed class TianShuSidecarThreadHistoryItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public IReadOnlyList<TianShuSidecarStructuredValue>? Content { get; set; }

    [JsonPropertyName("end_turn")]
    public bool? EndTurn { get; set; }

    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    [JsonPropertyName("summary")]
    public IReadOnlyList<TianShuSidecarStructuredValue>? Summary { get; set; }

    [JsonPropertyName("encrypted_content")]
    public string? EncryptedContent { get; set; }

    [JsonPropertyName("call_id")]
    public string? CallId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("action")]
    public TianShuSidecarStructuredValue? Action { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("arguments")]
    public TianShuSidecarStructuredValue? Arguments { get; set; }

    [JsonPropertyName("execution")]
    public string? Execution { get; set; }

    [JsonPropertyName("output")]
    public TianShuSidecarStructuredValue? Output { get; set; }

    [JsonPropertyName("input")]
    public string? Input { get; set; }

    [JsonPropertyName("tools")]
    public IReadOnlyList<TianShuSidecarStructuredValue>? Tools { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("revised_prompt")]
    public string? RevisedPrompt { get; set; }

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("ghost_commit")]
    public TianShuSidecarStructuredValue? GhostCommit { get; set; }
}

internal sealed class TianShuSidecarConfigField
{
    public string KeyPath { get; set; } = string.Empty;

    public string ValueKind { get; set; } = string.Empty;

    public string ValueText { get; set; } = string.Empty;

    public string ValueJson { get; set; } = string.Empty;

    public string SourceType { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string SourceText { get; set; } = "来源未知";
}

internal sealed class TianShuSidecarConfigLayer
{
    public string NameJson { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string ConfigJson { get; set; } = "{}";

    public string? DisabledReason { get; set; }
}

internal sealed class TianShuSidecarConfigRequirementsReadResult
{
    public string Message { get; set; } = string.Empty;

    public bool IsDefined { get; set; }

    public IReadOnlyList<string> AllowedApprovalPolicies { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> AllowedSandboxModes { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> AllowedWebSearchModes { get; set; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, bool> FeatureRequirements { get; set; } = new Dictionary<string, bool>();

    public string? EnforceResidency { get; set; }

    public TianShuSidecarConfigRequirementsNetwork? Network { get; set; }
}

internal sealed class TianShuSidecarConfigRequirementsNetwork
{
    public bool? Enabled { get; set; }

    public ushort? HttpPort { get; set; }

    public ushort? SocksPort { get; set; }

    public bool? AllowUpstreamProxy { get; set; }

    public bool? DangerouslyAllowNonLoopbackProxy { get; set; }

    public bool? DangerouslyAllowNonLoopbackAdmin { get; set; }

    public bool? DangerouslyAllowAllUnixSockets { get; set; }

    public IReadOnlyList<string> AllowedDomains { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> DeniedDomains { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> AllowUnixSockets { get; set; } = Array.Empty<string>();

    public bool? AllowLocalBinding { get; set; }
}

internal sealed class TianShuSidecarModelCatalogResult
{
    public string Message { get; set; } = string.Empty;

    public string? NextCursor { get; set; }

    public IReadOnlyList<TianShuSidecarModelCatalogItem> Items { get; set; } = Array.Empty<TianShuSidecarModelCatalogItem>();
}

internal sealed class TianShuSidecarModelCatalogItem
{
    public string Id { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string DefaultReasoningEffort { get; set; } = "medium";

    public IReadOnlyList<string> SupportedReasoningEfforts { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> InputModalities { get; set; } = Array.Empty<string>();

    public bool SupportsPersonality { get; set; }

    public string Description { get; set; } = string.Empty;
}

internal sealed class TianShuSidecarConfigValueWriteRequest
{
    public string KeyPath { get; set; } = string.Empty;

    public TianShuSidecarStructuredValue? Value { get; set; }

    public string MergeStrategy { get; set; } = "replace";

    public string? WorkingDirectory { get; set; }

    public string? FilePath { get; set; }

    public string? ExpectedVersion { get; set; }

    public bool ReloadUserConfig { get; set; }
}

internal sealed class TianShuSidecarConfigBatchWriteRequest
{
    public IReadOnlyList<TianShuSidecarConfigWriteItem> Items { get; set; } = Array.Empty<TianShuSidecarConfigWriteItem>();

    public string MergeStrategy { get; set; } = "replace";

    public string? WorkingDirectory { get; set; }

    public string? FilePath { get; set; }

    public string? ExpectedVersion { get; set; }

    public bool ReloadUserConfig { get; set; }
}

internal sealed class TianShuSidecarConfigWriteItem
{
    public string KeyPath { get; set; } = string.Empty;

    public TianShuSidecarStructuredValue? Value { get; set; }

    public string MergeStrategy { get; set; } = "replace";
}

internal sealed class TianShuSidecarConfigWriteResult
{
    public string Message { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public bool IsOverridden { get; set; }

    public TianShuSidecarConfigWriteOverriddenMetadata? OverriddenMetadata { get; set; }
}

internal sealed class TianShuSidecarConfigWriteOverriddenMetadata
{
    public string Message { get; set; } = string.Empty;

    public string? OverridingLayerType { get; set; }

    public string? OverridingLayerFile { get; set; }

    public string? OverridingLayerDotTianShuFolder { get; set; }

    public string? OverridingLayerVersion { get; set; }

    public TianShuSidecarStructuredValue? EffectiveValue { get; set; }
}

internal sealed class TianShuSidecarExperimentalFeatureListResult
{
    public string Message { get; set; } = string.Empty;

    public string? NextCursor { get; set; }

    public IReadOnlyList<TianShuSidecarExperimentalFeatureItem> Items { get; set; } = Array.Empty<TianShuSidecarExperimentalFeatureItem>();
}

internal sealed class TianShuSidecarExperimentalFeatureItem
{
    public string Name { get; set; } = string.Empty;

    public string Stage { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public string? Announcement { get; set; }

    public bool Enabled { get; set; }

    public bool DefaultEnabled { get; set; }
}

internal sealed class TianShuSidecarCollaborationModeListResult
{
    public string Message { get; set; } = string.Empty;

    public IReadOnlyList<TianShuSidecarCollaborationModeItem> Items { get; set; } = Array.Empty<TianShuSidecarCollaborationModeItem>();
}

internal sealed class TianShuSidecarCollaborationModeItem
{
    public string Name { get; set; } = string.Empty;

    public string? Mode { get; set; }

    public string? Model { get; set; }

    public string? ReasoningEffort { get; set; }
}

internal sealed class TianShuSidecarMcpServerStatusListResult
{
    public string Message { get; set; } = string.Empty;

    public string? NextCursor { get; set; }

    public IReadOnlyList<TianShuSidecarMcpServerStatusItem> Items { get; set; } = Array.Empty<TianShuSidecarMcpServerStatusItem>();
}

internal sealed class TianShuSidecarMcpServerStatusItem
{
    public string Name { get; set; } = string.Empty;

    public string AuthStatus { get; set; } = string.Empty;

    public IReadOnlyList<string> ToolNames { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> ResourceUris { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> ResourceTemplateUris { get; set; } = Array.Empty<string>();
}

internal sealed class TianShuSidecarSkillsListResult
{
    public string Message { get; set; } = string.Empty;

    public IReadOnlyList<TianShuSidecarSkillsListEntry> Entries { get; set; } = Array.Empty<TianShuSidecarSkillsListEntry>();
}

internal sealed class TianShuSidecarSkillsListEntry
{
    public string Cwd { get; set; } = string.Empty;

    public IReadOnlyList<TianShuSidecarSkillMetadata> Skills { get; set; } = Array.Empty<TianShuSidecarSkillMetadata>();

    public IReadOnlyList<TianShuSidecarSkillErrorInfo> Errors { get; set; } = Array.Empty<TianShuSidecarSkillErrorInfo>();
}

internal sealed class TianShuSidecarSkillMetadata
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? ShortDescription { get; set; }

    public string PathToSkillsMd { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}

internal sealed class TianShuSidecarSkillErrorInfo
{
    public string Path { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

internal sealed class TianShuSidecarSkillsRemoteListResult
{
    public string Message { get; set; } = string.Empty;

    public string? NextCursor { get; set; }

    public IReadOnlyList<TianShuSidecarRemoteSkillSummary> Items { get; set; } = Array.Empty<TianShuSidecarRemoteSkillSummary>();
}

internal sealed class TianShuSidecarRemoteSkillSummary
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? HazelnutScope { get; set; }
}

internal sealed class TianShuSidecarSkillsRemoteExportResult
{
    public string Message { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}

internal sealed class TianShuSidecarPluginListResult
{
    public string Message { get; set; } = string.Empty;

    public IReadOnlyList<TianShuSidecarPluginMarketplaceEntry> Marketplaces { get; set; } = Array.Empty<TianShuSidecarPluginMarketplaceEntry>();

    public string? RemoteSyncError { get; set; }
}

internal sealed class TianShuSidecarPluginMarketplaceEntry
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public IReadOnlyList<TianShuSidecarPluginSummary> Plugins { get; set; } = Array.Empty<TianShuSidecarPluginSummary>();
}

internal sealed class TianShuSidecarPluginSummary
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Installed { get; set; }

    public bool Enabled { get; set; }

    public string InstallPolicy { get; set; } = string.Empty;

    public string AuthPolicy { get; set; } = string.Empty;
}

internal sealed class TianShuSidecarPluginReadResult
{
    public string Message { get; set; } = string.Empty;

    public TianShuSidecarPluginDetail? Plugin { get; set; }
}

internal sealed class TianShuSidecarPluginDetail
{
    public string MarketplaceName { get; set; } = string.Empty;

    public string MarketplacePath { get; set; } = string.Empty;

    public TianShuSidecarPluginSummary Summary { get; set; } = new();

    public string? Description { get; set; }

    public IReadOnlyList<TianShuSidecarPluginSkillSummary> Skills { get; set; } = Array.Empty<TianShuSidecarPluginSkillSummary>();

    public IReadOnlyList<TianShuSidecarPluginAppSummary> Apps { get; set; } = Array.Empty<TianShuSidecarPluginAppSummary>();

    public IReadOnlyList<string> McpServers { get; set; } = Array.Empty<string>();
}

internal sealed class TianShuSidecarPluginSkillSummary
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? ShortDescription { get; set; }

    public string Path { get; set; } = string.Empty;
}

internal sealed class TianShuSidecarPluginAppSummary
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? InstallUrl { get; set; }
}

internal sealed class TianShuSidecarPluginInstallResult
{
    public string Message { get; set; } = string.Empty;

    public string AuthPolicy { get; set; } = string.Empty;

    public IReadOnlyList<TianShuSidecarPluginAppSummary> AppsNeedingAuth { get; set; } = Array.Empty<TianShuSidecarPluginAppSummary>();
}

internal sealed class TianShuSidecarAppListResult
{
    public string Message { get; set; } = string.Empty;

    public string? NextCursor { get; set; }

    public IReadOnlyList<TianShuSidecarAppInfo> Items { get; set; } = Array.Empty<TianShuSidecarAppInfo>();
}

internal sealed class TianShuSidecarAppInfo
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? InstallUrl { get; set; }

    public string? DistributionChannel { get; set; }

    public bool IsAccessible { get; set; }

    public bool IsEnabled { get; set; } = true;

    public IReadOnlyList<string> PluginDisplayNames { get; set; } = Array.Empty<string>();
}

internal sealed class TianShuSidecarReviewStartResult
{
    public string Message { get; set; } = string.Empty;

    public string ReviewThreadId { get; set; } = string.Empty;

    public TianShuSidecarReviewTurn? Turn { get; set; }
}

internal sealed class TianShuSidecarReviewTurn
{
    public string Id { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? DisplayText { get; set; }
}

internal sealed class TianShuSidecarMcpServerOauthLoginStartResult
{
    public string Message { get; set; } = string.Empty;

    public string? AuthorizationUrl { get; set; }
}

internal sealed class TianShuSidecarConversationSummaryResult
{
    public string Message { get; set; } = string.Empty;

    public TianShuSidecarConversationSummary? Summary { get; set; }
}

internal sealed class TianShuSidecarConversationSummary
{
    public string ConversationId { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Preview { get; set; } = string.Empty;

    public string? Timestamp { get; set; }

    public string? UpdatedAt { get; set; }

    public string ModelProvider { get; set; } = string.Empty;

    public string Cwd { get; set; } = string.Empty;

    public string CliVersion { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public TianShuSidecarGitInfo? GitInfo { get; set; }
}

internal sealed class TianShuSidecarGitDiffToRemoteResult
{
    public string Message { get; set; } = string.Empty;

    public bool HasChanges { get; set; }

    public string Diff { get; set; } = string.Empty;
}

internal sealed class TianShuSidecarPlanPayload
{
    public string? Explanation { get; set; }

    public TianShuSidecarPlanStepPayload[] Steps { get; set; } = [];
}

internal sealed class TianShuSidecarPlanStepPayload
{
    public int Sequence { get; set; }

    public string Step { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
}

internal sealed class TianShuSidecarToolCallPayload
{
    public string? ItemId { get; set; }

    public string? CallId { get; set; }

    public string? ToolName { get; set; }

    public string? ServerName { get; set; }

    public string? InputText { get; set; }

    public string? OutputText { get; set; }

    public string? Status { get; set; }

    public string? Phase { get; set; }

    public bool? RequiresApproval { get; set; }
}

internal sealed class TianShuSidecarApprovalRequestPayload
{
    public string? ToolName { get; set; }

    public string? ApprovalKind { get; set; }

    public string[]? AvailableDecisions { get; set; }

    public TianShuSidecarApprovalDecisionOptionPayload[]? AvailableDecisionOptions { get; set; }

    public string? Summary { get; set; }

    public TianShuSidecarApprovalFieldPayload[] MetadataFields { get; set; } = [];
}

public sealed class TianShuSidecarApprovalDecisionOptionPayload
{
    public TianShuApprovalDecision Decision { get; set; }

    public TianShuSidecarExecPolicyAmendmentPayload? ExecPolicyAmendment { get; set; }

    public TianShuSidecarNetworkPolicyAmendmentPayload? NetworkPolicyAmendment { get; set; }

    public bool IsApproved()
    {
        if (Decision == TianShuApprovalDecision.ApplyNetworkPolicyAmendment)
        {
            return !string.Equals(NetworkPolicyAmendment?.Action, "deny", StringComparison.OrdinalIgnoreCase);
        }

        return Decision.IsApproved();
    }
}

public sealed class TianShuSidecarExecPolicyAmendmentPayload
{
    public string[] CommandPrefix { get; set; } = [];
}

public sealed class TianShuSidecarNetworkPolicyAmendmentPayload
{
    public string? Host { get; set; }

    public string? Action { get; set; }
}

internal sealed class TianShuSidecarApprovalFieldPayload
{
    public string Key { get; set; } = string.Empty;

    public string ValueType { get; set; } = string.Empty;

    public string ValueText { get; set; } = string.Empty;
}

internal sealed class TianShuSidecarApprovalRequestDto
{
    public string? ToolName { get; set; }

    public string? ApprovalKind { get; set; }

    public string[]? AvailableDecisions { get; set; }

    public TianShuSidecarApprovalDecisionOptionDto[]? AvailableDecisionOptions { get; set; }

    public string? Summary { get; set; }

    public TianShuSidecarApprovalFieldPayload[]? MetadataFields { get; set; }
}

internal sealed class TianShuSidecarApprovalDecisionOptionDto
{
    public string? Type { get; set; }

    public string? Decision { get; set; }

    public TianShuSidecarExecPolicyAmendmentPayload? ExecPolicyAmendment { get; set; }

    public TianShuSidecarNetworkPolicyAmendmentPayload? NetworkPolicyAmendment { get; set; }
}

internal sealed class TianShuSidecarPermissionRequestPayload
{
    public string? Reason { get; set; }

    public TianShuSidecarPermissionFieldPayload[] Fields { get; set; } = [];

    public string? PermissionsJson { get; set; }

    public string? Summary { get; set; }
}

internal sealed class TianShuSidecarPermissionFieldPayload
{
    public string Key { get; set; } = string.Empty;

    public string ValueType { get; set; } = string.Empty;

    public string ValueText { get; set; } = string.Empty;
}

internal sealed class TianShuSidecarUserInputRequestPayload
{
    public TianShuSidecarUserInputQuestionPayload[] Questions { get; set; } = [];

    public string? Summary { get; set; }
}

internal sealed class TianShuSidecarServerRequestResolvedPayload
{
    public long RequestId { get; set; }

    public string? RequestIdRaw { get; set; }

    public string? RequestKind { get; set; }

    public string? CallId { get; set; }
}

internal sealed class TianShuSidecarUserInputQuestionPayload
{
    public string Id { get; set; } = string.Empty;

    public string Header { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public bool IsSecret { get; set; }

    public bool IsOther { get; set; }

    public TianShuSidecarUserInputOptionPayload[]? Options { get; set; }
}

internal sealed class TianShuSidecarUserInputOptionPayload
{
    public string Label { get; set; } = string.Empty;

    public string? Description { get; set; }
}

internal sealed class TianShuSidecarTaskPayload
{
    public string? TaskType { get; set; }

    public string? Status { get; set; }
}

internal sealed class TianShuSidecarOperationPayload
{
    public string? OperationName { get; set; }

    public string? Phase { get; set; }
}

internal sealed class TianShuSidecarReasoningPayload
{
    public string? ItemId { get; set; }

    public string? Status { get; set; }

    public string? Phase { get; set; }

    public string? Text { get; set; }

    public string? SourceMethod { get; set; }
}

internal sealed class TianShuSidecarItemPayload
{
    public string? ItemId { get; set; }

    public string? ItemType { get; set; }

    public string? Status { get; set; }

    public string? Phase { get; set; }

    public string? Text { get; set; }

    public int ImageCount { get; set; }

    public IReadOnlyList<TianShuSidecarUserInputPayload> Inputs { get; set; } =
        Array.Empty<TianShuSidecarUserInputPayload>();
}

internal sealed class TianShuSidecarCommittedUserMessagePayload
{
    public string? ItemId { get; set; }

    public string Text { get; set; } = string.Empty;

    public int ImageCount { get; set; }

    public string? CorrelationId { get; set; }

    public IReadOnlyList<TianShuSidecarUserInputPayload> Inputs { get; set; } =
        Array.Empty<TianShuSidecarUserInputPayload>();
}

internal sealed class TianShuSidecarPendingFollowUpPayload
{
    public string CorrelationId { get; set; } = string.Empty;

    public string RequestedMode { get; set; } = string.Empty;

    public string EffectiveMode { get; set; } = string.Empty;

    public string LifecycleState { get; set; } = string.Empty;

    public string? ExpectedTurnId { get; set; }

    public string? TurnId { get; set; }

    public TianShuSidecarPendingFollowUpCompareKeyPayload? CompareKey { get; set; }
}

internal sealed class TianShuSidecarPendingFollowUpCompareKeyPayload
{
    public string? Message { get; set; }

    public int ImageCount { get; set; }
}

internal sealed class TianShuSidecarPendingInputStatePayload
{
    public IReadOnlyList<TianShuSidecarPendingInputStateEntryPayload> Entries { get; set; } =
        Array.Empty<TianShuSidecarPendingInputStateEntryPayload>();
    public IReadOnlyList<TianShuSidecarPendingInputStateEntryPayload> QueuedUserMessages { get; set; } =
        Array.Empty<TianShuSidecarPendingInputStateEntryPayload>();
    public IReadOnlyList<TianShuSidecarPendingInputStateEntryPayload> PendingSteers { get; set; } =
        Array.Empty<TianShuSidecarPendingInputStateEntryPayload>();

    public bool InterruptRequestPending { get; set; }

    public bool SubmitPendingSteersAfterInterrupt { get; set; }
}

internal sealed class TianShuSidecarPendingInputStateEntryPayload
{
    public string CorrelationId { get; set; } = string.Empty;

    public string RequestedMode { get; set; } = string.Empty;

    public string EffectiveMode { get; set; } = string.Empty;

    public string LifecycleState { get; set; } = string.Empty;

    public string? ExpectedTurnId { get; set; }

    public string? TurnId { get; set; }

    public string PendingBucket { get; set; } = "QueuedUserMessage";

    public TianShuSidecarPendingFollowUpCompareKeyPayload? CompareKey { get; set; }

    public IReadOnlyList<TianShuSidecarUserInputPayload> Inputs { get; set; } =
        Array.Empty<TianShuSidecarUserInputPayload>();
}

internal sealed class TianShuSidecarUserInputPayload
{
    public string Type { get; set; } = string.Empty;

    public string? Text { get; set; }

    public string? Url { get; set; }

    public string? Path { get; set; }

    public string? Name { get; set; }

    public IReadOnlyList<TianShuSidecarTextElementPayload> TextElements { get; set; } =
        Array.Empty<TianShuSidecarTextElementPayload>();
}

internal sealed class TianShuSidecarTextElementPayload
{
    public TianShuSidecarByteRangePayload? ByteRange { get; set; }

    public string? Placeholder { get; set; }
}

internal sealed class TianShuSidecarByteRangePayload
{
    public int Start { get; set; }

    public int End { get; set; }
}

internal sealed class TianShuSidecarAgentJobProgressPayload
{
    public string JobId { get; set; } = string.Empty;

    public int TotalItems { get; set; }

    public int PendingItems { get; set; }

    public int RunningItems { get; set; }

    public int CompletedItems { get; set; }

    public int FailedItems { get; set; }

    public int? EtaSeconds { get; set; }
}

internal sealed class TianShuSidecarDeprecationNoticePayload
{
    public string Summary { get; set; } = string.Empty;

    public string? Details { get; set; }
}

internal sealed class TianShuSidecarConfigRangePositionPayload
{
    public int Line { get; set; }

    public int Column { get; set; }
}

internal sealed class TianShuSidecarConfigRangePayload
{
    public TianShuSidecarConfigRangePositionPayload? Start { get; set; }

    public TianShuSidecarConfigRangePositionPayload? End { get; set; }
}

internal sealed class TianShuSidecarConfigWarningPayload
{
    public string Summary { get; set; } = string.Empty;

    public string? Details { get; set; }

    public string? Path { get; set; }

    public TianShuSidecarConfigRangePayload? Range { get; set; }
}

internal sealed class TianShuSidecarThreadStatusChangedPayload
{
    public string Type { get; set; } = string.Empty;

    public IReadOnlyList<string> ActiveFlags { get; set; } = Array.Empty<string>();
}

internal sealed class TianShuSidecarThreadNameUpdatedPayload
{
    public string? ThreadName { get; set; }
}

internal sealed class TianShuSidecarTokenUsageBreakdownPayload
{
    public int TotalTokens { get; set; }

    public int InputTokens { get; set; }

    public int CachedInputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int ReasoningOutputTokens { get; set; }
}

internal sealed class TianShuSidecarThreadTokenUsagePayload
{
    public TianShuSidecarTokenUsageBreakdownPayload? Last { get; set; }

    public TianShuSidecarTokenUsageBreakdownPayload? Total { get; set; }

    public int? ModelContextWindow { get; set; }
}

internal sealed class TianShuSidecarCommandExecOutputDeltaPayload
{
    public string ProcessId { get; set; } = string.Empty;

    public string Stream { get; set; } = string.Empty;

    public string DeltaBase64 { get; set; } = string.Empty;

    public bool CapReached { get; set; }
}

internal sealed class TianShuSidecarAppListUpdatedPayload
{
    public IReadOnlyList<TianShuSidecarAppListUpdatedEntryPayload> Items { get; set; } =
        Array.Empty<TianShuSidecarAppListUpdatedEntryPayload>();
}

internal sealed class TianShuSidecarAppListUpdatedEntryPayload
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? LogoUrl { get; set; }

    public string? LogoUrlDark { get; set; }

    public string? DistributionChannel { get; set; }

    public TianShuSidecarAppBrandingPayload? Branding { get; set; }

    public TianShuSidecarAppMetadataPayload? AppMetadata { get; set; }

    public IReadOnlyDictionary<string, string> Labels { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public bool IsAccessible { get; set; }

    public bool IsEnabled { get; set; }

    public string? InstallUrl { get; set; }

    public IReadOnlyList<string> PluginDisplayNames { get; set; } = Array.Empty<string>();
}

internal sealed class TianShuSidecarAppBrandingPayload
{
    public string? Category { get; set; }

    public string? Developer { get; set; }

    public string? Website { get; set; }

    public string? PrivacyPolicy { get; set; }

    public string? TermsOfService { get; set; }

    public bool? IsDiscoverableApp { get; set; }
}

internal sealed class TianShuSidecarAppReviewPayload
{
    public string? Status { get; set; }

    public string? Message { get; set; }
}

internal sealed class TianShuSidecarAppScreenshotPayload
{
    public string? Caption { get; set; }

    public string? Url { get; set; }
}

internal sealed class TianShuSidecarAppMetadataPayload
{
    public TianShuSidecarAppReviewPayload? Review { get; set; }

    public IReadOnlyList<TianShuSidecarAppScreenshotPayload> Screenshots { get; set; } =
        Array.Empty<TianShuSidecarAppScreenshotPayload>();
}

internal sealed class TianShuSidecarWindowsSandboxSetupPayload
{
    public string? Mode { get; set; }

    public bool? Success { get; set; }

    public string? Error { get; set; }
}

internal sealed class TianShuSidecarMcpServerOauthLoginPayload
{
    public string? Name { get; set; }

    public bool? Success { get; set; }

    public string? Error { get; set; }
}

internal sealed class TianShuSidecarRealtimeSessionPayload
{
    public string? ThreadId { get; set; }

    public string? SessionId { get; set; }
}

internal sealed class TianShuSidecarFuzzyFileSearchSessionPayload
{
    public string? SessionId { get; set; }

    public IReadOnlyList<TianShuSidecarFuzzyFileSearchFilePayload> Files { get; set; } =
        Array.Empty<TianShuSidecarFuzzyFileSearchFilePayload>();

    public bool IsCompleted { get; set; }
}

internal sealed class TianShuSidecarFuzzyFileSearchFilePayload
{
    public string? Path { get; set; }

    public string? FileName { get; set; }
}

internal sealed class TianShuSidecarThreadRealtimeItemAddedPayload
{
    public string? ItemId { get; set; }

    public string? ItemType { get; set; }

    public string? Role { get; set; }

    public string? Status { get; set; }

    public string? Text { get; set; }
}

internal sealed class TianShuSidecarThreadRealtimeOutputAudioDeltaPayload
{
    public string Data { get; set; } = string.Empty;

    public int SampleRate { get; set; }

    public int NumChannels { get; set; }

    public int? SamplesPerChannel { get; set; }
}

internal sealed class TianShuSidecarThreadRealtimeErrorPayload
{
    public string Message { get; set; } = string.Empty;
}

internal sealed class TianShuSidecarThreadRealtimeClosedPayload
{
    public string? Reason { get; set; }
}

internal sealed class TianShuSidecarFollowUpAcceptedResult
{
    public string CorrelationId { get; set; } = string.Empty;

    public TianShuSidecarFollowUpMode RequestedMode { get; set; }
}

internal sealed class TianShuSidecarMcpServerStatusPayload
{
    public int? Count { get; set; }

    public TianShuSidecarMcpServerEntryPayload[] Servers { get; set; } = [];
}

internal sealed class TianShuSidecarMcpServerEntryPayload
{
    public string Name { get; set; } = string.Empty;

    public string? AuthStatus { get; set; }

    public int ToolCount { get; set; }

    public int ResourceCount { get; set; }

    public int ResourceTemplateCount { get; set; }
}

internal sealed class TianShuSidecarThreadItem
{
    public string ThreadId { get; set; } = string.Empty;

    public string Preview { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Cwd { get; set; }

    public string? Path { get; set; }

    public string? ModelProvider { get; set; }

    public TianShuSidecarSessionSource? Source { get; set; }

    public string? CliVersion { get; set; }

    public string? AgentNickname { get; set; }

    public string? AgentRole { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public bool IsEphemeral { get; set; }

    public TianShuSidecarThreadSessionConfiguration? SessionConfiguration { get; set; }

    public TianShuSidecarThreadStatus? Status { get; set; }

    public TianShuSidecarGitInfo? GitInfo { get; set; }

    public IReadOnlyList<TianShuSidecarThreadTurn> Turns { get; set; } = Array.Empty<TianShuSidecarThreadTurn>();

    public IReadOnlyList<TianShuSidecarSeedHistoryItem> SeedHistory { get; set; } = Array.Empty<TianShuSidecarSeedHistoryItem>();

    public TianShuSidecarPendingInputStatePayload? PendingInputState { get; set; }

    public IReadOnlyList<TianShuSidecarPendingInteractiveRequestReplayPayload> PendingInteractiveRequests { get; set; } =
        Array.Empty<TianShuSidecarPendingInteractiveRequestReplayPayload>();
}

internal sealed class TianShuSidecarThreadListRequest
{
    public int Limit { get; set; } = 20;

    public string? Cursor { get; set; }

    public bool Archived { get; set; }

    public string? Cwd { get; set; }

    public string SortKey { get; set; } = "updated_at";

    public IReadOnlyList<string> ModelProviders { get; set; } = Array.Empty<string>();

    public IReadOnlyList<TianShuSidecarThreadSourceKind> SourceKinds { get; set; } = Array.Empty<TianShuSidecarThreadSourceKind>();

    public string? SearchTerm { get; set; }

    public bool MatchCurrentCwd { get; set; } = true;
}

internal sealed class TianShuSidecarThreadListResult
{
    public IReadOnlyList<TianShuSidecarThreadItem> Items { get; set; } = Array.Empty<TianShuSidecarThreadItem>();

    public string? NextCursor { get; set; }
}

internal abstract class TianShuSidecarThreadRequestBase
{
    public string? Model { get; set; }

    public string? ModelProvider { get; set; }

    public TianShuSidecarServiceTierOverride ServiceTier { get; set; } = TianShuSidecarServiceTierOverride.Unspecified;

    public string? WorkingDirectory { get; set; }

    public TianShuSidecarApprovalPolicy? ApprovalPolicy { get; set; }

    public string? SandboxMode { get; set; }

    public IReadOnlyDictionary<string, TianShuSidecarStructuredValue>? Config { get; set; }

    public string? BaseInstructions { get; set; }

    public string? DeveloperInstructions { get; set; }
}

internal sealed class TianShuSidecarThreadStartRequest : TianShuSidecarThreadRequestBase
{
    public string? ServiceName { get; set; }

    public TianShuSidecarPersonality? Personality { get; set; }

    public bool? Ephemeral { get; set; }

    // 动态工具在 bridge 北向只保留结构化值承载，避免再次引入 presentation 专用 DTO。
    // Keep dynamic tools as structured values at the bridge boundary to avoid reintroducing presentation-only DTOs.
    public IReadOnlyList<TianShuSidecarStructuredValue>? DynamicTools { get; set; }

    public bool? PersistExtendedHistory { get; set; }

    public bool? ExperimentalRawEvents { get; set; }
}

internal sealed class TianShuSidecarThreadResumeRequest : TianShuSidecarThreadRequestBase
{
    public string ThreadId { get; set; } = string.Empty;

    public string? Path { get; set; }

    public IReadOnlyList<TianShuSidecarThreadHistoryItem>? History { get; set; }

    public TianShuSidecarPersonality? Personality { get; set; }

    public bool? PersistExtendedHistory { get; set; }
}

internal sealed class TianShuSidecarThreadForkRequest : TianShuSidecarThreadRequestBase
{
    public string ThreadId { get; set; } = string.Empty;

    public string? Path { get; set; }

    public bool? Ephemeral { get; set; }

    public bool? PersistExtendedHistory { get; set; }
}

internal sealed class TianShuSidecarThreadStatus
{
    public string Type { get; set; } = string.Empty;

    public IReadOnlyList<string> ActiveFlags { get; set; } = Array.Empty<string>();
}

internal sealed class TianShuSidecarGitInfo
{
    public string? Sha { get; set; }

    public string? Branch { get; set; }

    public string? OriginUrl { get; set; }
}

internal sealed class TianShuSidecarThreadTurn
{
    public string Id { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public TianShuSidecarThreadTurnError? Error { get; set; }

    public IReadOnlyList<TianShuSidecarThreadTurnItem> Items { get; set; } = Array.Empty<TianShuSidecarThreadTurnItem>();
}

internal sealed class TianShuSidecarThreadTurnError
{
    public string? Message { get; set; }

    public string? AdditionalDetails { get; set; }
}

internal sealed class TianShuSidecarThreadTurnItem
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Text { get; set; }

    public string? Phase { get; set; }

    public IReadOnlyList<TianShuSidecarUserInputPayload> Inputs { get; set; } =
        Array.Empty<TianShuSidecarUserInputPayload>();
}

internal sealed class TianShuSidecarSeedHistoryItem
{
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public IReadOnlyList<TianShuSidecarUserInputPayload> Inputs { get; set; } =
        Array.Empty<TianShuSidecarUserInputPayload>();
}

internal sealed class TianShuSidecarThreadOperationResult
{
    public string Message { get; set; } = string.Empty;

    public TianShuSidecarThreadItem? Thread { get; set; }
}

internal sealed class TianShuSidecarThreadSessionConfiguration
{
    public string? Model { get; set; }

    public string? ModelProvider { get; set; }

    public string? ModelProviderId { get; set; }

    public TianShuSidecarServiceTier? ServiceTier { get; set; }

    public TianShuSidecarApprovalPolicy? ApprovalPolicy { get; set; }

    public string? SandboxPolicy { get; set; }

    public TianShuSidecarStructuredValue? SandboxPolicyPayload { get; set; }

    public string? ReasoningEffort { get; set; }

    public string? HistoryLogId { get; set; }

    public int? HistoryEntryCount { get; set; }

    public string? RolloutPath { get; set; }

    public string? ForkedFromId { get; set; }

    public string? Cwd { get; set; }

    public bool? Ephemeral { get; set; }

    public bool? AllowLoginShell { get; set; }

    public TianShuSidecarStructuredValue? ShellEnvironmentPolicy { get; set; }

    public string? ProviderBaseUrl { get; set; }

    public string? ProviderApiKeyEnvironmentVariable { get; set; }

    public string? ProviderWireApi { get; set; }

    public int? ProviderRequestMaxRetries { get; set; }

    public int? ProviderStreamMaxRetries { get; set; }

    public long? ProviderStreamIdleTimeoutMs { get; set; }

    public bool? ProviderSupportsWebsockets { get; set; }

    public string? WebSearchMode { get; set; }

    public string? ServiceName { get; set; }

    public string? BaseInstructions { get; set; }

    public string? DeveloperInstructions { get; set; }

    public string? UserInstructions { get; set; }

    public string? ReasoningSummary { get; set; }

    public string? Verbosity { get; set; }

    public string? Personality { get; set; }

    public IReadOnlyList<TianShuSidecarStructuredValue>? DynamicTools { get; set; }

    public TianShuSidecarStructuredValue? CollaborationMode { get; set; }

    public bool? PersistExtendedHistory { get; set; }

    public TianShuSidecarSessionSource? SessionSource { get; set; }

    public string? WindowsSandboxLevel { get; set; }

    public bool? DefaultModeRequestUserInputEnabled { get; set; }
}

internal sealed class TianShuSidecarThreadMetadataUpdateRequest
{
    public string ThreadId { get; set; } = string.Empty;

    public TianShuSidecarGitInfo? GitInfo { get; set; }
}

internal sealed class TianShuSidecarConversationMessage
{
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public IReadOnlyList<TianShuSidecarUserInputPayload> Inputs { get; set; } =
        Array.Empty<TianShuSidecarUserInputPayload>();
}

internal sealed class TianShuSidecarHistoryMessage
{
    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    public IReadOnlyList<TianShuSidecarUserInputPayload> Inputs { get; set; } =
        Array.Empty<TianShuSidecarUserInputPayload>();
}

internal sealed class TianShuSidecarThreadSession
{
    public string ThreadId { get; set; } = string.Empty;

    public string Preview { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Cwd { get; set; }

    public string? Path { get; set; }

    public string? ModelProvider { get; set; }

    public TianShuSidecarSessionSource? Source { get; set; }

    public string? CliVersion { get; set; }

    public string? AgentNickname { get; set; }

    public string? AgentRole { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public bool IsEphemeral { get; set; }

    public bool MessagesAreAuthoritative { get; set; }

    public TianShuSidecarThreadSessionConfiguration? SessionConfiguration { get; set; }

    public TianShuSidecarThreadStatus? Status { get; set; }

    public TianShuSidecarGitInfo? GitInfo { get; set; }

    public IReadOnlyList<TianShuSidecarThreadTurn> Turns { get; set; } = Array.Empty<TianShuSidecarThreadTurn>();

    public IReadOnlyList<TianShuSidecarSeedHistoryItem> SeedHistory { get; set; } = Array.Empty<TianShuSidecarSeedHistoryItem>();

    public List<TianShuSidecarConversationMessage> Messages { get; } = new();

    public TianShuSidecarPendingInputStatePayload? PendingInputState { get; set; }

    public IReadOnlyList<TianShuSidecarPendingInteractiveRequestReplayPayload> PendingInteractiveRequests { get; set; } =
        Array.Empty<TianShuSidecarPendingInteractiveRequestReplayPayload>();
}

internal sealed class TianShuSidecarPendingInteractiveRequestReplayPayload
{
    public long RequestId { get; set; }

    public string? RequestIdRaw { get; set; }

    public string RequestKind { get; set; } = string.Empty;

    public string? RequestMethod { get; set; }

    public string CallId { get; set; } = string.Empty;

    public string? ThreadId { get; set; }

    public string? TurnId { get; set; }

    public string? ToolName { get; set; }

    public string? ServerName { get; set; }

    public string? Text { get; set; }

    public string? Status { get; set; }

    public string? Phase { get; set; }

    public bool? RequiresApproval { get; set; }

    public string? ApprovalKind { get; set; }

    public IReadOnlyList<string> AvailableDecisions { get; set; } = Array.Empty<string>();

    public IReadOnlyList<TianShuSidecarApprovalDecisionOptionPayload> AvailableDecisionOptions { get; set; } =
        Array.Empty<TianShuSidecarApprovalDecisionOptionPayload>();

    public TianShuSidecarApprovalRequestPayload? ApprovalRequest { get; set; }

    public TianShuSidecarPermissionRequestPayload? PermissionRequest { get; set; }

    public TianShuSidecarUserInputRequestPayload? UserInputRequest { get; set; }
}
