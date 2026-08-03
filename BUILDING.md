# Сборка и проверка МастерFlow

Требования для разработки:

- Windows 10 версии 2004 или новее либо Windows 11;
- .NET SDK 8 или новее;
- Microsoft Edge WebView2 Runtime для раздела Avito.

## Полная проверка

Запустите из корня проекта:

```powershell
./scripts/test-all.ps1
```

По умолчанию используется команда `dotnet` из системы. Чтобы явно выбрать SDK, задайте путь только для текущего сеанса:

```powershell
$env:MASTERFLOW_DOTNET = "D:\путь\к\dotnet.exe"
```

Сценарий выполняет модульные и интеграционные тесты, собирает Release и запускает постоянный Windows UI Automation E2E-тест с вымышленными данными во временной папке.

## Локальный MSI

```powershell
./scripts/build-installer.ps1
```

Сценарий сначала выполняет полную проверку, затем создаёт self-contained сборку `win-x64` и MSI в `artifacts/installer`. Каталог `artifacts` не добавляется в Git.

## Portable ZIP без установщика

```powershell
./scripts/build-portable.ps1
```

Сценарий выполняет полную проверку, создаёт self-contained `MasterFlow.exe` и упаковывает его с README, руководством и политикой конфиденциальности в `artifacts/portable/MasterFlow-0.1.1-win-x64-portable.zip`. Устанавливать .NET отдельно не требуется.

## Цифровая подпись MSI

Для подписи нужен действующий сертификат подписи кода с закрытым ключом в хранилище сертификатов Windows. Самоподписанный сертификат для публичного выпуска не используется.

```powershell
./scripts/sign-installer.ps1 -CertificateThumbprint "ОТПЕЧАТОК_СЕРТИФИКАТА"
```

Сценарий подписывает MSI алгоритмом SHA-256, добавляет доверенную отметку времени и проверяет готовую подпись. После подписи нужно снова выполнить `./scripts/validate-installer.ps1` и записать новый SHA-256 в описание выпуска.
