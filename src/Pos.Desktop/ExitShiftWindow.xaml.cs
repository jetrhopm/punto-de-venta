using System.Windows;

namespace Pos.Desktop;

public enum ExitShiftDecision
{
    Cancel,
    LeaveOpenAndExit,
    CloseShiftAndExit,
    CloseShiftAndSignOut
}

public partial class ExitShiftWindow : Window
{
    public ExitShiftDecision Decision { get; private set; } = ExitShiftDecision.Cancel;

    public ExitShiftWindow()
    {
        InitializeComponent();
    }

    public void ConfigureForOtherUser(string registerName, string userName)
    {
        HeadingText.Text = "La caja está abierta por otro usuario";
        DescriptionText.Text = $"{userName} mantiene abierta la caja {registerName}.";
        CloseShiftAndSignOutButton.Visibility = Visibility.Collapsed;
        CloseShiftAndExitButton.Visibility = Visibility.Collapsed;
        LeaveOpenAndExitButton.Content = "Salir sin modificar el turno de caja";
        FooterText.Text = "Solo el usuario que abrió el turno puede continuar ventas o realizar el corte. Puedes salir sin alterar el efectivo.";
    }

    private void OnCloseShiftAndExitClick(object sender, RoutedEventArgs e)
    {
        Decision = ExitShiftDecision.CloseShiftAndExit;
        DialogResult = true;
    }

    private void OnCloseShiftAndSignOutClick(object sender, RoutedEventArgs e)
    {
        Decision = ExitShiftDecision.CloseShiftAndSignOut;
        DialogResult = true;
    }

    private void OnLeaveOpenAndExitClick(object sender, RoutedEventArgs e)
    {
        Decision = ExitShiftDecision.LeaveOpenAndExit;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Decision = ExitShiftDecision.Cancel;
        DialogResult = false;
    }
}
