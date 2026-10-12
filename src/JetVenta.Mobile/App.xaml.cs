using Microsoft.Extensions.DependencyInjection;

namespace JetVenta.Mobile;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = _services.GetRequiredService<MainPage>();
        var window = new Window(new NavigationPage(page));
        window.Stopped += (_, _) => page.OnHostPaused();
        window.Resumed += (_, _) => page.OnHostResumed();
        return window;
    }
}
