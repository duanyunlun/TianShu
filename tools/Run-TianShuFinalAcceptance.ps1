param(
    [string]$RepoRoot,
    [string]$AcceptanceDocPath,
    [string]$TargetWorkspacePath,
    [string]$HarnessRootPath,
    [string]$ArtifactsRoot,
    [string]$ChatScriptPath,
    [string]$CliProjectPath,
    [string]$KernelProjectPath,
    [string]$ConfigPath,
    [string]$ProfileName,
    [int]$FirstToolTimeoutSeconds = 300,
    [int]$SecondToolTimeoutSeconds = 300,
    [int]$InterruptSettleTimeoutSeconds = 300,
    [Alias('FinalCompletionTimeoutSeconds')]
    [int]$FinalIdleWindowSeconds = 900,
    [string]$TransientResumePrompt = '刚才上游网络异常中断，请从当前线程状态继续未完成工作，不要重复已完成部分。',
    [switch]$ApproveAll,
    [switch]$VerboseEvents,
    [switch]$PrepareOnly,
    [switch]$SkipBuild,
    [switch]$SkipGuiLaunch,
    [int]$GuiLaunchProbeSeconds = 8,
    [int]$SubAgentLiveRunsPerCell = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "[TianShu Final Acceptance] $Message"
}

function ConvertFrom-JsonCompat {
    param([Parameter(Mandatory = $true)][string]$Text)

    if ($PSVersionTable.PSVersion.Major -ge 6) {
        return $Text | ConvertFrom-Json -Depth 100
    }

    return $Text | ConvertFrom-Json
}

