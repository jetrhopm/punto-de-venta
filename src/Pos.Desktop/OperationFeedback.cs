using System.Windows;

namespace Pos.Desktop;

/// <summary>
/// Centralizes the standard visual feedback used by configuration screens.
/// </summary>
internal static class OperationFeedback
{
    public static void Show(Window owner, string title, string message, OperationResultKind kind)
    {
        new OperationResultWindow(title, message, kind) { Owner = owner }.ShowDialog();
    }
}
