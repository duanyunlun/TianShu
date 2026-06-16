using System.Text.Json;
using System.Text.Json.Nodes;
using TianShu.AppHost.Tools;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Collaboration 工具域在 Runtime 内保留的协议支撑，不承载工具执行实现。
/// Runtime protocol support for the Collaboration tool domain without owning tool execution.
/// </summary>
internal static class KernelCollaborationRuntimeSupport
{
    private static readonly JsonElement SpawnAgentOutputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            agent_id = new { type = "string" },
            nickname = new
            {
                oneOf = new object[]
                {
                    new { type = "string" },
                    new { type = "null" },
                },
            },
        },
        required = new[] { "agent_id", "nickname" },
        additionalProperties = false,
    });

    private static readonly JsonElement SpawnAgentInputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            message = new { type = "string", description = "Initial plain-text task for the new agent. Use either message or items." },
            items = ToolSchemaHelpers.BuildCollabInputItemsArraySchema(),
            agent_type = new { type = "string", description = "Optional sub-agent role name." },
            fork_context = new { type = "boolean", description = "When true, fork the current thread history into the new agent before sending the initial prompt." },
            model = new { type = "string", description = "Optional model override for the new agent. Replaces the inherited model." },
            reasoning_effort = new { type = "string", description = "Optional reasoning effort override for the new agent. Replaces the inherited reasoning effort." },
        },
        additionalProperties = false,
    });

    public static ProviderResponsesToolDefinition BuildSpawnAgentProviderToolDefinition(string? agentTypeDescription)
    {
        var parametersNode = JsonNode.Parse(SpawnAgentInputSchemaElement.GetRawText())?.AsObject() ?? new JsonObject();
        if (!string.IsNullOrWhiteSpace(agentTypeDescription)
            && parametersNode["properties"] is JsonObject properties
            && properties["agent_type"] is JsonObject agentTypeProperty)
        {
            agentTypeProperty["description"] = agentTypeDescription;
        }

        return new ProviderResponsesFunctionToolDefinition(
            "spawn_agent",
            "Spawn a sub-agent for a well-scoped task. Returns the agent id and optional nickname.",
            JsonSerializer.SerializeToElement(parametersNode),
            SpawnAgentOutputSchemaElement,
            strict: false);
    }
}
