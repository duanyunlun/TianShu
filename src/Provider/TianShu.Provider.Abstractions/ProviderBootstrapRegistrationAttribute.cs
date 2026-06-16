namespace TianShu.Provider.Abstractions;

/// <summary>
/// 声明 provider 程序集内可由共享 loader 发现的 bootstrap 类型。
/// Declares a bootstrap type inside a provider assembly that can be discovered by the shared loader.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ProviderBootstrapRegistrationAttribute : Attribute
{
    /// <summary>
    /// 初始化 provider bootstrap 注册特性。
    /// Initializes the provider bootstrap registration attribute.
    /// </summary>
    /// <param name="bootstrapType">provider 程序集自声明的 bootstrap 类型。Bootstrap type self-declared by the provider assembly.</param>
    public ProviderBootstrapRegistrationAttribute(Type bootstrapType)
    {
        ArgumentNullException.ThrowIfNull(bootstrapType);
        BootstrapType = bootstrapType;
    }

    /// <summary>
    /// bootstrap 的具体类型。
    /// Concrete bootstrap type.
    /// </summary>
    public Type BootstrapType { get; }
}
