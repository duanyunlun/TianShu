param(
    [int]$ExpPid,
    [string]$RootSuffix = 'TianShuExp',
    [string]$Prompt = '当前目录是？',
    [string]$WorkingDirectory,
    [string]$ConfigPath,
    [string]$ProfileName,
    [int]$TimeoutSeconds = 45,
    [switch]$SkipOpenToolWindow,
    [switch]$SkipStartKernel
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "[TianShu UI Smoke] $Message"
}

function Get-ExpInstance {
    param(
        [int]$RequestedPid,
        [string]$RequestedRootSuffix
    )

    $rootSuffixPattern = [Regex]::Escape($RequestedRootSuffix)
    $candidates = @(Get-CimInstance Win32_Process -Filter "Name = 'devenv.exe'" |
        Where-Object { $_.CommandLine -match "(?i)(^|\s)/rootsuffix\s+$rootSuffixPattern(\s|$)" })

    if ($RequestedPid -gt 0) {
        $match = $candidates | Where-Object ProcessId -eq $RequestedPid | Select-Object -First 1
        if ($null -eq $match) {
            throw "未找到 PID=$RequestedPid 且 /rootsuffix $RequestedRootSuffix 的实验性 Visual Studio 实例。"
        }

        return $match
    }

    if ($candidates.Count -eq 0) {
        throw "当前没有运行中的实验性 Visual Studio 实例。请先在主 VS 中按 F5 拉起 /rootsuffix $RequestedRootSuffix 实例。"
    }

    if ($candidates.Count -gt 1) {
        $ids = ($candidates | Select-Object -ExpandProperty ProcessId) -join ', '
        throw "检测到多个实验性 Visual Studio 实例：$ids。请使用 -ExpPid 指定。"
    }

    return $candidates[0]
}

function Ensure-RotHelper {
    if ('TianShu.Tools.VisualStudioRotHelper' -as [type]) {
        return
    }

    $code = @"
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace TianShu.Tools
{
    public static class VisualStudioRotHelper
    {
        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable pprot);

        public static object GetDteForProcess(int processId, string versionPrefix)
        {
            string prefix = "!VisualStudio.DTE." + versionPrefix + ":" + processId.ToString();
            IRunningObjectTable rot;
            Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out rot));
            IEnumMoniker enumMoniker;
            rot.EnumRunning(out enumMoniker);
            enumMoniker.Reset();
            IMoniker[] monikers = new IMoniker[1];
            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
            {
                IBindCtx ctx;
                Marshal.ThrowExceptionForHR(CreateBindCtx(0, out ctx));
                string name;
                monikers[0].GetDisplayName(ctx, null, out name);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                object value;
                rot.GetObject(monikers[0], out value);
                return value;
            }

            return null;
        }
    }
}
"@

    Add-Type -Language CSharp -TypeDefinition $code
}

function Get-DteForProcess {
    param(
        [int]$ProcessId,
        [string]$VersionPrefix = '18.0'
    )

    Ensure-RotHelper
    return [TianShu.Tools.VisualStudioRotHelper]::GetDteForProcess($ProcessId, $VersionPrefix)
}

function Escape-RegexText {
    param([string]$Text)
    return [Regex]::Escape($Text)
}

function Get-UiaCliDll {
    $project = 'src/Features/WindowsAgent/src/Windows.Agent.Cli/Windows.Agent.Cli.csproj'
    $dll = 'src/Features/WindowsAgent/src/Windows.Agent.Cli/bin/Debug/net10.0-windows/Windows.Agent.Cli.dll'

    if (-not (Test-Path $dll)) {
        Write-Step '构建 Windows.Agent.Cli'
        dotnet build $project -nologo -v quiet | Out-Null
    }

    if (-not (Test-Path $dll)) {
        throw "未找到 Windows.Agent.Cli 输出：$dll"
    }

    return $dll
}

function Invoke-UiaCli {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$Dangerous,
        [switch]$AllowFailure
    )

    $cliDll = Get-UiaCliDll
    $allArgs = @($cliDll) + $Arguments
    if ($Dangerous) {
        $allArgs += '--dangerous'
    }

    Write-Step ("执行 UIA 命令: {0}" -f ($Arguments -join ' '))
    $output = & dotnet @allArgs 2>&1
    $jsonLine = $output | Where-Object { $_.TrimStart().StartsWith('{') } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($jsonLine)) {
        throw "UIA CLI 输出无法解析为 JSON：`n$($output | Out-String)"
    }

    $result = $jsonLine | ConvertFrom-Json
    if (-not $AllowFailure -and -not $result.success) {
        throw "UIA 调用失败：$($result.message)"
    }

    return $result
}

