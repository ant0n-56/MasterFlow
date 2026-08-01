using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MasterFlow.Core;

namespace MasterFlow.App;

public partial class MainWindow : Window
{
    private readonly MasterWorkspace _workspace = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppointmentDatePicker.SelectedDate = DateTime.Today.AddDays(1);
        AppointmentTimeTextBox.Text = "10:00";
        RefreshLists();
        TodayNavigationItem.IsSelected = true;
        TodayNavigationItem.Focus();
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
        Announce("Список ближайших записей обновлён.");
    }

    private void SaveAppointment_Click(object sender, RoutedEventArgs e)
    {
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

    private void RefreshLists()
    {
        var appointments = _workspace.GetUpcoming(DateTime.Now);
        UpcomingAppointmentsList.ItemsSource = appointments;
        ClientsList.ItemsSource = _workspace.Clients;
        EmptyAppointmentsText.Visibility = appointments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyClientsText.Visibility = _workspace.Clients.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
}
