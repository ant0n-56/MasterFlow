$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $projectRoot "src\MasterFlow.App\MasterFlow.App.csproj"
$projectXml = [xml](Get-Content -LiteralPath $projectPath)
$version = [string]$projectXml.Project.PropertyGroup.Version
$portableRoot = Join-Path $projectRoot "artifacts\portable"
$packageName = "MasterFlow-$version-win-x64-portable"
$outputDirectory = Join-Path $portableRoot $packageName
$archivePath = Join-Path $portableRoot "$packageName.zip"
$buildCacheRoot = Join-Path $projectRoot ".build-cache"
$buildTempDirectory = Join-Path $buildCacheRoot "temp"
$buildPackagesDirectory = Join-Path $buildCacheRoot "packages"
$buildHttpCacheDirectory = Join-Path $buildCacheRoot "http-cache"
$originalTempPath = $env:TEMP
$originalTmpPath = $env:TMP
$originalNugetPackagesPath = $env:NUGET_PACKAGES
$originalNugetHttpCachePath = $env:NUGET_HTTP_CACHE_PATH
$dotnetExecutable = if ([string]::IsNullOrWhiteSpace($env:MASTERFLOW_DOTNET)) {
    "dotnet"
}
else {
    $env:MASTERFLOW_DOTNET
}

New-Item -ItemType Directory -Force -Path $portableRoot, $buildTempDirectory, $buildPackagesDirectory, $buildHttpCacheDirectory | Out-Null
$resolvedPortableRoot = [System.IO.Path]::GetFullPath($portableRoot).TrimEnd('\') + '\'
$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($outputDirectory)
if (-not $resolvedOutputDirectory.StartsWith($resolvedPortableRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Отказ от очистки неожиданной папки portable-сборки: $resolvedOutputDirectory"
}
if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}
Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $portableRoot "MasterFlow.exe") -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$env:TEMP = $buildTempDirectory
$env:TMP = $buildTempDirectory
$env:NUGET_PACKAGES = $buildPackagesDirectory
$env:NUGET_HTTP_CACHE_PATH = $buildHttpCacheDirectory

Push-Location $projectRoot
try {
    & (Join-Path $projectRoot "scripts\test-all.ps1")
    if ($LASTEXITCODE -ne 0) { throw "Полная проверка МастерFlow завершилась ошибкой." }

    & $dotnetExecutable publish $projectPath `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:UseSharedCompilation=false `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $outputDirectory
    if ($LASTEXITCODE -ne 0) { throw "Публикация portable EXE завершилась ошибкой." }

    $publishedExecutable = Join-Path $outputDirectory "MasterFlow.App.exe"
    $portableExecutable = Join-Path $outputDirectory "MasterFlow.exe"
    if (-not (Test-Path -LiteralPath $publishedExecutable)) {
        throw "После публикации не найден MasterFlow.App.exe."
    }

    Move-Item -LiteralPath $publishedExecutable -Destination $portableExecutable -Force
    $extraFiles = @(Get-ChildItem -LiteralPath $outputDirectory -File | Where-Object Name -ne "MasterFlow.exe")
    if ($extraFiles.Count -ne 0) {
        throw "Portable-публикация содержит лишние файлы: $($extraFiles.Name -join ', ')."
    }

    Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination $outputDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot "USER_GUIDE.md") -Destination $outputDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot "PRIVACY.md") -Destination $outputDirectory
    Compress-Archive -Path (Join-Path $outputDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal

    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $archiveEntries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) } | ForEach-Object Name)
        $expectedEntries = @("MasterFlow.exe", "PRIVACY.md", "README.md", "USER_GUIDE.md")
        if ((Compare-Object $expectedEntries $archiveEntries).Count -ne 0) {
            throw "Portable ZIP содержит неожиданный набор файлов: $($archiveEntries -join ', ')."
        }
    }
    finally {
        $archive.Dispose()
    }

    Write-Host "Portable EXE готов: $portableExecutable"
    Write-Host "Portable ZIP готов: $archivePath"
}
finally {
    Pop-Location
    $env:TEMP = $originalTempPath
    $env:TMP = $originalTmpPath
    $env:NUGET_PACKAGES = $originalNugetPackagesPath
    $env:NUGET_HTTP_CACHE_PATH = $originalNugetHttpCachePath
}
