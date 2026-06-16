param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$TianShuHome = $(
        if (-not [string]::IsNullOrWhiteSpace($env:TIANSHU_HOME)) {
            $env:TIANSHU_HOME
        }
        else {
            Join-Path $env:USERPROFILE ".tianshu"
        }
    ),
    [switch]$PreserveConfig,
    [switch]$OverwriteConfig,
    [switch]$SkipPathUpdate
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Add-UserPathEntry {
    param([Parameter(Mandatory = $true)][string]$PathEntry)

    $currentUserPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $parts = @()
    if (-not [string]::IsNullOrWhiteSpace($currentUserPath)) {
        $parts = $currentUserPath -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }

    $alreadyPresent = $false
    foreach ($part in $parts) {
        if ([string]::Equals(
                [System.IO.Path]::GetFullPath($part.Trim()),
                [System.IO.Path]::GetFullPath($PathEntry),
                [System.StringComparison]::OrdinalIgnoreCase)) {
            $alreadyPresent = $true
            break
        }
    }

    if (-not $alreadyPresent) {
        $newPath = if ($parts.Count -gt 0) {
            ($parts + $PathEntry) -join ";"
        }
        else {
            $PathEntry
        }

        [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    }

    $processParts = @()
    if (-not [string]::IsNullOrWhiteSpace($env:Path)) {
        $processParts = $env:Path -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }

    $processAlreadyPresent = $false
    foreach ($part in $processParts) {
        if ([string]::Equals(
                [System.IO.Path]::GetFullPath($part.Trim()),
                [System.IO.Path]::GetFullPath($PathEntry),
                [System.StringComparison]::OrdinalIgnoreCase)) {
            $processAlreadyPresent = $true
            break
        }
    }

    if (-not $processAlreadyPresent) {
        $env:Path = "$PathEntry;$env:Path"
    }
}

function Remove-InstallManagedStalePath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedPath = Resolve-FullPath $Path
    $homeRoot = [System.IO.Path]::GetFullPath($tianShuHomeFullPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $homePrefix = "$homeRoot$([System.IO.Path]::DirectorySeparatorChar)"
    if (-not $resolvedPath.StartsWith($homePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理 TianShu Home 之外的安装残留：$resolvedPath"
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    Write-Host "已清理安装残留：$Label -> $resolvedPath"
}

function Copy-PublishDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$TargetDirectory,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $SourceDirectory)) {
        throw "发布目录不存在，无法安装 $Label：$SourceDirectory"
    }

    New-Item -ItemType Directory -Path $TargetDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath $SourceDirectory -Force | Copy-Item -Destination $TargetDirectory -Recurse -Force
}

function Copy-LegacyDirectoryToModulePath {
    param(
        [Parameter(Mandatory = $true)][string]$LegacyDirectory,
        [Parameter(Mandatory = $true)][string]$TargetDirectory,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $LegacyDirectory)) {
        return
    }

    New-Item -ItemType Directory -Path $TargetDirectory -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $LegacyDirectory -Force) {
        $targetPath = Join-Path $TargetDirectory $item.Name
        if (Test-Path -LiteralPath $targetPath) {
            continue
        }

        Copy-Item -LiteralPath $item.FullName -Destination $targetPath -Recurse -Force
    }

    Write-Host "已迁移旧目录到新目录：$Label -> $TargetDirectory"
}

function Stop-TianShuInstallProcesses {
    $processNames = @(
        "tianshu",
        "TianShu.ConfigGui",
        "TianShu.AppHost"
    )
    $processes = @(Get-Process -ErrorAction SilentlyContinue |
        Where-Object {
            foreach ($processName in $processNames) {
                if ([string]::Equals($_.ProcessName, $processName, [System.StringComparison]::OrdinalIgnoreCase)) {
                    return $true
                }
            }

            return $false
        })

    if ($processes.Count -eq 0) {
        Write-Host "未发现正在运行的 TianShu 三件套进程，无需关闭。"
        return
    }

    foreach ($process in $processes) {
        Write-Host "安装前关闭进程：$($process.ProcessName) [$($process.Id)]"
        Stop-Process -Id $process.Id -Force
    }

    foreach ($process in $processes) {
        try {
            Wait-Process -Id $process.Id -Timeout 10 -ErrorAction Stop
        }
        catch {
            Write-Warning "进程 $($process.ProcessName) [$($process.Id)] 未在 10 秒内确认退出；安装将继续，若仍占用文件会在复制阶段报错。"
        }
    }
}

function Ensure-TemplateFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    if (Test-Path -LiteralPath $Path) {
        if ($OverwriteConfig -and -not $PreserveConfig) {
            $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
            $backupPath = Join-Path $directory "$([System.IO.Path]::GetFileName($Path)).bak-$timestamp"
            Copy-Item -LiteralPath $Path -Destination $backupPath -Force
            Set-Content -LiteralPath $Path -Value $Content -Encoding UTF8
            Write-Host "已按显式请求备份并更新$Label：$backupPath"
        }
        else {
            Write-Host "保留已有$Label：$Path"
        }
        return
    }

    Set-Content -LiteralPath $Path -Value $Content -Encoding UTF8
    Write-Host "已安装$Label：$Path"
}

function Test-GeneratedTemplate {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string[]]$Markers
    )

    foreach ($marker in $Markers) {
        if ($Text -notmatch [regex]::Escape($marker)) {
            return $false
        }
    }

    return $true
}

function Ensure-DefaultModuleTemplate {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string[]]$GeneratedMarkers,
        [Parameter(Mandatory = $true)][string[]]$RequiredMarkers,
        [switch]$RefreshWhenRequiredMarkersMissing,
        [string[]]$StaleMarkers = @()
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null

    if (-not (Test-Path -LiteralPath $Path)) {
        Set-Content -LiteralPath $Path -Value $Content -Encoding UTF8
        Write-Host "已安装$Label：$Path"
        return
    }

    if ($OverwriteConfig -and -not $PreserveConfig) {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $backupPath = Join-Path $directory "$([System.IO.Path]::GetFileName($Path)).bak-$timestamp"
        Copy-Item -LiteralPath $Path -Destination $backupPath -Force
        Set-Content -LiteralPath $Path -Value $Content -Encoding UTF8
        Write-Host "已按显式请求备份并更新$Label：$backupPath"
        return
    }

    $existingText = Get-Content -LiteralPath $Path -Raw
    $isGeneratedTemplate = Test-GeneratedTemplate -Text $existingText -Markers $GeneratedMarkers
    $hasAllRequiredMarkers = $true
    foreach ($marker in $RequiredMarkers) {
        if ($existingText -notmatch [regex]::Escape($marker)) {
            $hasAllRequiredMarkers = $false
            break
        }
    }

    $hasStaleMarker = $false
    foreach ($marker in $StaleMarkers) {
        if ($existingText -match [regex]::Escape($marker)) {
            $hasStaleMarker = $true
            break
        }
    }

    if (($isGeneratedTemplate -or $RefreshWhenRequiredMarkersMissing) -and (-not $hasAllRequiredMarkers -or $hasStaleMarker)) {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $backupPath = Join-Path $directory "$([System.IO.Path]::GetFileName($Path)).bak-auto-refresh-$timestamp"
        Copy-Item -LiteralPath $Path -Destination $backupPath -Force
        Set-Content -LiteralPath $Path -Value $Content -Encoding UTF8
        Write-Host "已自动刷新过期$Label，并备份旧文件：$backupPath"
        return
    }

    Write-Host "保留已有$Label：$Path"
}

