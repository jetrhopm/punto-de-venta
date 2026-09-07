using MahApps.Metro.IconPacks;
using System.Windows;
using System.Windows.Media;

namespace Pos.Desktop;

public partial class JetVentaAlertWindow : Window
{
    private readonly MessageBoxButton _buttons;

    public JetVentaAlertWindow(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        _buttons = buttons;
        Title = string.IsNullOrWhiteSpace(title) ? "JetVenta" : title;
        TitleText.Text = Title;
        MessageText.Text = message ?? string.Empty;
        ApplySeverity(image);
        ConfigureButtons();
    }

    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    private void ApplySeverity(MessageBoxImage image)
    {
        var (title, icon, foreground, background, border) = image switch
        {
            MessageBoxImage.Error => ("No se pudo completar la acción", PackIconMaterialKind.AlertCircleOutline, "#B42318", "#FDECEA", "#E9AAA4"),
            MessageBoxImage.Warning => ("Revisa esta información antes de continuar", PackIconMaterialKind.AlertOutline, "#9A5B00", "#FFF5DF", "#E8C06F"),
            MessageBoxImage.Question => ("Se requiere confirmación", PackIconMaterialKind.HelpCircleOutline, "#176894", "#E7F3FA", "#9ECBE0"),
            _ => ("Información de JetVenta", PackIconMaterialKind.InformationOutline, "#156EA8", "#E8F3F9", "#A9CDE1")
        };

        SeverityText.Text = title;
        SeverityText.Foreground = Brush(foreground);
        SeverityIcon.Kind = icon;
        SeverityIcon.Foreground = Brush(foreground);
        IconSurface.Background = Brush(background);
        MessageSurface.Background = Brush(background);
        MessageSurface.BorderBrush = Brush(border);
    }

    private void ConfigureButtons()
    {
        CancelButton.Visibility = Visibility.Collapsed;
        DeclineButton.Visibility = Visibility.Collapsed;
        AcceptButton.IsDefault = true;

        switch (_buttons)
        {
            case MessageBoxButton.OK:
                AcceptButton.Content = "Aceptar";
                HintText.Text = "Enter para aceptar";
                break;
            case MessageBoxButton.OKCancel:
                AcceptButton.Content = "Aceptar";
                CancelButton.Content = "Cancelar";
                CancelButton.Visibility = Visibility.Visible;
                CancelButton.IsCancel = true;
                HintText.Text = "Enter para aceptar · Esc para cancelar";
                break;
            case MessageBoxButton.YesNo:
                AcceptButton.Content = "Sí, continuar";
                DeclineButton.Content = "No";
                DeclineButton.Visibility = Visibility.Visible;
                DeclineButton.IsCancel = true;
                HintText.Text = "Enter para confirmar · Esc para volver";
                break;
            case MessageBoxButton.YesNoCancel:
                AcceptButton.Content = "Sí, continuar";
                DeclineButton.Content = "No";
                DeclineButton.Visibility = Visibility.Visible;
                CancelButton.Content = "Cancelar";
                CancelButton.Visibility = Visibility.Visible;
                CancelButton.IsCancel = true;
                HintText.Text = "Enter para confirmar · Esc para cancelar";
                break;
        }
    }

    private void OnAcceptClick(object sender, RoutedEventArgs e) => Complete(_buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK);
    private void OnDeclineClick(object sender, RoutedEventArgs e) => Complete(MessageBoxResult.No);
    private void OnCancelClick(object sender, RoutedEventArgs e) => Complete(MessageBoxResult.Cancel);

    private void Complete(MessageBoxResult result)
    {
        Result = result;
        DialogResult = true;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (Result != MessageBoxResult.None) return;
        Result = _buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.No : MessageBoxResult.Cancel;
    }

    private static Brush Brush(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
}

public static class MessageBox
{
    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        var alert = new JetVentaAlertWindow(messageBoxText, caption, button, icon);
        var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? System.Windows.Application.Current?.MainWindow;
        if (owner is { IsVisible: true } && owner != alert) alert.Owner = owner;
        alert.ShowDialog();
        return alert.Result;
    }
}
