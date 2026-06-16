using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Tools.Collaboration;

/// <summary>
/// Collaboration / Multi-agent 工具域 Provider。
/// Provider for the Collaboration / Multi-agent tool domain.
/// </summary>
public sealed class CollaborationToolProvider : ITianShuToolProvider
{
    private static readonly IReadOnlyDictionary<string, ToolDescriptor> Descriptors =
        new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal)
        {
            [CollaborationToolNames.UpdatePlan] = CollaborationToolDescriptors.BuildDescriptor(
                CollaborationToolNames.UpdatePlan,
                "Update Plan",
                "Updates the task plan. Provide an optional explanation and a list of plan items, each with a step and status. At most one step can be in_progress at a time.",
                CollaborationToolSchemas.UpdatePlanInputSchema,
                capabilities: [new ToolCapability("plan-projection", "Update the host-owned task plan projection.")]),
            [CollaborationToolNames.SpawnAgent] = CollaborationToolDescriptors.BuildDescriptor(
                CollaborationToolNames.SpawnAgent,
                "Spawn Agent",
                "Spawn a sub-agent for a well-scoped task. Returns the agent id and optional nickname.",
                CollaborationToolSchemas.SpawnAgentInputSchema,
                outputSchema: CollaborationToolSchemas.SpawnAgentOutputSchema,
                capabilities: [new ToolCapability("agent-lifecycle", "Create a governed sub-agent under the current parent turn.")]),
            [CollaborationToolNames.SendInput] = CollaborationToolDescriptors.BuildDescriptor(
                CollaborationToolNames.SendInput,
                "Send Input",
                "Send input to an existing sub-agent. Returns a submission id.",
                CollaborationToolSchemas.SendInputInputSchema,
                outputSchema: CollaborationToolSchemas.SendInputOutputSchema,
                capabilities: [new ToolCapability("agent-io", "Send governed input to an existing sub-agent.")]),
            [CollaborationToolNames.ResumeAgent] = CollaborationToolDescriptors.BuildDescriptor(
                CollaborationToolNames.ResumeAgent,
                "Resume Agent",
                "Resume a previously closed agent by id so it can receive send_input and wait calls.",
                CollaborationToolSchemas.AgentIdInputSchema,
                outputSchema: CollaborationToolSchemas.AgentStatusOutputSchema,
                capabilities: [new ToolCapability("agent-lifecycle", "Resume a governed sub-agent session.")]),
            [CollaborationToolNames.Wait] = CollaborationToolDescriptors.BuildDescriptor(
                CollaborationToolNames.Wait,
                "Wait",
                "Wait for agents to reach a final status. Returns empty status when timed out.",
                CollaborationToolSchemas.WaitInputSchema,
                outputSchema: CollaborationToolSchemas.WaitOutputSchema,
                capabilities: [new ToolCapability("agent-wait", "Wait for one or more sub-agent statuses through the host lifecycle.")]),
            [CollaborationToolNames.CloseAgent] = CollaborationToolDescriptors.BuildDescriptor(
                CollaborationToolNames.CloseAgent,
                "Close Agent",
                "Close an agent when it is no longer needed and return its last known status.",
                CollaborationToolSchemas.AgentIdInputSchema,
                outputSchema: CollaborationToolSchemas.AgentStatusOutputSchema,
                capabilities: [new ToolCapability("agent-lifecycle", "Close a governed sub-agent session.")]),
        };

    public IReadOnlyList<ToolDescriptor> DescribeTools(TianShuToolRegistrationContext context)
    {
        _ = context;
        return Descriptors.Values.ToArray();
    }

    public ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context)
    {
        _ = context;
        return Descriptors.TryGetValue(toolKey, out var descriptor)
            ? new CollaborationToolHandler(descriptor)
            : throw new InvalidOperationException($"Unknown collaboration tool: {toolKey}");
    }
}

internal static class CollaborationToolNames
{
    public const string UpdatePlan = "update_plan";
    public const string SpawnAgent = "spawn_agent";
    public const string SendInput = "send_input";
    public const string ResumeAgent = "resume_agent";
    public const string Wait = "wait";
    public const string CloseAgent = "close_agent";
    public const string ImplementationId = "tianshu.tools.collaboration";
}

internal sealed class CollaborationToolHandler : ITianShuToolHandler
{
    public CollaborationToolHandler(ToolDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public ToolDescriptor Descriptor { get; }

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (context.CollaborationServices is null)
        {
            return CollaborationToolResultFactory.Failure(request, "collaboration services unavailable");
        }

        var result = await context.CollaborationServices
            .InvokeCollaborationToolAsync(new TianShuCollaborationToolRequest(request.ToolKey, request.Input), cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return CollaborationToolResultFactory.Failure(request, result.OutputText);
        }

        return CollaborationToolResultFactory.Success(
            request,
            result.StructuredOutput ?? StructuredValue.FromString(result.OutputText));
    }
}

