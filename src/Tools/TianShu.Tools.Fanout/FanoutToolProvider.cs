using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Tools.Fanout;

/// <summary>
/// Fanout Jobs 工具域 Provider。
/// Provider for the Fanout Jobs tool domain.
/// </summary>
public sealed class FanoutToolProvider : ITianShuToolProvider
{
    private static readonly IReadOnlyDictionary<string, ToolDescriptor> Descriptors =
        new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal)
        {
            [FanoutToolNames.SpawnAgentsOnCsv] = FanoutToolDescriptors.BuildDescriptor(
                FanoutToolNames.SpawnAgentsOnCsv,
                "Spawn Agents On CSV",
                "Process a CSV by spawning one worker sub-agent per row. The instruction string is a template where `{column}` placeholders are replaced with row values. Each worker must call `report_agent_job_result` with a JSON object (matching `output_schema` when provided); missing reports are treated as failures. This call blocks until all rows finish and automatically exports results to `output_csv_path` (or a default path).",
                FanoutToolSchemas.SpawnAgentsOnCsvInputSchema,
                capabilities: [new ToolCapability("fanout-job", "Run a governed CSV fan-out job through host-owned orchestration.")]),
            [FanoutToolNames.ReportAgentJobResult] = FanoutToolDescriptors.BuildDescriptor(
                FanoutToolNames.ReportAgentJobResult,
                "Report Agent Job Result",
                "Worker-only tool to report a result for an agent job item. Main agents should not call this.",
                FanoutToolSchemas.ReportAgentJobResultInputSchema,
                capabilities: [new ToolCapability("fanout-report", "Report a governed fan-out job item result to the host.")]),
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
            ? new FanoutToolHandler(descriptor)
            : throw new InvalidOperationException($"Unknown fanout tool: {toolKey}");
    }
}

internal static class FanoutToolNames
{
    public const string SpawnAgentsOnCsv = "spawn_agents_on_csv";
    public const string ReportAgentJobResult = "report_agent_job_result";
    public const string ImplementationId = "tianshu.tools.fanout";
}

internal sealed class FanoutToolHandler : ITianShuToolHandler
{
    public FanoutToolHandler(ToolDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public ToolDescriptor Descriptor { get; }

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (context.FanoutServices is null)
        {
            return FanoutToolResultFactory.Failure(request, "fanout services unavailable");
        }

        var result = await context.FanoutServices
            .InvokeFanoutToolAsync(new TianShuFanoutToolRequest(request.ToolKey, request.Input), cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return FanoutToolResultFactory.Failure(request, result.OutputText);
        }

        return FanoutToolResultFactory.Success(
            request,
            result.StructuredOutput ?? StructuredValue.FromString(result.OutputText));
    }
}

internal static class FanoutToolDescriptors
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
                implementationId: FanoutToolNames.ImplementationId),
            inputSchema: inputSchema);
}

internal static class FanoutToolResultFactory
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

internal static class FanoutToolSchemas
{
    public static readonly JsonElement SpawnAgentsOnCsvInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            csv_path = new { type = "string", description = "Path to the CSV file containing input rows." },
            instruction = new { type = "string", description = "Instruction template to apply to each CSV row. Use {column_name} placeholders to inject values from the row." },
            id_column = new { type = "string", description = "Optional column name to use as stable item id." },
            output_csv_path = new { type = "string", description = "Optional output CSV path for exported results." },
            max_concurrency = new { type = "number", description = "Maximum concurrent workers for this job. Defaults to 16 and is capped by config." },
            max_workers = new { type = "number", description = "Alias for max_concurrency. Set to 1 to run sequentially." },
            max_runtime_seconds = new { type = "number", description = "Maximum runtime per worker before it is failed. Defaults to agents.job_max_runtime_seconds when configured, otherwise 1800 seconds." },
            output_schema = new { type = "object", description = "Optional JSON schema that worker result objects should match." },
        },
        required = new[] { "csv_path", "instruction" },
        additionalProperties = false,
    });

    public static readonly JsonElement ReportAgentJobResultInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            job_id = new { type = "string", description = "Identifier of the job." },
            item_id = new { type = "string", description = "Identifier of the job item." },
            result = new { type = "object" },
            stop = new { type = "boolean", description = "Optional. When true, cancels the remaining job items after this result is recorded." },
        },
        required = new[] { "job_id", "item_id", "result" },
        additionalProperties = false,
    });
}
