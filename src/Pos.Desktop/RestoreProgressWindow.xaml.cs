using System.Windows;

namespace Pos.Desktop;

public partial class RestoreProgressWindow : Window
{
    public RestoreProgressWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string status) => StatusText.Text = status;
}
