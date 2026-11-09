using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;
using ScreenKeeper.Services.Native;

namespace ScreenKeeper.Views;

/// <summary>
/// Tray-Symbol (WinForms-NotifyIcon) mit WPF-Kontextmenü: Aufnahme, History öffnen, Einstellungen, Beenden – und nur
/// bei einer neuen Version „Update auf x installieren“. Doppelklick öffnet die History.
/// </summary>
public sealed class TrayIcon : IDisposable {
    private readonly System.Windows.Forms.NotifyIcon notifyIcon;
    private readonly System.Drawing.Icon icon;
    private readonly ContextMenu menu;
    private MenuItem? updateItem;
    private Separator? updateSeparator;

    public TrayIcon() {
        icon = loadIcon();
        menu = buildMenu();
        notifyIcon = new System.Windows.Forms.NotifyIcon {
            Icon = icon,
            Text = truncate(Strings.TrayTooltip, 127),
            Visible = true
        };
        notifyIcon.MouseUp += onMouseUp;
        notifyIcon.MouseDoubleClick += onMouseDoubleClick;
    }

    public event EventHandler? CaptureRequested;

    public event EventHandler? HistoryRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? UpdateRequested;

    /// <summary>
    /// Update-Hinweis setzen: Menüeintrag „Update auf x installieren“ und „· Update verfügbar“ im Tooltip; null = keiner.
    /// </summary>
    public void SetUpdate(string? version) {
        bool visible = version != null;
        if(updateItem != null) {
            updateItem.Header = visible ? Strings.Format(Strings.MenuInstallUpdate, version) : "";
            updateItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
        if(updateSeparator != null) updateSeparator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        notifyIcon.Text = truncate(visible ? Strings.TrayTooltip + Strings.TrayUpdateSuffix : Strings.TrayTooltip, 127);
    }

    /// <summary>Kurze Benachrichtigung am Tray-Symbol.</summary>
    public void ShowBalloon(string title, string text, bool warning) {
        System.Windows.Forms.ToolTipIcon tipIcon = warning ? System.Windows.Forms.ToolTipIcon.Warning : System.Windows.Forms.ToolTipIcon.Info;
        notifyIcon.ShowBalloonTip(warning ? 10000 : 3000, title, text, tipIcon);
    }

    public void Dispose() {
        notifyIcon.Visible = false;
        notifyIcon.MouseUp -= onMouseUp;
        notifyIcon.MouseDoubleClick -= onMouseDoubleClick;
        notifyIcon.Dispose();
        icon.Dispose();
        menu.IsOpen = false;
    }

    private ContextMenu buildMenu() {
        ContextMenu contextMenu = new ContextMenu();
        contextMenu.Items.Add(createItem(Strings.MenuCapture, () => CaptureRequested?.Invoke(this, EventArgs.Empty), true));
        contextMenu.Items.Add(createItem(Strings.MenuHistory, () => HistoryRequested?.Invoke(this, EventArgs.Empty), false));
        contextMenu.Items.Add(createItem(Strings.MenuSettings, () => SettingsRequested?.Invoke(this, EventArgs.Empty), false));
        updateSeparator = new Separator { Visibility = Visibility.Collapsed };
        contextMenu.Items.Add(updateSeparator);
        updateItem = createItem("", () => UpdateRequested?.Invoke(this, EventArgs.Empty), false);
        updateItem.Visibility = Visibility.Collapsed;
        contextMenu.Items.Add(updateItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(createItem(Strings.MenuExit, () => ExitRequested?.Invoke(this, EventArgs.Empty), false));
        contextMenu.Placement = PlacementMode.MousePoint;
        contextMenu.Opened += onMenuOpened;
        return contextMenu;
    }

    private static MenuItem createItem(string header, Action action, bool bold) {
        MenuItem item = new MenuItem { Header = header };
        if(bold) item.FontWeight = FontWeights.Bold;
        item.Click += (s, e) => action();
        return item;
    }

    private void onMouseUp(object? sender, System.Windows.Forms.MouseEventArgs e) {
        if(e.Button != System.Windows.Forms.MouseButtons.Right) return;
        menu.IsOpen = true;
    }

    private void onMouseDoubleClick(object? sender, System.Windows.Forms.MouseEventArgs e) {
        if(e.Button != System.Windows.Forms.MouseButtons.Left) return;
        HistoryRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Das Popup in den Vordergrund holen, sonst schließt es nicht beim Klick daneben.
    /// </summary>
    private void onMenuOpened(object sender, RoutedEventArgs e) {
        if(PresentationSource.FromVisual(menu) is HwndSource source) {
            User32.SetForegroundWindow(source.Handle);
        }
        menu.Focus();
    }

    private static System.Drawing.Icon loadIcon() {
        int size = User32.GetSystemMetrics(User32.SM_CXSMICON);
        if(size <= 0) size = 16;
        try {
            System.Windows.Resources.StreamResourceInfo? info = Application.GetResourceStream(new Uri("pack://application:,,,/ScreenKeeper.ico", UriKind.Absolute));
            if(info != null) {
                using (Stream stream = info.Stream) {
                    return new System.Drawing.Icon(stream, new System.Drawing.Size(size, size));
                }
            }
        } catch(Exception ex) {
            Log.Error(ex, "Tray-Symbol konnte nicht aus den Ressourcen geladen werden");
        }
        System.Drawing.Icon? fallback = Environment.ProcessPath != null ? System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath) : null;
        return fallback ?? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    private static string truncate(string text, int maxLength) {
        return text.Length <= maxLength ? text : text.Substring(0, maxLength);
    }
}
