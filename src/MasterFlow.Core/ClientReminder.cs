using System.Globalization;

namespace MasterFlow.Core;

public enum ReminderState
{
    Due,
    Scheduled,
    Sent,
    Missed
}

public sealed record ClientReminder(
    Guid AppointmentId,
    Guid ClientId,
    string ClientName,
    string Contact,
    string ServiceName,
    DateTime StartsAt,
    DateTime ReminderAt,
    DateTime? SentAt,
    ReminderState State)
{
    public string Message =>
        $"Здравствуйте, {ClientName}! Напоминаю: {ServiceName} — " +
        $"{StartsAt.ToString("d MMMM", CultureInfo.CurrentCulture)} в {StartsAt:HH:mm}. " +
        "Если планы изменились, пожалуйста, сообщите.";

    public string StatusText => State switch
    {
        ReminderState.Due => "Пора отправить",
        ReminderState.Scheduled => $"Отправить {ReminderAt:dddd, d MMMM, HH:mm}",
        ReminderState.Sent => $"Отправлено {SentAt:dddd, d MMMM, HH:mm}",
        ReminderState.Missed => "Сеанс уже прошёл, напоминание не отмечено",
        _ => "Статус неизвестен"
    };

    public string AccessibleSummary =>
        $"{StatusText}. {ClientName}. Контакт: {Contact}. {ServiceName}. Сеанс {StartsAt:dddd, d MMMM, HH:mm}.";
}
