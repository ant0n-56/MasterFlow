namespace MasterFlow.Core;

public sealed record WorkspaceSnapshot(
    int Version,
    IReadOnlyList<ClientRecord> Clients,
    IReadOnlyList<Appointment> Appointments)
{
    public const int CurrentVersion = 1;
}
