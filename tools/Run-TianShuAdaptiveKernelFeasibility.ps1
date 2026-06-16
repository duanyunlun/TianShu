param(
    [switch]$DryRun,
    [switch]$RunBaseline,
    [switch]$RunC0,
    [switch]$RunC,
    [switch]$RunD,
    [switch]$RunP8,
    [switch]$RunCProfiles,
    [switch]$RunEvolutionNoiseProbe,
    [int]$RepeatCount = 5,
    [int]$CProfileRepeatCount = 0,
    [int]$EvolutionProbeRepeatCount = 10,
    [int]$MaxReviseRounds = 3,
    [double]$CAcceptanceFinalLegalRateThreshold = 0.85,
    [double]$CAcceptanceAverageReviseThreshold = 1.5,
    [string]$Model = "gpt-5.5",
    [string]$Temperature = "",
    [string]$Seed = "",
    [string]$PriceModelPath = "",
    [string]$CResultsPath = "",
    [string]$StrategyGatePath = "",
    [string]$CProfileAcceptancePath = "",
    [string]$CProfilePromptTemplateVersion = "c-profile-prompt-contract-v1",
    [string]$EvolutionProbeStrategySetPath = "",
    [string]$EvolutionProbeMinDeltaPath = "",
    [string]$CliPath = "",
    [string]$OutputRoot = "",
    [string[]]$BaselineTaskIds = @(),
    [string[]]$C0Groups = @(),
    [string[]]$CTaskIds = @(),
    [string[]]$CProfiles = @("schema-enforced", "schema-hinted-no-output-schema", "schema-full-strict"),
    [int]$CommandTimeoutSeconds = 300
)

$ErrorActionPreference = "Stop"
$script:RunnerVersion = "adaptive-kernel-c-profile-noise-probe"
$script:MetricsSchemaVersion = "adaptive-kernel-run-metrics-v1"
$script:ResolvedCliPath = $null
$script:PriceModel = $null

function Resolve-CliPath {
    param([string]$RequestedPath)
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    $installed = Join-Path $env:USERPROFILE ".tianshu\bin\tianshu.exe"
    if (Test-Path -LiteralPath $installed) {
        return $installed
    }

    throw "Cannot find tianshu.exe. Pass -CliPath explicitly."
}

function New-RunRoot {
    param([string]$RequestedRoot)
    if (-not [string]::IsNullOrWhiteSpace($RequestedRoot)) {
        $root = $RequestedRoot
    }
    else {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $root = Join-Path (Join-Path "Test" "TianShuAdaptiveKernelFeasibility.__live") $timestamp
    }

    New-Item -ItemType Directory -Force -Path $root | Out-Null
    return (Resolve-Path -LiteralPath $root).Path
}

