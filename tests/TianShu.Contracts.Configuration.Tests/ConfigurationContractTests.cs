using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Configuration.Tests;

public sealed class ConfigurationContractTests
{
    [Fact]
    public void Projection_CarriesTypedCategoriesFieldsValuesAndSources()
    {
        var projection = new ConfigurationProjection
        {
            Profile = "default",
            Categories =
            [
                new ConfigurationCategoryDescriptor
                {
                    Id = ConfigurationCategoryIds.Foundation,
                    Kind = ConfigurationCategoryKind.Foundation,
                    DisplayName = "基础入口",
                    Order = 0,
                },
            ],
            Groups =
            [
                new ConfigurationGroupDescriptor
                {
                    Id = "app",
                    CategoryId = ConfigurationCategoryIds.Foundation,
                    DisplayName = "应用入口",
                },
            ],
            Fields =
            [
                new ConfigurationFieldDescriptor
                {
                    Key = "model",
                    GroupId = "app",
                    DisplayName = "默认模型",
                    ValueKind = ConfigurationValueKind.String,
                },
            ],
            Sources =
            [
                new ConfigurationSourceLayer
                {
                    Id = "user",
                    Kind = ConfigurationSourceKind.User,
                    DisplayName = "用户配置",
                    Path = @"C:\Users\Example\.tianshu\tianshu.toml",
                    IsWritable = true,
                },
            ],
            Values =
            [
                new ConfigurationFieldValue
                {
                    Key = "model",
                    SourceLayerId = "user",
                    Value = StructuredValue.FromString("gpt-5.4"),
                    IsConfigured = true,
                },
            ],
        };

        Assert.Equal("基础入口", Assert.Single(projection.Categories).DisplayName);
        Assert.Equal("model", Assert.Single(projection.Fields).Key);
        Assert.Equal("user", Assert.Single(projection.Values).SourceLayerId);
    }

    [Fact]
    public void Preview_CanApply_WhenNoErrorIssuesExist()
    {
        var projection = new ConfigurationProjection();
        var preview = new ConfigurationChangePreview
        {
            Before = projection,
            After = projection,
            Issues =
            [
                new ConfigurationIssue
                {
                    Severity = ConfigurationIssueSeverity.Warning,
                    Code = "config.preview.warning",
                    Message = "需要人工确认。",
                },
            ],
        };

        Assert.True(preview.CanApply);
    }

    [Fact]
    public void ConfigurationFacts_CarryKernelExecutionAndModuleTypedFacts()
    {
        var facts = new ConfigurationFacts
        {
            Kernel = new KernelConfigurationFacts
            {
                DefaultGraphId = "builtin.default",
                AdaptiveOrchestrationEnabled = true,
                AllowedKernelTools = ["propose_stage"],
                TokenBudget = 12000,
                RequireGovernanceEnvelope = true,
            },
            Execution = new ExecutionConfigurationFacts
            {
                DefaultProfile = "default",
                Profiles =
                [
                    new ExecutionRuntimeProfileConfigurationFacts
                    {
                        ProfileId = "default",
                        TimeoutMs = 30000,
                        MaxParallelism = 1,
                        SideEffectCeiling = "read_only",
                    },
                ],
            },
            Modules = new ModuleConfigurationFacts
            {
                DiscoveryRoots = ["modules"],
                Entries =
                [
                    new ModuleConfigurationEntryFacts
                    {
                        ModuleArea = "tools",
                        ModuleId = "shell",
                        DescriptorRef = "module://tools/shell",
                        TrustLevel = "trusted",
                        Capabilities = ["tool.shell"],
                    },
                ],
                Providers =
                [
                    new ProviderConfigurationFacts
                    {
                        ProviderId = "openai",
                        Endpoint = "https://api.openai.com/v1",
                        DefaultProtocol = "openai_responses",
                        SecretReferences = ["OPENAI_API_KEY"],
                    },
                ],
                Tools =
                [
                    new ToolConfigurationFacts
                    {
                        ToolId = "shell",
                        PermissionDeclaration = new ToolPermissionDeclarationFacts
                        {
                            ApprovalPolicy = "on-request",
                        },
                        SideEffectProfile = new ToolSideEffectProfileFacts
                        {
                            TimeoutSeconds = 120,
                        },
                        AuditProfile = new ToolAuditProfileFacts
                        {
                            EnvironmentPolicy = "inherit-safe",
                        },
                    },
                ],
                Memory = new MemoryConfigurationFacts
                {
                    DefaultProfile = "default",
                    Spaces =
                    [
                        new MemorySpaceConfigurationFacts
                        {
                            SpaceId = "default",
                            ProviderId = "local",
                            ReadOnly = true,
                        },
                    ],
                },
                Diagnostics = new DiagnosticsConfigurationFacts
                {
                    DefaultLevel = "stats",
                    RedactSecrets = true,
                },
                Workspace = new WorkspaceConfigurationFacts
                {
                    Profiles =
                    [
                        new WorkspaceProfileConfigurationFacts
                        {
                            ProfileId = "default",
                            TrustPolicy = "prompt",
                        },
                    ],
                },
            },
        };

        Assert.Equal("builtin.default", facts.Kernel.DefaultGraphId);
        Assert.Equal("default", Assert.Single(facts.Execution.Profiles).ProfileId);
        Assert.Equal("shell", Assert.Single(facts.Modules.Entries).ModuleId);
        Assert.Equal("openai", Assert.Single(facts.Modules.Providers).ProviderId);
        Assert.Equal("shell", Assert.Single(facts.Modules.Tools).ToolId);
        Assert.Equal("default", Assert.Single(facts.Modules.Memory.Spaces).SpaceId);
        Assert.Equal("stats", facts.Modules.Diagnostics.DefaultLevel);
        Assert.Equal("default", Assert.Single(facts.Modules.Workspace.Profiles).ProfileId);
    }

