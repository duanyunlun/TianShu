using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using TianShu.Cli.Interaction.Projection;
using TianShu.ControlPlane;
using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Interaction.Recording;

internal sealed class ChatInteractionRecorder(JsonSerializerOptions jsonOptions)
{
    private readonly object transcriptGate = new();
    private ConcurrentQueue<ProbeEventRecord> recordedEvents = new();
    private ConcurrentQueue<ChatProjectionRecord> projectionRecords = new();
    private ConcurrentQueue<string> executedInputs = new();
    private ConcurrentQueue<CliTranscriptRecord> transcriptRecords = new();
    private StringBuilder transcriptBuilder = new();

    public void Reset()
    {
        recordedEvents = new ConcurrentQueue<ProbeEventRecord>();
        projectionRecords = new ConcurrentQueue<ChatProjectionRecord>();
        executedInputs = new ConcurrentQueue<string>();
        transcriptRecords = new ConcurrentQueue<CliTranscriptRecord>();
        transcriptBuilder = new StringBuilder();
    }

    public void RecordStreamEvent(ControlPlaneConversationStreamEvent streamEvent)
        => recordedEvents.Enqueue(ProbeEventRecord.FromStreamEvent(streamEvent));

    public void RecordExecutedInput(string input, string? threadId, string? turnId)
    {
        executedInputs.Enqueue(input);
        RecordSyntheticEvent("CliInput", threadId, turnId, input);
    }

    public void RecordSyntheticEvent(
        string kind,
        string? threadId,
        string? turnId,
        string? message,
        string? text = null,
        string? status = null)
        => recordedEvents.Enqueue(ProbeEventRecord.CreateSynthetic(
            kind,
            threadId,
            turnId,
            callId: null,
            toolName: null,
            message: message,
            text: text,
            status: status));

    public InteractionPipelineProjectionResult RecordPresentationProjection(
        InteractionPipeline pipeline,
        ControlPlaneConversationStreamEvent streamEvent)
    {
        var result = pipeline.ProjectStreamEvent(streamEvent);
        foreach (var record in result.Projection.Records)
        {
            projectionRecords.Enqueue(record);
        }

        return result;
    }

    public void RecordIncompletePresentationSnapshot(
        InteractionPipeline pipeline,
        DateTimeOffset timestamp,
        string reason)
    {
        var record = pipeline.CaptureIncompleteAssistantSnapshot(timestamp, reason);
        if (record is not null)
        {
            projectionRecords.Enqueue(record);
        }
    }

    public void AppendTranscript(string text, bool appendNewLine, CliTranscriptRecordKind kind, bool isError)
    {
        if (string.IsNullOrEmpty(text) && !appendNewLine)
        {
            return;
        }

        lock (transcriptGate)
        {
            transcriptRecords.Enqueue(CliTranscriptRecord.Create(kind, text, appendNewLine, isError));
            if (isError && !string.IsNullOrEmpty(text))
            {
                transcriptBuilder.Append("[stderr] ");
            }

            if (!string.IsNullOrEmpty(text))
            {
                transcriptBuilder.Append(text);
            }

            if (appendNewLine)
            {
                transcriptBuilder.AppendLine();
            }
        }
    }

    public async Task WriteArtifactsAsync(
        ChatCommandOptions options,
        CliRuntimeBootstrapResult bootstrap,
        ChatScriptCommandFile? script,
        InteractionPipeline pipeline,
        DateTimeOffset startedAt,
        int exitCode,
        string? failureMessage,
        string? threadId,
        string? turnId,
        string? turnStatus,
        int failureCount,
        string resultText,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.Now;
        RecordIncompletePresentationSnapshot(
            pipeline,
            completedAt,
            exitCode == 0 ? "artifact_finalization" : "artifact_finalization_failure");

        var commandsText = string.Join(Environment.NewLine, executedInputs);
        var transcriptText = transcriptBuilder.ToString();
        var typedTranscript = transcriptRecords.ToArray();
        var projectionRecordList = projectionRecords.ToArray();
        var eventList = recordedEvents.ToArray();
        var summary = new ChatCommandSummary
        {
            Success = exitCode == 0,
            ExitCode = exitCode,
            ExitCodeName = exitCode == 0 ? "Success" : "Failure",
            StartedAt = startedAt,
            CompletedAt = completedAt,
            DurationMs = Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds),
            WorkingDirectory = bootstrap.RuntimeOptions.WorkingDirectory ?? string.Empty,
            ConfigFilePath = bootstrap.RuntimeOptions.ConfigFilePath ?? string.Empty,
            ProfileName = bootstrap.RuntimeOptions.ProfileName,
            ApproveAll = options.ApproveAll,
            PermissionsJsonPath = options.PermissionsJsonPath,
            UserInputJsonPath = options.UserInputJsonPath,
            CollaborationMode = bootstrap.RuntimeOptions.CollaborationMode,
            RequestedResumeThreadId = bootstrap.RuntimeOptions.ResumeThreadId,
            ResumeLatestThread = bootstrap.RuntimeOptions.ResumeLatestThread,
            ResumeLatestMatchCwd = bootstrap.RuntimeOptions.ResumeLatestMatchCwd,
            AppHostProjectPath = bootstrap.AppHostProjectPath,
            ThreadId = threadId,
            TurnId = turnId,
            TurnStatus = turnStatus,
            OutputProtocol = options.OutputProtocol.ToString(),
            InitialMessage = options.InitialMessage,
            ScriptPath = script?.Path,
            CommandCount = executedInputs.Count,
            EventCount = eventList.Length,
            ProjectionRecordCount = projectionRecordList.Length,
            TranscriptRecordCount = typedTranscript.Length,
            FailureCount = failureCount,
            ResultText = resultText,
            FailureMessage = failureMessage,
        };

        var resolvedOptions = ProbeResolvedOptions.FromRuntimeOptions(
            bootstrap.RuntimeOptions,
            bootstrap.ResolvedConfig,
            bootstrap.AppHostProjectPath,
            options.ArtifactsRoot!,
            !string.IsNullOrWhiteSpace(options.ArtifactsRoot),
            options.ApproveAll,
            options.PermissionsJsonPath,
            options.UserInputJsonPath);
        var writer = new ChatCommandArtifactsWriter(jsonOptions);
        var artifactResult = await writer
            .WriteAsync(
                options.ArtifactsRoot!,
                summary,
                resolvedOptions,
                eventList,
                projectionRecordList,
                typedTranscript,
                commandsText,
                transcriptText,
                cancellationToken)
            .ConfigureAwait(false);
        summary.ArtifactsDirectory = artifactResult.RunDirectory;
        await writer.RewriteSummaryAsync(artifactResult.RunDirectory, summary, cancellationToken).ConfigureAwait(false);
    }
}
