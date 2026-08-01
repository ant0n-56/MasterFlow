using MasterFlow.Core;

namespace MasterFlow.Core.Tests;

public sealed class AppointmentTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 10, 0, 0);

    [Fact]
    public void Create_CalculatesReminderTime()
    {
        var client = ClientRecord.Create("Анна", "+7 900 000-00-00");

        var appointment = Appointment.Create(
            client,
            "Массаж спины",
            new DateTime(2026, 8, 2, 15, 30, 0),
            TimeSpan.FromHours(2),
            Now);

        Assert.Equal(new DateTime(2026, 8, 2, 13, 30, 0), appointment.ReminderAt);
        Assert.Contains("Напомнить за 2 часа", appointment.AccessibleSummary);
    }

    [Fact]
    public void Create_RejectsPastAppointment()
    {
        var client = ClientRecord.Create("Анна", "avito-chat-1");

        var error = Assert.Throws<ArgumentException>(() => Appointment.Create(
            client,
            "Массаж",
            Now.AddMinutes(-1),
            TimeSpan.FromHours(2),
            Now));

        Assert.Equal("Выберите будущие дату и время сеанса. (Parameter 'startsAt')", error.Message);
    }

    [Fact]
    public void AddAppointment_RejectsBusyTime()
    {
        var workspace = new MasterWorkspace();
        var first = workspace.AddClient("Анна", "avito-chat-1");
        var second = workspace.AddClient("Ирина", "avito-chat-2");
        var start = Now.AddDays(1);
        workspace.AddAppointment(first, "Массаж", start, TimeSpan.FromHours(2), Now);

        var error = Assert.Throws<InvalidOperationException>(() =>
            workspace.AddAppointment(second, "Массаж", start, TimeSpan.FromHours(1), Now));

        Assert.Equal("На это время уже есть запись. Выберите другое время.", error.Message);
    }

    [Fact]
    public void AddClient_ReturnsExistingClientForSameContact()
    {
        var workspace = new MasterWorkspace();
        var first = workspace.AddClient("Анна", "avito-chat-1");
        var second = workspace.AddClient("Анна П.", "AVITO-CHAT-1");

        Assert.Same(first, second);
        Assert.Single(workspace.Clients);
    }

    [Fact]
    public void ClientAccessibleSummary_ContainsOnlyUsefulInformation()
    {
        var client = ClientRecord.Create("Анна", "avito-chat-1");

        Assert.Equal("Анна. Контакт: avito-chat-1. Источник: Avito.", client.AccessibleSummary);
        Assert.DoesNotContain("Guid", client.AccessibleSummary);
    }

    [Fact]
    public void GetClientReminders_PrioritizesDueReminderAndBuildsClearMessage()
    {
        var workspace = new MasterWorkspace();
        var client = workspace.AddClient("Анна", "avito-chat-1");
        workspace.AddAppointment(
            client,
            "Массаж спины",
            Now.AddHours(1),
            TimeSpan.FromHours(2),
            Now.AddDays(-1));

        var reminder = Assert.Single(workspace.GetClientReminders(Now));

        Assert.Equal(ReminderState.Due, reminder.State);
        Assert.StartsWith("Здравствуйте, Анна!", reminder.Message);
        Assert.Contains("Массаж спины", reminder.Message);
        Assert.Contains("11:00", reminder.Message);
        Assert.Contains("Контакт: avito-chat-1", reminder.AccessibleSummary);
    }

    [Fact]
    public void MarkAndResetReminder_ChangeVisibleStatus()
    {
        var workspace = new MasterWorkspace();
        var client = workspace.AddClient("Анна", "avito-chat-1");
        var appointment = workspace.AddAppointment(
            client,
            "Массаж",
            Now.AddDays(1),
            TimeSpan.FromHours(2),
            Now);

        workspace.MarkReminderSent(appointment.Id, Now.AddMinutes(5));

        var sent = Assert.Single(workspace.GetClientReminders(Now.AddMinutes(10)));
        Assert.Equal(ReminderState.Sent, sent.State);
        Assert.Equal(Now.AddMinutes(5), sent.SentAt);

        workspace.ResetReminder(appointment.Id);

        var scheduled = Assert.Single(workspace.GetClientReminders(Now.AddMinutes(10)));
        Assert.Equal(ReminderState.Scheduled, scheduled.State);
        Assert.Null(scheduled.SentAt);
    }
}
