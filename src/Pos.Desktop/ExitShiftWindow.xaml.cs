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

    public ExitShiftWindow() => InitializeComponent();

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
