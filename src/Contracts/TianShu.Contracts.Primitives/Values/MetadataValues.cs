namespace TianShu.Contracts.Primitives;

/// <summary>
/// 元数据容器，用于在不引入 raw JSON 的前提下承载附加信息。
/// Metadata container that carries supplemental information without introducing raw JSON.
/// </summary>
public sealed class MetadataBag
{
    private static readonly IReadOnlyDictionary<string, StructuredValue> EmptyEntries =
        new Dictionary<string, StructuredValue>(StringComparer.Ordinal);

    /// <summary>
    /// 基于结构化值字典初始化元数据容器。
    /// Initializes the metadata bag from a structured-value dictionary.
    /// </summary>
    public MetadataBag(IReadOnlyDictionary<string, StructuredValue>? entries = null)
    {
        Entries = entries ?? EmptyEntries;
    }

    /// <summary>
    /// 元数据条目集合。
    /// Metadata entries carried by this bag.
    /// </summary>
    public IReadOnlyDictionary<string, StructuredValue> Entries { get; }

    public int Count => Entries.Count;

    public bool TryGetValue(string key, out StructuredValue value) => Entries.TryGetValue(key, out value!);

    /// <summary>
    /// 空元数据容器。
    /// Empty metadata bag.
    /// </summary>
    public static MetadataBag Empty { get; } = new();
}

/// <summary>
/// 标签集合，用于表达稳定、可去重的标签视图。
/// Label set that represents a stable deduplicated label view.
/// </summary>
public sealed class LabelSet
{
    private LabelSet(IReadOnlyList<string> values)
    {
        Values = values;
    }

    /// <summary>
    /// 标签值列表，保持去重后的原始顺序。
    /// Label values preserving the deduplicated insertion order.
    /// </summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>
    /// 空标签集合。
    /// Empty label set.
    /// </summary>
    public static LabelSet Empty { get; } = new(Array.Empty<string>());

    /// <summary>
    /// 从任意字符串序列创建标签集合，并自动裁剪空白与重复值。
    /// Creates a label set from arbitrary strings while trimming blanks and removing duplicates.
    /// </summary>
    public static LabelSet Create(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return Empty;
        }

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalized = value.Trim();
            if (seen.Add(normalized))
            {
                results.Add(normalized);
            }
        }

        return results.Count == 0 ? Empty : new LabelSet(results);
    }
}
