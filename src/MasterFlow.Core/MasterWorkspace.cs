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
}
