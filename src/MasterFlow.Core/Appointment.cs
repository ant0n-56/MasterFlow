namespace MasterFlow.Core;

public sealed record Appointment(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string ServiceName,
    DateTime StartsAt,
    TimeSpan ReminderBefore,
    DateTime? ReminderSentAt = null)
{
    public DateTime ReminderAt => StartsAt - ReminderBefore;

    public string AccessibleSummary =>
        $"{ClientName}. {ServiceName}. {StartsAt:dddd, d MMMM, HH:mm}. " +
        (ReminderSentAt.HasValue
            ? $"Напоминание отправлено {ReminderSentAt.Value:dddd, d MMMM, HH:mm}."
            : $"Напомнить за {FormatReminder(ReminderBefore)}.");

    public static Appointment Create(
        ClientRecord client,
        string serviceName,
        DateTime startsAt,
        TimeSpan reminderBefore,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Укажите услугу.", nameof(serviceName));
        }

        if (startsAt <= now)
        {
            throw new ArgumentException("Выберите будущие дату и время сеанса.", nameof(startsAt));
        }

        if (reminderBefore < TimeSpan.Zero)
        {
            throw new ArgumentException("Время напоминания не может быть отрицательным.", nameof(reminderBefore));
        }

        if (reminderBefore >= startsAt - now)
        {
            throw new ArgumentException("Напоминание должно быть позже текущего времени и раньше сеанса.", nameof(reminderBefore));
        }

        return Restore(
            Guid.NewGuid(),
            client.Id,
            client.Name,
            serviceName.Trim(),
            startsAt,
            reminderBefore);
    }

    internal static Appointment Restore(
        Guid id,
        Guid clientId,
        string clientName,
        string serviceName,
        DateTime startsAt,
        TimeSpan reminderBefore,
        DateTime? reminderSentAt = null)
    {
        if (id == Guid.Empty || clientId == Guid.Empty)
        {
            throw new ArgumentException("Не удалось восстановить идентификатор записи.");
        }

        if (string.IsNullOrWhiteSpace(clientName) || string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("В сохранённой записи не хватает имени клиента или услуги.");
        }

        if (reminderBefore < TimeSpan.Zero)
        {
            throw new ArgumentException("Время напоминания не может быть отрицательным.", nameof(reminderBefore));
        }

        return new Appointment(
            id,
            clientId,
            clientName.Trim(),
            serviceName.Trim(),
            startsAt,
            reminderBefore,
            reminderSentAt);
    }

    private static string FormatReminder(TimeSpan value)
    {
        if (value.TotalMinutes == 0)
        {
            return "0 минут";
        }

        if (value.TotalHours >= 1 && value.TotalMinutes % 60 == 0)
        {
            return value.TotalHours switch
            {
                1 => "1 час",
                2 or 3 or 4 => $"{value.TotalHours:0} часа",
                _ => $"{value.TotalHours:0} часов"
            };
        }

        return $"{value.TotalMinutes:0} минут";
    }
}