function Ensure-MainConfigurationReferences {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $text = Get-Content -LiteralPath $Path -Raw
    $rootLines = New-Object System.Collections.Generic.List[string]
    $requiredRootReferences = [ordered]@{
        "profile" = '"default"'
        "model_route_set" = '"default"'
        "model_protocol_rule_set" = '"default"'
        "provider_instances" = '"default"'
        "approval_policy" = '"never"'
        "sandbox_mode" = '"danger-full-access"'
    }

    foreach ($entry in $requiredRootReferences.GetEnumerator()) {
        $pattern = "(?m)^\s*$([regex]::Escape($entry.Key))\s*="
        if ($text -notmatch $pattern) {
            $rootLines.Add("$($entry.Key) = $($entry.Value)")
        }
    }

    $profileChunk = $null
    if ($text -notmatch '(?m)^\s*\[profiles\.default\]\s*$') {
        $profileChunk = @'
[profiles.default]
agent = "default"
execution = "default"
conversation = "default"
permissions = "default"
model_route_set = "default"
memory = "default"
tools = "default"
tui = "default"
workspace = "default"
session = "default"
collaboration = "default"
workflow = "default"
identity = "local"
governance = "default"
features = "default"
realtime = "default"
'@
    }

    if ($rootLines.Count -eq 0 -and [string]::IsNullOrWhiteSpace($profileChunk)) {
        return
    }

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupPath = Join-Path (Split-Path -Parent $Path) "tianshu.toml.bak-main-refs-$timestamp"
    Copy-Item -LiteralPath $Path -Destination $backupPath -Force

    $parts = New-Object System.Collections.Generic.List[string]
    if ($rootLines.Count -gt 0) {
        $parts.Add(($rootLines -join [Environment]::NewLine))
    }
    $parts.Add($text.TrimEnd())
    if (-not [string]::IsNullOrWhiteSpace($profileChunk)) {
        $parts.Add($profileChunk.Trim())
    }

    Set-Content -LiteralPath $Path -Value (($parts | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ([Environment]::NewLine + [Environment]::NewLine)) -Encoding UTF8
    Write-Host "已补齐主配置入口引用，并备份旧配置：$backupPath"
}

$repoRoot = Resolve-FullPath (Join-Path $PSScriptRoot "..")
$tianShuHomeFullPath = Resolve-FullPath $TianShuHome
$binDirectory = Join-Path $tianShuHomeFullPath "bin"
$runtimeDirectory = Join-Path $tianShuHomeFullPath "runtime"
$appHostDirectory = Join-Path $runtimeDirectory "apphost"
$modulesDirectory = Join-Path $tianShuHomeFullPath "modules"
$dataDirectory = Join-Path $tianShuHomeFullPath "data"
$toolsDirectory = Join-Path $modulesDirectory "tools\packages"
$builtinToolsDirectory = Join-Path $toolsDirectory "builtin"
$providersDirectory = Join-Path $modulesDirectory "model\provider-adapters"
$builtinProvidersDirectory = Join-Path $providersDirectory "builtin"
$artifactStoresDirectory = Join-Path $modulesDirectory "artifacts\stores"
$builtinArtifactStoresDirectory = Join-Path $artifactStoresDirectory "builtin"
$diagnosticSinksDirectory = Join-Path $modulesDirectory "diagnostics\sinks"
$builtinDiagnosticSinksDirectory = Join-Path $diagnosticSinksDirectory "builtin"
$workspaceResolversDirectory = Join-Path $modulesDirectory "workspace\resolvers"
$builtinWorkspaceResolversDirectory = Join-Path $workspaceResolversDirectory "builtin"
$policyStrategiesDirectory = Join-Path $modulesDirectory "policies\strategies"
$builtinPolicyStrategiesDirectory = Join-Path $policyStrategiesDirectory "builtin"
$mcpServersDirectory = Join-Path $modulesDirectory "mcp-servers"
$promptPacksDirectory = Join-Path $modulesDirectory "prompts"
$skillsDirectory = Join-Path $modulesDirectory "skills"
$legacyAppHostDirectory = Join-Path $tianShuHomeFullPath "apphost"
$legacyToolsDirectory = Join-Path $tianShuHomeFullPath "tools"
$legacyCasedToolsDirectory = Join-Path $tianShuHomeFullPath "Tools"
$legacyProvidersDirectory = Join-Path $tianShuHomeFullPath "providers"
$legacyArtifactStoresDirectory = Join-Path $tianShuHomeFullPath "artifact-stores"
$legacyDiagnosticSinksDirectory = Join-Path $tianShuHomeFullPath "diagnostic-sinks"
$legacyWorkspaceResolversDirectory = Join-Path $tianShuHomeFullPath "workspace-resolvers"
$legacyPolicyStrategiesDirectory = Join-Path $tianShuHomeFullPath "policy-strategies"
$legacyMcpServersDirectory = Join-Path $tianShuHomeFullPath "mcp-servers"
$legacyPromptPacksDirectory = Join-Path $tianShuHomeFullPath "prompts"
$staleModelRouteSetsRootDirectory = Join-Path $tianShuHomeFullPath "model-route-sets"
$legacyModelProtocolRulesDirectory = Join-Path $tianShuHomeFullPath "model-protocol-rules"
$legacyProviderInstancesDirectory = Join-Path $tianShuHomeFullPath "provider-instances"
$legacySkillsDirectory = Join-Path $tianShuHomeFullPath "skills"
$builtinToolManifestSourcePath = Join-Path $repoRoot "src\Hosting\TianShu.AppHost.Tools.Runtime\Resources\tools\builtin\tool.toml"
$builtinToolManifestTargetPath = Join-Path $builtinToolsDirectory "tool.toml"
$builtinProviderManifestSourcePath = Join-Path $repoRoot "src\Provider\TianShu.Provider.Abstractions\Resources\providers\builtin\provider.toml"
$builtinProviderManifestTargetPath = Join-Path $builtinProvidersDirectory "provider.toml"
$builtinArtifactStoreManifestSourcePath = Join-Path $repoRoot "src\Core\TianShu.ArtifactStore\Resources\artifact-stores\builtin\store.toml"
$builtinArtifactStoreManifestTargetPath = Join-Path $builtinArtifactStoresDirectory "store.toml"
$builtinDiagnosticSinkManifestSourcePath = Join-Path $repoRoot "src\Core\TianShu.Diagnostics\Resources\diagnostic-sinks\builtin\sink.toml"
$builtinDiagnosticSinkManifestTargetPath = Join-Path $builtinDiagnosticSinksDirectory "sink.toml"
$builtinWorkspaceResolverManifestSourcePath = Join-Path $repoRoot "src\Core\TianShu.Configuration\Resources\workspace-resolvers\builtin\resolver.toml"
$builtinWorkspaceResolverManifestTargetPath = Join-Path $builtinWorkspaceResolversDirectory "resolver.toml"
$builtinPolicyStrategyManifestSourcePath = Join-Path $repoRoot "src\Core\TianShu.Configuration\Resources\policy-strategies\builtin\policy.toml"
$builtinPolicyStrategyManifestTargetPath = Join-Path $builtinPolicyStrategiesDirectory "policy.toml"
$projectConfigPath = Join-Path $repoRoot ".tianshu\tianshu.toml"
$userConfigPath = Join-Path $tianShuHomeFullPath "tianshu.toml"
$modelRouteSetsDirectory = Join-Path $modulesDirectory "model\route-sets"
$defaultModelRouteSetTemplatePath = Join-Path $modelRouteSetsDirectory "default.toml"
$modelProtocolRulesDirectory = Join-Path $modulesDirectory "model\protocol-rules"
$defaultModelProtocolRulesTemplatePath = Join-Path $modelProtocolRulesDirectory "default.toml"
$providerInstancesDirectory = Join-Path $modulesDirectory "model\provider-instances"
$defaultProviderInstanceTemplatePath = Join-Path $providerInstancesDirectory "default.toml"
$agentAgentsDirectory = Join-Path $modulesDirectory "agent\agents"
$agentExecutionProfilesDirectory = Join-Path $modulesDirectory "agent\execution-profiles"
$agentSessionProfilesDirectory = Join-Path $modulesDirectory "agent\session-profiles"
$agentConversationProfilesDirectory = Join-Path $modulesDirectory "agent\conversation-profiles"
$memoryProfilesDirectory = Join-Path $modulesDirectory "memory\profiles"
$memorySpacesDirectory = Join-Path $modulesDirectory "memory\spaces"
$memoryProvidersDirectory = Join-Path $modulesDirectory "memory\providers"
$memoryBindingsDirectory = Join-Path $modulesDirectory "memory\bindings"
$toolProfilesDirectory = Join-Path $modulesDirectory "tools\profiles"
$workspaceProfilesDirectory = Join-Path $modulesDirectory "workspace\profiles"
$permissionProfilesDirectory = Join-Path $modulesDirectory "policies\permission-profiles"
$governanceProfilesDirectory = Join-Path $modulesDirectory "policies\governance-profiles"
$sandboxesDirectory = Join-Path $modulesDirectory "policies\sandboxes"
$tuiProfilesDirectory = Join-Path $modulesDirectory "experience\tui-profiles"
$featureProfilesDirectory = Join-Path $modulesDirectory "experience\feature-profiles"
$realtimeProfilesDirectory = Join-Path $modulesDirectory "experience\realtime-profiles"
$identityProfilesDirectory = Join-Path $modulesDirectory "identity\profiles"
$accountsDirectory = Join-Path $modulesDirectory "identity\accounts"
$devicesDirectory = Join-Path $modulesDirectory "identity\devices"
$collaborationProfilesDirectory = Join-Path $modulesDirectory "collaboration\profiles"
$workflowProfilesDirectory = Join-Path $modulesDirectory "collaboration\workflow-profiles"
$defaultAgentTemplatePath = Join-Path $agentAgentsDirectory "default.toml"
$defaultExecutionProfileTemplatePath = Join-Path $agentExecutionProfilesDirectory "default.toml"
$defaultSessionProfileTemplatePath = Join-Path $agentSessionProfilesDirectory "default.toml"
$defaultConversationProfileTemplatePath = Join-Path $agentConversationProfilesDirectory "default.toml"
$defaultMemoryProfileTemplatePath = Join-Path $memoryProfilesDirectory "default.toml"
$defaultMemorySpaceTemplatePath = Join-Path $memorySpacesDirectory "default.toml"
$defaultMemoryProviderTemplatePath = Join-Path $memoryProvidersDirectory "local.toml"
$defaultMemoryBindingTemplatePath = Join-Path $memoryBindingsDirectory "default.toml"
$defaultToolProfileTemplatePath = Join-Path $toolProfilesDirectory "default.toml"
$defaultWorkspaceProfileTemplatePath = Join-Path $workspaceProfilesDirectory "default.toml"
$defaultPermissionProfileTemplatePath = Join-Path $permissionProfilesDirectory "default.toml"
$defaultGovernanceProfileTemplatePath = Join-Path $governanceProfilesDirectory "default.toml"
$defaultWorkspaceSandboxTemplatePath = Join-Path $sandboxesDirectory "workspace-write.toml"
$defaultDangerSandboxTemplatePath = Join-Path $sandboxesDirectory "danger-full-access.toml"
$defaultTuiProfileTemplatePath = Join-Path $tuiProfilesDirectory "default.toml"
$defaultFeatureProfileTemplatePath = Join-Path $featureProfilesDirectory "default.toml"
$defaultRealtimeProfileTemplatePath = Join-Path $realtimeProfilesDirectory "default.toml"
$defaultIdentityProfileTemplatePath = Join-Path $identityProfilesDirectory "local.toml"
$defaultAccountTemplatePath = Join-Path $accountsDirectory "local.toml"
$defaultDeviceTemplatePath = Join-Path $devicesDirectory "local.toml"
$defaultCollaborationProfileTemplatePath = Join-Path $collaborationProfilesDirectory "default.toml"
$defaultWorkflowProfileTemplatePath = Join-Path $workflowProfilesDirectory "default.toml"
$cliProjectPath = Join-Path $repoRoot "src\Presentations\TianShu.Cli\TianShu.Cli.csproj"
$cliPublishDirectory = Join-Path $repoRoot "src\Presentations\TianShu.Cli\bin\$Configuration\net10.0\$RuntimeIdentifier\publish"
$configGuiProjectPath = Join-Path $repoRoot "src\Presentations\TianShu.ConfigGui\TianShu.ConfigGui.csproj"
$configGuiPublishDirectory = Join-Path $repoRoot "src\Presentations\TianShu.ConfigGui\bin\$Configuration\net10.0\$RuntimeIdentifier\publish"
$appHostProjectPath = Join-Path $repoRoot "src\Hosting\TianShu.AppHost\TianShu.AppHost.csproj"
$staleBinResourcesDirectory = Join-Path $binDirectory "Resources"
$legacyBuiltinToolsAssemblyPath = Join-Path $builtinToolsDirectory "TianShu.Tooling.BuiltinTools.dll"
$legacyBuiltinBundleDirectory = Join-Path $builtinToolsDirectory "bundle"
$builtinShellToolDirectory = Join-Path $builtinToolsDirectory "shell"
$builtinInteractionToolDirectory = Join-Path $builtinToolsDirectory "interaction"
$builtinCollaborationToolDirectory = Join-Path $builtinToolsDirectory "collaboration"
$builtinFanoutToolDirectory = Join-Path $builtinToolsDirectory "fanout"
$builtinCodeToolDirectory = Join-Path $builtinToolsDirectory "code"
$builtinArtifactToolDirectory = Join-Path $builtinToolsDirectory "artifacts"
$builtinSearchToolDirectory = Join-Path $builtinToolsDirectory "search"
$builtinMemoryToolDirectory = Join-Path $builtinToolsDirectory "memory"
$builtinMcpResourcesToolDirectory = Join-Path $builtinToolsDirectory "mcp-resources"
$builtinFileSystemToolDirectory = Join-Path $builtinToolsDirectory "filesystem"
$builtinFileSystemMutatingToolDirectory = Join-Path $builtinToolsDirectory "filesystem-mutating"
$builtinOpenAiCompatibleProviderDirectory = Join-Path $builtinProvidersDirectory "openai-compatible"
$builtinOpenAiProviderDirectory = Join-Path $builtinProvidersDirectory "openai"
$builtinAnthropicProviderDirectory = Join-Path $builtinProvidersDirectory "anthropic"
$builtinGoogleProviderDirectory = Join-Path $builtinProvidersDirectory "google"
$builtinToolProjects = @(
    @{
        Name = "Shell 工具域 Provider"
        ProjectPath = Join-Path $repoRoot "src\Tools\TianShu.Tools.Shell\TianShu.Tools.Shell.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Tools\TianShu.Tools.Shell\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Tools.Shell.dll"
        TargetDirectory = $builtinShellToolDirectory
    },
    @{
        Name = "Interaction 工具域 Provider"
        ProjectPath = Join-Path $repoRoot "src\Tools\TianShu.Tools.Interaction\TianShu.Tools.Interaction.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Tools\TianShu.Tools.Interaction\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Tools.Interaction.dll"
        TargetDirectory = $builtinInteractionToolDirectory
    },
    @{
        Name = "Collaboration 工具域 Provider"
        ProjectPath = Join-Path $repoRoot "src\Tools\TianShu.Tools.Collaboration\TianShu.Tools.Collaboration.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Tools\TianShu.Tools.Collaboration\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Tools.Collaboration.dll"
        TargetDirectory = $builtinCollaborationToolDirectory
    },
    @{
        Name = "Fanout Jobs 工具域 Provider"
        ProjectPath = Join-Path $repoRoot "src\Tools\TianShu.Tools.Fanout\TianShu.Tools.Fanout.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Tools\TianShu.Tools.Fanout\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Tools.Fanout.dll"
        TargetDirectory = $builtinFanoutToolDirectory
    },
    @{
        Name = "Code / REPL 工具域 Provider"
        ProjectPath = Join-Path $repoRoot "src\Tools\TianShu.Tools.Code\TianShu.Tools.Code.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Tools\TianShu.Tools.Code\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Tools.Code.dll"
        TargetDirectory = $builtinCodeToolDirectory
    },
    @{
        Name = "Artifact / View 工具域 Provider"
        ProjectPath = Join-Path $repoRoot "src\Tools\TianShu.Tools.Artifacts\TianShu.Tools.Artifacts.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Tools\TianShu.Tools.Artifacts\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Tools.Artifacts.dll"
        TargetDirectory = $builtinArtifactToolDirectory
    },
    @{
        Name = "Search 工具域 Provider"
        ProjectPath = Join-Path $repoRoot "src\Tools\TianShu.Tools.Search\TianShu.Tools.Search.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Tools\TianShu.Tools.Search\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Tools.Search.dll"
        TargetDirectory = $builtinSearchToolDirectory
    },
    @{
        Name = "Memory 工具域 Provider"
        ProjectPath = Join-Path $repoRoot "src\Tools\TianShu.Tools.Memory\TianShu.Tools.Memory.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Tools\TianShu.Tools.Memory\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Tools.Memory.dll"
        TargetDirectory = $builtinMemoryToolDirectory
    },
    @{
        Name = "MCP Resource 工具域 Provider"
        ProjectPath = Join-Path $repoRoot "src\Tools\TianShu.Tools.McpResources\TianShu.Tools.McpResources.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Tools\TianShu.Tools.McpResources\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Tools.McpResources.dll"
        TargetDirectory = $builtinMcpResourcesToolDirectory
    },
    @{
        Name = "FileSystem 只读工具域 Provider"
        ProjectPath = Join-Path $repoRoot "src\Tools\TianShu.Tools.FileSystem\TianShu.Tools.FileSystem.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Tools\TianShu.Tools.FileSystem\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Tools.FileSystem.dll"
        TargetDirectory = $builtinFileSystemToolDirectory
    },
    @{
        Name = "FileSystem 写入工具域 Provider"
        ProjectPath = Join-Path $repoRoot "src\Tools\TianShu.Tools.FileSystemMutating\TianShu.Tools.FileSystemMutating.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Tools\TianShu.Tools.FileSystemMutating\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Tools.FileSystemMutating.dll"
        TargetDirectory = $builtinFileSystemMutatingToolDirectory
    }
)
$builtinProviderProjects = @(
    @{
        Name = "OpenAI-compatible Chat Completions Provider"
        ProjectPath = Join-Path $repoRoot "src\Provider\TianShu.Provider.OpenAICompatible\TianShu.Provider.OpenAICompatible.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Provider\TianShu.Provider.OpenAICompatible\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Provider.OpenAICompatible.dll"
        TargetDirectory = $builtinOpenAiCompatibleProviderDirectory
    },
    @{
        Name = "OpenAI Responses Provider"
        ProjectPath = Join-Path $repoRoot "src\Provider\TianShu.Provider.OpenAI\TianShu.Provider.OpenAI.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Provider\TianShu.Provider.OpenAI\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Provider.OpenAI.dll"
        TargetDirectory = $builtinOpenAiProviderDirectory
    },
    @{
        Name = "Anthropic Messages Provider"
        ProjectPath = Join-Path $repoRoot "src\Provider\TianShu.Provider.Anthropic\TianShu.Provider.Anthropic.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Provider\TianShu.Provider.Anthropic\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Provider.Anthropic.dll"
        TargetDirectory = $builtinAnthropicProviderDirectory
    },
    @{
        Name = "Google Generative Provider"
        ProjectPath = Join-Path $repoRoot "src\Provider\TianShu.Provider.Google\TianShu.Provider.Google.csproj"
        BuildDirectory = Join-Path $repoRoot "src\Provider\TianShu.Provider.Google\bin\$Configuration\net10.0"
        AssemblyName = "TianShu.Provider.Google.dll"
        TargetDirectory = $builtinGoogleProviderDirectory
    }
)
$defaultConfigContent = @'
# TianShu user default configuration.
# 本文件只引用 API key 所在环境变量名，不保存任何 secret。

profile = "default"
model_route_set = "default"
model_protocol_rule_set = "default"
provider_instances = "default"
approval_policy = "never"
sandbox_mode = "danger-full-access"

[profiles.default]
agent = "default"
execution = "default"
conversation = "default"
permissions = "default"
model_route_set = "default"
memory = "default"
tools = "default"
tui = "default"
workspace = "default"
session = "default"
collaboration = "default"
workflow = "default"
identity = "local"
governance = "default"
features = "default"
realtime = "default"
'@

$defaultAgentTemplateContent = @'
[agents.default]
display_name = "Default Agent"
model_route_set = "default"
personality = "default"
max_output_tokens = 4096

[agents.default.reasoning]
enabled = true
effort = "medium"
summary = "auto"
verbosity = "normal"
budget_tokens = 4096
'@

$defaultExecutionProfileTemplateContent = @'
[execution_profiles.default]
agent = "default"
model_route_set = "default"
approval = "never"
sandbox = "danger-full-access"
web_search = "auto"
parallel_tool_calls = true
'@

$defaultSessionProfileTemplateContent = @'
[session_profiles.default]
model_binding = "snapshot-on-create"
memory_mode = "read-write"
auto_resume = "ask"
'@

$defaultConversationProfileTemplateContent = @'
[conversation_profiles.default]
thread_source = "local"
history = "sliced"
fuzzy_file_search = true
pending_input_timeout_seconds = 120
'@

$defaultMemoryProfileContent = @'
memory.enabled = true
memory.default_profile = "default"

[memory_profiles.default]
enabled = true
default_space = "default"
overlay = true
extract = "manual"
retention = "keep"
'@

$defaultMemorySpaceContent = @'
[memory.spaces.default]
scope = "user"
provider = "local"
read_only = false
tags = ["default", "local"]
'@

$defaultMemoryProviderContent = @'
[memory.providers.local]
kind = "local"
display_name = "Local Memory"
enabled = true
mode = "read-write"
root = "./data/memory"
capabilities = ["filter", "add", "feedback"]
'@

$defaultMemoryBindingContent = @'
[memory.bindings.default]
space = "default"
provider = "local"
capabilities = ["filter", "add", "feedback"]
mode = "read-write"
'@

$defaultToolProfileContent = @'
[tool_profiles.default]
enabled = ["builtin"]
disabled = []

[tool_profiles.default.memory]
enabled = true
default_profile = "default"
'@

$defaultWorkspaceProfileContent = @'
[workspace_profiles.default]
root_markers = [".git", ".tianshu"]
trust_policy = "prompt"
artifact_root = ".tianshu/artifacts"
state_root = ".tianshu/state"
model = "inherit"
model_lock = "snapshot-on-create"
'@

$defaultPermissionProfileContent = @'
[permission_profiles.default]
approval = "never"
sandbox = "danger-full-access"
allow_network = true
allow_shell = true
allow_file_write = true
allow_process_spawn = true
rules = []
'@

$defaultGovernanceProfileContent = @'
[governance_profiles.default]
approval_queue = true
user_input_requests = true
risk_acknowledgement = "ask"
default_requested_from = "current-user"
'@

$defaultWorkspaceSandboxContent = @'
[sandboxes.workspace-write]
mode = "workspace-write"
network = true
writable_roots = []
readable_roots = []
exclude_tmpdir_env_var = false
exclude_slash_tmp = false
'@

$defaultDangerSandboxContent = @'
[sandboxes.danger-full-access]
mode = "danger-full-access"
network = true
writable_roots = []
readable_roots = []
exclude_tmpdir_env_var = false
exclude_slash_tmp = false
'@

$defaultTuiProfileContent = @'
[tui_profiles.default]
theme = "tianshu"
startup_card = true
show_model = true
show_directory = true
show_permissions = true
loading = "thinking"
cursor = "bar"
'@

$defaultFeatureProfileContent = @'
[feature_profiles.default]
enabled = []
disabled = []
'@

$defaultRealtimeProfileContent = @'
[realtime_profiles.default]
enabled = false
provider = "openai-compatible"
model = "openai-compatible-default"
audio_input = true
audio_output = true
handoff_mode = "manual"
'@

$defaultIdentityProfileContent = @'
[identity_profiles.local]
account = "local"
device_binding = "local"
habit_profile = "user"
allow_device_sync = false
'@

$defaultAccountContent = @'
[accounts.local]
display_name = "Local Account"
provider = "local"
'@

$defaultDeviceContent = @'
[devices.local]
display_name = "Local Device"
kind = "desktop"
trust = "local"
'@

$defaultCollaborationProfileContent = @'
[collaboration_profiles.default]
default_space = "local"
default_workspace = "."
default_execution_profile = "default"
policy_key = "default"
participant_role = "owner"
'@

$defaultWorkflowProfileContent = @'
[workflow_profiles.default]
default_space = "local"
default_owner = "current-user"
task_state = "todo"
verification_gate = "manual"
auto_dispatch_jobs = false
'@

$defaultProviderInstanceTemplateContent = @'
# TianShu 默认 Provider instance 模板。
# 本文件只引用 API key 所在环境变量名，不保存任何 secret。

[providers.openai]
base_url = "https://api.openai.com"
api_key_env = "OPENAI_API_KEY"
default_protocol = "openai_responses"
protocol_fallbacks = ["openai_responses"]
request_max_retries = 2
stream_max_retries = 2
stream_idle_timeout_ms = 30000
websocket_connect_timeout_ms = 15000
supports_websockets = false

[providers.openai.reasoning]
enabled = true
effort = "medium"
summary = "auto"
verbosity = "normal"
budget_tokens = 4096

[providers.openai-compatible]
base_url = "https://api.openai.com"
api_key_env = "OPENAI_COMPATIBLE_API_KEY"
default_protocol = "openai_chat_completions"
protocol_fallbacks = ["openai_chat_completions"]
model_overrides = [{ name = "openai-compatible-default", protocols = ["openai_chat_completions"] }]
request_max_retries = 2
stream_max_retries = 2
stream_idle_timeout_ms = 30000
websocket_connect_timeout_ms = 15000
supports_websockets = false

[providers.openai-compatible.reasoning]
enabled = true
effort = "medium"
summary = "auto"
verbosity = "normal"
budget_tokens = 4096

[providers.anthropic]
base_url = "https://api.anthropic.com"
api_key_env = "ANTHROPIC_API_KEY"
default_protocol = "anthropic_messages"
protocol_fallbacks = ["anthropic_messages"]
model_overrides = [{ name = "claude-opus-4.8", protocols = ["anthropic_messages"] }]
request_max_retries = 2
stream_max_retries = 2
stream_idle_timeout_ms = 30000
websocket_connect_timeout_ms = 15000
supports_websockets = false
'@

$defaultModelRouteSetTemplateContent = @'
# TianShu 默认模型路由方案模板。
# 该模板只包含 provider/model/protocol 引用，不保存 API key、base URL 或其它 secret。

[model_route_sets.default]
display_name = "Default Model Route Set"
description = "TianShu route-set-first model routing template for non-Google live protocols."
routes = [
  { kind = "default", candidates = [{ provider = "openai", model = "gpt-5.5", protocol = "responses" }] },
  { kind = "planning", candidates = [{ provider = "openai", model = "gpt-5.5", protocol = "responses" }] },
  { kind = "coding", candidates = [{ provider = "openai", model = "gpt-5.5", protocol = "responses" }] },
  { kind = "review", candidates = [{ provider = "anthropic", model = "claude-opus-4.8", protocol = "anthropic_messages" }] },
  { kind = "summarization", candidates = [{ provider = "openai-compatible", model = "openai-compatible-default", protocol = "openai_chat_completions" }] },
  { kind = "memory_extraction", candidates = [{ provider = "openai-compatible", model = "openai-compatible-default", protocol = "openai_chat_completions" }] },
  { kind = "long_context", candidates = [{ provider = "openai", model = "gpt-5.5", protocol = "responses" }] },
  { kind = "fast", candidates = [{ provider = "openai-compatible", model = "openai-compatible-default", protocol = "openai_chat_completions" }] },
]
'@

$defaultModelProtocolRulesTemplateContent = @'
# TianShu 默认模型协议规则模板。
# Provider 内 model_overrides / protocol_rules 显式命中时优先于这里的全局默认规则。

[model_protocol_rule_sets.default]
display_name = "Default Model Protocol Rules"
description = "Common model-family to wire protocol priority rules."
rules = [
  { match = "anthropic/claude-opus-4.8", protocols = ["anthropic_messages"] },
  { match = "anthropic/claude*", protocols = ["anthropic_messages"] },
  { match = "claude-opus-4.8", protocols = ["anthropic_messages"] },
  { match = "claude*", protocols = ["anthropic_messages"] },
  { match = "openai/gpt-*", protocols = ["openai_responses", "openai_chat_completions"] },
  { match = "gpt-*", protocols = ["openai_responses", "openai_chat_completions"] },
  { match = "o1*", protocols = ["openai_responses", "openai_chat_completions"] },
  { match = "o3*", protocols = ["openai_responses", "openai_chat_completions"] },
  { match = "o4*", protocols = ["openai_responses", "openai_chat_completions"] },
  { match = "openai-compatible-default", protocols = ["openai_chat_completions"] },
  { match = "deepseek*", protocols = ["openai_chat_completions"] },
  { match = "qwen*", protocols = ["openai_chat_completions"] },
  { match = "kimi*", protocols = ["openai_chat_completions"] },
  { match = "moonshot*", protocols = ["openai_chat_completions"] },
  { match = "glm*", protocols = ["openai_chat_completions"] },
  { match = "minimax*", protocols = ["openai_chat_completions"] },
  { match = "mimo*", protocols = ["openai_chat_completions"] },
  { match = "grok*", protocols = ["openai_chat_completions"] },
  { match = "baichuan*", protocols = ["openai_chat_completions"] },
  { match = "doubao*", protocols = ["openai_chat_completions"] },
  { match = "mistral*", protocols = ["openai_chat_completions"] },
  { match = "mixtral*", protocols = ["openai_chat_completions"] },
  { match = "llama*", protocols = ["openai_chat_completions"] },
  { match = "yi*", protocols = ["openai_chat_completions"] },
]
'@

if (-not (Test-Path $cliProjectPath)) {
    throw "找不到 CLI 项目：$cliProjectPath"
}

if (-not (Test-Path $configGuiProjectPath)) {
    throw "找不到 ConfigGUI 项目：$configGuiProjectPath"
}

if (-not (Test-Path $appHostProjectPath)) {
    throw "找不到 AppHost 项目：$appHostProjectPath"
}

foreach ($builtinToolProject in $builtinToolProjects) {
    if (-not (Test-Path $builtinToolProject.ProjectPath)) {
        throw "找不到$($builtinToolProject.Name)项目：$($builtinToolProject.ProjectPath)"
    }
}

foreach ($builtinProviderProject in $builtinProviderProjects) {
    if (-not (Test-Path $builtinProviderProject.ProjectPath)) {
        throw "找不到$($builtinProviderProject.Name)项目：$($builtinProviderProject.ProjectPath)"
    }
}

Stop-TianShuInstallProcesses

New-Item -ItemType Directory -Path $tianShuHomeFullPath -Force | Out-Null

$directories = @(
    $tianShuHomeFullPath,
    $binDirectory,
    $runtimeDirectory,
    $appHostDirectory,
    $modulesDirectory,
    $dataDirectory,
    (Join-Path $dataDirectory "artifacts"),
    (Join-Path $dataDirectory "cache"),
    (Join-Path $dataDirectory "input-history"),
    (Join-Path $dataDirectory "memory"),
    (Join-Path $dataDirectory "sessions"),
    (Join-Path $dataDirectory "state"),
    (Join-Path $tianShuHomeFullPath "logs"),
    $modelRouteSetsDirectory,
    $modelProtocolRulesDirectory,
    $providerInstancesDirectory,
    $agentAgentsDirectory,
    $agentExecutionProfilesDirectory,
    $agentSessionProfilesDirectory,
    $agentConversationProfilesDirectory,
    $memoryProfilesDirectory,
    $memorySpacesDirectory,
    $memoryProvidersDirectory,
    $memoryBindingsDirectory,
    $toolProfilesDirectory,
    $workspaceProfilesDirectory,
    $permissionProfilesDirectory,
    $governanceProfilesDirectory,
    $sandboxesDirectory,
    $tuiProfilesDirectory,
    $featureProfilesDirectory,
    $realtimeProfilesDirectory,
    $identityProfilesDirectory,
    $accountsDirectory,
    $devicesDirectory,
    $collaborationProfilesDirectory,
    $workflowProfilesDirectory,
    (Join-Path $tianShuHomeFullPath "plugins"),
    $promptPacksDirectory,
    $skillsDirectory,
    (Join-Path $tianShuHomeFullPath "tmp"),
    $toolsDirectory,
    $builtinToolsDirectory,
    $providersDirectory,
    $builtinProvidersDirectory,
    $artifactStoresDirectory,
    $builtinArtifactStoresDirectory,
    $diagnosticSinksDirectory,
    $builtinDiagnosticSinksDirectory,
    $workspaceResolversDirectory,
    $builtinWorkspaceResolversDirectory,
    $policyStrategiesDirectory,
    $builtinPolicyStrategiesDirectory,
    $mcpServersDirectory,
    $builtinShellToolDirectory,
    $builtinInteractionToolDirectory,
    $builtinCollaborationToolDirectory,
    $builtinFanoutToolDirectory,
    $builtinCodeToolDirectory,
    $builtinArtifactToolDirectory,
    $builtinSearchToolDirectory,
    $builtinMemoryToolDirectory,
    $builtinMcpResourcesToolDirectory,
    $builtinFileSystemToolDirectory,
    $builtinFileSystemMutatingToolDirectory,
    $builtinOpenAiCompatibleProviderDirectory,
    $builtinOpenAiProviderDirectory,
    $builtinAnthropicProviderDirectory,
    $builtinGoogleProviderDirectory
)

foreach ($directory in $directories) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

Copy-LegacyDirectoryToModulePath -LegacyDirectory $legacyToolsDirectory -TargetDirectory $toolsDirectory -Label "tools -> modules/tools/packages"
Copy-LegacyDirectoryToModulePath -LegacyDirectory $legacyCasedToolsDirectory -TargetDirectory $toolsDirectory -Label "Tools -> modules/tools/packages"
Copy-LegacyDirectoryToModulePath -LegacyDirectory $legacyProvidersDirectory -TargetDirectory $providersDirectory -Label "providers -> modules/model/provider-adapters"
Copy-LegacyDirectoryToModulePath -LegacyDirectory $legacyArtifactStoresDirectory -TargetDirectory $artifactStoresDirectory -Label "artifact-stores -> modules/artifacts/stores"
Copy-LegacyDirectoryToModulePath -LegacyDirectory $legacyDiagnosticSinksDirectory -TargetDirectory $diagnosticSinksDirectory -Label "diagnostic-sinks -> modules/diagnostics/sinks"
Copy-LegacyDirectoryToModulePath -LegacyDirectory $legacyWorkspaceResolversDirectory -TargetDirectory $workspaceResolversDirectory -Label "workspace-resolvers -> modules/workspace/resolvers"
Copy-LegacyDirectoryToModulePath -LegacyDirectory $legacyPolicyStrategiesDirectory -TargetDirectory $policyStrategiesDirectory -Label "policy-strategies -> modules/policies/strategies"
Copy-LegacyDirectoryToModulePath -LegacyDirectory $legacyMcpServersDirectory -TargetDirectory $mcpServersDirectory -Label "mcp-servers -> modules/mcp-servers"
Copy-LegacyDirectoryToModulePath -LegacyDirectory $legacyPromptPacksDirectory -TargetDirectory $promptPacksDirectory -Label "prompts -> modules/prompts"
Copy-LegacyDirectoryToModulePath -LegacyDirectory $legacyModelProtocolRulesDirectory -TargetDirectory $modelProtocolRulesDirectory -Label "model-protocol-rules -> modules/model/protocol-rules"
Copy-LegacyDirectoryToModulePath -LegacyDirectory $legacyProviderInstancesDirectory -TargetDirectory $providerInstancesDirectory -Label "provider-instances -> modules/model/provider-instances"
Copy-LegacyDirectoryToModulePath -LegacyDirectory $legacySkillsDirectory -TargetDirectory $skillsDirectory -Label "skills -> modules/skills"
Copy-LegacyDirectoryToModulePath -LegacyDirectory (Join-Path $tianShuHomeFullPath "artifacts") -TargetDirectory (Join-Path $dataDirectory "artifacts") -Label "artifacts -> data/artifacts"
Copy-LegacyDirectoryToModulePath -LegacyDirectory (Join-Path $tianShuHomeFullPath "cache") -TargetDirectory (Join-Path $dataDirectory "cache") -Label "cache -> data/cache"
Copy-LegacyDirectoryToModulePath -LegacyDirectory (Join-Path $tianShuHomeFullPath "memory") -TargetDirectory (Join-Path $dataDirectory "memory") -Label "memory -> data/memory"
Copy-LegacyDirectoryToModulePath -LegacyDirectory (Join-Path $tianShuHomeFullPath "memories") -TargetDirectory (Join-Path $dataDirectory "memory") -Label "memories -> data/memory"
Copy-LegacyDirectoryToModulePath -LegacyDirectory (Join-Path $tianShuHomeFullPath "sessions") -TargetDirectory (Join-Path $dataDirectory "sessions") -Label "sessions -> data/sessions"
Copy-LegacyDirectoryToModulePath -LegacyDirectory (Join-Path $tianShuHomeFullPath "state") -TargetDirectory (Join-Path $dataDirectory "state") -Label "state -> data/state"

Remove-InstallManagedStalePath -Path $legacyAppHostDirectory -Label "旧 apphost 根目录"
Remove-InstallManagedStalePath -Path $legacyToolsDirectory -Label "旧 tools 根目录"
Remove-InstallManagedStalePath -Path $legacyCasedToolsDirectory -Label "旧 Tools 根目录"
Remove-InstallManagedStalePath -Path $legacyProvidersDirectory -Label "旧 providers 根目录"
Remove-InstallManagedStalePath -Path $legacyArtifactStoresDirectory -Label "旧 artifact-stores 根目录"
Remove-InstallManagedStalePath -Path $legacyDiagnosticSinksDirectory -Label "旧 diagnostic-sinks 根目录"
Remove-InstallManagedStalePath -Path $legacyWorkspaceResolversDirectory -Label "旧 workspace-resolvers 根目录"
Remove-InstallManagedStalePath -Path $legacyPolicyStrategiesDirectory -Label "旧 policy-strategies 根目录"
Remove-InstallManagedStalePath -Path $legacyMcpServersDirectory -Label "旧 mcp-servers 根目录"
Remove-InstallManagedStalePath -Path $legacyPromptPacksDirectory -Label "旧 prompts 根目录"
Remove-InstallManagedStalePath -Path $staleModelRouteSetsRootDirectory -Label "旧 model-route-sets 根目录"
Remove-InstallManagedStalePath -Path $legacyModelProtocolRulesDirectory -Label "旧 model-protocol-rules 根目录"
Remove-InstallManagedStalePath -Path $legacyProviderInstancesDirectory -Label "旧 provider-instances 根目录"
Remove-InstallManagedStalePath -Path $legacySkillsDirectory -Label "旧 skills 根目录"
Remove-InstallManagedStalePath -Path (Join-Path $tianShuHomeFullPath "artifacts") -Label "旧 artifacts 根目录"
Remove-InstallManagedStalePath -Path (Join-Path $tianShuHomeFullPath "cache") -Label "旧 cache 根目录"
Remove-InstallManagedStalePath -Path (Join-Path $tianShuHomeFullPath "memory") -Label "旧 memory 根目录"
Remove-InstallManagedStalePath -Path (Join-Path $tianShuHomeFullPath "memories") -Label "旧 memories 根目录"
Remove-InstallManagedStalePath -Path (Join-Path $tianShuHomeFullPath "sessions") -Label "旧 sessions 根目录"
Remove-InstallManagedStalePath -Path (Join-Path $tianShuHomeFullPath "state") -Label "旧 state 根目录"
Remove-InstallManagedStalePath -Path $staleBinResourcesDirectory -Label "bin Resources 默认 manifest 副本"

$mainConfigurationWasWritten = $false

if (Test-Path $userConfigPath) {
    if ($OverwriteConfig -and -not $PreserveConfig) {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $backupPath = Join-Path $tianShuHomeFullPath "tianshu.toml.bak-$timestamp"
        Copy-Item -LiteralPath $userConfigPath -Destination $backupPath -Force
        if (Test-Path $projectConfigPath) {
            Copy-Item -LiteralPath $projectConfigPath -Destination $userConfigPath -Force
        }
        else {
            Set-Content -LiteralPath $userConfigPath -Value $defaultConfigContent -Encoding UTF8
        }
        $mainConfigurationWasWritten = $true
        Write-Host "已按显式请求备份并更新配置：$backupPath"
    }
    else {
        Write-Host "保留已有配置：$userConfigPath"
    }
}
else {
    if (Test-Path $projectConfigPath) {
        Copy-Item -LiteralPath $projectConfigPath -Destination $userConfigPath -Force
    }
    else {
        Set-Content -LiteralPath $userConfigPath -Value $defaultConfigContent -Encoding UTF8
    }
    $mainConfigurationWasWritten = $true
    Write-Host "已安装默认配置：$userConfigPath"
}

Ensure-MainConfigurationReferences -Path $userConfigPath

Ensure-DefaultModuleTemplate `
    -Path $defaultModelRouteSetTemplatePath `
    -Content $defaultModelRouteSetTemplateContent `
    -Label "默认模型路由方案模板" `
    -GeneratedMarkers @("[model_route_sets.default]", "TianShu route-set-first model routing template") `
    -RequiredMarkers @("openai", "gpt-5.5", "responses", "anthropic", "claude-opus-4.8", "anthropic_messages", "openai-compatible", "openai-compatible-default", "openai_chat_completions") `
    -StaleMarkers @("protocol = `"auto`"", "google_generative")

Ensure-DefaultModuleTemplate `
    -Path $defaultModelProtocolRulesTemplatePath `
    -Content $defaultModelProtocolRulesTemplateContent `
    -Label "默认模型协议规则模板" `
    -GeneratedMarkers @("[model_protocol_rule_sets.default]", "Common model-family to wire protocol priority rules") `
    -RequiredMarkers @("gpt-*", "openai_responses", "claude-opus-4.8", "anthropic_messages", "openai-compatible-default", "openai_chat_completions") `
    -StaleMarkers @("google_generative", "protocols = [`"anthropic_messages`", `"openai_chat_completions`"]")

Ensure-DefaultModuleTemplate `
    -Path $defaultProviderInstanceTemplatePath `
    -Content $defaultProviderInstanceTemplateContent `
    -Label "默认 Provider instance 模板" `
    -GeneratedMarkers @("TianShu 默认 Provider instance 模板", "[providers.openai-compatible]") `
    -RequiredMarkers @("[providers.openai]", "OPENAI_API_KEY", "openai_responses", "[providers.openai-compatible]", "OPENAI_COMPATIBLE_API_KEY", "openai-compatible-default", "openai_chat_completions", "[providers.anthropic]", "ANTHROPIC_API_KEY", "claude-opus-4.8", "anthropic_messages") `
    -RefreshWhenRequiredMarkersMissing `
    -StaleMarkers @("default_protocol = `"auto`"", "google_generative")

Ensure-TemplateFile -Path $defaultAgentTemplatePath -Content $defaultAgentTemplateContent -Label "默认 Agent 配置模块"
Ensure-TemplateFile -Path $defaultExecutionProfileTemplatePath -Content $defaultExecutionProfileTemplateContent -Label "默认执行配置模块"
Ensure-TemplateFile -Path $defaultSessionProfileTemplatePath -Content $defaultSessionProfileTemplateContent -Label "默认会话配置模块"
Ensure-TemplateFile -Path $defaultConversationProfileTemplatePath -Content $defaultConversationProfileTemplateContent -Label "默认对话配置模块"
Ensure-TemplateFile -Path $defaultMemoryProfileTemplatePath -Content $defaultMemoryProfileContent -Label "默认 Memory 配置文件模块"
Ensure-TemplateFile -Path $defaultMemorySpaceTemplatePath -Content $defaultMemorySpaceContent -Label "默认 Memory Space 模块"
Ensure-TemplateFile -Path $defaultMemoryProviderTemplatePath -Content $defaultMemoryProviderContent -Label "默认 Memory Provider 模块"
Ensure-TemplateFile -Path $defaultMemoryBindingTemplatePath -Content $defaultMemoryBindingContent -Label "默认 Memory Binding 模块"
Ensure-TemplateFile -Path $defaultToolProfileTemplatePath -Content $defaultToolProfileContent -Label "默认工具配置模块"
Ensure-TemplateFile -Path $defaultWorkspaceProfileTemplatePath -Content $defaultWorkspaceProfileContent -Label "默认工作空间配置模块"
Ensure-TemplateFile -Path $defaultPermissionProfileTemplatePath -Content $defaultPermissionProfileContent -Label "默认审批配置模块"
Ensure-TemplateFile -Path $defaultGovernanceProfileTemplatePath -Content $defaultGovernanceProfileContent -Label "默认治理配置模块"
Ensure-TemplateFile -Path $defaultWorkspaceSandboxTemplatePath -Content $defaultWorkspaceSandboxContent -Label "默认 workspace-write 沙箱模块"
Ensure-TemplateFile -Path $defaultDangerSandboxTemplatePath -Content $defaultDangerSandboxContent -Label "默认 danger-full-access 沙箱模块"
Ensure-TemplateFile -Path $defaultTuiProfileTemplatePath -Content $defaultTuiProfileContent -Label "默认 TUI 配置模块"
Ensure-TemplateFile -Path $defaultFeatureProfileTemplatePath -Content $defaultFeatureProfileContent -Label "默认功能配置模块"
Ensure-TemplateFile -Path $defaultRealtimeProfileTemplatePath -Content $defaultRealtimeProfileContent -Label "默认 Realtime 配置模块"
Ensure-TemplateFile -Path $defaultIdentityProfileTemplatePath -Content $defaultIdentityProfileContent -Label "默认身份配置模块"
Ensure-TemplateFile -Path $defaultAccountTemplatePath -Content $defaultAccountContent -Label "默认账号配置模块"
Ensure-TemplateFile -Path $defaultDeviceTemplatePath -Content $defaultDeviceContent -Label "默认设备配置模块"
Ensure-TemplateFile -Path $defaultCollaborationProfileTemplatePath -Content $defaultCollaborationProfileContent -Label "默认协作配置模块"
Ensure-TemplateFile -Path $defaultWorkflowProfileTemplatePath -Content $defaultWorkflowProfileContent -Label "默认工作流配置模块"

if (-not (Test-Path $builtinToolManifestSourcePath)) {
    throw "找不到内置工具包 manifest：$builtinToolManifestSourcePath"
}

if (Test-Path $builtinToolManifestTargetPath) {
    if ($OverwriteConfig -and -not $PreserveConfig) {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $backupPath = Join-Path $builtinToolsDirectory "tool.toml.bak-$timestamp"
        Copy-Item -LiteralPath $builtinToolManifestTargetPath -Destination $backupPath -Force
        Copy-Item -LiteralPath $builtinToolManifestSourcePath -Destination $builtinToolManifestTargetPath -Force
        Write-Host "已按显式请求备份并更新内置工具包 manifest：$backupPath"
    }
    else {
        $builtinManifestText = Get-Content -LiteralPath $builtinToolManifestTargetPath -Raw
        if ($builtinManifestText -notmatch '(?m)^\s*\[\[providers\]\]\s*$' `
            -or $builtinManifestText -match 'TianShu\.Tooling\.BuiltinTools' `
            -or $builtinManifestText -match 'TianShu\.Tools\.BuiltinBundle' `
            -or $builtinManifestText -notmatch 'TianShu\.Tools\.Shell\.ShellToolProvider' `
            -or $builtinManifestText -notmatch 'TianShu\.Tools\.Interaction\.InteractionToolProvider' `
            -or $builtinManifestText -notmatch 'TianShu\.Tools\.Collaboration\.CollaborationToolProvider' `
            -or $builtinManifestText -notmatch 'TianShu\.Tools\.Fanout\.FanoutToolProvider' `
            -or $builtinManifestText -notmatch 'TianShu\.Tools\.Code\.CodeToolProvider' `
            -or $builtinManifestText -notmatch 'TianShu\.Tools\.Artifacts\.ArtifactToolProvider' `
            -or $builtinManifestText -notmatch 'TianShu\.Tools\.Memory\.MemoryToolProvider' `
            -or $builtinManifestText -notmatch 'TianShu\.Tools\.McpResources\.McpResourceToolProvider' `
            -or $builtinManifestText -notmatch 'TianShu\.Tools\.FileSystem\.FileSystemToolProvider' `
            -or $builtinManifestText -notmatch 'TianShu\.Tools\.FileSystemMutating\.MutatingFileSystemToolProvider') {
            $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
            $backupPath = Join-Path $builtinToolsDirectory "tool.toml.bak-$timestamp"
            Copy-Item -LiteralPath $builtinToolManifestTargetPath -Destination $backupPath -Force
            Copy-Item -LiteralPath $builtinToolManifestSourcePath -Destination $builtinToolManifestTargetPath -Force
            Write-Host "已刷新内置工具包 manifest 为工具域 Provider 格式，并备份旧 manifest：$backupPath"
        }
        Write-Host "保留已有内置工具包 manifest：$builtinToolManifestTargetPath"
    }
}
else {
    Copy-Item -LiteralPath $builtinToolManifestSourcePath -Destination $builtinToolManifestTargetPath -Force
    Write-Host "已安装内置工具包 manifest：$builtinToolManifestTargetPath"
}

$installedBuiltinManifestText = Get-Content -LiteralPath $builtinToolManifestTargetPath -Raw
if ($installedBuiltinManifestText -notmatch 'TianShu\.Tooling\.BuiltinTools') {
    Remove-InstallManagedStalePath -Path $legacyBuiltinToolsAssemblyPath -Label "旧 Tooling.BuiltinTools 工具程序集"
}

if ($installedBuiltinManifestText -notmatch 'TianShu\.Tools\.BuiltinBundle') {
    Remove-InstallManagedStalePath -Path $legacyBuiltinBundleDirectory -Label "旧 BuiltinBundle 工具目录"
}

if (-not (Test-Path $builtinProviderManifestSourcePath)) {
    throw "找不到内置模型 Provider 包 manifest：$builtinProviderManifestSourcePath"
}

if (Test-Path $builtinProviderManifestTargetPath) {
    if ($OverwriteConfig -and -not $PreserveConfig) {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $backupPath = Join-Path $builtinProvidersDirectory "provider.toml.bak-$timestamp"
        Copy-Item -LiteralPath $builtinProviderManifestTargetPath -Destination $backupPath -Force
        Copy-Item -LiteralPath $builtinProviderManifestSourcePath -Destination $builtinProviderManifestTargetPath -Force
        Write-Host "已按显式请求备份并更新内置模型 Provider 包 manifest：$backupPath"
    }
    else {
        $builtinProviderManifestText = Get-Content -LiteralPath $builtinProviderManifestTargetPath -Raw
        if ($builtinProviderManifestText -notmatch 'TianShu\.Provider\.OpenAICompatible\.dll' `
            -or $builtinProviderManifestText -notmatch 'TianShu\.Provider\.OpenAI\.dll' `
            -or $builtinProviderManifestText -notmatch 'TianShu\.Provider\.Anthropic\.dll' `
            -or $builtinProviderManifestText -notmatch 'TianShu\.Provider\.Google\.dll') {
            $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
            $backupPath = Join-Path $builtinProvidersDirectory "provider.toml.bak-$timestamp"
            Copy-Item -LiteralPath $builtinProviderManifestTargetPath -Destination $backupPath -Force
            Copy-Item -LiteralPath $builtinProviderManifestSourcePath -Destination $builtinProviderManifestTargetPath -Force
            Write-Host "已刷新内置模型 Provider 包 manifest，并备份旧 manifest：$backupPath"
        }
        Write-Host "保留已有内置模型 Provider 包 manifest：$builtinProviderManifestTargetPath"
    }
}
else {
    Copy-Item -LiteralPath $builtinProviderManifestSourcePath -Destination $builtinProviderManifestTargetPath -Force
    Write-Host "已安装内置模型 Provider 包 manifest：$builtinProviderManifestTargetPath"
}

if (-not (Test-Path $builtinArtifactStoreManifestSourcePath)) {
    throw "找不到内置工件存储包 manifest：$builtinArtifactStoreManifestSourcePath"
}

if (Test-Path $builtinArtifactStoreManifestTargetPath) {
    if ($OverwriteConfig -and -not $PreserveConfig) {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $backupPath = Join-Path $builtinArtifactStoresDirectory "store.toml.bak-$timestamp"
        Copy-Item -LiteralPath $builtinArtifactStoreManifestTargetPath -Destination $backupPath -Force
        Copy-Item -LiteralPath $builtinArtifactStoreManifestSourcePath -Destination $builtinArtifactStoreManifestTargetPath -Force
        Write-Host "已按显式请求备份并更新内置工件存储包 manifest：$backupPath"
    }
    else {
        $builtinArtifactStoreManifestText = Get-Content -LiteralPath $builtinArtifactStoreManifestTargetPath -Raw
        if ($builtinArtifactStoreManifestText -notmatch '(?m)^\s*\[\[stores\]\]\s*$' `
            -or $builtinArtifactStoreManifestText -notmatch 'local-filesystem' `
            -or $builtinArtifactStoreManifestText -notmatch 'root\s*=\s*"\./data"') {
            $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
            $backupPath = Join-Path $builtinArtifactStoresDirectory "store.toml.bak-$timestamp"
            Copy-Item -LiteralPath $builtinArtifactStoreManifestTargetPath -Destination $backupPath -Force
            Copy-Item -LiteralPath $builtinArtifactStoreManifestSourcePath -Destination $builtinArtifactStoreManifestTargetPath -Force
            Write-Host "已刷新内置工件存储包 manifest，并备份旧 manifest：$backupPath"
        }
        Write-Host "保留已有内置工件存储包 manifest：$builtinArtifactStoreManifestTargetPath"
    }
}
else {
    Copy-Item -LiteralPath $builtinArtifactStoreManifestSourcePath -Destination $builtinArtifactStoreManifestTargetPath -Force
    Write-Host "已安装内置工件存储包 manifest：$builtinArtifactStoreManifestTargetPath"
}

if (-not (Test-Path $builtinDiagnosticSinkManifestSourcePath)) {
    throw "找不到内置诊断 Sink 包 manifest：$builtinDiagnosticSinkManifestSourcePath"
}

if (Test-Path $builtinDiagnosticSinkManifestTargetPath) {
    if ($OverwriteConfig -and -not $PreserveConfig) {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $backupPath = Join-Path $builtinDiagnosticSinksDirectory "sink.toml.bak-$timestamp"
        Copy-Item -LiteralPath $builtinDiagnosticSinkManifestTargetPath -Destination $backupPath -Force
        Copy-Item -LiteralPath $builtinDiagnosticSinkManifestSourcePath -Destination $builtinDiagnosticSinkManifestTargetPath -Force
        Write-Host "已按显式请求备份并更新内置诊断 Sink 包 manifest：$backupPath"
    }
    else {
        $builtinDiagnosticSinkManifestText = Get-Content -LiteralPath $builtinDiagnosticSinkManifestTargetPath -Raw
        if ($builtinDiagnosticSinkManifestText -notmatch '(?m)^\s*\[\[sinks\]\]\s*$' `
            -or $builtinDiagnosticSinkManifestText -notmatch 'turn-log' `
            -or $builtinDiagnosticSinkManifestText -notmatch 'provider-request-artifacts' `
            -or $builtinDiagnosticSinkManifestText -notmatch 'telemetry') {
            $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
            $backupPath = Join-Path $builtinDiagnosticSinksDirectory "sink.toml.bak-$timestamp"
            Copy-Item -LiteralPath $builtinDiagnosticSinkManifestTargetPath -Destination $backupPath -Force
            Copy-Item -LiteralPath $builtinDiagnosticSinkManifestSourcePath -Destination $builtinDiagnosticSinkManifestTargetPath -Force
            Write-Host "已刷新内置诊断 Sink 包 manifest，并备份旧 manifest：$backupPath"
        }
        Write-Host "保留已有内置诊断 Sink 包 manifest：$builtinDiagnosticSinkManifestTargetPath"
    }
}
else {
    Copy-Item -LiteralPath $builtinDiagnosticSinkManifestSourcePath -Destination $builtinDiagnosticSinkManifestTargetPath -Force
    Write-Host "已安装内置诊断 Sink 包 manifest：$builtinDiagnosticSinkManifestTargetPath"
}

if (-not (Test-Path $builtinWorkspaceResolverManifestSourcePath)) {
    throw "找不到内置 Workspace Resolver 包 manifest：$builtinWorkspaceResolverManifestSourcePath"
}

if (Test-Path $builtinWorkspaceResolverManifestTargetPath) {
    if ($OverwriteConfig -and -not $PreserveConfig) {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $backupPath = Join-Path $builtinWorkspaceResolversDirectory "resolver.toml.bak-$timestamp"
        Copy-Item -LiteralPath $builtinWorkspaceResolverManifestTargetPath -Destination $backupPath -Force
        Copy-Item -LiteralPath $builtinWorkspaceResolverManifestSourcePath -Destination $builtinWorkspaceResolverManifestTargetPath -Force
        Write-Host "已按显式请求备份并更新内置 Workspace Resolver 包 manifest：$backupPath"
    }
    else {
        $builtinWorkspaceResolverManifestText = Get-Content -LiteralPath $builtinWorkspaceResolverManifestTargetPath -Raw
        if ($builtinWorkspaceResolverManifestText -notmatch '(?m)^\s*\[\[resolvers\]\]\s*$' `
            -or $builtinWorkspaceResolverManifestText -notmatch 'root_markers' `
            -or $builtinWorkspaceResolverManifestText -notmatch 'TianShu\.sln') {
            $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
            $backupPath = Join-Path $builtinWorkspaceResolversDirectory "resolver.toml.bak-$timestamp"
            Copy-Item -LiteralPath $builtinWorkspaceResolverManifestTargetPath -Destination $backupPath -Force
            Copy-Item -LiteralPath $builtinWorkspaceResolverManifestSourcePath -Destination $builtinWorkspaceResolverManifestTargetPath -Force
            Write-Host "已刷新内置 Workspace Resolver 包 manifest，并备份旧 manifest：$backupPath"
        }
        Write-Host "保留已有内置 Workspace Resolver 包 manifest：$builtinWorkspaceResolverManifestTargetPath"
    }
}
else {
    Copy-Item -LiteralPath $builtinWorkspaceResolverManifestSourcePath -Destination $builtinWorkspaceResolverManifestTargetPath -Force
    Write-Host "已安装内置 Workspace Resolver 包 manifest：$builtinWorkspaceResolverManifestTargetPath"
}

if (-not (Test-Path $builtinPolicyStrategyManifestSourcePath)) {
    throw "找不到内置审批策略包 manifest：$builtinPolicyStrategyManifestSourcePath"
}

if (Test-Path $builtinPolicyStrategyManifestTargetPath) {
    if ($OverwriteConfig -and -not $PreserveConfig) {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $backupPath = Join-Path $builtinPolicyStrategiesDirectory "policy.toml.bak-$timestamp"
        Copy-Item -LiteralPath $builtinPolicyStrategyManifestTargetPath -Destination $backupPath -Force
        Copy-Item -LiteralPath $builtinPolicyStrategyManifestSourcePath -Destination $builtinPolicyStrategyManifestTargetPath -Force
        Write-Host "已按显式请求备份并更新内置审批策略包 manifest：$backupPath"
    }
    else {
        $builtinPolicyStrategyManifestText = Get-Content -LiteralPath $builtinPolicyStrategyManifestTargetPath -Raw
        if ($builtinPolicyStrategyManifestText -notmatch '(?m)^\s*\[\[strategies\]\]\s*$' `
            -or $builtinPolicyStrategyManifestText -notmatch 'approval_policy' `
            -or $builtinPolicyStrategyManifestText -notmatch 'sandbox_mode' `
            -or $builtinPolicyStrategyManifestText -notmatch '(?m)^\s*\[\[strategies\.command_rules\]\]\s*$') {
            $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
            $backupPath = Join-Path $builtinPolicyStrategiesDirectory "policy.toml.bak-$timestamp"
            Copy-Item -LiteralPath $builtinPolicyStrategyManifestTargetPath -Destination $backupPath -Force
            Copy-Item -LiteralPath $builtinPolicyStrategyManifestSourcePath -Destination $builtinPolicyStrategyManifestTargetPath -Force
            Write-Host "已刷新内置审批策略包 manifest，并备份旧 manifest：$backupPath"
        }
        Write-Host "保留已有内置审批策略包 manifest：$builtinPolicyStrategyManifestTargetPath"
    }
}
else {
    Copy-Item -LiteralPath $builtinPolicyStrategyManifestSourcePath -Destination $builtinPolicyStrategyManifestTargetPath -Force
    Write-Host "已安装内置审批策略包 manifest：$builtinPolicyStrategyManifestTargetPath"
}

$vswherePath = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path -LiteralPath $vswherePath) {
    $vswhereDirectory = Split-Path -Parent $vswherePath
    if ($env:PATH -notlike "*$vswhereDirectory*") {
        $env:PATH = "$vswhereDirectory;$env:PATH"
    }
}

$publishArgs = @(
    "publish",
    $cliProjectPath,
    "-c",
    $Configuration,
    "-r",
    $RuntimeIdentifier,
    "--self-contained",
    "false",
    "-v",
    "minimal",
    "-p:DeleteExistingFiles=true",
    "-p:AllowedReferenceRelatedFileExtensions=",
    "/p:UseSharedCompilation=false",
    "-m:1",
    "/nodeReuse:false"
)

Write-Host "开始发布天枢 TianShu CLI framework-dependent"
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码：$LASTEXITCODE"
}

$tianshuExePath = Join-Path $binDirectory "tianshu.exe"
$publishedExePath = Join-Path $cliPublishDirectory "TianShu.Cli.exe"
if (-not (Test-Path $publishedExePath)) {
    throw "发布后未找到 TianShu.Cli.exe：$publishedExePath"
}
Copy-PublishDirectory -SourceDirectory $cliPublishDirectory -TargetDirectory $binDirectory -Label "CLI"
Copy-Item -LiteralPath $publishedExePath -Destination $tianshuExePath -Force
Write-Host "CLI framework-dependent 已安装：$tianshuExePath"

if (-not (Test-Path $tianshuExePath)) {
    throw "发布后未找到 tianshu.exe：$tianshuExePath"
}

Get-ChildItem -LiteralPath $binDirectory -Filter "*.pdb" -File | Remove-Item -Force

$configGuiPublishArgs = @(
    "publish",
    $configGuiProjectPath,
    "-c",
    $Configuration,
    "-r",
    $RuntimeIdentifier,
    "--self-contained",
    "false",
    "-v",
    "minimal",
    "-p:DeleteExistingFiles=true",
    "-m:1",
    "-p:AllowedReferenceRelatedFileExtensions=",
    "/p:UseSharedCompilation=false",
    "/nodeReuse:false"
)

Write-Host "开始发布天枢 TianShu ConfigGUI framework-dependent"
& dotnet @configGuiPublishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish ConfigGUI 失败，退出码：$LASTEXITCODE"
}

$publishedConfigGuiExePath = Join-Path $configGuiPublishDirectory "TianShu.ConfigGui.exe"
if (-not (Test-Path $publishedConfigGuiExePath)) {
    throw "发布后未找到 TianShu.ConfigGui.exe：$publishedConfigGuiExePath"
}

$configGuiExePath = Join-Path $binDirectory "TianShu.ConfigGui.exe"
Copy-PublishDirectory -SourceDirectory $configGuiPublishDirectory -TargetDirectory $binDirectory -Label "ConfigGUI"
Write-Host "ConfigGUI 已安装：$configGuiExePath"

Get-ChildItem -LiteralPath $appHostDirectory -Force | Remove-Item -Recurse -Force

$appHostPublishArgs = @(
    "publish",
    $appHostProjectPath,
    "-c",
    $Configuration,
    "-r",
    $RuntimeIdentifier,
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-p:UseSharedCompilation=false",
    "-p:AllowedReferenceRelatedFileExtensions=",
    "-m:1",
    "/nodeReuse:false",
    "--self-contained",
    "false",
    "-o",
    $appHostDirectory
)

Write-Host "开始发布天枢 TianShu AppHost framework-dependent 到：$appHostDirectory"
& dotnet @appHostPublishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish AppHost 失败，退出码：$LASTEXITCODE"
}

$appHostExePath = Join-Path $appHostDirectory "TianShu.AppHost.exe"
if (-not (Test-Path $appHostExePath)) {
    throw "发布后未找到 TianShu.AppHost.exe：$appHostExePath"
}

Get-ChildItem -LiteralPath $appHostDirectory -Filter "*.pdb" -File | Remove-Item -Force

foreach ($builtinToolProject in $builtinToolProjects) {
    $buildArgs = @(
        "build",
        $builtinToolProject.ProjectPath,
        "-c",
        $Configuration,
        "-v",
        "minimal",
        "-p:DebugType=none",
        "-p:DebugSymbols=false",
        "-p:AllowedReferenceRelatedFileExtensions=",
        "-m:1",
        "/p:UseSharedCompilation=false",
        "/nodeReuse:false"
    )

    Write-Host "开始构建天枢 TianShu $($builtinToolProject.Name)"
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build $($builtinToolProject.Name)失败，退出码：$LASTEXITCODE"
    }

    $sourcePath = Join-Path $builtinToolProject.BuildDirectory $builtinToolProject.AssemblyName
    if (-not (Test-Path $sourcePath)) {
        throw "构建后未找到$($builtinToolProject.Name)：$sourcePath"
    }

    New-Item -ItemType Directory -Path $builtinToolProject.TargetDirectory -Force | Out-Null
    $targetPath = Join-Path $builtinToolProject.TargetDirectory $builtinToolProject.AssemblyName
    Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
    Write-Host "$($builtinToolProject.Name)已安装：$targetPath"
}

foreach ($builtinProviderProject in $builtinProviderProjects) {
    $buildArgs = @(
        "build",
        $builtinProviderProject.ProjectPath,
        "-c",
        $Configuration,
        "-v",
        "minimal",
        "-p:DebugType=none",
        "-p:DebugSymbols=false",
        "-p:AllowedReferenceRelatedFileExtensions=",
        "-m:1",
        "/p:UseSharedCompilation=false",
        "/nodeReuse:false"
    )

    Write-Host "开始构建天枢 TianShu $($builtinProviderProject.Name)"
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build $($builtinProviderProject.Name)失败，退出码：$LASTEXITCODE"
    }

    $sourcePath = Join-Path $builtinProviderProject.BuildDirectory $builtinProviderProject.AssemblyName
    if (-not (Test-Path $sourcePath)) {
        throw "构建后未找到$($builtinProviderProject.Name)：$sourcePath"
    }

    New-Item -ItemType Directory -Path $builtinProviderProject.TargetDirectory -Force | Out-Null
    $targetPath = Join-Path $builtinProviderProject.TargetDirectory $builtinProviderProject.AssemblyName
    Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
    Write-Host "$($builtinProviderProject.Name)已安装：$targetPath"
}

[Environment]::SetEnvironmentVariable("TIANSHU_HOME", $tianShuHomeFullPath, "User")
$env:TIANSHU_HOME = $tianShuHomeFullPath

if (-not $SkipPathUpdate) {
    Add-UserPathEntry -PathEntry $binDirectory
}

Write-Host ""
Write-Host "天枢 TianShu CLI 已安装"
Write-Host "  TIANSHU_HOME   = $tianShuHomeFullPath"
Write-Host "  executable     = $tianshuExePath"
Write-Host "  config-gui     = $configGuiExePath"
Write-Host "  apphost        = $appHostExePath"
Write-Host "  config         = $userConfigPath"
Write-Host "  modules        = $modulesDirectory"
Write-Host "  data           = $dataDirectory"
Write-Host "  prompt-packs   = $promptPacksDirectory"
Write-Host "  tool-packages  = $toolsDirectory"
Write-Host "  provider-adapters= $providersDirectory"
Write-Host "  provider-instances= $providerInstancesDirectory"
Write-Host "  artifact-stores= $artifactStoresDirectory"
Write-Host "  diagnostic-sinks= $diagnosticSinksDirectory"
Write-Host "  workspace-resolvers= $workspaceResolversDirectory"
Write-Host "  policy-strategies= $policyStrategiesDirectory"
Write-Host "  mcp-servers    = $mcpServersDirectory"
Write-Host ""
Write-Host "当前 PowerShell 会话可直接运行："
Write-Host "  tianshu --help"
