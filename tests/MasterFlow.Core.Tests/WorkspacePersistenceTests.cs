using System.Text;
using MasterFlow.Core;

namespace MasterFlow.Core.Tests;

public sealed class WorkspacePersistenceTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 10, 0, 0);

    [Fact]
    public void SaveAndLoad_RestoresClientsAndAppointmentsWithoutPlainTextContact()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "workspace.dat");
        var store = new FileWorkspaceStore(path, new XorProtector());
        var workspace = CreateWorkspace("test-contact-42");

        store.Save(workspace);
        var result = store.Load();

        var client = Assert.Single(result.Workspace.Clients);
        var appointment = Assert.Single(result.Workspace.Appointments);
        Assert.Equal("test-contact-42", client.Contact);
        Assert.Equal(client.Id, appointment.ClientId);
        Assert.False(result.RecoveredFromBackup);
        Assert.DoesNotContain("test-contact-42", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void Load_UsesBackupWhenPrimaryFileIsDamaged()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "workspace.dat");
        var store = new FileWorkspaceStore(path, new XorProtector());
        store.Save(CreateWorkspace("first-contact"));
        store.Save(CreateWorkspace("second-contact"));
        File.WriteAllText(path, "damaged");

        var result = store.Load();

        Assert.True(result.RecoveredFromBackup);
        Assert.Equal("first-contact", Assert.Single(result.Workspace.Clients).Contact);
        Assert.False(store.Load().RecoveredFromBackup);
    }

    [Fact]
    public void UpdateClient_UpdatesAppointmentNameAndSearchableDetails()
    {
        var workspace = CreateWorkspace("old-contact");
        var client = Assert.Single(workspace.Clients);

        var updated = workspace.UpdateClient(client.Id, "Ольга П.", "new-contact", "Рекомендация", "Предпочитает утро");

        Assert.Equal("Ольга П.", updated.Name);
        Assert.Equal("Ольга П.", Assert.Single(workspace.Appointments).ClientName);
        Assert.Same(updated, Assert.Single(workspace.SearchClients("утро")));
    }

    [Fact]
    public void DeleteClient_RemovesClientAndVisitHistory()
    {
        var workspace = CreateWorkspace("test-contact");
        var client = Assert.Single(workspace.Clients);

        var removed = workspace.DeleteClient(client.Id);

        Assert.True(removed);
        Assert.Empty(workspace.Clients);
        Assert.Empty(workspace.Appointments);
    }

    [Fact]
    public void UpdateClient_RejectsDuplicateContactWithoutChangingClient()
    {
        var workspace = new MasterWorkspace();
        var first = workspace.AddClient("Ольга", "contact-one");
        workspace.AddClient("Ирина", "contact-two");

        var error = Assert.Throws<InvalidOperationException>(() =>
            workspace.UpdateClient(first.Id, "Ольга новая", "contact-two", "Avito", ""));

        Assert.Equal("Клиент с таким контактом уже существует.", error.Message);
        Assert.Equal("Ольга", workspace.Clients.Single(client => client.Id == first.Id).Name);
        Assert.Equal("contact-one", workspace.Clients.Single(client => client.Id == first.Id).Contact);
    }

    [Fact]
    public void SaveAndLoad_PreservesSentReminderStatus()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "workspace.dat");
        var store = new FileWorkspaceStore(path, new XorProtector());
        var workspace = CreateWorkspace("reminder-contact");
        var appointment = Assert.Single(workspace.Appointments);
        var sentAt = Now.AddMinutes(15);
        workspace.MarkReminderSent(appointment.Id, sentAt);

        store.Save(workspace);
        var restored = store.Load().Workspace;

        Assert.Equal(sentAt, Assert.Single(restored.Appointments).ReminderSentAt);
        Assert.Equal(ReminderState.Sent, Assert.Single(restored.GetClientReminders(Now.AddMinutes(20))).State);
    }

    private static MasterWorkspace CreateWorkspace(string contact)
    {
        var workspace = new MasterWorkspace();
        var client = workspace.AddClient("Ольга", contact, "Avito", "Первое обращение");
        workspace.AddAppointment(
            client,
            "Консультация",
            Now.AddDays(1),
            TimeSpan.FromHours(2),
            Now);
        return workspace;
    }

    private sealed class XorProtector : IWorkspaceProtector
    {
        public byte[] Protect(byte[] data) => Transform(data);

        public byte[] Unprotect(byte[] data) => Transform(data);

        private static byte[] Transform(byte[] data) => data.Select(value => (byte)(value ^ 0xA5)).ToArray();
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MasterFlow.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
