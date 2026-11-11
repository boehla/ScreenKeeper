using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Views;

/// <summary>
/// Einstellungen (Allgemein, Hotkeys, Editor) mit OK/Abbrechen/Übernehmen. Während ein Hotkey-Feld den Fokus hat,
/// sind die globalen Hotkeys ausgesetzt, damit die Kombination im Feld ankommt.
/// </summary>
public partial class SettingsWindow : Window, ISettingsHost {
    private readonly SettingsViewModel viewModel;
    private readonly UpdateViewModel update;
    private bool hotkeysSuspended;

    public SettingsWindow() {
        InitializeComponent();
        update = new UpdateViewModel(AppServices.Updates);
        viewModel = new SettingsViewModel(AppServices.Settings, AppServices.Autostart, AppServices.Hotkeys, EnvironmentCheckService.IsSnippingKeyEnabled) { Host = this, Update = update };
        viewModel.CloseRequested += (s, e) => Close();
        DataContext = viewModel;
        Closed += (s, e) => {
            resumeHotkeys();
            update.Detach();
            viewModel.Detach();
        };
    }

    public SettingsViewModel ViewModel => viewModel;

    /// <summary>
    /// Nur für automatisierte Tests: jede Seite einmal anzeigen (damit alle Bindungen ausgewertet werden).
    /// Liefert die Zahl der Seiten.
    /// </summary>
    public async Task<int> RunTestAsync() {
        TabControl? tabs = findTabControl(this);
        if(tabs == null) return 0;
        for(int i = 0; i < tabs.Items.Count; i++) {
            tabs.SelectedIndex = i;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
        tabs.SelectedIndex = 0;
        return tabs.Items.Count;
    }

    private static TabControl? findTabControl(DependencyObject parent) {
        foreach(object child in LogicalTreeHelper.GetChildren(parent)) {
            if(child is TabControl tabs) return tabs;
            if(child is DependencyObject element) {
                TabControl? nested = findTabControl(element);
                if(nested != null) return nested;
            }
        }
        return null;
    }

    bool ISettingsHost.Confirm(string message) {
        return MessageBox.Show(this, message, Strings.SettingsTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    string? ISettingsHost.PickFolder(string title, string initialFolder) {
        OpenFolderDialog dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if(!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder)) dialog.InitialDirectory = initialFolder;
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    void ISettingsHost.ShowError(string message) {
        MessageBox.Show(this, message, Strings.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void onRecordingChanged(object? sender, EventArgs e) {
        if(sender is HotkeyBox box && box.IsRecording) {
            if(!hotkeysSuspended && AppServices.Hotkeys != null) {
                AppServices.Hotkeys.Suspend();
                hotkeysSuspended = true;
            }
        } else {
            resumeHotkeys();
        }
        viewModel.RefreshHotkeyStatus();
    }

    private void resumeHotkeys() {
        if(!hotkeysSuspended) return;
        hotkeysSuspended = false;
        AppServices.Hotkeys?.Resume();
    }
}
