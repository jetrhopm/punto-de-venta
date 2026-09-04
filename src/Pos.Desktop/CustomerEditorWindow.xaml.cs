using System.Windows;

namespace Pos.Desktop;

public partial class CustomerEditorWindow : Window
{
    public CustomerEditorWindow(CustomerView? customer)
    {
        InitializeComponent();
        if (customer is null)
        {
            Loaded += (_, _) => NameBox.Focus();
            return;
        }

        Title = "Editar cliente";
        TitleText.Text = "Editar cliente";
        DescriptionText.Text = "Actualiza sus datos generales. El crédito se administra por separado.";
        SaveButtonText.Text = "Guardar cambios";
        NameBox.Text = customer.Name;
        PhoneBox.Text = customer.Phone ?? string.Empty;
        EmailBox.Text = customer.Email ?? string.Empty;
        TaxIdBox.Text = customer.TaxId ?? string.Empty;
        Loaded += (_, _) => NameBox.Focus();
    }

    public CustomerEditorResult? Result { get; private set; }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Escribe el nombre del cliente.", "Datos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }

        Result = new CustomerEditorResult(NameBox.Text.Trim(), Clean(PhoneBox.Text), Clean(EmailBox.Text), Clean(TaxIdBox.Text));
        DialogResult = true;
    }

    private static string? Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