internal static class CollaborationToolDescriptors
{
    public static ToolDescriptor BuildDescriptor(
        string name,
        string displayName,
        string description,
        JsonElement inputSchema,
        IReadOnlyList<ToolCapability> capabilities,
        JsonElement? outputSchema = null)
        => new(
            name,
            displayName,
            description,
            capabilities: capabilities,
            approvalRequirement: ToolApprovalRequirement.None,
            concurrencyClass: ToolConcurrencyClass.Sequential,
            implementationBinding: new ToolImplementationBinding(
                name,
                ToolImplementationKind.Managed,
                implementationId: CollaborationToolNames.ImplementationId),
            inputSchema: inputSchema,
            outputSchema: outputSchema);
}

internal static class CollaborationToolResultFactory
{
    public static ToolInvocationResult Success(ToolInvocationRequest request, StructuredValue payload)
        => new(
            request.CallId,
            request.ToolKey,
            [new ToolStreamItem("text", payload, isTerminal: true)]);

    public static ToolInvocationResult Failure(ToolInvocationRequest request, string message)
        => new(
            request.CallId,
            request.ToolKey,
            failure: new ToolInvocationFailure($"{request.ToolKey}.invalid_request", message));
}

internal static class CollaborationToolSchemas
{
    public static readonly JsonElement UpdatePlanInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            explanation = new { type = "string" },
            plan = new
            {
                type = "array",
                description = "The list of steps",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        step = new { type = "string" },
                        status = new { type = "string", description = "One of: pending, in_progress, completed" },
                    },
                    required = new[] { "step", "status" },
                    additionalProperties = false,
                },
            },
        },
        required = new[] { "plan" },
        additionalProperties = false,
    });

    public static readonly JsonElement SpawnAgentInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            message = new { type = "string", description = "Initial plain-text task for the new agent. Use either message or items." },
            items = BuildCollabInputItemsArraySchema(),
            agent_type = new { type = "string", description = "Optional sub-agent role name." },
            fork_context = new { type = "boolean", description = "When true, fork the current thread history into the new agent before sending the initial prompt." },
            model = new { type = "string", description = "Optional model override for the new agent. Replaces the inherited model." },
            reasoning_effort = new { type = "string", description = "Optional reasoning effort override for the new agent. Replaces the inherited reasoning effort." },
        },
        additionalProperties = false,
    });

    public static readonly JsonElement SpawnAgentOutputSchema = JsonSerializer.SerializeToElement(new
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

    public static readonly JsonElement SendInputInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string", description = "Agent id to message (from spawn_agent)." },
            message = new { type = "string", description = "Legacy plain-text message to send to the agent. Use either message or items." },
            items = BuildCollabInputItemsArraySchema(),
            interrupt = new { type = "boolean", description = "When true, stop the agent's current task and handle this immediately." },
        },
        required = new[] { "id" },
        additionalProperties = false,
    });

    public static readonly JsonElement SendInputOutputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            submission_id = new { type = "string" },
        },
        required = new[] { "submission_id" },
        additionalProperties = false,
    });

    public static readonly JsonElement AgentIdInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string", description = "Agent id." },
        },
        required = new[] { "id" },
        additionalProperties = false,
    });

    public static readonly JsonElement AgentStatusOutputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            status = BuildCollaborationAgentStatusSchema(),
        },
        required = new[] { "status" },
        additionalProperties = false,
    });

    public static readonly JsonElement WaitInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            ids = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Agent ids to wait on. Pass multiple ids to wait for whichever finishes first.",
            },
            timeout_ms = new { type = "number", description = "Optional timeout in milliseconds." },
        },
        required = new[] { "ids" },
        additionalProperties = false,
    });

    public static readonly JsonElement WaitOutputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            status = new
            {
                type = "object",
                additionalProperties = BuildCollaborationAgentStatusSchema(),
            },
            timed_out = new { type = "boolean" },
        },
        required = new[] { "status", "timed_out" },
        additionalProperties = false,
    });

    private static object BuildCollabInputItemsArraySchema()
        => new
        {
            type = "array",
            items = new
            {
                type = "object",
                properties = new
                {
                    type = new { type = "string" },
                    text = new { type = "string" },
                    name = new { type = "string" },
                    path = new { type = "string" },
                    image_url = new { type = "string" },
                    imageUrl = new { type = "string" },
                },
                additionalProperties = false,
            },
        };

    private static object BuildCollaborationAgentStatusSchema()
        => new
        {
            oneOf = new object[]
            {
                new
                {
                    type = "string",
                    @enum = new[] { "pending_init", "running", "shutdown", "not_found" },
                },
                new
                {
                    type = "object",
                    properties = new
                    {
                        completed = new
                        {
                            oneOf = new object[]
                            {
                                new { type = "string" },
                                new { type = "null" },
                            },
                        },
                    },
                    required = new[] { "completed" },
                    additionalProperties = false,
                },
                new
                {
                    type = "object",
                    properties = new
                    {
                        errored = new { type = "string" },
                    },
                    required = new[] { "errored" },
                    additionalProperties = false,
                },
            },
        };
}
