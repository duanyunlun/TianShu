using System.Text.Json;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider-neutral 的 Responses 工具定义基类。
/// Base provider-neutral tool definition for Responses tool surfaces.
/// </summary>
public abstract record ProviderResponsesToolDefinition;

/// <summary>
/// Responses 工具输出 schema 的语义形状。
/// Semantic output-schema shape for a Responses tool definition.
/// </summary>
public enum ProviderResponsesToolOutputShape
{
    /// <summary>
    /// 直接将 schema 作为工具输出 schema。
    /// Uses the schema directly as the tool output schema.
    /// </summary>
    DirectSchema = 0,

    /// <summary>
    /// 将 schema 包装为 MCP 风格工具结果信封。
    /// Wraps the schema into an MCP-style tool result envelope.
    /// </summary>
    McpToolResultEnvelope = 1,
}

/// <summary>
/// Provider-neutral 的 function tool 定义。
/// Provider-neutral function-tool definition.
/// </summary>
public sealed record ProviderResponsesFunctionToolDefinition : ProviderResponsesToolDefinition
{
    /// <summary>
    /// 初始化 function tool 定义。
    /// Initializes a function-tool definition.
    /// </summary>
    public ProviderResponsesFunctionToolDefinition(
        string name,
        string description,
        JsonElement inputSchema,
        JsonElement? outputSchema = null,
        bool strict = false,
        ProviderResponsesToolOutputShape outputShape = ProviderResponsesToolOutputShape.DirectSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Name = name;
        Description = description;
        InputSchema = inputSchema.Clone();
        OutputSchema = outputSchema?.Clone();
        Strict = strict;
        OutputShape = outputShape;
    }

    /// <summary>
    /// 工具名称。
    /// Tool name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 工具描述。
    /// Tool description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 输入 schema。
    /// Input schema.
    /// </summary>
    public JsonElement InputSchema { get; }

    /// <summary>
    /// 输出 schema。
    /// Output schema.
    /// </summary>
    public JsonElement? OutputSchema { get; }

    /// <summary>
    /// strict 标志。
    /// strict flag.
    /// </summary>
    public bool Strict { get; }

    /// <summary>
    /// 输出 schema 形状。
    /// Output schema shape.
    /// </summary>
    public ProviderResponsesToolOutputShape OutputShape { get; }
}

/// <summary>
/// Provider-neutral 的 custom tool 定义。
/// Provider-neutral custom-tool definition.
/// </summary>
public sealed record ProviderResponsesCustomToolDefinition : ProviderResponsesToolDefinition
{
    /// <summary>
    /// 初始化 custom tool 定义。
    /// Initializes a custom-tool definition.
    /// </summary>
    public ProviderResponsesCustomToolDefinition(
        string name,
        string description,
        JsonElement format,
        JsonElement? outputSchema = null,
        ProviderResponsesToolOutputShape outputShape = ProviderResponsesToolOutputShape.DirectSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Name = name;
        Description = description;
        Format = format.Clone();
        OutputSchema = outputSchema?.Clone();
        OutputShape = outputShape;
    }

    /// <summary>
    /// 工具名称。
    /// Tool name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 工具描述。
    /// Tool description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// freeform/custom 格式定义。
    /// Freeform/custom format definition.
    /// </summary>
    public JsonElement Format { get; }

    /// <summary>
    /// 输出 schema。
    /// Output schema.
    /// </summary>
    public JsonElement? OutputSchema { get; }

    /// <summary>
    /// 输出 schema 形状。
    /// Output schema shape.
    /// </summary>
    public ProviderResponsesToolOutputShape OutputShape { get; }
}

/// <summary>
/// Provider-native 的 hosted tool 定义。
/// Provider-native hosted-tool definition.
/// </summary>
public sealed record ProviderResponsesHostedToolDefinition : ProviderResponsesToolDefinition
{
    /// <summary>
    /// 初始化 hosted tool 定义。
    /// Initializes a hosted-tool definition.
    /// </summary>
    public ProviderResponsesHostedToolDefinition(
        string toolType,
        string? description = null,
        JsonElement? inputSchema = null,
        JsonElement? outputSchema = null,
        ProviderResponsesToolOutputShape outputShape = ProviderResponsesToolOutputShape.DirectSchema,
        bool? strict = null,
        string? execution = null,
        bool? externalWebAccess = null,
        IReadOnlyList<string>? searchContentTypes = null,
        string? outputFormat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolType);

        ToolType = toolType;
        Description = description;
        InputSchema = inputSchema?.Clone();
        OutputSchema = outputSchema?.Clone();
        OutputShape = outputShape;
        Strict = strict;
        Execution = execution;
        ExternalWebAccess = externalWebAccess;
        SearchContentTypes = searchContentTypes?.ToArray();
        OutputFormat = outputFormat;
    }

    /// <summary>
    /// Provider-native 工具类型。
    /// Provider-native tool type.
    /// </summary>
    public string ToolType { get; }

    /// <summary>
    /// 可选描述。
    /// Optional description.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// 可选输入 schema。
    /// Optional input schema.
    /// </summary>
    public JsonElement? InputSchema { get; }

    /// <summary>
    /// 可选输出 schema。
    /// Optional output schema.
    /// </summary>
    public JsonElement? OutputSchema { get; }

    /// <summary>
    /// 输出 schema 形状。
    /// Output schema shape.
    /// </summary>
    public ProviderResponsesToolOutputShape OutputShape { get; }

    /// <summary>
    /// 可选 strict 标志。
    /// Optional strict flag.
    /// </summary>
    public bool? Strict { get; }

    /// <summary>
    /// 可选执行位置提示。
    /// Optional execution-location hint.
    /// </summary>
    public string? Execution { get; }

    /// <summary>
    /// 外部 Web 访问开关。
    /// External web access flag.
    /// </summary>
    public bool? ExternalWebAccess { get; }

    /// <summary>
    /// 搜索内容类型。
    /// Search content types.
    /// </summary>
    public IReadOnlyList<string>? SearchContentTypes { get; }

    /// <summary>
    /// 输出格式。
    /// Output format.
    /// </summary>
    public string? OutputFormat { get; }
}

/// <summary>
/// Provider-specific Responses 工具面构建上下文。
/// Context for provider-specific Responses tool-surface building.
/// </summary>
public sealed record ProviderResponsesToolSurfaceBuilderContext(
    IReadOnlyList<ProviderResponsesToolDefinition> Definitions);

/// <summary>
/// Provider-specific Responses 工具面构建器。
/// Provider-specific builder for Responses tool surfaces.
/// </summary>
public interface IProviderResponsesToolSurfaceBuilder
{
    /// <summary>
    /// 该 builder 对应的 wire API 标识。
    /// Wire API identifier owned by this builder.
    /// </summary>
    string WireApi { get; }

    /// <summary>
    /// 构建 provider-specific Responses 工具载荷。
    /// Builds provider-specific Responses tool payloads.
    /// </summary>
    IReadOnlyList<object> Build(ProviderResponsesToolSurfaceBuilderContext context);
}
