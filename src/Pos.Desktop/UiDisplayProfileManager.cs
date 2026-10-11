using System.Windows;

namespace Pos.Desktop;

public static class UiDisplayProfileManager
{
    public static void Apply(DisplayProfile profile)
    {
        var values = profile switch
        {
            DisplayProfile.Compact => new UiMetrics(13d, 33d, 32d, 33d, new Thickness(12d, 7d, 12d, 7d), new Thickness(8d, 5d, 8d, 5d), 145d, 82d, 50d, 38d, 66d, 58d, 64d, 170d, 58d, 10d, 98d, 270d, new Thickness(8d, 8d, 8d, 8d), 340d, 350d, 145d, 34d, new Thickness(14d)),
            DisplayProfile.Reduced => new UiMetrics(12d, 30d, 30d, 30d, new Thickness(10d, 6d, 10d, 6d), new Thickness(7d, 4d, 7d, 4d), 134d, 76d, 46d, 35d, 60d, 53d, 58d, 152d, 54d, 9d, 90d, 250d, new Thickness(6d, 6d, 6d, 6d), 310d, 320d, 125d, 31d, new Thickness(10d)),
            _ => new UiMetrics(14d, 36d, 34d, 36d, new Thickness(14d, 8d, 14d, 8d), new Thickness(10d, 9d, 10d, 9d), 155d, 90d, 54d, 42d, 72d, 64d, 76d, 210d, 86d, 11d, 122d, 310d, new Thickness(14d, 12d, 14d, 12d), 390d, 420d, 190d, 38d, new Thickness(22d))
        };

        var resources = System.Windows.Application.Current.Resources;
        resources["UiWindowFontSize"] = values.FontSize;
        resources["UiButtonMinHeight"] = values.ButtonMinHeight;
        resources["UiInputMinHeight"] = values.InputMinHeight;
        resources["UiDataGridRowHeight"] = values.DataGridRowHeight;
        resources["UiButtonPadding"] = values.ButtonPadding;
        resources["UiDataGridHeaderPadding"] = values.DataGridHeaderPadding;
        resources["UiTileWidth"] = values.TileWidth;
        resources["UiTileHeight"] = values.TileHeight;
        resources["UiNavigationHeight"] = values.NavigationHeight;
        resources["UiToolbarHeight"] = values.ToolbarHeight;
        resources["UiRailButtonHeight"] = values.RailButtonHeight;
        resources["UiPaymentButtonHeight"] = values.PaymentButtonHeight;
        resources["UiMainHeaderHeight"] = new GridLength(values.MainHeaderHeight);
        resources["UiBrandColumnWidth"] = new GridLength(values.BrandColumnWidth);
        resources["UiTopNavMinWidth"] = values.TopNavMinWidth;
        resources["UiTopNavFontSize"] = values.TopNavFontSize;
        resources["UiSalesRailWidth"] = new GridLength(values.SalesRailWidth);
        resources["UiSalesPaymentWidth"] = new GridLength(values.SalesPaymentWidth);
        resources["UiMainContentMargin"] = values.MainContentMargin;
        resources["UiProductEditorWidth"] = new GridLength(values.ProductEditorWidth);
        resources["UiHistoryDetailWidth"] = new GridLength(values.HistoryDetailWidth);
        resources["UiCutHandoffHeight"] = new GridLength(values.CutHandoffHeight);
        resources["UiModuleActionHeight"] = values.ModuleActionHeight;
        resources["UiModulePageMargin"] = values.ModulePageMargin;
    }

    private sealed record UiMetrics(
        double FontSize,
        double ButtonMinHeight,
        double InputMinHeight,
        double DataGridRowHeight,
        Thickness ButtonPadding,
        Thickness DataGridHeaderPadding,
        double TileWidth,
        double TileHeight,
        double NavigationHeight,
        double ToolbarHeight,
        double RailButtonHeight,
        double PaymentButtonHeight,
        double MainHeaderHeight,
        double BrandColumnWidth,
        double TopNavMinWidth,
        double TopNavFontSize,
        double SalesRailWidth,
        double SalesPaymentWidth,
        Thickness MainContentMargin,
        double ProductEditorWidth,
        double HistoryDetailWidth,
        double CutHandoffHeight,
        double ModuleActionHeight,
        Thickness ModulePageMargin);
}
