param(
    [Parameter(Mandatory = $true)]
    [string] $AppPath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$resolvedAppPath = (Resolve-Path -LiteralPath $AppPath).Path
$testData = Join-Path ([System.IO.Path]::GetTempPath()) ("MasterFlow.E2E." + [Guid]::NewGuid().ToString("N"))
$env:MASTERFLOW_DATA_FOLDER = $testData

function Find-NamedElement($root, $name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-NamedControl($root, $name, $controlType) {
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $name)
    $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        $controlType)
    $condition = New-Object System.Windows.Automation.AndCondition($nameCondition, $typeCondition)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-AutomationId($root, $automationId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $automationId)
    $element = $null
    for ($attempt = 0; $attempt -lt 20 -and $null -eq $element; $attempt++) {
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -eq $element) { Start-Sleep -Milliseconds 200 }
    }
    return $element
}

function Start-MasterFlow {
    $started = Start-Process -FilePath $resolvedAppPath -PassThru
    $window = $null
    for ($attempt = 0; $attempt -lt 60 -and $null -eq $window; $attempt++) {
        Start-Sleep -Milliseconds 250
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $started.Id)
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            $condition)
    }
    if ($null -eq $window) { throw "Окно МастерFlow не найдено через Windows UI Automation." }
    Start-Sleep -Milliseconds 500
    return @($started, $window)
}

function Stop-MasterFlow($started) {
    if (-not $started.HasExited) {
        $started.CloseMainWindow() | Out-Null
        if (-not $started.WaitForExit(4000)) { $started.Kill() }
    }
}

function Select-Section($window, $name) {
    $item = Find-NamedElement $window $name
    if ($null -eq $item) { throw "Раздел не найден: $name" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
}

function Set-TextById($window, $automationId, $value) {
    $field = Find-AutomationId $window $automationId
    if ($null -eq $field) { throw "Поле не найдено: $automationId" }
    $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

$first = Start-MasterFlow
$process = $first[0]
$window = $first[1]
try {
    $transform = $window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
    if ($transform.Current.CanResize) {
        $transform.Resize(820, 560)
        Start-Sleep -Milliseconds 250
    }
    foreach ($name in @("Сегодня", "Клиенты", "Расписание", "Отзывы Avito", "Анализ переписки", "Настройки")) {
        if ($null -eq (Find-NamedElement $window $name)) { throw "Нет доступного раздела: $name" }
    }

    $todayItem = Find-NamedElement $window "Сегодня"
    $todayItem.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait("{HOME}{DOWN}{DOWN}{TAB}")
    Start-Sleep -Milliseconds 250
    $keyboardFocus = [System.Windows.Automation.AutomationElement]::FocusedElement
    if ($keyboardFocus.Current.Name -ne "Имя клиента") {
        throw "Клавиатурный переход из дерева в форму расписания нарушен: $($keyboardFocus.Current.Name)"
    }
    [System.Windows.Forms.SendKeys]::SendWait("KeyboardFocusProbe")
    [System.Windows.Forms.SendKeys]::SendWait("{TAB 8}{ENTER}")
    Start-Sleep -Milliseconds 250
    $keyboardProbeValue = (Find-AutomationId $window "ClientNameTextBox").GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($keyboardProbeValue.Length -ne 0) {
        throw "Кнопка очистки формы не сработала с клавиатуры."
    }

    Select-Section $window "Расписание"
    Set-TextById $window "ClientNameTextBox" "Тестовый клиент"
    Set-TextById $window "ClientContactTextBox" "test-contact-e2e"
    Set-TextById $window "ServiceNameTextBox" "Тестовая услуга"
    Set-TextById $window "AppointmentTimeTextBox" "15:30"
    Set-TextById $window "ClientNotesTextBox" "Тестовая заметка"
    $date = Find-AutomationId $window "AppointmentDatePicker"
    $date.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue(
        [DateTime]::Today.AddDays(2).ToString("d"))
    $saveAppointment = Find-NamedElement $window "Сохранить запись"
    $saveAppointment.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 500
    $upcoming = Find-NamedControl $window "Список ближайших записей" ([System.Windows.Automation.ControlType]::List)
    if ($null -eq $upcoming -or $upcoming.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition).Count -eq 0) {
        throw "Созданная запись не появилась в списке."
    }
    $workspacePath = Join-Path $testData "workspace.dat"
    if (-not (Test-Path -LiteralPath $workspacePath)) { throw "Файл клиентской базы не создан." }
    $workspaceRaw = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($workspacePath))
    if ($workspaceRaw.Contains("test-contact-e2e")) { throw "Контакт клиента сохранён в открытом виде." }

    Select-Section $window "Анализ переписки"
    Set-TextById $window "ConversationTextBox" "Клиент: Здравствуйте! Сколько стоит массаж?`nМастер: Добрый день! Цена 1500 рублей. Могу записать завтра."
    $localConsent = Find-NamedControl $window "Согласие на локальный анализ переписки" ([System.Windows.Automation.ControlType]::CheckBox)
    $localConsent.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    (Find-NamedElement $window "Проанализировать текст переписки локально").GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
    if ($null -eq (Find-NamedElement $window "Рекомендации по общению с клиентом")) {
        throw "Локальные рекомендации не появились."
    }
    (Find-NamedElement $window "Удалить текст переписки из программы").GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    if ((Find-AutomationId $window "ConversationTextBox").GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern).Current.Value.Length -ne 0) {
        throw "Очистка переписки не удалила текст."
    }

    Select-Section $window "Настройки"
    $keyField = Find-NamedControl $window "Ключ OpenAI API" ([System.Windows.Automation.ControlType]::Edit)
    $keyField.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait("sk-test-e2e-abcdefghijklmnopqrstuvwxyz")
    (Find-NamedElement $window "Сохранить ключ OpenAI API защищённо").GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 250
    $settingsPath = Join-Path $testData "ai-settings.dat"
    if (-not (Test-Path -LiteralPath $settingsPath)) { throw "Защищённые настройки ИИ не созданы." }
    $settingsRaw = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($settingsPath))
    if ($settingsRaw.Contains("sk-test-e2e")) { throw "Ключ API сохранён в открытом виде." }
}
finally {
    Stop-MasterFlow $process
}

