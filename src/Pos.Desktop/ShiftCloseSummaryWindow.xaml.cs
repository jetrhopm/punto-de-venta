using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Pos.Desktop;

public partial class ShiftCloseSummaryWindow : Window
{
    public ShiftCloseSummaryWindow(decimal expectedCash, decimal countedCash, decimal difference)
    {
        InitializeComponent();
        var culture = CultureInfo.GetCultureInfo("es-MX");
        ExpectedText.Text = expectedCash.ToString("C2", culture);
        CountedText.Text = countedCash.ToString("C2", culture);
        DifferenceText.Text = difference.ToString("C2", culture);
        DifferenceText.Foreground = difference == 0m ? Brushes.ForestGreen : Brushes.Firebrick;
    }

    private void OnContinueClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