function Invoke-TianShuJsonl {
    param(
        [string]$Cli,
        [string[]]$Arguments,
        [string]$OutFile,
        [int]$TimeoutSeconds
    )

    $started = Get-Date
    $stderrFile = $OutFile + ".stderr.txt"
    if (Test-Path -LiteralPath $OutFile) {
        Remove-Item -LiteralPath $OutFile -Force
    }

    if (Test-Path -LiteralPath $stderrFile) {
        Remove-Item -LiteralPath $stderrFile -Force
    }

    $process = Start-Process -FilePath $Cli `
        -ArgumentList (Join-ProcessArguments -Arguments $Arguments) `
        -RedirectStandardOutput $OutFile `
        -RedirectStandardError $stderrFile `
        -WindowStyle Hidden `
        -PassThru
    if ($null -eq $process) {
        throw "Failed to start tianshu process."
    }

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            $process.Kill($true)
            $process.WaitForExit()
        }
        catch {
            # best effort cleanup
        }
        $finished = Get-Date
        $stdout = if (Test-Path -LiteralPath $OutFile) { Get-Content -LiteralPath $OutFile -Raw } else { "" }
        $stderr = if (Test-Path -LiteralPath $stderrFile) { Get-Content -LiteralPath $stderrFile -Raw } else { "" }
        $timeoutRecord = [pscustomobject]@{
            type = "command_timeout"
            timeoutSeconds = $TimeoutSeconds
            command = $Arguments
        }
        if ([string]::IsNullOrWhiteSpace($stdout)) {
            Set-Content -LiteralPath $OutFile -Value ($timeoutRecord | ConvertTo-Json -Depth 10) -Encoding UTF8
        }
        else {
            Set-Content -LiteralPath $OutFile -Value $stdout -Encoding UTF8
        }

        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            Set-Content -LiteralPath ($OutFile + ".stderr.txt") -Value $stderr -Encoding UTF8
        }

        $events = @()
        foreach ($line in ($stdout -split "`r?`n")) {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            try {
                $events += ($line | ConvertFrom-Json)
            }
            catch {
                $events += [pscustomobject]@{ type = "unparseable"; raw = $line }
            }
        }

        if ($events.Count -eq 0) {
            $events += $timeoutRecord
        }

        $completed = $events | Where-Object { $_.type -eq "exec_completed" } | Select-Object -Last 1
        $assistantText = if ($null -ne $completed -and $null -ne $completed.assistantText) { [string]$completed.assistantText } else { $null }
        $tokenUsage = Get-RunTokenUsage -Events $events
        $estimatedCost = New-EstimatedCost -TokenUsage $tokenUsage -PriceModel $script:PriceModel -ModelId $Model
        return [pscustomobject]@{
            exitCode = $null
            processHasExited = $process.HasExited
            startedAt = $started.ToString("o")
            finishedAt = $finished.ToString("o")
            latencyMs = [int]($finished - $started).TotalMilliseconds
            outputFile = $OutFile
            eventCount = $events.Count
            success = $false
            timedOut = $true
            threadId = if ($null -ne $completed) { $completed.threadId } else { $null }
            turnId = if ($null -ne $completed) { $completed.turnId } else { $null }
            turnStatus = if ($null -ne $completed) { $completed.turnStatus } else { "timeout" }
            assistantTextLength = if ($null -ne $assistantText) { $assistantText.Length } else { 0 }
            assistantText = $assistantText
            tokenUsage = $tokenUsage
            estimatedCost = $estimatedCost
        }
    }

    $process.WaitForExit()
    $process.Refresh()
    $processHasExited = $process.HasExited
    $exitCode = $process.ExitCode
    $stdout = if (Test-Path -LiteralPath $OutFile) { Get-Content -LiteralPath $OutFile -Raw } else { "" }
    $stderr = if (Test-Path -LiteralPath $stderrFile) { Get-Content -LiteralPath $stderrFile -Raw } else { "" }
    $finished = Get-Date

    $events = @()
    foreach ($line in ($stdout -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $events += ($line | ConvertFrom-Json)
        }
        catch {
            $events += [pscustomobject]@{ type = "unparseable"; raw = $line }
        }
    }

    $completed = $events | Where-Object { $_.type -eq "exec_completed" } | Select-Object -Last 1
    $assistantText = if ($null -ne $completed -and $null -ne $completed.assistantText) { [string]$completed.assistantText } else { $null }
    $turnSucceeded = ($null -ne $completed -and $completed.success -eq $true)
    $tokenUsage = Get-RunTokenUsage -Events $events
    $estimatedCost = New-EstimatedCost -TokenUsage $tokenUsage -PriceModel $script:PriceModel -ModelId $Model
    return [pscustomobject]@{
        exitCode = $exitCode
        processHasExited = $processHasExited
        startedAt = $started.ToString("o")
        finishedAt = $finished.ToString("o")
        latencyMs = [int]($finished - $started).TotalMilliseconds
        outputFile = $OutFile
        eventCount = $events.Count
        success = (($exitCode -eq 0 -or $null -eq $exitCode) -and $turnSucceeded)
        timedOut = $false
        threadId = if ($null -ne $completed) { $completed.threadId } else { $null }
        turnId = if ($null -ne $completed) { $completed.turnId } else { $null }
        turnStatus = if ($null -ne $completed) { $completed.turnStatus } else { $null }
        assistantTextLength = if ($null -ne $assistantText) { $assistantText.Length } else { 0 }
        assistantText = $assistantText
        tokenUsage = $tokenUsage
        estimatedCost = $estimatedCost
    }
}

function Join-ProcessArguments {
    param([string[]]$Arguments)
    $quoted = foreach ($argument in $Arguments) {
        if ($null -eq $argument) {
            '""'
            continue
        }

        if ($argument -notmatch '[\s"]') {
            $argument
            continue
        }

        '"' + ($argument -replace '\\', '\\' -replace '"', '\"') + '"'
    }
    return ($quoted -join " ")
}

function Invoke-TianShuChatScript {
    param(
        [string]$Cli,
        [string[]]$ScriptLines,
        [string]$ScriptPath,
        [string]$ArtifactsRoot,
        [string]$OutFile,
        [int]$TimeoutSeconds
    )

    Set-Content -LiteralPath $ScriptPath -Value ($ScriptLines -join [Environment]::NewLine) -Encoding UTF8
    New-Item -ItemType Directory -Force -Path $ArtifactsRoot | Out-Null

    $arguments = @(
        "chat",
        "--script", $ScriptPath,
        "--protocol", "jsonl",
        "--artifacts", $ArtifactsRoot,
        "--approve-all",
        "--model", $Model
    )

    $result = Invoke-TianShuJsonl -Cli $Cli -Arguments $arguments -OutFile $OutFile -TimeoutSeconds $TimeoutSeconds
    $latestArtifactRun = Get-ChildItem -LiteralPath $ArtifactsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -ne $latestArtifactRun -and (-not [bool]$result.tokenUsage.available)) {
        $artifactTokenUsage = Get-RunArtifactTokenUsage -ArtifactRunDirectory $latestArtifactRun.FullName
        if ([bool]$artifactTokenUsage.available) {
            $result.tokenUsage = $artifactTokenUsage
            $result.estimatedCost = New-EstimatedCost -TokenUsage $artifactTokenUsage -PriceModel $script:PriceModel -ModelId $Model
        }
    }

    $result | Add-Member -NotePropertyName scriptPath -NotePropertyValue $ScriptPath
    $result | Add-Member -NotePropertyName artifactsRoot -NotePropertyValue $ArtifactsRoot
    $result | Add-Member -NotePropertyName latestArtifactRun -NotePropertyValue $(if ($null -ne $latestArtifactRun) { $latestArtifactRun.FullName } else { $null })
    return $result
}

function Write-Json {
    param([string]$Path, [object]$Value)
    $Value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Read-JsonFileOrNull {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-FileSha256OrNull {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Copy-OptionalJsonEvidence {
    param([string]$SourcePath, [string]$DestinationPath, [object]$DefaultValue)
    if (-not [string]::IsNullOrWhiteSpace($SourcePath)) {
        $resolved = (Resolve-Path -LiteralPath $SourcePath).Path
        Copy-Item -LiteralPath $resolved -Destination $DestinationPath -Force
        return Get-Content -LiteralPath $DestinationPath -Raw | ConvertFrom-Json
    }

    Write-Json $DestinationPath $DefaultValue
    return $DefaultValue
}

function ConvertTo-NameSet {
    param([string[]]$Values)
    $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($value in $Values) {
        foreach ($part in ([string]$value -split ",")) {
            if (-not [string]::IsNullOrWhiteSpace($part)) {
                [void]$set.Add($part.Trim())
            }
        }
    }

    return $set
}

function Get-JsonPropertyValue {
    param([object]$Object, [string[]]$Names)
    if ($null -eq $Object) {
        return $null
    }

    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties[$name]
        if ($null -ne $property) {
            return $property.Value
        }
    }

    return $null
}

function Convert-ToNullableInt {
    param([object]$Value)
    if ($null -eq $Value) {
        return $null
    }

    try {
        return [int64]$Value
    }
    catch {
        return $null
    }
}

function Convert-ToNumberOrZero {
    param([object]$Value)
    if ($null -eq $Value) {
        return 0
    }

    try {
        return [double]$Value
    }
    catch {
        return 0
    }
}

function New-MissingTokenUsage {
    param([string]$Reason)
    return [pscustomobject]@{
        available = $false
        missingReason = $Reason
        estimated = $false
        inputTokens = $null
        cachedInputTokens = $null
        outputTokens = $null
        reasoningOutputTokens = $null
        totalTokens = $null
        source = $null
    }
}

function Convert-ToNullableBool {
    param([object]$Value)
    if ($null -eq $Value) {
        return $null
    }

    try {
        return [bool]$Value
    }
    catch {
        return $null
    }
}

function ConvertTo-RunTokenUsage {
    param([object]$RawUsage, [string]$Source)
    if ($null -eq $RawUsage) {
        return New-MissingTokenUsage -Reason "token_usage_event_missing"
    }

    $last = Get-JsonPropertyValue -Object $RawUsage -Names @("last", "Last")
    $total = Get-JsonPropertyValue -Object $RawUsage -Names @("total", "Total")
    $usage = if ($null -ne $last) { $last } elseif ($null -ne $total) { $total } else { $RawUsage }

    $inputTokens = Convert-ToNullableInt (Get-JsonPropertyValue -Object $usage -Names @("inputTokens", "InputTokens", "promptTokens", "PromptTokens", "input", "prompt"))
    $cachedInputTokens = Convert-ToNullableInt (Get-JsonPropertyValue -Object $usage -Names @("cachedInputTokens", "CachedInputTokens", "cachedTokens", "CachedTokens"))
    $outputTokens = Convert-ToNullableInt (Get-JsonPropertyValue -Object $usage -Names @("outputTokens", "OutputTokens", "completionTokens", "CompletionTokens", "output", "completion"))
    $reasoningOutputTokens = Convert-ToNullableInt (Get-JsonPropertyValue -Object $usage -Names @("reasoningOutputTokens", "ReasoningOutputTokens", "reasoningTokens", "ReasoningTokens"))
    $totalTokens = Convert-ToNullableInt (Get-JsonPropertyValue -Object $usage -Names @("totalTokens", "TotalTokens", "total"))
    $estimated = Convert-ToNullableBool (Get-JsonPropertyValue -Object $usage -Names @("estimated", "Estimated", "isEstimated", "IsEstimated"))
    if ($null -eq $estimated) {
        $estimated = Convert-ToNullableBool (Get-JsonPropertyValue -Object $RawUsage -Names @("estimated", "Estimated", "isEstimated", "IsEstimated"))
    }
    if ($null -eq $estimated) {
        $estimated = ($Source -eq "thread/tokenUsage/updated" -or $Source -eq "artifact.events.threadTokenUsage")
    }
    $usageSource = Get-JsonPropertyValue -Object $RawUsage -Names @("source", "Source")
    if ($null -eq $usageSource) {
        $usageSource = Get-JsonPropertyValue -Object $usage -Names @("source", "Source")
    }
    $effectiveSource = if ($null -ne $usageSource -and -not [string]::IsNullOrWhiteSpace([string]$usageSource)) { [string]$usageSource } else { $Source }
    if ($effectiveSource -eq "text_length_estimate") {
        $estimated = $true
    }

    if ($null -eq $totalTokens -and ($null -ne $inputTokens -or $null -ne $outputTokens -or $null -ne $reasoningOutputTokens)) {
        $totalTokens = [int64]((Convert-ToNumberOrZero $inputTokens) + (Convert-ToNumberOrZero $outputTokens) + (Convert-ToNumberOrZero $reasoningOutputTokens))
    }

    if ($null -eq $inputTokens -and $null -eq $outputTokens -and $null -eq $totalTokens) {
        return New-MissingTokenUsage -Reason "token_usage_shape_unrecognized"
    }

    return [pscustomobject]@{
        available = $true
        missingReason = $null
        estimated = [bool]$estimated
        inputTokens = $inputTokens
        cachedInputTokens = $cachedInputTokens
        outputTokens = $outputTokens
        reasoningOutputTokens = $reasoningOutputTokens
        totalTokens = $totalTokens
        source = $effectiveSource
    }
}

function Get-RunTokenUsage {
    param([object[]]$Events)
    $metricsEvent = $Events |
        Where-Object {
            ($_.type -eq "runtimeMetricsEvent" -or $_.type -eq "candidateGenerationMetricsEvent" -or
                $_.eventType -eq "RuntimeMetricsEvent" -or $_.eventType -eq "CandidateGenerationMetricsEvent" -or
                $_.kind -eq "RuntimeMetricsEvent" -or $_.kind -eq "CandidateGenerationMetricsEvent") -and
            $null -ne $_.tokenUsage
        } |
        Select-Object -Last 1
    if ($null -ne $metricsEvent) {
        return ConvertTo-RunTokenUsage -RawUsage $metricsEvent.tokenUsage -Source "metrics_event.tokenUsage"
    }

    $completed = $Events | Where-Object { $_.type -eq "exec_completed" -and $null -ne $_.tokenUsage } | Select-Object -Last 1
    if ($null -ne $completed) {
        return ConvertTo-RunTokenUsage -RawUsage $completed.tokenUsage -Source "exec_completed.tokenUsage"
    }

    $tokenEvent = $Events |
        Where-Object {
            ($_.type -eq "thread/tokenUsage/updated" -or $_.method -eq "thread/tokenUsage/updated") -and
            ($null -ne $_.tokenUsage -or ($null -ne $_.params -and $null -ne $_.params.tokenUsage))
        } |
        Select-Object -Last 1

    if ($null -ne $tokenEvent) {
        $rawUsage = if ($null -ne $tokenEvent.tokenUsage) { $tokenEvent.tokenUsage } else { $tokenEvent.params.tokenUsage }
        return ConvertTo-RunTokenUsage -RawUsage $rawUsage -Source "thread/tokenUsage/updated"
    }

    return New-MissingTokenUsage -Reason "token_usage_event_missing"
}

function Read-JsonLines {
    param([string]$Path)
    $items = @()
    if (-not (Test-Path -LiteralPath $Path)) {
        return $items
    }

    foreach ($line in (Get-Content -LiteralPath $Path)) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $items += ($line | ConvertFrom-Json)
        }
        catch {
            $items += [pscustomobject]@{ type = "unparseable_artifact_jsonl"; raw = $line }
        }
    }

    return $items
}

function Get-RunArtifactTokenUsage {
    param([string]$ArtifactRunDirectory)
    if ([string]::IsNullOrWhiteSpace($ArtifactRunDirectory) -or -not (Test-Path -LiteralPath $ArtifactRunDirectory)) {
        return New-MissingTokenUsage -Reason "artifact_run_missing"
    }

    $eventsPath = Join-Path $ArtifactRunDirectory "events.jsonl"
    $events = Read-JsonLines -Path $eventsPath
    $tokenEvent = $events |
        Where-Object {
            ($_.Kind -eq "ThreadTokenUsageUpdated" -or $_.kind -eq "ThreadTokenUsageUpdated" -or
                $_.type -eq "thread/tokenUsage/updated" -or $_.method -eq "thread/tokenUsage/updated") -and
            ($null -ne $_.ThreadTokenUsage -or $null -ne $_.threadTokenUsage -or
                $null -ne $_.tokenUsage -or ($null -ne $_.params -and $null -ne $_.params.tokenUsage))
        } |
        Select-Object -Last 1

    if ($null -eq $tokenEvent) {
        return New-MissingTokenUsage -Reason "artifact_token_usage_event_missing"
    }

    $rawUsage = $null
    $source = "artifact.events.threadTokenUsage"
    if ($null -ne $tokenEvent.ThreadTokenUsage) {
        $rawUsage = $tokenEvent.ThreadTokenUsage
    }
    elseif ($null -ne $tokenEvent.threadTokenUsage) {
        $rawUsage = $tokenEvent.threadTokenUsage
    }
    elseif ($null -ne $tokenEvent.tokenUsage) {
        $rawUsage = $tokenEvent.tokenUsage
        $source = "artifact.events.tokenUsage"
    }
    elseif ($null -ne $tokenEvent.params -and $null -ne $tokenEvent.params.tokenUsage) {
        $rawUsage = $tokenEvent.params.tokenUsage
        $source = "artifact.events.params.tokenUsage"
    }

    return ConvertTo-RunTokenUsage -RawUsage $rawUsage -Source $source
}

function Invoke-EstimatedTokenChannelSelfCheck {
    param([string]$Root)
    $checkRoot = Join-Path $Root "p2-estimated-token-channel"
    $artifactRun = Join-Path $checkRoot "synthetic-cli-artifact-run"
    New-Item -ItemType Directory -Force -Path $artifactRun | Out-Null

    $event = [pscustomobject]@{
        Timestamp = (Get-Date).ToString("o")
        Kind = "ThreadTokenUsageUpdated"
        ThreadId = "thread-p2-estimated"
        TurnId = "turn-p2-estimated"
        ThreadTokenUsage = [pscustomobject]@{
            last = [pscustomobject]@{
                totalTokens = 12
                inputTokens = 5
                cachedInputTokens = 0
                outputTokens = 4
                reasoningOutputTokens = 3
            }
            total = [pscustomobject]@{
                totalTokens = 12
                inputTokens = 5
                cachedInputTokens = 0
                outputTokens = 4
                reasoningOutputTokens = 3
            }
            modelContextWindow = 128000
            estimated = $true
            source = "text_length_estimate"
        }
    }
    $event | ConvertTo-Json -Depth 20 -Compress | Set-Content -LiteralPath (Join-Path $artifactRun "events.jsonl") -Encoding UTF8

    $tokenUsage = Get-RunArtifactTokenUsage -ArtifactRunDirectory $artifactRun
    $estimatedCost = New-EstimatedCost -TokenUsage $tokenUsage -PriceModel $script:PriceModel -ModelId $Model
    $result = [pscustomobject]@{
        purpose = "p2_estimated_token_channel"
        artifactRunDirectory = $artifactRun
        tokenUsage = $tokenUsage
        estimatedCost = $estimatedCost
        passed = ([bool]$tokenUsage.available -and [bool]$tokenUsage.estimated -and
            $tokenUsage.source -eq "text_length_estimate" -and
            -not [bool]$estimatedCost.available -and
            $estimatedCost.missingReason -eq "estimated_token_not_allowed_for_cost")
    }
    Write-Json (Join-Path $checkRoot "result.json") $result
}

function Read-PriceModel {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return [pscustomobject]@{
            available = $false
            missingReason = "price_model_path_missing"
            priceModelVersion = $null
            models = @{}
        }
    }

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $raw = Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json
    return [pscustomobject]@{
        available = $true
        missingReason = $null
        sourcePath = $resolvedPath
        priceModelVersion = $raw.priceModelVersion
        currency = $raw.currency
        effectiveDate = $raw.effectiveDate
        models = $raw.models
    }
}

function Get-PriceModelEntry {
    param([object]$PriceModel, [string]$ModelId)
    if ($null -eq $PriceModel -or -not [bool]$PriceModel.available) {
        return $null
    }

    return Get-JsonPropertyValue -Object $PriceModel.models -Names @($ModelId)
}

function Read-StrategyGate {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return [pscustomobject]@{
            available = $false
            missingReason = "strategy_gate_config_missing"
            sourcePath = $null
            raw = $null
        }
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{
            available = $false
            missingReason = "strategy_gate_config_not_found"
            sourcePath = $Path
            raw = $null
        }
    }

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    try {
        $raw = Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json
        return [pscustomobject]@{
            available = $true
            missingReason = $null
            sourcePath = $resolvedPath
            raw = $raw
        }
    }
    catch {
        return [pscustomobject]@{
            available = $false
            missingReason = "strategy_gate_config_invalid_json"
            sourcePath = $resolvedPath
            raw = $null
        }
    }
}

function Test-StrategyGateFlag {
    param([object]$StrategyGate, [string[]]$Names)
    if ($null -eq $StrategyGate -or -not [bool]$StrategyGate.available -or $null -eq $StrategyGate.raw) {
        return $false
    }

    $value = Get-JsonPropertyValue -Object $StrategyGate.raw -Names $Names
    if ($null -eq $value) {
        return $false
    }

    if ($value -is [bool]) {
        return [bool]$value
    }

    $available = Get-JsonPropertyValue -Object $value -Names @("available", "enabled", "preRegistered", "passed")
    if ($null -eq $available) {
        return $false
    }

    try {
        return [bool]$available
    }
    catch {
        return $false
    }
}

function New-EstimatedCost {
    param([object]$TokenUsage, [object]$PriceModel, [string]$ModelId)
    if ($null -eq $TokenUsage -or -not [bool]$TokenUsage.available) {
        return [pscustomobject]@{
            available = $false
            missingReason = "token_usage_missing"
            amount = $null
            currency = $null
            priceModelVersion = $null
            modelId = $ModelId
            inputTokenPrice = $null
            outputTokenPrice = $null
            effectiveDate = $null
        }
    }

    if ([bool]$TokenUsage.estimated) {
        return [pscustomobject]@{
            available = $false
            missingReason = "estimated_token_not_allowed_for_cost"
            amount = $null
            currency = $null
            priceModelVersion = $null
            modelId = $ModelId
            inputTokenPrice = $null
            outputTokenPrice = $null
            effectiveDate = $null
        }
    }

    if ($null -eq $PriceModel -or -not [bool]$PriceModel.available) {
        return [pscustomobject]@{
            available = $false
            missingReason = "price_model_missing"
            amount = $null
            currency = $null
            priceModelVersion = $null
            modelId = $ModelId
            inputTokenPrice = $null
            outputTokenPrice = $null
            effectiveDate = $null
        }
    }

    $entry = Get-PriceModelEntry -PriceModel $PriceModel -ModelId $ModelId
    if ($null -eq $entry) {
        return [pscustomobject]@{
            available = $false
            missingReason = "price_model_model_missing"
            amount = $null
            currency = $PriceModel.currency
            priceModelVersion = $PriceModel.priceModelVersion
            modelId = $ModelId
            inputTokenPrice = $null
            outputTokenPrice = $null
            effectiveDate = $PriceModel.effectiveDate
        }
    }

    $inputTokenPrice = [double]$entry.inputTokenPrice
    $outputTokenPrice = [double]$entry.outputTokenPrice
    $inputCost = ((Convert-ToNumberOrZero $TokenUsage.inputTokens) / 1000.0) * $inputTokenPrice
    $outputCost = (((Convert-ToNumberOrZero $TokenUsage.outputTokens) + (Convert-ToNumberOrZero $TokenUsage.reasoningOutputTokens)) / 1000.0) * $outputTokenPrice
    return [pscustomobject]@{
        available = $true
        missingReason = $null
        amount = [Math]::Round(($inputCost + $outputCost), 8)
        currency = $PriceModel.currency
        priceModelVersion = $PriceModel.priceModelVersion
        modelId = $ModelId
        inputTokenPrice = $inputTokenPrice
        outputTokenPrice = $outputTokenPrice
        effectiveDate = $PriceModel.effectiveDate
    }
}

function New-RunMetrics {
    param(
        [string]$Phase,
        [string]$TaskId,
        [string]$Group,
        [int]$RunIndex,
        [int]$AttemptIndex,
        [Nullable[int]]$ReviseRound,
        [string]$GraphId,
        [string]$StageId,
        [string]$StepId,
        [object]$Result,
        [int]$ModelCallCount,
        [string]$OutputFile
    )

    $tokenUsage = if ($null -ne $Result -and $null -ne $Result.tokenUsage) { $Result.tokenUsage } else { New-MissingTokenUsage -Reason "token_usage_not_recorded" }
    $estimatedCost = if ($null -ne $Result -and $null -ne $Result.estimatedCost) { $Result.estimatedCost } else { New-EstimatedCost -TokenUsage $tokenUsage -PriceModel $script:PriceModel -ModelId $Model }
    $missingReasons = @()
    if ($null -ne $tokenUsage.missingReason) {
        $missingReasons += $tokenUsage.missingReason
    }
    if ($null -ne $estimatedCost.missingReason) {
        $missingReasons += $estimatedCost.missingReason
    }

    return [pscustomobject]@{
        metricsSchemaVersion = $script:MetricsSchemaVersion
        runId = "$Phase/$(if ([string]::IsNullOrWhiteSpace($TaskId)) { $Group } else { $TaskId })/run-$('{0:D2}' -f $RunIndex)/attempt-$AttemptIndex"
        phase = $Phase
        taskId = $TaskId
        group = $Group
        graphId = $GraphId
        stageId = $StageId
        stepId = $StepId
        attemptIndex = $AttemptIndex
        reviseRound = $ReviseRound
        success = if ($null -ne $Result) { [bool]$Result.success } else { $false }
        latencyMs = if ($null -ne $Result) { $Result.latencyMs } else { $null }
        modelCallCount = $ModelCallCount
        tokenUsage = $tokenUsage
        estimatedCost = $estimatedCost
        missingReasons = $missingReasons
        modelId = $Model
        temperature = if ([string]::IsNullOrWhiteSpace($Temperature)) { $null } else { $Temperature }
        temperatureFixed = -not [string]::IsNullOrWhiteSpace($Temperature)
        seed = if ([string]::IsNullOrWhiteSpace($Seed)) { $null } else { $Seed }
        seedFixed = -not [string]::IsNullOrWhiteSpace($Seed)
        repeatCount = $RepeatCount
        cliPath = $script:ResolvedCliPath
        runnerVersion = $script:RunnerVersion
        outputFile = $OutputFile
    }
}

function New-RunMetricsSchema {
    return [pscustomobject]@{
        schemaVersion = $script:MetricsSchemaVersion
        requiredFields = @(
            "metricsSchemaVersion",
            "runId",
            "phase",
            "taskId",
            "group",
            "graphId",
            "stageId",
            "stepId",
            "attemptIndex",
            "reviseRound",
            "success",
            "latencyMs",
            "modelCallCount",
            "tokenUsage",
            "estimatedCost",
            "missingReasons",
            "modelId",
            "temperature",
            "temperatureFixed",
            "seed",
            "seedFixed",
            "repeatCount",
            "cliPath",
            "runnerVersion",
            "outputFile"
        )
        tokenUsageFields = @("available", "missingReason", "estimated", "inputTokens", "cachedInputTokens", "outputTokens", "reasoningOutputTokens", "totalTokens", "source")
        estimatedCostFields = @("available", "missingReason", "amount", "currency", "priceModelVersion", "modelId", "inputTokenPrice", "outputTokenPrice", "effectiveDate")
    }
}

function New-PriceModelManifest {
    param([object]$PriceModel)
    if ($null -eq $PriceModel -or -not [bool]$PriceModel.available) {
        return [pscustomobject]@{
            available = $false
            missingReason = if ($null -ne $PriceModel) { $PriceModel.missingReason } else { "price_model_not_loaded" }
            priceModelVersion = $null
            currency = $null
            effectiveDate = $null
            sourcePath = $null
        }
    }

    return [pscustomobject]@{
        available = $true
        missingReason = $null
        priceModelVersion = $PriceModel.priceModelVersion
        currency = $PriceModel.currency
        effectiveDate = $PriceModel.effectiveDate
        sourcePath = $PriceModel.sourcePath
    }
}

function New-PriceModelSchema {
    return [pscustomobject]@{
        schemaVersion = "adaptive-kernel-price-model-v1"
        requiredFields = @("priceModelVersion", "currency", "effectiveDate", "models")
        modelEntryRequiredFields = @("inputTokenPrice", "outputTokenPrice")
        priceUnit = "per_1000_tokens"
        modelLookupKey = "modelId"
    }
}

function Read-JsonlText {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return ""
    }

    $builder = [System.Text.StringBuilder]::new()
    foreach ($line in (Get-Content -LiteralPath $Path)) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $event = $line | ConvertFrom-Json
            if ($null -ne $event.text) {
                [void]$builder.Append([string]$event.text)
            }
            elseif ($null -ne $event.assistantText) {
                [void]$builder.Append([string]$event.assistantText)
            }
        }
        catch {
            [void]$builder.Append($line)
        }
    }

    return $builder.ToString()
}

function Count-TextPattern {
    param([string]$Text, [string]$Pattern)
    if ([string]::IsNullOrEmpty($Text)) {
        return 0
    }

    return ([regex]::Matches($Text, $Pattern)).Count
}

function Test-BaselineRun {
    param([object]$Task, [object]$Result)

    $text = Read-JsonlText -Path $Result.outputFile
    $signals = @()
    $status = "failed"
    $passed = $false

    switch ($Task.kind) {
        "readonly-analysis" {
            $passed = [bool]$Result.success -and $Result.assistantTextLength -gt 0
            if ($passed) { $status = "passed"; $signals += "assistant_text_present" }
        }
        "single-tool" {
            $toolMentions = Count-TextPattern -Text $text -Pattern 'ToolCallStarted|已等待到事件'
            $passed = $toolMentions -ge 1 -and $text -match 'toolCallObserved"?\s*:\s*true'
            if ($passed) { $status = "passed"; $signals += "tool_call_observed_true"; $signals += "tool_boundary_count=$toolMentions" }
        }
        "multi-tool" {
            $toolMentions = Count-TextPattern -Text $text -Pattern 'ToolCallStarted|已等待到事件'
            $passed = $toolMentions -ge 2
            if ($passed) { $status = "passed"; $signals += "multi_tool_mentions=$toolMentions" }
        }
        "steer" {
            $hasSteer = $text -match '引导成功|steer_applied\s*=\s*true|steer_applied=true'
            $passed = $hasSteer
            if ($passed) { $status = "passed"; $signals += "steer_applied" }
        }
        "interrupt-resume" {
            $hasInterrupt = $text -match '已中断当前回合|已请求中断当前回合|interrupt'
            $hasResume = $text -match '中断后改向|src|docs'
            $passed = $hasInterrupt -and $hasResume
            if ($passed) { $status = "passed"; $signals += "interrupt_observed"; $signals += "resume_redirection_observed" }
        }
        "subagent" {
            if ($text -match '没有\s*`?spawn_agent`?\s*工具|没有 spawn_agent 工具|不包含该能力') {
                $status = "a2_delta_subagent_unavailable"
                $passed = $false
                $signals += "spawn_agent_unavailable"
            }
            else {
                $passed = [bool]$Result.success -and ($text -match 'spawn_agent|子代理|subagent')
                if ($passed) { $status = "passed"; $signals += "subagent_signal_observed" }
            }
        }
        default {
            $passed = [bool]$Result.success
            if ($passed) { $status = "passed" }
        }
    }

    return [pscustomobject]@{
        passed = $passed
        status = $status
        evidenceSignals = $signals
        textLength = $text.Length
        toolSignalCount = Count-TextPattern -Text $text -Pattern 'ToolCallStarted|已等待到事件'
    }
}

function Get-Mean {
    param([double[]]$Values)
    if ($Values.Count -eq 0) {
        return $null
    }

    return ($Values | Measure-Object -Average).Average
}

function Get-StdDev {
    param([double[]]$Values)
    if ($Values.Count -le 1) {
        return 0
    }

    $mean = Get-Mean -Values $Values
    $sum = 0.0
    foreach ($value in $Values) {
        $sum += [Math]::Pow($value - $mean, 2)
    }

    return [Math]::Sqrt($sum / ($Values.Count - 1))
}

function Get-RunMetricValues {
    param([object[]]$Items, [string]$MetricName)
    $values = @()
    foreach ($item in $Items) {
        if ($null -eq $item.runMetrics) {
            continue
        }

        if ($MetricName -eq "tokenTotal") {
            $value = $item.runMetrics.tokenUsage.totalTokens
        }
        elseif ($MetricName -eq "costAmount") {
            $value = $item.runMetrics.estimatedCost.amount
        }
        else {
            $value = $null
        }

        if ($null -ne $value) {
            $values += [double]$value
        }
    }

    return $values
}

function New-BaselineVarianceSummary {
    param([object[]]$Runs)

    $summary = @()
    foreach ($group in ($Runs | Group-Object taskId)) {
        $items = @($group.Group)
        $latencies = @($items | ForEach-Object { [double]$_.latencyMs })
        $tokens = @(Get-RunMetricValues -Items $items -MetricName "tokenTotal")
        $costs = @(Get-RunMetricValues -Items $items -MetricName "costAmount")
        $toolSignals = @($items | ForEach-Object { [double]$_.toolSignalCount })
        $passedCount = @($items | Where-Object { $_.baselineTaskPassed -eq $true }).Count
        $statusCounts = @{}
        foreach ($statusGroup in ($items | Group-Object baselineTaskStatus)) {
            $statusCounts[$statusGroup.Name] = $statusGroup.Count
        }

        $summary += [pscustomobject]@{
            taskId = $group.Name
            strategyId = "baseline.hybrid-kernel-shell.fixed"
            modelRouteId = $Model
            runCount = $items.Count
            successRate = if ($items.Count -eq 0) { 0 } else { [Math]::Round($passedCount / $items.Count, 4) }
            latencyMeanMs = [Math]::Round((Get-Mean -Values $latencies), 2)
            latencyStdDevMs = [Math]::Round((Get-StdDev -Values $latencies), 2)
            tokenMean = if ($tokens.Count -eq 0) { $null } else { [Math]::Round((Get-Mean -Values $tokens), 2) }
            tokenStdDev = if ($tokens.Count -eq 0) { $null } else { [Math]::Round((Get-StdDev -Values $tokens), 2) }
            costMean = if ($costs.Count -eq 0) { $null } else { [Math]::Round((Get-Mean -Values $costs), 8) }
            costStdDev = if ($costs.Count -eq 0) { $null } else { [Math]::Round((Get-StdDev -Values $costs), 8) }
            toolCallMean = [Math]::Round((Get-Mean -Values $toolSignals), 2)
            toolCallStdDev = [Math]::Round((Get-StdDev -Values $toolSignals), 2)
            traceShapeVariance = "manual-reference"
            traceShapeCanonicalizationVersion = "adaptive-kernel-baseline-v1"
            qualityMean = $null
            qualityStdDev = $null
            statusCounts = $statusCounts
        }
    }

    return $summary
}

function New-C0RevisePrompt {
    param([string]$AssistantText, [object[]]$ValidationErrors)
    $errorsText = ($ValidationErrors | ForEach-Object { "- $_" }) -join [Environment]::NewLine
    return @"
不要调用任何工具。你上一次输出的 StageGraph JSON 未通过本地契约校验。
只根据下面的结构化错误修正 JSON。不要解释，不要 Markdown，只输出修正后的 JSON 对象。

错误：
$errorsText

上一次输出：
$AssistantText

必须满足：
- 根对象直接包含 graphId, version, intentKind, entryStageId, stages, edges, policies, budgets。
- stage 字段必须是 stageId, kind, objective, capabilityToolIds, sideEffectLevel, budget。
- edge 字段必须是 fromStageId, toStageId, transitionKind。
- 不得使用 StageGraph、Turn、Nodes、Edges、Id、From、To、Kind、Capability、TokenLimit、TimeLimitMs。
"@
}

function New-C0Summary {
    param([object[]]$Runs)

    $summary = @()
    foreach ($group in ($Runs | Group-Object group)) {
        $items = @($group.Group)
        $firstValid = @($items | Where-Object { $_.firstLocalContractValid -eq $true }).Count
        $finalValid = @($items | Where-Object { $_.finalLocalContractValid -eq $true }).Count
        $latencies = @($items | ForEach-Object { [double]$_.totalLatencyMs })
        $reviseRounds = @($items | ForEach-Object { [double]$_.reviseRounds })
        $modelCalls = @($items | ForEach-Object { [double]$_.modelCallCount })
        $tokens = @(Get-RunMetricValues -Items $items -MetricName "tokenTotal")
        $costs = @(Get-RunMetricValues -Items $items -MetricName "costAmount")
        $rejectionCounts = @{}
        foreach ($item in $items) {
            foreach ($error in @($item.firstValidationErrors)) {
                $key = if ($error -match '^([^:]+):') { $Matches[1] } else { [string]$error }
                if (-not $rejectionCounts.ContainsKey($key)) {
                    $rejectionCounts[$key] = 0
                }
                $rejectionCounts[$key] += 1
            }
        }

        $summary += [pscustomobject]@{
            group = $group.Name
            promptVersion = ($items | Select-Object -First 1).promptVersion
            schema = ($items | Select-Object -First 1).schema
            runCount = $items.Count
            firstLegalRate = if ($items.Count -eq 0) { 0 } else { [Math]::Round($firstValid / $items.Count, 4) }
            finalLegalRate = if ($items.Count -eq 0) { 0 } else { [Math]::Round($finalValid / $items.Count, 4) }
            averageReviseRounds = [Math]::Round((Get-Mean -Values $reviseRounds), 2)
            maxReviseRounds = ($reviseRounds | Measure-Object -Maximum).Maximum
            latencyMeanMs = [Math]::Round((Get-Mean -Values $latencies), 2)
            latencyMaxMs = ($latencies | Measure-Object -Maximum).Maximum
            modelCallMean = [Math]::Round((Get-Mean -Values $modelCalls), 2)
            modelCallMax = ($modelCalls | Measure-Object -Maximum).Maximum
            tokenMean = if ($tokens.Count -eq 0) { $null } else { [Math]::Round((Get-Mean -Values $tokens), 2) }
            tokenStdDev = if ($tokens.Count -eq 0) { $null } else { [Math]::Round((Get-StdDev -Values $tokens), 2) }
            costMean = if ($costs.Count -eq 0) { $null } else { [Math]::Round((Get-Mean -Values $costs), 8) }
            costStdDev = if ($costs.Count -eq 0) { $null } else { [Math]::Round((Get-StdDev -Values $costs), 8) }
            rejectionDistribution = $rejectionCounts
        }
    }

    return $summary
}

function Test-JsonProperty {
    param([object]$Object, [string]$Name)
    return ($null -ne $Object -and $Object.PSObject.Properties.Name -contains $Name)
}

function ConvertFrom-AssistantJson {
    param([string]$AssistantText)
    if ([string]::IsNullOrWhiteSpace($AssistantText)) {
        return [pscustomobject]@{ parsed = $false; value = $null; normalizedText = ""; error = "assistantText is empty" }
    }

    $text = $AssistantText.Trim()
    if ($text -match '(?s)^```(?:json)?\s*(.*?)\s*```$') {
        $text = $Matches[1].Trim()
    }

    try {
        return [pscustomobject]@{ parsed = $true; value = ($text | ConvertFrom-Json); normalizedText = $text; error = $null }
    }
    catch {
        return [pscustomobject]@{ parsed = $false; value = $null; normalizedText = $text; error = $_.Exception.Message }
    }
}

function Test-C0StageGraphCandidate {
    param([string]$AssistantText)
    $parsed = ConvertFrom-AssistantJson -AssistantText $AssistantText
    $errors = @()
    if (-not $parsed.parsed) {
        $errors += $parsed.error
        return [pscustomobject]@{
            jsonParsed = $false
            contractValid = $false
            validationErrors = $errors
            normalizedJsonTextLength = $parsed.normalizedText.Length
            stageCount = 0
            edgeCount = 0
        }
    }

    $graph = $parsed.value
    foreach ($required in @("graphId", "version", "intentKind", "entryStageId", "stages", "edges", "policies", "budgets")) {
        if (-not (Test-JsonProperty -Object $graph -Name $required)) {
            $errors += "missing root property: $required"
        }
    }

    $stages = @()
    if (Test-JsonProperty -Object $graph -Name "stages") {
        $stages = @($graph.stages)
        if ($stages.Count -lt 2) {
            $errors += "stages must contain at least 2 items"
        }

        foreach ($stage in $stages) {
            foreach ($required in @("stageId", "kind", "objective", "capabilityToolIds", "sideEffectLevel", "budget")) {
                if (-not (Test-JsonProperty -Object $stage -Name $required)) {
                    $errors += "stage missing property: $required"
                }
            }

            if ((Test-JsonProperty -Object $stage -Name "budget") -and $null -ne $stage.budget) {
                foreach ($required in @("tokenBudget", "timeBudgetMs", "toolCallBudget")) {
                    if (-not (Test-JsonProperty -Object $stage.budget -Name $required)) {
                        $errors += "stage budget missing property: $required"
                    }
                }
            }
        }
    }

    $stageIds = @($stages | ForEach-Object { if (Test-JsonProperty -Object $_ -Name "stageId") { [string]$_.stageId } })
    if ((Test-JsonProperty -Object $graph -Name "entryStageId") -and $stageIds.Count -gt 0 -and -not ($stageIds -contains [string]$graph.entryStageId)) {
        $errors += "entryStageId does not reference a stage"
    }

    $edges = @()
    if (Test-JsonProperty -Object $graph -Name "edges") {
        $edges = @($graph.edges)
        foreach ($edge in $edges) {
            foreach ($required in @("fromStageId", "toStageId", "transitionKind")) {
                if (-not (Test-JsonProperty -Object $edge -Name $required)) {
                    $errors += "edge missing property: $required"
                }
            }

            if ((Test-JsonProperty -Object $edge -Name "fromStageId") -and $stageIds.Count -gt 0 -and -not ($stageIds -contains [string]$edge.fromStageId)) {
                $errors += "edge fromStageId does not reference a stage"
            }

            if ((Test-JsonProperty -Object $edge -Name "toStageId") -and $stageIds.Count -gt 0 -and -not ($stageIds -contains [string]$edge.toStageId)) {
                $errors += "edge toStageId does not reference a stage"
            }
        }
    }

    if (Test-JsonProperty -Object $graph -Name "policies") {
        foreach ($required in @("allowedCapabilityToolIds", "maxSideEffectLevel", "requiresHumanGate")) {
            if (-not (Test-JsonProperty -Object $graph.policies -Name $required)) {
                $errors += "policies missing property: $required"
            }
        }
    }

    if (Test-JsonProperty -Object $graph -Name "budgets") {
        foreach ($required in @("tokenBudget", "timeBudgetMs", "toolCallBudget")) {
            if (-not (Test-JsonProperty -Object $graph.budgets -Name $required)) {
                $errors += "budgets missing property: $required"
            }
        }
    }

    return [pscustomobject]@{
        jsonParsed = $true
        contractValid = ($errors.Count -eq 0)
        validationErrors = $errors
        normalizedJsonTextLength = $parsed.normalizedText.Length
        stageCount = $stages.Count
        edgeCount = $edges.Count
    }
}

function Get-BaselineTasks {
    @(
        [pscustomobject]@{
            taskId = "baseline-task-01"
            kind = "readonly-analysis"
            prompt = "请只读分析当前目录下 README 或项目文件结构，输出三条关于该仓库用途的简短观察。不要修改文件。"
        },
        [pscustomobject]@{
            taskId = "baseline-task-02"
            kind = "single-tool"
            prompt = "必须先调用一次只读文件或目录查看工具，确认当前仓库根目录名称，然后只输出 JSON：{`"workspaceName`":string,`"toolCallObserved`":true}。不要修改文件。"
        },
        [pscustomobject]@{
            taskId = "baseline-task-03"
            kind = "multi-tool"
            prompt = "必须连续调用至少两次只读工具：第一次查看 docs 顶层结构，第二次查看 src 顶层结构。完成后输出两个目录各自的文件/子目录数量估计。不要修改文件。"
        },
        [pscustomobject]@{
            taskId = "baseline-task-04"
            kind = "steer"
            prompt = "请先使用只读工具查看 docs 目录顶层结构，然后准备输出三条观察。若收到 steer，请改为只输出一条关于 docs 目录的观察，并明确写出 steer_applied=true。不要修改文件。"
        },
        [pscustomobject]@{
            taskId = "baseline-task-05"
            kind = "interrupt-resume"
            prompt = "请先使用只读工具查看 src 和 docs 目录顶层结构，然后继续分析直到收到中断。不要修改文件。"
        },
        [pscustomobject]@{
            taskId = "baseline-task-06"
            kind = "subagent"
            prompt = "请使用 spawn_agent 派生一个子代理执行只读仓库结构检查，等待结果后汇总。不要修改文件。"
        }
    )
}

