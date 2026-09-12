using System.Windows.Threading;
using System.Net.Http;
using System.Net.Http.Json;

namespace Pos.Desktop;

public enum ApiAvailabilityState { Available, Unavailable, Maintenance }

public sealed record ApiAvailabilityStatus(ApiAvailabilityState State, string? Message = null);

public sealed class ApiAvailabilityMonitor : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private ApiAvailabilityStatus? _status;
    private bool _checking;

    public event EventHandler<ApiAvailabilityStatus>? AvailabilityChanged;

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
            if (!response.IsSuccessStatusCode)
            {
                SetStatus(new(ApiAvailabilityState.Unavailable));
                return;
            }

            var maintenance = await ApiClient.Client.GetFromJsonAsync<MaintenanceStatus>("api/maintenance/status", cancellation.Token);
            SetStatus(maintenance?.IsActive == true
                ? new(ApiAvailabilityState.Maintenance, maintenance.Reason)
                : new(ApiAvailabilityState.Available));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            SetStatus(new(ApiAvailabilityState.Unavailable));
        }
        finally { _checking = false; }
    }

    private void SetStatus(ApiAvailabilityStatus status)
    {
        if (_status == status) return;
        _status = status;
        AvailabilityChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= async (_, _) => await CheckAsync();
    }

    private sealed record MaintenanceStatus(bool IsActive, string? Reason);
}
