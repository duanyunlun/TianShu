using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.Kernel;

/// <summary>
/// 会话执行请求工厂，负责将编排步骤与模型路由结果固化为 StageExecutionRequest。
/// Session execution request factory that pins an orchestration step and model route result into a StageExecutionRequest.
/// </summary>
public sealed class SessionExecutionRequestFactory
{
    /// <summary>
    /// 创建 Stage Executor 执行请求。
    /// Creates a stage executor execution request.
    /// </summary>
    public StageExecutionRequest Create(
        SessionOrchestrationStep step,
        ModelRouteResolutionResult modelRoute,
        StructuredValue? input = null,
        MetadataBag? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(modelRoute);

        return new StageExecutionRequest(
            BuildStableId("execution", step.Decision.DecisionId, step.Stage.Id),
            step.Stage,
            step.Decision,
            step.ContextPackage,
            modelRoute,
            input: input,
            metadata: metadata);
    }

    private static string BuildStableId(string prefix, string correlationId, string stageId)
        => $"{prefix}-{Normalize(correlationId) ?? "session"}-{stageId}";

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
