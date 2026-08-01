$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

Push-Location $projectRoot
try {
    dotnet test MasterFlow.slnx
    if ($LASTEXITCODE -ne 0) { throw "Модульные и интеграционные тесты завершились ошибкой." }

    dotnet build MasterFlow.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Release-сборка завершилась ошибкой." }

    $appPath = Join-Path $projectRoot "src\MasterFlow.App\bin\Release\net8.0-windows10.0.19041.0\MasterFlow.App.exe"
    & (Join-Path $projectRoot "tests\e2e\MasterFlow.E2E.ps1") -AppPath $appPath
}
finally {
    Pop-Location
}
