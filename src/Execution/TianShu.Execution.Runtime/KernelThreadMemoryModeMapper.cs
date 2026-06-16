using TianShu.Contracts.Memory;

namespace TianShu.Execution.Runtime;

/// <summary>
/// 将历史线程记忆字符串映射到稳定记忆契约。
/// Maps legacy thread memory strings to the stable memory contract.
/// </summary>
internal static class KernelThreadMemoryModeMapper
{
    public const string EnabledStorageMode = "enabled";
    public const string DisabledStorageMode = "disabled";
    public const string ReadOnlyStorageMode = "readOnly";
    public const string EphemeralStorageMode = "ephemeral";
    public const string PollutedStorageMode = "polluted";

    /// <summary>
    /// 规范化持久化字符串，同时保留历史兼容值。
    /// Normalizes storage strings while preserving legacy-compatible values.
    /// </summary>
    public static string NormalizeStorageMode(string? memoryMode)
    {
        var text = Normalize(memoryMode);
        return text?.ToLowerInvariant() switch
        {
            DisabledStorageMode => DisabledStorageMode,
            "readonly" or "read_only" or "read-only" => ReadOnlyStorageMode,
            EphemeralStorageMode => EphemeralStorageMode,
            PollutedStorageMode => PollutedStorageMode,
            _ => EnabledStorageMode,
        };
    }

    /// <summary>
    /// 将存储模式和线程临时状态投影为正式记忆模式。
    /// Projects storage mode plus ephemeral state into the formal memory mode.
    /// </summary>
    public static ThreadMemoryMode ToThreadMemoryMode(string? memoryMode, bool isEphemeral)
    {
        var normalized = NormalizeStorageMode(memoryMode);
        return normalized switch
        {
            DisabledStorageMode => ThreadMemoryMode.Disabled,
            EphemeralStorageMode => ThreadMemoryMode.Ephemeral,
            _ when isEphemeral => ThreadMemoryMode.Ephemeral,
            ReadOnlyStorageMode => ThreadMemoryMode.ReadOnly,
            PollutedStorageMode => ThreadMemoryMode.ReadOnly,
            _ => ThreadMemoryMode.ReadWrite,
        };
    }

    /// <summary>
    /// 将正式契约模式转换为当前兼容的存储字符串。
    /// Converts the formal contract mode into the current compatible storage string.
    /// </summary>
    public static string ToStorageMode(ThreadMemoryMode memoryMode)
        => memoryMode switch
        {
            ThreadMemoryMode.Disabled => DisabledStorageMode,
            ThreadMemoryMode.ReadOnly => ReadOnlyStorageMode,
            ThreadMemoryMode.Ephemeral => EphemeralStorageMode,
            _ => EnabledStorageMode,
        };

    private static string? Normalize(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