function Get-AbsolutePath {
    param([string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-PathUnderRoot {
    param(
        [string]$Path,
        [string]$Root
    )

    $resolvedPath = (Get-AbsolutePath -Path $Path).TrimEnd('\', '/')
    $resolvedRoot = (Get-AbsolutePath -Path $Root).TrimEnd('\', '/')
    $rootWithSeparator = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar

    if ($resolvedPath.Equals($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    if ($resolvedPath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    throw "路径超出仓库根目录，已拒绝操作：$resolvedPath"
}

function Remove-DirectorySafe {
    param(
        [string]$Path,
        [string]$Root
    )

    Assert-PathUnderRoot -Path $Path -Root $Root
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Ensure-Directory {
    param(
        [string]$Path,
        [string]$Root
    )

    Assert-PathUnderRoot -Path $Path -Root $Root
    $null = New-Item -ItemType Directory -Path $Path -Force
}

function Prepare-AcceptanceConfigCopy {
    param(
        [string]$SourcePath,
        [string]$TargetPath,
        [string]$Root
    )

    if ([string]::IsNullOrWhiteSpace($SourcePath)) {
        return $null
    }

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        throw "验收配置源文件不存在：$SourcePath"
    }

    Assert-PathUnderRoot -Path $TargetPath -Root $Root
    $targetDirectory = Split-Path -Parent $TargetPath
    if (-not [string]::IsNullOrWhiteSpace($targetDirectory)) {
        Ensure-Directory -Path $targetDirectory -Root $Root
    }

    Copy-Item -LiteralPath $SourcePath -Destination $TargetPath -Force
    return (Get-AbsolutePath -Path $TargetPath)
}

function Copy-AcceptanceModuleConfigTree {
    param(
        [string]$SourceConfigPath,
        [string]$TargetConfigPath,
        [string]$Root
    )

    if ([string]::IsNullOrWhiteSpace($SourceConfigPath) -or [string]::IsNullOrWhiteSpace($TargetConfigPath)) {
        return $null
    }

    $sourceHomePath = Split-Path -Parent (Get-AbsolutePath -Path $SourceConfigPath)
    $targetHomePath = Split-Path -Parent (Get-AbsolutePath -Path $TargetConfigPath)
    $sourceModulesPath = Join-Path $sourceHomePath 'modules'
    $targetModulesPath = Join-Path $targetHomePath 'modules'

    Assert-PathUnderRoot -Path $targetModulesPath -Root $Root
    if (-not (Test-Path -LiteralPath $sourceModulesPath)) {
        return [pscustomobject]@{
            SourceModulesPath = $sourceModulesPath
            TargetModulesPath = $targetModulesPath
            Copied = $false
        }
    }

    if (Test-Path -LiteralPath $targetModulesPath) {
        Remove-Item -LiteralPath $targetModulesPath -Recurse -Force
    }

    Copy-Item -LiteralPath $sourceModulesPath -Destination $targetModulesPath -Recurse -Force
    return [pscustomobject]@{
        SourceModulesPath = $sourceModulesPath
        TargetModulesPath = $targetModulesPath
        Copied = $true
    }
}

function Get-ProviderBaseUrlFromModule {
    param(
        [string]$ProviderModulePath,
        [string]$FallbackBaseUrl
    )

    if (-not (Test-Path -LiteralPath $ProviderModulePath)) {
        return $FallbackBaseUrl
    }

    $match = Select-String -LiteralPath $ProviderModulePath -Pattern '^\s*base_url\s*=\s*"([^"]+)"' | Select-Object -First 1
    if ($null -eq $match -or $match.Matches.Count -eq 0) {
        return $FallbackBaseUrl
    }

    $value = $match.Matches[0].Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $FallbackBaseUrl
    }

    return $value
}

function Set-AcceptanceLiveModelModuleDefaults {
    param(
        [string]$TargetConfigPath,
        [string]$Root
    )

    $targetHomePath = Split-Path -Parent (Get-AbsolutePath -Path $TargetConfigPath)
    $modelModulesPath = Join-Path $targetHomePath 'modules\model'
    $providerInstancesPath = Join-Path $modelModulesPath 'provider-instances\default.toml'
    $routeSetPath = Join-Path $modelModulesPath 'route-sets\default.toml'
    $protocolRulesPath = Join-Path $modelModulesPath 'protocol-rules\default.toml'

    foreach ($path in @($providerInstancesPath, $routeSetPath, $protocolRulesPath)) {
        Assert-PathUnderRoot -Path $path -Root $Root
        $directory = Split-Path -Parent $path
        Ensure-Directory -Path $directory -Root $Root
    }

    $baseUrl = Get-ProviderBaseUrlFromModule -ProviderModulePath $providerInstancesPath -FallbackBaseUrl 'https://api.openai.com'
    $providerInstancesContent = @"
# TianShu final acceptance live provider matrix.
# This file only stores endpoint references and API key environment variable names, never secret values.

[providers.openai]
base_url = "$baseUrl"
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
base_url = "$baseUrl"
api_key_env = "OPENAI_API_KEY"
default_protocol = "openai_chat_completions"
protocol_fallbacks = ["openai_chat_completions"]
model_overrides = [{ name = "openai-compatible-default", protocols = ["openai_chat_completions"] }]
request_max_retries = 2
stream_max_retries = 2
stream_idle_timeout_ms = 30000
websocket_connect_timeout_ms = 15000
supports_websockets = false

[providers.anthropic]
base_url = "$baseUrl"
api_key_env = "OPENAI_API_KEY"
default_protocol = "anthropic_messages"
protocol_fallbacks = ["anthropic_messages"]
model_overrides = [{ name = "claude-opus-4.8", protocols = ["anthropic_messages"] }]
request_max_retries = 2
stream_max_retries = 2
stream_idle_timeout_ms = 30000
websocket_connect_timeout_ms = 15000
supports_websockets = false
"@

    $routeSetContent = @'
# TianShu final acceptance live model route set.
# Default route intentionally uses Responses + gpt-5.5 because it is the current stable final-acceptance model.

[model_route_sets.default]
display_name = "Final Acceptance Model Route Set"
description = "Routes used by TianShu final acceptance against current live provider modules."
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

    $protocolRulesContent = @'
# TianShu final acceptance live protocol rules.

[model_protocol_rule_sets.default]
display_name = "Final Acceptance Protocol Rules"
description = "Explicit protocol mapping for current live final-acceptance models."
rules = [
  { match = "openai/gpt-*", protocols = ["openai_responses"] },
  { match = "gpt-*", protocols = ["openai_responses"] },
  { match = "claude-opus-4.8", protocols = ["anthropic_messages"] },
  { match = "claude*", protocols = ["anthropic_messages"] },
  { match = "openai-compatible-default", protocols = ["openai_chat_completions"] },
  { match = "deepseek*", protocols = ["openai_chat_completions"] },
]
'@

    Set-Content -LiteralPath $providerInstancesPath -Value $providerInstancesContent -Encoding UTF8
    Set-Content -LiteralPath $routeSetPath -Value $routeSetContent -Encoding UTF8
    Set-Content -LiteralPath $protocolRulesPath -Value $protocolRulesContent -Encoding UTF8

    return [pscustomobject]@{
        ProviderInstancesPath = $providerInstancesPath
        RouteSetPath = $routeSetPath
        ProtocolRulesPath = $protocolRulesPath
        DefaultProvider = 'openai'
        DefaultModel = 'gpt-5.5'
        DefaultProtocol = 'openai_responses'
        ChatCompletionsProvider = 'openai-compatible'
        ChatCompletionsModel = 'openai-compatible-default'
        AnthropicProvider = 'anthropic'
        AnthropicModel = 'claude-opus-4.8'
        BaseUrlSource = $baseUrl
    }
}

function ConvertTo-AcceptanceSafeId {
    param([string]$Value)

    $normalized = if ([string]::IsNullOrWhiteSpace($Value)) { 'item' } else { $Value.Trim().ToLowerInvariant() }
    $safe = [Regex]::Replace($normalized, '[^a-z0-9._-]+', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($safe)) {
        return 'item'
    }

    return $safe
}

function New-SubAgentLiveScenarioSet {
    param(
        [string]$ConfigGuiPromptLine,
        [string]$ProviderMatrixPromptLine,
        [string]$AcceptanceEvidencePromptLine
    )

    return ,@(
        [pscustomobject]@{
            Id = 'config-gui'
            Label = 'Config GUI audit planning'
            PromptLine = $ConfigGuiPromptLine
        },
        [pscustomobject]@{
            Id = 'provider-matrix'
            Label = 'Provider protocol compatibility audit'
            PromptLine = $ProviderMatrixPromptLine
        },
        [pscustomobject]@{
            Id = 'acceptance-evidence'
            Label = 'Final acceptance evidence audit'
            PromptLine = $AcceptanceEvidencePromptLine
        }
    )
}

function New-SubAgentLiveModelCellSet {
    return ,@(
        [pscustomobject]@{
            Id = 'openai-gpt-5.5-responses'
            Provider = 'openai'
            Model = 'gpt-5.5'
            Protocol = 'openai_responses'
            ApiKeyEnv = 'OPENAI_API_KEY'
        },
        [pscustomobject]@{
            Id = 'anthropic-claude-opus-4.8-messages'
            Provider = 'anthropic'
            Model = 'claude-opus-4.8'
            Protocol = 'anthropic_messages'
            ApiKeyEnv = 'OPENAI_API_KEY'
        },
        [pscustomobject]@{
            Id = 'openai-compatible-default-chat-completions'
            Provider = 'openai-compatible'
            Model = 'openai-compatible-default'
            Protocol = 'openai_chat_completions'
            ApiKeyEnv = 'OPENAI_API_KEY'
        }
    )
}

function New-SubAgentLiveCellConfig {
    param(
        [string]$SourceConfigPath,
        [object]$ModelCell,
        [string]$ScenarioId,
        [int]$RunIndex,
        [string]$LiveRoot,
        [string]$Root
    )

    $cellId = ConvertTo-AcceptanceSafeId -Value ([string]$ModelCell.Id)
    $configDirectory = Join-Path $LiveRoot (Join-Path 'configs' (Join-Path $ScenarioId (Join-Path $cellId ("run-$RunIndex"))))
    Ensure-Directory -Path $configDirectory -Root $Root

    $targetConfigPath = Join-Path $configDirectory 'tianshu.toml'
    $copiedConfigPath = Prepare-AcceptanceConfigCopy -SourcePath $SourceConfigPath -TargetPath $targetConfigPath -Root $Root
    $null = Copy-AcceptanceModuleConfigTree -SourceConfigPath $SourceConfigPath -TargetConfigPath $copiedConfigPath -Root $Root
    $null = Set-AcceptanceLiveModelCellDefaults -TargetConfigPath $copiedConfigPath -Root $Root -ModelCell $ModelCell
    return $copiedConfigPath
}

function Set-AcceptanceLiveModelCellDefaults {
    param(
        [string]$TargetConfigPath,
        [string]$Root,
        [object]$ModelCell
    )

    $targetHomePath = Split-Path -Parent (Get-AbsolutePath -Path $TargetConfigPath)
    $modelModulesPath = Join-Path $targetHomePath 'modules\model'
    $providerInstancesPath = Join-Path $modelModulesPath 'provider-instances\default.toml'
    $routeSetPath = Join-Path $modelModulesPath 'route-sets\default.toml'
    $protocolRulesPath = Join-Path $modelModulesPath 'protocol-rules\default.toml'

    foreach ($path in @($providerInstancesPath, $routeSetPath, $protocolRulesPath)) {
        Assert-PathUnderRoot -Path $path -Root $Root
        $directory = Split-Path -Parent $path
        Ensure-Directory -Path $directory -Root $Root
    }

    $baseUrl = Get-ProviderBaseUrlFromModule -ProviderModulePath $providerInstancesPath -FallbackBaseUrl 'https://api.openai.com'
    $provider = [string]$ModelCell.Provider
    $model = [string]$ModelCell.Model
    $protocol = [string]$ModelCell.Protocol
    $apiKeyEnv = [string]$ModelCell.ApiKeyEnv

    $providerInstancesContent = @"
# TianShu final acceptance Sub-Agent live observation cell.
# This file freezes one provider/model/protocol cell and never stores secret values.

[providers.$provider]
base_url = "$baseUrl"
api_key_env = "$apiKeyEnv"
default_protocol = "$protocol"
protocol_fallbacks = ["$protocol"]
model_overrides = [{ name = "$model", protocols = ["$protocol"] }]
request_max_retries = 2
stream_max_retries = 2
stream_idle_timeout_ms = 30000
websocket_connect_timeout_ms = 15000
supports_websockets = false

[providers.$provider.reasoning]
enabled = true
effort = "medium"
summary = "auto"
verbosity = "normal"
budget_tokens = 4096
"@

    $routeSetContent = @"
# TianShu final acceptance Sub-Agent live observation route set.

[model_route_sets.default]
display_name = "Sub-Agent Live Observation Cell"
description = "Frozen route set for one provider/model/protocol observation cell."
routes = [
  { kind = "default", candidates = [{ provider = "$provider", model = "$model", protocol = "$protocol" }] },
  { kind = "planning", candidates = [{ provider = "$provider", model = "$model", protocol = "$protocol" }] },
  { kind = "coding", candidates = [{ provider = "$provider", model = "$model", protocol = "$protocol" }] },
  { kind = "review", candidates = [{ provider = "$provider", model = "$model", protocol = "$protocol" }] },
  { kind = "summarization", candidates = [{ provider = "$provider", model = "$model", protocol = "$protocol" }] },
  { kind = "memory_extraction", candidates = [{ provider = "$provider", model = "$model", protocol = "$protocol" }] },
  { kind = "long_context", candidates = [{ provider = "$provider", model = "$model", protocol = "$protocol" }] },
  { kind = "fast", candidates = [{ provider = "$provider", model = "$model", protocol = "$protocol" }] },
]
"@

    $protocolRulesContent = @"
# TianShu final acceptance Sub-Agent live observation protocol rules.

[model_protocol_rule_sets.default]
display_name = "Sub-Agent Live Observation Protocol Rules"
description = "Frozen protocol rule for one provider/model/protocol observation cell."
rules = [
  { match = "$model", protocols = ["$protocol"] },
  { match = "$provider/$model", protocols = ["$protocol"] },
]
"@

    Set-Content -LiteralPath $providerInstancesPath -Value $providerInstancesContent -Encoding UTF8
    Set-Content -LiteralPath $routeSetPath -Value $routeSetContent -Encoding UTF8
    Set-Content -LiteralPath $protocolRulesPath -Value $protocolRulesContent -Encoding UTF8

    return [pscustomobject]@{
        ProviderInstancesPath = $providerInstancesPath
        RouteSetPath = $routeSetPath
        ProtocolRulesPath = $protocolRulesPath
        Provider = $provider
        Model = $model
        Protocol = $protocol
        BaseUrlSource = $baseUrl
    }
}

function Invoke-WithTemporaryEnvironment {
    param(
        [hashtable]$Variables,
        [scriptblock]$ScriptBlock
    )

    if ($null -eq $Variables) {
        & $ScriptBlock
        return
    }

    $previousValues = @{}
    foreach ($entry in $Variables.GetEnumerator()) {
        $name = [string]$entry.Key
        $previousValues[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, [string]$entry.Value)
    }

    try {
        & $ScriptBlock
    }
    finally {
        foreach ($entry in $previousValues.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable([string]$entry.Key, $entry.Value)
        }
    }
}

function Get-MarkdownPromptBlock {
    param(
        [string]$MarkdownPath,
        [string]$MarkerName
    )

    $lines = [System.IO.File]::ReadAllLines(
        (Get-AbsolutePath -Path $MarkdownPath),
        [System.Text.Encoding]::UTF8)
    $markerPattern = '^\s*<!--\s*acceptance-prompt:' + [Regex]::Escape($MarkerName) + '\s*-->\s*$'
    $markerIndex = -1
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match $markerPattern) {
            $markerIndex = $i
            break
        }
    }

    if ($markerIndex -lt 0) {
        throw "未在验收文档中找到提示词标记：$MarkerName"
    }

    $fenceStart = -1
    for ($i = $markerIndex + 1; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^\s*```') {
            $fenceStart = $i
            break
        }
    }

    if ($fenceStart -lt 0) {
        throw "提示词标记 $MarkerName 后缺少代码块起始围栏。"
    }

    $buffer = [System.Collections.Generic.List[string]]::new()
    for ($i = $fenceStart + 1; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^\s*```') {
            break
        }

        $buffer.Add($lines[$i])
    }

    if ($buffer.Count -eq 0) {
        throw "提示词标记 $MarkerName 的代码块为空。"
    }

    return ($buffer -join [Environment]::NewLine).Trim()
}

function ConvertTo-ScriptLine {
    param([string]$Text)

    $segments = $Text -split '\r?\n' |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_.Length -gt 0 }
    return ($segments -join ' ').Trim()
}

function Get-TianShuSessionsRoot {
    param([string]$SessionsHomePath)

    $sessionsHome = if ([string]::IsNullOrWhiteSpace($SessionsHomePath)) {
        if (-not [string]::IsNullOrWhiteSpace($env:TIANSHU_SESSIONS_HOME)) {
            $env:TIANSHU_SESSIONS_HOME
        }
        else {
            $env:TIANSHU_SESSIONS_HOME
        }
    }
    else {
        $SessionsHomePath
    }

    if (-not [string]::IsNullOrWhiteSpace($sessionsHome)) {
        return $sessionsHome
    }

    $TianShuHome = $env:TIANSHU_HOME
    if ([string]::IsNullOrWhiteSpace($TianShuHome)) {
        $TianShuHome = $env:TIANSHU_HOME
    }

    if ([string]::IsNullOrWhiteSpace($TianShuHome)) {
        $TianShuHome = Join-Path $env:USERPROFILE '.tianshu'
    }

    return Join-Path $TianShuHome 'sessions'
}

function Get-LatestArtifactsRunDirectory {
    param([string]$RootPath)

    if (-not (Test-Path -LiteralPath $RootPath)) {
        return $null
    }

    return Get-ChildItem -LiteralPath $RootPath -Directory |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-ArchitectureAlignedChatInvocation {
    param(
        [string[]]$InvokeArgs,
        [string]$CliProjectPath
    )

    Assert-Condition ($InvokeArgs.Count -ge 5) "CLI 验收调用参数不完整，无法确认架构路径。"
    Assert-Condition ([string]::Equals($InvokeArgs[0], 'run', [System.StringComparison]::OrdinalIgnoreCase)) "最终验收必须通过 dotnet run 启动当前分支 CLI 开发入口，不得直接调用已安装 tianshu.exe。"
    Assert-Condition ([string]::Equals($InvokeArgs[1], '--project', [System.StringComparison]::OrdinalIgnoreCase)) "最终验收缺少 dotnet run --project，无法证明使用当前分支 CLI 项目。"
    Assert-Condition (Test-AbsolutePathEquals -Left $InvokeArgs[2] -Right $CliProjectPath) "最终验收使用的 CLI 项目路径与当前分支入参不一致。"
    Assert-Condition (-not ($InvokeArgs -contains '--apphost-control-plane')) "最终验收命令不得包含已移除的 --apphost-control-plane 旧执行路径。"

    $installedCliArgs = @($InvokeArgs | Where-Object { [string]$_ -match '(?i)(^|[\\/])tianshu\.exe$' })
    Assert-Condition ($installedCliArgs.Count -eq 0) "最终验收不得直接调用用户级已安装 tianshu.exe。"
}

function Assert-ArchitectureAlignedSendInvocation {
    param(
        [string[]]$InvokeArgs,
        [string]$CliProjectPath
    )

    Assert-Condition ($InvokeArgs.Count -ge 6) "最终验收 send 命令参数不完整。"
    Assert-Condition ([string]::Equals($InvokeArgs[0], 'run', [System.StringComparison]::OrdinalIgnoreCase)) "最终验收 send 必须通过 dotnet run 启动当前分支 CLI 开发入口。"
    Assert-Condition ([string]::Equals($InvokeArgs[1], '--project', [System.StringComparison]::OrdinalIgnoreCase)) "最终验收 send 缺少 dotnet run --project。"
    Assert-Condition (Test-AbsolutePathEquals -Left $InvokeArgs[2] -Right $CliProjectPath) "最终验收 send 使用的 CLI 项目路径与当前分支入参不一致。"
    Assert-Condition ($InvokeArgs -contains 'send') "最终验收 send 命令缺少 send 子命令。"
    Assert-Condition ($InvokeArgs -contains '--kernel-runtime-loop') "Sub-Agent live 验收必须走当前 Kernel→Runtime loop。"
    Assert-Condition ($InvokeArgs -contains '--enable-subagents') "Sub-Agent live 验收必须显式开放 --enable-subagents。"
    Assert-Condition ($InvokeArgs -contains '--approve-all') "Sub-Agent live 验收必须显式携带 --approve-all 作为 HostMutation 授权边界。"
    Assert-Condition (-not ($InvokeArgs -contains '--apphost-control-plane')) "Sub-Agent live 验收不得包含已移除的 --apphost-control-plane 旧执行路径。"
}

function ConvertFrom-CommandJsonOutput {
    param([string]$Text)

    $trimmed = ([string]$Text).Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        throw "命令未输出 JSON 摘要。"
    }

    try {
        return ConvertFrom-JsonCompat -Text $trimmed
    }
    catch {
        $start = $trimmed.IndexOf('{')
        $end = $trimmed.LastIndexOf('}')
        if ($start -lt 0 -or $end -le $start) {
            throw "命令输出中未找到可解析 JSON 摘要。原始输出：$trimmed"
        }

        $json = $trimmed.Substring($start, $end - $start + 1)
        return ConvertFrom-JsonCompat -Text $json
    }
}

function Get-RequiredJsonFile {
    param(
        [string]$Path,
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label 不存在：$Path"
    }

    $content = [System.IO.File]::ReadAllText(
        (Get-AbsolutePath -Path $Path),
        [System.Text.Encoding]::UTF8)
    if ([string]::IsNullOrWhiteSpace($content)) {
        throw "$Label 为空：$Path"
    }

    return ConvertFrom-JsonCompat -Text $content
}

function Get-RequiredJsonLines {
    param(
        [string]$Path,
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label 不存在：$Path"
    }

    $lines = [System.IO.File]::ReadAllLines(
        (Get-AbsolutePath -Path $Path),
        [System.Text.Encoding]::UTF8) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($lines.Count -eq 0) {
        throw "$Label 为空：$Path"
    }

    return ,@($lines | ForEach-Object { ConvertFrom-JsonCompat -Text $_ })
}

function Get-RequiredTextLines {
    param(
        [string]$Path,
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label 不存在：$Path"
    }

    $lines = [System.IO.File]::ReadAllLines(
        (Get-AbsolutePath -Path $Path),
        [System.Text.Encoding]::UTF8) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($lines.Count -eq 0) {
        throw "$Label 为空：$Path"
    }

    return ,@($lines)
}

function Get-JsonPropertyValue {
    param(
        [object]$Object,
        [string]$PropertyName,
        [string]$Label
    )

    if ($null -eq $Object) {
        throw "$Label 为空，无法读取属性：$PropertyName"
    }

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        throw "$Label 缺少属性：$PropertyName"
    }

    return $property.Value
}

function TryGet-JsonPropertyValue {
    param(
        [object]$Object,
        [string]$PropertyName
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Test-AbsolutePathEquals {
    param(
        [string]$Left,
        [string]$Right
    )

    if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) {
        return $false
    }

    $normalizedLeft = (Get-AbsolutePath -Path $Left).TrimEnd('\', '/')
    $normalizedRight = (Get-AbsolutePath -Path $Right).TrimEnd('\', '/')
    return $normalizedLeft.Equals($normalizedRight, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-OptionalFileSha256 {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Join-NonEmptyText {
    param(
        [object[]]$Values,
        [string]$Separator = [Environment]::NewLine
    )

    if ($null -eq $Values) {
        return string.Empty
    }

    $segments = [System.Collections.Generic.List[string]]::new()
    foreach ($value in $Values) {
        if ($null -eq $value) {
            continue
        }

        $text = [string]$value
        if ([string]::IsNullOrWhiteSpace($text)) {
            continue
        }

        $segments.Add($text)
    }

    return ($segments -join $Separator)
}

function Get-EventToolNames {
    param([object[]]$Events)

    $names = [System.Collections.Generic.List[string]]::new()
    foreach ($event in @($Events)) {
        foreach ($candidate in @(
            (TryGet-JsonPropertyValue -Object $event -PropertyName 'ToolName'),
            (TryGet-JsonPropertyValue -Object $event -PropertyName 'toolName'),
            (TryGet-JsonPropertyValue -Object $event -PropertyName 'Name'),
            (TryGet-JsonPropertyValue -Object $event -PropertyName 'name')
        )) {
            $text = [string]$candidate
            if (-not [string]::IsNullOrWhiteSpace($text)) {
                $names.Add($text)
            }
        }

        $toolCall = TryGet-JsonPropertyValue -Object $event -PropertyName 'ToolCall'
        if ($null -eq $toolCall) {
            $toolCall = TryGet-JsonPropertyValue -Object $event -PropertyName 'toolCall'
        }

        foreach ($candidate in @(
            (TryGet-JsonPropertyValue -Object $toolCall -PropertyName 'ToolName'),
            (TryGet-JsonPropertyValue -Object $toolCall -PropertyName 'toolName'),
            (TryGet-JsonPropertyValue -Object $toolCall -PropertyName 'Name'),
            (TryGet-JsonPropertyValue -Object $toolCall -PropertyName 'name'),
            (TryGet-JsonPropertyValue -Object $toolCall -PropertyName 'FunctionName'),
            (TryGet-JsonPropertyValue -Object $toolCall -PropertyName 'functionName')
        )) {
            $text = [string]$candidate
            if (-not [string]::IsNullOrWhiteSpace($text)) {
                $names.Add($text)
            }
        }
    }

    return ,@($names)
}

function Test-SubagentEvidence {
    param(
        [object[]]$Events,
        [object[]]$ThreadLogEntries,
        [string]$TranscriptText
    )

    $toolNames = Get-EventToolNames -Events $Events
    if (($toolNames | Where-Object { [string]::Equals($_, 'spawn_agent', [System.StringComparison]::OrdinalIgnoreCase) } | Measure-Object).Count -gt 0) {
        return $true
    }

    $threadText = Join-NonEmptyText -Values @($ThreadLogEntries | ForEach-Object { $_ | ConvertTo-Json -Depth 20 -Compress })
    $combinedText = Join-NonEmptyText -Values @($TranscriptText, $threadText)
    return $combinedText -match '(?i)\bspawn_agent\b' -or $combinedText -match '(?i)\bsubagent[_\.-]'
}

function Test-SubAgentMechanismEvidence {
    param(
        [object]$Evidence,
        [string]$EvidenceText
    )

    return [bool]$Evidence.success `
        -and [bool]$Evidence.moduleCapabilityStepObserved `
        -and [bool]$Evidence.subAgentBridgeObserved `
        -and [bool]$Evidence.parentSecondModelReceivedToolResult `
        -and $EvidenceText.Contains('spawn_agent') `
        -and $EvidenceText.Contains('module.sub_agent') `
        -and $EvidenceText.Contains('sub_agent.spawn')
}

function Test-SubAgentLiveSpawnObserved {
    param(
        [object]$Summary,
        [string]$SummaryText
    )

    $executionEvidence = @()
    foreach ($propertyName in @('replaySummary', 'ReplaySummary')) {
        $value = TryGet-JsonPropertyValue -Object $Summary -PropertyName $propertyName
        if ($null -ne $value) {
            $executionEvidence += ($value | ConvertTo-Json -Depth 80 -Compress)
        }
    }

    foreach ($propertyName in @('kernelRuntimeTerminalProjection', 'KernelRuntimeTerminalProjection')) {
        $value = TryGet-JsonPropertyValue -Object $Summary -PropertyName $propertyName
        if ($null -ne $value) {
            $executionEvidence += ($value | ConvertTo-Json -Depth 80 -Compress)
        }
    }

    $text = Join-NonEmptyText -Values $executionEvidence
    if ($text -match '(?i)tool-exec-request[^\r\n"]*spawn_agent') {
        return $true
    }

    if ($text -match '(?i)module\.sub_agent' -or
        $text -match '(?i)sub_agent\.spawn' -or
        $text -match '(?i)subagent_module_bridge') {
        return $true
    }

    return $false
}

function Get-GeneratedWpfProjects {
    param([object[]]$Projects)

    return ,@(
        foreach ($project in @($Projects)) {
            $content = Get-Content -LiteralPath $project.FullName -Raw
            if ($content -match '<UseWPF>\s*true\s*</UseWPF>' -or $content -match '<UseWpf>\s*true\s*</UseWpf>') {
                $project
            }
        }
    )
}

function Invoke-GeneratedWpfLaunchProbe {
    param(
        [object]$Project,
        [int]$ProbeSeconds
    )

    $quotedProjectPath = '"' + $Project.FullName + '"'
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', $quotedProjectPath, '--no-build') -PassThru
    Start-Sleep -Seconds $ProbeSeconds

    if ($process.HasExited) {
        throw "GUI 启动探针失败，进程过早退出：project=$($Project.FullName), exitCode=$($process.ExitCode)"
    }

    return [pscustomobject]@{
        ProjectPath = $Project.FullName
        ProcessId = $process.Id
        ProbeSeconds = $ProbeSeconds
        StillRunning = -not $process.HasExited
    }
}

function Write-ChatScriptFile {
    param(
        [string]$Path,
        [string[]]$Lines,
        [string]$Root
    )

    Assert-PathUnderRoot -Path $Path -Root $Root
    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        Ensure-Directory -Path $directory -Root $Root
    }

    Set-Content -LiteralPath $Path -Value $Lines -Encoding UTF8
    return (Get-AbsolutePath -Path $Path)
}

function Get-MeaningfulWorkEvents {
    param([object[]]$Events)

    $meaningfulKinds = @(
        'AssistantTextDelta',
        'AssistantTextCompleted',
        'ToolCallStarted',
        'ToolCallCompleted',
        'TaskStarted',
        'TaskCompleted',
        'ReasoningDelta',
        'ReasoningCompleted',
        'ApprovalRequested',
        'PermissionRequested',
        'UserInputRequested',
        'CommandExecOutputDelta',
        'AgentJobProgress',
        'PlanUpdated',
        'OperationReported',
        'ItemStarted',
        'ItemCompleted',
        'TurnCompleted'
    )

    return ,@(
        $Events | Where-Object {
            $kind = [string](TryGet-JsonPropertyValue -Object $_ -PropertyName 'Kind')
            $meaningfulKinds -contains $kind
        }
    )
}

function Test-TransientNetworkFailure {
    param(
        [object]$Summary,
        [object[]]$Events,
        [string]$TranscriptText
    )

    $eventDiagnostics = [System.Collections.Generic.List[string]]::new()
    foreach ($event in @($Events | Select-Object -Last 80)) {
        $toolCall = TryGet-JsonPropertyValue -Object $event -PropertyName 'ToolCall'
        foreach ($candidate in @(
            (TryGet-JsonPropertyValue -Object $event -PropertyName 'Text'),
            (TryGet-JsonPropertyValue -Object $event -PropertyName 'Message'),
            (TryGet-JsonPropertyValue -Object $toolCall -PropertyName 'OutputText')
        )) {
            $text = [string]$candidate
            if (-not [string]::IsNullOrWhiteSpace($text)) {
                $eventDiagnostics.Add($text)
            }
        }
    }

    $diagnostics = Join-NonEmptyText -Values @(
        (TryGet-JsonPropertyValue -Object $Summary -PropertyName 'FailureMessage'),
        (TryGet-JsonPropertyValue -Object $Summary -PropertyName 'ResultText'),
        $TranscriptText,
        $eventDiagnostics
    )

    $patterns = @(
        'stream closed before response\.completed',
        'HTTP\s*502',
        'HTTP\s*503',
        'BadGateway',
        'ServiceUnavailable',
        'server_is_overloaded',
        'service_unavailable_error',
        'servers are currently overloaded',
        'ResponseEnded',
        'response ended prematurely',
        'responses stream emitted invalid JSON event',
        'connection was reset',
        'could not connect to server'
    )

    foreach ($pattern in $patterns) {
        if ($diagnostics -match $pattern) {
            return $true
        }
    }

    return $false
}

function Test-WaitCompleteTimeout {
    param(
        [object]$Summary,
        [string]$TranscriptText
    )

    $diagnostics = Join-NonEmptyText -Values @(
        (TryGet-JsonPropertyValue -Object $Summary -PropertyName 'FailureMessage'),
        $TranscriptText
    )

    return $diagnostics -match '等待回合结束超时'
}

function Get-ThreadLogState {
    param(
        [string]$ThreadId,
        [string]$SessionsHomePath
    )

    if ([string]::IsNullOrWhiteSpace($ThreadId)) {
        return $null
    }

    $threadLogPath = Join-Path (Get-TianShuSessionsRoot -SessionsHomePath $SessionsHomePath) ($ThreadId + '.jsonl')
    if (-not (Test-Path -LiteralPath $threadLogPath)) {
        return [pscustomobject]@{
            ThreadLogPath = $threadLogPath
            Entries = @()
            SessionEntry = $null
            LastTurnEntry = $null
        }
    }

    $entries = Get-RequiredJsonLines -Path $threadLogPath -Label '线程日志'
    $sessionEntry = $entries | Where-Object {
        [string](TryGet-JsonPropertyValue -Object $_ -PropertyName 'type') -in @('session_meta', 'session_state')
    } | Select-Object -Last 1
    $lastTurnEntry = $entries | Where-Object {
        [string](TryGet-JsonPropertyValue -Object $_ -PropertyName 'type') -eq 'turn'
    } | Select-Object -Last 1

    return [pscustomobject]@{
        ThreadLogPath = $threadLogPath
        Entries = $entries
        SessionEntry = $sessionEntry
        LastTurnEntry = $lastTurnEntry
    }
}

function Get-AcceptanceRunArtifacts {
    param(
        [string]$RunDirectory,
        [int]$ExitCode,
        [string]$SessionName,
        [string]$ScriptPath,
        [string[]]$ExpectedCommands
    )

    Assert-Condition (-not [string]::IsNullOrWhiteSpace($RunDirectory)) "会话 [$SessionName] 未生成工件目录。"

    $summaryPath = Join-Path $RunDirectory 'summary.json'
    $resolvedOptionsPath = Join-Path $RunDirectory 'resolved-options.json'
    $eventsPath = Join-Path $RunDirectory 'events.jsonl'
    $commandsPath = Join-Path $RunDirectory 'commands.txt'
    $transcriptPath = Join-Path $RunDirectory 'transcript.txt'
    $transcriptRecordsPath = Join-Path $RunDirectory 'transcript-records.jsonl'

    $summary = Get-RequiredJsonFile -Path $summaryPath -Label 'summary.json'
    $resolvedOptions = Get-RequiredJsonFile -Path $resolvedOptionsPath -Label 'resolved-options.json'
    $events = Get-RequiredJsonLines -Path $eventsPath -Label 'events.jsonl'
    $transcriptRecords = Get-RequiredJsonLines -Path $transcriptRecordsPath -Label 'transcript-records.jsonl'
    $commandLines = Get-RequiredTextLines -Path $commandsPath -Label 'commands.txt'
    $transcriptText = [System.IO.File]::ReadAllText(
        (Get-AbsolutePath -Path $transcriptPath),
        [System.Text.Encoding]::UTF8)
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($transcriptText)) "transcript.txt 为空：$transcriptPath"
    Assert-Condition ($transcriptRecords.Count -gt 0) "transcript-records.jsonl 为空：$transcriptRecordsPath"

    $meaningfulWorkEvents = Get-MeaningfulWorkEvents -Events $events
    $transientFailureDetected = Test-TransientNetworkFailure -Summary $summary -Events $events -TranscriptText $transcriptText
    $waitCompleteTimeoutDetected = Test-WaitCompleteTimeout -Summary $summary -TranscriptText $transcriptText

    return [pscustomobject]@{
        SessionName = $SessionName
        ExitCode = $ExitCode
        RunDirectory = $RunDirectory
        ScriptPath = $ScriptPath
        ExpectedCommands = $ExpectedCommands
        SummaryPath = $summaryPath
        ResolvedOptionsPath = $resolvedOptionsPath
        EventsPath = $eventsPath
        CommandsPath = $commandsPath
        TranscriptPath = $transcriptPath
        TranscriptRecordsPath = $transcriptRecordsPath
        Summary = $summary
        ResolvedOptions = $resolvedOptions
        Events = $events
        CommandLines = $commandLines
        TranscriptText = $transcriptText
        TranscriptRecords = $transcriptRecords
        ThreadId = [string](TryGet-JsonPropertyValue -Object $summary -PropertyName 'ThreadId')
        TurnId = [string](TryGet-JsonPropertyValue -Object $summary -PropertyName 'TurnId')
        TurnStatus = [string](TryGet-JsonPropertyValue -Object $summary -PropertyName 'TurnStatus')
        Success = [bool](TryGet-JsonPropertyValue -Object $summary -PropertyName 'Success')
        FailureMessage = [string](TryGet-JsonPropertyValue -Object $summary -PropertyName 'FailureMessage')
        ResultText = [string](TryGet-JsonPropertyValue -Object $summary -PropertyName 'ResultText')
        MeaningfulWorkEvents = $meaningfulWorkEvents
        MeaningfulWorkObserved = $meaningfulWorkEvents.Count -gt 0
        TransientFailureDetected = $transientFailureDetected
        WaitCompleteTimeoutDetected = $waitCompleteTimeoutDetected
    }
}

function Assert-SessionArtifactsMatchInvocation {
    param(
        [object]$Session,
        [string]$ArtifactsRoot,
        [string]$WorkingDirectory,
        [string]$KernelProjectPath,
        [string]$ConfigPath,
        [string]$ProfileName,
        [bool]$ApproveAll,
        [string]$RequestedResumeThreadId
    )

    $summary = $Session.Summary
    $resolvedOptions = $Session.ResolvedOptions
    $events = $Session.Events
    $commandLines = $Session.CommandLines
    $expectedCommands = $Session.ExpectedCommands

    Assert-Condition ([bool]$summary.Success -eq ($Session.ExitCode -eq 0)) "summary.json 的 Success 与实际退出码不一致：$($Session.SessionName)"
    Assert-Condition ([int]$summary.ExitCode -eq $Session.ExitCode) "summary.json 的 ExitCode 与实际退出码不一致：$($Session.SessionName)"
    Assert-Condition ([bool](Get-JsonPropertyValue -Object $summary -PropertyName 'ApproveAll' -Label 'summary.json') -eq [bool]$ApproveAll) "summary.json 的 ApproveAll 与脚本入参不一致：$($Session.SessionName)"
    Assert-Condition (Test-AbsolutePathEquals -Left ([string]$summary.ArtifactsDirectory) -Right $Session.RunDirectory) "summary.json 的 ArtifactsDirectory 与工件目录不一致：$($Session.SessionName)"
    Assert-Condition (Test-AbsolutePathEquals -Left ([string]$summary.WorkingDirectory) -Right $WorkingDirectory) "summary.json 的 WorkingDirectory 与验收目标工作区不一致：$($Session.SessionName)"
    Assert-Condition (Test-AbsolutePathEquals -Left ([string](Get-JsonPropertyValue -Object $summary -PropertyName 'AppHostProjectPath' -Label 'summary.json')) -Right $KernelProjectPath) "summary.json 的 AppHostProjectPath 与实际入参不一致：$($Session.SessionName)"
    Assert-Condition (Test-AbsolutePathEquals -Left ([string](Get-JsonPropertyValue -Object $summary -PropertyName 'ScriptPath' -Label 'summary.json')) -Right $Session.ScriptPath) "summary.json 的 ScriptPath 与生成的 chat 脚本不一致：$($Session.SessionName)"
    Assert-Condition ([int]$summary.CommandCount -eq $expectedCommands.Count) "summary.json 的 CommandCount 与预期执行命令数不一致：$($Session.SessionName)"
    Assert-Condition ([int]$summary.EventCount -eq $events.Count) "summary.json 的 EventCount 与 events.jsonl 行数不一致：$($Session.SessionName)"

    Assert-Condition (Test-AbsolutePathEquals -Left ([string](Get-JsonPropertyValue -Object $resolvedOptions -PropertyName 'ArtifactsRoot' -Label 'resolved-options.json')) -Right $ArtifactsRoot) "resolved-options.json 的 ArtifactsRoot 与脚本入参不一致：$($Session.SessionName)"
    Assert-Condition (Test-AbsolutePathEquals -Left ([string]$resolvedOptions.WorkingDirectory) -Right $WorkingDirectory) "resolved-options.json 的 WorkingDirectory 与验收目标工作区不一致：$($Session.SessionName)"
    Assert-Condition (Test-AbsolutePathEquals -Left ([string](Get-JsonPropertyValue -Object $resolvedOptions -PropertyName 'AppHostProjectPath' -Label 'resolved-options.json')) -Right $KernelProjectPath) "resolved-options.json 的 AppHostProjectPath 与实际入参不一致：$($Session.SessionName)"
    Assert-Condition ([bool]$resolvedOptions.ApproveAll -eq [bool]$ApproveAll) "resolved-options.json 的 ApproveAll 与脚本入参不一致：$($Session.SessionName)"

    if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
        Assert-Condition (Test-AbsolutePathEquals -Left ([string]$summary.ConfigFilePath) -Right $ConfigPath) "summary.json 的 ConfigFilePath 与脚本入参不一致：$($Session.SessionName)"
        Assert-Condition (Test-AbsolutePathEquals -Left ([string]$resolvedOptions.ConfigFilePath) -Right $ConfigPath) "resolved-options.json 的 ConfigFilePath 与脚本入参不一致：$($Session.SessionName)"
    }

    if (-not [string]::IsNullOrWhiteSpace($ProfileName)) {
        Assert-Condition ([string]$summary.ProfileName -eq $ProfileName) "summary.json 的 ProfileName 与脚本入参不一致：$($Session.SessionName)"
        Assert-Condition ([string]$resolvedOptions.ProfileName -eq $ProfileName) "resolved-options.json 的 ProfileName 与脚本入参不一致：$($Session.SessionName)"
    }

    if (-not [string]::IsNullOrWhiteSpace($RequestedResumeThreadId)) {
        Assert-Condition ([string]$summary.RequestedResumeThreadId -eq $RequestedResumeThreadId) "summary.json 的 RequestedResumeThreadId 与脚本入参不一致：$($Session.SessionName)"
        Assert-Condition ([string]$resolvedOptions.ResumeThreadId -eq $RequestedResumeThreadId) "resolved-options.json 的 ResumeThreadId 与脚本入参不一致：$($Session.SessionName)"
    }

    Assert-Condition ($commandLines.Count -eq $expectedCommands.Count) "commands.txt 的命令数与预期不一致：$($Session.SessionName)"
    for ($i = 0; $i -lt $expectedCommands.Count; $i++) {
        Assert-Condition ($commandLines[$i] -eq $expectedCommands[$i]) "commands.txt 第 $($i + 1) 行与预期命令不一致：$($Session.SessionName)"
    }

    $cliInputs = @($events | Where-Object { [string]$_.Kind -eq 'CliInput' })
    Assert-Condition ($cliInputs.Count -eq $commandLines.Count) "CliInput 事件数与 commands.txt 行数不一致：$($Session.SessionName)"
    for ($i = 0; $i -lt $commandLines.Count; $i++) {
        $cliInputMessage = [string](Get-JsonPropertyValue -Object $cliInputs[$i] -PropertyName 'Message' -Label "CliInput[$i]")
        Assert-Condition ($cliInputMessage -eq $commandLines[$i]) "CliInput 第 $($i + 1) 条消息与 commands.txt 不一致：$($Session.SessionName)"
    }
}

function Invoke-AcceptanceCliSession {
    param(
        [string]$SessionName,
        [string[]]$ScriptLines,
        [string]$RequestedResumeThreadId,
        [string]$HarnessRootPath,
        [string]$ArtifactsRoot,
        [string]$RepoRoot,
        [string]$TargetWorkspacePath,
        [string]$CliProjectPath,
        [string]$KernelProjectPath,
        [string]$ConfigPath,
        [string]$ProfileName,
        [bool]$ApproveAll,
        [bool]$VerboseEvents,
        [string]$TianShuHomePath,
        [string]$StateHomePath,
        [string]$SessionsHomePath
    )

    $scriptFileName = if ($SessionName -eq 'initial') { 'chat-script.txt' } else { "chat-script.$SessionName.txt" }
    $scriptPath = Join-Path $HarnessRootPath $scriptFileName
    $scriptPath = Write-ChatScriptFile -Path $scriptPath -Lines $ScriptLines -Root $RepoRoot
    Write-Step "生成 chat 脚本 [$SessionName]：$scriptPath"

    $expectedCommands = @($ScriptLines | Where-Object { $_ -notmatch '^\s*#' })
    $existingRunNames = @{}
    if (Test-Path -LiteralPath $ArtifactsRoot) {
        foreach ($directory in Get-ChildItem -LiteralPath $ArtifactsRoot -Directory) {
            $existingRunNames[$directory.Name] = $true
        }
    }

    $invokeArgs = @(
        'run',
        '--project', $CliProjectPath,
        '--no-build',
        '--',
        'chat',
        '--cwd', $TargetWorkspacePath,
        '--script', $scriptPath,
        '--artifacts', $ArtifactsRoot,
        '--apphost-project', $KernelProjectPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
        $invokeArgs += @('--config', (Get-AbsolutePath -Path $ConfigPath))
    }

    if (-not [string]::IsNullOrWhiteSpace($ProfileName)) {
        $invokeArgs += @('--profile', $ProfileName)
    }

    if (-not [string]::IsNullOrWhiteSpace($RequestedResumeThreadId)) {
        $invokeArgs += @('--resume-thread-id', $RequestedResumeThreadId)
    }

    if ($ApproveAll) {
        $invokeArgs += '--approve-all'
    }

    if ($VerboseEvents) {
        $invokeArgs += '--verbose-events'
    }

    Assert-ArchitectureAlignedChatInvocation -InvokeArgs $invokeArgs -CliProjectPath $CliProjectPath

    Write-Step "开始执行 CLI 最终验收会话 [$SessionName]"
    Write-Step "CLI 验收入口 [$SessionName]：dotnet run --project $CliProjectPath -- chat"
    Write-Step "宿主启动入口 [$SessionName]：$KernelProjectPath（仅用于 chat 交互宿主启动，不作为旧 AppHost control-plane turn loop）"
    Invoke-WithTemporaryEnvironment -Variables @{
        TIANSHU_HOME = $TianShuHomePath
        TIANSHU_STATE_HOME = $StateHomePath
        TIANSHU_SESSIONS_HOME = $SessionsHomePath
        TIANSHU_DISABLE_SYSTEM_PROXY = '1'
        HTTP_PROXY = ''
        HTTPS_PROXY = ''
        ALL_PROXY = ''
        NO_PROXY = '*'
        WS_PROXY = ''
        WSS_PROXY = ''
    } -ScriptBlock {
        & dotnet @invokeArgs | Out-Host
    }
    $exitCode = $LASTEXITCODE

    $newRun = Get-ChildItem -LiteralPath $ArtifactsRoot -Directory |
        Where-Object { -not $existingRunNames.ContainsKey($_.Name) } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $newRun) {
        $newRun = Get-LatestArtifactsRunDirectory -RootPath $ArtifactsRoot
    }

    Assert-Condition ($null -ne $newRun) "会话 [$SessionName] 未生成任何 CLI 工件目录：$ArtifactsRoot"
    $session = Get-AcceptanceRunArtifacts -RunDirectory $newRun.FullName -ExitCode $exitCode -SessionName $SessionName -ScriptPath $scriptPath -ExpectedCommands $expectedCommands
    Assert-SessionArtifactsMatchInvocation -Session $session -ArtifactsRoot $ArtifactsRoot -WorkingDirectory $TargetWorkspacePath -KernelProjectPath $KernelProjectPath -ConfigPath $ConfigPath -ProfileName $ProfileName -ApproveAll $ApproveAll -RequestedResumeThreadId $RequestedResumeThreadId
    return $session
}

function Invoke-SubAgentMechanismAcceptance {
    param(
        [string]$RepoRoot,
        [string]$HarnessRootPath,
        [bool]$SkipBuild
    )

    $projectPath = Join-Path $RepoRoot 'tools\acceptance\TianShu.SubAgentAcceptance\TianShu.SubAgentAcceptance.csproj'
    $workdir = Join-Path $HarnessRootPath 'subagent-mechanism'
    $outputPath = Join-Path $workdir 'evidence.json'
    Assert-PathUnderRoot -Path $projectPath -Root $RepoRoot
    Assert-PathUnderRoot -Path $workdir -Root $RepoRoot
    Assert-PathUnderRoot -Path $outputPath -Root $RepoRoot
    Assert-Condition (Test-Path -LiteralPath $projectPath) "Sub-Agent 机制验收项目不存在：$projectPath"
    Ensure-Directory -Path $workdir -Root $RepoRoot

    if (-not $SkipBuild) {
        Write-Step "构建 Sub-Agent 机制验收 harness：$projectPath"
        & dotnet build $projectPath --nologo --verbosity:minimal | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Sub-Agent 机制验收 harness 构建失败，退出码：$LASTEXITCODE"
        }
    }

    Write-Step "执行 Sub-Agent 机制验收 harness：$projectPath"
    & dotnet run --project $projectPath --no-build -- --workdir $workdir --output $outputPath | Out-Host
    $exitCode = $LASTEXITCODE
    Assert-Condition ($exitCode -eq 0) "Sub-Agent 机制验收 harness 失败，退出码：$exitCode"
    Assert-Condition (Test-Path -LiteralPath $outputPath) "Sub-Agent 机制验收未生成 evidence.json：$outputPath"

    $evidenceText = [System.IO.File]::ReadAllText(
        (Get-AbsolutePath -Path $outputPath),
        [System.Text.Encoding]::UTF8)
    $evidence = ConvertFrom-JsonCompat -Text $evidenceText
    Assert-Condition (Test-SubAgentMechanismEvidence -Evidence $evidence -EvidenceText $evidenceText) "Sub-Agent 机制验收证据不完整，必须包含 spawn_agent、ModuleCapabilityStep、module.sub_agent/sub_agent.spawn、bridge 与 toolResults 回流。"

    return [pscustomobject]@{
        Accepted = $true
        ProjectPath = $projectPath
        Workdir = $workdir
        EvidencePath = $outputPath
        Evidence = $evidence
    }
}

function Get-SubAgentLiveProviderToolSurface {
    param([object]$Summary)

    $diagnostics = TryGet-JsonPropertyValue -Object $Summary -PropertyName 'runtimeDiagnosticsProjection'
    if ($null -eq $diagnostics) {
        $diagnostics = TryGet-JsonPropertyValue -Object $Summary -PropertyName 'RuntimeDiagnosticsProjection'
    }

    $surface = TryGet-JsonPropertyValue -Object $diagnostics -PropertyName 'providerToolSurface'
    if ($null -eq $surface) {
        $surface = TryGet-JsonPropertyValue -Object $diagnostics -PropertyName 'ProviderToolSurface'
    }

    if ($null -eq $surface) {
        return [pscustomobject]@{
            Available = $false
            HasSpawnAgent = $false
            Names = @()
            WireApis = @()
            MissingReason = 'provider_tool_surface_missing'
        }
    }

    $namesValue = TryGet-JsonPropertyValue -Object $surface -PropertyName 'names'
    if ($null -eq $namesValue) {
        $namesValue = TryGet-JsonPropertyValue -Object $surface -PropertyName 'Names'
    }

    $wireApisValue = TryGet-JsonPropertyValue -Object $surface -PropertyName 'wireApis'
    if ($null -eq $wireApisValue) {
        $wireApisValue = TryGet-JsonPropertyValue -Object $surface -PropertyName 'WireApis'
    }

    $names = @($namesValue | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $wireApis = @($wireApisValue | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $hasSpawnAgent = (($names | Where-Object { [string]::Equals($_, 'spawn_agent', [System.StringComparison]::OrdinalIgnoreCase) }) | Measure-Object).Count -gt 0

    return [pscustomobject]@{
        Available = $true
        HasSpawnAgent = [bool]$hasSpawnAgent
        Names = $names
        WireApis = $wireApis
        MissingReason = $null
    }
}

function Get-SubAgentLiveActualToolRequestNames {
    param(
        [object]$Summary,
        [string]$SummaryText
    )

    $segments = [System.Collections.Generic.List[string]]::new()
    foreach ($propertyName in @('replaySummary', 'ReplaySummary', 'kernelRuntimeTerminalProjection', 'KernelRuntimeTerminalProjection')) {
        $value = TryGet-JsonPropertyValue -Object $Summary -PropertyName $propertyName
        if ($null -ne $value) {
            $segments.Add(($value | ConvertTo-Json -Depth 100 -Compress))
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($SummaryText)) {
        $segments.Add($SummaryText)
    }

    $text = Join-NonEmptyText -Values @($segments)
    $names = [System.Collections.Generic.List[string]]::new()
    foreach ($match in [Regex]::Matches($text, '(?i)"toolId"\s*:\s*"([^"]+)"')) {
        $names.Add($match.Groups[1].Value)
    }

    foreach ($match in [Regex]::Matches($text, '(?i)tool-exec-request[^\r\n"]*?([a-z0-9_.-]+)')) {
        $candidate = $match.Groups[1].Value
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and $candidate -ne 'tool-exec-request') {
            $names.Add($candidate)
        }
    }

    return ,@($names | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

function Invoke-SubAgentLiveAcceptanceRun {
    param(
        [object]$Scenario,
        [object]$ModelCell,
        [int]$RunIndex,
        [string]$PromptLine,
        [string]$RepoRoot,
        [string]$HarnessRootPath,
        [string]$TargetWorkspacePath,
        [string]$CliProjectPath,
        [string]$ConfigPath,
        [string]$ProfileName,
        [string]$TianShuHomePath,
        [string]$StateHomePath,
        [string]$SessionsHomePath
    )

    $scenarioId = ConvertTo-AcceptanceSafeId -Value ([string]$Scenario.Id)
    $modelCellId = ConvertTo-AcceptanceSafeId -Value ([string]$ModelCell.Id)
    $liveRoot = Join-Path $HarnessRootPath 'subagent-live'
    $runRoot = Join-Path $liveRoot (Join-Path $scenarioId (Join-Path $modelCellId ("run-$RunIndex")))
    $liveArtifactsRoot = Join-Path $runRoot 'artifacts'
    $stdoutPath = Join-Path $runRoot 'stdout.txt'
    $summaryPath = Join-Path $runRoot 'summary.json'
    foreach ($path in @($liveRoot, $runRoot, $liveArtifactsRoot)) {
        Ensure-Directory -Path $path -Root $RepoRoot
    }

    $cellConfigPath = New-SubAgentLiveCellConfig -SourceConfigPath $ConfigPath `
        -ModelCell $ModelCell `
        -ScenarioId $scenarioId `
        -RunIndex $RunIndex `
        -LiveRoot $liveRoot `
        -Root $RepoRoot

    $invokeArgs = @(
        'run',
        '--project', $CliProjectPath,
        '--no-build',
        '--',
        'send',
        '--message', $PromptLine,
        '--cwd', $TargetWorkspacePath,
        '--artifacts', $liveArtifactsRoot,
        '--turn-timeout-seconds', '900',
        '--json',
        '--kernel-runtime-loop',
        '--enable-subagents',
        '--approve-all'
    )

    if (-not [string]::IsNullOrWhiteSpace($cellConfigPath)) {
        $invokeArgs += @('--config', (Get-AbsolutePath -Path $cellConfigPath))
    }

    if (-not [string]::IsNullOrWhiteSpace($ProfileName)) {
        $invokeArgs += @('--profile', $ProfileName)
    }

    Assert-ArchitectureAlignedSendInvocation -InvokeArgs $invokeArgs -CliProjectPath $CliProjectPath

    Write-Step "开始执行 Sub-Agent live 观察：scenario=$scenarioId, model=$modelCellId, run=$RunIndex"
    Write-Step "Sub-Agent live 入口：dotnet run --project $CliProjectPath -- send --kernel-runtime-loop --enable-subagents"
    $outputLines = $null
    Invoke-WithTemporaryEnvironment -Variables @{
        TIANSHU_HOME = $TianShuHomePath
        TIANSHU_STATE_HOME = $StateHomePath
        TIANSHU_SESSIONS_HOME = $SessionsHomePath
        TIANSHU_DISABLE_SYSTEM_PROXY = '1'
        HTTP_PROXY = ''
        HTTPS_PROXY = ''
        ALL_PROXY = ''
        NO_PROXY = '*'
        WS_PROXY = ''
        WSS_PROXY = ''
    } -ScriptBlock {
        $script:SubAgentLiveOutputLines = & dotnet @invokeArgs 2>&1
        $script:SubAgentLiveExitCode = $LASTEXITCODE
    }

    $outputLines = @($script:SubAgentLiveOutputLines)
    $exitCode = [int]$script:SubAgentLiveExitCode
    $outputText = ($outputLines | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    Set-Content -LiteralPath $stdoutPath -Value $outputText -Encoding UTF8

    $summary = $null
    $spawnObserved = $false
    $providerToolSurface = [pscustomobject]@{
        Available = $false
        HasSpawnAgent = $false
        Names = @()
        WireApis = @()
        MissingReason = 'run_failed'
    }
    $actualToolRequests = @()
    $conclusion = 'not-attempted'
    if ($exitCode -eq 0) {
        $summary = ConvertFrom-CommandJsonOutput -Text $outputText
        $summaryText = $summary | ConvertTo-Json -Depth 100
        Set-Content -LiteralPath $summaryPath -Value $summaryText -Encoding UTF8
        $providerToolSurface = Get-SubAgentLiveProviderToolSurface -Summary $summary
        $actualToolRequests = @(Get-SubAgentLiveActualToolRequestNames -Summary $summary -SummaryText $summaryText)
        $spawnObserved = Test-SubAgentLiveSpawnObserved -Summary $summary -SummaryText $summaryText
        if (-not [bool]$providerToolSurface.Available -or -not [bool]$providerToolSurface.HasSpawnAgent) {
            $conclusion = 'live-tool-surface-invalid'
        }
        elseif ($spawnObserved) {
            $conclusion = 'live-autonomous-spawn-observed'
        }
        else {
            $conclusion = 'mechanism-ready-live-autonomous-spawn-not-observed'
        }
    }
    else {
        $conclusion = 'live-run-failed'
    }

    return [pscustomobject]@{
        Attempted = $true
        ScenarioId = $scenarioId
        ScenarioLabel = [string]$Scenario.Label
        ModelCellId = $modelCellId
        Provider = [string]$ModelCell.Provider
        Model = [string]$ModelCell.Model
        Protocol = [string]$ModelCell.Protocol
        RunIndex = $RunIndex
        ExitCode = $exitCode
        ProviderToolSurface = $providerToolSurface
        ActualToolRequests = $actualToolRequests
        SpawnObserved = [bool]$spawnObserved
        Conclusion = $conclusion
        RunRoot = $runRoot
        ArtifactsRoot = $liveArtifactsRoot
        StdoutPath = $stdoutPath
        SummaryPath = if (Test-Path -LiteralPath $summaryPath) { $summaryPath } else { $null }
        Summary = $summary
        ConfigPath = $cellConfigPath
    }
}

function Invoke-SubAgentLiveObservationMatrix {
    param(
        [object[]]$Scenarios,
        [object[]]$ModelCells,
        [int]$RunsPerCell,
        [string]$RepoRoot,
        [string]$HarnessRootPath,
        [string]$TargetWorkspacePath,
        [string]$CliProjectPath,
        [string]$ConfigPath,
        [string]$ProfileName,
        [string]$TianShuHomePath,
        [string]$StateHomePath,
        [string]$SessionsHomePath
    )

    Assert-Condition ($RunsPerCell -gt 0) "Sub-Agent live 观察矩阵每格轮数必须为正数。"

    $results = [System.Collections.Generic.List[object]]::new()
    $plannedRunCount = @($Scenarios).Count * @($ModelCells).Count * $RunsPerCell
    Write-Step "Sub-Agent live 观察矩阵已冻结：tasks=$(@($Scenarios).Count), models=$(@($ModelCells).Count), runsPerCell=$RunsPerCell, total=$plannedRunCount"

    foreach ($scenario in @($Scenarios)) {
        Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$scenario.PromptLine)) "Sub-Agent live 观察场景缺少提示词：$($scenario.Id)"
        foreach ($modelCell in @($ModelCells)) {
            for ($runIndex = 1; $runIndex -le $RunsPerCell; $runIndex++) {
                $result = Invoke-SubAgentLiveAcceptanceRun -Scenario $scenario `
                    -ModelCell $modelCell `
                    -RunIndex $runIndex `
                    -PromptLine ([string]$scenario.PromptLine) `
                    -RepoRoot $RepoRoot `
                    -HarnessRootPath $HarnessRootPath `
                    -TargetWorkspacePath $TargetWorkspacePath `
                    -CliProjectPath $CliProjectPath `
                    -ConfigPath $ConfigPath `
                    -ProfileName $ProfileName `
                    -TianShuHomePath $TianShuHomePath `
                    -StateHomePath $StateHomePath `
                    -SessionsHomePath $SessionsHomePath
                $results.Add($result)
            }
        }
    }

    $resultArray = @($results.ToArray())
    $completedRunCount = @($resultArray | Where-Object { [int]$_.ExitCode -eq 0 }).Count
    $failedRunCount = $resultArray.Count - $completedRunCount
    $invalidToolSurfaceCount = @($resultArray | Where-Object {
        [int]$_.ExitCode -eq 0 -and
        (-not [bool]$_.ProviderToolSurface.Available -or -not [bool]$_.ProviderToolSurface.HasSpawnAgent)
    }).Count
    $spawnObservedCount = @($resultArray | Where-Object { [bool]$_.SpawnObserved }).Count
    $matrixComplete = $resultArray.Count -eq $plannedRunCount

    $triggerRates = @(
        foreach ($scenario in @($Scenarios)) {
            foreach ($modelCell in @($ModelCells)) {
                $scenarioId = ConvertTo-AcceptanceSafeId -Value ([string]$scenario.Id)
                $modelCellId = ConvertTo-AcceptanceSafeId -Value ([string]$modelCell.Id)
                $cellResults = @($resultArray | Where-Object {
                    [string]$_.ScenarioId -eq $scenarioId -and [string]$_.ModelCellId -eq $modelCellId
                })
                $cellSpawnCount = @($cellResults | Where-Object { [bool]$_.SpawnObserved }).Count
                $triggerRate = 0.0
                if ($cellResults.Count -gt 0) {
                    $triggerRate = [double]$cellSpawnCount / [double]$cellResults.Count
                }

                [pscustomobject]@{
                    ScenarioId = $scenarioId
                    ModelCellId = $modelCellId
                    Provider = [string]$modelCell.Provider
                    Model = [string]$modelCell.Model
                    Protocol = [string]$modelCell.Protocol
                    Runs = $cellResults.Count
                    SpawnObserved = $cellSpawnCount
                    TriggerRate = $triggerRate
                }
            }
        }
    )

    $matrixConclusion = 'live-observation-matrix-complete-no-autonomous-spawn-observed'
    if (-not $matrixComplete) {
        $matrixConclusion = 'live-observation-matrix-incomplete'
    }
    elseif ($failedRunCount -gt 0 -or $invalidToolSurfaceCount -gt 0) {
        $matrixConclusion = 'live-observation-matrix-invalid'
    }
    elseif ($spawnObservedCount -gt 0) {
        $matrixConclusion = 'live-observation-matrix-complete-with-autonomous-spawn-observed'
    }

    return [pscustomobject]@{
        Attempted = $true
        PlannedScenarioCount = @($Scenarios).Count
        PlannedModelCellCount = @($ModelCells).Count
        RunsPerCell = $RunsPerCell
        PlannedRunCount = $plannedRunCount
        ObservedRunCount = $resultArray.Count
        CompletedRunCount = $completedRunCount
        FailedRunCount = $failedRunCount
        InvalidToolSurfaceCount = $invalidToolSurfaceCount
        SpawnObservedCount = $spawnObservedCount
        SpawnObservedAny = $spawnObservedCount -gt 0
        MatrixComplete = [bool]$matrixComplete
        TriggerRates = $triggerRates
        Results = $resultArray
        ArtifactsRoot = Join-Path $HarnessRootPath 'subagent-live'
        Conclusion = $matrixConclusion
    }
}

function Wait-AcceptanceConversationUntilSettled {
    param(
        [object]$InitialSession,
        [int]$IdleWindowSeconds,
        [string]$ResumePrompt,
        [string]$HarnessRootPath,
        [string]$ArtifactsRoot,
        [string]$RepoRoot,
        [string]$TargetWorkspacePath,
        [string]$CliProjectPath,
        [string]$KernelProjectPath,
        [string]$ConfigPath,
        [string]$ProfileName,
        [bool]$ApproveAll,
        [bool]$VerboseEvents,
        [string]$TianShuHomePath,
        [string]$StateHomePath,
        [string]$SessionsHomePath
    )

    $sessions = [System.Collections.Generic.List[object]]::new()
    $sessions.Add($InitialSession)
    $currentSession = $InitialSession
    $sessionIndex = 2

    while ($true) {
        $threadId = [string]$currentSession.ThreadId
        $threadLogState = Get-ThreadLogState -ThreadId $threadId -SessionsHomePath $SessionsHomePath
        $lastTurnEntry = if ($null -eq $threadLogState) { $null } else { $threadLogState.LastTurnEntry }
        $lastTurnId = [string](TryGet-JsonPropertyValue -Object $lastTurnEntry -PropertyName 'turnId')
        $lastTurnStatus = [string](TryGet-JsonPropertyValue -Object $lastTurnEntry -PropertyName 'status')
        $sessionTurnStatus = [string]$currentSession.TurnStatus
        $threadInProgress = [string]::Equals($sessionTurnStatus, 'inProgress', [System.StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($lastTurnStatus, 'inProgress', [System.StringComparison]::OrdinalIgnoreCase)
        $progressCanContinue = $threadInProgress -or [string]::IsNullOrWhiteSpace($lastTurnStatus)

        if (($currentSession.Success -and [string]::Equals($sessionTurnStatus, 'completed', [System.StringComparison]::OrdinalIgnoreCase)) -or
            [string]::Equals($lastTurnStatus, 'completed', [System.StringComparison]::OrdinalIgnoreCase)) {
            return [pscustomobject]@{
                Sessions = @($sessions)
                FinalSession = $currentSession
                ThreadLogState = $threadLogState
                FinalThreadTurnId = $lastTurnId
                FinalThreadTurnStatus = $lastTurnStatus
            }
        }

        if ([string]::IsNullOrWhiteSpace($threadId)) {
            return [pscustomobject]@{
                Sessions = @($sessions)
                FinalSession = $currentSession
                ThreadLogState = $threadLogState
                FinalThreadTurnId = $lastTurnId
                FinalThreadTurnStatus = $lastTurnStatus
            }
        }

        $nextScriptLines = $null
        if ($currentSession.TransientFailureDetected -and ($progressCanContinue -or $currentSession.MeaningfulWorkObserved)) {
            Write-Step "会话 [$($currentSession.SessionName)] 出现上游网络型异常，恢复同一线程继续：$threadId"
            $nextScriptLines = @(
                $ResumePrompt
                "/wait-complete $IdleWindowSeconds"
                '/exit'
            )
        }
        elseif ($currentSession.WaitCompleteTimeoutDetected -and $progressCanContinue -and $currentSession.MeaningfulWorkObserved) {
            Write-Step "会话 [$($currentSession.SessionName)] 观察窗口超时但线程仍在推进，恢复同一线程继续观察：$threadId"
            $nextScriptLines = @(
                "/wait-complete $IdleWindowSeconds"
                '/exit'
            )
        }
        if ($null -eq $nextScriptLines) {
            return [pscustomobject]@{
                Sessions = @($sessions)
                FinalSession = $currentSession
                ThreadLogState = $threadLogState
                FinalThreadTurnId = $lastTurnId
                FinalThreadTurnStatus = $lastTurnStatus
            }
        }

        $nextSessionName = 'resume-{0:d2}' -f $sessionIndex
        $currentSession = Invoke-AcceptanceCliSession -SessionName $nextSessionName `
            -ScriptLines $nextScriptLines `
            -RequestedResumeThreadId $threadId `
            -HarnessRootPath $HarnessRootPath `
            -ArtifactsRoot $ArtifactsRoot `
            -RepoRoot $RepoRoot `
            -TargetWorkspacePath $TargetWorkspacePath `
            -CliProjectPath $CliProjectPath `
            -KernelProjectPath $KernelProjectPath `
            -ConfigPath $ConfigPath `
            -ProfileName $ProfileName `
            -ApproveAll $ApproveAll `
            -VerboseEvents $VerboseEvents `
            -TianShuHomePath $TianShuHomePath `
            -StateHomePath $StateHomePath `
            -SessionsHomePath $SessionsHomePath
        $sessions.Add($currentSession)
        $sessionIndex++
    }
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Join-Path $PSScriptRoot '..'
}

$RepoRoot = Get-AbsolutePath -Path $RepoRoot

if ([string]::IsNullOrWhiteSpace($AcceptanceDocPath)) {
    $AcceptanceDocPath = Join-Path $RepoRoot 'docs\天枢最终验收案例.md'
}

if ([string]::IsNullOrWhiteSpace($TargetWorkspacePath)) {
    $TargetWorkspacePath = Join-Path $RepoRoot 'Test\TianShu最终验收'
}

if ([string]::IsNullOrWhiteSpace($HarnessRootPath)) {
    $HarnessRootPath = Join-Path $RepoRoot 'Test\TianShu最终验收.__cli'
}

if ([string]::IsNullOrWhiteSpace($ArtifactsRoot)) {
    $ArtifactsRoot = Join-Path $HarnessRootPath 'artifacts'
}

if ([string]::IsNullOrWhiteSpace($ChatScriptPath)) {
    $ChatScriptPath = Join-Path $HarnessRootPath 'chat-script.txt'
}

if ([string]::IsNullOrWhiteSpace($CliProjectPath)) {
    $CliProjectPath = Join-Path $RepoRoot 'src\Presentations\TianShu.Cli\TianShu.Cli.csproj'
}

if ([string]::IsNullOrWhiteSpace($KernelProjectPath)) {
    $KernelProjectPath = Join-Path $RepoRoot 'src\Hosting\TianShu.AppHost\TianShu.AppHost.csproj'
}

$userTianShuConfigPath = Join-Path $env:USERPROFILE '.tianshu\tianshu.toml'
$repoTianShuConfigPath = Join-Path $RepoRoot '.tianshu\tianshu.toml'
$userCodexConfigPath = Join-Path $env:USERPROFILE '.codex\config.toml'
$userTianShuConfigHashBefore = Get-OptionalFileSha256 -Path $userTianShuConfigPath
$userCodexConfigHashBefore = Get-OptionalFileSha256 -Path $userCodexConfigPath

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    if (Test-Path -LiteralPath $repoTianShuConfigPath) {
        $ConfigPath = $repoTianShuConfigPath
    }
    elseif (Test-Path -LiteralPath $userTianShuConfigPath) {
        $ConfigPath = $userTianShuConfigPath
    }
}

$AcceptanceDocPath = Get-AbsolutePath -Path $AcceptanceDocPath
$TargetWorkspacePath = Get-AbsolutePath -Path $TargetWorkspacePath
$HarnessRootPath = Get-AbsolutePath -Path $HarnessRootPath
$ArtifactsRoot = Get-AbsolutePath -Path $ArtifactsRoot
$ChatScriptPath = Get-AbsolutePath -Path $ChatScriptPath
$CliProjectPath = Get-AbsolutePath -Path $CliProjectPath
$KernelProjectPath = Get-AbsolutePath -Path $KernelProjectPath
if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Get-AbsolutePath -Path $ConfigPath
}

Assert-PathUnderRoot -Path $AcceptanceDocPath -Root $RepoRoot
Assert-PathUnderRoot -Path $TargetWorkspacePath -Root $RepoRoot
Assert-PathUnderRoot -Path $HarnessRootPath -Root $RepoRoot
Assert-PathUnderRoot -Path $ArtifactsRoot -Root $RepoRoot
Assert-PathUnderRoot -Path $ChatScriptPath -Root $RepoRoot
Assert-PathUnderRoot -Path $CliProjectPath -Root $RepoRoot
Assert-PathUnderRoot -Path $KernelProjectPath -Root $RepoRoot

if (-not (Test-Path -LiteralPath $AcceptanceDocPath)) {
    throw "验收文档不存在：$AcceptanceDocPath"
}

if (-not (Test-Path -LiteralPath $CliProjectPath)) {
    throw "CLI 项目不存在：$CliProjectPath"
}

Write-Step "仓库根目录：$RepoRoot"
Write-Step '清理上一轮最终验收生成物'
Remove-DirectorySafe -Path $TargetWorkspacePath -Root $RepoRoot
Remove-DirectorySafe -Path $HarnessRootPath -Root $RepoRoot
Ensure-Directory -Path $TargetWorkspacePath -Root $RepoRoot
Ensure-Directory -Path $HarnessRootPath -Root $RepoRoot
Ensure-Directory -Path $ArtifactsRoot -Root $RepoRoot

$moduleCopy = $null
$liveModelModuleDefaults = $null
if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
    $configSourcePath = Get-AbsolutePath -Path $ConfigPath
    $acceptanceConfigPath = Join-Path $HarnessRootPath 'acceptance-tianshu.toml'
    $ConfigPath = Prepare-AcceptanceConfigCopy -SourcePath $ConfigPath -TargetPath $acceptanceConfigPath -Root $RepoRoot
    Write-Step "本次 TianShu 运行配置复制来源：$configSourcePath"
    $moduleCopy = Copy-AcceptanceModuleConfigTree -SourceConfigPath $configSourcePath -TargetConfigPath $ConfigPath -Root $RepoRoot
    if ($null -ne $moduleCopy -and [bool]$moduleCopy.Copied) {
        Write-Step "本次 TianShu 模块配置复制来源：$($moduleCopy.SourceModulesPath)"
    }
    elseif ($null -ne $moduleCopy) {
        Write-Step "本次 TianShu 模块配置源不存在，未复制：$($moduleCopy.SourceModulesPath)"
    }

    $liveModelModuleDefaults = Set-AcceptanceLiveModelModuleDefaults -TargetConfigPath $ConfigPath -Root $RepoRoot
    Write-Step "本次最终验收默认模型路由：$($liveModelModuleDefaults.DefaultProvider)/$($liveModelModuleDefaults.DefaultModel)/$($liveModelModuleDefaults.DefaultProtocol)"
}

$TianShuHomePath = $HarnessRootPath
Write-Step "本次验收 TianShuHome：$TianShuHomePath"
$StateHomePath = Join-Path $HarnessRootPath 'state'
Ensure-Directory -Path $StateHomePath -Root $RepoRoot
Write-Step "本次验收 StateHome：$StateHomePath"
$SessionsHomePath = Join-Path $HarnessRootPath 'sessions'
Ensure-Directory -Path $SessionsHomePath -Root $RepoRoot
Write-Step "本次验收 SessionsHome：$SessionsHomePath"

$initialPrompt = Get-MarkdownPromptBlock -MarkdownPath $AcceptanceDocPath -MarkerName 'initial'
$steerPrompt = Get-MarkdownPromptBlock -MarkdownPath $AcceptanceDocPath -MarkerName 'steer'
$resumeAfterInterruptPrompt = Get-MarkdownPromptBlock -MarkdownPath $AcceptanceDocPath -MarkerName 'resume-after-interrupt'
$subAgentLiveConfigGuiPrompt = Get-MarkdownPromptBlock -MarkdownPath $AcceptanceDocPath -MarkerName 'subagent-live-config-gui'
$subAgentLiveProviderMatrixPrompt = Get-MarkdownPromptBlock -MarkdownPath $AcceptanceDocPath -MarkerName 'subagent-live-provider-matrix'
$subAgentLiveAcceptanceEvidencePrompt = Get-MarkdownPromptBlock -MarkdownPath $AcceptanceDocPath -MarkerName 'subagent-live-acceptance-evidence'
$initialPromptLine = ConvertTo-ScriptLine -Text $initialPrompt
$steerPromptLine = ConvertTo-ScriptLine -Text $steerPrompt
$resumeAfterInterruptPromptLine = ConvertTo-ScriptLine -Text $resumeAfterInterruptPrompt
$subAgentLiveScenarios = New-SubAgentLiveScenarioSet `
    -ConfigGuiPromptLine (ConvertTo-ScriptLine -Text $subAgentLiveConfigGuiPrompt) `
    -ProviderMatrixPromptLine (ConvertTo-ScriptLine -Text $subAgentLiveProviderMatrixPrompt) `
    -AcceptanceEvidencePromptLine (ConvertTo-ScriptLine -Text $subAgentLiveAcceptanceEvidencePrompt)
$subAgentLiveModelCells = New-SubAgentLiveModelCellSet

$initialSessionScriptLines = @(
    '# TianShu 最终验收脚本：自动生成，请勿手工修改本文件后再声称与验收文档一致'
    $initialPromptLine
    "/wait-next-tool-call $FirstToolTimeoutSeconds"
    "/follow-up steer $steerPromptLine"
    "/wait-next-tool-call $SecondToolTimeoutSeconds"
    '/interrupt'
    "/wait-complete $InterruptSettleTimeoutSeconds"
    $resumeAfterInterruptPromptLine
    "/wait-complete $FinalIdleWindowSeconds"
    '/exit'
)

$initialSessionScriptText = $initialSessionScriptLines -join [Environment]::NewLine
Assert-Condition ($initialSessionScriptText -notmatch '--apphost-control-plane') "最终验收 chat 脚本不得包含已移除的 --apphost-control-plane 旧执行路径。"

if (-not $SkipBuild) {
    Write-Step '构建天枢 CLI 开发入口 TianShu.Cli'
    & dotnet build $CliProjectPath -nologo -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw "构建失败，退出码：$LASTEXITCODE"
    }
}

if ($PrepareOnly) {
    $ChatScriptPath = Write-ChatScriptFile -Path $ChatScriptPath -Lines $initialSessionScriptLines -Root $RepoRoot
    Write-Step 'PrepareOnly 已启用，仅生成脚本与目录，不执行 CLI 会话'
    [pscustomobject]@{
        Success = $true
        Mode = 'PrepareOnly'
        AcceptanceDocPath = $AcceptanceDocPath
        TargetWorkspacePath = $TargetWorkspacePath
        HarnessRootPath = $HarnessRootPath
        ChatScriptPath = $ChatScriptPath
        ArtifactsRoot = $ArtifactsRoot
        CliInvocationMode = 'current-source-dotnet-run'
        CurrentArchitectureExecutionPath = 'kernel-runtime-loop'
        AppHostProjectUsage = 'host-bootstrap-entry-only'
        KernelProjectPath = $KernelProjectPath
        ConfigPath = $ConfigPath
        UserCodexConfigPath = $userCodexConfigPath
        TianShuHomePath = $TianShuHomePath
        ModuleConfigCopy = $moduleCopy
        LiveModelModuleDefaults = $liveModelModuleDefaults
        StateHomePath = $StateHomePath
        SessionsHomePath = $SessionsHomePath
        ScriptLineCount = $initialSessionScriptLines.Count
        FinalIdleWindowSeconds = $FinalIdleWindowSeconds
        TransientResumePrompt = $TransientResumePrompt
        SubAgentMechanismPlanned = $true
        SubAgentMechanismEvidencePath = Join-Path $HarnessRootPath 'subagent-mechanism\evidence.json'
        SubAgentLivePlanned = $true
        SubAgentLiveObservationProtocol = 'fixed-task-by-fixed-model-by-fixed-runs'
        SubAgentLiveScenarioCount = @($subAgentLiveScenarios).Count
        SubAgentLiveModelCellCount = @($subAgentLiveModelCells).Count
        SubAgentLiveRunsPerCell = $SubAgentLiveRunsPerCell
        SubAgentLivePlannedRunCount = @($subAgentLiveScenarios).Count * @($subAgentLiveModelCells).Count * $SubAgentLiveRunsPerCell
        SubAgentLivePromptsLoaded = @($subAgentLiveScenarios | ForEach-Object {
            [pscustomobject]@{
                ScenarioId = [string]$_.Id
                Loaded = -not [string]::IsNullOrWhiteSpace([string]$_.PromptLine)
            }
        })
        SubAgentLiveArtifactsRoot = Join-Path $HarnessRootPath 'subagent-live'
        SkipGuiLaunch = [bool]$SkipGuiLaunch
        GuiLaunchProbeSeconds = $GuiLaunchProbeSeconds
    } | ConvertTo-Json -Depth 5
    return
}

$subAgentMechanism = Invoke-SubAgentMechanismAcceptance -RepoRoot $RepoRoot -HarnessRootPath $HarnessRootPath -SkipBuild ([bool]$SkipBuild)

$initialSession = Invoke-AcceptanceCliSession -SessionName 'initial' `
    -ScriptLines $initialSessionScriptLines `
    -RequestedResumeThreadId $null `
    -HarnessRootPath $HarnessRootPath `
    -ArtifactsRoot $ArtifactsRoot `
    -RepoRoot $RepoRoot `
    -TargetWorkspacePath $TargetWorkspacePath `
    -CliProjectPath $CliProjectPath `
    -KernelProjectPath $KernelProjectPath `
    -ConfigPath $ConfigPath `
    -ProfileName $ProfileName `
    -ApproveAll $ApproveAll `
    -VerboseEvents $VerboseEvents `
    -TianShuHomePath $TianShuHomePath `
    -StateHomePath $StateHomePath `
    -SessionsHomePath $SessionsHomePath

$sessionSet = Wait-AcceptanceConversationUntilSettled -InitialSession $initialSession `
    -IdleWindowSeconds $FinalIdleWindowSeconds `
    -ResumePrompt $TransientResumePrompt `
    -HarnessRootPath $HarnessRootPath `
    -ArtifactsRoot $ArtifactsRoot `
    -RepoRoot $RepoRoot `
    -TargetWorkspacePath $TargetWorkspacePath `
    -CliProjectPath $CliProjectPath `
    -KernelProjectPath $KernelProjectPath `
    -ConfigPath $ConfigPath `
    -ProfileName $ProfileName `
    -ApproveAll $ApproveAll `
    -VerboseEvents $VerboseEvents `
    -TianShuHomePath $TianShuHomePath `
    -StateHomePath $StateHomePath `
    -SessionsHomePath $SessionsHomePath

$allSessions = @($sessionSet.Sessions)
$finalSession = $sessionSet.FinalSession
$threadLogState = $sessionSet.ThreadLogState
$threadId = [string]$finalSession.ThreadId
$threadLogPath = [string]$threadLogState.ThreadLogPath
$summary = $finalSession.Summary
$latestRunDirectory = $finalSession.RunDirectory
$summaryPath = $finalSession.SummaryPath
$resolvedOptionsPath = $finalSession.ResolvedOptionsPath
$eventsPath = $finalSession.EventsPath
$commandsPath = $finalSession.CommandsPath
$transcriptPath = $finalSession.TranscriptPath

$events = $initialSession.Events
$transcriptText = $initialSession.TranscriptText
$commandLines = $initialSession.CommandLines
$expectedExecutedInputs = $initialSession.ExpectedCommands

$eventKinds = @($events | ForEach-Object { [string]$_.Kind })
Assert-Condition (($eventKinds -contains 'CliInput')) "events.jsonl 缺少 CliInput 事件。"
Assert-Condition (($eventKinds -contains 'CliConversationRequested')) "events.jsonl 缺少 CliConversationRequested 事件。"
Assert-Condition (($eventKinds -contains 'ToolCallStarted')) "events.jsonl 缺少 ToolCallStarted 事件，无法证明发生过真实工具调用。"
Assert-Condition (($eventKinds -contains 'PendingFollowUpUpdated')) "events.jsonl 缺少 PendingFollowUpUpdated 事件，无法证明中途引导进入待发链路。"

$hasInterruptEvidence =
    ($eventKinds -contains 'CliConversationInterrupted') -or
    ($events | Where-Object {
        ([string]$_.Kind -eq 'TurnCompleted' -or [string]$_.Kind -eq 'ThreadStatusChanged') -and
        ([string]$_.Status -match 'interrupt')
    } | Measure-Object).Count -gt 0
Assert-Condition $hasInterruptEvidence "events.jsonl 缺少中断生效证据。"

$firstToolCallIndex = [Array]::IndexOf($eventKinds, 'ToolCallStarted')
$steerCliInputEvent = $events | Where-Object {
    [string]$_.Kind -eq 'CliInput' -and
    [string]$_.Message -eq "/follow-up steer $steerPromptLine"
} | Select-Object -First 1
Assert-Condition ($null -ne $steerCliInputEvent) "events.jsonl 缺少 /follow-up steer CLI 输入事件。"
$steerCliInputIndex = [Array]::IndexOf($events, $steerCliInputEvent)
$firstFollowUpIndex = [Array]::IndexOf($eventKinds, 'PendingFollowUpUpdated')
$firstTurnSteeredIndex = [Array]::IndexOf($eventKinds, 'TurnSteered')
$steerEvidenceIndexes = @($steerCliInputIndex, $firstFollowUpIndex, $firstTurnSteeredIndex) | Where-Object { $_ -ge 0 }
$firstSteerEvidenceIndex = ($steerEvidenceIndexes | Measure-Object -Minimum).Minimum
Assert-Condition ($firstToolCallIndex -ge 0 -and $firstSteerEvidenceIndex -gt $firstToolCallIndex) "中途引导证据没有出现在首个 ToolCallStarted 之后，无法证明引导时机正确。"

$hasSteerRuntimeEvidence =
    ($eventKinds -contains 'TurnSteered') -or
    (($events | Where-Object {
        [string]$_.Kind -eq 'PendingFollowUpUpdated' -and
        ([string]$_.Message -match 'committed|awaiting_commit')
    } | Measure-Object).Count -gt 0) -or
    (($events | Where-Object {
        [string]$_.Kind -eq 'UserMessageCommitted' -and
        [string](TryGet-JsonPropertyValue -Object $_.CommittedUserMessage -PropertyName 'Text') -eq $steerPromptLine
    } | Measure-Object).Count -gt 0)
Assert-Condition $hasSteerRuntimeEvidence "events.jsonl 缺少 TurnSteered / PendingFollowUpUpdated / UserMessageCommitted 中任一运行时引导提交证据。"

$interruptEvent = $events | Where-Object { [string]$_.Kind -eq 'CliConversationInterrupted' } | Select-Object -First 1
Assert-Condition ($null -ne $interruptEvent) "未找到 CliConversationInterrupted 事件。"
$interruptedTurnId = [string](Get-JsonPropertyValue -Object $interruptEvent -PropertyName 'TurnId' -Label 'CliConversationInterrupted')
Assert-Condition (-not [string]::IsNullOrWhiteSpace($interruptedTurnId)) "CliConversationInterrupted 缺少 TurnId。"
$interruptEventIndex = -1
for ($i = 0; $i -lt $events.Count; $i++) {
    if ([object]::ReferenceEquals($events[$i], $interruptEvent)) {
        $interruptEventIndex = $i
        break
    }
}
Assert-Condition ($interruptEventIndex -ge 0) "无法定位 CliConversationInterrupted 的事件序号。"
$resumeCliInput = $null
for ($i = $interruptEventIndex + 1; $i -lt $events.Count; $i++) {
    if ([string]$events[$i].Kind -eq 'CliInput' -and [string]$events[$i].Message -eq $resumeAfterInterruptPromptLine) {
        $resumeCliInput = $events[$i]
        break
    }
}
Assert-Condition ($null -ne $resumeCliInput) "中断后未找到继续/改向消息对应的 CliInput 事件。"
$postInterruptForbiddenKinds = @(
    'AssistantTextDelta',
    'AssistantTextCompleted',
    'ReasoningDelta',
    'PlanUpdated',
    'ToolCallStarted',
    'ToolCallOutputDelta',
    'ToolCallCompleted',
    'ApprovalRequested',
    'PermissionRequested',
    'UserInputRequested',
    'ItemStarted',
    'ItemCompleted',
    'TaskStarted',
    'TaskCompleted',
    'OperationReported',
    'CommandExecOutputDelta'
)
$staleInterruptedTurnEvents = @()
for ($i = $interruptEventIndex + 1; $i -lt $events.Count; $i++) {
    if ([string]$events[$i].TurnId -eq $interruptedTurnId -and $postInterruptForbiddenKinds -contains [string]$events[$i].Kind) {
        $staleInterruptedTurnEvents += "$([string]$events[$i].Kind)#$i"
    }
}
Assert-Condition ($staleInterruptedTurnEvents.Count -eq 0) "中断后的旧回合仍继续产生推进事件：$($staleInterruptedTurnEvents -join ', ')"

Assert-Condition (-not [string]::IsNullOrWhiteSpace($threadId)) "最终会话缺少 ThreadId。"
Assert-Condition (-not [string]::IsNullOrWhiteSpace($threadLogPath)) "无法从最终会话推导线程日志路径。"
Assert-Condition (Test-Path -LiteralPath $threadLogPath) "线程日志不存在：$threadLogPath"
$threadLogEntries = @($threadLogState.Entries)
$threadSessionEntry = $threadLogState.SessionEntry
Assert-Condition ($null -ne $threadSessionEntry) "线程日志缺少 session_meta/session_state 记录。"
Assert-Condition ([string](Get-JsonPropertyValue -Object $threadSessionEntry -PropertyName 'threadId' -Label '线程日志 session 记录') -eq $threadId) "线程日志中的 threadId 与最终会话不一致。"
Assert-Condition (Test-AbsolutePathEquals -Left ([string](Get-JsonPropertyValue -Object $threadSessionEntry -PropertyName 'cwd' -Label '线程日志 session 记录')) -Right $TargetWorkspacePath) "线程日志中的 cwd 与验收目标工作区不一致。"
$threadRolloutPath = [string](TryGet-JsonPropertyValue -Object $threadSessionEntry -PropertyName 'rolloutPath')
if (-not [string]::IsNullOrWhiteSpace($threadRolloutPath)) {
    Assert-PathUnderRoot -Path $threadRolloutPath -Root $RepoRoot
}
$threadTurnEntry = $threadLogState.LastTurnEntry
Assert-Condition ($null -ne $threadTurnEntry) "线程日志缺少最终 turn 记录。"
$finalTurnId = [string](TryGet-JsonPropertyValue -Object $threadTurnEntry -PropertyName 'turnId')
$finalTurnStatus = [string](TryGet-JsonPropertyValue -Object $threadTurnEntry -PropertyName 'status')
Assert-Condition (-not [string]::IsNullOrWhiteSpace($finalTurnId)) "线程日志中的最终 turn 缺少 turnId。"
Assert-Condition ([string]::Equals($finalTurnStatus, 'completed', [System.StringComparison]::OrdinalIgnoreCase)) "最终 turn 未完成，当前状态：$finalTurnStatus"

$allEvents = [System.Collections.Generic.List[object]]::new()
$allTranscripts = [System.Collections.Generic.List[string]]::new()
foreach ($session in $allSessions) {
    foreach ($event in $session.Events) {
        $allEvents.Add($event)
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$session.TranscriptText)) {
        $allTranscripts.Add([string]$session.TranscriptText)
    }
}

$finalTurnEvent = @($allEvents) | Where-Object {
    [string]$_.Kind -eq 'TurnCompleted' -and
    [string]$_.TurnId -eq $finalTurnId -and
    [string]$_.Status -eq $finalTurnStatus
} | Select-Object -Last 1
$completionMarker = "回合完成：thread=$threadId, turn=$finalTurnId, status=$finalTurnStatus"
$aggregatedTranscript = Join-NonEmptyText -Values $allTranscripts
$observedIdleMarker = $aggregatedTranscript.Contains('当前没有运行中的回合。')
Assert-Condition (($null -ne $finalTurnEvent) -or $aggregatedTranscript.Contains($completionMarker) -or $observedIdleMarker) "聚合工件中缺少最终回合完成或线程空闲证据。"
$subagentEvidenceObserved = Test-SubagentEvidence -Events @($allEvents) -ThreadLogEntries $threadLogEntries -TranscriptText $aggregatedTranscript
if ($subagentEvidenceObserved) {
    Write-Step '检测到 agent job / 子代理附加观察项证据；该证据不参与当前默认 Agent 工作流必过判定。'
}
else {
    Write-Step '未检测到 agent job / 子代理附加观察项证据；按 34 终局决策，这不是当前最终验收失败条件。'
}

$subAgentLive = Invoke-SubAgentLiveObservationMatrix -Scenarios $subAgentLiveScenarios `
    -ModelCells $subAgentLiveModelCells `
    -RunsPerCell $SubAgentLiveRunsPerCell `
    -RepoRoot $RepoRoot `
    -HarnessRootPath $HarnessRootPath `
    -TargetWorkspacePath $TargetWorkspacePath `
    -CliProjectPath $CliProjectPath `
    -ConfigPath $ConfigPath `
    -ProfileName $ProfileName `
    -TianShuHomePath $TianShuHomePath `
    -StateHomePath $StateHomePath `
    -SessionsHomePath $SessionsHomePath
if (-not [bool]$subAgentLive.MatrixComplete) {
    Write-Step "Sub-Agent live 观察矩阵未完整执行；最终结论只能保留机制门禁通过：$($subAgentLive.ArtifactsRoot)"
}
elseif ([int]$subAgentLive.FailedRunCount -gt 0 -or [int]$subAgentLive.InvalidToolSurfaceCount -gt 0) {
    Write-Step "Sub-Agent live 观察矩阵存在失败或工具面无效 run；最终结论只能保留机制门禁通过：failed=$($subAgentLive.FailedRunCount), invalidToolSurface=$($subAgentLive.InvalidToolSurfaceCount)"
}
elseif ($subAgentLive.SpawnObservedAny) {
    Write-Step "Sub-Agent live 观察矩阵检测到真实 spawn_agent 证据：$($subAgentLive.SpawnObservedCount)/$($subAgentLive.PlannedRunCount)"
}
else {
    Write-Step "Sub-Agent live 观察矩阵未检测到真实 spawn_agent：0/$($subAgentLive.PlannedRunCount)；机制门禁仍通过，但不得宣称模型自主 Sub-Agent 通过。"
}
Assert-Condition ([bool]$subAgentLive.MatrixComplete) "Sub-Agent live 观察矩阵未完整执行，无法形成有效 live 观察证据。"
Assert-Condition ([int]$subAgentLive.FailedRunCount -eq 0) "Sub-Agent live 观察矩阵存在执行失败 run，无法形成有效 live 观察证据。"
Assert-Condition ([int]$subAgentLive.InvalidToolSurfaceCount -eq 0) "Sub-Agent live 观察矩阵存在 provider tool surface 缺失 spawn_agent 的 run，无法形成有效 live 观察证据。"

$generatedProjects = @(Get-ChildItem -LiteralPath $TargetWorkspacePath -Recurse -Filter *.csproj -File)
Assert-Condition ($generatedProjects.Count -gt 0) "目标工作区未生成任何 .csproj 项目：$TargetWorkspacePath"
$generatedWpfProjects = @(Get-GeneratedWpfProjects -Projects $generatedProjects)
Assert-Condition ($generatedWpfProjects.Count -gt 0) "目标工作区未生成任何启用 UseWPF 的 WPF 项目：$TargetWorkspacePath"
$exportedConfigFiles = @(Get-ChildItem -LiteralPath $TargetWorkspacePath -Recurse -Filter config.toml -File)
Assert-Condition ($exportedConfigFiles.Count -gt 0) "目标工作区未导出任何 Codex config.toml：$TargetWorkspacePath"

foreach ($project in $generatedProjects) {
    Write-Step "构建最终验收产物项目：$($project.FullName)"
    & dotnet build $project.FullName -nologo -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw "最终验收产物构建失败：$($project.FullName)"
    }
}

$guiLaunchProbe = $null
if ($SkipGuiLaunch) {
    Write-Step '已按 -SkipGuiLaunch 跳过 GUI 启动探针；本轮不能单独判定最终人工 GUI 验收通过。'
}
else {
    $guiLaunchProbe = Invoke-GeneratedWpfLaunchProbe -Project $generatedWpfProjects[0] -ProbeSeconds $GuiLaunchProbeSeconds
    Write-Step "GUI 启动探针通过，已保留进程供人工验收：project=$($guiLaunchProbe.ProjectPath), pid=$($guiLaunchProbe.ProcessId)"
}

$userTianShuConfigHashAfter = Get-OptionalFileSha256 -Path $userTianShuConfigPath
Assert-Condition ([string]::Equals([string]$userTianShuConfigHashBefore, [string]$userTianShuConfigHashAfter, [System.StringComparison]::OrdinalIgnoreCase)) "用户级 TianShu 运行配置发生变化，验收失败：$userTianShuConfigPath"
$userCodexConfigHashAfter = Get-OptionalFileSha256 -Path $userCodexConfigPath
Assert-Condition ([string]::Equals([string]$userCodexConfigHashBefore, [string]$userCodexConfigHashAfter, [System.StringComparison]::OrdinalIgnoreCase)) "用户级 Codex 配置发生变化，验收失败：$userCodexConfigPath"

[pscustomobject]@{
    Success = $true
    Mode = 'Run'
    AcceptanceDocPath = $AcceptanceDocPath
    TargetWorkspacePath = $TargetWorkspacePath
    HarnessRootPath = $HarnessRootPath
    ChatScriptPath = $ChatScriptPath
    ArtifactsRoot = $ArtifactsRoot
    ConfigPath = $ConfigPath
    UserCodexConfigPath = $userCodexConfigPath
    TianShuHomePath = $TianShuHomePath
    ModuleConfigCopy = $moduleCopy
    LiveModelModuleDefaults = $liveModelModuleDefaults
    StateHomePath = $StateHomePath
    SessionsHomePath = $SessionsHomePath
    CliInvocationMode = 'current-source-dotnet-run'
    CurrentArchitectureExecutionPath = 'kernel-runtime-loop'
    AppHostProjectUsage = 'host-bootstrap-entry-only'
    LatestRunDirectory = $latestRunDirectory
    SummaryPath = $summaryPath
    ResolvedOptionsPath = $resolvedOptionsPath
    EventsPath = $eventsPath
    CommandsPath = $commandsPath
    TranscriptPath = $transcriptPath
    ThreadId = $threadId
    ThreadLogPath = $threadLogPath
    SessionCount = $allSessions.Count
    RunDirectories = @($allSessions | ForEach-Object { $_.RunDirectory })
    GeneratedProjectCount = $generatedProjects.Count
    GeneratedWpfProjectCount = $generatedWpfProjects.Count
    ExportedCodexConfigCount = $exportedConfigFiles.Count
    SubagentEvidenceObserved = [bool]$subagentEvidenceObserved
    SubAgentMechanismEvidencePath = $subAgentMechanism.EvidencePath
    SubAgentMechanismAccepted = [bool]$subAgentMechanism.Accepted
    SubAgentLiveAttempted = [bool]$subAgentLive.Attempted
    SubAgentLiveObservationProtocol = 'fixed-task-by-fixed-model-by-fixed-runs'
    SubAgentLiveScenarioCount = [int]$subAgentLive.PlannedScenarioCount
    SubAgentLiveModelCellCount = [int]$subAgentLive.PlannedModelCellCount
    SubAgentLiveRunsPerCell = [int]$subAgentLive.RunsPerCell
    SubAgentLivePlannedRunCount = [int]$subAgentLive.PlannedRunCount
    SubAgentLiveObservedRunCount = [int]$subAgentLive.ObservedRunCount
    SubAgentLiveCompletedRunCount = [int]$subAgentLive.CompletedRunCount
    SubAgentLiveFailedRunCount = [int]$subAgentLive.FailedRunCount
    SubAgentLiveInvalidToolSurfaceCount = [int]$subAgentLive.InvalidToolSurfaceCount
    SubAgentLiveSpawnObserved = [bool]$subAgentLive.SpawnObservedAny
    SubAgentLiveSpawnObservedCount = [int]$subAgentLive.SpawnObservedCount
    SubAgentLiveConclusion = [string]$subAgentLive.Conclusion
    SubAgentLiveArtifactsRoot = [string]$subAgentLive.ArtifactsRoot
    SubAgentLiveTriggerRates = $subAgentLive.TriggerRates
    SubAgentLiveMatrix = $subAgentLive.Results
    UserTianShuConfigHashUnchanged = $true
    UserCodexConfigHashUnchanged = $true
    GuiLaunchSkipped = [bool]$SkipGuiLaunch
    GuiLaunchProbe = $guiLaunchProbe
} | ConvertTo-Json -Depth 10
