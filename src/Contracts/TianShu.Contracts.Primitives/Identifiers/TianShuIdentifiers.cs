using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.Contracts.Primitives;

/// <summary>
/// 统一的字符串标识校验器，用于保证所有 Contracts 标识类型遵循一致的非空约束。
/// Unified string identifier guard that enforces the same non-empty invariant for all contract identifiers.
/// </summary>
public static class IdentifierGuard
{
    /// <summary>
    /// 校验字符串标识不能为空白，并返回规范化后的值。
    /// Validates that an identifier is not blank and returns the normalized value.
    /// </summary>
    public static string AgainstNullOrWhiteSpace(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("值不能为空。", paramName);
        }

        return value.Trim();
    }
}

/// <summary>
/// 会话标识，表示一个工作时段级别的控制平面边界。
/// Session identifier that represents a work-session boundary in the control plane.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<SessionId>))]
public readonly record struct SessionId
{
    public SessionId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(SessionId value) => value.Value;
}

/// <summary>
/// 线程标识，表示一条可恢复、可分叉的对话轨道。
/// Thread identifier that represents a resumable and forkable conversation lane.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<ThreadId>))]
public readonly record struct ThreadId
{
    public ThreadId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(ThreadId value) => value.Value;
}

/// <summary>
/// 轮次标识，表示线程中的一次执行或交互推进。
/// Turn identifier that represents a single execution or interaction advancement within a thread.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<TurnId>))]
public readonly record struct TurnId
{
    public TurnId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(TurnId value) => value.Value;
}

/// <summary>
/// 工作流标识，表示长期编排对象。
/// Workflow identifier that represents a long-lived orchestration object.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<WorkflowId>))]
public readonly record struct WorkflowId
{
    public WorkflowId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(WorkflowId value) => value.Value;
}

/// <summary>
/// 任务标识，表示工作流中的一个任务单元。
/// Task identifier that represents a task unit inside a workflow.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<TaskId>))]
public readonly record struct TaskId
{
    public TaskId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(TaskId value) => value.Value;
}

/// <summary>
/// 作业标识，表示执行平面中的一个作业实例。
/// Job identifier that represents a job instance in the execution plane.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<JobId>))]
public readonly record struct JobId
{
    public JobId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(JobId value) => value.Value;
}

/// <summary>
/// 作业项标识，表示作业内部的更细粒度条目。
/// Job-item identifier that represents a finer-grained entry within a job.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<JobItemId>))]
public readonly record struct JobItemId
{
    public JobItemId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(JobItemId value) => value.Value;
}

/// <summary>
/// 代理标识，表示一个正式的执行主体。
/// Agent identifier that represents a formal execution actor.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<AgentId>))]
public readonly record struct AgentId
{
    public AgentId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(AgentId value) => value.Value;
}

/// <summary>
/// 产物标识，表示可索引、可引用、可回放的产物对象。
/// Artifact identifier that represents an indexable, referable, replayable artifact.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<ArtifactId>))]
public readonly record struct ArtifactId
{
    public ArtifactId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(ArtifactId value) => value.Value;
}

/// <summary>
/// 账户标识，表示 TianShu 自身身份平面中的账户主体。
/// Account identifier that represents an account in TianShu's identity plane.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<AccountId>))]
public readonly record struct AccountId
{
    public AccountId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(AccountId value) => value.Value;
}

/// <summary>
/// 协作空间标识，表示高于 Session 的长期协作域。
/// Collaboration-space identifier that represents a long-lived collaboration scope above sessions.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<CollaborationSpaceId>))]
public readonly record struct CollaborationSpaceId
{
    public CollaborationSpaceId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(CollaborationSpaceId value) => value.Value;
}

/// <summary>
/// 交互包络标识，表示一次归一化后的 northbound 输入。
/// Interaction-envelope identifier that represents a normalized northbound input unit.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<InteractionEnvelopeId>))]
public readonly record struct InteractionEnvelopeId
{
    public InteractionEnvelopeId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(InteractionEnvelopeId value) => value.Value;
}

/// <summary>
/// 参与者标识，表示统一参与主体模型中的实例。
/// Participant identifier that represents an instance in the unified participant model.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<ParticipantId>))]
public readonly record struct ParticipantId
{
    public ParticipantId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(ParticipantId value) => value.Value;
}

/// <summary>
/// 调用标识，表示一次工具或治理相关调用链路。
/// Call identifier that represents a tool or governance-related invocation chain.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<CallId>))]
public readonly record struct CallId
{
    public CallId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(CallId value) => value.Value;
}

/// <summary>
/// 执行标识，表示一次执行平面的正式运行实例。
/// Execution identifier that represents a formal runtime instance in the execution plane.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<ExecutionId>))]
public readonly record struct ExecutionId
{
    public ExecutionId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(ExecutionId value) => value.Value;
}

/// <summary>
/// 审批标识，表示一次治理审批单据。
/// Approval identifier that represents a governance approval document.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<ApprovalId>))]
public readonly record struct ApprovalId
{
    public ApprovalId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(ApprovalId value) => value.Value;
}

/// <summary>
/// 用户补录请求标识，表示一次等待用户补充输入的治理请求。
/// User-input request identifier that represents a governance request waiting for additional user input.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<UserInputRequestId>))]
public readonly record struct UserInputRequestId
{
    public UserInputRequestId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(UserInputRequestId value) => value.Value;
}

