namespace MasterFlow.Core;

public sealed record CloudTextPreparation(string Text, int RedactionCount)
{
    public string Summary => RedactionCount == 0
        ? "Явные телефоны, email и ссылки не найдены. Проверьте имена и другие личные сведения вручную."
        : $"Скрыто фрагментов с телефонами, email или ссылками: {RedactionCount}. Проверьте имена и другие личные сведения вручную.";
}
