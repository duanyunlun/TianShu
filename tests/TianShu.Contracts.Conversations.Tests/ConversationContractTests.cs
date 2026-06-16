using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Conversations.Tests;

public sealed class ConversationContractTests
{
    [Fact]
    public void Thread_RejectsBlankTitle()
    {
        var collaboration = new CollaborationSpaceRef(
            new CollaborationSpaceId("space-thread"),
            "design",
            "Design");

        Assert.Throws<ArgumentException>(() => new Thread(
            new ThreadId("thread-001"),
            collaboration,
            " "));
    }

    [Fact]
    public void Turn_PreservesInteractionAndParticipant()
    {
        var collaboration = new CollaborationSpaceRef(
            new CollaborationSpaceId("space-turn"),
            "design",
            "Design");
        var participant = new ServiceParticipant(
            new ParticipantId("participant-turn"),
            "Coordinator",
            "owner");
        var envelope = new InteractionEnvelope(
            new InteractionEnvelopeId("interaction-turn"),
            new InteractionSource(InteractionSourceKind.Host, "cli"),
            new InteractionItem[]
            {
                new TextInteractionItem("继续"),
            });

        var turn = new Turn(
            new TurnId("turn-001"),
            new ThreadId("thread-001"),
            InteractionEnvelopeRef.From(envelope),
            ParticipantRef.From(participant),
            collaboration,
            TurnState.Running);

        Assert.Equal(TurnState.Running, turn.State);
        Assert.Equal("interaction-turn", turn.InteractionEnvelope.Id.Value);
        Assert.Equal("participant-turn", turn.InitiatedByParticipant.Id.Value);
    }

    [Fact]
    public void ControlPlaneSubmitTurnCommand_PreservesHistoryInputsAndNormalizedEnvelope()
    {
        var envelope = new InteractionEnvelope(
            new InteractionEnvelopeId("interaction-command"),
            new InteractionSource(InteractionSourceKind.Host, "cli"),
            [new TextInteractionItem("继续")]);
        var command = new ControlPlaneSubmitTurnCommand
        {
            Envelope = envelope,
            Inputs =
            [
                new ControlPlaneTextInput(
                    "继续",
                    [new ControlPlaneTextElement(new ControlPlaneByteRange(0, 2), "prompt")]),
            ],
            History =
            [
                new ControlPlaneConversationMessage
                {
                    Role = ControlPlaneConversationRole.Assistant,
                    Content = "上一轮回复",
                    ContentItems = [new ControlPlaneTextInput("上一轮回复")],
                    IsStreaming = true,
                },
            ],
        };

        var input = Assert.IsType<ControlPlaneTextInput>(Assert.Single(command.Inputs));
        var history = Assert.Single(command.History);
        Assert.Equal("interaction-command", command.Envelope?.Id.Value);
        Assert.Equal("cli", command.Envelope?.Source.Surface);
        Assert.Equal("继续", input.Text);
        Assert.Equal("prompt", Assert.Single(input.TextElements!).Placeholder);
        Assert.Equal(ControlPlaneConversationRole.Assistant, history.Role);
        Assert.True(history.IsStreaming);
    }

    [Fact]
    public void ControlPlaneThreadSnapshot_PreservesReplayState()
    {
        var snapshot = new ControlPlaneThreadSnapshot
        {
            Thread = new ControlPlaneThreadSummary
            {
                ThreadId = new ThreadId("thread-control-1"),
                Preview = "resume preview",
                UpdatedAt = new DateTimeOffset(2026, 4, 9, 1, 0, 0, TimeSpan.Zero),
            },
            PendingInputState = new ControlPlanePendingInputState(
                Entries:
                [
                    new ControlPlanePendingInputStateEntry(
                        "corr-1",
                        "Queue",
                        "Queue",
                        "awaiting_commit",
                        "turn-1",
                        null,
                        StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                        {
                            ["message"] = StructuredValue.FromString("follow-up"),
                        })),
                ],
                InterruptRequestPending: true),
            PendingInteractiveRequests =
            [
                new ControlPlanePendingInteractiveRequest
                {
                    RequestId = 7,
                    CallId = "call-1",
                    RequestKind = "approval_requested",
                    ApprovalKind = "shell_command",
                    AvailableDecisionOptions =
                    [
                        new ControlPlaneApprovalDecisionOption(
                            "acceptWithExecpolicyAmendment",
                            new ControlPlaneExecPolicyAmendment(["git", "status"])),
                    ],
                },
            ],
        };