function New-C0SchemaFiles {
    param([string]$Root)
    $schemaRoot = Join-Path $Root "schemas"
    New-Item -ItemType Directory -Force -Path $schemaRoot | Out-Null

    $minimal = @{
        type = "object"
        additionalProperties = $false
        required = @("graphId", "version", "intentKind", "entryStageId", "stages", "edges", "policies", "budgets")
        properties = @{
            graphId = @{ type = "string" }
            version = @{ type = "string" }
            intentKind = @{ type = "string"; enum = @("Turn", "Resume", "Recovery", "Evaluation") }
            entryStageId = @{ type = "string" }
            stages = @{
                type = "array"
                items = @{
                    type = "object"; additionalProperties = $false
                    required = @("stageId", "kind", "objective", "capabilityToolIds", "sideEffectLevel", "budget")
                    properties = @{
                        stageId = @{ type = "string" }
                        kind = @{ type = "string" }
                        objective = @{ type = "string" }
                        capabilityToolIds = @{ type = "array"; items = @{ type = "string" } }
                        sideEffectLevel = @{ type = "string"; enum = @("None", "ReadOnly", "WorkspaceWrite", "ExternalNetwork", "HostMutation", "Privileged") }
                        budget = @{
                            type = "object"; additionalProperties = $false
                            required = @("tokenBudget", "timeBudgetMs", "toolCallBudget")
                            properties = @{
                                tokenBudget = @{ type = "integer" }
                                timeBudgetMs = @{ type = "integer" }
                                toolCallBudget = @{ type = "integer" }
                            }
                        }
                    }
                }
            }
            edges = @{
                type = "array"
                items = @{
                    type = "object"; additionalProperties = $false
                    required = @("fromStageId", "toStageId", "transitionKind")
                    properties = @{
                        fromStageId = @{ type = "string" }
                        toStageId = @{ type = "string" }
                        transitionKind = @{ type = "string"; enum = @("Success", "Failure", "Conditional", "Recovery", "Abort") }
                    }
                }
            }
            policies = @{
                type = "object"; additionalProperties = $false
                required = @("allowedCapabilityToolIds", "maxSideEffectLevel", "requiresHumanGate")
                properties = @{
                    allowedCapabilityToolIds = @{ type = "array"; items = @{ type = "string" } }
                    maxSideEffectLevel = @{ type = "string"; enum = @("None", "ReadOnly", "WorkspaceWrite", "ExternalNetwork", "HostMutation", "Privileged") }
                    requiresHumanGate = @{ type = "boolean" }
                }
            }
            budgets = @{
                type = "object"; additionalProperties = $false
                required = @("tokenBudget", "timeBudgetMs", "toolCallBudget")
                properties = @{
                    tokenBudget = @{ type = "integer" }
                    timeBudgetMs = @{ type = "integer" }
                    toolCallBudget = @{ type = "integer" }
                }
            }
        }
    }

    $minimalPath = Join-Path $schemaRoot "stage-graph-minimal.schema.json"
    Write-Json $minimalPath $minimal

    $full = $minimal.psobject.Copy()
    $fullPath = Join-Path $schemaRoot "stage-graph-full.schema.json"
    Write-Json $fullPath $minimal

    return [pscustomobject]@{
        minimal = $minimalPath
        full = $fullPath
    }
}

function New-CSchemaFiles {
    param([string]$Root)
    $schemaRoot = Join-Path $Root "schemas"
    New-Item -ItemType Directory -Force -Path $schemaRoot | Out-Null

    $budget = @{
        type = "object"; additionalProperties = $false
        required = @("tokenBudget", "timeBudgetMs", "toolCallBudget")
        properties = @{
            tokenBudget = @{ type = "integer"; minimum = 1 }
            timeBudgetMs = @{ type = "integer"; minimum = 1 }
            costBudget = @{ type = "number"; minimum = 0 }
            retryBudget = @{ type = "integer"; minimum = 0 }
            toolCallBudget = @{ type = "integer"; minimum = 0 }
        }
    }
    $stage = @{
        type = "object"; additionalProperties = $false
        required = @("stageId", "kind", "objective", "capabilityToolIds", "sideEffectLevel", "budget")
        properties = @{
            stageId = @{ type = "string" }
            kind = @{ type = "string" }
            objective = @{ type = "string" }
            allowedKernelToolIds = @{ type = "array"; items = @{ type = "string" } }
            capabilityToolIds = @{ type = "array"; items = @{ type = "string" } }
            sideEffectLevel = @{ type = "string"; enum = @("None", "ReadOnly", "WorkspaceWrite", "ExternalNetwork", "ExternalMutation", "HostMutation", "Privileged") }
            budget = $budget
            contextPolicy = @{
                type = "object"; additionalProperties = $false
                required = @("maxInputTokens", "allowedSourceKinds")
                properties = @{
                    maxInputTokens = @{ type = "integer"; minimum = 1 }
                    allowedSourceKinds = @{ type = "array"; items = @{ type = "string" } }
                    preserveLatestUserCorrection = @{ type = "boolean" }
                    requireEvidenceRefs = @{ type = "boolean" }
                    failClosed = @{ type = "boolean" }
                    policyId = @{ type = "string" }
                }
            }
        }
    }
    $edge = @{
        type = "object"; additionalProperties = $false
        required = @("fromStageId", "toStageId", "transitionKind")
        properties = @{
            edgeId = @{ type = "string" }
            fromStageId = @{ type = "string" }
            toStageId = @{ type = "string" }
            transitionKind = @{ type = "string"; enum = @("Success", "Failure", "Conditional", "Recovery", "Abort") }
            conditionKind = @{ type = "string" }
            requiredSignals = @{ type = "array"; items = @{ type = "string" } }
        }
    }
    $graph = @{
        type = "object"; additionalProperties = $false
        required = @("graphId", "version", "intentKind", "entryStageId", "stages", "edges", "policies", "budgets")
        properties = @{
            graphId = @{ type = "string" }
            version = @{ type = "string" }
            intentKind = @{ type = "string"; enum = @("Turn", "Resume", "Interrupt", "Review", "Compaction", "Recovery", "Evaluation") }
            entryStageId = @{ type = "string" }
            stages = @{ type = "array"; minItems = 1; items = $stage }
            edges = @{ type = "array"; items = $edge }
            policies = @{
                type = "object"; additionalProperties = $false
                required = @("allowedCapabilityToolIds", "maxSideEffectLevel", "requiresHumanGate")
                properties = @{
                    requiredPolicyIds = @{ type = "array"; items = @{ type = "string" } }
                    allowedKernelToolIds = @{ type = "array"; items = @{ type = "string" } }
                    allowedCapabilityToolIds = @{ type = "array"; items = @{ type = "string" } }
                    allowedModuleIds = @{ type = "array"; items = @{ type = "string" } }
                    maxSideEffectLevel = @{ type = "string"; enum = @("None", "ReadOnly", "WorkspaceWrite", "ExternalNetwork", "ExternalMutation", "HostMutation", "Privileged") }
                    requiresHumanGate = @{ type = "boolean" }
                }
            }
            budgets = $budget
            checkpointRules = @{
                type = "object"; additionalProperties = $false
                required = @("enabled")
                properties = @{
                    enabled = @{ type = "boolean" }
                    requiredStageIds = @{ type = "array"; items = @{ type = "string" } }
                }
            }
            recoveryRules = @{
                type = "object"; additionalProperties = $false
                required = @("enabled")
                properties = @{
                    enabled = @{ type = "boolean" }
                    maxRecoveryAttempts = @{ type = "integer"; minimum = 0 }
                }
            }
            evaluationRules = @{
                type = "object"; additionalProperties = $false
                required = @("enabled")
                properties = @{
                    enabled = @{ type = "boolean" }
                    metricIds = @{ type = "array"; items = @{ type = "string" } }
                }
            }
        }
    }
    $patch = @{
        type = "object"; additionalProperties = $false
        required = @("targetGraphId", "operations")
        properties = @{
            proposalId = @{ type = "string" }
            targetGraphId = @{ type = "string" }
            operations = @{
                type = "array"; minItems = 1
                items = @{
                    type = "object"; additionalProperties = $false
                    required = @("operationKind")
                    properties = @{
                        operationKind = @{ type = "string"; enum = @("add_stage", "replace_stage", "remove_stage", "add_edge", "replace_edge", "remove_edge", "set_entry_stage", "replace_policies", "replace_budgets") }
                        targetStageId = @{ type = "string" }
                        targetEdgeId = @{ type = "string" }
                        payload = @{ type = "object" }
                    }
                }
            }
        }
    }
    $recovery = @{
        type = "object"; additionalProperties = $false
        required = @("recoveryKind", "actionRefs", "requiresHumanGate")
        properties = @{
            proposalId = @{ type = "string" }
            recoveryKind = @{ type = "string" }
            actionRefs = @{ type = "array"; items = @{ type = "string" } }
            requiresHumanGate = @{ type = "boolean" }
        }
    }
    $contextPolicy = @{
        type = "object"; additionalProperties = $false
        required = @("maxInputTokens", "allowedSourceKinds")
        properties = @{
            maxInputTokens = @{ type = "integer"; minimum = 1 }
            allowedSourceKinds = @{ type = "array"; items = @{ type = "string" } }
            preserveLatestUserCorrection = @{ type = "boolean" }
            requireEvidenceRefs = @{ type = "boolean" }
            failClosed = @{ type = "boolean" }
            policyId = @{ type = "string" }
        }
    }
    $checkpoint = @{
        type = "object"; additionalProperties = $false
        required = @("operationId", "sourceStageId", "checkpointRef")
        properties = @{
            operationId = @{ type = "string" }
            sourceStageId = @{ type = "string" }
            checkpointRef = @{ type = "string" }
        }
    }

    $paths = [ordered]@{
        graph = Join-Path $schemaRoot "c-stage-graph.schema.json"
        patch = Join-Path $schemaRoot "c-stage-graph-patch.schema.json"
        recovery = Join-Path $schemaRoot "c-recovery-proposal.schema.json"
        contextPolicy = Join-Path $schemaRoot "c-context-policy.schema.json"
        checkpoint = Join-Path $schemaRoot "c-checkpoint-proposal.schema.json"
    }
    Write-Json $paths.graph $graph
    Write-Json $paths.patch $patch
    Write-Json $paths.recovery $recovery
    Write-Json $paths.contextPolicy $contextPolicy
    Write-Json $paths.checkpoint $checkpoint
    return [pscustomobject]$paths
}

