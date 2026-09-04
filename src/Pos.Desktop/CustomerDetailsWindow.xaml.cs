using System.Windows;

namespace Pos.Desktop;

public partial class CustomerDetailsWindow : Window
{
    public CustomerDetailsWindow(CustomerView customer)
    {
        InitializeComponent();
        NameText.Text = customer.Name;
        PhoneText.Text = string.IsNullOrWhiteSpace(customer.Phone) ? "Sin teléfono" : customer.Phone;
        EmailText.Text = string.IsNullOrWhiteSpace(customer.Email) ? "Sin correo" : customer.Email;
        TaxIdText.Text = string.IsNullOrWhiteSpace(customer.TaxId) ? "No registrado" : customer.TaxId;
        StateText.Text = customer.IsActive ? "Activo" : "Inactivo";
        CreditText.Text = customer.CreditEnabled ? "Habilitado" : "No habilitado";
        LimitText.Text = customer.CreditLimit.ToString("C2");
        BalanceText.Text = customer.Balance.ToString("C2");
        DeactivateButton.IsEnabled = customer.IsActive && customer.Balance <= 0m;
        DeactivateButton.ToolTip = customer.Balance > 0m ? "No se puede desactivar mientras tenga saldo pendiente." : "Desactivar cliente";
    }

    public CustomerDetailsAction Action { get; private set; }

    private void OnEditClick(object sender, RoutedEventArgs e) => CloseWith(CustomerDetailsAction.Edit);
    private void OnManageCreditClick(object sender, RoutedEventArgs e) => CloseWith(CustomerDetailsAction.ManageCredit);
    private void OnDeactivateClick(object sender, RoutedEventArgs e) => CloseWith(CustomerDetailsAction.Deactivate);

    private void CloseWith(CustomerDetailsAction action)
    {
        Action = action;
        DialogResult = true;
    }
}
