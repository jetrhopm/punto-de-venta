using MahApps.Metro.IconPacks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pos.Desktop;

public enum OperationResultKind { Success, Warning, Error, Information }

public partial class OperationResultWindow : Window
{
    private readonly TimeSpan? _autoCloseAfter;
    private DispatcherTimer? _autoCloseTimer;

    public OperationResultWindow(string title, string message, OperationResultKind kind, TimeSpan? autoCloseAfter = null)
    {
        InitializeComponent();
        _autoCloseAfter = autoCloseAfter;
        Title = title;
        TitleText.Text = title;
        SubtitleText.Text = kind switch { OperationResultKind.Success => "Operación completada", OperationResultKind.Warning => "Revisa la información", OperationResultKind.Error => "No se pudo completar", _ => "Información de JetVenta" };
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
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_autoCloseAfter is null) return;
        _autoCloseTimer = new DispatcherTimer { Interval = _autoCloseAfter.Value };
        _autoCloseTimer.Tick += (_, _) => Close();
        _autoCloseTimer.Start();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer?.Stop();
        base.OnClosed(e);
    }
}
