using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelPendingInteractiveReplayHelpersTests
{
    [Theory]
    [InlineData("item/commandExecution/requestApproval", true)]
    [InlineData("item/tool/requestUserInput", true)]
    [InlineData("serverRequest/respond", false)]
    public void IsPendingInteractiveRequestMethod_ShouldMatchExpectedMethods(string method, bool expected)
    {
        var actual = KernelPendingInteractiveReplayHelpers.IsPendingInteractiveRequestMethod(method);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryReadPendingInteractiveCallId_WhenElicitationRequestWithoutApprovalId_ShouldUseElicitationId()
    {
        using var parameters = JsonDocument.Parse(
            """
            {
              "threadId": "thread_001",
              "elicitationId": "elicitation_001"
            }
            """);

        var callId = KernelPendingInteractiveReplayHelpers.TryReadPendingInteractiveCallId(
            "mcpServer/elicitation/request",
            parameters.RootElement,
            requestId: 42);

        Assert.Equal("elicitation_001", callId);
    }

    [Fact]
    public void BuildPendingInteractiveRequestPayload_WhenApprovalRequestUsesMetaDecisions_ShouldNormalizePayload()
    {
        using var parameters = JsonDocument.Parse(
            """
            {
              "threadId": "thread_approval",
              "turnId": "turn_approval",
              "serverName": "docs-mcp",
              "message": "需要安装工具",
              "_meta": {
                "codex_approval_kind": "tool_suggestion",
                "tool_name": "browser_search",
                "install_url": "https://example.test/install",
                "available_decisions": [
                  "approved",
                  { "type": "accept_for_session" },
                  { "decline": "no" }
                ]
              }
            }
            """);

        var payload = KernelPendingInteractiveReplayHelpers.BuildPendingInteractiveRequestPayload(
            requestId: 7,
            method: "mcpServer/elicitation/request",
            callId: "elicitation_007",
            threadId: "thread_approval",
            turnId: "turn_approval",
            parameters: parameters.RootElement);
        var element = JsonSerializer.SerializeToElement(payload);

        Assert.Equal("approval_requested", element.GetProperty("requestKind").GetString());
        Assert.Equal("tool_suggest", element.GetProperty("toolName").GetString());
        Assert.Equal("tool_suggestion", element.GetProperty("approvalKind").GetString());
        Assert.Equal("docs-mcp", element.GetProperty("serverName").GetString());
        Assert.Equal(
            ["accept", "acceptForSession", "decline"],
            element.GetProperty("availableDecisions").EnumerateArray().Select(static item => item.GetString()!).ToArray());

        var summary = element.GetProperty("text").GetString();
        Assert.Contains("需要安装工具", summary, StringComparison.Ordinal);
        Assert.Contains("tool=browser_search", summary, StringComparison.Ordinal);
        Assert.Contains("install_url=https://example.test/install", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPendingInteractiveRequestPayload_WhenPermissionRequest_ShouldPreservePermissionSummary()
    {
        using var parameters = JsonDocument.Parse(
            """
            {
              "threadId": "thread_permission",
              "turnId": "turn_permission",
              "reason": "需要网络权限",
              "permissions": {
                "network": {
                  "enabled": true
                }
              }
            }
            """);

        var payload = KernelPendingInteractiveReplayHelpers.BuildPendingInteractiveRequestPayload(
            requestId: 8,
            method: "item/permissions/requestApproval",
            callId: "permission_008",
            threadId: "thread_permission",
            turnId: "turn_permission",
            parameters: parameters.RootElement);
        var element = JsonSerializer.SerializeToElement(payload);

        Assert.Equal("permission_requested", element.GetProperty("requestKind").GetString());
        Assert.Equal("request_permissions", element.GetProperty("toolName").GetString());
        Assert.Equal("awaitingPermission", element.GetProperty("status").GetString());
        Assert.Contains("需要网络权限", element.GetProperty("text").GetString(), StringComparison.Ordinal);
        using var permissionsJson = JsonDocument.Parse(element.GetProperty("permissionRequest").GetProperty("permissionsJson").GetString()!);
        Assert.True(permissionsJson.RootElement.GetProperty("network").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void BuildPendingInteractiveRequestPayload_WhenUserInputRequest_ShouldBuildQuestionsAndSummary()
    {
        using var parameters = JsonDocument.Parse(
            """
            {
              "threadId": "thread_input",
              "turnId": "turn_input",
              "questions": [
                {
                  "id": "config_path",
                  "header": "配置文件",
                  "question": "请选择配置文件",
                  "isOther": true,
                  "isSecret": false,
                  "options": [
                    {
                      "label": "使用项目配置",
                      "description": "优先采用当前仓库配置"
                    }
                  ]
                }
              ]
            }
            """);

        var payload = KernelPendingInteractiveReplayHelpers.BuildPendingInteractiveRequestPayload(
            requestId: 9,
            method: "item/tool/requestUserInput",
            callId: "input_009",
            threadId: "thread_input",
            turnId: "turn_input",
            parameters: parameters.RootElement);
        var element = JsonSerializer.SerializeToElement(payload);

        Assert.Equal("request_user_input", element.GetProperty("requestKind").GetString());
        Assert.Equal("requestUserInput", element.GetProperty("toolName").GetString());
        Assert.Equal("- 配置文件", element.GetProperty("text").GetString());

        var question = Assert.Single(element.GetProperty("userInputRequest").GetProperty("questions").EnumerateArray().ToArray());
        Assert.Equal("config_path", question.GetProperty("id").GetString());
        Assert.Equal("配置文件", question.GetProperty("header").GetString());
        Assert.Equal("请选择配置文件", question.GetProperty("prompt").GetString());
        Assert.True(question.GetProperty("isOther").GetBoolean());
        Assert.False(question.GetProperty("isSecret").GetBoolean());

        var option = Assert.Single(question.GetProperty("options").EnumerateArray().ToArray());
        Assert.Equal("使用项目配置", option.GetProperty("label").GetString());
        Assert.Equal("优先采用当前仓库配置", option.GetProperty("description").GetString());
    }
}
