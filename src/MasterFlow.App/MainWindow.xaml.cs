using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MasterFlow.Core;
using Microsoft.Web.WebView2.Core;

namespace MasterFlow.App;

public partial class MainWindow : Window
{
    private MasterWorkspace _workspace = new();
    private readonly FileWorkspaceStore _workspaceStore;
    private readonly DispatcherTimer _reviewDetectionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly HashSet<Guid> _announcedDueReminders = [];
    private bool _reviewBrowserReady;
    private bool _reviewImportInProgress;
    private bool _reviewDialogWasVisible;
    private IReadOnlyList<ReviewRecord> _importedReviews = [];
    private double? _importedAverageRating;

    public MainWindow()
    {
        InitializeComponent();
        var dataFolder = ResolveDataFolder();
        _workspaceStore = new FileWorkspaceStore(
            Path.Combine(dataFolder, "workspace.dat"),
            new WindowsWorkspaceProtector());
        _reviewDetectionTimer.Tick += ReviewDetectionTimer_Tick;
        _reminderTimer.Tick += ReminderTimer_Tick;
    }

    private static string ResolveDataFolder()
    {
        var testOverride = Environment.GetEnvironmentVariable("MASTERFLOW_DATA_FOLDER");
        return string.IsNullOrWhiteSpace(testOverride)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MasterFlow",
                "Data")
            : Path.GetFullPath(testOverride);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var loadMessage = LoadWorkspace();
        AppointmentDatePicker.SelectedDate = DateTime.Today.AddDays(1);
        AppointmentTimeTextBox.Text = "10:00";
        RefreshLists();
        _reminderTimer.Start();
        TodayNavigationItem.IsSelected = true;
        TodayNavigationItem.Focus();
        if (!string.IsNullOrEmpty(loadMessage))
        {
            Announce(loadMessage);
        }
        await InitializeReviewBrowserAsync();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _reviewDetectionTimer.Stop();
        _reminderTimer.Stop();
    }

    private void NavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem item || item.Tag is not string section)
        {
            return;
        }

        ShowSection(section);
    }

    private void OpenSchedule_Click(object sender, RoutedEventArgs e)
    {
        SelectNavigationItem("Schedule");
        ClientNameTextBox.Focus();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshLists();
        Announce("Списки записей и напоминаний обновлены.");
    }

    private void ClientRemindersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var reminder = ClientRemindersList.SelectedItem as ClientReminder;
        CopyReminderButton.IsEnabled = reminder is not null;
        MarkReminderSentButton.IsEnabled = reminder is not null && reminder.State != ReminderState.Sent;
        ResetReminderButton.IsEnabled = reminder?.State == ReminderState.Sent;
    }

    private void CopyReminder_Click(object sender, RoutedEventArgs e)
    {
        if (ClientRemindersList.SelectedItem is not ClientReminder reminder)
        {
            Announce("Выберите напоминание.");
            ClientRemindersList.Focus();
            return;
        }

        try
        {
            Clipboard.SetText(reminder.Message);
            Announce($"Текст напоминания для клиента «{reminder.ClientName}» скопирован.");
        }
        catch (Exception)
        {
            Announce("Не удалось скопировать текст. Закройте программы, которые используют буфер обмена, и попробуйте снова.");
        }
    }

    private void MarkReminderSent_Click(object sender, RoutedEventArgs e)
    {
        if (ClientRemindersList.SelectedItem is not ClientReminder reminder)
        {
            Announce("Выберите напоминание.");
            ClientRemindersList.Focus();
            return;
        }

        var snapshotBeforeChange = _workspace.CreateSnapshot();
        _workspace.MarkReminderSent(reminder.AppointmentId, DateTime.Now);
        if (!SaveWorkspace(snapshotBeforeChange))
        {
            return;
        }

        _announcedDueReminders.Add(reminder.AppointmentId);
        RefreshLists(selectedReminderId: reminder.AppointmentId);
        ClientRemindersList.Focus();
        Announce($"Напоминание для клиента «{reminder.ClientName}» отмечено как отправленное.");
    }

    private void ResetReminder_Click(object sender, RoutedEventArgs e)
    {
        if (ClientRemindersList.SelectedItem is not ClientReminder reminder)
        {
            Announce("Выберите отправленное напоминание.");
            ClientRemindersList.Focus();
            return;
        }

        var snapshotBeforeChange = _workspace.CreateSnapshot();
        _workspace.ResetReminder(reminder.AppointmentId);
        if (!SaveWorkspace(snapshotBeforeChange))
        {
            return;
        }

        RefreshLists(selectedReminderId: reminder.AppointmentId);
        ClientRemindersList.Focus();
        Announce($"Напоминание для клиента «{reminder.ClientName}» снова ожидает отправки.");
    }

    private void ReminderTimer_Tick(object? sender, EventArgs e)
    {
        var reminders = _workspace.GetClientReminders(DateTime.Now);
        var newlyDue = reminders
            .Where(reminder => reminder.State == ReminderState.Due)
            .Where(reminder => _announcedDueReminders.Add(reminder.AppointmentId))
            .ToList();
        RefreshReminderList(reminders);
        if (newlyDue.Count == 1)
        {
            Announce($"Пора отправить напоминание клиенту «{newlyDue[0].ClientName}».");
        }
        else if (newlyDue.Count > 1)
        {
            Announce($"Пора отправить напоминания. Клиентов: {newlyDue.Count}.");
        }
    }

    private void SaveAppointment_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceSnapshot? snapshotBeforeChange = null;
        try
        {
            if (!AppointmentDatePicker.SelectedDate.HasValue)
            {
                ShowInputError("Выберите дату сеанса.", AppointmentDatePicker);
                return;
            }

            if (!TimeSpan.TryParseExact(
                    AppointmentTimeTextBox.Text.Trim(),
                    ["h\\:mm", "hh\\:mm"],
                    CultureInfo.InvariantCulture,
                    out var time))
            {
                ShowInputError("Введите время в формате часы и минуты, например 15:30.", AppointmentTimeTextBox);
                return;
            }

            if (ReminderComboBox.SelectedItem is not ComboBoxItem reminderItem ||
                !double.TryParse(reminderItem.Tag?.ToString(), out var reminderMinutes))
            {
                ShowInputError("Выберите время напоминания.", ReminderComboBox);
                return;
            }

            snapshotBeforeChange = _workspace.CreateSnapshot();
            var client = _workspace.AddClient(
                ClientNameTextBox.Text,
                ClientContactTextBox.Text,
                "Avito",
                ClientNotesTextBox.Text);

            var startsAt = AppointmentDatePicker.SelectedDate.Value.Date + time;
            var appointment = _workspace.AddAppointment(
                client,
                ServiceNameTextBox.Text,
                startsAt,
                TimeSpan.FromMinutes(reminderMinutes),
                DateTime.Now);

            if (!SaveWorkspace(snapshotBeforeChange))
            {
                return;
            }

            RefreshLists();
            ClearForm();
            SelectNavigationItem("Today");
            UpcomingAppointmentsList.SelectedItem = appointment;
            UpcomingAppointmentsList.Focus();
            Announce($"Запись сохранена. {appointment.AccessibleSummary}");
        }
        catch (ArgumentException error)
        {
            Announce(error.Message.Split(" (Parameter", StringSplitOptions.None)[0]);
        }
        catch (InvalidOperationException error)
        {
            Announce(error.Message);
        }
    }

    private void ClearForm_Click(object sender, RoutedEventArgs e)
    {
        ClearForm();
        ClientNameTextBox.Focus();
        Announce("Форма очищена.");
    }

    private void ClientSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        RefreshClientList();
    }

    private void ClientsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClientsList.SelectedItem is not ClientRecord client)
        {
            ClientCardGroupBox.Visibility = Visibility.Collapsed;
            return;
        }

        ClientCardNameTextBox.Text = client.Name;
        ClientCardContactTextBox.Text = client.Contact;
        ClientCardSourceTextBox.Text = client.Source;
        ClientCardNotesTextBox.Text = client.Notes;
        var history = _workspace.GetClientAppointments(client.Id);
        ClientHistoryList.ItemsSource = history;
        EmptyClientHistoryText.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClientCardGroupBox.Visibility = Visibility.Visible;
    }

    private void SaveClientChanges_Click(object sender, RoutedEventArgs e)
    {
        if (ClientsList.SelectedItem is not ClientRecord selected)
        {
            Announce("Выберите клиента.");
            return;
        }

        var snapshotBeforeChange = _workspace.CreateSnapshot();
        try
        {
            var updated = _workspace.UpdateClient(
                selected.Id,
                ClientCardNameTextBox.Text,
                ClientCardContactTextBox.Text,
                ClientCardSourceTextBox.Text,
                ClientCardNotesTextBox.Text);
            if (!SaveWorkspace(snapshotBeforeChange))
            {
                return;
            }

            RefreshLists(updated.Id);
            Announce($"Карточка клиента «{updated.Name}» сохранена.");
        }
        catch (ArgumentException error)
        {
            RestoreWorkspaceAfterFailedChange(snapshotBeforeChange);
            Announce(error.Message.Split(" (Parameter", StringSplitOptions.None)[0]);
        }
        catch (InvalidOperationException error)
        {
            RestoreWorkspaceAfterFailedChange(snapshotBeforeChange);
            Announce(error.Message);
        }
    }

    private void CreateAppointmentForSelectedClient_Click(object sender, RoutedEventArgs e)
    {
        if (ClientsList.SelectedItem is not ClientRecord client)
        {
            Announce("Выберите клиента.");
            return;
        }

        ClientNameTextBox.Text = client.Name;
        ClientContactTextBox.Text = client.Contact;
        ClientNotesTextBox.Text = client.Notes;
        SelectNavigationItem("Schedule");
        ServiceNameTextBox.Focus();
        Announce($"Создаём следующую запись для клиента «{client.Name}». Укажите услугу, дату и время.");
    }

    private void DeleteSelectedClient_Click(object sender, RoutedEventArgs e)
    {
        if (ClientsList.SelectedItem is not ClientRecord client)
        {
            Announce("Выберите клиента.");
            return;
        }

        var appointmentCount = _workspace.GetClientAppointments(client.Id).Count;
        var confirmation = new ConfirmDeleteDialog(client.Name, appointmentCount)
        {
            Owner = this
        };
        if (confirmation.ShowDialog() != true)
        {
            Announce("Удаление отменено.");
            return;
        }

        var snapshotBeforeChange = _workspace.CreateSnapshot();
        _workspace.DeleteClient(client.Id);
        if (!SaveWorkspace(snapshotBeforeChange))
        {
            return;
        }

        RefreshLists();
        ClientSearchTextBox.Focus();
        Announce($"Клиент «{client.Name}» и его записи удалены.");
    }

    private async void OpenAvito_Click(object sender, RoutedEventArgs e)
    {
        if (!AvitoLink.TryCreate(AvitoUrlTextBox.Text, out var link, out var error))
        {
            Announce(error);
            AvitoUrlTextBox.Focus();
            return;
        }

        if (!await EnsureReviewBrowserIsReadyAsync())
        {
            return;
        }

        ImportVisibleReviewsButton.IsEnabled = false;
        _reviewDialogWasVisible = false;
        ResetImportedReviews();
        AvitoBrowser.Source = link!.Uri;
        Announce("Открываем Avito. Дождитесь загрузки страницы, затем откройте отзывы.");
        AvitoBrowser.Focus();
    }

    private async void ImportVisibleReviews_Click(object sender, RoutedEventArgs e)
    {
        await ImportReviewsFromOpenPageAsync(isAutomatic: false);
    }

    private async Task ImportReviewsFromOpenPageAsync(bool isAutomatic)
    {
        if (_reviewImportInProgress)
        {
            return;
        }

        if (!await EnsureReviewBrowserIsReadyAsync() || AvitoBrowser.Source is null)
        {
            return;
        }

        if (!AvitoLink.TryCreate(AvitoBrowser.Source.ToString(), out _, out _))
        {
            Announce("Импорт разрешён только со страницы avito.ru.");
            return;
        }

        try
        {
            _reviewImportInProgress = true;
            ImportVisibleReviewsButton.IsEnabled = false;
            Announce(isAutomatic
                ? "Окно отзывов найдено. Импортируем отзывы автоматически."
                : "Повторно читаем отзывы с открытой страницы.");

            var extraction = await CollectReviewBlocksAsync();
            var reviews = ReviewTextParser.Parse(extraction.Blocks);

            ImportedReviewsList.ItemsSource = reviews;
            if (reviews.Count == 0)
            {
                ResetImportedReviews();
                ReviewImportSummaryText.Text = "Отзывы не найдены. Откройте на странице раздел с отзывами и повторите импорт.";
                Announce(ReviewImportSummaryText.Text);
                return;
            }

            var rated = reviews.Where(review => review.Rating.HasValue).ToList();
            double? average = rated.Count == 0 ? null : rated.Average(review => review.Rating!.Value);
            if (!average.HasValue && double.TryParse(
                    extraction.OverallRating?.Replace(',', '.'),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var overallRating))
            {
                average = overallRating;
            }
            _importedReviews = reviews;
            _importedAverageRating = average;
            ReviewImportSummaryText.Text = average.HasValue
                ? $"Импортировано отзывов: {reviews.Count}. Средняя оценка: {average:0.0} из 5."
                : $"Импортировано отзывов: {reviews.Count}. Оценки в открытом тексте не найдены.";
            AnalyzeReviewsButton.IsEnabled = true;
            ShowReviewAnalysis(focusResults: false);
            ImportedReviewsList.SelectedIndex = 0;
            ImportedReviewsList.Focus();
            Announce(ReviewImportSummaryText.Text);
        }
        catch (Exception)
        {
            Announce("Не удалось прочитать открытую страницу. Проверьте, что Avito загрузился, и повторите импорт.");
        }
        finally
        {
            _reviewImportInProgress = false;
            ImportVisibleReviewsButton.IsEnabled = _reviewBrowserReady;
        }
    }

    private async Task<ReviewCollection> CollectReviewBlocksAsync()
    {
        await AvitoBrowser.ExecuteScriptAsync(ReviewResetScrollScript);
        await Task.Delay(200);

        var blocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? overallRating = null;
        var stablePasses = 0;

        for (var pass = 0; pass < 30 && stablePasses < 3; pass++)
        {
            var json = await AvitoBrowser.ExecuteScriptAsync(ReviewExtractionScript);
            var extraction = JsonSerializer.Deserialize<PageExtraction>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (extraction is null)
            {
                break;
            }

            overallRating ??= extraction.OverallRating;
            var countBefore = blocks.Count;
            foreach (var block in extraction.Blocks)
            {
                blocks.Add(block);
            }

            stablePasses = extraction.EndReached && blocks.Count == countBefore
                ? stablePasses + 1
                : 0;
            await Task.Delay(350);
        }

        return new ReviewCollection(blocks.ToArray(), overallRating);
    }

    private async void ClearBrowserData_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureReviewBrowserIsReadyAsync())
        {
            return;
        }

        try
        {
            await AvitoBrowser.CoreWebView2.Profile.ClearBrowsingDataAsync();
            AvitoBrowser.CoreWebView2.Navigate("about:blank");
            ResetImportedReviews();
            ImportVisibleReviewsButton.IsEnabled = false;
            Announce("Данные браузера удалены. Вы вышли из Avito в МастерFlow.");
        }
        catch (Exception)
        {
            Announce("Не удалось удалить данные браузера. Закройте программу и попробуйте снова.");
        }
    }

    private void AnalyzeReviews_Click(object sender, RoutedEventArgs e)
    {
        if (_importedReviews.Count == 0)
        {
            Announce("Сначала импортируйте отзывы.");
            return;
        }

        ShowReviewAnalysis(focusResults: true);
        Announce("Анализ отзывов готов. Показаны сильные стороны, темы для внимания и рекомендации.");
    }

    private void ShowReviewAnalysis(bool focusResults)
    {
        var analysis = ReviewAnalyzer.Analyze(_importedReviews, _importedAverageRating);
        ReviewAnalysisSummaryText.Text = analysis.Summary;
        ReviewStrengthsList.ItemsSource = analysis.Strengths;
        ReviewAttentionList.ItemsSource = analysis.AttentionAreas;
        ReviewRecommendationsList.ItemsSource = analysis.Recommendations;
        NoStrengthsText.Visibility = analysis.Strengths.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoAttentionAreasText.Visibility = analysis.AttentionAreas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewAnalysisPanel.Visibility = Visibility.Visible;

        if (!focusResults)
        {
            return;
        }

        if (analysis.Strengths.Count > 0)
        {
            ReviewStrengthsList.SelectedIndex = 0;
            FocusFirstAnalysisItem(ReviewStrengthsList);
        }
        else
        {
            ReviewRecommendationsList.SelectedIndex = 0;
            FocusFirstAnalysisItem(ReviewRecommendationsList);
        }
    }

    private void FocusFirstAnalysisItem(ListBox list)
    {
        Dispatcher.BeginInvoke(() =>
        {
            list.UpdateLayout();
            if (list.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem item)
            {
                item.Focus();
            }
            else
            {
                list.Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    private void ResetImportedReviews()
    {
        _importedReviews = [];
        _importedAverageRating = null;
        ImportedReviewsList.ItemsSource = null;
        ReviewImportSummaryText.Text = "Отзывы ещё не импортированы.";
        AnalyzeReviewsButton.IsEnabled = false;
        ReviewAnalysisPanel.Visibility = Visibility.Collapsed;
        ReviewStrengthsList.ItemsSource = null;
        ReviewAttentionList.ItemsSource = null;
        ReviewRecommendationsList.ItemsSource = null;
    }

    private void ClearForm()
    {
        ClientNameTextBox.Clear();
        ClientContactTextBox.Clear();
        ServiceNameTextBox.Clear();
        ClientNotesTextBox.Clear();
        AppointmentDatePicker.SelectedDate = DateTime.Today.AddDays(1);
        AppointmentTimeTextBox.Text = "10:00";
        ReminderComboBox.SelectedIndex = 2;
    }

    private string? LoadWorkspace()
    {
        try
        {
            var result = _workspaceStore.Load();
            _workspace = result.Workspace;
            return result.RecoveredFromBackup
                ? "Основной файл данных был повреждён. Клиенты и записи восстановлены из резервной копии."
                : _workspace.Clients.Count > 0
                    ? $"Локальные данные загружены. Клиентов: {_workspace.Clients.Count}."
                    : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _workspace = new MasterWorkspace();
            return "Не удалось открыть сохранённые данные. Программа начала с пустой базы и не изменяла повреждённый файл.";
        }
    }

    private bool SaveWorkspace(WorkspaceSnapshot snapshotBeforeChange)
    {
        try
        {
            _workspaceStore.Save(_workspace);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or CryptographicException or JsonException)
        {
            _workspace = MasterWorkspace.Restore(snapshotBeforeChange);
            RefreshLists();
            Announce("Не удалось сохранить данные на компьютере. Изменение отменено. Проверьте доступное место и права на папку.");
            return false;
        }
    }

    private void RestoreWorkspaceAfterFailedChange(WorkspaceSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        _workspace = MasterWorkspace.Restore(snapshot);
        RefreshLists();
    }

    private void RefreshLists(Guid? selectedClientId = null, Guid? selectedReminderId = null)
    {
        var appointments = _workspace.GetUpcoming(DateTime.Now);
        UpcomingAppointmentsList.ItemsSource = appointments;
        EmptyAppointmentsText.Visibility = appointments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshReminderList(_workspace.GetClientReminders(DateTime.Now), selectedReminderId);
        RefreshClientList(selectedClientId);
    }

    private void RefreshReminderList(
        IReadOnlyList<ClientReminder> reminders,
        Guid? selectedReminderId = null)
    {
        var previousId = selectedReminderId ?? (ClientRemindersList.SelectedItem as ClientReminder)?.AppointmentId;
        ClientRemindersList.ItemsSource = reminders;
        EmptyRemindersText.Visibility = reminders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (previousId.HasValue)
        {
            ClientRemindersList.SelectedItem = reminders.FirstOrDefault(
                reminder => reminder.AppointmentId == previousId.Value);
        }

        if (ClientRemindersList.SelectedItem is null)
        {
            CopyReminderButton.IsEnabled = false;
            MarkReminderSentButton.IsEnabled = false;
            ResetReminderButton.IsEnabled = false;
        }
    }

    private void RefreshClientList(Guid? selectedClientId = null)
    {
        var previousId = selectedClientId ?? (ClientsList.SelectedItem as ClientRecord)?.Id;
        var clients = _workspace.SearchClients(ClientSearchTextBox.Text);
        ClientsList.ItemsSource = clients;
        EmptyClientsText.Text = _workspace.Clients.Count == 0
            ? "Клиентов пока нет. Клиент появится здесь после создания первой записи."
            : "По вашему запросу клиенты не найдены.";
        EmptyClientsText.Visibility = clients.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (previousId.HasValue)
        {
            ClientsList.SelectedItem = clients.FirstOrDefault(client => client.Id == previousId.Value);
        }

        if (ClientsList.SelectedItem is null)
        {
            ClientCardGroupBox.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowSection(string section)
    {
        TodayPanel.Visibility = section == "Today" ? Visibility.Visible : Visibility.Collapsed;
        ClientsPanel.Visibility = section == "Clients" ? Visibility.Visible : Visibility.Collapsed;
        SchedulePanel.Visibility = section == "Schedule" ? Visibility.Visible : Visibility.Collapsed;
        ReviewsPanel.Visibility = section == "Reviews" ? Visibility.Visible : Visibility.Collapsed;
        ConversationsPanel.Visibility = section == "Conversations" ? Visibility.Visible : Visibility.Collapsed;

        var name = section switch
        {
            "Today" => "Сегодня",
            "Clients" => "Клиенты",
            "Schedule" => "Новая запись",
            "Reviews" => "Отзывы Avito",
            "Conversations" => "Анализ переписки",
            _ => "раздел программы"
        };
        Announce($"Открыт раздел «{name}».");
    }

    private void SelectNavigationItem(string tag)
    {
        var item = FindNavigationItem(NavigationTree.Items, tag);
        if (item is not null)
        {
            item.IsSelected = true;
            item.BringIntoView();
        }
    }

    private static TreeViewItem? FindNavigationItem(ItemCollection items, string tag)
    {
        foreach (var value in items)
        {
            if (value is not TreeViewItem item)
            {
                continue;
            }

            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal))
            {
                return item;
            }

            var nested = FindNavigationItem(item.Items, tag);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void ShowInputError(string message, Control control)
    {
        Announce(message);
        control.Focus();
    }

    private void Announce(string message)
    {
        StatusText.Text = string.Empty;
        Dispatcher.BeginInvoke(() => StatusText.Text = message, DispatcherPriority.Background);
    }

    private async Task InitializeReviewBrowserAsync()
    {
        try
        {
            var browserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MasterFlow",
                "BrowserData");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: browserDataFolder);
            await AvitoBrowser.EnsureCoreWebView2Async(environment);

            AvitoBrowser.CoreWebView2.Profile.IsPasswordAutosaveEnabled = false;
            AvitoBrowser.CoreWebView2.Profile.IsGeneralAutofillEnabled = false;
            await AvitoBrowser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ReviewDetectionScript);
            AvitoBrowser.CoreWebView2.NavigationStarting += ReviewBrowser_NavigationStarting;
            AvitoBrowser.CoreWebView2.NavigationCompleted += ReviewBrowser_NavigationCompleted;
            AvitoBrowser.CoreWebView2.WebMessageReceived += ReviewBrowser_WebMessageReceived;
            AvitoBrowser.CoreWebView2.NewWindowRequested += (_, eventArgs) =>
            {
                eventArgs.Handled = true;
                AvitoBrowser.CoreWebView2.Navigate(eventArgs.Uri);
            };
            _reviewBrowserReady = true;
            _reviewDetectionTimer.Start();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Announce("Для открытия Avito нужен компонент Microsoft Edge WebView2 Runtime.");
        }
        catch (Exception)
        {
            Announce("Не удалось подготовить браузер Avito. Перезапустите программу.");
        }
    }

    private async Task<bool> EnsureReviewBrowserIsReadyAsync()
    {
        if (!_reviewBrowserReady)
        {
            await InitializeReviewBrowserAsync();
        }

        return _reviewBrowserReady;
    }

    private void ReviewBrowser_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        _reviewDialogWasVisible = false;
        if (eventArgs.Uri == "about:blank")
        {
            return;
        }

        if (!AvitoLink.TryCreate(eventArgs.Uri, out _, out _))
        {
            eventArgs.Cancel = true;
            Announce("Переход отменён: встроенный браузер открывает только страницы avito.ru.");
        }
    }

    private void ReviewBrowser_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        ImportVisibleReviewsButton.IsEnabled = eventArgs.IsSuccess;
        Announce(eventArgs.IsSuccess
            ? "Страница Avito загружена. Откройте окно отзывов — импорт начнётся автоматически."
            : "Avito не загрузился. Проверьте интернет или выполните проверку на странице.");
    }

    private async void ReviewBrowser_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        try
        {
            if (eventArgs.TryGetWebMessageAsString() == "masterflow:reviews-visible")
            {
                await ImportReviewsFromOpenPageAsync(isAutomatic: true);
            }
        }
        catch (ArgumentException)
        {
            // Сообщения другого формата не относятся к МастерFlow и игнорируются.
        }
    }

    private async void ReviewDetectionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_reviewBrowserReady || AvitoBrowser.Source is null || _reviewImportInProgress)
        {
            return;
        }

        try
        {
            var result = await AvitoBrowser.ExecuteScriptAsync(ReviewDialogVisibleScript);
            var isVisible = string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
            if (isVisible && !_reviewDialogWasVisible)
            {
                _reviewDialogWasVisible = true;
                await ImportReviewsFromOpenPageAsync(isAutomatic: true);
            }
            else if (!isVisible)
            {
                _reviewDialogWasVisible = false;
            }
        }
        catch (Exception)
        {
            // Страница может меняться во время перехода. Следующая проверка повторится автоматически.
        }
    }

    private const string ReviewDialogVisibleScript = """
        (() => {
            const semanticDialog = [...document.querySelectorAll('[role="dialog"]')]
                .some(dialog => /отзыв/i.test(dialog.innerText || ''));
            if (semanticDialog) return true;
            const text = document.body?.innerText || '';
            return /сначала\s+новые/i.test(text) && /написать\s+отзыв/i.test(text);
        })()
        """;

    private const string ReviewDetectionScript = """
        (() => {
            let messageSent = false;
            const findReviewDialog = () => {
                const semanticDialog = [...document.querySelectorAll('[role="dialog"]')]
                    .find(dialog => /отзыв/i.test(dialog.innerText || ''));
                if (semanticDialog) return semanticDialog;
                const bodyText = document.body?.innerText || '';
                return /сначала\s+новые/i.test(bodyText) && /написать\s+отзыв/i.test(bodyText)
                    ? document.body
                    : null;
            };
            const notify = () => {
                const dialog = findReviewDialog();
                if (dialog && !messageSent) {
                    messageSent = true;
                    window.chrome.webview.postMessage('masterflow:reviews-visible');
                } else if (!dialog) {
                    messageSent = false;
                }
            };
            const startObserver = () => {
                if (!document.documentElement) {
                    window.setTimeout(startObserver, 50);
                    return;
                }
                const observer = new MutationObserver(() => window.setTimeout(notify, 150));
                observer.observe(document.documentElement, { childList: true, subtree: true });
                notify();
            };
            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', startObserver, { once: true });
            } else {
                startObserver();
            }
        })()
        """;

    private const string ReviewExtractionScript = """
        (() => {
            let reviewDialog = [...document.querySelectorAll('[role="dialog"]')]
                .find(dialog => /отзыв/i.test(dialog.innerText || ''));
            if (!reviewDialog) {
                const textContainers = [...document.querySelectorAll('div')]
                    .filter(element => {
                        const text = element.innerText || '';
                        return /сначала\s+новые/i.test(text) && /написать\s+отзыв/i.test(text);
                    })
                    .sort((left, right) => (left.innerText || '').length - (right.innerText || '').length);
                reviewDialog = textContainers[0] || null;
            }
            const scope = reviewDialog || document;
            const selectors = [
                '[data-marker*="review"]',
                '[data-testid*="review"]',
                '[itemprop="review"]',
                'article'
            ];
            const nodes = [...new Set(selectors.flatMap(selector => [...scope.querySelectorAll(selector)]))];
            const blocks = nodes
                .map(node => (node.innerText || '').trim())
                .filter(text => text.length >= 20 && text.length <= 5000);
            const ratingMatch = (scope.innerText || '').match(/(?:^|\s)([1-5][,.]\d)(?=\s)/);
            const scrollable = [scope, ...scope.querySelectorAll('*')]
                .filter(element => element.scrollHeight > element.clientHeight + 40)
                .sort((left, right) => (right.scrollHeight - right.clientHeight) - (left.scrollHeight - left.clientHeight))[0];
            let endReached = true;
            let scrollHeight = 0;
            let scrollTop = 0;
            if (scrollable) {
                scrollHeight = scrollable.scrollHeight;
                scrollTop = scrollable.scrollTop;
                endReached = scrollTop + scrollable.clientHeight >= scrollHeight - 5;
                scrollable.scrollTop = Math.min(scrollHeight, scrollTop + Math.max(240, scrollable.clientHeight * 0.8));
            }
            return {
                url: location.href,
                title: document.title,
                blocks,
                overallRating: ratingMatch ? ratingMatch[1] : null,
                scrollHeight,
                scrollTop,
                endReached
            };
        })()
        """;

    private const string ReviewResetScrollScript = """
        (() => {
            let scope = [...document.querySelectorAll('[role="dialog"]')]
                .find(dialog => /отзыв/i.test(dialog.innerText || ''));
            if (!scope) {
                scope = [...document.querySelectorAll('div')]
                    .filter(element => {
                        const text = element.innerText || '';
                        return /сначала\s+новые/i.test(text) && /написать\s+отзыв/i.test(text);
                    })
                    .sort((left, right) => (left.innerText || '').length - (right.innerText || '').length)[0];
            }
            if (!scope) return false;
            const scrollable = [scope, ...scope.querySelectorAll('*')]
                .filter(element => element.scrollHeight > element.clientHeight + 40)
                .sort((left, right) => (right.scrollHeight - right.clientHeight) - (left.scrollHeight - left.clientHeight))[0];
            if (!scrollable) return false;
            scrollable.scrollTop = 0;
            return true;
        })()
        """;

    private sealed record PageExtraction(
        string Url,
        string Title,
        string[] Blocks,
        string? OverallRating,
        int ScrollHeight,
        int ScrollTop,
        bool EndReached);

    private sealed record ReviewCollection(string[] Blocks, string? OverallRating);
}
