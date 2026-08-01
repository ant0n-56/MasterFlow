using System.Windows;

namespace MasterFlow.App;

public partial class ConfirmDeleteDialog : Window
{
    public ConfirmDeleteDialog(string clientName, int appointmentCount)
    {
        InitializeComponent();
        ConfirmationMessageText.Text =
            $"Удалить клиента «{clientName}» и все его записи? Количество записей: {appointmentCount}.";
    }

    private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