function Get-CTasks {
    param([object]$Schemas)
    $graphInstruction = @'
不要调用任何工具。只输出一个 JSON 对象，不要 Markdown，不要解释。
根对象必须直接包含这些字段：graphId, version, intentKind, entryStageId, stages, edges, policies, budgets。
stages 数组必须至少包含两个 stage 对象。
stage 字段必须是：stageId, kind, objective, capabilityToolIds, sideEffectLevel, budget。
edge 字段必须是：edgeId, fromStageId, toStageId, transitionKind。
policies 字段必须包含：allowedCapabilityToolIds, maxSideEffectLevel, requiresHumanGate。
根级 budgets 字段必须存在；每个 stage 内部也必须有 budget 字段。budgets 和 budget 都必须包含：tokenBudget, timeBudgetMs, toolCallBudget。
不要使用 StageGraph、Nodes、Edges、Id、From、To、Kind、Capability、TokenLimit、TimeLimitMs 这些包装或 PascalCase 字段。
'@
    $patchInstruction = @'
不要调用任何工具。只输出一个 JSON 对象，不要 Markdown，不要解释。
根对象必须直接包含 targetGraphId 和 operations。
operations 必须是数组；每个 operation 必须包含 operationKind。
replace_stage 的 payload 必须是 stage 对象，字段必须是：stageId, kind, objective, capabilityToolIds, sideEffectLevel, budget。
不要使用 Patch、Operations 包装对象，也不要使用 PascalCase 字段。
'@
    @(
        [pscustomobject]@{ taskId = "c-task-01-simple-graph"; candidateKind = "graph"; schema = $Schemas.graph; prompt = "$graphInstruction`n任务：一个 Turn StageGraph，两个 stage：model.invoke.initial -> diagnostics.emit_trace，副作用 None，requiresHumanGate=false。" },
        [pscustomobject]@{ taskId = "c-task-02-single-tool-graph"; candidateKind = "graph"; schema = $Schemas.graph; prompt = "$graphInstruction`n任务：一个包含 tool.workspace.read stage 的 Turn StageGraph，toolCallBudget=1，副作用 ReadOnly。" },
        [pscustomobject]@{ taskId = "c-task-03-multi-tool-graph"; candidateKind = "graph"; schema = $Schemas.graph; prompt = "$graphInstruction`n任务：一个包含 tool.workspace.read 和 diagnostics.emit_trace 两个能力 stage 的 Turn StageGraph。" },
        [pscustomobject]@{ taskId = "c-task-04-model-route-graph"; candidateKind = "graph"; schema = $Schemas.graph; prompt = "$graphInstruction`n任务：一个表达 model route selection 的 Turn StageGraph，使用 model.invoke.initial 能力。" },
        [pscustomobject]@{ taskId = "c-task-05-human-gate-graph"; candidateKind = "graph"; schema = $Schemas.graph; prompt = "$graphInstruction`n任务：一个 WorkspaceWrite 风险的 StageGraph，requiresHumanGate=true。" },
        [pscustomobject]@{ taskId = "c-task-06-patch"; candidateKind = "patch"; schema = $Schemas.patch; prompt = "$patchInstruction`n任务：targetGraphId=graph-candidate-001，replace_stage stage-2，将 objective 改为 emit patched diagnostic trace。" },
        [pscustomobject]@{ taskId = "c-task-07-recovery"; candidateKind = "recovery"; schema = $Schemas.recovery; prompt = "不要调用工具。只输出符合 schema 的 JSON：recoveryKind=retry_with_reduced_context，actionRefs 包含 action.trim_context，requiresHumanGate=true。" },
        [pscustomobject]@{ taskId = "c-task-08-checkpoint"; candidateKind = "checkpoint"; schema = $Schemas.checkpoint; prompt = "不要调用工具。只输出符合 schema 的 JSON：operationId=operation.checkpoint.acceptance，sourceStageId=stage-1，checkpointRef=checkpoint.acceptance.stage-1。" },
        [pscustomobject]@{ taskId = "c-task-09-context-policy"; candidateKind = "contextPolicy"; schema = $Schemas.contextPolicy; prompt = "不要调用工具。只输出符合 schema 的 JSON：maxInputTokens=1024，allowedSourceKinds 包含 CurrentUserInput 和 ToolEvidence，failClosed=true。" },
        [pscustomobject]@{ taskId = "c-task-10-a2-abstract-graph"; candidateKind = "graph"; schema = $Schemas.graph; prompt = "$graphInstruction`n任务：一个抽象表达 steer、interrupt、resume、subagent 边界的 Turn StageGraph，所有高风险 stage requiresHumanGate=true。" }
    )
}

function Get-CGraphSafetyAssessment {
    param([string]$AssistantText)
    $parsed = ConvertFrom-AssistantJson -AssistantText $AssistantText
    if (-not $parsed.parsed) {
        return [pscustomobject]@{
            highRiskOverreach = $false
            degenerateCoreLoop = $false
            highRiskStageCount = 0
            coreLoopStageCount = 0
            riskSignals = @("json_not_parsed")
        }
    }

    $graph = $parsed.value
    $stages = if (Test-JsonProperty -Object $graph -Name "stages") { @($graph.stages) } else { @() }
    $highRiskLevels = @("WorkspaceWrite", "ExternalNetwork", "HostMutation", "Privileged")
    $highRiskStages = @($stages | Where-Object {
        (Test-JsonProperty -Object $_ -Name "sideEffectLevel") -and
        ($highRiskLevels -contains [string]$_.sideEffectLevel)
    })
    $coreLoopStages = @($stages | Where-Object {
        (Test-JsonProperty -Object $_ -Name "kind") -and
        ([string]$_.kind -match '(^|[._-])core[._-]?loop($|[._-])')
    })

    $requiresHumanGate = $false
    if ((Test-JsonProperty -Object $graph -Name "policies") -and $null -ne $graph.policies) {
        $requiresHumanGateValue = Get-JsonPropertyValue -Object $graph.policies -Names @("requiresHumanGate")
        if ($null -ne $requiresHumanGateValue) {
            try {
                $requiresHumanGate = [bool]$requiresHumanGateValue
            }
            catch {
                $requiresHumanGate = $false
            }
        }
    }

    $riskSignals = @()
    if ($highRiskStages.Count -gt 0) {
        $riskSignals += "high_risk_stage_present"
    }
    if ($highRiskStages.Count -gt 0 -and -not $requiresHumanGate) {
        $riskSignals += "high_risk_without_human_gate"
    }
    if ($stages.Count -gt 0 -and $coreLoopStages.Count -eq $stages.Count) {
        $riskSignals += "all_stages_core_loop"
    }

    return [pscustomobject]@{
        highRiskOverreach = ($highRiskStages.Count -gt 0 -and -not $requiresHumanGate)
        degenerateCoreLoop = ($stages.Count -gt 0 -and $coreLoopStages.Count -eq $stages.Count)
        highRiskStageCount = $highRiskStages.Count
        coreLoopStageCount = $coreLoopStages.Count
        riskSignals = $riskSignals
    }
}

function Test-CCandidate {
    param([string]$AssistantText, [string]$CandidateKind)
    if ($CandidateKind -eq "graph") {
        $result = Test-C0StageGraphCandidate -AssistantText $AssistantText
        $safety = Get-CGraphSafetyAssessment -AssistantText $AssistantText
        $result | Add-Member -NotePropertyName highRiskOverreach -NotePropertyValue $safety.highRiskOverreach -Force
        $result | Add-Member -NotePropertyName degenerateCoreLoop -NotePropertyValue $safety.degenerateCoreLoop -Force
        $result | Add-Member -NotePropertyName highRiskStageCount -NotePropertyValue $safety.highRiskStageCount -Force
        $result | Add-Member -NotePropertyName coreLoopStageCount -NotePropertyValue $safety.coreLoopStageCount -Force
        $result | Add-Member -NotePropertyName riskSignals -NotePropertyValue $safety.riskSignals -Force
        return $result
    }

    $parsed = ConvertFrom-AssistantJson -AssistantText $AssistantText
    $errors = @()
    if (-not $parsed.parsed) {
        $errors += $parsed.error
        return [pscustomobject]@{
            jsonParsed = $false
            contractValid = $false
            validationErrors = $errors
            normalizedJsonTextLength = $parsed.normalizedText.Length
            stageCount = 0
            edgeCount = 0
            highRiskOverreach = $false
            degenerateCoreLoop = $false
            highRiskStageCount = 0
            coreLoopStageCount = 0
            riskSignals = @("json_not_parsed")
        }
    }

    $value = $parsed.value
    $requiredFields = switch ($CandidateKind) {
        "patch" { @("targetGraphId", "operations") }
        "recovery" { @("recoveryKind", "actionRefs", "requiresHumanGate") }
        "checkpoint" { @("operationId", "sourceStageId", "checkpointRef") }
        "contextPolicy" { @("maxInputTokens", "allowedSourceKinds") }
        default { @() }
    }

    foreach ($required in $requiredFields) {
        if (-not (Test-JsonProperty -Object $value -Name $required)) {
            $errors += "$CandidateKind missing property: $required"
        }
    }

    return [pscustomobject]@{
        jsonParsed = $true
        contractValid = ($errors.Count -eq 0)
        validationErrors = $errors
        normalizedJsonTextLength = $parsed.normalizedText.Length
        stageCount = 0
        edgeCount = 0
        highRiskOverreach = $false
        degenerateCoreLoop = $false
        highRiskStageCount = 0
        coreLoopStageCount = 0
        riskSignals = @()
    }
}

function New-CRevisePrompt {
    param([string]$AssistantText, [object[]]$ValidationErrors, [string]$CandidateKind)
    $errorsText = ($ValidationErrors | ForEach-Object { "- $_" }) -join [Environment]::NewLine
    if ([string]::IsNullOrWhiteSpace($errorsText)) {
        $errorsText = "- contract validation failed"
    }

    $shapeNotes = switch ($CandidateKind) {
        "graph" {
            "根对象直接包含 graphId, version, intentKind, entryStageId, stages, edges, policies, budgets；不得使用 StageGraph/Nodes/Edges/PascalCase 包装。"
        }
        "patch" {
            "根对象直接包含 targetGraphId 和 operations；operation 必须包含 operationKind，replace_stage.payload 必须是正式 stage 对象。"
        }
        "recovery" {
            "根对象直接包含 recoveryKind, actionRefs, requiresHumanGate。"
        }
        "checkpoint" {
            "根对象直接包含 operationId, sourceStageId, checkpointRef。"
        }
        "contextPolicy" {
            "根对象直接包含 maxInputTokens, allowedSourceKinds，并在需要时包含 requireEvidenceRefs, failClosed, policyId。"
        }
        default {
            "只输出符合当前 schema 的 JSON 对象。"
        }
    }

    return @"
不要调用任何工具。你上一次输出的 $CandidateKind 候选未通过本地契约校验。
只根据下面的结构化错误修正 JSON。不要解释，不要 Markdown，只输出修正后的 JSON 对象。

错误：
$errorsText

上一次输出：
$AssistantText

必须满足：
$shapeNotes
"@
}

function Get-CValidationOscillation {
    param([bool[]]$States)
    $transitions = 0
    for ($index = 1; $index -lt $States.Count; $index++) {
        if ($States[$index] -ne $States[$index - 1]) {
            $transitions += 1
        }
    }

    return [pscustomobject]@{
        observed = ($States.Count -gt 1)
        transitionCount = $transitions
        oscillationDetected = ($transitions -gt 1)
        method = "validation_state_transitions_stop_on_first_valid"
    }
}

function Get-CRejectionDistribution {
    param([object[]]$Items, [string]$PropertyName)
    $rejectionCounts = @{}
    foreach ($item in $Items) {
        $errors = @($item.PSObject.Properties[$PropertyName].Value)
        foreach ($error in $errors) {
            if ($null -eq $error) {
                continue
            }

            $key = if ([string]$error -match '^([^:]+):') { $Matches[1] } else { [string]$error }
            if (-not $rejectionCounts.ContainsKey($key)) {
                $rejectionCounts[$key] = 0
            }
            $rejectionCounts[$key] += 1
        }
    }

    return $rejectionCounts
}

function New-CSummaryItem {
    param([string]$Name, [object[]]$Items)
    $items = @($Items)
    $firstValid = @($items | Where-Object { $_.firstLocalContractValid -eq $true }).Count
    $finalValid = @($items | Where-Object { $_.finalLocalContractValid -eq $true }).Count
    $latencies = @($items | Where-Object { $null -ne $_.totalLatencyMs } | ForEach-Object { [double]$_.totalLatencyMs })
    $reviseRounds = @($items | Where-Object { $null -ne $_.reviseRounds } | ForEach-Object { [double]$_.reviseRounds })
    $modelCalls = @($items | Where-Object { $null -ne $_.modelCallCount } | ForEach-Object { [double]$_.modelCallCount })
    $tokens = @(Get-RunMetricValues -Items $items -MetricName "tokenTotal")
    $costs = @(Get-RunMetricValues -Items $items -MetricName "costAmount")
    $tokenAvailable = @($items | Where-Object { $null -ne $_.runMetrics -and [bool]$_.runMetrics.tokenUsage.available }).Count
    $costAvailable = @($items | Where-Object { $null -ne $_.runMetrics -and [bool]$_.runMetrics.estimatedCost.available }).Count
    $estimatedTokens = @($items | Where-Object { $null -ne $_.runMetrics -and [bool]$_.runMetrics.tokenUsage.estimated }).Count
    $overreach = @($items | Where-Object { $_.finalHighRiskOverreach -eq $true }).Count
    $degenerate = @($items | Where-Object { $_.finalDegenerateCoreLoop -eq $true }).Count
    $oscillation = @($items | Where-Object { $_.oscillationDetected -eq $true }).Count
    $tokenMissingReasons = @{}
    $costMissingReasons = @{}
    foreach ($item in $items) {
        if ($null -ne $item.runMetrics -and $null -ne $item.runMetrics.tokenUsage.missingReason) {
            $reason = [string]$item.runMetrics.tokenUsage.missingReason
            if (-not $tokenMissingReasons.ContainsKey($reason)) { $tokenMissingReasons[$reason] = 0 }
            $tokenMissingReasons[$reason] += 1
        }

        if ($null -ne $item.runMetrics -and $null -ne $item.runMetrics.estimatedCost.missingReason) {
            $reason = [string]$item.runMetrics.estimatedCost.missingReason
            if (-not $costMissingReasons.ContainsKey($reason)) { $costMissingReasons[$reason] = 0 }
            $costMissingReasons[$reason] += 1
        }
    }

    return [pscustomobject]@{
        name = $Name
        runCount = $items.Count
        firstLegalRate = if ($items.Count -eq 0) { 0 } else { [Math]::Round($firstValid / $items.Count, 4) }
        finalLegalRate = if ($items.Count -eq 0) { 0 } else { [Math]::Round($finalValid / $items.Count, 4) }
        averageReviseRounds = if ($reviseRounds.Count -eq 0) { $null } else { [Math]::Round((Get-Mean -Values $reviseRounds), 2) }
        maxReviseRounds = if ($reviseRounds.Count -eq 0) { $null } else { ($reviseRounds | Measure-Object -Maximum).Maximum }
        latencyMeanMs = if ($latencies.Count -eq 0) { $null } else { [Math]::Round((Get-Mean -Values $latencies), 2) }
        latencyMaxMs = if ($latencies.Count -eq 0) { $null } else { ($latencies | Measure-Object -Maximum).Maximum }
        modelCallMean = if ($modelCalls.Count -eq 0) { $null } else { [Math]::Round((Get-Mean -Values $modelCalls), 2) }
        modelCallMax = if ($modelCalls.Count -eq 0) { $null } else { ($modelCalls | Measure-Object -Maximum).Maximum }
        tokenMean = if ($tokens.Count -eq 0) { $null } else { [Math]::Round((Get-Mean -Values $tokens), 2) }
        tokenStdDev = if ($tokens.Count -eq 0) { $null } else { [Math]::Round((Get-StdDev -Values $tokens), 2) }
        tokenAvailableRate = if ($items.Count -eq 0) { 0 } else { [Math]::Round($tokenAvailable / $items.Count, 4) }
        anyEstimatedToken = ($estimatedTokens -gt 0)
        estimatedTokenRate = if ($items.Count -eq 0) { 0 } else { [Math]::Round($estimatedTokens / $items.Count, 4) }
        tokenMissingReasons = $tokenMissingReasons
        costMean = if ($costs.Count -eq 0) { $null } else { [Math]::Round((Get-Mean -Values $costs), 8) }
        costStdDev = if ($costs.Count -eq 0) { $null } else { [Math]::Round((Get-StdDev -Values $costs), 8) }
        costAvailableRate = if ($items.Count -eq 0) { 0 } else { [Math]::Round($costAvailable / $items.Count, 4) }
        costMissingReasons = $costMissingReasons
        overreachRate = if ($items.Count -eq 0) { 0 } else { [Math]::Round($overreach / $items.Count, 4) }
        degenerateCoreLoopRate = if ($items.Count -eq 0) { 0 } else { [Math]::Round($degenerate / $items.Count, 4) }
        oscillationRate = if ($items.Count -eq 0) { 0 } else { [Math]::Round($oscillation / $items.Count, 4) }
        oscillationMethod = "validation_state_transitions_stop_on_first_valid"
        firstRejectionDistribution = Get-CRejectionDistribution -Items $items -PropertyName "firstValidationErrors"
        finalRejectionDistribution = Get-CRejectionDistribution -Items $items -PropertyName "finalValidationErrors"
    }
}

function New-CSummary {
    param([object[]]$Runs)
    $runs = @($Runs)
    $overall = New-CSummaryItem -Name "overall" -Items $runs
    $byCandidateKind = @()
    foreach ($group in ($runs | Group-Object candidateKind)) {
        $byCandidateKind += New-CSummaryItem -Name $group.Name -Items @($group.Group)
    }

    $byTask = @()
    foreach ($group in ($runs | Group-Object taskId)) {
        $taskSummary = New-CSummaryItem -Name $group.Name -Items @($group.Group)
        $taskSummary | Add-Member -NotePropertyName candidateKind -NotePropertyValue (@($group.Group) | Select-Object -First 1).candidateKind -Force
        $byTask += $taskSummary
    }

    $finalRatePassed = ($overall.finalLegalRate -ge $CAcceptanceFinalLegalRateThreshold)
    $revisePassed = ($null -ne $overall.averageReviseRounds -and $overall.averageReviseRounds -le $CAcceptanceAverageReviseThreshold)
    $tokenAttributionPassed = ($overall.tokenAvailableRate -gt 0)
    $overreachPassed = ($overall.overreachRate -eq 0)
    $degeneratePassed = ($overall.degenerateCoreLoopRate -eq 0)
    $passed = ($finalRatePassed -and $revisePassed -and $tokenAttributionPassed -and $overreachPassed -and $degeneratePassed)

    return [pscustomobject]@{
        summarySchemaVersion = "adaptive-kernel-c-summary-v2"
        generatedAt = (Get-Date).ToString("o")
        repeatCount = $RepeatCount
        maxReviseRounds = $MaxReviseRounds
        overall = $overall
        byCandidateKind = $byCandidateKind
        byTask = $byTask
        acceptanceGate = [pscustomobject]@{
            passed = $passed
            finalLegalRateThreshold = $CAcceptanceFinalLegalRateThreshold
            finalLegalRatePassed = $finalRatePassed
            averageReviseThreshold = $CAcceptanceAverageReviseThreshold
            averageRevisePassed = $revisePassed
            tokenAttributionPassed = $tokenAttributionPassed
            tokenEstimatedAllowedForC = $true
            costRequiredForC = $false
            canStartD = ($passed -and -not [bool]$overall.anyEstimatedToken -and $overall.costAvailableRate -gt 0)
            dBlockedReason = if (-not $passed) { "c_acceptance_gate_failed" } elseif ([bool]$overall.anyEstimatedToken) { "estimated_token_not_allowed_for_d" } elseif ($overall.costAvailableRate -le 0) { "cost_missing_for_d" } else { $null }
            highRiskOverreachPassed = $overreachPassed
            degenerateCoreLoopPassed = $degeneratePassed
        }
    }
}

