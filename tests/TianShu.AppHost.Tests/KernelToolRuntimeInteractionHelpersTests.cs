using System.Text.Json;
using TianShu.AppHost.Tools;
using TianShu.AppHost;
using TianShu.AppHost.Catalog;
using TianShu.AppHost.Configuration;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelToolRuntimeInteractionHelpersTests
{
    [Theory]
    [InlineData("in_progress", "inProgress")]
    [InlineData(" pending ", "pending")]
    [InlineData("completed", "completed")]
    [InlineData("custom", "custom")]
    public void NormalizePlanStatus_ShouldNormalizeKnownStatuses(string input, string expected)
    {
        var actual = KernelToolRuntimeInteractionHelpers.NormalizePlanStatus(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseRequestUserInputResponse_WhenPayloadInvalid_ShouldReturnEmptyAnswers()
    {
        using var response = JsonDocument.Parse(
            """
            {
              "invalid": true
            }
            """);

        var parsed = KernelToolRuntimeInteractionHelpers.ParseRequestUserInputResponse(response.RootElement);

        Assert.Empty(parsed.Answers);
    }

    [Fact]
    public void BuildCollabPrompt_WhenMessageMissing_ShouldUseItemsPreview()
    {
        var items = new[]
        {
            new KernelCollabInputItem("text", "分析当前实现", null, null, null),
            new KernelCollabInputItem("mention", null, "search-specialist", "app://search", null),
        };

        var prompt = KernelToolRuntimeInteractionHelpers.BuildCollabPrompt(null, items);

        Assert.Contains("分析当前实现", prompt, StringComparison.Ordinal);
        Assert.Contains("search-specialist", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCollabTurnInputItems_ShouldConvertAndFilterEmptyItems()
    {
        var items = new[]
        {
            new KernelCollabInputItem("text", "  继续  ", null, null, null),
            new KernelCollabInputItem("local_image", null, null, "  C:\\temp\\a.png  ", null),
            new KernelCollabInputItem("text", "   ", null, null, null),
        };

        var converted = KernelToolRuntimeInteractionHelpers.BuildCollabTurnInputItems(items);

        Assert.NotNull(converted);
        Assert.Collection(
            converted!,
            first =>
            {
                Assert.Equal("text", first.Type);
                Assert.Equal("继续", first.Text);
            },
            second =>
            {
                Assert.Equal("local_image", second.Type);
                Assert.Equal("C:\\temp\\a.png", second.Path);
            });
    }

    [Fact]
    public void NormalizeSpawnAgentRequestedModelAndReasoning_WhenReasoningMissing_ShouldUseCatalogDefault()
    {
        Assert.True(KernelCatalogSurfaceUtilities.TryGetBuiltInModel("gpt-5.2", out var descriptor));

        var actual = KernelToolRuntimeInteractionHelpers.NormalizeSpawnAgentRequestedModelAndReasoning(
            currentModel: "gpt-5.1-codex-mini",
            requestedModel: "gpt-5.2",
            requestedReasoningEffort: null);

        Assert.Equal(descriptor!.Model, actual.Model);
        Assert.Equal(descriptor.DefaultReasoningEffort, actual.ReasoningEffort);
    }

    [Fact]
    public void NormalizeSpawnAgentRequestedModelAndReasoning_WhenModelUnknown_ShouldUseCatalogModelListInError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => KernelToolRuntimeInteractionHelpers.NormalizeSpawnAgentRequestedModelAndReasoning(
            currentModel: "gpt-5",
            requestedModel: "gpt-5.4-mini",
            requestedReasoningEffort: null));

        var available = string.Join(", ", KernelCatalogSurfaceUtilities.GetBuiltInModelNames());
        Assert.Equal($"Unknown model `gpt-5.4-mini` for spawn_agent. Available models: {available}", ex.Message);
    }

    [Fact]
    public void BuildForkedSpawnAgentFunctionCallItems_ShouldKeepCallIdAndPayloadShape()
    {
        var request = new KernelSpawnAgentRequest(
            Message: "继续处理",
            Items:
            [
                new KernelCollabInputItem("text", "补充上下文", null, null, null),
            ],
            AgentType: "worker",
            ForkContext: true,
            Model: "gpt-5.2",
            ReasoningEffort: "high",
            ParentCallId: "call_parent_001");

        var functionCall = KernelToolRuntimeInteractionHelpers.BuildForkedSpawnAgentFunctionCallItem("call_parent_001", request);
        var functionCallOutput = KernelToolRuntimeInteractionHelpers.BuildForkedSpawnAgentFunctionCallOutputItem("call_parent_001");
        var items = new[]
        {
            new KernelTurnItemRecord { Id = "fc", Type = "function_call", Payload = functionCall },
            new KernelTurnItemRecord { Id = "fco", Type = "function_call_output", Payload = functionCallOutput },
        };

        Assert.Equal("function_call", functionCall.GetProperty("type").GetString());
        Assert.Equal("spawn_agent", functionCall.GetProperty("name").GetString());
        Assert.Equal("call_parent_001", functionCall.GetProperty("call_id").GetString());
        Assert.Equal("function_call_output", functionCallOutput.GetProperty("type").GetString());
        Assert.Equal("call_parent_001", functionCallOutput.GetProperty("call_id").GetString());
        Assert.True(KernelToolRuntimeInteractionHelpers.ContainsRawResponseTurnItem(items, "function_call", "call_parent_001"));
        Assert.True(KernelToolRuntimeInteractionHelpers.ContainsRawResponseTurnItem(items, "function_call_output", "call_parent_001"));
    }

    [Fact]
    public void CloneTurnRecordForResponse_ShouldDeepCloneItemsAndError()
    {
        var source = new KernelTurnRecord
        {
            Id = "turn_001",
            Status = "inProgress",
            UserMessage = "user",
            AssistantMessage = "assistant",
            IsContextCompaction = true,
            Items =
            [
                new KernelTurnItemRecord
                {
                    Id = "item_001",
                    Type = "reasoning",
                    Payload = JsonSerializer.SerializeToElement(new { type = "reasoning", text = "step" }),
                },
            ],
            Error = new KernelTurnErrorRecord
            {
                Message = "failed",
                AdditionalDetails = "details",
            },
        };

        var cloned = KernelToolRuntimeInteractionHelpers.CloneTurnRecordForResponse(source);

        Assert.NotSame(source, cloned);
        Assert.NotSame(source.Items, cloned.Items);
        Assert.NotSame(source.Items[0], cloned.Items[0]);
        Assert.Equal("reasoning", cloned.Items[0].Payload.GetProperty("type").GetString());
        Assert.NotSame(source.Error, cloned.Error);
        Assert.Equal("failed", cloned.Error!.Message);
        Assert.True(cloned.IsContextCompaction);
    }

    [Fact]
    public void InjectForkedSpawnAgentToolItems_ShouldCloneAndAppendMissingItems()
    {
        var source = new KernelTurnRecord
        {
            Id = "turn_parent",
            Items =
            [
                new KernelTurnItemRecord
                {
                    Id = "existing",
                    Type = "assistant_message",
                    Payload = JsonSerializer.SerializeToElement(new { type = "output_text", text = "hi" }),
                },
            ],
        };
        var request = new KernelSpawnAgentRequest(
            Message: "继续处理",
            Items: null,
            AgentType: "worker",
            ForkContext: true,
            Model: "gpt-5.2",
            ReasoningEffort: "high",
            ParentCallId: " call_parent_002 ");

        var injected = KernelToolRuntimeInteractionHelpers.InjectForkedSpawnAgentToolItems(source, request.ParentCallId, request);

        Assert.NotSame(source, injected);
        Assert.Equal(3, injected!.Items.Count);
        Assert.True(KernelToolRuntimeInteractionHelpers.ContainsRawResponseTurnItem(injected.Items, "function_call", "call_parent_002"));
        Assert.True(KernelToolRuntimeInteractionHelpers.ContainsRawResponseTurnItem(injected.Items, "function_call_output", "call_parent_002"));
    }

    [Fact]
    public void InjectForkedSpawnAgentToolItems_ShouldAvoidDuplicatingExistingItems()
    {
        var request = new KernelSpawnAgentRequest(
            Message: "继续处理",
            Items: null,
            AgentType: "worker",
            ForkContext: true,
            Model: "gpt-5.2",
            ReasoningEffort: "high",
            ParentCallId: "call_parent_003");
        var source = new KernelTurnRecord
        {
            Id = "turn_parent",
            Items =
            [
                new KernelTurnItemRecord
                {
                    Id = "existing_fc",
                    Type = "function_call",
                    Payload = KernelToolRuntimeInteractionHelpers.BuildForkedSpawnAgentFunctionCallItem("call_parent_003", request),
                },
                new KernelTurnItemRecord
                {
                    Id = "existing_fco",
                    Type = "function_call_output",
                    Payload = KernelToolRuntimeInteractionHelpers.BuildForkedSpawnAgentFunctionCallOutputItem("call_parent_003"),
                },
            ],
        };

        var injected = KernelToolRuntimeInteractionHelpers.InjectForkedSpawnAgentToolItems(source, request.ParentCallId, request);

        Assert.NotSame(source, injected);
        Assert.Equal(2, injected!.Items.Count);
    }
}
