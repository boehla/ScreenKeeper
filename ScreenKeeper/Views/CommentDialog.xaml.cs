using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ScreenKeeper.Services.Native;

namespace ScreenKeeper.Views;

/// <summary>
/// Kleiner modaler Dialog für den Kommentar einer Aufnahme (Editor und History). Mehrzeilig; Strg+Enter übernimmt, Esc
/// bricht ab. Als eigenes, vom Aufrufer besessenes Fenster bekommt er alle Tastatureingaben – Editor-Kürzel greifen
/// währenddessen nicht. Über einem Vollbild-Editor (Topmost) ist er ebenfalls Topmost und wird auf dem Monitor des
/// Ankerpunkts zentriert statt mitten über dem ganzen virtuellen Bildschirm.
/// </summary>
public partial class CommentDialog : Window {
    private readonly Point? screenAnchor;

    private CommentDialog(string? comment, Point? screenAnchor) {
        InitializeComponent();
        this.screenAnchor = screenAnchor;
        commentBox.Text = comment ?? "";
        PreviewKeyDown += onPreviewKeyDown;
        SourceInitialized += onSourceInitialized;
        Loaded += (s, e) => {
            commentBox.Focus();
            commentBox.CaretIndex = commentBox.Text.Length;
        };
    }

    /// <summary>
    /// Zeigt den Dialog modal. <paramref name="screenAnchor"/> (Bildschirmkoordinaten in physischen Pixeln): der Dialog
    /// wird auf dem Monitor dieses Punkts zentriert; null = über dem Besitzer. Liefert den eingegebenen Text (auch leer =
    /// Kommentar entfernen) oder null bei Abbruch.
    /// </summary>
    public static string? Ask(Window? owner, string? comment, Point? screenAnchor = null) {
        CommentDialog dialog = new CommentDialog(comment, screenAnchor);
        if(owner != null && owner.IsVisible) {
            dialog.Owner = owner;
            // Besitzer liegt im Vollbild über allem: der Dialog muss darüber liegen
            if(owner.Topmost) dialog.Topmost = true;
        } else {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        if(screenAnchor.HasValue) dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        bool? result = dialog.ShowDialog();
        return result == true ? dialog.commentBox.Text : null;
    }

    private void onApplyClick(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }

    private void onPreviewKeyDown(object sender, KeyEventArgs e) {
        if(e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) {
            e.Handled = true;
            DialogResult = true;
        }
    }

    /// <summary>
    /// Vor dem ersten Anzeigen auf den Monitor des Ankerpunkts legen (Arbeitsbereich, physische Pixel).
    /// </summary>
    private void onSourceInitialized(object? sender, EventArgs e) {
        if(!screenAnchor.HasValue) return;
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if(handle == IntPtr.Zero || !User32.GetWindowRect(handle, out RECT window)) return;
        POINT anchor = new POINT { X = (int)Math.Round(screenAnchor.Value.X), Y = (int)Math.Round(screenAnchor.Value.Y) };
        IntPtr monitor = User32.MonitorFromPoint(anchor, User32.MONITOR_DEFAULTTONEAREST);
        MONITORINFOEX info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if(monitor == IntPtr.Zero || !User32.GetMonitorInfo(monitor, ref info)) return;
        RECT work = info.rcWork;
        int width = window.Right - window.Left;
        int height = window.Bottom - window.Top;
        int x = work.Left + Math.Max(0, (work.Right - work.Left - width) / 2);
        int y = work.Top + Math.Max(0, (work.Bottom - work.Top - height) / 2);
        User32.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
    }
}