/// <summary>
/// 团队标识，表示一个代理协作团队。
/// Team identifier that represents an agent collaboration team.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<TeamId>))]
public readonly record struct TeamId
{
    public TeamId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(TeamId value) => value.Value;
}

/// <summary>
/// 设备标识，表示身份平面绑定的一台设备。
/// Device identifier that represents a device bound in the identity plane.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<DeviceId>))]
public readonly record struct DeviceId
{
    public DeviceId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(DeviceId value) => value.Value;
}

/// <summary>
/// 记忆空间标识，表示一个长期记忆分区。
/// Memory-space identifier that represents a long-lived memory partition.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<MemorySpaceId>))]
public readonly record struct MemorySpaceId
{
    public MemorySpaceId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(MemorySpaceId value) => value.Value;
}

/// <summary>
/// 记忆记录标识，表示一条可引用、可反馈、可遗忘的记忆事实。
/// Memory-record identifier that represents a citable, feedbackable, and forgettable memory fact.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<MemoryRecordId>))]
public readonly record struct MemoryRecordId
{
    public MemoryRecordId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(MemoryRecordId value) => value.Value;
}

/// <summary>
/// 执行追踪标识，表示一次可回放的诊断追踪。
/// Execution-trace identifier that represents a replayable diagnostic trace.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<ExecutionTraceId>))]
public readonly record struct ExecutionTraceId
{
    public ExecutionTraceId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(ExecutionTraceId value) => value.Value;
}

/// <summary>
/// 审计记录标识，表示一条正式审计记录。
/// Audit-record identifier that represents a formal audit entry.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<AuditRecordId>))]
public readonly record struct AuditRecordId
{
    public AuditRecordId(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(AuditRecordId value) => value.Value;
}

/// <summary>
/// 投影游标，表示只读投影视图的增量订阅位置。
/// Projection cursor that represents the incremental subscription position of a read model.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<ProjectionCursor>))]
public readonly record struct ProjectionCursor
{
    public ProjectionCursor(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(ProjectionCursor value) => value.Value;
}

/// <summary>
/// 版本戳，表示 Contracts 对象的单调递增版本。
/// Version stamp that represents a monotonically increasing version for contract objects.
/// </summary>
[JsonConverter(typeof(TianShuVersionStampJsonConverter))]
public readonly record struct VersionStamp
{
    public VersionStamp(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "版本号不能为负。");
        }

        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString();
}

public sealed class TianShuStringIdentifierJsonConverter<TIdentifier> : JsonConverter<TIdentifier>
    where TIdentifier : struct
{
    private static readonly ConstructorInfo Constructor = typeof(TIdentifier).GetConstructor([typeof(string)])
        ?? throw new InvalidOperationException($"{typeof(TIdentifier).Name} 必须提供 string 构造函数。");
    private static readonly PropertyInfo ValueProperty = typeof(TIdentifier).GetProperty("Value")
        ?? throw new InvalidOperationException($"{typeof(TIdentifier).Name} 必须提供 Value 属性。");

    public override TIdentifier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Create(ReadIdentifierValue(ref reader, typeToConvert.Name));

    public override void Write(Utf8JsonWriter writer, TIdentifier value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("value", ValueProperty.GetValue(value)?.ToString());
        writer.WriteEndObject();
    }

    private static TIdentifier Create(string value)
        => (TIdentifier)Constructor.Invoke([value]);

    private static string ReadIdentifierValue(ref Utf8JsonReader reader, string subject)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() ?? throw new JsonException($"{subject} 不能为空。");
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"{subject} 必须是字符串或包含 value 的对象。");
        }

        string? value = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                break;
            }

            if (string.Equals(propertyName, "value", StringComparison.OrdinalIgnoreCase))
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    value = reader.GetString() ?? throw new JsonException($"{subject}.value 不能为空。");
                    continue;
                }

                if (reader.TokenType == JsonTokenType.Number)
                {
                    value = reader.GetInt64().ToString();
                    continue;
                }

                throw new JsonException($"{subject}.value 必须是字符串。");
            }

            reader.Skip();
        }

        return value ?? throw new JsonException($"{subject} 缺少 value。");
    }
}

public sealed class TianShuVersionStampJsonConverter : JsonConverter<VersionStamp>
{
    public override VersionStamp Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(ReadVersionValue(ref reader));

    public override void Write(Utf8JsonWriter writer, VersionStamp value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("value", value.Value);
        writer.WriteEndObject();
    }

    private static long ReadVersionValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt64();
        }

        if (reader.TokenType == JsonTokenType.String
            && long.TryParse(reader.GetString(), out var stringValue))
        {
            return stringValue;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("VersionStamp 必须是数字或包含 value 的对象。");
        }

        long? value = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                break;
            }

            if (string.Equals(propertyName, "value", StringComparison.OrdinalIgnoreCase))
            {
                if (reader.TokenType == JsonTokenType.Number)
                {
                    value = reader.GetInt64();
                    continue;
                }

                if (reader.TokenType == JsonTokenType.String
                    && long.TryParse(reader.GetString(), out var objectStringValue))
                {
                    value = objectStringValue;
                    continue;
                }

                throw new JsonException("VersionStamp.value 必须是数字。");
            }

            reader.Skip();
        }

        return value ?? throw new JsonException("VersionStamp 缺少 value。");
    }
}
