using MahApps.Metro.IconPacks;
using System.Windows;
using System.Windows.Media;

namespace Pos.Desktop;

public partial class OperationConfirmationWindow : Window
{
    private readonly MessageBoxButton _buttons;
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.Cancel;

    public OperationConfirmationWindow(string title, string message, OperationResultKind kind, MessageBoxButton buttons)
    {
        InitializeComponent();
        _buttons = buttons;
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        var (background, border, foreground, icon) = kind switch
        {
            OperationResultKind.Success => ("#EAF7EF", "#A7D8BA", "#16834A", PackIconMaterialKind.CheckCircleOutline),
            OperationResultKind.Warning => ("#FFF4DE", "#E7C57C", "#A96300", PackIconMaterialKind.AlertOutline),
            OperationResultKind.Error => ("#FBE8E6", "#E8BBB7", "#C33B32", PackIconMaterialKind.AlertCircleOutline),
            _ => ("#DCEEF8", "#A9CCE3", "#156EA8", PackIconMaterialKind.InformationOutline)
        };
        IconPanel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background));
        IconPanel.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));
        ResultIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(foreground));
        ResultIcon.Kind = icon;

        if (buttons == MessageBoxButton.YesNo)
        {
            ConfirmButton.Content = "Sí, continuar";
            CancelButton.Content = "No";
        }
        else if (buttons == MessageBoxButton.YesNoCancel)
        {
            ConfirmButton.Content = "Sí, continuar";
            CancelButton.Content = "Cancelar";
        }
        else
        {
            ConfirmButton.Content = "Continuar";
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        Result = _buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = _buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
        DialogResult = false;
    }
}
