using ZXing.Net.Maui.Controls;

namespace JetVenta.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().UseBarcodeReader();
        builder.Services.AddSingleton<MobileSettings>();
        builder.Services.AddSingleton<MobileApiClient>();
        builder.Services.AddSingleton<MobileShoppingListStore>();
        builder.Services.AddTransient<MainPage>();
        return builder.Build();
    }
}
