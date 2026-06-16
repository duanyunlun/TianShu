using System.Reflection;
using TianShu.Provider.Abstractions;
using TianShu.RuntimeComposition;

namespace TianShu.AppHost;

/// <summary>
/// AppHost 发布形态下的 provider 程序集预加载器。
/// Provider assembly preloader for AppHost publishing layouts.
/// </summary>
internal static class AppHostProviderAssemblyPreloader
{
    private static readonly string[] ProviderAssemblyNames =
    [
        "TianShu.Provider.OpenAICompatible",
        "TianShu.Provider.OpenAI",
        "TianShu.Provider.Anthropic",
        "TianShu.Provider.Google",
    ];

    /// <summary>
    /// 预加载随 AppHost 分发的 provider assemblies，兼容 single-file 发布时无磁盘 DLL 可扫描的场景。
    /// Preloads provider assemblies distributed with AppHost so single-file publishing does not depend on disk DLL scanning.
    /// </summary>
    public static void TryLoadPackagedProviders()
    {
        ProviderRuntimeComposition.PreloadProviderPackages();

        foreach (var assemblyName in ProviderAssemblyNames)
        {
            try
            {
                Assembly.Load(new AssemblyName(assemblyName));
            }
            catch (FileNotFoundException)
            {
                // 允许非完整宿主裁剪 provider；真正需要 provider 时注册表会给出明确错误。
            }
            catch (FileLoadException)
            {
                // 允许已在其它上下文加载或被宿主策略拦截；后续注册表仍以实际 loaded assemblies 为准。
            }
            catch (BadImageFormatException)
            {
                // 允许 RID/平台不匹配的实验分发产物不影响 help 等无需 provider 的命令。
            }
        }
    }
}
