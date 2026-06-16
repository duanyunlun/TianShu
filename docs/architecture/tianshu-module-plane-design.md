# TianShu Module Plane 设计

## 1. 文档定位

Module Plane 承载可装配、可替换、可第三方实现的能力模块。Module 不是普通底层依赖；每个 Module 必须能声明能力、配置 schema、权限、副作用、审计、信任等级和健康状态。

本文件以 `docs/tianshu-architecture-spec.md` 为总架构基线，作为 Module Plane 当前代码落地与后续验收基线。

## 2. 涉及项目

| 模块家族 | 当前涉及项目 | 当前接入方式 |
| --- | --- | --- |
| 通用 Module 契约 | `src/Contracts/TianShu.Contracts.Modules` | 定义 `ModuleDescriptor`、`ModuleCapabilityDescriptor`、`ModuleHealthProbe`、`ModuleSmokeCheckResult`、`IModuleHealthCheck`。 |
| Provider Module | `src/Provider/TianShu.Provider.Abstractions`、`TianShu.Provider.OpenAI`、`TianShu.Provider.OpenAICompatible`、`TianShu.Provider.Anthropic`、`TianShu.Provider.Google` | `ProviderModuleDescriptorFactory.CreateModuleDescriptor` 将 `ProviderDescriptor` 投影为 `ModuleDescriptor`。 |
| Tool Module | `src/Contracts/TianShu.Contracts.Tools`、`src/Tools/TianShu.Tools.*` | `ToolDescriptor.ToModuleDescriptor` 将工具能力投影为 `ModuleDescriptor`。 |
| Memory / Identity Module | `src/Core/TianShu.IdentityMemory`、`src/Contracts/TianShu.Contracts.Memory`、`src/Contracts/TianShu.Contracts.Identity` | `IMemoryModule` 定义统一入口，`MemoryServiceModuleAdapter` 包裹当前 `IMemoryService`，并使用 `BuiltInModuleDescriptors.MemoryIdentity`。 |
| Artifact / State / Projection Module | `src/Contracts/TianShu.Contracts.Projections`、`src/Core/TianShu.ArtifactStore`、`src/Core/TianShu.ProjectionStores`、`src/Hosting/TianShu.AppHost.State` | `IArtifactStateProjectionModule` 定义统一入口，`ArtifactStateProjectionModuleAdapter` 包裹当前 `IArtifactStore` 与 projection stores，并使用 `BuiltInModuleDescriptors.ArtifactStateProjection`。 |
| Diagnostics Module | `src/Core/TianShu.Diagnostics`、`src/Contracts/TianShu.Contracts.Diagnostics`、`src/Execution/TianShu.Execution.Runtime` | `IDiagnosticsModule` 定义统一入口，`DiagnosticsModuleAdapter` 包裹当前诊断 sink / redactor，`ExecutionRuntimeDiagnosticsModuleBridge` 通过 `DiagnosticStep` 调用，并使用 `BuiltInModuleDescriptors.Diagnostics`。 |
| Workspace / Environment Module | `src/Contracts/TianShu.Contracts.Environment`、`src/Core/TianShu.RuntimeComposition`、`src/Execution/TianShu.Execution.Runtime` | `IWorkspaceModule` 定义统一入口，`BuiltInWorkspaceEnvironmentModule` 输出只读 workspace facts，`ExecutionRuntimeWorkspaceModuleBridge` 通过 `ModuleCapabilityStep` 调用，并使用 `BuiltInModuleDescriptors.WorkspaceEnvironment`。 |
| Configuration Module | `src/Core/TianShu.Configuration`、`src/Hosting/TianShu.AppHost.Configuration` | `BuiltInModuleDescriptors.Configuration` 提供当前目标 descriptor。 |

## 3. 接口骨架归属

归属项目：`src/Contracts/TianShu.Contracts.Modules/TianShu.Contracts.Modules.csproj`

```csharp
public sealed record ModuleDescriptor
{
    public string ModuleId { get; }
    public ModuleKind Kind { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public IReadOnlyList<ModuleCapabilityDescriptor> Capabilities { get; }
    public ModuleSchemaRef? ConfigurationSchema { get; }
    public PermissionEnvelope Permission { get; }
    public SideEffectProfile SideEffects { get; }
    public ModuleAuditProfile Audit { get; }
    public ModuleTrustLevel TrustLevel { get; }
    public ModuleHealthProbe Health { get; }
    public ModuleImplementationBinding? ImplementationBinding { get; }
    public MetadataBag Metadata { get; }
}

public sealed record ModuleCapabilityDescriptor
{
    public string CapabilityId { get; }
    public string DisplayName { get; }
    public ModuleSchemaRef? InputSchema { get; }
    public ModuleSchemaRef? OutputSchema { get; }
    public PermissionEnvelope Permission { get; }
    public SideEffectProfile SideEffects { get; }
    public MetadataBag Metadata { get; }
}

public interface IModuleHealthCheck
{
    ModuleDescriptor Descriptor { get; }

    ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken);
}
```

`ModuleDescriptor` 的默认治理必须 fail closed：

- `ModuleTrustLevel.Unspecified` 不能被装配层当成可信模块。
- `SideEffectLevel.Unspecified` 不能被 Execution Runtime 当成可执行副作用。
- 默认 `PermissionEnvelope.RequiresHumanGate` 为 `true`。
- 默认 `ModuleHealthStatus.Unknown` 不能被当成 healthy。

## 4. 当前接入规则

- Provider Module 必须先声明 `ProviderDescriptor`，再投影为 `ModuleDescriptor`。
- Tool Module 必须先声明 `ToolDescriptor`，再投影为 `ModuleDescriptor`。
- Configuration 在专项改造完成前，先以 `BuiltInModuleDescriptors` 作为目标 descriptor 基线；已完成专项改造的 Memory、Artifact、Diagnostics、Workspace 必须提供对应 module surface 与 Runtime bridge。
- Diagnostics Module 必须通过 `IDiagnosticsModule.EmitAsync` 接收 typed `DiagnosticsModuleEvent`，并在写入 sink 前统一脱敏 payload、metadata 和 failure message。
- Module implementation binding 只能标记项目、类型或包标识，不暴露外部 SDK 私有类型。
- Module capability 的 permission、side effect 和 audit 必须与对应 provider/tool/module 专项 descriptor 保持一致。
- Module health / smoke check 只返回 typed `ModuleSmokeCheckResult` 与 diagnostics ref，不直接修复或变更运行时状态。

## 5. 边界约束

- Module 不反向引用 Experience、Host Gateway、Control Plane、Kernel 或 Execution Runtime 具体实现。
- Module 不选择 StageGraph。
- Module 不生成 RuntimeStep。
- Module 不决定 ModelRoutePolicy、ContextPolicy 或 GovernanceEnvelope。
- Module 调用必须由 Execution Runtime 执行 Kernel 已批准的 `RuntimeStep`。
- 高副作用 Module 必须声明 side effect、audit，并由 policy 决定是否需要 human gate。

## 6. 验收标准

- 所有 AI 可使用能力都能映射为 `ITianShuTool` 或 `ModuleCapabilityDescriptor`。
- Provider、Tool、Memory、Artifact、Diagnostics、Workspace、Configuration 均有可发现的 `ModuleDescriptor`。
- Module descriptor 能投影 trust level、capability set、configuration schema、permission、side effect、audit 和 health。
- Module 项目依赖测试禁止其引用上层宿主、控制面、Kernel 实现或 Execution Runtime 实现。
- Module 输出必须可诊断、可审计、可回放。