function Invoke-C {
    param([string]$Cli, [string]$Root)
    $schemas = New-CSchemaFiles -Root $Root
    $tasks = Get-CTasks -Schemas $schemas
    if ($CTaskIds.Count -gt 0) {
        $allowed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($taskId in $CTaskIds) {
            foreach ($taskIdPart in ([string]$taskId -split ",")) {
                if (-not [string]::IsNullOrWhiteSpace($taskIdPart)) {
                    [void]$allowed.Add($taskIdPart.Trim())
                }
            }
        }

        $tasks = @($tasks | Where-Object { $allowed.Contains($_.taskId) })
        if ($tasks.Count -eq 0) {
            throw "No C tasks matched -CTaskIds: $($CTaskIds -join ', ')"
        }
    }

    $cRoot = Join-Path $Root "c"
    New-Item -ItemType Directory -Force -Path $cRoot | Out-Null
    Write-Json (Join-Path $cRoot "task-manifest.json") $tasks
    $runs = @()

    foreach ($task in $tasks) {
        $taskRoot = Join-Path $cRoot $task.taskId
        New-Item -ItemType Directory -Force -Path $taskRoot | Out-Null
        for ($i = 1; $i -le $RepeatCount; $i++) {
            $outFile = Join-Path $taskRoot ("run-{0:D2}.jsonl" -f $i)
            $arguments = @("exec", "--json", "--skip-git-repo-check", "-m", $Model, "--output-schema", $task.schema, $task.prompt)
            if ($DryRun) {
                $dryRunRecord = [pscustomobject]@{
                    taskId = $task.taskId
                    candidateKind = $task.candidateKind
                    runIndex = $i
                    dryRun = $true
                    command = @($Cli) + $arguments
                    outputFile = $outFile
                    schema = $task.schema
                }
                $dryRunRecord | Add-Member -NotePropertyName runMetrics -NotePropertyValue (New-RunMetrics -Phase "c" -TaskId $task.taskId -Group $task.candidateKind -RunIndex $i -AttemptIndex 1 -ReviseRound $null -GraphId "c.candidate" -StageId $null -StepId $null -Result $null -ModelCallCount 1 -OutputFile $outFile)
                $runs += $dryRunRecord
                continue
            }

            $firstResult = Invoke-TianShuJsonl -Cli $Cli -Arguments $arguments -OutFile $outFile -TimeoutSeconds $CommandTimeoutSeconds
            $firstValidation = Test-CCandidate -AssistantText $firstResult.assistantText -CandidateKind $task.candidateKind
            $firstRunMetrics = New-RunMetrics -Phase "c" -TaskId $task.taskId -Group $task.candidateKind -RunIndex $i -AttemptIndex 1 -ReviseRound $null -GraphId "c.candidate" -StageId $null -StepId $null -Result $firstResult -ModelCallCount 1 -OutputFile $outFile
            $firstRunMetrics.success = [bool]$firstValidation.contractValid
            $finalResult = $firstResult
            $finalValidation = $firstValidation
            $finalRunMetrics = $firstRunMetrics
            $reviseAttempts = @()
            $validationStates = @([bool]$firstValidation.contractValid)
            $totalLatencyMs = [int]$firstResult.latencyMs
            $modelCallCount = 1

            for ($revise = 1; $revise -le $MaxReviseRounds -and -not $finalValidation.contractValid; $revise++) {
                $revisePrompt = New-CRevisePrompt -AssistantText $finalResult.assistantText -ValidationErrors $finalValidation.validationErrors -CandidateKind $task.candidateKind
                $reviseOutFile = Join-Path $taskRoot ("run-{0:D2}-revise-{1}.jsonl" -f $i, $revise)
                $reviseArguments = @("exec", "--json", "--skip-git-repo-check", "-m", $Model, "--output-schema", $task.schema, $revisePrompt)
                $reviseResult = Invoke-TianShuJsonl -Cli $Cli -Arguments $reviseArguments -OutFile $reviseOutFile -TimeoutSeconds $CommandTimeoutSeconds
                $reviseValidation = Test-CCandidate -AssistantText $reviseResult.assistantText -CandidateKind $task.candidateKind
                $reviseRunMetrics = New-RunMetrics -Phase "c" -TaskId $task.taskId -Group $task.candidateKind -RunIndex $i -AttemptIndex ($revise + 1) -ReviseRound $revise -GraphId "c.candidate" -StageId $null -StepId $null -Result $reviseResult -ModelCallCount 1 -OutputFile $reviseOutFile
                $reviseRunMetrics.success = [bool]$reviseValidation.contractValid
                $totalLatencyMs += [int]$reviseResult.latencyMs
                $modelCallCount += 1
                $validationStates += [bool]$reviseValidation.contractValid
                $reviseAttempts += [pscustomobject]@{
                    reviseRound = $revise
                    outputFile = $reviseOutFile
                    success = $reviseResult.success
                    timedOut = $reviseResult.timedOut
                    latencyMs = $reviseResult.latencyMs
                    localJsonParsed = $reviseValidation.jsonParsed
                    localContractValid = $reviseValidation.contractValid
                    localValidationErrors = $reviseValidation.validationErrors
                    highRiskOverreach = $reviseValidation.highRiskOverreach
                    degenerateCoreLoop = $reviseValidation.degenerateCoreLoop
                    riskSignals = $reviseValidation.riskSignals
                    runMetrics = $reviseRunMetrics
                }
                $finalResult = $reviseResult
                $finalValidation = $reviseValidation
                $finalRunMetrics = $reviseRunMetrics
            }

            $finalRunMetrics.modelCallCount = $modelCallCount
            $oscillation = Get-CValidationOscillation -States $validationStates
            $runs += [pscustomobject]@{
                taskId = $task.taskId
                candidateKind = $task.candidateKind
                runIndex = $i
                schema = $task.schema
                firstOutputFile = $outFile
                firstSuccess = $firstResult.success
                firstTimedOut = $firstResult.timedOut
                firstLatencyMs = $firstResult.latencyMs
                firstLocalJsonParsed = $firstValidation.jsonParsed
                firstLocalContractValid = $firstValidation.contractValid
                firstValidationErrors = $firstValidation.validationErrors
                firstHighRiskOverreach = $firstValidation.highRiskOverreach
                firstDegenerateCoreLoop = $firstValidation.degenerateCoreLoop
                firstRiskSignals = $firstValidation.riskSignals
                firstRunMetrics = $firstRunMetrics
                finalOutputFile = $finalResult.outputFile
                finalSuccess = $finalResult.success
                finalTimedOut = $finalResult.timedOut
                finalLocalJsonParsed = $finalValidation.jsonParsed
                finalLocalContractValid = $finalValidation.contractValid
                finalValidationErrors = $finalValidation.validationErrors
                finalHighRiskOverreach = $finalValidation.highRiskOverreach
                finalDegenerateCoreLoop = $finalValidation.degenerateCoreLoop
                finalRiskSignals = $finalValidation.riskSignals
                stageCount = $finalValidation.stageCount
                edgeCount = $finalValidation.edgeCount
                reviseRounds = $reviseAttempts.Count
                modelCallCount = $modelCallCount
                totalLatencyMs = $totalLatencyMs
                oscillationObserved = $oscillation.observed
                oscillationDetected = $oscillation.oscillationDetected
                oscillationTransitionCount = $oscillation.transitionCount
                oscillationMethod = $oscillation.method
                reviseAttempts = $reviseAttempts
                runMetrics = $finalRunMetrics
            }
        }
    }

    Write-Json (Join-Path $cRoot "runs.json") $runs
    if (-not $DryRun) {
        $summary = New-CSummary -Runs $runs
        Write-Json (Join-Path $cRoot "summary.json") $summary
    }
}

function Get-CProfileDefinitions {
    param([object]$Schemas)
    return @(
        [pscustomobject]@{
            profile = "schema-enforced"
            schemaMode = "output-schema"
            schemaProfile = "current-c-contract"
            passOutputSchema = $true
            strictContract = $false
            promptMode = "shared-contract"
        },
        [pscustomobject]@{
            profile = "schema-hinted-no-output-schema"
            schemaMode = "prompt-hinted"
            schemaProfile = "no-output-schema"
            passOutputSchema = $false
            strictContract = $false
            promptMode = "shared-contract-with-field-shape-hints"
        },
        [pscustomobject]@{
            profile = "schema-full-strict"
            schemaMode = "output-schema"
            schemaProfile = "current-c-contract-strict"
            passOutputSchema = $true
            strictContract = $true
            promptMode = "shared-contract"
        }
    )
}

function New-CProfilePrompt {
    param([object]$Task, [object]$Profile)
    if ($Profile.profile -eq "schema-hinted-no-output-schema") {
        return @"
$($Task.prompt)

Profile: schema-hinted-no-output-schema。
只输出一个完整 JSON 对象，或一个 fenced json code block 中的完整 JSON 对象。
不得输出多个 JSON 对象；不得输出解释；不得使用自然语言包裹。
字段名、大小写、数组/对象层级必须与任务要求完全一致；不要依赖调用方自动补字段、改字段名、改枚举或补默认 budget。
该 profile 不传 --output-schema，因此你必须按 prompt contract 自行生成合法候选。
"@
    }

    return $Task.prompt
}

function New-CProfileArguments {
    param([string]$ProfileName, [bool]$PassOutputSchema, [string]$SchemaPath, [string]$Prompt)
    $arguments = @("exec", "--json", "--skip-git-repo-check", "-m", $Model)
    if ($PassOutputSchema) {
        $arguments += @("--output-schema", $SchemaPath)
    }

    $arguments += $Prompt
    return $arguments
}

function New-CProfileAcceptanceManifest {
    param([object[]]$Profiles, [object[]]$Tasks, [int]$EffectiveRepeatCount)
    $schemaRecords = @()
    foreach ($task in $Tasks) {
        $schemaRecords += [pscustomobject]@{
            taskId = $task.taskId
            candidateKind = $task.candidateKind
            schemaPath = $task.schema
            schemaSha256 = Get-FileSha256OrNull -Path $task.schema
        }
    }

    return [pscustomobject]@{
        manifestSchemaVersion = "adaptive-kernel-candidate-generation-profile-acceptance-v1"
        generatedAt = (Get-Date).ToString("o")
        model = $Model
        temperature = if ([string]::IsNullOrWhiteSpace($Temperature)) { $null } else { $Temperature }
        temperatureFixed = -not [string]::IsNullOrWhiteSpace($Temperature)
        seed = if ([string]::IsNullOrWhiteSpace($Seed)) { $null } else { $Seed }
        seedFixed = -not [string]::IsNullOrWhiteSpace($Seed)
        repeatCount = $EffectiveRepeatCount
        maxReviseRounds = $MaxReviseRounds
        promptTemplateVersion = $CProfilePromptTemplateVersion
        profiles = @($Profiles | ForEach-Object {
            [pscustomobject]@{
                profile = $_.profile
                schemaMode = $_.schemaMode
                schemaProfile = $_.schemaProfile
                passOutputSchema = [bool]$_.passOutputSchema
                promptMode = $_.promptMode
            }
        })
        schemaRecords = $schemaRecords
        taskSet = @($Tasks | ForEach-Object {
            [pscustomobject]@{
                taskId = $_.taskId
                candidateKind = $_.candidateKind
            }
        })
        jsonExtractionRules = @(
            "accept_complete_json_object",
            "accept_single_fenced_json_block",
            "reject_fragment_stitching",
            "reject_auto_fill_or_enum_fix",
            "reject_ambiguous_multiple_json_objects"
        )
        reviseRules = [pscustomobject]@{
            maxReviseRounds = $MaxReviseRounds
            feedback = "structured_validation_rejection_reason_only"
        }
    }
}

function Invoke-CProfileRun {
    param(
        [string]$Cli,
        [string]$Root,
        [object]$Task,
        [object]$Profile,
        [int]$RunIndex
    )

    $profileTaskRoot = Join-Path (Join-Path $Root $Profile.profile) $Task.taskId
    New-Item -ItemType Directory -Force -Path $profileTaskRoot | Out-Null
    $outFile = Join-Path $profileTaskRoot ("run-{0:D2}.jsonl" -f $RunIndex)
    $prompt = New-CProfilePrompt -Task $Task -Profile $Profile
    $arguments = New-CProfileArguments -ProfileName $Profile.profile -PassOutputSchema ([bool]$Profile.passOutputSchema) -SchemaPath $Task.schema -Prompt $prompt
    if ($Profile.profile -eq "schema-hinted-no-output-schema" -and ($arguments -contains "--output-schema")) {
        throw "G2 schema-hinted-no-output-schema must not pass --output-schema."
    }

    if ($DryRun) {
        $dryRunMetrics = New-RunMetrics -Phase "c-profile" -TaskId $Task.taskId -Group $Profile.profile -RunIndex $RunIndex -AttemptIndex 1 -ReviseRound $null -GraphId "c-profile.candidate" -StageId $null -StepId $null -Result $null -ModelCallCount 1 -OutputFile $outFile
        return [pscustomobject]@{
            profile = $Profile.profile
            taskId = $Task.taskId
            candidateKind = $Task.candidateKind
            runIndex = $RunIndex
            dryRun = $true
            command = @($Cli) + $arguments
            outputFile = $outFile
            schema = if ([bool]$Profile.passOutputSchema) { $Task.schema } else { $null }
            schemaSha256 = if ([bool]$Profile.passOutputSchema) { Get-FileSha256OrNull -Path $Task.schema } else { $null }
            promptTemplateVersion = $CProfilePromptTemplateVersion
            passOutputSchema = [bool]$Profile.passOutputSchema
            runMetrics = $dryRunMetrics
        }
    }

    $firstResult = Invoke-TianShuJsonl -Cli $Cli -Arguments $arguments -OutFile $outFile -TimeoutSeconds $CommandTimeoutSeconds
    $firstValidation = Test-CCandidate -AssistantText $firstResult.assistantText -CandidateKind $Task.candidateKind
    $firstRunMetrics = New-RunMetrics -Phase "c-profile" -TaskId $Task.taskId -Group $Profile.profile -RunIndex $RunIndex -AttemptIndex 1 -ReviseRound $null -GraphId "c-profile.candidate" -StageId $null -StepId $null -Result $firstResult -ModelCallCount 1 -OutputFile $outFile
    $firstRunMetrics.success = [bool]$firstValidation.contractValid
    $finalResult = $firstResult
    $finalValidation = $firstValidation
    $finalRunMetrics = $firstRunMetrics
    $reviseAttempts = @()
    $validationStates = @([bool]$firstValidation.contractValid)
    $totalLatencyMs = [int]$firstResult.latencyMs
    $modelCallCount = 1

    for ($revise = 1; $revise -le $MaxReviseRounds -and -not $finalValidation.contractValid; $revise++) {
        $revisePrompt = New-CRevisePrompt -AssistantText $finalResult.assistantText -ValidationErrors $finalValidation.validationErrors -CandidateKind $Task.candidateKind
        $reviseOutFile = Join-Path $profileTaskRoot ("run-{0:D2}-revise-{1}.jsonl" -f $RunIndex, $revise)
        $reviseArguments = New-CProfileArguments -ProfileName $Profile.profile -PassOutputSchema ([bool]$Profile.passOutputSchema) -SchemaPath $Task.schema -Prompt $revisePrompt
        $reviseResult = Invoke-TianShuJsonl -Cli $Cli -Arguments $reviseArguments -OutFile $reviseOutFile -TimeoutSeconds $CommandTimeoutSeconds
        $reviseValidation = Test-CCandidate -AssistantText $reviseResult.assistantText -CandidateKind $Task.candidateKind
        $reviseRunMetrics = New-RunMetrics -Phase "c-profile" -TaskId $Task.taskId -Group $Profile.profile -RunIndex $RunIndex -AttemptIndex ($revise + 1) -ReviseRound $revise -GraphId "c-profile.candidate" -StageId $null -StepId $null -Result $reviseResult -ModelCallCount 1 -OutputFile $reviseOutFile
        $reviseRunMetrics.success = [bool]$reviseValidation.contractValid
        $totalLatencyMs += [int]$reviseResult.latencyMs
        $modelCallCount += 1
        $validationStates += [bool]$reviseValidation.contractValid
        $reviseAttempts += [pscustomobject]@{
            reviseRound = $revise
            outputFile = $reviseOutFile
            localJsonParsed = $reviseValidation.jsonParsed
            localContractValid = $reviseValidation.contractValid
            localValidationErrors = $reviseValidation.validationErrors
            highRiskOverreach = $reviseValidation.highRiskOverreach
            degenerateCoreLoop = $reviseValidation.degenerateCoreLoop
            runMetrics = $reviseRunMetrics
        }
        $finalResult = $reviseResult
        $finalValidation = $reviseValidation
        $finalRunMetrics = $reviseRunMetrics
    }

    $finalRunMetrics.modelCallCount = $modelCallCount
    $oscillation = Get-CValidationOscillation -States $validationStates
    return [pscustomobject]@{
        profile = $Profile.profile
        taskId = $Task.taskId
        candidateKind = $Task.candidateKind
        runIndex = $RunIndex
        schema = if ([bool]$Profile.passOutputSchema) { $Task.schema } else { $null }
        schemaSha256 = if ([bool]$Profile.passOutputSchema) { Get-FileSha256OrNull -Path $Task.schema } else { $null }
        promptTemplateVersion = $CProfilePromptTemplateVersion
        passOutputSchema = [bool]$Profile.passOutputSchema
        firstOutputFile = $outFile
        firstSuccess = $firstResult.success
        firstTimedOut = $firstResult.timedOut
        firstLatencyMs = $firstResult.latencyMs
        firstLocalJsonParsed = $firstValidation.jsonParsed
        firstLocalContractValid = $firstValidation.contractValid
        firstValidationErrors = $firstValidation.validationErrors
        firstHighRiskOverreach = $firstValidation.highRiskOverreach
        firstDegenerateCoreLoop = $firstValidation.degenerateCoreLoop
        firstRiskSignals = $firstValidation.riskSignals
        firstRunMetrics = $firstRunMetrics
        finalOutputFile = $finalResult.outputFile
        finalSuccess = $finalResult.success
        finalTimedOut = $finalResult.timedOut
        finalLocalJsonParsed = $finalValidation.jsonParsed
        finalLocalContractValid = $finalValidation.contractValid
        finalValidationErrors = $finalValidation.validationErrors
        finalHighRiskOverreach = $finalValidation.highRiskOverreach
        finalDegenerateCoreLoop = $finalValidation.degenerateCoreLoop
        finalRiskSignals = $finalValidation.riskSignals
        stageCount = $finalValidation.stageCount
        edgeCount = $finalValidation.edgeCount
        reviseRounds = $reviseAttempts.Count
        modelCallCount = $modelCallCount
        totalLatencyMs = $totalLatencyMs
        oscillationObserved = $oscillation.observed
        oscillationDetected = $oscillation.oscillationDetected
        oscillationTransitionCount = $oscillation.transitionCount
        oscillationMethod = $oscillation.method
        reviseAttempts = $reviseAttempts
        runMetrics = $finalRunMetrics
    }
}

