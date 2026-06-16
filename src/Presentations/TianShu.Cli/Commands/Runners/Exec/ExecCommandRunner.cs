using System.Text;
using System.Text.Json;
using TianShu.ControlPlane;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.Events;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Workflows;
using Task = System.Threading.Tasks.Task;

namespace TianShu.Cli;

internal sealed class ExecCommandRunner
{
    private readonly Func<IExecutionRuntime> runtimeFactory;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public ExecCommandRunner()
        : this(TianShuAppHostRuntimeClientFactory.Create)
    {
    }

    internal ExecCommandRunner(Func<IExecutionRuntime> runtimeFactory)
    {
        this.runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
    }

    public async Task<int> RunAsync(ExecCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            if (!options.SkipGitRepoCheck
                && !options.DangerouslyBypassApprovalsAndSandbox
                && !IsInsideGitRepository(options.WorkingDirectory))
            {
                return WriteFailure(options, "当前目录不在 Git 仓库内；如需强制执行，请传入 --skip-git-repo-check。");
            }

            var prompt = options.CommandKind == ExecCommandKind.Review
                ? null
                : ResolvePrompt(options.Prompt);
            var bootstrap = CliRuntimeBootstrapper.Prepare(options);
            bootstrap.RuntimeOptions.OutputSchema = LoadOutputSchema(options.OutputSchemaFilePath);

            await using var runtime = runtimeFactory();
            var assistantText = new StringBuilder();
            var errors = new List<string>();
            var blockedInteractive = false;
            var cancelled = false;
            var sawErrorEvent = false;
            ControlPlaneTurnSubmissionResult? result = null;
            ControlPlaneReviewStartResult? reviewStartResult = null;
            string? targetThreadId = null;
            string? targetTurnId = null;
            var reviewAwaitingCompletion = false;
            var reviewCompletion = new TaskCompletionSource<ReviewExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task RequestInterruptAsync()
                => Task.Run(async () =>
                {
                    try
                    {
                        await TianShuControlPlaneClientFactory.Create(runtime).Conversations.InterruptTurnAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                });

            void CompleteReviewFromEvent(ControlPlaneConversationStreamEvent streamEvent)
            {
                if (!reviewAwaitingCompletion)
                {
                    return;
                }

                if (!IsMatchingReviewTurn(streamEvent, targetThreadId, targetTurnId))
                {
                    return;
                }

                switch (streamEvent.Kind)
                {
                    case ControlPlaneConversationStreamEventKind.TurnCompleted:
                        reviewCompletion.TrySetResult(new ReviewExecutionResult(
                            IsCompletedTurnStatus(streamEvent.Status),
                            streamEvent.TurnId?.Value ?? targetTurnId,
                            streamEvent.Status));
                        break;
                    case ControlPlaneConversationStreamEventKind.Error when streamEvent.WillRetry != true:
                        reviewCompletion.TrySetResult(new ReviewExecutionResult(
                            false,
                            streamEvent.TurnId?.Value ?? targetTurnId,
                            streamEvent.Status));
                        break;
                }
            }

            EventHandler<ControlPlaneConversationStreamEventArgs>? handler = (_, args) =>
            {
                ControlPlaneConversationStreamEvent streamEvent = args.StreamEvent;
                switch (streamEvent.Kind)
                {
                    case ControlPlaneConversationStreamEventKind.AssistantTextDelta:
                        if (!string.IsNullOrEmpty(streamEvent.Text))
                        {
                            assistantText.Append(streamEvent.Text);
                            if (options.OutputJson)
                            {
                                WriteJsonEvent(new
                                {
                                    type = "assistant_text_delta",
                                    text = streamEvent.Text,
                                    threadId = streamEvent.ThreadId?.Value,
                                    turnId = streamEvent.TurnId?.Value,
                                });
                            }
                        }

                        break;
                    case ControlPlaneConversationStreamEventKind.AssistantTextCompleted:
                        if (options.OutputJson)
                        {
                            WriteJsonEvent(new
                            {
                                type = "assistant_text_completed",
                                threadId = streamEvent.ThreadId?.Value,
                                turnId = streamEvent.TurnId?.Value,
                            });
                        }

                        break;
                    case ControlPlaneConversationStreamEventKind.TurnStarted:
                        if (options.OutputJson)
                        {
                            WriteJsonEvent(new
                            {
                                type = "turn_started",
                                threadId = streamEvent.ThreadId?.Value,
                                turnId = streamEvent.TurnId?.Value,
                            });
                        }

                        break;
                    case ControlPlaneConversationStreamEventKind.TurnCompleted:
                        CompleteReviewFromEvent(streamEvent);
                        if (options.OutputJson)
                        {
                            WriteJsonEvent(new
                            {
                                type = "turn_completed",
                                threadId = streamEvent.ThreadId?.Value,
                                turnId = streamEvent.TurnId?.Value,
                                status = streamEvent.Status,
                            });
                        }

                        break;
                    case ControlPlaneConversationStreamEventKind.ApprovalRequested:
                    case ControlPlaneConversationStreamEventKind.PermissionRequested:
                    case ControlPlaneConversationStreamEventKind.UserInputRequested:
                        blockedInteractive = true;
                        errors.Add(BuildInteractiveRejectionMessage(streamEvent.Kind));
                        reviewCompletion.TrySetResult(new ReviewExecutionResult(
                            false,
                            streamEvent.TurnId?.Value ?? targetTurnId,
                            streamEvent.Status));
                        _ = RequestInterruptAsync();

                        if (options.OutputJson)
                        {
                            WriteJsonEvent(new
                            {
                                type = "error",
                                message = errors[^1],
                                threadId = streamEvent.ThreadId?.Value,
                                turnId = streamEvent.TurnId?.Value,
                                callId = streamEvent.CallId?.Value,
                            });
                        }

                        sendCancellation.Cancel();
                        break;
                    case ControlPlaneConversationStreamEventKind.Error when streamEvent.WillRetry != true:
                        sawErrorEvent = true;
                        errors.Add(streamEvent.Message ?? streamEvent.Text ?? "收到错误事件。");
                        CompleteReviewFromEvent(streamEvent);
                        if (options.OutputJson)
                        {
                            WriteJsonEvent(new
                            {
                                type = "error",
                                message = errors[^1],
                                threadId = streamEvent.ThreadId?.Value,
                                turnId = streamEvent.TurnId?.Value,
                            });
                        }

                        break;
                }
            };

            runtime.StreamEventReceived += handler;
            try
            {
                await runtime.InitializeAsync(bootstrap.RuntimeOptions, dynamicToolCallHandler: null, cancellationToken).ConfigureAwait(false);
                var controlPlane = TianShuControlPlaneClientFactory.Create(runtime);
                await PrepareThreadAsync(runtime, bootstrap.RuntimeOptions, options, cancellationToken).ConfigureAwait(false);
                if (options.CommandKind == ExecCommandKind.Review)
                {
                    reviewAwaitingCompletion = true;
                    var sessionSnapshot = await CliSessionSnapshotUtilities.GetSnapshotAsync(runtime, sendCancellation.Token).ConfigureAwait(false);
                    reviewStartResult = await controlPlane.Workflows
                        .StartReviewAsync(BuildReviewStartRequest(sessionSnapshot.ActiveThreadId?.Value, options), sendCancellation.Token)
                        .ConfigureAwait(false);

                    sessionSnapshot = await CliSessionSnapshotUtilities.GetSnapshotAsync(runtime, sendCancellation.Token).ConfigureAwait(false);
                    targetThreadId = Normalize(reviewStartResult.ReviewThreadId) ?? sessionSnapshot.ActiveThreadId?.Value;
                    targetTurnId = Normalize(reviewStartResult.Turn?.Id);
                    if (IsCompletedTurnStatus(reviewStartResult.Turn?.Status))
                    {
                        reviewCompletion.TrySetResult(new ReviewExecutionResult(
                            true,
                            targetTurnId,
                            reviewStartResult.Turn?.Status));
                    }

                    if (!blockedInteractive && !reviewCompletion.Task.IsCompleted)
                    {
                        var reviewExecution = await reviewCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                        targetTurnId = reviewExecution.TurnId ?? targetTurnId;
                    }
                }
                else
                {
                    var userInputs = BuildUserInputs(options.ImagePaths, prompt!);
                    result = await controlPlane.Conversations.SubmitTurnAsync(
                            CliConversationEnvelopeFactory.Normalize(new ControlPlaneSubmitTurnCommand
                            {
                                Inputs = userInputs,
                            }),
                            sendCancellation.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (blockedInteractive)
            {
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                errors.Add("exec 已取消。");
                await RequestInterruptAsync().ConfigureAwait(false);
            }
            finally
            {
                runtime.StreamEventReceived -= handler;
                var sessionSnapshot = await CliSessionSnapshotUtilities.GetSnapshotAsync(runtime, CancellationToken.None).ConfigureAwait(false);
                await TryUnsubscribeThreadAsync(runtime, targetThreadId ?? sessionSnapshot.ActiveThreadId?.Value).ConfigureAwait(false);
            }

            var finalAssistantText = ResolveFinalAssistantText(
                assistantText.ToString(),
                result,
                reviewStartResult);
            WriteLastMessageFile(options.OutputLastMessageFilePath, finalAssistantText);

            var success = options.CommandKind == ExecCommandKind.Review
                ? !blockedInteractive
                  && !cancelled
                  && !sawErrorEvent
                  && reviewCompletion.Task.IsCompletedSuccessfully
                  && reviewCompletion.Task.Result.Success
                : !blockedInteractive
                  && !cancelled
                  && !sawErrorEvent
                  && result is not null
                  && result.Accepted;

            if (options.OutputJson)
            {
                var sessionSnapshot = await CliSessionSnapshotUtilities.GetSnapshotAsync(runtime, CancellationToken.None).ConfigureAwait(false);
                WriteJsonEvent(new
                {
                    type = success ? "exec_completed" : "exec_failed",
                    success,
                    threadId = targetThreadId ?? sessionSnapshot.ActiveThreadId?.Value,
                    turnId = targetTurnId ?? result?.TurnId?.Value,
                    turnStatus = reviewCompletion.Task.IsCompletedSuccessfully
                        ? reviewCompletion.Task.Result.Status
                        : result?.TurnStatus,
                    assistantText = finalAssistantText,
                    message = result?.Message ?? BuildReviewFailureMessage(reviewCompletion.Task),
                    errors = errors.Count == 0 ? null : errors,
                    tokenUsage = BuildEstimatedTokenUsage(prompt, finalAssistantText),
                });
                return success ? 0 : 1;
            }

            if (!string.IsNullOrWhiteSpace(finalAssistantText))
            {
                Console.Out.Write(finalAssistantText);
                if (!finalAssistantText.EndsWith('\n'))
                {
                    Console.Out.WriteLine();
                }
            }

            if (!success)
            {
                foreach (var error in errors)
                {
                    Console.Error.WriteLine(error);
                }

                if (errors.Count == 0)
                {
                    Console.Error.WriteLine(result?.Message ?? BuildReviewFailureMessage(reviewCompletion.Task) ?? "exec 执行失败。");
                }
            }

            return success ? 0 : 1;

            void WriteJsonEvent(object payload)
                => Console.Out.WriteLine(JsonSerializer.Serialize(payload, jsonOptions));
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or DirectoryNotFoundException or FormatException or JsonException)
        {
            return WriteFailure(options, ex.Message);
        }
    }

    private static async Task PrepareThreadAsync(
        IExecutionRuntime runtime,
        ControlPlaneInitializeRuntimeCommand runtimeOptions,
        ExecCommandOptions options,
        CancellationToken cancellationToken)
    {
        if (options.CommandKind != ExecCommandKind.Resume)
        {
            var startResult = await TianShuControlPlaneClientFactory.Create(runtime).Conversations.StartThreadAsync(BuildThreadStartCommand(runtimeOptions, options), cancellationToken).ConfigureAwait(false);
            if (startResult is null)
            {
                throw new InvalidOperationException("创建 exec 线程失败。");
            }

            return;
        }

        ControlPlaneThreadSummary? candidate = null;
        if (options.UseLast)
        {
            candidate = await FindLatestThreadAsync(runtime, runtimeOptions, options, cancellationToken).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(options.ResumeTarget))
        {
            candidate = await FindMatchingThreadAsync(runtime, options.ResumeTarget!, cancellationToken).ConfigureAwait(false);
        }

        if (candidate is not null)
        {
            var resumed = await TianShuControlPlaneClientFactory.Create(runtime).Conversations.ResumeThreadAsync(BuildThreadResumeCommand(candidate.ThreadId.Value, runtimeOptions, options), cancellationToken).ConfigureAwait(false);
            if (resumed is null)
            {
                throw new InvalidOperationException($"恢复 exec 线程失败：{candidate.ThreadId.Value}");
            }

            return;
        }

        var startResultWhenMissingResumeTarget = await TianShuControlPlaneClientFactory.Create(runtime).Conversations.StartThreadAsync(BuildThreadStartCommand(runtimeOptions, options), cancellationToken).ConfigureAwait(false);
        if (startResultWhenMissingResumeTarget is null)
        {
            throw new InvalidOperationException("创建 exec 线程失败。");
        }
    }

    private static async Task<ControlPlaneThreadSummary?> FindLatestThreadAsync(
        IExecutionRuntime runtime,
        ControlPlaneInitializeRuntimeCommand runtimeOptions,
        ExecCommandOptions options,
        CancellationToken cancellationToken)
    {
        var modelProviders = string.IsNullOrWhiteSpace(runtimeOptions.ModelProvider)
            ? Array.Empty<string>()
            : new[] { runtimeOptions.ModelProvider };
        var result = await TianShuControlPlaneClientFactory.Create(runtime).Conversations.ListThreadsAsync(
                new ControlPlaneThreadListQuery
                {
                    Limit = 1,
                    Archived = false,
                    WorkingDirectory = options.ShowAll ? null : options.WorkingDirectory,
                    SortKey = "updated_at",
                    ModelProviders = modelProviders,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return result.Threads.FirstOrDefault();
    }

    private static async Task<ControlPlaneThreadSummary?> FindMatchingThreadAsync(
        IExecutionRuntime runtime,
        string target,
        CancellationToken cancellationToken)
    {
        var result = await TianShuControlPlaneClientFactory.Create(runtime).Conversations.ListThreadsAsync(
                new ControlPlaneThreadListQuery
                {
                    Limit = 200,
                    Archived = false,
                    SortKey = "updated_at",
                    SearchTerm = target,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return result.Threads
            .Where(item =>
                string.Equals(item.ThreadId.Value, target, StringComparison.Ordinal)
                || string.Equals(item.Name, target, StringComparison.Ordinal))
            .OrderByDescending(static item => item.UpdatedAt)
            .FirstOrDefault();
    }

    private static ControlPlaneStartThreadCommand BuildThreadStartCommand(ControlPlaneInitializeRuntimeCommand runtimeOptions, ExecCommandOptions options)
        => new()
        {
            Model = runtimeOptions.Model,
            WorkingDirectory = runtimeOptions.WorkingDirectory,
            ApprovalPolicy = runtimeOptions.ApprovalPolicy,
            SandboxMode = runtimeOptions.SandboxMode,
            Configuration = BuildExecConfig(options),
            Ephemeral = options.Ephemeral ? true : null,
        };

    private static ControlPlaneResumeThreadCommand BuildThreadResumeCommand(string threadId, ControlPlaneInitializeRuntimeCommand runtimeOptions, ExecCommandOptions options)
        => new()
        {
            ThreadId = new ThreadId(threadId),
            Model = runtimeOptions.Model,
            WorkingDirectory = runtimeOptions.WorkingDirectory,
            ApprovalPolicy = runtimeOptions.ApprovalPolicy,
            SandboxMode = runtimeOptions.SandboxMode,
            Configuration = BuildExecConfig(options),
        };

    private static IReadOnlyList<ControlPlaneInputItem> BuildUserInputs(IReadOnlyList<string> imagePaths, string prompt)
        => CliConversationInputUtilities.BuildTextAndImageInputs(imagePaths, prompt);

    private static StructuredValue? LoadOutputSchema(string? outputSchemaFilePath)
    {
        if (string.IsNullOrWhiteSpace(outputSchemaFilePath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(outputSchemaFilePath));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"output schema 必须是 JSON 对象：{outputSchemaFilePath}");
            }

            return StructuredValue.FromJsonElement(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"output schema 文件不是合法 JSON：{outputSchemaFilePath}，{ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"读取 output schema 文件失败：{outputSchemaFilePath}，{ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException($"读取 output schema 文件失败：{outputSchemaFilePath}，{ex.Message}", ex);
        }
    }

    private static IReadOnlyDictionary<string, StructuredValue>? BuildExecConfig(ExecCommandOptions? options)
    {
        if (options is null || options.AdditionalWritableDirectories.Count == 0)
        {
            return null;
        }

        var writableRoots = options.AdditionalWritableDirectories
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Select(static path => StructuredValue.FromString(path))
            .ToArray();
        if (writableRoots.Length == 0)
        {
            return null;
        }

        return new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["sandbox_workspace_write"] = StructuredValue.FromObject(
                new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["writable_roots"] = StructuredValue.FromArray(writableRoots),
                }),
        };
    }

    private static ControlPlaneReviewStartCommand BuildReviewStartRequest(string? threadId, ExecCommandOptions options)
        => new()
        {
            ThreadId = Normalize(threadId)
                ?? throw new InvalidOperationException("exec review 在启动 review 前未能创建线程。"),
            Target = BuildReviewTarget(options),
        };

    private static ControlPlaneReviewTarget BuildReviewTarget(ExecCommandOptions options)
    {
        if (options.ReviewUncommitted)
        {
            return new ControlPlaneReviewUncommittedChangesTarget();
        }

        if (!string.IsNullOrWhiteSpace(options.ReviewBaseBranch))
        {
            return new ControlPlaneReviewBaseBranchTarget
            {
                Branch = options.ReviewBaseBranch,
            };
        }

        if (!string.IsNullOrWhiteSpace(options.ReviewCommit))
        {
            return new ControlPlaneReviewCommitTarget
            {
                Sha = options.ReviewCommit,
                Title = Normalize(options.ReviewCommitTitle),
            };
        }

        var instructions = ResolvePrompt(options.ReviewPrompt).Trim();
        if (instructions.Length == 0)
        {
            throw new InvalidOperationException("review instructions 不能为空。");
        }

        return new ControlPlaneReviewCustomTarget
        {
            Instructions = instructions,
        };
    }

    private static string ResolveFinalAssistantText(
        string assistantText,
        ControlPlaneTurnSubmissionResult? result,
        ControlPlaneReviewStartResult? reviewStartResult)
    {
        if (!string.IsNullOrWhiteSpace(assistantText))
        {
            return assistantText;
        }

        if (result?.Accepted == true)
        {
            return result.Message;
        }

        return !string.IsNullOrWhiteSpace(reviewStartResult?.Turn?.DisplayText)
            ? reviewStartResult.Turn.DisplayText
            : string.Empty;
    }

    private static string? BuildReviewFailureMessage(Task<ReviewExecutionResult> completionTask)
    {
        if (!completionTask.IsCompletedSuccessfully)
        {
            return null;
        }

        var status = completionTask.Result.Status;
        if (string.IsNullOrWhiteSpace(status) || IsCompletedTurnStatus(status))
        {
            return null;
        }

        return $"review 未成功完成，当前状态：{status}。";
    }

    private static bool IsMatchingReviewTurn(ControlPlaneConversationStreamEvent streamEvent, string? threadId, string? turnId)
    {
        if (!string.IsNullOrWhiteSpace(turnId))
        {
            return string.Equals(streamEvent.TurnId?.Value, turnId, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(threadId))
        {
            return string.Equals(streamEvent.ThreadId?.Value, threadId, StringComparison.Ordinal);
        }

        return true;
    }

    private static bool IsCompletedTurnStatus(string? status)
        => string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ThreadTokenUsagePayload BuildEstimatedTokenUsage(string? inputText, string assistantText)
    {
        var inputTokens = EstimateTokens(inputText);
        var outputTokens = EstimateTokens(assistantText);
        var totalTokens = inputTokens + outputTokens;
        var usage = new TokenUsageBreakdownPayload(
            totalTokens,
            inputTokens,
            0,
            outputTokens,
            0);
        return new ThreadTokenUsagePayload(
            usage,
            usage,
            null,
            true,
            "text_length_estimate");
    }

    private static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0d));
    }

    private sealed record ReviewExecutionResult(bool Success, string? TurnId, string? Status);

    private static void WriteLastMessageFile(string? path, string assistantText)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, assistantText);
    }

    private static async Task TryUnsubscribeThreadAsync(IExecutionRuntime runtime, string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (normalizedThreadId is null)
        {
            return;
        }

        try
        {
            await TianShuControlPlaneClientFactory.Create(runtime).Conversations.UnsubscribeThreadAsync(
                    new ControlPlaneUnsubscribeThreadCommand
                    {
                        ThreadId = new ThreadId(normalizedThreadId),
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static bool IsInsideGitRepository(string workingDirectory)
    {
        var directory = new DirectoryInfo(workingDirectory);
        while (directory is not null)
        {
            var gitDirectory = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitDirectory) || File.Exists(gitDirectory))
            {
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private static string BuildInteractiveRejectionMessage(ControlPlaneConversationStreamEventKind kind)
        => kind switch
        {
            ControlPlaneConversationStreamEventKind.ApprovalRequested => "exec headless 模式不接受 approval 请求。",
            ControlPlaneConversationStreamEventKind.PermissionRequested => "exec headless 模式不接受权限补录请求。",
            ControlPlaneConversationStreamEventKind.UserInputRequested => "exec headless 模式不接受用户补录请求。",
            _ => "exec headless 模式收到了不支持的交互请求。",
        };

    private int WriteFailure(ExecCommandOptions options, string message)
    {
        if (options.OutputJson)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                type = "error",
                message,
            }, jsonOptions));
        }
        else
        {
            Console.Error.WriteLine(message);
        }

        return 1;
    }

    private static string ResolvePrompt(string? promptArgument)
    {
        if (!string.Equals(promptArgument, "-", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(promptArgument))
        {
            return promptArgument!;
        }

        var forceStdin = string.Equals(promptArgument, "-", StringComparison.Ordinal);
        if (!Console.IsInputRedirected && !forceStdin)
        {
            throw new InvalidOperationException("未提供 prompt。请传入位置参数，或通过 stdin 管道输入。");
        }

        if (!forceStdin)
        {
            Console.Error.WriteLine("Reading prompt from stdin...");
        }

        using var stdin = Console.OpenStandardInput();
        using var buffer = new MemoryStream();
        stdin.CopyTo(buffer);
        var prompt = DecodePromptBytes(buffer.ToArray());
        if (string.IsNullOrWhiteSpace(prompt.Trim()))
        {
            throw new InvalidOperationException("stdin 中未提供有效 prompt。");
        }

        return prompt;
    }

    private static string DecodePromptBytes(byte[] input)
    {
        if (input.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            input = input[3..];
        }

        if (input.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
        {
            throw new InvalidOperationException("stdin 看起来是 UTF-32LE，请先转换为 UTF-8 后重试。");
        }

        if (input.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
        {
            throw new InvalidOperationException("stdin 看起来是 UTF-32BE，请先转换为 UTF-8 后重试。");
        }

        if (input.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return DecodeUtf16(input[2..], littleEndian: true);
        }

        if (input.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            return DecodeUtf16(input[2..], littleEndian: false);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(input);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidOperationException($"stdin 不是有效的 UTF-8：{ex.Message}", ex);
        }
    }

    private static string DecodeUtf16(byte[] input, bool littleEndian)
    {
        if (input.Length % 2 != 0)
        {
            throw new InvalidOperationException("stdin 看起来像 UTF-16，但字节长度非法。");
        }

        try
        {
            var encoding = new UnicodeEncoding(bigEndian: !littleEndian, byteOrderMark: false, throwOnInvalidBytes: true);
            return encoding.GetString(input);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidOperationException("stdin 看起来像 UTF-16，但无法完成解码。", ex);
        }
    }
}
