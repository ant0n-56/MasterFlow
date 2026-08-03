param(
    [string] $MsiPath = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($MsiPath)) {
    $MsiPath = Join-Path $projectRoot "artifacts\installer\MasterFlow-0.1.1-win-x64.msi"
}
$resolvedMsiPath = (Resolve-Path -LiteralPath $MsiPath).Path
$validationDirectory = Join-Path $projectRoot "artifacts\validation\administrative-image"
$validationTempDirectory = Join-Path $projectRoot "artifacts\validation\temp"
$originalTempPath = $env:TEMP
$originalTmpPath = $env:TMP

if (Test-Path -LiteralPath $validationDirectory) {
    $resolvedOldValidation = (Resolve-Path -LiteralPath $validationDirectory).Path
    $allowedValidationRoot = Join-Path $projectRoot "artifacts\validation"
    if (-not $resolvedOldValidation.StartsWith($allowedValidationRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Отказ от очистки неожиданной папки проверки: $resolvedOldValidation"
    }
    Remove-Item -LiteralPath $resolvedOldValidation -Recurse -Force
}
try {
    $systemDriveName = ([System.IO.Path]::GetPathRoot($env:SystemRoot)).TrimEnd('\').TrimEnd(':')
    $systemDriveFree = (Get-PSDrive -Name $systemDriveName).Free
    $testedApplication = $null
    $validationMode = ""

    if ($systemDriveFree -ge 512MB) {
        New-Item -ItemType Directory -Force -Path $validationDirectory, $validationTempDirectory | Out-Null
        $env:TEMP = $validationTempDirectory
        $env:TMP = $validationTempDirectory
        $arguments = @(
            "/a",
            ('"' + $resolvedMsiPath + '"'),
            "/qn",
            ('TARGETDIR="' + $validationDirectory + '"')
        )
        $extraction = Start-Process -FilePath "msiexec.exe" `
            -ArgumentList $arguments `
            -WindowStyle Hidden `
            -Wait `
            -PassThru
        if ($extraction.ExitCode -ne 0) {
            throw "Windows Installer не смог распаковать MSI. Код: $($extraction.ExitCode)."
        }
        $testedApplication = Get-ChildItem -LiteralPath $validationDirectory -Recurse -Filter "MasterFlow.App.exe" | Select-Object -First 1
        $validationMode = "Административная распаковка MSI и E2E"
    }
    else {
        Write-Warning "На системном диске меньше 512 МБ. Административная распаковка MSI пропущена; проверяется точная publish-папка, из которой собран пакет."
        $publishedAppPath = Join-Path $projectRoot "artifacts\publish\win-x64\MasterFlow.App.exe"
        if (Test-Path -LiteralPath $publishedAppPath) {
            $testedApplication = Get-Item -LiteralPath $publishedAppPath
        }
        $validationMode = "E2E исходной self-contained publish-папки; распаковка MSI требует места на системном диске"
    }

    if ($null -eq $testedApplication) { throw "Не найдено приложение для E2E-проверки установщика." }
    & (Join-Path $projectRoot "tests\e2e\MasterFlow.E2E.ps1") -AppPath $testedApplication.FullName

    $hash = Get-FileHash -LiteralPath $resolvedMsiPath -Algorithm SHA256
    $signature = Get-AuthenticodeSignature -LiteralPath $resolvedMsiPath
    $item = Get-Item -LiteralPath $resolvedMsiPath
    [PSCustomObject]@{
        Path = $item.FullName
        SizeBytes = $item.Length
        SHA256 = $hash.Hash
        Signature = $signature.Status
        TestedApplication = $testedApplication.FullName
        ValidationMode = $validationMode
    } | Format-List
}
finally {
    $env:TEMP = $originalTempPath
    $env:TMP = $originalTmpPath
    if (Test-Path -LiteralPath $validationDirectory) {
        $resolvedValidation = (Resolve-Path -LiteralPath $validationDirectory).Path
        $allowedValidationRoot = Join-Path $projectRoot "artifacts\validation"
        if ($resolvedValidation.StartsWith($allowedValidationRoot, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedValidation -Recurse -Force
        }
    }
    if (Test-Path -LiteralPath $validationTempDirectory) {
        Remove-Item -LiteralPath $validationTempDirectory -Recurse -Force
    }
}