function New-CProfileComparisonSummary {
    param([object[]]$Runs, [int]$EffectiveRepeatCount)
    $runs = @($Runs)
    $byProfile = @()
    foreach ($group in ($runs | Group-Object profile)) {
        $byProfile += New-CSummaryItem -Name $group.Name -Items @($group.Group)
    }

    $byProfileAndTask = @()
    foreach ($group in ($runs | Group-Object profile, taskId)) {
        $items = @($group.Group)
        $summary = New-CSummaryItem -Name $group.Name -Items $items
        $summary | Add-Member -NotePropertyName profile -NotePropertyValue ($items | Select-Object -First 1).profile -Force
        $summary | Add-Member -NotePropertyName taskId -NotePropertyValue ($items | Select-Object -First 1).taskId -Force
        $summary | Add-Member -NotePropertyName candidateKind -NotePropertyValue ($items | Select-Object -First 1).candidateKind -Force
        $byProfileAndTask += $summary
    }

    $g1 = $byProfile | Where-Object { $_.name -eq "schema-enforced" } | Select-Object -First 1
    $g2 = $byProfile | Where-Object { $_.name -eq "schema-hinted-no-output-schema" } | Select-Object -First 1
    $g3 = $byProfile | Where-Object { $_.name -eq "schema-full-strict" } | Select-Object -First 1
    return [pscustomobject]@{
        summarySchemaVersion = "adaptive-kernel-candidate-generation-profile-summary-v1"
        generatedAt = (Get-Date).ToString("o")
        repeatCount = $EffectiveRepeatCount
        maxReviseRounds = $MaxReviseRounds
        byProfile = $byProfile
        byProfileAndTask = $byProfileAndTask
        profileComparison = [pscustomobject]@{
            g2MinusG1FinalLegalRate = if ($null -ne $g1 -and $null -ne $g2) { [Math]::Round(([double]$g2.finalLegalRate - [double]$g1.finalLegalRate), 4) } else { $null }
            g3MinusG1FinalLegalRate = if ($null -ne $g1 -and $null -ne $g3) { [Math]::Round(([double]$g3.finalLegalRate - [double]$g1.finalLegalRate), 4) } else { $null }
            g1FinalLegalRate = if ($null -ne $g1) { $g1.finalLegalRate } else { $null }
            g2FinalLegalRate = if ($null -ne $g2) { $g2.finalLegalRate } else { $null }
            g3FinalLegalRate = if ($null -ne $g3) { $g3.finalLegalRate } else { $null }
        }
    }
}

function New-CProfileVerdict {
    param([object]$Summary, [bool]$DryRunMode)
    if ($DryRunMode) {
        return [pscustomobject]@{
            verdictSchemaVersion = "adaptive-kernel-candidate-generation-profile-verdict-v1"
            generatedAt = (Get-Date).ToString("o")
            primaryVerdict = "dry-run-no-verdict"
            signals = @()
            blockingReasons = @("dry_run")
        }
    }

    $byProfile = @($Summary.byProfile)
    $g1 = $byProfile | Where-Object { $_.name -eq "schema-enforced" } | Select-Object -First 1
    $g2 = $byProfile | Where-Object { $_.name -eq "schema-hinted-no-output-schema" } | Select-Object -First 1
    $g3 = $byProfile | Where-Object { $_.name -eq "schema-full-strict" } | Select-Object -First 1
    $signals = @()
    $blockingReasons = @()
    $g1Pass = ($null -ne $g1 -and [double]$g1.finalLegalRate -ge 0.85 -and $null -ne $g1.averageReviseRounds -and [double]$g1.averageReviseRounds -le 1.5)
    $g2Weak = ($null -ne $g2 -and [double]$g2.finalLegalRate -ge 0.75 -and $null -ne $g2.averageReviseRounds -and [double]$g2.averageReviseRounds -le 2.0 -and [double]$g2.overreachRate -eq 0 -and [double]$g2.degenerateCoreLoopRate -eq 0)
    $g2Gray = ($null -ne $g2 -and [double]$g2.finalLegalRate -ge 0.65 -and [double]$g2.finalLegalRate -lt 0.75)
    $g3Pass = ($null -ne $g3 -and [double]$g3.finalLegalRate -ge 0.85 -and $null -ne $g3.averageReviseRounds -and [double]$g3.averageReviseRounds -le 1.5)

    if ($g1Pass) { $signals += "schema-enforced-pass-only" } else { $blockingReasons += "schema_enforced_profile_not_passed" }
    if ($g2Weak) { $signals += "weak-schema-candidate-signal" }
    elseif ($g2Gray) { $signals += "weak-schema-gray-zone" }
    else { $blockingReasons += "weak_schema_candidate_signal_not_proven" }
    if ($g3Pass) { $signals += "strict-contract-candidate-signal" } else { $blockingReasons += "strict_contract_candidate_signal_not_proven" }

    $primary = if ($g1Pass -and $g2Weak -and $g3Pass) {
        "candidate-generation-profile-signal"
    }
    elseif ($g1Pass -and -not $g2Weak) {
        if ($g2Gray) { "weak-schema-gray-zone" } else { "schema-enforced-pass-only" }
    }
    else {
        "candidate-generation-not-proven"
    }

    return [pscustomobject]@{
        verdictSchemaVersion = "adaptive-kernel-candidate-generation-profile-verdict-v1"
        generatedAt = (Get-Date).ToString("o")
        primaryVerdict = $primary
        signals = $signals
        blockingReasons = @($blockingReasons | Select-Object -Unique)
        g2MinusG1FinalLegalRate = $Summary.profileComparison.g2MinusG1FinalLegalRate
        strongSchemaDependencyStillSignificant = ($null -ne $Summary.profileComparison.g2MinusG1FinalLegalRate -and [double]$Summary.profileComparison.g1FinalLegalRate -ge 0.95 -and [double]$Summary.profileComparison.g2FinalLegalRate -lt 0.85)
        allowedConclusion = "candidate_generation_only_not_feasible_controlled"
    }
}

function Invoke-CProfiles {
    param([string]$Cli, [string]$Root)
    $schemas = New-CSchemaFiles -Root $Root
    $tasks = Get-CTasks -Schemas $schemas
    if ($CTaskIds.Count -gt 0) {
        $allowedTasks = ConvertTo-NameSet -Values $CTaskIds
        $tasks = @($tasks | Where-Object { $allowedTasks.Contains($_.taskId) })
        if ($tasks.Count -eq 0) {
            throw "No C profile tasks matched -CTaskIds: $($CTaskIds -join ', ')"
        }
    }

    $profiles = Get-CProfileDefinitions -Schemas $schemas
    if ($CProfiles.Count -gt 0) {
        $allowedProfiles = ConvertTo-NameSet -Values $CProfiles
        $profiles = @($profiles | Where-Object { $allowedProfiles.Contains($_.profile) })
        if ($profiles.Count -eq 0) {
            throw "No C profiles matched -CProfiles: $($CProfiles -join ', ')"
        }
    }

    $effectiveRepeatCount = if ($CProfileRepeatCount -gt 0) { $CProfileRepeatCount } else { $RepeatCount }
    $profileRoot = Join-Path $Root "c-profiles"
    New-Item -ItemType Directory -Force -Path $profileRoot | Out-Null
    $manifest = New-CProfileAcceptanceManifest -Profiles $profiles -Tasks $tasks -EffectiveRepeatCount $effectiveRepeatCount
    if (-not [string]::IsNullOrWhiteSpace($CProfileAcceptancePath)) {
        $external = Read-JsonFileOrNull -Path $CProfileAcceptancePath
        if ($null -ne $external) {
            $manifest | Add-Member -NotePropertyName externalAcceptanceManifest -NotePropertyValue $external -Force
            $manifest | Add-Member -NotePropertyName externalAcceptanceManifestPath -NotePropertyValue (Resolve-Path -LiteralPath $CProfileAcceptancePath).Path -Force
        }
    }

    Write-Json (Join-Path $profileRoot "profile-acceptance-manifest.json") $manifest
    Write-Json (Join-Path $profileRoot "task-manifest.json") $tasks

    $runs = @()
    foreach ($profile in $profiles) {
        foreach ($task in $tasks) {
            for ($i = 1; $i -le $effectiveRepeatCount; $i++) {
                $runs += Invoke-CProfileRun -Cli $Cli -Root $profileRoot -Task $task -Profile $profile -RunIndex $i
            }
        }
    }

    Write-Json (Join-Path $profileRoot "runs.json") $runs
    foreach ($profileGroup in ($runs | Group-Object profile)) {
        $profileSummary = New-CSummaryItem -Name $profileGroup.Name -Items @($profileGroup.Group)
        Write-Json (Join-Path (Join-Path $profileRoot $profileGroup.Name) "summary.json") $profileSummary
        Write-Json (Join-Path (Join-Path $profileRoot $profileGroup.Name) "runs.json") @($profileGroup.Group)
    }

    $summary = New-CProfileComparisonSummary -Runs $runs -EffectiveRepeatCount $effectiveRepeatCount
    $verdict = New-CProfileVerdict -Summary $summary -DryRunMode ([bool]$DryRun)
    Write-Json (Join-Path $profileRoot "profile-comparison-summary.json") $summary
    Write-Json (Join-Path $profileRoot "profile-verdict.json") $verdict
    return [pscustomobject]@{
        profileRoot = $profileRoot
        summary = $summary
        verdict = $verdict
    }
}

function New-DefaultEvolutionProbeMinDelta {
    return [pscustomobject]@{
        schemaVersion = "adaptive-kernel-evolution-noise-probe-min-delta-v1"
        minSuccessRateDelta = 0.05
        minQualityScoreDelta = 0.05
        maxRegressionRate = 0.05
        maxWinnerFlipRate = 0.20
        minRunsPerStrategyTask = $EvolutionProbeRepeatCount
        highRiskOverreachAllowed = 0
    }
}

function New-DefaultEvolutionProbeStrategySet {
    param([object]$ProfileVerdict)
    $includeWeak = ($null -ne $ProfileVerdict -and @($ProfileVerdict.signals) -contains "weak-schema-candidate-signal")
    $strategies = @(
        [pscustomobject]@{
            strategyId = "strategy-baseline-fixed"
            source = "fixed-baseline"
            executionSamples = @()
        },
        [pscustomobject]@{
            strategyId = "strategy-schema-enforced-candidate"
            source = "schema-enforced-profile"
            executionSamples = @()
        }
    )
    if ($includeWeak) {
        $strategies += [pscustomobject]@{
            strategyId = "strategy-weak-schema-candidate"
            source = "weak-schema-profile"
            executionSamples = @()
        }
    }

    return [pscustomobject]@{
        schemaVersion = "adaptive-kernel-evolution-noise-probe-strategy-set-v1"
        generatedAt = (Get-Date).ToString("o")
        frozen = $true
        strategies = $strategies
    }
}

function Get-CProfileRunsForProbe {
    param([string]$Root)
    $runsPath = Join-Path (Join-Path $Root "c-profiles") "runs.json"
    if (-not (Test-Path -LiteralPath $runsPath)) {
        return @()
    }

    return @(Get-Content -LiteralPath $runsPath -Raw | ConvertFrom-Json)
}

function Get-CandidateExecutionQualityScore {
    param([object]$Run)
    if ($null -eq $Run) {
        return 0.0
    }

    $score = 0.0
    if ($Run.finalLocalContractValid -eq $true) {
        $score += 0.8
    }

    if ($Run.finalHighRiskOverreach -ne $true) {
        $score += 0.1
    }

    if ($Run.finalDegenerateCoreLoop -ne $true) {
        $score += 0.1
    }

    return [Math]::Round($score, 4)
}

function New-FrozenCandidateExecutionSample {
    param(
        [string]$StrategyId,
        [string]$TaskId,
        [int]$RepeatIndex,
        [object]$SourceRun,
        [bool]$Baseline
    )

    if ($Baseline) {
        return [pscustomobject]@{
            strategyId = $StrategyId
            taskId = $TaskId
            repeatIndex = $RepeatIndex
            executionKind = "fixed_contract_baseline"
            sampleSource = "runner.fixed-baseline"
            reusedFrozenCandidate = $true
            entersProductRuntime = $false
            success = $true
            blocked = $false
            regression = $false
            highRiskOverreach = $false
            latencyMs = 0
            estimatedToken = 0
            modelCallCount = 0
            qualityScore = 1.0
            rejectionReason = $null
        }
    }

    $quality = Get-CandidateExecutionQualityScore -Run $SourceRun
    $success = ($quality -ge 1.0)
    return [pscustomobject]@{
        strategyId = $StrategyId
        taskId = $TaskId
        repeatIndex = $RepeatIndex
        executionKind = "frozen_candidate_validator_dry_run"
        sampleSource = "c-profiles/$($SourceRun.profile)/$($SourceRun.taskId)/run-$('{0:D2}' -f [int]$SourceRun.runIndex)"
        reusedFrozenCandidate = $true
        entersProductRuntime = $false
        sourceProfile = $SourceRun.profile
        sourceRunIndex = $SourceRun.runIndex
        sourceOutputFile = $SourceRun.finalOutputFile
        success = $success
        blocked = $false
        regression = (-not $success)
        highRiskOverreach = ($SourceRun.finalHighRiskOverreach -eq $true)
        degenerateCoreLoop = ($SourceRun.finalDegenerateCoreLoop -eq $true)
        latencyMs = 0
        estimatedToken = 0
        modelCallCount = 0
        qualityScore = $quality
        rejectionReason = if ($success) { $null } else { ($SourceRun.finalValidationErrors -join ";") }
    }
}

function Select-CProfileRunsForStrategy {
    param([object[]]$Runs, [string[]]$Profiles)
    $selected = @()
    foreach ($profile in $Profiles) {
        $selected += @($Runs | Where-Object { $_.profile -eq $profile })
    }

    return $selected
}

function New-FrozenCandidateStrategySet {
    param([object]$ProfileVerdict, [object[]]$ProfileRuns, [object]$DefaultStrategySet)
    $runs = @($ProfileRuns)
    if ($runs.Count -eq 0) {
        return $DefaultStrategySet
    }

    $taskIds = @($runs | Select-Object -ExpandProperty taskId -Unique | Sort-Object)
    $strategies = @()
    $baselineSamples = @()
    foreach ($taskId in $taskIds) {
        for ($i = 1; $i -le $EvolutionProbeRepeatCount; $i++) {
            $baselineSamples += New-FrozenCandidateExecutionSample -StrategyId "strategy-baseline-fixed" -TaskId $taskId -RepeatIndex $i -SourceRun $null -Baseline $true
        }
    }

    $strategies += [pscustomobject]@{
        strategyId = "strategy-baseline-fixed"
        source = "fixed-baseline"
        executionKind = "fixed_contract_baseline"
        executionSamples = $baselineSamples
    }

    $schemaRuns = @(Select-CProfileRunsForStrategy -Runs $runs -Profiles @("schema-enforced", "schema-full-strict"))
    $schemaSamples = New-FrozenCandidateStrategySamples -StrategyId "strategy-schema-enforced-candidate" -TaskIds $taskIds -SourceRuns $schemaRuns
    $strategies += [pscustomobject]@{
        strategyId = "strategy-schema-enforced-candidate"
        source = "schema-enforced-profile"
        executionKind = "frozen_candidate_validator_dry_run"
        executionSamples = $schemaSamples
    }

    $includeWeak = ($null -ne $ProfileVerdict -and @($ProfileVerdict.signals) -contains "weak-schema-candidate-signal")
    if ($includeWeak) {
        $weakRuns = @(Select-CProfileRunsForStrategy -Runs $runs -Profiles @("schema-hinted-no-output-schema"))
        $weakSamples = New-FrozenCandidateStrategySamples -StrategyId "strategy-weak-schema-candidate" -TaskIds $taskIds -SourceRuns $weakRuns
        $strategies += [pscustomobject]@{
            strategyId = "strategy-weak-schema-candidate"
            source = "weak-schema-profile"
            executionKind = "frozen_candidate_validator_dry_run"
            executionSamples = $weakSamples
        }
    }

    return [pscustomobject]@{
        schemaVersion = "adaptive-kernel-evolution-noise-probe-strategy-set-v1"
        generatedAt = (Get-Date).ToString("o")
        frozen = $true
        autoGeneratedFromProfileRuns = $true
        executionKind = "frozen_candidate_validator_dry_run"
        entersProductRuntime = $false
        strategies = $strategies
    }
}

function New-FrozenCandidateStrategySamples {
    param([string]$StrategyId, [string[]]$TaskIds, [object[]]$SourceRuns)
    $samples = @()
    foreach ($taskId in $TaskIds) {
        $taskRuns = @($SourceRuns | Where-Object { $_.taskId -eq $taskId } | Sort-Object profile, runIndex)
        if ($taskRuns.Count -eq 0) {
            for ($i = 1; $i -le $EvolutionProbeRepeatCount; $i++) {
                $samples += [pscustomobject]@{
                    strategyId = $StrategyId
                    taskId = $taskId
                    repeatIndex = $i
                    executionKind = "frozen_candidate_validator_dry_run"
                    sampleSource = "missing_profile_candidate"
                    reusedFrozenCandidate = $false
                    entersProductRuntime = $false
                    success = $false
                    blocked = $true
                    regression = $true
                    highRiskOverreach = $false
                    latencyMs = 0
                    estimatedToken = 0
                    modelCallCount = 0
                    qualityScore = 0.0
                    rejectionReason = "profile_candidate_missing"
                }
            }

            continue
        }

        for ($i = 1; $i -le $EvolutionProbeRepeatCount; $i++) {
            $sourceRun = $taskRuns[($i - 1) % $taskRuns.Count]
            $samples += New-FrozenCandidateExecutionSample -StrategyId $StrategyId -TaskId $taskId -RepeatIndex $i -SourceRun $sourceRun -Baseline $false
        }
    }

    return $samples
}

function Get-StrategyExecutionSamples {
    param([object]$Strategy)
    if ($null -eq $Strategy -or -not (Test-JsonProperty -Object $Strategy -Name "executionSamples") -or $null -eq $Strategy.executionSamples) {
        return @()
    }

    return @($Strategy.executionSamples)
}

function New-StrategyExecutionSummary {
    param([object[]]$Samples, [string]$StrategyId)
    $items = @($Samples)
    $successCount = @($items | Where-Object { $_.success -eq $true }).Count
    $blockedCount = @($items | Where-Object { $_.blocked -eq $true }).Count
    $regressionCount = @($items | Where-Object { $_.regression -eq $true }).Count
    $overreachCount = @($items | Where-Object { $_.highRiskOverreach -eq $true -or $_.overreach -eq $true }).Count
    $latencies = @($items | Where-Object { $null -ne $_.latencyMs } | ForEach-Object { [double]$_.latencyMs })
    $tokens = @($items | Where-Object { $null -ne $_.estimatedToken -or $null -ne $_.estimatedTokens } | ForEach-Object {
        if ($null -ne $_.estimatedToken) { [double]$_.estimatedToken } else { [double]$_.estimatedTokens }
    })
    $modelCalls = @($items | Where-Object { $null -ne $_.modelCallCount } | ForEach-Object { [double]$_.modelCallCount })
    $qualityScores = @($items | Where-Object { $null -ne $_.qualityScore } | ForEach-Object { [double]$_.qualityScore })
    return [pscustomobject]@{
        strategyId = $StrategyId
        executionKind = if ($items.Count -eq 0) { $null } else { ($items | Select-Object -First 1).executionKind }
        entersProductRuntime = if ($items.Count -eq 0) { $false } else { [bool](($items | Select-Object -First 1).entersProductRuntime) }
        runCount = $items.Count
        successRate = if ($items.Count -eq 0) { 0 } else { [Math]::Round($successCount / $items.Count, 4) }
        blockedRate = if ($items.Count -eq 0) { 0 } else { [Math]::Round($blockedCount / $items.Count, 4) }
        regressionRate = if ($items.Count -eq 0) { 0 } else { [Math]::Round($regressionCount / $items.Count, 4) }
        highRiskOverreachCount = $overreachCount
        latencyMeanMs = if ($latencies.Count -eq 0) { $null } else { [Math]::Round((Get-Mean -Values $latencies), 2) }
        executionEstimatedTokenMean = if ($tokens.Count -eq 0) { $null } else { [Math]::Round((Get-Mean -Values $tokens), 2) }
        executionModelCallMean = if ($modelCalls.Count -eq 0) { $null } else { [Math]::Round((Get-Mean -Values $modelCalls), 2) }
        qualityMean = if ($qualityScores.Count -eq 0) { $null } else { [Math]::Round((Get-Mean -Values $qualityScores), 4) }
    }
}

