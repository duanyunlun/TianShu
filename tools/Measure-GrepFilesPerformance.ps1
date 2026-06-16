param(
    [int]$Iterations = 5,
    [int]$SyntheticFiles = 5000,
    [double]$MaxManagedToRgRatio = 100,
    [string]$ArtifactDirectory = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$artifactRoot = if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    Join-Path $repoRoot ("Test\GrepFilesPerformance\artifacts\" + (Get-Date -Format "yyyyMMdd-HHmmssfff"))
} else {
    $ArtifactDirectory
}

$previousRun = $env:TIANSHU_RUN_GREP_PERF
$previousIterations = $env:TIANSHU_GREP_PERF_ITERATIONS
$previousSyntheticFiles = $env:TIANSHU_GREP_PERF_SYNTHETIC_FILES
$previousArtifactDir = $env:TIANSHU_GREP_PERF_ARTIFACT_DIR
$previousMaxRatio = $env:TIANSHU_GREP_PERF_ASSERT_MAX_RATIO

try {
    $env:TIANSHU_RUN_GREP_PERF = "1"
    $env:TIANSHU_GREP_PERF_ITERATIONS = [string]$Iterations
    $env:TIANSHU_GREP_PERF_SYNTHETIC_FILES = [string]$SyntheticFiles
    $env:TIANSHU_GREP_PERF_ARTIFACT_DIR = [string]$artifactRoot
    $env:TIANSHU_GREP_PERF_ASSERT_MAX_RATIO = [string]$MaxManagedToRgRatio

    dotnet test (Join-Path $repoRoot "tests\TianShu.AppHost.Tests\TianShu.AppHost.Tests.csproj") `
        --filter "FullyQualifiedName~GrepFilesPerformanceTests" `
        -v minimal `
        -m:1 `
        /p:UseSharedCompilation=false `
        /nodeReuse:false `
        --logger "console;verbosity=detailed"

    Write-Host ""
    Write-Host "grep_files performance artifacts:"
    Write-Host $artifactRoot
} finally {
    $env:TIANSHU_RUN_GREP_PERF = $previousRun
    $env:TIANSHU_GREP_PERF_ITERATIONS = $previousIterations
    $env:TIANSHU_GREP_PERF_SYNTHETIC_FILES = $previousSyntheticFiles
    $env:TIANSHU_GREP_PERF_ARTIFACT_DIR = $previousArtifactDir
    $env:TIANSHU_GREP_PERF_ASSERT_MAX_RATIO = $previousMaxRatio
}