    [Fact]
    public void ConfigurationFacts_DoNotPromoteModuleSpecificExecutionInputs()
    {
        var propertyNames = typeof(ConfigurationFacts)
            .GetProperties()
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Execution", "Issues", "Kernel", "Modules"], propertyNames);
        Assert.DoesNotContain(propertyNames, static name => name.Contains("Provider", StringComparison.Ordinal));
        Assert.DoesNotContain(propertyNames, static name => name.Contains("Tool", StringComparison.Ordinal));
        Assert.DoesNotContain(propertyNames, static name => name.Contains("Memory", StringComparison.Ordinal));
        Assert.DoesNotContain(propertyNames, static name => name.Contains("Diagnostics", StringComparison.Ordinal));
        Assert.DoesNotContain(propertyNames, static name => name.Contains("Workspace", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeLayoutPaths_ResolveUserModulesDataAndRuntimeRootsFromTianShuHome()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var home = Path.Combine(Path.GetTempPath(), $"tianshu-contract-layout-{Guid.NewGuid():N}");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", home);

            Assert.Equal(Path.GetFullPath(home), Path.GetFullPath(TianShuRuntimeLayoutPaths.ResolveTianShuHomePath()));
            Assert.Equal(Path.Combine(home, "tianshu.toml"), TianShuRuntimeLayoutPaths.ResolveTianShuConfigFilePath());
            Assert.Equal(Path.Combine(home, "tianshu.toml"), TianShuRuntimeLayoutPaths.ResolveTianShuConfigFilePathFromHome(home));
            Assert.Equal(Path.Combine(home, "runtime", "apphost"), TianShuRuntimeLayoutPaths.ResolveRuntimePathFromHome(home, "apphost"));
            Assert.Equal(Path.Combine(home, "data", "memory"), TianShuRuntimeLayoutPaths.ResolveDataPathFromHome(home, "memory"));
            Assert.Equal(Path.Combine(home, "modules", "model", "provider-adapters"), TianShuRuntimeLayoutPaths.ResolveModulePathFromHome(home, "model", "provider-adapters"));
            Assert.Equal(Path.Combine(home, "modules", "skills", ".system"), TianShuRuntimeLayoutPaths.ResolveSystemSkillsCacheRoot(home));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
        }
    }

    [Fact]
    public void RuntimeLayoutPaths_TryResolvePortableLayoutFromBinDirectory()
    {
        var packageRoot = Path.Combine(Path.GetTempPath(), $"tianshu-portable-layout-{Guid.NewGuid():N}");
        var binRoot = Path.Combine(packageRoot, "bin");
        var modulesRoot = Path.Combine(packageRoot, "modules");

        try
        {
            Directory.CreateDirectory(binRoot);
            Directory.CreateDirectory(modulesRoot);
            File.WriteAllText(Path.Combine(packageRoot, "tianshu.toml"), string.Empty);

            var layout = TianShuRuntimeLayoutPaths.TryResolvePortableTianShuHomeLayoutFrom(binRoot);

            Assert.NotNull(layout);
            Assert.Equal(Path.GetFullPath(packageRoot), layout!.PackageRoot);
            Assert.Equal(Path.GetFullPath(binRoot), layout.BinRoot);
            Assert.Equal(Path.Combine(packageRoot, "tianshu.toml"), layout.ConfigFilePath);
            Assert.Equal(modulesRoot, layout.ModulesRoot);
            Assert.Equal(Path.Combine(packageRoot, "runtime"), layout.RuntimeRoot);
            Assert.Equal(Path.Combine(packageRoot, "data"), layout.DataRoot);
            Assert.Equal(
                Path.Combine(packageRoot, "modules", "model"),
                TianShuRuntimeLayoutPaths.ResolveModulePathFromConfig(layout.ConfigFilePath, "model"));
        }
        finally
        {
            DeleteDirectoryIfExists(packageRoot);
        }
    }

    [Fact]
    public void RuntimeLayoutPaths_PortableHomeWinsOverConfiguredTianShuHome()
    {
        var packageRoot = Path.Combine(Path.GetTempPath(), $"tianshu-portable-home-{Guid.NewGuid():N}");
        var configuredHome = Path.Combine(Path.GetTempPath(), $"tianshu-configured-home-{Guid.NewGuid():N}");
        var profileRoot = Path.Combine(Path.GetTempPath(), $"tianshu-profile-{Guid.NewGuid():N}");
        var binRoot = Path.Combine(packageRoot, "bin");

        try
        {
            Directory.CreateDirectory(binRoot);
            Directory.CreateDirectory(Path.Combine(packageRoot, "modules"));
            File.WriteAllText(Path.Combine(packageRoot, "tianshu.toml"), string.Empty);

            var resolved = TianShuRuntimeLayoutPaths.ResolveTianShuHomePathFrom(binRoot, configuredHome, profileRoot);

            Assert.Equal(Path.GetFullPath(packageRoot), resolved);
        }
        finally
        {
            DeleteDirectoryIfExists(packageRoot);
        }
    }

    [Fact]
    public void RuntimeLayoutPaths_UsesConfiguredTianShuHomeWhenPortableLayoutMissing()
    {
        var programDirectory = Path.Combine(Path.GetTempPath(), $"tianshu-program-{Guid.NewGuid():N}");
        var configuredHome = Path.Combine(Path.GetTempPath(), $"tianshu-configured-home-{Guid.NewGuid():N}");
        var profileRoot = Path.Combine(Path.GetTempPath(), $"tianshu-profile-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(programDirectory);

            var resolved = TianShuRuntimeLayoutPaths.ResolveTianShuHomePathFrom(programDirectory, configuredHome, profileRoot);

            Assert.Equal(configuredHome, resolved);
        }
        finally
        {
            DeleteDirectoryIfExists(programDirectory);
        }
    }

    [Fact]
    public void RuntimeLayoutPaths_FallsBackToUserProfileWhenPortableAndConfiguredHomeMissing()
    {
        var programDirectory = Path.Combine(Path.GetTempPath(), $"tianshu-program-{Guid.NewGuid():N}");
        var profileRoot = Path.Combine(Path.GetTempPath(), $"tianshu-profile-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(programDirectory);

            var resolved = TianShuRuntimeLayoutPaths.ResolveTianShuHomePathFrom(programDirectory, null, profileRoot);

            Assert.Equal(Path.Combine(profileRoot, ".tianshu"), resolved);
        }
        finally
        {
            DeleteDirectoryIfExists(programDirectory);
        }
    }

    [Fact]
    public void RuntimeLayoutPaths_DoesNotTreatNonBinChildAsPortablePackage()
    {
        var packageRoot = Path.Combine(Path.GetTempPath(), $"tianshu-nonportable-child-{Guid.NewGuid():N}");
        var childRoot = Path.Combine(packageRoot, "tools");
        var profileRoot = Path.Combine(Path.GetTempPath(), $"tianshu-profile-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(childRoot);
            Directory.CreateDirectory(Path.Combine(packageRoot, "modules"));
            File.WriteAllText(Path.Combine(packageRoot, "tianshu.toml"), string.Empty);

            var layout = TianShuRuntimeLayoutPaths.TryResolvePortableTianShuHomeLayoutFrom(childRoot);
            var resolved = TianShuRuntimeLayoutPaths.ResolveTianShuHomePathFrom(childRoot, null, profileRoot);

            Assert.Null(layout);
            Assert.Equal(Path.Combine(profileRoot, ".tianshu"), resolved);
        }
        finally
        {
            DeleteDirectoryIfExists(packageRoot);
        }
    }

    [Fact]
    public void RuntimeLayoutPaths_ResolveWorkspaceRuntimePath_UsesStableOpaqueWorkspaceKey()
    {
        var home = Path.Combine(Path.GetTempPath(), $"tianshu-runtime-home-{Guid.NewGuid():N}");
        var workspace = Path.Combine(Path.GetTempPath(), $"tianshu-workspace-{Guid.NewGuid():N}");
        var siblingWorkspace = Path.Combine(Path.GetTempPath(), $"tianshu-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(siblingWorkspace);

        try
        {
            var key = TianShuRuntimeLayoutPaths.ResolveWorkspaceKey(workspace);
            var keyWithTrailingSeparator = TianShuRuntimeLayoutPaths.ResolveWorkspaceKey(workspace + Path.DirectorySeparatorChar);
            var siblingKey = TianShuRuntimeLayoutPaths.ResolveWorkspaceKey(siblingWorkspace);
            var path = TianShuRuntimeLayoutPaths.ResolveRuntimeWorkspacePathFromHome(home, "kernel-runtime", workspace, "host-control");

            Assert.Equal(key, keyWithTrailingSeparator);
            Assert.NotEqual(key, siblingKey);
            Assert.StartsWith("workspace-", key, StringComparison.Ordinal);
            Assert.DoesNotContain(Path.GetFileName(workspace), key, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.Combine(Path.GetFullPath(home), "runtime", "kernel-runtime", key, "host-control"), path);
        }
        finally
        {
            DeleteDirectoryIfExists(workspace);
            DeleteDirectoryIfExists(siblingWorkspace);
            DeleteDirectoryIfExists(home);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
