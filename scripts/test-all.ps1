$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$originalDotnetRoot = $env:DOTNET_ROOT
$originalDotnetRootX64 = $env:DOTNET_ROOT_X64
$dotnetExecutable = if ([string]::IsNullOrWhiteSpace($env:MASTERFLOW_DOTNET)) {
    "dotnet"
}
else {
    $env:MASTERFLOW_DOTNET
}

if (-not [string]::IsNullOrWhiteSpace($env:MASTERFLOW_DOTNET)) {
    $selectedDotnetRoot = Split-Path -Parent (Resolve-Path -LiteralPath $dotnetExecutable).Path
    $env:DOTNET_ROOT = $selectedDotnetRoot
    $env:DOTNET_ROOT_X64 = $selectedDotnetRoot
}

Push-Location $projectRoot
try {
    & $dotnetExecutable test "tests\MasterFlow.Core.Tests\MasterFlow.Core.Tests.csproj" -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw "Модульные и интеграционные тесты завершились ошибкой." }

    & $dotnetExecutable build "src\MasterFlow.App\MasterFlow.App.csproj" -c Release -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw "Release-сборка завершилась ошибкой." }

    $appPath = Join-Path $projectRoot "src\MasterFlow.App\bin\Release\net8.0-windows10.0.19041.0\MasterFlow.App.exe"
    & (Join-Path $projectRoot "tests\e2e\MasterFlow.E2E.ps1") -AppPath $appPath
}
finally {
    Pop-Location
    $env:DOTNET_ROOT = $originalDotnetRoot
    $env:DOTNET_ROOT_X64 = $originalDotnetRootX64
}
