using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Environment;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Host;
using TianShu.Contracts.Identity;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Provider;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Tools;
using TianShu.Contracts.Workflows;
using ConversationThread = TianShu.Contracts.Conversations.Thread;

namespace TianShu.Contracts.Tests;

public sealed class ContractAggregationTests
{
    [Fact]
    public void AggregationProject_ExposesRepresentativeTypesFromAllContractAssemblies()
    {
        var representativeTypes = new[]
        {
            typeof(TianShu.Contracts.AssemblyMarker),
            typeof(IdentifierGuard),
            typeof(HostInteractionEnvelope),
            typeof(CollaborationSpace),
            typeof(ConfigurationProjection),
            typeof(InteractionEnvelope),
            typeof(Participant),
            typeof(Session),
            typeof(ConversationThread),
            typeof(Workflow),
            typeof(Agent),
            typeof(Approval),
            typeof(CapabilityCatalogSnapshot),
            typeof(ToolDescriptor),
            typeof(ProviderInvocationRequest),
            typeof(ExecutionRequest),
            typeof(HostEnvironmentProfile),
            typeof(Artifact),
            typeof(WorkflowBoardProjection),
            typeof(Account),
            typeof(MemorySpace),
            typeof(StageDefinition),
            typeof(ExecutionTrace),
            typeof(CoreIntent),
        };

        var actualAssemblyNames = representativeTypes
            .Select(static type => type.Assembly.GetName().Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var expectedAssemblyNames = new[]
        {
            "TianShu.Contracts",
            "TianShu.Contracts.Agents",
            "TianShu.Contracts.Artifacts",
            "TianShu.Contracts.Catalog",
            "TianShu.Contracts.Collaboration",
            "TianShu.Contracts.Configuration",
            "TianShu.Contracts.Conversations",
            "TianShu.Contracts.Diagnostics",
            "TianShu.Contracts.Environment",
            "TianShu.Contracts.Execution",
            "TianShu.Contracts.Governance",
            "TianShu.Contracts.Host",
            "TianShu.Contracts.Identity",
            "TianShu.Contracts.Interactions",
            "TianShu.Contracts.Kernel",
            "TianShu.Contracts.Memory",
            "TianShu.Contracts.Orchestration",
            "TianShu.Contracts.Participants",
            "TianShu.Contracts.Primitives",
            "TianShu.Contracts.Projections",
            "TianShu.Contracts.Provider",
            "TianShu.Contracts.Sessions",
            "TianShu.Contracts.Tools",
            "TianShu.Contracts.Workflows",
        };

        Assert.Equal(expectedAssemblyNames, actualAssemblyNames);
    }

    [Fact]
    public void AggregationProject_ExposesLateMigratedFacadeContracts()
    {
        var migratedFacadeTypes = new[]
        {
            typeof(ControlPlaneSkillConfigWriteCommand),
            typeof(ControlPlaneConfigBatchWriteCommand),
            typeof(ControlPlaneConversationArtifactQuery),
            typeof(ControlPlaneGitDiffArtifactQuery),
            typeof(ControlPlanePluginCatalogQuery),
            typeof(ControlPlaneMcpServerStatusQuery),
        };

        var actualAssemblyNames = migratedFacadeTypes
            .Select(static type => type.Assembly.GetName().Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "TianShu.Contracts.Artifacts",
                "TianShu.Contracts.Catalog",
            },
            actualAssemblyNames);
    }

    [Fact]
    public void ContractProjects_DoNotReferenceImplementationProjects()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var contractsRoot = Path.Combine(repositoryRoot, "src", "Contracts");
        var contractProjects = Directory.GetFiles(contractsRoot, "*.csproj", SearchOption.AllDirectories);

        Assert.NotEmpty(contractProjects);

        foreach (var projectPath in contractProjects)
        {
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var references = System.Xml.Linq.XDocument.Load(projectPath)
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Path.GetFullPath(Path.Combine(projectDirectory, value!)))
                .ToArray();

            Assert.All(references, reference =>
            {
                Assert.StartsWith(contractsRoot, reference, StringComparison.OrdinalIgnoreCase);
            });
        }
    }
}
