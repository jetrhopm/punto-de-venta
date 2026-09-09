using System.Windows.Threading;
using System.Net.Http;

namespace Pos.Desktop;

public sealed class ApiAvailabilityMonitor : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool? _available;
    private bool _checking;

    public event EventHandler<bool>? AvailabilityChanged;

    public ApiAvailabilityMonitor()
    {
        _timer.Tick += async (_, _) => await CheckAsync();
    }

    public void Start()
    {
        _timer.Start();
        _ = CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (_checking || string.IsNullOrWhiteSpace(SessionContext.AccessToken)) return;
        _checking = true;
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var response = await ApiClient.Client.GetAsync("health", cancellation.Token);
            SetAvailable(response.IsSuccessStatusCode);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            SetAvailable(false);
        }
        finally { _checking = false; }
    }

    private void SetAvailable(bool available)
    {
        if (_available == available) return;
        _available = available;
        AvailabilityChanged?.Invoke(this, available);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= async (_, _) => await CheckAsync();
    }
}
