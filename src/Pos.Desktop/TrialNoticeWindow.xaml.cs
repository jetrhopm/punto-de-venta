using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Pos.Desktop;

public partial class TrialNoticeWindow : Window
{
    private const int RequiredAcknowledgementSeconds = 30;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _remainingSeconds = RequiredAcknowledgementSeconds;
    private bool _mayClose;

    public TrialNoticeWindow(DateTimeOffset expiresAtUtc)
    {
        InitializeComponent();
        RemainingText.Text = $"Tu demo termina el {expiresAtUtc.LocalDateTime:dd/MM/yyyy HH:mm}.";
        ExpirationText.Text = "Este aviso aparece al abrir JetVenta mientras no exista una licencia válida.";
        _timer.Tick += OnTimerTick;
        Loaded += (_, _) => UpdateCountdown();
        Closed += (_, _) => _timer.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _remainingSeconds = Math.Max(0, _remainingSeconds - 1);
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        WaitProgress.Value = RequiredAcknowledgementSeconds - _remainingSeconds;
        if (_remainingSeconds > 0)
        {
            WaitText.Text = $"Podrás continuar en {_remainingSeconds} s";
            if (!_timer.IsEnabled) _timer.Start();
            return;
        }

        _timer.Stop();
        WaitText.Text = "Puedes continuar.";
        ContinueButton.IsEnabled = true;
        ContinueButton.Focus();
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        if (!ContinueButton.IsEnabled) return;
        _mayClose = true;
        DialogResult = true;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) => e.Cancel = !_mayClose;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Enter)
        {
            if (ContinueButton.IsEnabled) OnContinueClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }
}