$second = Start-MasterFlow
$process = $second[0]
$window = $second[1]
try {
    Select-Section $window "Клиенты"
    $clientsList = Find-NamedControl $window "Список клиентов" ([System.Windows.Automation.ControlType]::List)
    $restoredClientFound = $false
    for ($attempt = 0; $attempt -lt 20 -and -not $restoredClientFound; $attempt++) {
        $clientItems = $clientsList.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($clientItem in $clientItems) {
            if ($clientItem.Current.Name.StartsWith("Тестовый клиент", [StringComparison]::CurrentCultureIgnoreCase)) {
                $restoredClientFound = $true
            }
        }
        if (-not $restoredClientFound) { Start-Sleep -Milliseconds 200 }
    }
    if (-not $restoredClientFound) {
        throw "Клиент не восстановился после перезапуска."
    }
    Select-Section $window "Настройки"
    if ($null -eq (Find-NamedElement $window "Ключ API сохранён и защищён для текущей учётной записи Windows.")) {
        throw "Статус ключа API не восстановился после перезапуска."
    }
    (Find-NamedElement $window "Удалить сохранённый ключ OpenAI API").GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
finally {
    Stop-MasterFlow $process
    if (Test-Path -LiteralPath $testData) {
        $resolvedTestData = (Resolve-Path -LiteralPath $testData).Path
        if (-not $resolvedTestData.StartsWith([System.IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) {
            throw "Отказ от удаления неожиданной папки E2E-теста: $resolvedTestData"
        }
        Remove-Item -LiteralPath $resolvedTestData -Recurse -Force
    }
}

Write-Output "MasterFlow E2E passed: keyboard navigation, persistence, encryption, local analysis, cleanup and protected AI settings."
