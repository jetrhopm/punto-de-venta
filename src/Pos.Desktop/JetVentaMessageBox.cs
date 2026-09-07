using System.Windows;

namespace Pos.Desktop;

// Keeps legacy call sites compatible while rendering JetVenta's dialogs instead of Windows MessageBox.
public static class MessageBox
{
    public static MessageBoxResult Show(string message) => Show(message, "JetVenta", MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string message, string caption) => Show(message, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string message, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive);
        var kind = icon switch
        {
            MessageBoxImage.Error or MessageBoxImage.Stop or MessageBoxImage.Hand => OperationResultKind.Error,
            MessageBoxImage.Warning or MessageBoxImage.Exclamation => OperationResultKind.Warning,
            MessageBoxImage.Question => OperationResultKind.Information,
            _ => OperationResultKind.Information
        };

        if (button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel or MessageBoxButton.OKCancel)
        {
            var confirmation = new OperationConfirmationWindow(caption, message, kind, button);
            if (owner is not null) confirmation.Owner = owner;
            confirmation.ShowDialog();
            return confirmation.Result;
        }

        var result = new OperationResultWindow(caption, message, kind);
        if (owner is not null) result.Owner = owner;
        result.ShowDialog();
        return MessageBoxResult.OK;
    }
}
