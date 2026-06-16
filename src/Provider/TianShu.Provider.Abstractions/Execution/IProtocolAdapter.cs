using TianShu.Contracts.Conversations;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// provider southbound 输入适配器边界。
/// Provider southbound input-adapter boundary.
/// </summary>
public interface IProtocolAdapter
{
    /// <summary>
    /// 当前适配器标识。
    /// Identifier of the current adapter.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 当前适配器是否为实验性实现。
    /// Indicates whether the adapter is experimental.
    /// </summary>
    bool IsExperimental { get; }

    /// <summary>
    /// 当前适配器能力摘要。
    /// Capability summary of the adapter.
    /// </summary>
    string CapabilitySummary { get; }

    /// <summary>
    /// 构造纯文本输入项对应的 southbound payload。
    /// Builds the southbound payload for a plain-text input item.
    /// </summary>
    object BuildTextUserInput(string text);

    /// <summary>
    /// 将统一输入项契约映射为 southbound payload。
    /// Maps the shared input-item contract into a southbound payload.
    /// </summary>
    object BuildUserInput(ControlPlaneInputItem input);
}
