$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publishDirectory = Join-Path $projectRoot "artifacts\publish\win-x64"
$installerProject = Join-Path $projectRoot "installer\MasterFlow.Installer.wixproj"
$buildCacheRoot = Join-Path $projectRoot ".build-cache"
$buildTempDirectory = Join-Path $buildCacheRoot "temp"
$buildPackagesDirectory = Join-Path $buildCacheRoot "packages"
$buildHttpCacheDirectory = Join-Path $buildCacheRoot "http-cache"
$originalTempPath = $env:TEMP
$originalTmpPath = $env:TMP
$originalNugetPackagesPath = $env:NUGET_PACKAGES
$originalNugetHttpCachePath = $env:NUGET_HTTP_CACHE_PATH

New-Item -ItemType Directory -Force -Path $buildTempDirectory, $buildPackagesDirectory, $buildHttpCacheDirectory | Out-Null
$env:TEMP = $buildTempDirectory
$env:TMP = $buildTempDirectory
$env:NUGET_PACKAGES = $buildPackagesDirectory
$env:NUGET_HTTP_CACHE_PATH = $buildHttpCacheDirectory

Push-Location $projectRoot
try {
    & (Join-Path $projectRoot "scripts\test-all.ps1")
    if ($LASTEXITCODE -ne 0) { throw "Полная проверка МастерFlow завершилась ошибкой." }

    dotnet publish "src\MasterFlow.App\MasterFlow.App.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw "Публикация self-contained сборки завершилась ошибкой." }

    dotnet build $installerProject -c Release -p:PublishDir=$publishDirectory
    if ($LASTEXITCODE -ne 0) { throw "Сборка MSI завершилась ошибкой." }

    & (Join-Path $projectRoot "scripts\validate-installer.ps1")
}
finally {
    Pop-Location
    $env:TEMP = $originalTempPath
    $env:TMP = $originalTmpPath
    $env:NUGET_PACKAGES = $originalNugetPackagesPath
    $env:NUGET_HTTP_CACHE_PATH = $originalNugetHttpCachePath
}
