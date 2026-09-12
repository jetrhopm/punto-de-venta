using System.Windows;
namespace Pos.Desktop;
public partial class RenameRegisterWindow : Window
{
    public string NewRegisterName => NameBox.Text.Trim();
    public RenameRegisterWindow(string name) { InitializeComponent(); NameBox.Text = name; Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); }; }
    private void OnSaveClick(object sender, RoutedEventArgs e) { if (NewRegisterName.Length < 2) { OperationFeedback.Show(this, "Nombre requerido", "Escribe un nombre de al menos 2 caracteres.", OperationResultKind.Warning); return; } DialogResult = true; }
}
