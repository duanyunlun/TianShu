/// <summary>
/// 为 AppHost 项目补齐共享宿主命名空间与 helpers。
/// Provides AppHost-wide imports for shared host types and helpers.
/// </summary>
global using TianShu.AppHost.Configuration;
global using TianShu.AppHost.Catalog;
global using TianShu.AppHost.State;
global using TianShu.AppHost.Tools;
global using TianShu.Execution.Runtime;
global using KernelModelProtocolResolver = TianShu.Configuration.KernelModelProtocolResolver;
global using TianShuPromptConfiguration = TianShu.Configuration.TianShuPromptConfiguration;
global using TianShuPromptConfigUtilities = TianShu.Configuration.TianShuPromptConfigUtilities;
global using static TianShu.AppHost.Catalog.KernelCatalogSurfaceUtilities;
global using static TianShu.AppHost.Configuration.TianShuHomePathUtilities;
global using static TianShu.AppHost.Configuration.KernelConfigReadLayerUtilities;
global using static TianShu.AppHost.Configuration.KernelConfigWriteUtilities;
global using static TianShu.AppHost.Configuration.KernelConfigRequirementsUtilities;
global using static TianShu.AppHost.Configuration.KernelConfigPersistenceUtilities;
global using static TianShu.AppHost.Configuration.KernelTomlTextParsingUtilities;
global using static TianShu.AppHost.Configuration.KernelInstructionConfigUtilities;
global using static TianShu.AppHost.Configuration.KernelModelProviderConfigUtilities;
global using static TianShu.AppHost.Tools.KernelAppCatalogUtilities;
global using static TianShu.AppHost.Tools.KernelCommandApprovalUtilities;
global using static TianShu.AppHost.Tools.KernelConversationSummaryUtilities;
global using static TianShu.AppHost.Tools.KernelFuzzyFileSearchUtilities;
global using static TianShu.AppHost.Tools.McpServerAuthUtilities;
global using static TianShu.AppHost.Tools.KernelPersistedSkillConfigUtilities;
global using static TianShu.AppHost.Tools.KernelReviewAppHostUtilities;
global using static TianShu.AppHost.Tools.KernelWindowsSandboxSetupUtilities;
