param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$projects = @(
    'samples\modules\provider\TianShu.Samples.Provider.Echo.Tests\TianShu.Samples.Provider.Echo.Tests.csproj',
    'samples\modules\tool\TianShu.Samples.Tool.WordCount.Tests\TianShu.Samples.Tool.WordCount.Tests.csproj',
    'samples\modules\memory\TianShu.Samples.Memory.InMemory.Tests\TianShu.Samples.Memory.InMemory.Tests.csproj'
)

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    Write-Host "Testing $project"
    dotnet test $projectPath --configuration $Configuration --nologo --logger "console;verbosity=minimal" /p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) {
        throw "Sample module validation failed: $project"
    }
}