function Get-UiaParsed {
    param($Result)

    if ($null -eq $Result.result -or $null -eq $Result.result.parsed) {
        return $null
    }

    return $Result.result.parsed
}

function Wait-UiaElement {
    param(
        [Parameter(Mandatory = $true)][string]$WindowRegex,
        [Parameter(Mandatory = $true)][string]$Selector,
        [int]$TimeoutSeconds = 20
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $result = Invoke-UiaCli -Arguments @('desktop', 'uia-find', '--window', $WindowRegex, '--selector', $Selector, '--limit', '1') -AllowFailure
        if ($result.success) {
            $parsed = Get-UiaParsed -Result $result
            if ($parsed -and $parsed.matches.Count -ge 1) {
                return $result
            }
        }

        Start-Sleep -Milliseconds 500
    }

    throw "等待 UIA 元素超时：$Selector"
}

$exp = Get-ExpInstance -RequestedPid $ExpPid -RequestedRootSuffix $RootSuffix
$process = Get-Process -Id $exp.ProcessId
if ([string]::IsNullOrWhiteSpace($process.MainWindowTitle)) {
    throw "实验性实例 PID=$($exp.ProcessId) 尚未出现主窗口标题，请等 VS 完全启动后重试。"
}

$windowRegex = Escape-RegexText $process.MainWindowTitle
Write-Step "目标实验性实例 PID=$($exp.ProcessId)，标题=$($process.MainWindowTitle)"

if (-not $SkipOpenToolWindow) {
    $dte = Get-DteForProcess -ProcessId $exp.ProcessId
    if ($null -eq $dte) {
        throw '未能在 ROT 中获取实验性实例的 DTE 对象，无法自动打开工具窗口。'
    }

    Write-Step '通过 DTE.Commands.Raise 打开 TianShu 对话工具窗口'
    $null = $dte.Commands.Raise('{d8c148b7-6cc7-49bc-9e5d-f23a215a8c9d}', 256, $null, $null)
}

$null = Wait-UiaElement -WindowRegex $windowRegex -Selector 'automationId=TianShuConversationRoot' -TimeoutSeconds $TimeoutSeconds
Write-Step '已检测到 TianShu 对话工具窗口'

if ($WorkingDirectory) {
    $null = Invoke-UiaCli -Arguments @('desktop', 'uia-setvalue', '--window', $windowRegex, '--selector', 'automationId=WorkingDirectoryTextBox;controlType=Edit', '--value', $WorkingDirectory) -Dangerous
}

if ($ConfigPath) {
    $null = Invoke-UiaCli -Arguments @('desktop', 'uia-setvalue', '--window', $windowRegex, '--selector', 'automationId=ConfigPathTextBox;controlType=Edit', '--value', $ConfigPath) -Dangerous
}

if ($PSBoundParameters.ContainsKey('ProfileName')) {
    $null = Invoke-UiaCli -Arguments @('desktop', 'uia-setvalue', '--window', $windowRegex, '--selector', 'automationId=ProfileNameTextBox;controlType=Edit', '--value', $ProfileName) -Dangerous
}

if (-not $SkipStartKernel) {
    $null = Invoke-UiaCli -Arguments @('desktop', 'uia-invoke', '--window', $windowRegex, '--selector', 'automationId=StartKernelButton;controlType=Button') -Dangerous
    Start-Sleep -Seconds 2
}

$null = Invoke-UiaCli -Arguments @('desktop', 'uia-setvalue', '--window', $windowRegex, '--selector', 'automationId=InputTextBox;controlType=Edit', '--value', $Prompt) -Dangerous
$null = Invoke-UiaCli -Arguments @('desktop', 'uia-invoke', '--window', $windowRegex, '--selector', 'automationId=SendButton;controlType=Button') -Dangerous
Start-Sleep -Seconds 2

$chatStatusResult = Invoke-UiaCli -Arguments @('desktop', 'uia-find', '--window', $windowRegex, '--selector', 'automationId=ChatStatusText;controlType=Text', '--limit', '1')
$hostStatusResult = Invoke-UiaCli -Arguments @('desktop', 'uia-find', '--window', $windowRegex, '--selector', 'automationId=HostStatusText;controlType=Text', '--limit', '1')

$chatStatus = (Get-UiaParsed -Result $chatStatusResult).matches[0].name
$hostStatus = (Get-UiaParsed -Result $hostStatusResult).matches[0].name

[pscustomobject]@{
    success = $true
    expPid = $exp.ProcessId
    windowTitle = $process.MainWindowTitle
    chatStatus = $chatStatus
    hostStatus = $hostStatus
    prompt = $Prompt
} | ConvertTo-Json -Depth 5
