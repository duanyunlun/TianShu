using TianShu.Contracts.Kernel;
using TianShu.Contracts.Tools;
using TianShu.Tools.Artifacts;
using TianShu.Tools.Code;
using TianShu.Tools.Collaboration;
using TianShu.Tools.Fanout;
using TianShu.Tools.FileSystem;
using TianShu.Tools.FileSystemMutating;
using TianShu.Tools.Interaction;
using TianShu.Tools.McpResources;
using TianShu.Tools.Memory;
using TianShu.Tools.Search;
using TianShu.Tools.Shell;

namespace TianShu.AppHost.Tests;

public sealed class ToolCapabilityDescriptorTests
{
    [Fact]
    public void BuiltInToolProviders_ShouldExposeGovernedDescriptors()
    {
        var descriptors = DescribeAllBuiltInTools().ToArray();

        Assert.NotEmpty(descriptors);
        Assert.All(descriptors, descriptor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.ToolId));
            Assert.NotEqual(ToolKind.Unspecified, descriptor.Kind);
            Assert.True(
                descriptor.InputSchema.HasValue || descriptor.InputSchemaRef is not null || descriptor.CustomInputDefinition is not null,
                $"{descriptor.ToolId} must declare an input schema.");
            Assert.NotEmpty(descriptor.Permissions.RequiredScopes);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Permissions.Rationale));
            Assert.NotEqual(SideEffectLevel.Unspecified, descriptor.SideEffects.Level);
            Assert.NotEmpty(descriptor.SideEffects.AffectedResources);
            Assert.True(descriptor.Audit.Required);
            Assert.NotEmpty(descriptor.Audit.EventKinds);
            Assert.NotNull(descriptor.ImplementationBinding);
        });
    }

    [Fact]
    public void ShellTools_ShouldDeclareCommandSideEffectAndHumanGate()
    {
        var descriptors = new ShellToolProvider().DescribeTools(new TianShuToolRegistrationContext());

        Assert.NotEmpty(descriptors);
        Assert.All(descriptors, descriptor =>
        {
            Assert.True(descriptor.Permissions.RequiresHumanGate);
            Assert.Equal(SideEffectLevel.HostMutation, descriptor.SideEffects.Level);
            Assert.Contains("command", descriptor.SideEffects.AffectedResources);
            Assert.True(descriptor.Audit.Required);
        });
    }

    [Fact]
    public void FileSystemReadTools_ShouldDeclareReadOnlySideEffect()
    {
        var descriptors = new FileSystemToolProvider().DescribeTools(new TianShuToolRegistrationContext());

        Assert.NotEmpty(descriptors);
        Assert.All(descriptors, descriptor =>
        {
            Assert.False(descriptor.Permissions.RequiresHumanGate);
            Assert.Equal(SideEffectLevel.ReadOnly, descriptor.SideEffects.Level);
            Assert.Equal(ToolConcurrencyClass.SharedReadOnly, descriptor.ConcurrencyClass);
        });
    }

    [Fact]
    public void MutatingFileSystemTools_ShouldDeclareWriteSideEffectAndHumanGate()
    {
        var descriptors = new MutatingFileSystemToolProvider().DescribeTools(new TianShuToolRegistrationContext());

        Assert.NotEmpty(descriptors);
        Assert.All(descriptors, descriptor =>
        {
            Assert.True(descriptor.Permissions.RequiresHumanGate);
            Assert.Equal(SideEffectLevel.WorkspaceWrite, descriptor.SideEffects.Level);
            Assert.Equal(ToolConcurrencyClass.Exclusive, descriptor.ConcurrencyClass);
        });
    }

    [Fact]
    public void ToolProjects_ShouldNotReferenceKernelOrControlPlaneImplementations()
    {
        var repoRoot = FindRepoRoot();
        var projectFiles = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src", "Tools"), "*.csproj", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(projectFiles);
        foreach (var projectFile in projectFiles)
        {
            var source = File.ReadAllText(projectFile);
            Assert.DoesNotContain("src\\Core\\TianShu.Kernel", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TianShu.Kernel.csproj", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TianShu.ControlPlane.csproj", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TianShu.Execution.Runtime.csproj", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IEnumerable<ToolDescriptor> DescribeAllBuiltInTools()
    {
        var providers = new ITianShuToolProvider[]
        {
            new ArtifactToolProvider(),
            new CodeToolProvider(),
            new CollaborationToolProvider(),
            new FanoutToolProvider(),
            new FileSystemToolProvider(),
            new MutatingFileSystemToolProvider(),
            new InteractionToolProvider(),
            new McpResourceToolProvider(),
            new MemoryToolProvider(),
            new SearchToolProvider(),
            new ShellToolProvider(),
        };
        var context = new TianShuToolRegistrationContext();

        foreach (var provider in providers)
        {
            foreach (var descriptor in provider.DescribeTools(context))
            {
                yield return descriptor;
            }
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TianShu.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
