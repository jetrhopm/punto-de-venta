using System.Windows;

namespace Pos.Desktop;

public partial class DisplaySettingsWindow : Window
{
    public DisplaySettingsWindow()
    {
        InitializeComponent();
        SetSelection(ApiClient.DisplayProfile);
    }

    private void OnProfileChanged(object sender, RoutedEventArgs e) => StatusText.Text = "El cambio se aplicará al guardar.";

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var profile = StandardButton.IsChecked == true ? DisplayProfile.Standard : CompactButton.IsChecked == true ? DisplayProfile.Compact : DisplayProfile.Reduced;
        ApiClient.SetDisplayProfile(profile);
        UiDisplayProfileManager.Apply(profile);
        StatusText.Text = "Perfil guardado para esta computadora. Reinicia JetVenta para aplicarlo en todas las ventanas.";
    }

    private void SetSelection(DisplayProfile profile)
    {
        StandardButton.IsChecked = profile == DisplayProfile.Standard;
        CompactButton.IsChecked = profile == DisplayProfile.Compact;
        ReducedButton.IsChecked = profile == DisplayProfile.Reduced;
    }
}
