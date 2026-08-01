namespace MasterFlow.Core;

public sealed record ClientRecord(
    Guid Id,
    string Name,
    string Contact,
    string Source,
    string Notes)
{
    public string AccessibleSummary => $"{Name}. Контакт: {Contact}. Источник: {Source}.";

    public static ClientRecord Create(string name, string contact, string source = "Avito", string notes = "")
        => Restore(Guid.NewGuid(), name, contact, source, notes);

    public ClientRecord Update(string name, string contact, string source, string notes) =>
        Restore(Id, name, contact, source, notes);

    internal static ClientRecord Restore(Guid id, string name, string contact, string source, string notes)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Не удалось восстановить идентификатор клиента.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Укажите имя клиента.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(contact))
        {
            throw new ArgumentException("Укажите контакт клиента.", nameof(contact));
        }

        return new ClientRecord(
            id,
            name.Trim(),
            contact.Trim(),
            string.IsNullOrWhiteSpace(source) ? "Не указан" : source.Trim(),
            notes.Trim());
    }
}