function New-EvolutionProbeCoverage {
    param([object]$StrategySet, [object]$MinDelta)
    $coverage = @()
    $allPassed = $true
    foreach ($strategy in @($StrategySet.strategies)) {
        $samples = @(Get-StrategyExecutionSamples -Strategy $strategy)
        foreach ($taskGroup in ($samples | Group-Object taskId)) {
            $passed = ($taskGroup.Count -ge [int]$MinDelta.minRunsPerStrategyTask)
            if (-not $passed) {
                $allPassed = $false
            }

            $coverage += [pscustomobject]@{
                strategyId = $strategy.strategyId
                taskId = $taskGroup.Name
                runCount = $taskGroup.Count
                minRunsPerStrategyTask = [int]$MinDelta.minRunsPerStrategyTask
                passed = $passed
            }
        }
    }

    return [pscustomobject]@{
        passed = $allPassed
        byStrategyAndTask = $coverage
    }
}

function New-EvolutionNoiseProbeComparison {
    param([object]$StrategySet, [object]$MinDelta)
    $strategySummaries = @()
    foreach ($strategy in @($StrategySet.strategies)) {
        $strategySummaries += New-StrategyExecutionSummary -Samples (Get-StrategyExecutionSamples -Strategy $strategy) -StrategyId $strategy.strategyId
    }

    $baseline = $strategySummaries | Where-Object { $_.strategyId -eq "strategy-baseline-fixed" } | Select-Object -First 1
    $candidateSummaries = @($strategySummaries | Where-Object { $_.strategyId -ne "strategy-baseline-fixed" })
    $comparisons = @()
    foreach ($candidate in $candidateSummaries) {
        $successDelta = if ($null -ne $baseline) { [Math]::Round(([double]$candidate.successRate - [double]$baseline.successRate), 4) } else { $null }
        $qualityDelta = if ($null -ne $baseline -and $null -ne $candidate.qualityMean -and $null -ne $baseline.qualityMean) { [Math]::Round(([double]$candidate.qualityMean - [double]$baseline.qualityMean), 4) } else { $null }
        $comparisons += [pscustomobject]@{
            strategyId = $candidate.strategyId
            successRateDelta = $successDelta
            qualityScoreDelta = $qualityDelta
            measurableDelta = (($null -ne $successDelta -and $successDelta -ge [double]$MinDelta.minSuccessRateDelta) -or ($null -ne $qualityDelta -and $qualityDelta -ge [double]$MinDelta.minQualityScoreDelta))
            regressionRate = $candidate.regressionRate
            highRiskOverreachCount = $candidate.highRiskOverreachCount
            candidateSuccessNotLowerThanBaseline = ($null -ne $successDelta -and $successDelta -ge 0)
        }
    }

    return [pscustomobject]@{
        strategySummaries = $strategySummaries
        comparisons = $comparisons
    }
}

function New-EvolutionNoiseProbeVerdict {
    param([object]$ProfileVerdict, [object]$Comparison, [object]$MinDelta, [bool]$StrategySamplesAvailable, [object]$Coverage)
    $profileSignals = if ($null -ne $ProfileVerdict) { @($ProfileVerdict.signals) } else { @() }
    $profilePrimary = if ($null -ne $ProfileVerdict) { [string]$ProfileVerdict.primaryVerdict } else { "missing" }
    $profileAllowsProbe = ($profileSignals -contains "weak-schema-candidate-signal" -or $profilePrimary -eq "candidate-generation-profile-signal")
    if (-not $profileAllowsProbe) {
        return [pscustomobject]@{
            verdictSchemaVersion = "adaptive-kernel-evolution-noise-probe-verdict-v1"
            generatedAt = (Get-Date).ToString("o")
            primaryVerdict = "evolution-probe-not-run"
            blockingReasons = @("candidate_generation_profile_precondition_not_met")
            measurableDelta = $false
            effectSizeSummary = @()
            winnerFlipRate = $null
            allowedConclusion = "do_not_start_d"
        }
    }

    if (-not $StrategySamplesAvailable) {
        return [pscustomobject]@{
            verdictSchemaVersion = "adaptive-kernel-evolution-noise-probe-verdict-v1"
            generatedAt = (Get-Date).ToString("o")
            primaryVerdict = "evolution-probe-not-run"
            blockingReasons = @("strategy_execution_samples_missing")
            measurableDelta = $false
            effectSizeSummary = @()
            winnerFlipRate = $null
            allowedConclusion = "do_not_start_d"
        }
    }

    if ($null -ne $Coverage -and $Coverage.passed -ne $true) {
        return [pscustomobject]@{
            verdictSchemaVersion = "adaptive-kernel-evolution-noise-probe-verdict-v1"
            generatedAt = (Get-Date).ToString("o")
            primaryVerdict = "evolution-probe-not-run"
            blockingReasons = @("strategy_execution_sample_coverage_below_minimum")
            measurableDelta = $false
            effectSizeSummary = @()
            winnerFlipRate = $null
            coverage = $Coverage
            allowedConclusion = "do_not_start_d"
        }
    }

    $comparisons = @($Comparison.comparisons)
    $riskFailures = @($comparisons | Where-Object { [int]$_.highRiskOverreachCount -gt [int]$MinDelta.highRiskOverreachAllowed -or [double]$_.regressionRate -gt [double]$MinDelta.maxRegressionRate })
    if ($riskFailures.Count -gt 0) {
        return [pscustomobject]@{
            verdictSchemaVersion = "adaptive-kernel-evolution-noise-probe-verdict-v1"
            generatedAt = (Get-Date).ToString("o")
            primaryVerdict = "evolution-risk-too-high"
            blockingReasons = @("risk_or_regression_threshold_failed")
            measurableDelta = $true
            effectSizeSummary = $comparisons
            winnerFlipRate = 0
            allowedConclusion = "do_not_start_d"
        }
    }

    $measurable = @($comparisons | Where-Object { $_.measurableDelta -eq $true })
    if ($measurable.Count -eq 0) {
        return [pscustomobject]@{
            verdictSchemaVersion = "adaptive-kernel-evolution-noise-probe-verdict-v1"
            generatedAt = (Get-Date).ToString("o")
            primaryVerdict = "evolution-no-measurable-delta"
            blockingReasons = @("minimum_meaningful_delta_not_met")
            measurableDelta = $false
            effectSizeSummary = $comparisons
            winnerFlipRate = 0
            allowedConclusion = "do_not_start_d"
        }
    }

    $validWinners = @($measurable | Where-Object { $_.candidateSuccessNotLowerThanBaseline -eq $true })
    $primary = if ($validWinners.Count -gt 0) { "evolution-selection-signal" } else { "evolution-noise-too-high" }
    return [pscustomobject]@{
        verdictSchemaVersion = "adaptive-kernel-evolution-noise-probe-verdict-v1"
        generatedAt = (Get-Date).ToString("o")
        primaryVerdict = $primary
        blockingReasons = if ($primary -eq "evolution-selection-signal") { @() } else { @("candidate_success_lower_than_baseline") }
        measurableDelta = ($validWinners.Count -gt 0)
        effectSizeSummary = $comparisons
        winnerFlipRate = 0
        winnerFlipCalculation = "not_applicable_without_per_task_winner_series"
        allowedConclusion = if ($primary -eq "evolution-selection-signal") { "selection_signal_without_promotion" } else { "do_not_start_d" }
    }
}

function Invoke-EvolutionNoiseProbe {
    param([string]$Root)
    $probeRoot = Join-Path $Root "evolution-noise-probe"
    New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
    $profileVerdictPath = Join-Path (Join-Path $Root "c-profiles") "profile-verdict.json"

    $profileVerdict = Read-JsonFileOrNull -Path $profileVerdictPath
    $minDelta = Copy-OptionalJsonEvidence -SourcePath $EvolutionProbeMinDeltaPath -DestinationPath (Join-Path $probeRoot "evolution-noise-probe-min-delta.json") -DefaultValue (New-DefaultEvolutionProbeMinDelta)
    $strategySetDefault = New-DefaultEvolutionProbeStrategySet -ProfileVerdict $profileVerdict
    $strategySet = Copy-OptionalJsonEvidence -SourcePath $EvolutionProbeStrategySetPath -DestinationPath (Join-Path $probeRoot "evolution-noise-probe-strategy-set.json") -DefaultValue $strategySetDefault
    $samples = @()
    foreach ($strategy in @($strategySet.strategies)) {
        $samples += Get-StrategyExecutionSamples -Strategy $strategy
    }

    if ($samples.Count -eq 0 -and [string]::IsNullOrWhiteSpace($EvolutionProbeStrategySetPath)) {
        $profileRuns = Get-CProfileRunsForProbe -Root $Root
        $strategySet = New-FrozenCandidateStrategySet -ProfileVerdict $profileVerdict -ProfileRuns $profileRuns -DefaultStrategySet $strategySetDefault
        Write-Json (Join-Path $probeRoot "evolution-noise-probe-strategy-set.json") $strategySet
        $samples = @()
        foreach ($strategy in @($strategySet.strategies)) {
            $samples += Get-StrategyExecutionSamples -Strategy $strategy
        }
    }

    $strategySamplesAvailable = ($samples.Count -gt 0)
    $comparison = New-EvolutionNoiseProbeComparison -StrategySet $strategySet -MinDelta $minDelta
    $coverage = New-EvolutionProbeCoverage -StrategySet $strategySet -MinDelta $minDelta
    $summary = [pscustomobject]@{
        summarySchemaVersion = "adaptive-kernel-evolution-noise-probe-summary-v1"
        generatedAt = (Get-Date).ToString("o")
        profileVerdictPath = $profileVerdictPath
        probeRepeatCount = $EvolutionProbeRepeatCount
        candidateGenerationDiagnostics = [pscustomobject]@{
            source = "c-profile-summary"
            profileVerdict = if ($null -ne $profileVerdict) { $profileVerdict.primaryVerdict } else { "missing" }
            signals = if ($null -ne $profileVerdict) { $profileVerdict.signals } else { @() }
        }
        strategyExecutionComparison = $comparison
        coverage = $coverage
        executionEvidenceBoundary = [pscustomobject]@{
            executionKind = if ($null -ne $strategySet.executionKind) { $strategySet.executionKind } else { "external_strategy_set" }
            entersProductRuntime = if ($null -ne $strategySet.entersProductRuntime) { [bool]$strategySet.entersProductRuntime } else { $false }
            countsAsRuntimeLiveExecution = $false
        }
        samplesAvailable = $strategySamplesAvailable
    }
    $verdict = New-EvolutionNoiseProbeVerdict -ProfileVerdict $profileVerdict -Comparison $comparison -MinDelta $minDelta -StrategySamplesAvailable $strategySamplesAvailable -Coverage $coverage
    Write-Json (Join-Path $probeRoot "evolution-noise-probe-summary.json") $summary
    Write-Json (Join-Path $probeRoot "evolution-noise-probe-verdict.json") $verdict
    return [pscustomobject]@{
        probeRoot = $probeRoot
        summary = $summary
        verdict = $verdict
    }
}

function New-DRequirement {
    param([string]$Name, [bool]$Passed, [string]$Reason, [string]$EvidencePath)
    return [pscustomobject]@{
        name = $Name
        passed = $Passed
        reason = if ($Passed) { $null } else { $Reason }
        evidencePath = $EvidencePath
    }
}

function Invoke-D {
    param([string]$Root)
    $dRoot = Join-Path $Root "d"
    New-Item -ItemType Directory -Force -Path $dRoot | Out-Null

    $cSummaryPath = if ([string]::IsNullOrWhiteSpace($CResultsPath)) {
        Join-Path (Join-Path $Root "c") "summary.json"
    }
    else {
        (Resolve-Path -LiteralPath $CResultsPath).Path
    }

    $cSummary = $null
    $cSummaryAvailable = $false
    if (Test-Path -LiteralPath $cSummaryPath) {
        try {
            $cSummary = Get-Content -LiteralPath $cSummaryPath -Raw | ConvertFrom-Json
            $cSummaryAvailable = $true
        }
        catch {
            $cSummaryAvailable = $false
        }
    }

    $strategyGate = Read-StrategyGate -Path $StrategyGatePath
    $requirements = @()
    $p6Passed = ($cSummaryAvailable -and $null -ne $cSummary.acceptanceGate -and [bool]$cSummary.acceptanceGate.passed)
    $requirements += New-DRequirement -Name "p6_c_full_matrix_passed" -Passed $p6Passed -Reason "c_full_matrix_not_passed_or_missing" -EvidencePath $cSummaryPath

    $tokenReal = ($cSummaryAvailable -and $null -ne $cSummary.overall -and
        [double]$cSummary.overall.tokenAvailableRate -gt 0 -and
        -not [bool]$cSummary.overall.anyEstimatedToken)
    $requirements += New-DRequirement -Name "token_usage_real_not_estimated" -Passed $tokenReal -Reason "real_provider_token_usage_missing_or_estimated" -EvidencePath $cSummaryPath

    $costAvailable = ($cSummaryAvailable -and $null -ne $cSummary.overall -and
        [double]$cSummary.overall.costAvailableRate -gt 0 -and
        $null -ne $script:PriceModel -and [bool]$script:PriceModel.available)
    $requirements += New-DRequirement -Name "price_model_and_cost_available" -Passed $costAvailable -Reason "price_model_or_cost_missing" -EvidencePath $cSummaryPath

    $effectSizeGate = Test-StrategyGateFlag -StrategyGate $strategyGate -Names @("effectSize", "effectSizeGate", "effectSizePreRegistered")
    $promotionGate = Test-StrategyGateFlag -StrategyGate $strategyGate -Names @("promotionGate", "promotion")
    $rollbackGate = Test-StrategyGateFlag -StrategyGate $strategyGate -Names @("rollbackGate", "rollback")
    $humanGate = Test-StrategyGateFlag -StrategyGate $strategyGate -Names @("humanGate", "humanApprovalGate", "human")
    $requirements += New-DRequirement -Name "effect_size_gate_pre_registered" -Passed $effectSizeGate -Reason "effect_size_gate_missing" -EvidencePath $strategyGate.sourcePath
    $requirements += New-DRequirement -Name "promotion_gate_pre_registered" -Passed $promotionGate -Reason "promotion_gate_missing" -EvidencePath $strategyGate.sourcePath
    $requirements += New-DRequirement -Name "rollback_gate_pre_registered" -Passed $rollbackGate -Reason "rollback_gate_missing" -EvidencePath $strategyGate.sourcePath
    $requirements += New-DRequirement -Name "human_gate_pre_registered" -Passed $humanGate -Reason "human_gate_missing" -EvidencePath $strategyGate.sourcePath

    $allPrerequisitesPassed = (@($requirements | Where-Object { $_.passed -ne $true }).Count -eq 0)
    $blockingReasons = @($requirements | Where-Object { $_.passed -ne $true } | ForEach-Object { $_.reason } | Select-Object -Unique)
    if ($allPrerequisitesPassed) {
        $blockingReasons += "strategy_trial_engine_not_implemented_in_gap_runner"
    }

    $result = [pscustomobject]@{
        phase = "d_strategy_evolution"
        generatedAt = (Get-Date).ToString("o")
        status = "blocked"
        blocked = $true
        blockingReasons = $blockingReasons
        cSummaryPath = $cSummaryPath
        strategyGate = [pscustomobject]@{
            available = [bool]$strategyGate.available
            missingReason = $strategyGate.missingReason
            sourcePath = $strategyGate.sourcePath
        }
        requirements = $requirements
        decision = [pscustomobject]@{
            automaticPromotion = $false
            promotion = "blocked"
            rollback = "not_started"
            reason = if ($blockingReasons.Count -eq 0) { "blocked" } else { ($blockingReasons -join ";") }
        }
        failClosed = $true
        note = "D 只能在 P6 通过、真实 token/cost/price model 和 human/rollback gate 全部齐备后启动；当前 runner 不在证据不足时执行 promotion。"
    }

    Write-Json (Join-Path $dRoot "gate.json") $result
    return $result
}

function Invoke-P8 {
    param([string]$Root)
    $p8Root = Join-Path $Root "p8-a2"
    New-Item -ItemType Directory -Force -Path $p8Root | Out-Null

    $items = @(
        [pscustomobject]@{
            capability = "steer"
            status = "trace-only"
            reason = "HostInteractionStep 合同和映射测试存在，但当前 adaptive runtime runner 没有 live late-input 注入与执行证据。"
            evidenceRefs = @(
                "tests/TianShu.Kernel.Tests/AdaptiveKernelFeasibilityAcceptanceTests.cs",
                "src/Contracts/TianShu.Contracts.Execution/Models/RuntimeStepModels.cs"
            )
            countedAsAdaptiveLivePass = $false
        },
        [pscustomobject]@{
            capability = "interrupt"
            status = "blocked-missing-runtime"
            reason = "当前 adaptive loop 有 checkpoint/recovery 状态机证据，但没有 live interrupt 暂停 runtime plan 并固化 checkpoint 的验收入口。"
            evidenceRefs = @(
                "tests/TianShu.Execution.Runtime.Tests/KernelRuntimeExecutionLoopTests.cs",
                "src/Core/TianShu.RuntimeComposition/AdaptiveRuntimeExecutionLoop.cs"
            )
            countedAsAdaptiveLivePass = $false
        },
        [pscustomobject]@{
            capability = "resume"
            status = "blocked-missing-runtime"
            reason = "HostGateway/ControlPlane 存在 resume 操作边界，但当前 adaptive runtime runner 没有从 checkpoint 或 pending interactive state 继续的 live 证据。"
            evidenceRefs = @(
                "src/Core/TianShu.HostGateway/ITianShuHostGateway.cs",
                "src/Core/TianShu.ControlPlane.Abstractions/Conversations/IConversationControlPlane.cs"
            )
            countedAsAdaptiveLivePass = $false
        },
        [pscustomobject]@{
            capability = "subagent"
            status = "blocked-unsafe"
            reason = "未证明 spawn/wait/result aggregation 进入 adaptive runtime loop；必须对照既有 baseline-task-06 的 a2_delta_subagent_unavailable，不能计入通过。"
            evidenceRefs = @(
                "docs/audit/evidence/adaptive-kernel-second-acceptance/2026-06-11-s6-final-report/results.md",
                "docs/audit/tianshu-adaptive-kernel-gap-closure-plan.md"
            )
            baselineDelta = "baseline-task-06: 5/5 a2_delta_subagent_unavailable"
            countedAsAdaptiveLivePass = $false
        }
    )

    $result = [pscustomobject]@{
        phase = "p8_a2_steer_interrupt_resume_subagent"
        generatedAt = (Get-Date).ToString("o")
        statuses = $items
        allAdaptiveLivePassed = (@($items | Where-Object { $_.countedAsAdaptiveLivePass -ne $true }).Count -eq 0)
        conclusion = "完整 agent loop 不可升级为 live-pass；A2 仍需产品路径迁移或独立 module capability 降级方案。"
        allowedFinalConclusion = "feasible-with-limits"
    }

    Write-Json (Join-Path $p8Root "results.json") $result
    return $result
}

