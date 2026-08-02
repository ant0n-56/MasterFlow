param(
    [Parameter(Mandatory = $true)]
    [string] $CertificateThumbprint,
    [string] $MsiPath = "",
    [string] $TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

if ([string]::IsNullOrWhiteSpace($MsiPath)) {
    $MsiPath = Join-Path $projectRoot "artifacts\installer\MasterFlow-0.1.0-win-x64.msi"
}

$resolvedMsiPath = (Resolve-Path -LiteralPath $MsiPath).Path
$normalizedThumbprint = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
$certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert |
    Where-Object { $_.Thumbprint -eq $normalizedThumbprint } |
    Select-Object -First 1

if ($null -eq $certificate) {
    throw "Сертификат подписи кода с указанным отпечатком не найден."
}
if (-not $certificate.HasPrivateKey) {
    throw "У сертификата нет доступного закрытого ключа."
}
if ($certificate.NotAfter -le (Get-Date)) {
    throw "Срок действия сертификата истёк."
}

$signTool = Get-ChildItem -LiteralPath "C:\Program Files (x86)\Windows Kits\10\bin" `
    -Filter "signtool.exe" -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if ($null -eq $signTool) {
    throw "Не найден SignTool из Windows SDK."
}

& $signTool.FullName sign `
    /sha1 $normalizedThumbprint `
    /fd SHA256 `
    /tr $TimestampUrl `
    /td SHA256 `
    /d "МастерFlow" `
    $resolvedMsiPath
if ($LASTEXITCODE -ne 0) {
    throw "SignTool не смог подписать MSI. Код: $LASTEXITCODE."
}

& $signTool.FullName verify /pa /all /v $resolvedMsiPath
if ($LASTEXITCODE -ne 0) {
    throw "Проверка цифровой подписи завершилась ошибкой. Код: $LASTEXITCODE."
}

$signature = Get-AuthenticodeSignature -LiteralPath $resolvedMsiPath
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Windows не считает подпись действительной: $($signature.StatusMessage)"
}

$hash = Get-FileHash -LiteralPath $resolvedMsiPath -Algorithm SHA256
[PSCustomObject]@{
    Path = $resolvedMsiPath
    Signer = $signature.SignerCertificate.Subject
    CertificateThumbprint = $signature.SignerCertificate.Thumbprint
    Timestamped = $null -ne $signature.TimeStamperCertificate
    SHA256 = $hash.Hash
} | Format-List
