using TianShu.AppHost.Tools.Runtime;
using TianShu.Contracts.Tools;
using TianShu.Tools.FileSystem;
using TianShu.Tools.FileSystemMutating;

namespace TianShu.AppHost.Tests;

internal static class ToolProviderTestAdapters
{
    public static IKernelToolHandler CreateFileSystemRuntimeHandler(string toolKey)
        => new KernelContractToolHandlerAdapter(
            new FileSystemToolProvider().CreateHandler(toolKey, new TianShuToolActivationContext()));

    public static IKernelToolHandler CreateMutatingFileSystemRuntimeHandler(string toolKey)
        => new KernelContractToolHandlerAdapter(
            new MutatingFileSystemToolProvider().CreateHandler(toolKey, new TianShuToolActivationContext()));

    public static void RegisterFileSystemProviderTools(KernelToolRegistry registry)
        => RegisterProviderTools(registry, new FileSystemToolProvider());

    public static void RegisterMutatingFileSystemProviderTools(KernelToolRegistry registry)
        => RegisterProviderTools(registry, new MutatingFileSystemToolProvider());

    private static void RegisterProviderTools(KernelToolRegistry registry, ITianShuToolProvider provider)
    {
        foreach (var descriptor in provider.DescribeTools(new TianShuToolRegistrationContext()))
        {
            registry.Register(new KernelContractToolHandlerAdapter(
                provider.CreateHandler(descriptor.Key, new TianShuToolActivationContext())));
        }
    }
}
