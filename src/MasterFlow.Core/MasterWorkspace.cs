namespace MasterFlow.Core;

public sealed class MasterWorkspace
{
    private readonly List<ClientRecord> _clients = [];
    private readonly List<Appointment> _appointments = [];

    public IReadOnlyList<ClientRecord> Clients => _clients;
    public IReadOnlyList<Appointment> Appointments => _appointments.OrderBy(item => item.StartsAt).ToList();

    public ClientRecord AddClient(string name, string contact, string source = "Avito", string notes = "")
    {
        var existing = _clients.FirstOrDefault(client =>
            string.Equals(client.Contact, contact.Trim(), StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            return existing;
        }

        var client = ClientRecord.Create(name, contact, source, notes);
        _clients.Add(client);
        return client;
    }

    public ClientRecord UpdateClient(Guid clientId, string name, string contact, string source, string notes)
    {
        var index = _clients.FindIndex(client => client.Id == clientId);
        if (index < 0)
        {
            throw new InvalidOperationException("Клиент не найден.");
        }

        var normalizedContact = contact.Trim();
        if (_clients.Any(client =>
                client.Id != clientId &&
                string.Equals(client.Contact, normalizedContact, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Клиент с таким контактом уже существует.");
        }

        var updated = _clients[index].Update(name, contact, source, notes);
        _clients[index] = updated;
        for (var appointmentIndex = 0; appointmentIndex < _appointments.Count; appointmentIndex++)
        {
            var appointment = _appointments[appointmentIndex];
            if (appointment.ClientId == clientId)
            {
                _appointments[appointmentIndex] = appointment with { ClientName = updated.Name };
            }
        }

        return updated;
    }

    public bool DeleteClient(Guid clientId)
    {
        var removed = _clients.RemoveAll(client => client.Id == clientId) > 0;
        if (removed)
        {
            _appointments.RemoveAll(appointment => appointment.ClientId == clientId);
        }

        return removed;
    }

    public IReadOnlyList<ClientRecord> SearchClients(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return _clients.OrderBy(client => client.Name).ToList();
        }

        var value = query.Trim();
        return _clients
            .Where(client =>
                client.Name.Contains(value, StringComparison.CurrentCultureIgnoreCase) ||
                client.Contact.Contains(value, StringComparison.CurrentCultureIgnoreCase) ||
                client.Notes.Contains(value, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(client => client.Name)
            .ToList();
    }

    public Appointment AddAppointment(
        ClientRecord client,
        string serviceName,
        DateTime startsAt,
        TimeSpan reminderBefore,
        DateTime now)
    {
        if (_appointments.Any(item => item.StartsAt == startsAt))
        {
            throw new InvalidOperationException("На это время уже есть запись. Выберите другое время.");
        }

        var appointment = Appointment.Create(client, serviceName, startsAt, reminderBefore, now);
        _appointments.Add(appointment);
        return appointment;
    }

    public IReadOnlyList<Appointment> GetUpcoming(DateTime now) =>
        _appointments.Where(item => item.StartsAt > now).OrderBy(item => item.StartsAt).ToList();

    public IReadOnlyList<Appointment> GetClientAppointments(Guid clientId) =>
        _appointments
            .Where(item => item.ClientId == clientId)
            .OrderByDescending(item => item.StartsAt)
            .ToList();

    public IReadOnlyList<ClientReminder> GetClientReminders(DateTime now) =>
        _appointments
            .Where(appointment => !appointment.ReminderSentAt.HasValue || appointment.StartsAt > now)
            .Select(appointment =>
            {
                var client = _clients.Single(item => item.Id == appointment.ClientId);
                var state = appointment.ReminderSentAt.HasValue
                    ? ReminderState.Sent
                    : appointment.StartsAt <= now
                        ? ReminderState.Missed
                        : appointment.ReminderAt <= now
                            ? ReminderState.Due
                            : ReminderState.Scheduled;
                return new ClientReminder(
                    appointment.Id,
                    appointment.ClientId,
                    appointment.ClientName,
                    client.Contact,
                    appointment.ServiceName,
                    appointment.StartsAt,
                    appointment.ReminderAt,
                    appointment.ReminderSentAt,
                    state);
            })
            .OrderBy(reminder => reminder.State switch
            {
                ReminderState.Due => 0,
                ReminderState.Missed => 1,
                ReminderState.Scheduled => 2,
                ReminderState.Sent => 3,
                _ => 4
            })
            .ThenBy(reminder => reminder.ReminderAt)
            .ToList();

    public Appointment MarkReminderSent(Guid appointmentId, DateTime sentAt)
    {
        var index = _appointments.FindIndex(appointment => appointment.Id == appointmentId);
        if (index < 0)
        {
            throw new InvalidOperationException("Запись для напоминания не найдена.");
        }

        var updated = _appointments[index] with { ReminderSentAt = sentAt };
        _appointments[index] = updated;
        return updated;
    }

    public Appointment ResetReminder(Guid appointmentId)
    {
        var index = _appointments.FindIndex(appointment => appointment.Id == appointmentId);
        if (index < 0)
        {
            throw new InvalidOperationException("Запись для напоминания не найдена.");
        }

        var updated = _appointments[index] with { ReminderSentAt = null };
        _appointments[index] = updated;
        return updated;
    }

    public WorkspaceSnapshot CreateSnapshot() =>
        new(WorkspaceSnapshot.CurrentVersion, _clients.ToArray(), _appointments.ToArray());

    public static MasterWorkspace Restore(WorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Version != WorkspaceSnapshot.CurrentVersion)
        {
            throw new InvalidDataException("Версия сохранённых данных не поддерживается.");
        }

        var workspace = new MasterWorkspace();
        foreach (var client in snapshot.Clients)
        {
            if (workspace._clients.Any(existing => existing.Id == client.Id))
            {
                throw new InvalidDataException("В сохранённых данных найден повтор клиента.");
            }

            workspace._clients.Add(ClientRecord.Restore(
                client.Id,
                client.Name,
                client.Contact,
                client.Source,
                client.Notes));
        }

        foreach (var appointment in snapshot.Appointments)
        {
            var client = workspace._clients.SingleOrDefault(item => item.Id == appointment.ClientId)
                ?? throw new InvalidDataException("Запись ссылается на отсутствующего клиента.");
            if (workspace._appointments.Any(existing => existing.Id == appointment.Id))
            {
                throw new InvalidDataException("В сохранённых данных найдена повторная запись.");
            }

            workspace._appointments.Add(Appointment.Restore(
                appointment.Id,
                appointment.ClientId,
                client.Name,
                appointment.ServiceName,
                appointment.StartsAt,
                appointment.ReminderBefore,
                appointment.ReminderSentAt));
        }

        return workspace;
    }
}
