using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Tools.Interaction;

/// <summary>
/// Interaction / Governance 工具域 Provider。
/// Provider for the Interaction / Governance tool domain.
/// </summary>
public sealed class InteractionToolProvider : ITianShuToolProvider
{
    private static readonly IReadOnlyDictionary<string, ToolDescriptor> Descriptors =
        new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal)
        {
            [InteractionToolNames.RequestUserInput] = InteractionToolDescriptors.BuildDescriptor(
                InteractionToolNames.RequestUserInput,
                "Request User Input",
                "Request user input for one to three short questions and wait for the response. Availability depends on the current collaboration mode and runtime feature gates.",
                InteractionToolSchemas.RequestUserInputSchema,
                [new ToolCapability("user-input", "Pause the turn and request structured user input through the host.")]),
            [InteractionToolNames.RequestPermissions] = InteractionToolDescriptors.BuildDescriptor(
                InteractionToolNames.RequestPermissions,
                "Request Permissions",
                "Request additional filesystem or network permissions from the user and wait for a granted subset that can be reused later in the current turn or session.",
                InteractionToolSchemas.RequestPermissionsSchema,
                [new ToolCapability("permission-request", "Pause the turn and request additional host-governed permissions.")]),
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
            ? new InteractionToolHandler(descriptor)
            : throw new InvalidOperationException($"Unknown interaction tool: {toolKey}");
    }
}

internal static class InteractionToolNames
{
    public const string RequestUserInput = "request_user_input";
    public const string RequestPermissions = "request_permissions";
    public const string ImplementationId = "tianshu.tools.interaction";
}

internal sealed class InteractionToolHandler : ITianShuToolHandler
{
    public InteractionToolHandler(ToolDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public ToolDescriptor Descriptor { get; }

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (context.InteractionServices is null)
        {
            return InteractionToolResultFactory.Failure(request, "interaction services unavailable");
        }

        var result = await context.InteractionServices
            .InvokeInteractionToolAsync(new TianShuInteractionToolRequest(request.ToolKey, request.Input), cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return InteractionToolResultFactory.Failure(request, result.OutputText);
        }

        return InteractionToolResultFactory.Success(
            request,
            result.StructuredOutput ?? StructuredValue.FromString(result.OutputText));
    }
}

internal static class InteractionToolDescriptors
{
    public static ToolDescriptor BuildDescriptor(
        string name,
        string displayName,
        string description,
        JsonElement inputSchema,
        IReadOnlyList<ToolCapability> capabilities)
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
                implementationId: InteractionToolNames.ImplementationId),
            inputSchema: inputSchema);
}

internal static class InteractionToolResultFactory
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

internal static class InteractionToolSchemas
{
    public static readonly JsonElement RequestUserInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            questions = new
            {
                type = "array",
                description = "Questions to show the user. Prefer 1 and do not exceed 3",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Stable identifier for mapping answers (snake_case)." },
                        header = new { type = "string", description = "Short header label shown in the UI (12 or fewer chars)." },
                        question = new { type = "string", description = "Single-sentence prompt shown to the user." },
                        options = new
                        {
                            type = "array",
                            description = "Provide 2-3 mutually exclusive choices. Put the recommended option first and suffix its label with \"(Recommended)\". Do not include an \"Other\" option in this list; the client will add a free-form \"Other\" option automatically.",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    label = new { type = "string", description = "User-facing label (1-5 words)." },
                                    description = new { type = "string", description = "One short sentence explaining impact/tradeoff if selected." },
                                },
                                required = new[] { "label", "description" },
                                additionalProperties = false,
                            },
                        },
                    },
                    required = new[] { "id", "header", "question", "options" },
                    additionalProperties = false,
                },
            },
        },
        required = new[] { "questions" },
        additionalProperties = false,
    });

    public static readonly JsonElement RequestPermissionsSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            reason = new
            {
                type = "string",
                description = "Why the tool needs these permissions.",
            },
            permissions = new
            {
                type = "object",
                properties = new
                {
                    network = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            enabled = new
                            {
                                type = "boolean",
                                description = "Set to true to request network access.",
                            },
                        },
                    },
                    file_system = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            read = new
                            {
                                type = "array",
                                items = new { type = "string" },
                                description = "Additional readable roots for this turn or session.",
                            },
                            write = new
                            {
                                type = "array",
                                items = new { type = "string" },
                                description = "Additional writable roots for this turn or session.",
                            },
                        },
                    },
                },
                additionalProperties = false,
            },
        },
        required = new[] { "permissions" },
        additionalProperties = false,
    });
}