        Assert.Equal("thread-control-1", snapshot.Thread.ThreadId.Value);
        var entry = Assert.Single(snapshot.PendingInputState!.Entries);
        Assert.Equal("follow-up", entry.CompareKey!.Properties["message"].StringValue);
        var request = Assert.Single(snapshot.PendingInteractiveRequests);
        Assert.Equal("shell_command", request.ApprovalKind);
        Assert.Equal(["git", "status"], request.AvailableDecisionOptions![0].ExecPolicyAmendment?.CommandPrefix);
    }

    [Fact]
    public void ControlPlaneThreadOperationResult_PreservesThreadDetail()
    {
        var result = new ControlPlaneThreadOperationResult
        {
            Thread = new ControlPlaneThreadDetail
            {
                ThreadId = new ThreadId("thread-detail-1"),
                Preview = "detail preview",
                UpdatedAt = new DateTimeOffset(2026, 4, 10, 1, 0, 0, TimeSpan.Zero),
                Turns =
                [
                    new ControlPlaneThreadTurn
                    {
                        Id = "turn-1",
                        Status = "completed",
                    },
                ],
                PendingInteractiveRequests =
                [
                    new ControlPlanePendingInteractiveRequest
                    {
                        RequestId = 9,
                        CallId = "call-9",
                        RequestKind = "approval_requested",
                    },
                ],
            },
        };

        Assert.NotNull(result.Thread);
        Assert.Equal("thread-detail-1", result.Thread.ThreadId.Value);
        Assert.Equal("turn-1", Assert.Single(result.Thread.Turns).Id);
        Assert.Equal("call-9", Assert.Single(result.Thread.PendingInteractiveRequests).CallId);
    }

    [Fact]
    public void ControlPlaneThreadManagementContracts_PreserveIdentifiersAndFlags()
    {
        var metadata = new ControlPlaneUpdateThreadMetadataCommand
        {
            ThreadId = new ThreadId("thread-meta-1"),
            HasGitSha = true,
            GitSha = "abc123",
            HasGitBranch = true,
            GitBranch = "main",
        };

        var loaded = new ControlPlaneLoadedThreadListResult
        {
            ThreadIds =
            [
                new ThreadId("thread-a"),
                new ThreadId("thread-b"),
            ],
            NextCursor = "cursor-1",
        };

        Assert.Equal("thread-meta-1", metadata.ThreadId.Value);
        Assert.True(metadata.HasGitSha);
        Assert.Equal("abc123", metadata.GitSha);
        Assert.Equal(["thread-a", "thread-b"], loaded.ThreadIds.Select(static item => item.Value).ToArray());
        Assert.Equal("cursor-1", loaded.NextCursor);
    }

    [Fact]
    public void ControlPlaneFuzzyFileSearchContracts_PreserveQuerySessionAndFiles()
    {
        var query = new ControlPlaneFuzzyFileSearchQuery
        {
            Query = "kernel",
            WorkingDirectory = "/repo",
            Limit = 8,
            Roots = ["/repo/src", "/repo/tests"],
        };
        var start = new ControlPlaneStartFuzzyFileSearchSessionCommand
        {
            SessionId = "session-1",
            Roots = ["/repo/src"],
        };
        var update = new ControlPlaneUpdateFuzzyFileSearchSessionCommand
        {
            SessionId = "session-1",
            Query = "program",
        };
        var stop = new ControlPlaneStopFuzzyFileSearchSessionCommand
        {
            SessionId = "session-1",
        };
        var result = new ControlPlaneFuzzyFileSearchResult
        {
            Files =
            [
                new ControlPlaneFuzzyFileSearchFile
                {
                    Path = "/repo/src/Program.cs",
                    FileName = "Program.cs",
                },
            ],
        };

        Assert.Equal("kernel", query.Query);
        Assert.Equal("/repo", query.WorkingDirectory);
        Assert.Equal(8, query.Limit);
        Assert.Equal(["/repo/src", "/repo/tests"], query.Roots);
        Assert.Equal("session-1", start.SessionId);
        Assert.Equal("/repo/src", Assert.Single(start.Roots));
        Assert.Equal("program", update.Query);
        Assert.Equal("session-1", stop.SessionId);
        var file = Assert.Single(result.Files);
        Assert.Equal("/repo/src/Program.cs", file.Path);
        Assert.Equal("Program.cs", file.FileName);
    }

    [Fact]
    public void ControlPlaneRealtimeContracts_PreserveThreadSessionAndPayload()
    {
        var start = new ControlPlaneRealtimeStartCommand
        {
            ThreadId = new ThreadId("thread-rt-1"),
            SessionId = "session-rt-1",
            Prompt = "start prompt",
        };
        var appendText = new ControlPlaneRealtimeAppendTextCommand
        {
            ThreadId = new ThreadId("thread-rt-1"),
            SessionId = "session-rt-1",
            Text = "hello realtime",
        };
        var appendAudio = new ControlPlaneRealtimeAppendAudioCommand
        {
            ThreadId = new ThreadId("thread-rt-1"),
            SessionId = "session-rt-1",
            Audio = new ControlPlaneRealtimeAudioInput
            {
                Data = "AQIDBA==",
                SampleRate = 24000,
                NumChannels = 1,
                SamplesPerChannel = 480,
            },
        };
        var handoff = new ControlPlaneRealtimeHandoffOutputCommand
        {
            ThreadId = new ThreadId("thread-rt-1"),
            SessionId = "session-rt-1",
            HandoffId = "call-rt-1",
            Output = "delegated result",
        };
        var stop = new ControlPlaneRealtimeStopCommand
        {
            ThreadId = new ThreadId("thread-rt-1"),
            SessionId = "session-rt-1",
        };
        var accepted = new ControlPlaneRealtimeCommandAcceptedResult();

        Assert.Equal("thread-rt-1", start.ThreadId.Value);
        Assert.Equal("session-rt-1", start.SessionId);
        Assert.Equal("start prompt", start.Prompt);
        Assert.Equal("hello realtime", appendText.Text);
        Assert.Equal("AQIDBA==", appendAudio.Audio.Data);
        Assert.Equal(24000, appendAudio.Audio.SampleRate);
        Assert.Equal(1, appendAudio.Audio.NumChannels);
        Assert.Equal(480, appendAudio.Audio.SamplesPerChannel);
        Assert.Equal("call-rt-1", handoff.HandoffId);
        Assert.Equal("delegated result", handoff.Output);
        Assert.Equal("thread-rt-1", stop.ThreadId.Value);
        Assert.NotNull(accepted);
    }

    [Fact]
    public void ControlPlaneConversationStreamEvent_PreservesTypedPayloadAndDiagnostics()
    {
        var streamEvent = new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ApprovalRequested,
            ThreadId = new ThreadId("thread-stream-1"),
            TurnId = new TurnId("turn-stream-1"),
            CallId = new CallId("call-stream-1"),
            ApprovalKind = "shell_command",
            AvailableDecisionOptions =
            [
                new ControlPlaneApprovalDecisionOption(
                    "acceptWithExecpolicyAmendment",
                    new ControlPlaneExecPolicyAmendment(["git", "status"])),
            ],
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ApprovalRequest,
            Payload = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["toolName"] = StructuredValue.FromString("shell"),
                ["message"] = StructuredValue.FromString("需要审批"),
            }),
            TurnError = new ControlPlaneThreadTurnError
            {
                Message = "approval blocked",
                AdditionalDetails = "needs explicit accept",
            },
            Diagnostics = new ControlPlaneConversationStreamDiagnostics
            {
                RawJson = "{\"kind\":\"approval_requested\"}",
            },
        };

        Assert.Equal(ControlPlaneConversationStreamEventKind.ApprovalRequested, streamEvent.Kind);
        Assert.Equal("thread-stream-1", streamEvent.ThreadId!.Value);
        Assert.Equal("turn-stream-1", streamEvent.TurnId!.Value);
        Assert.Equal("call-stream-1", streamEvent.CallId!.Value);
        Assert.Equal("shell", streamEvent.Payload!.Properties["toolName"].StringValue);
        Assert.Equal("approval blocked", streamEvent.TurnError!.Message);
        Assert.Equal("{\"kind\":\"approval_requested\"}", streamEvent.Diagnostics!.RawJson);
        Assert.Equal(["git", "status"], streamEvent.AvailableDecisionOptions![0].ExecPolicyAmendment!.CommandPrefix);
    }
}
