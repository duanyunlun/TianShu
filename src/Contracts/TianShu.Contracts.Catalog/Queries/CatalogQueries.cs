namespace TianShu.Contracts.Catalog;

/// <summary>
/// 查询能力目录。
/// Query that fetches the capability catalog.
/// </summary>
public sealed record GetCapabilityCatalog
{
    /// <summary>
    /// 初始化能力目录查询。
    /// Initializes the capability-catalog query.
    /// </summary>
    public GetCapabilityCatalog(
        string? workspacePath = null,
        bool includeHiddenModels = false,
        int modelLimit = 200,
        bool includeHiddenTools = false)
    {
        if (modelLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modelLimit), "模型限制数量必须大于零。");
        }

        WorkspacePath = workspacePath;
        IncludeHiddenModels = includeHiddenModels;
        ModelLimit = modelLimit;
        IncludeHiddenTools = includeHiddenTools;
    }

    public string? WorkspacePath { get; }

    public bool IncludeHiddenModels { get; }

    public int ModelLimit { get; }

    public bool IncludeHiddenTools { get; }
}

/// <summary>
/// 查询引擎绑定解析结果。
/// Query that resolves an engine binding.
/// </summary>
public sealed record ResolveEngineBinding(
    string? WorkspacePath = null,
    string? PreferredProviderKey = null,
    string? PreferredModelKey = null,
    string? ReasoningEffort = null,
    string? ReasoningSummary = null,
    string? Verbosity = null,
    bool PreferWebsocketTransport = false);