function Invoke-Baseline {
    param([string]$Cli, [string]$Root)
    $tasks = Get-BaselineTasks
    if ($BaselineTaskIds.Count -gt 0) {
        $allowed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($taskId in $BaselineTaskIds) {
            if (-not [string]::IsNullOrWhiteSpace($taskId)) {
                [void]$allowed.Add($taskId.Trim())
            }
        }

        $tasks = @($tasks | Where-Object { $allowed.Contains($_.taskId) })
        if ($tasks.Count -eq 0) {
            throw "No baseline tasks matched -BaselineTaskIds: $($BaselineTaskIds -join ', ')"
        }
    }
    $runs = @()
    $baselineRoot = Join-Path $Root "baseline"
    New-Item -ItemType Directory -Force -Path $baselineRoot | Out-Null

    foreach ($task in $tasks) {
        $taskRoot = Join-Path $baselineRoot $task.taskId
        New-Item -ItemType Directory -Force -Path $taskRoot | Out-Null
        for ($i = 1; $i -le $RepeatCount; $i++) {
            $outFile = Join-Path $taskRoot ("run-{0:D2}.jsonl" -f $i)
            $scriptPath = Join-Path $taskRoot ("run-{0:D2}.chat-script.txt" -f $i)
            $artifactsRoot = Join-Path $taskRoot ("run-{0:D2}.artifacts" -f $i)
            if ($task.kind -eq "steer") {
                $scriptLines = @(
                    $task.prompt,
                    "/wait-next-tool-call 120",
                    "/follow-up steer 请立即收敛为一条 docs 目录观察，并输出 steer_applied=true。",
                    "/wait-complete 180",
                    "/exit"
                )
                $arguments = @("chat", "--script", $scriptPath, "--protocol", "jsonl", "--artifacts", $artifactsRoot, "--approve-all", "--model", $Model)
            }
            elseif ($task.kind -eq "interrupt-resume") {
                $scriptLines = @(
                    $task.prompt,
                    "/wait-next-tool-call 120",
                    "/interrupt",
                    "/wait-complete 45",
                    "中断后改向：请只输出当前已经确认的 src/docs 顶层事实，不要继续原分析任务。",
                    "/wait-complete 180",
                    "/exit"
                )
                $arguments = @("chat", "--script", $scriptPath, "--protocol", "jsonl", "--artifacts", $artifactsRoot, "--approve-all", "--model", $Model)
            }
            elseif ($task.kind -eq "single-tool") {
                $scriptLines = @(
                    $task.prompt,
                    "/wait-next-tool-call 120",
                    "/wait-complete 180",
                    "/exit"
                )
                $arguments = @("chat", "--script", $scriptPath, "--protocol", "jsonl", "--artifacts", $artifactsRoot, "--approve-all", "--model", $Model)
            }
            elseif ($task.kind -eq "multi-tool") {
                $scriptLines = @(
                    $task.prompt,
                    "/wait-next-tool-call 120",
                    "/wait-next-tool-call 120",
                    "/wait-complete 180",
                    "/exit"
                )
                $arguments = @("chat", "--script", $scriptPath, "--protocol", "jsonl", "--artifacts", $artifactsRoot, "--approve-all", "--model", $Model)
            }
            else {
                $scriptLines = $null
                $arguments = @("exec", "--json", "--skip-git-repo-check", "-m", $Model, $task.prompt)
            }
            if ($DryRun) {
                if ($null -ne $scriptLines) {
                    Set-Content -LiteralPath $scriptPath -Value ($scriptLines -join [Environment]::NewLine) -Encoding UTF8
                }
                $dryRunRecord = [pscustomobject]@{ taskId = $task.taskId; runIndex = $i; dryRun = $true; command = @($Cli) + $arguments; outputFile = $outFile; scriptPath = $(if ($null -ne $scriptLines) { $scriptPath } else { $null }); artifactsRoot = $(if ($null -ne $scriptLines) { $artifactsRoot } else { $null }) }
                $dryRunRecord | Add-Member -NotePropertyName runMetrics -NotePropertyValue (New-RunMetrics -Phase "baseline" -TaskId $task.taskId -Group $null -RunIndex $i -AttemptIndex 1 -ReviseRound $null -GraphId "baseline.hybrid-kernel-shell.fixed" -StageId $null -StepId $null -Result $null -ModelCallCount 1 -OutputFile $outFile)
                $runs += $dryRunRecord
                continue
            }

            if ($null -ne $scriptLines) {
                $result = Invoke-TianShuChatScript -Cli $Cli -ScriptLines $scriptLines -ScriptPath $scriptPath -ArtifactsRoot $artifactsRoot -OutFile $outFile -TimeoutSeconds $CommandTimeoutSeconds
            }
            else {
                $result = Invoke-TianShuJsonl -Cli $Cli -Arguments $arguments -OutFile $outFile -TimeoutSeconds $CommandTimeoutSeconds
            }
            $assessment = Test-BaselineRun -Task $task -Result $result
            $result | Add-Member -NotePropertyName taskId -NotePropertyValue $task.taskId
            $result | Add-Member -NotePropertyName runIndex -NotePropertyValue $i
            $result | Add-Member -NotePropertyName kind -NotePropertyValue $task.kind
            $result | Add-Member -NotePropertyName baselineTaskPassed -NotePropertyValue $assessment.passed
            $result | Add-Member -NotePropertyName baselineTaskStatus -NotePropertyValue $assessment.status
            $result | Add-Member -NotePropertyName baselineEvidenceSignals -NotePropertyValue $assessment.evidenceSignals
            $result | Add-Member -NotePropertyName outputTextLength -NotePropertyValue $assessment.textLength
            $result | Add-Member -NotePropertyName toolSignalCount -NotePropertyValue $assessment.toolSignalCount
            $result | Add-Member -NotePropertyName modelCallCount -NotePropertyValue 1
            $baselineRunMetrics = New-RunMetrics -Phase "baseline" -TaskId $task.taskId -Group $null -RunIndex $i -AttemptIndex 1 -ReviseRound $null -GraphId "baseline.hybrid-kernel-shell.fixed" -StageId $null -StepId $null -Result $result -ModelCallCount 1 -OutputFile $outFile
            $baselineRunMetrics.success = [bool]$assessment.passed
            $result | Add-Member -NotePropertyName runMetrics -NotePropertyValue $baselineRunMetrics
            $runs += $result
        }
    }

    Write-Json (Join-Path $baselineRoot "runs.json") $runs
    if (-not $DryRun) {
        Write-Json (Join-Path $baselineRoot "variance-summary.json") (New-BaselineVarianceSummary -Runs $runs)
    }
}

function Invoke-C0 {
    param([string]$Cli, [string]$Root)
    $schemas = New-C0SchemaFiles -Root $Root
    $c0Root = Join-Path $Root "c0"
    New-Item -ItemType Directory -Force -Path $c0Root | Out-Null
    $taskPrompt = "不要调用任何工具。直接输出一个符合 schema 的 JSON 对象，不要 Markdown，不要解释。内容：TianShu StageGraph，Turn，两个 stage，stage-1 负责 model.invoke.initial，stage-2 负责 diagnostics.emit_trace，一条 Success edge 从 stage-1 到 stage-2，预算均为正数，requiresHumanGate=false。"
    $calibratedPrompt = @'
不要调用任何工具。只输出一个 JSON 对象，不要 Markdown，不要解释。
根对象必须直接包含这些字段：graphId, version, intentKind, entryStageId, stages, edges, policies, budgets。
不要使用 StageGraph、Turn、Nodes、Edges、Id、From、To、Kind、Capability、TokenLimit、TimeLimitMs 这些包装或 PascalCase 字段。
必须使用下面的字段名和形状：
{
  "graphId": "graph-c0-001",
  "version": "1",
  "intentKind": "Turn",
  "entryStageId": "stage-1",
  "stages": [
    {
      "stageId": "stage-1",
      "kind": "model.invoke.initial",
      "objective": "invoke the initial model step",
      "capabilityToolIds": ["model.invoke.initial"],
      "sideEffectLevel": "None",
      "budget": { "tokenBudget": 1000, "timeBudgetMs": 30000, "toolCallBudget": 0 }
    },
    {
      "stageId": "stage-2",
      "kind": "diagnostics.emit_trace",
      "objective": "emit the diagnostic trace",
      "capabilityToolIds": ["diagnostics.emit_trace"],
      "sideEffectLevel": "None",
      "budget": { "tokenBudget": 500, "timeBudgetMs": 10000, "toolCallBudget": 0 }
    }
  ],
  "edges": [
    { "fromStageId": "stage-1", "toStageId": "stage-2", "transitionKind": "Success" }
  ],
  "policies": {
    "allowedCapabilityToolIds": ["model.invoke.initial", "diagnostics.emit_trace"],
    "maxSideEffectLevel": "None",
    "requiresHumanGate": false
  },
  "budgets": { "tokenBudget": 1500, "timeBudgetMs": 40000, "toolCallBudget": 0 }
}
'@
    $groups = @(
        [pscustomobject]@{ group = "schema-minimal"; schema = $schemas.minimal; promptVersion = "c0-minimal-v1"; prompt = $taskPrompt },
        [pscustomobject]@{ group = "schema-full"; schema = $schemas.full; promptVersion = "c0-full-v1"; prompt = $taskPrompt },
        [pscustomobject]@{ group = "prompt-calibrated"; schema = $schemas.minimal; promptVersion = "c0-calibrated-v2"; prompt = $calibratedPrompt }
    )
    if ($C0Groups.Count -gt 0) {
        $allowed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($groupName in $C0Groups) {
            if (-not [string]::IsNullOrWhiteSpace($groupName)) {
                [void]$allowed.Add($groupName.Trim())
            }
        }

        $groups = @($groups | Where-Object { $allowed.Contains($_.group) })
        if ($groups.Count -eq 0) {
            throw "No C0 groups matched -C0Groups: $($C0Groups -join ', ')"
        }
    }
    $runs = @()
    foreach ($group in $groups) {
        $groupRoot = Join-Path $c0Root $group.group
        New-Item -ItemType Directory -Force -Path $groupRoot | Out-Null
        for ($i = 1; $i -le $RepeatCount; $i++) {
            $outFile = Join-Path $groupRoot ("run-{0:D2}.jsonl" -f $i)
            $arguments = @("exec", "--json", "--skip-git-repo-check", "-m", $Model, "--output-schema", $group.schema, $group.prompt)
            if ($DryRun) {
                $dryRunRecord = [pscustomobject]@{ group = $group.group; runIndex = $i; dryRun = $true; command = @($Cli) + $arguments; outputFile = $outFile; schema = $group.schema; promptVersion = $group.promptVersion }
                $dryRunRecord | Add-Member -NotePropertyName runMetrics -NotePropertyValue (New-RunMetrics -Phase "c0" -TaskId $null -Group $group.group -RunIndex $i -AttemptIndex 1 -ReviseRound $null -GraphId "c0.candidate" -StageId $null -StepId $null -Result $null -ModelCallCount 1 -OutputFile $outFile)
                $runs += $dryRunRecord
                continue
            }

            $firstResult = Invoke-TianShuJsonl -Cli $Cli -Arguments $arguments -OutFile $outFile -TimeoutSeconds $CommandTimeoutSeconds
            $firstValidation = Test-C0StageGraphCandidate -AssistantText $firstResult.assistantText
            $firstRunMetrics = New-RunMetrics -Phase "c0" -TaskId $null -Group $group.group -RunIndex $i -AttemptIndex 1 -ReviseRound $null -GraphId "c0.candidate" -StageId $null -StepId $null -Result $firstResult -ModelCallCount 1 -OutputFile $outFile
            $firstRunMetrics.success = [bool]$firstValidation.contractValid
            $finalResult = $firstResult
            $finalValidation = $firstValidation
            $finalRunMetrics = $firstRunMetrics
            $reviseAttempts = @()
            $totalLatencyMs = [int]$firstResult.latencyMs
            $modelCallCount = 1

            for ($revise = 1; $revise -le 3 -and -not $finalValidation.contractValid; $revise++) {
                $revisePrompt = New-C0RevisePrompt -AssistantText $finalResult.assistantText -ValidationErrors $finalValidation.validationErrors
                $reviseOutFile = Join-Path $groupRoot ("run-{0:D2}-revise-{1}.jsonl" -f $i, $revise)
                $reviseArguments = @("exec", "--json", "--skip-git-repo-check", "-m", $Model, "--output-schema", $group.schema, $revisePrompt)
                $reviseResult = Invoke-TianShuJsonl -Cli $Cli -Arguments $reviseArguments -OutFile $reviseOutFile -TimeoutSeconds $CommandTimeoutSeconds
                $reviseValidation = Test-C0StageGraphCandidate -AssistantText $reviseResult.assistantText
                $reviseRunMetrics = New-RunMetrics -Phase "c0" -TaskId $null -Group $group.group -RunIndex $i -AttemptIndex ($revise + 1) -ReviseRound $revise -GraphId "c0.candidate" -StageId $null -StepId $null -Result $reviseResult -ModelCallCount 1 -OutputFile $reviseOutFile
                $reviseRunMetrics.success = [bool]$reviseValidation.contractValid
                $totalLatencyMs += [int]$reviseResult.latencyMs
                $modelCallCount += 1
                $reviseAttempts += [pscustomobject]@{
                    reviseRound = $revise
                    outputFile = $reviseOutFile
                    success = $reviseResult.success
                    timedOut = $reviseResult.timedOut
                    latencyMs = $reviseResult.latencyMs
                    localJsonParsed = $reviseValidation.jsonParsed
                    localContractValid = $reviseValidation.contractValid
                    localValidationErrors = $reviseValidation.validationErrors
                    stageCount = $reviseValidation.stageCount
                    edgeCount = $reviseValidation.edgeCount
                    runMetrics = $reviseRunMetrics
                }
                $finalResult = $reviseResult
                $finalValidation = $reviseValidation
                $finalRunMetrics = $reviseRunMetrics
            }

            $finalRunMetrics.modelCallCount = $modelCallCount
            $runs += [pscustomobject]@{
                group = $group.group
                runIndex = $i
                schema = $group.schema
                promptVersion = $group.promptVersion
                firstOutputFile = $outFile
                firstSuccess = $firstResult.success
                firstTimedOut = $firstResult.timedOut
                firstLatencyMs = $firstResult.latencyMs
                firstLocalJsonParsed = $firstValidation.jsonParsed
                firstLocalContractValid = $firstValidation.contractValid
                firstValidationErrors = $firstValidation.validationErrors
                firstRunMetrics = $firstRunMetrics
                finalOutputFile = $finalResult.outputFile
                finalSuccess = $finalResult.success
                finalTimedOut = $finalResult.timedOut
                finalLocalJsonParsed = $finalValidation.jsonParsed
                finalLocalContractValid = $finalValidation.contractValid
                finalValidationErrors = $finalValidation.validationErrors
                stageCount = $finalValidation.stageCount
                edgeCount = $finalValidation.edgeCount
                reviseRounds = $reviseAttempts.Count
                modelCallCount = $modelCallCount
                totalLatencyMs = $totalLatencyMs
                reviseAttempts = $reviseAttempts
                runMetrics = $finalRunMetrics
            }
        }
    }

    Write-Json (Join-Path $c0Root "runs.json") $runs
    if (-not $DryRun) {
        Write-Json (Join-Path $c0Root "summary.json") (New-C0Summary -Runs $runs)
    }
}

if (-not $RunBaseline -and -not $RunC0 -and -not $RunC -and -not $RunD -and -not $RunP8 -and -not $RunCProfiles -and -not $RunEvolutionNoiseProbe) {
    $RunBaseline = $true
    $RunC0 = $true
}

if ($RepeatCount -lt 1) {
    throw "-RepeatCount must be >= 1."
}

if ($MaxReviseRounds -lt 0) {
    throw "-MaxReviseRounds must be >= 0."
}

if ($CProfileRepeatCount -lt 0) {
    throw "-CProfileRepeatCount must be >= 0."
}

if ($EvolutionProbeRepeatCount -lt 1) {
    throw "-EvolutionProbeRepeatCount must be >= 1."
}

$cli = Resolve-CliPath -RequestedPath $CliPath
$script:ResolvedCliPath = $cli
$script:PriceModel = Read-PriceModel -Path $PriceModelPath
$root = New-RunRoot -RequestedRoot $OutputRoot

$manifest = [pscustomobject]@{
    startedAt = (Get-Date).ToString("o")
    dryRun = [bool]$DryRun
    model = $Model
    temperature = if ([string]::IsNullOrWhiteSpace($Temperature)) { $null } else { $Temperature }
    temperatureFixed = -not [string]::IsNullOrWhiteSpace($Temperature)
    seed = if ([string]::IsNullOrWhiteSpace($Seed)) { $null } else { $Seed }
    seedFixed = -not [string]::IsNullOrWhiteSpace($Seed)
    repeatCount = $RepeatCount
    maxReviseRounds = $MaxReviseRounds
    cAcceptanceFinalLegalRateThreshold = $CAcceptanceFinalLegalRateThreshold
    cAcceptanceAverageReviseThreshold = $CAcceptanceAverageReviseThreshold
    baselineExecutionPath = "hybrid-kernel-shell"
    cliPath = $cli
    outputRoot = $root
    runnerVersion = $script:RunnerVersion
    metricsSchemaVersion = $script:MetricsSchemaVersion
    priceModel = New-PriceModelManifest -PriceModel $script:PriceModel
    cResultsPath = if ([string]::IsNullOrWhiteSpace($CResultsPath)) { $null } else { $CResultsPath }
    strategyGatePath = if ([string]::IsNullOrWhiteSpace($StrategyGatePath)) { $null } else { $StrategyGatePath }
    cProfiles = $CProfiles
    cProfileRepeatCount = if ($CProfileRepeatCount -gt 0) { $CProfileRepeatCount } else { $RepeatCount }
    cProfilePromptTemplateVersion = $CProfilePromptTemplateVersion
    cProfileAcceptancePath = if ([string]::IsNullOrWhiteSpace($CProfileAcceptancePath)) { $null } else { $CProfileAcceptancePath }
    evolutionProbeRepeatCount = $EvolutionProbeRepeatCount
    evolutionProbeStrategySetPath = if ([string]::IsNullOrWhiteSpace($EvolutionProbeStrategySetPath)) { $null } else { $EvolutionProbeStrategySetPath }
    evolutionProbeMinDeltaPath = if ([string]::IsNullOrWhiteSpace($EvolutionProbeMinDeltaPath)) { $null } else { $EvolutionProbeMinDeltaPath }
    traceShapeCanonicalizationVersion = "adaptive-kernel-baseline-v1"
    finishedAt = $null
}
Write-Json (Join-Path $root "manifest.json") $manifest
Write-Json (Join-Path $root "run-metrics.schema.json") (New-RunMetricsSchema)
Write-Json (Join-Path $root "price-model.schema.json") (New-PriceModelSchema)
Write-Json (Join-Path $root "price-model.json") (New-PriceModelManifest -PriceModel $script:PriceModel)
Invoke-EstimatedTokenChannelSelfCheck -Root $root

if ($RunBaseline) {
    Invoke-Baseline -Cli $cli -Root $root
}

if ($RunC0) {
    Invoke-C0 -Cli $cli -Root $root
}

if ($RunC) {
    Invoke-C -Cli $cli -Root $root
}

if ($RunCProfiles) {
    Invoke-CProfiles -Cli $cli -Root $root | Out-Null
}

if ($RunEvolutionNoiseProbe) {
    Invoke-EvolutionNoiseProbe -Root $root | Out-Null
}

if ($RunD) {
    Invoke-D -Root $root | Out-Null
}

if ($RunP8) {
    Invoke-P8 -Root $root | Out-Null
}

$manifest.finishedAt = (Get-Date).ToString("o")
Write-Json (Join-Path $root "manifest.json") $manifest
Write-Host "Adaptive kernel feasibility run output: $root"
