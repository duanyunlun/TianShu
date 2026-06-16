param(
    [string]$ProjectPath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$Target = 'Restore,Build',
    [string]$Verbosity = 'minimal',
    [string]$MSBuildPath,
    [string]$VsWherePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "[TianShu VSIX Build] $Message"
}

function Get-AbsolutePath {
    param([string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Resolve-VsWherePath {
    param([string]$RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $resolvedRequestedPath = Get-AbsolutePath -Path $RequestedPath
        if (-not (Test-Path -LiteralPath $resolvedRequestedPath)) {
            throw "指定的 vswhere.exe 不存在：$resolvedRequestedPath"
        }

        return $resolvedRequestedPath
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\Installer\vswhere.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Get-AbsolutePath -Path $candidate)
        }
    }

    throw '未找到 vswhere.exe，无法解析 Visual Studio 2026 的 MSBuild 路径。'
}

function Resolve-Vs2026MSBuildPath {
    param(
        [string]$RequestedPath,
        [string]$ResolvedVsWherePath
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $resolvedRequestedPath = Get-AbsolutePath -Path $RequestedPath
        if (-not (Test-Path -LiteralPath $resolvedRequestedPath)) {
            throw "指定的 MSBuild.exe 不存在：$resolvedRequestedPath"
        }

        return $resolvedRequestedPath
    }

    # 优先通过 vswhere 精确解析安装了 VSSDK 的 VS2026（18.x）实例。
    # Prefer vswhere so the script binds to a VS2026 (18.x) instance that actually has VSSDK installed.
    $vswhereResult = & $ResolvedVsWherePath `
        -products * `
        -requires Microsoft.VisualStudio.Component.VSSDK `
        -version '[18.0,19.0)' `
        -latest `
        -find 'MSBuild\Current\Bin\MSBuild.exe'

    $resolvedByVsWhere = @($vswhereResult | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
    if ($resolvedByVsWhere.Count -gt 0 -and (Test-Path -LiteralPath $resolvedByVsWhere[0])) {
        return (Get-AbsolutePath -Path $resolvedByVsWhere[0])
    }

    # 若 vswhere 未返回结果，再退回已知的 VS2026 安装目录探测。
    # If vswhere returns nothing, fall back to well-known VS2026 install locations.
    $fallbackPaths = @(
        'C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\18\Preview\MSBuild\Current\Bin\MSBuild.exe'
    )

    foreach ($fallbackPath in $fallbackPaths) {
        if (Test-Path -LiteralPath $fallbackPath) {
            return (Get-AbsolutePath -Path $fallbackPath)
        }
    }

    throw '未找到 Visual Studio 2026 (18.x) 的 MSBuild.exe。请确认已安装带 VSSDK 的 VS2026。'
}

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $PSScriptRoot '..\src\Presentations\TianShu.VSSDK.VSExtension\TianShu.VSSDK.VSExtension.csproj'
}

$resolvedProjectPath = Get-AbsolutePath -Path $ProjectPath
if (-not (Test-Path -LiteralPath $resolvedProjectPath)) {
    throw "VSIX 项目不存在：$resolvedProjectPath"
}

$resolvedVsWherePath = Resolve-VsWherePath -RequestedPath $VsWherePath
$resolvedMSBuildPath = Resolve-Vs2026MSBuildPath -RequestedPath $MSBuildPath -ResolvedVsWherePath $resolvedVsWherePath

Write-Step "VSIX 项目：$resolvedProjectPath"
Write-Step "MSBuild 路径：$resolvedMSBuildPath"
Write-Step "构建配置：$Configuration"
Write-Step "构建目标：$Target"

& $resolvedMSBuildPath $resolvedProjectPath "/t:$Target" "/p:Configuration=$Configuration" "/v:$Verbosity" '/nologo'
if ($LASTEXITCODE -ne 0) {
    throw "VSIX 构建失败，退出码：$LASTEXITCODE"
}

[pscustomobject]@{
    Success = $true
    ProjectPath = $resolvedProjectPath
    Configuration = $Configuration
    Target = $Target
    Verbosity = $Verbosity
    MSBuildPath = $resolvedMSBuildPath
    VsWherePath = $resolvedVsWherePath
} | ConvertTo-Json -Depth 5
