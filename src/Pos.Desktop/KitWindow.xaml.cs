using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class KitWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private readonly ObservableCollection<ComponentRow> _components = [];
    private ProductRow? _kit;
    private CancellationTokenSource? _kitSearchCancellation;

    public KitWindow()
    {
        InitializeComponent();
        ComponentsList.ItemsSource = _components;
        Closed += (_, _) => _kitSearchCancellation?.Cancel();
    }

    private async void OnKitTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _kitSearchCancellation?.Cancel();
        _kitSearchCancellation = new CancellationTokenSource();
        await SearchAsync(KitBox.Text, KitResults, _kitSearchCancellation.Token);
    }

    private static async Task SearchAsync(string query, System.Windows.Controls.ListBox target, CancellationToken cancellationToken)
    {
        if (query.Trim().Length < 2) { target.Visibility = Visibility.Collapsed; return; }
        try
        {
            await Task.Delay(180, cancellationToken);
            var rows = await Client.GetFromJsonAsync<List<ProductRow>>($"/api/products/search?q={Uri.EscapeDataString(query.Trim())}", cancellationToken) ?? [];
            target.ItemsSource = rows;
            target.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) { }
        catch { target.Visibility = Visibility.Collapsed; }
    }
    private void OnKitSelected(object sender, MouseButtonEventArgs e) { if (KitResults.SelectedItem is ProductRow row) { _kit = row; KitBox.Text = row.Display; KitResults.Visibility = Visibility.Collapsed; } }
    private async void OnAddClick(object sender, RoutedEventArgs e)
    { if (_kit is null || !decimal.TryParse(QuantityBox.Text, out var quantity) || quantity <= 0m) { StatusText.Text = "Selecciona un kit y una cantidad válida."; return; } var dialog = new ProductPickerWindow { Owner = this }; if (dialog.ShowDialog() != true || dialog.Selected is null) return; if (dialog.Selected.Id == _kit.Id) { StatusText.Text = "Un kit no puede contenerse a sí mismo."; return; } _components.Add(new ComponentRow(dialog.Selected.Id, dialog.Selected.Display, quantity)); StatusText.Text = "Componente agregado."; }
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    { if (_kit is null || _components.Count == 0) { StatusText.Text = "Selecciona el kit y agrega componentes."; return; } Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken); try { var response = await Client.PostAsJsonAsync("/api/kits", new { kitProductId = _kit.Id, components = _components.Select(item => new { componentProductId = item.Id, quantity = item.Quantity }).ToArray() }); response.EnsureSuccessStatusCode(); StatusText.Text = "Kit guardado correctamente."; } catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo guardar"); } }
    private sealed record ProductRow(Guid Id, string Code, string Description) { public string Display => $"{Code} | {Description}"; }
    private sealed record ComponentRow(Guid Id, string Display, decimal Quantity);

    private sealed class ProductPickerWindow : Window
    {
        public ProductRow? Selected { get; private set; }
        private readonly ListBox _list = new() { Height = 260, DisplayMemberPath = "Display" };
        private readonly TextBox _search = new() { Height = 32, Margin = new Thickness(0, 0, 0, 8) };
        private CancellationTokenSource? _searchCancellation;

        public ProductPickerWindow()
        {
            Title = "Seleccionar componente"; Width = 520; Height = 380; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(_search); panel.Children.Add(_list); Content = panel;
            _search.TextChanged += async (_, _) => await LoadAsync();
            _list.MouseDoubleClick += (_, _) => { if (_list.SelectedItem is ProductRow row) { Selected = row; DialogResult = true; } };
            Closed += (_, _) => _searchCancellation?.Cancel();
        }

        private async Task LoadAsync()
        {
            _searchCancellation?.Cancel();
            _searchCancellation = new CancellationTokenSource();
            var cancellationToken = _searchCancellation.Token;
            if (_search.Text.Trim().Length < 2) { _list.ItemsSource = null; return; }
            try
            {
                await Task.Delay(180, cancellationToken);
                var rows = await Client.GetFromJsonAsync<List<ProductRow>>($"/api/products/search?q={Uri.EscapeDataString(_search.Text.Trim())}", cancellationToken) ?? [];
                _list.ItemsSource = rows;
            }
            catch (OperationCanceledException) { }
        }
    }
}
