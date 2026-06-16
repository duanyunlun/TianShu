using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelSpawnAgentsOnCsvRuntimeHelpersTests
{
    [Fact]
    public void ParseAgentJobCsv_ShouldTrimBomAndPreserveQuotedContent()
    {
        var (headers, rows) = KernelSpawnAgentsOnCsvRuntimeHelpers.ParseAgentJobCsv(
            "\uFEFFid,name,notes\r\n1,Alpha,\"hello,\r\nworld\"\r\n");

        Assert.Equal(["id", "name", "notes"], headers);
        var row = Assert.Single(rows);
        Assert.Equal(["1", "Alpha", "hello,\r\nworld"], row);
    }

    [Fact]
    public void NormalizeSpawnAgentsOnCsvConcurrency_ShouldCapRequestedValueToThreadLimit()
    {
        var cappedRequested = KernelSpawnAgentsOnCsvRuntimeHelpers.NormalizeSpawnAgentsOnCsvConcurrency(16, 6);
        var cappedDefault = KernelSpawnAgentsOnCsvRuntimeHelpers.NormalizeSpawnAgentsOnCsvConcurrency(null, 6);

        Assert.Equal(6, cappedRequested);
        Assert.Equal(6, cappedDefault);
    }

    [Fact]
    public void BuildAgentJobWorkerPrompt_ShouldRenderInstructionRowAndPrettySchema()
    {
        var job = new KernelAgentJobRecord(
            Id: "job_001",
            Name: "job",
            Status: "running",
            Instruction: "Summarize {name} with {{escaped}} braces.",
            OutputSchemaJson: """{"type":"object","properties":{"summary":{"type":"string"}}}""",
            InputHeadersJson: """["name"]""",
            InputCsvPath: "input.csv",
            OutputCsvPath: "output.csv",
            AutoExport: true,
            MaxRuntimeSeconds: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            StartedAt: null,
            CompletedAt: null,
            LastError: null);
        var item = new KernelAgentJobItemRecord(
            JobId: "job_001",
            ItemId: "row-1",
            RowIndex: 1,
            SourceId: "src-1",
            RowJson: JsonSerializer.Serialize(new { name = "Alpha" }),
            Status: "pending",
            AssignedThreadId: null,
            AttemptCount: 0,
            ResultJson: null,
            LastError: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            ReportedAt: null);

        var prompt = KernelSpawnAgentsOnCsvRuntimeHelpers.BuildAgentJobWorkerPrompt(job, item);

        Assert.Contains("Summarize Alpha with {escaped} braces.", prompt, StringComparison.Ordinal);
        Assert.Contains("\"summary\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"string\"", prompt, StringComparison.Ordinal);
        Assert.Contains("report_agent_job_result", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveConfiguredAgentJobMaxRuntimeSeconds_ShouldOnlyHonorFormalKey()
    {
        var snakeCaseConfig = new Dictionary<string, object?>
        {
            ["agents"] = new Dictionary<string, object?>
            {
                ["job_max_runtime_seconds"] = 120,
            },
        };
        var legacyCamelCaseConfig = new Dictionary<string, object?>
        {
            ["agents"] = new Dictionary<string, object?>
            {
                ["jobMaxRuntimeSeconds"] = "240",
            },
        };

        var snakeCase = KernelSpawnAgentsOnCsvRuntimeHelpers.ResolveConfiguredAgentJobMaxRuntimeSeconds(snakeCaseConfig);
        var legacyCamelCase = KernelSpawnAgentsOnCsvRuntimeHelpers.ResolveConfiguredAgentJobMaxRuntimeSeconds(legacyCamelCaseConfig);

        Assert.Equal(120, snakeCase);
        Assert.Equal(60 * 30, legacyCamelCase);
    }
}
