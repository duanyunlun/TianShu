using System.Text.Json.Serialization;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Kernel;

/// <summary>
/// 核心意图标识，用于追踪 Control Plane 交给 Kernel 的归一化请求。
/// Core-intent identifier used to trace normalized requests passed from Control Plane to Kernel.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<CoreIntentId>))]
public readonly record struct CoreIntentId
{
    public CoreIntentId(string value) => Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(CoreIntentId value) => value.Value;
}

/// <summary>
/// Kernel 运行标识。
/// Kernel-run identifier.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<KernelRunId>))]
public readonly record struct KernelRunId
{
    public KernelRunId(string value) => Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(KernelRunId value) => value.Value;
}

/// <summary>
/// StageGraph 标识。
/// StageGraph identifier.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<StageGraphId>))]
public readonly record struct StageGraphId
{
    public StageGraphId(string value) => Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(StageGraphId value) => value.Value;
}

/// <summary>
/// Stage 标识。
/// Stage identifier.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<StageId>))]
public readonly record struct StageId
{
    public StageId(string value) => Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(StageId value) => value.Value;
}

/// <summary>
/// Stage 边标识。
/// Stage-edge identifier.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<StageEdgeId>))]
public readonly record struct StageEdgeId
{
    public StageEdgeId(string value) => Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(StageEdgeId value) => value.Value;
}

/// <summary>
/// Kernel proposal 标识。
/// Kernel-proposal identifier.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<KernelProposalId>))]
public readonly record struct KernelProposalId
{
    public KernelProposalId(string value) => Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(KernelProposalId value) => value.Value;
}

/// <summary>
/// Kernel operation 标识。
/// Kernel-operation identifier.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<KernelOperationId>))]
public readonly record struct KernelOperationId
{
    public KernelOperationId(string value) => Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(KernelOperationId value) => value.Value;
}

/// <summary>
/// Kernel trace 标识。
/// Kernel-trace identifier.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<KernelTraceId>))]
public readonly record struct KernelTraceId
{
    public KernelTraceId(string value) => Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(KernelTraceId value) => value.Value;
}

/// <summary>
/// Kernel strategy 标识。
/// Kernel-strategy identifier.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<StrategyId>))]
public readonly record struct StrategyId
{
    public StrategyId(string value) => Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(StrategyId value) => value.Value;
}
