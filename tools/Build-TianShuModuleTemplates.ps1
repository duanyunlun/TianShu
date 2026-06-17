param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$projects = @(
    'templates\modules\provider\TianShu.Template.ProviderModule.Tests\TianShu.Template.ProviderModule.Tests.csproj',
    'templates\modules\tool\TianShu.Template.ToolModule.Tests\TianShu.Template.ToolModule.Tests.csproj',
    'templates\modules\memory\TianShu.Template.MemoryModule.Tests\TianShu.Template.MemoryModule.Tests.csproj'
)

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    Write-Host "Testing $project"
    dotnet test $projectPath --configuration $Configuration --nologo --logger "console;verbosity=minimal" /p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) {
        throw "Template validation failed: $project"
    }
}
