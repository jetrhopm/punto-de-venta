using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.Generic;

namespace Pos.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Exit += (_, _) => BarcodeScannerService.Stop();
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnWindowPreviewKeyDown));
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));

        var startup = new StartupWindow();
        MainWindow = startup;
        startup.Show();
    }

    private static readonly HashSet<Window> WatchedDialogs = [];

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window dialog || !IsDialogWindow(dialog)) return;

        // A dialog is part of its current task. It must not become a hidden, separate window.
        dialog.ResizeMode = ResizeMode.NoResize;
        dialog.ShowInTaskbar = false;

        EventHandler preventMinimize = (_, _) =>
        {
            if (dialog.WindowState != WindowState.Minimized) return;
            dialog.Dispatcher.BeginInvoke(() =>
            {
                if (dialog.IsVisible) dialog.WindowState = WindowState.Normal;
            }, DispatcherPriority.ApplicationIdle);
        };
        dialog.StateChanged += preventMinimize;
        dialog.Closed += (_, _) => dialog.StateChanged -= preventMinimize;

        if (dialog.Owner is not Window owner) return;
        lock (WatchedDialogs)
        {
            if (!WatchedDialogs.Add(dialog)) return;
        }

        EventHandler restoreDialog = (_, _) => RestoreDialogAfterDesktopMinimize(dialog, owner);
        owner.StateChanged += restoreDialog;
        owner.Activated += restoreDialog;
        dialog.Closed += (_, _) =>
        {
            owner.StateChanged -= restoreDialog;
            owner.Activated -= restoreDialog;
            lock (WatchedDialogs) WatchedDialogs.Remove(dialog);
        };
    }

    // Windows+D minimizes both owner and modal window. Restoring only the owner must also restore its modal child.
    private static void RestoreDialogAfterDesktopMinimize(Window dialog, Window owner)
    {
        if (!dialog.IsVisible || owner.WindowState == WindowState.Minimized || dialog.WindowState != WindowState.Minimized) return;
        dialog.Dispatcher.BeginInvoke(() =>
        {
            if (!dialog.IsVisible || owner.WindowState == WindowState.Minimized) return;
            dialog.WindowState = WindowState.Normal;
            dialog.Activate();
        }, DispatcherPriority.ApplicationIdle);
    }

    private static void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Window window || !IsDialogWindow(window) || e.Handled) return;

        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            CloseDialog(window);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        if (Keyboard.FocusedElement is TextBox { AcceptsReturn: true } ||
            Keyboard.FocusedElement is ComboBox { IsDropDownOpen: true }) return;
        if (Keyboard.FocusedElement is Button) return;

        var primaryButton = FindPrimaryButton(window);
        if (primaryButton is null) return;
        primaryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, primaryButton));
        e.Handled = true;
    }

    private static bool IsDialogWindow(Window window) =>
        window.Owner is not null || (window is not Pos.Desktop.MainWindow and not LoginWindow and not StartupWindow);

    private static void CloseDialog(Window window)
    {
        try { window.DialogResult = false; }
        catch (InvalidOperationException) { window.Close(); }
    }

    private static Button? FindPrimaryButton(DependencyObject root)
    {
        var buttons = FindVisualChildren<Button>(root)
            .Where(button => button.IsEnabled && button.Visibility == Visibility.Visible)
            .ToList();
        if (buttons.Count == 0) return null;

        return buttons
            .Select((button, index) => (button, score: PrimaryButtonScore(GetButtonText(button)), index))
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.index)
            .First().button;
    }

    private static int PrimaryButtonScore(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        if (normalized.Contains("cancelar") || normalized.Contains("eliminar") || normalized.Contains("desactivar")) return -100;
        if (normalized.Contains("guardar")) return 100;
        if (normalized.Contains("confirmar") || normalized.Contains("cobrar") || normalized.Contains("registrar")) return 95;
        if (normalized.Contains("importar") || normalized.Contains("recibir") || normalized.Contains("crear")) return 90;
        if (normalized.Contains("aplicar") || normalized.Contains("actualizar")) return 85;
        if (normalized.Contains("abrir") || normalized.Contains("continuar") || normalized.Contains("aceptar")) return 80;
        if (normalized.Contains("reintentar") || normalized.Contains("reparar") || normalized.Contains("probar")) return 70;
        if (normalized.Contains("cerrar") || normalized.Contains("salir")) return 10;
        return 0;
    }

    private static string GetButtonText(Button button)
    {
        if (button.Content is string text) return text;
        return FindVisualChildren<TextBlock>(button).Select(textBlock => textBlock.Text).FirstOrDefault() ?? string.Empty;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null) yield break;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);
        MessageBox.Show(
            "Ocurrio un error en la pantalla actual. El programa seguira abierto.\n\nDetalle: " + e.Exception.Message,
            "JetVenta",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception) LogException(exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception);
        e.SetObserved();
    }

    private static void LogException(Exception exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "logs");
            Directory.CreateDirectory(directory);
            var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {exception}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(directory, "desktop-error.log"), message, Encoding.UTF8);
        }
        catch
        {
            // Logging must never hide the original UI error.
        }
    }
}
