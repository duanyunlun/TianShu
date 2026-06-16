using TianShu.Contracts.Provider;
using TianShu.Provider.Abstractions;
using TianShu.Provider.OpenAI;

namespace TianShu.Provider.OpenAI.Tests;

public sealed class OpenAiProviderNotificationInterpreterTests
{
    [Fact]
    public void InterpretItem_WhenAssistantDeltaIsWhitespace_PreservesTextDelta()
    {
        var projection = new OpenAiProviderNotificationInterpreter().InterpretItem(
            new ProviderItemNotification(
                Method: "item/delta",
                ThreadId: "thread-1",
                TurnId: "turn-1",
                ItemId: "msg-1",
                Type: "assistant_text",
                Status: null,
                Phase: null,
                ToolName: null,
                Name: null,
                CallId: null,
                ToolCallId: null,
                Delta: " ",
                Output: null,
                Arguments: null,
                Input: null,
                RequiresApproval: null,
                ApprovalRequired: null,
                ApprovalStateRequired: null,
                Message: null,
                SummaryIndex: null,
                ContentIndex: null,
                ProcessId: null,
                Stdin: null,
                Item: new ProviderNotificationItem(
                    Id: "msg-1",
                    Type: "assistant_text",
                    Status: null,
                    Phase: null,
                    Name: null,
                    ToolName: null,
                    CallId: null,
                    Text: null,
                    OutputText: null,
                    Delta: " ",
                    Output: null,
                    Arguments: null,
                    Input: null)));

        Assert.NotNull(projection);
        var providerEvent = Assert.Single(projection.CreateEvents());
        var textDelta = Assert.IsType<ProviderTextDeltaEvent>(providerEvent);
        Assert.Equal(" ", textDelta.TextDelta);
    }
}
