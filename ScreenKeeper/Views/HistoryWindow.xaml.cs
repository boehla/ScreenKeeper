using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Views;

/// <summary>
/// History-Fenster (Hauptfenster): Aufnahmen nach Tagen gruppiert, Nachladen beim Scrollen, Filter und Aktionen.
/// Im Tray-Modus versteckt Schließen das Fenster nur (Größe und Position werden gemerkt).
/// </summary>
public partial class HistoryWindow : Window, IHistoryHost {
    /// <summary>Nachladen, wenn weniger als so viele Bildschirmhöhen bis zum Ende fehlen.</summary>
    private const double loadAheadViewports = 1.5;

    private readonly HistoryViewModel viewModel;
    private readonly ThumbnailLoader thumbnails;
    private readonly UpdateViewModel update;
    private readonly bool hideOnClose;
    private ScrollViewer? scrollViewer;
    private bool allowClose;
    private bool testMode;

    /// <param name="hideOnClose">Tray-Modus: Schließen versteckt nur, Lage wird in den Einstellungen gemerkt.</param>
    public HistoryWindow(bool hideOnClose) {
        InitializeComponent();
        this.hideOnClose = hideOnClose;
        thumbnails = new ThumbnailLoader(AppServices.Storage, Dispatcher);
        update = new UpdateViewModel(AppServices.Updates);
        viewModel = new HistoryViewModel(AppServices.Storage, thumbnails) { Host = this, Update = update };
        DataContext = viewModel;
        if(hideOnClose) restorePlacement();

        viewModel.BlockLoaded += onBlockLoaded;
        viewModel.SelectRequested += onSelectRequested;
        AppServices.Workflow.CaptureStored += onCaptureStored;
        AppServices.Workflow.CaptureCopied += onCaptureCopied;
        AppServices.Workflow.EditorClosed += onEditorClosed;
        AppServices.Settings.Changed += onSettingsChanged;
        Loaded += onLoaded;
        Activated += (s, e) => viewModel.RefreshDayLabels();
        Closing += onClosing;
        Closed += onClosed;
        RefreshTask = viewModel.RefreshAsync();
    }

    public HistoryViewModel ViewModel => viewModel;

    /// <summary>Aufgabe des ersten Ladeblocks (für Tests).</summary>
    public Task RefreshTask { get; }

    /// <summary>Wirklich schließen (Programmende), statt nur zu verstecken.</summary>
    public void CloseForShutdown() {
        allowClose = true;
        Close();
    }

    /// <summary>Liste neu laden (z. B. nach der automatischen Aufbewahrung).</summary>
    public void Reload() {
        _ = viewModel.RefreshAsync();
    }

    bool IHistoryHost.Confirm(string message) {
        if(testMode) {
            Log.Info($"Test: Rückfrage automatisch bestätigt: {message}");
            return true;
        }
        return MessageBox.Show(this, message, Strings.HistoryTitle, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    void IHistoryHost.ShowError(string message) {
        if(testMode) {
            Console.Error.WriteLine(message);
            return;
        }
        MessageBox.Show(this, message, Strings.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void onLoaded(object sender, RoutedEventArgs e) {
        scrollViewer = findChild<ScrollViewer>(list);
        if(scrollViewer != null) scrollViewer.ScrollChanged += (s, args) => checkLoadMore();
        checkLoadMore();
        list.Focus();
    }

    private void onBlockLoaded(object? sender, EventArgs e) {
        // nach dem Layout prüfen, ob die Liste den sichtbaren Bereich schon füllt
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(checkLoadMore));
    }

    private void checkLoadMore() {
        if(scrollViewer == null || viewModel.IsComplete || viewModel.IsLoading) return;
        double remaining = scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset;
        if(remaining <= Math.Max(scrollViewer.ViewportHeight, 200) * loadAheadViewports) _ = viewModel.LoadMoreAsync();
    }

    private void onSelectionChanged(object sender, SelectionChangedEventArgs e) {
        // Überschriften sind nicht auswählbar (z. B. nach Strg+A)
        foreach(object added in e.AddedItems) {
            if(added is HistoryHeaderRow) list.SelectedItems.Remove(added);
        }
        viewModel.SetSelection(list.SelectedItems.Cast<object>().ToList());
    }

    private void onItemDoubleClick(object sender, MouseButtonEventArgs e) {
        if(e.ChangedButton != MouseButton.Left) return;
        if(sender is ListBoxItem container && container.DataContext is HistoryItem item) {
            e.Handled = true;
            viewModel.Edit(item);
        }
    }

    private void onCaptureStored(object? sender, CaptureFiles files) {
        CaptureEntry? entry = AppServices.Storage.GetEntry(files);
        if(entry != null) viewModel.AddNewCapture(entry);
    }

    private void onEditorClosed(object? sender, string jsonPath) {
        viewModel.OnEditorClosed(jsonPath);
    }

    private void onCaptureCopied(object? sender, (CaptureFiles Original, CaptureFiles Copy) copied) {
        CaptureEntry? entry = AppServices.Storage.GetEntry(copied.Copy);
        if(entry != null) viewModel.InsertCopy(copied.Original.PngPath, entry);
    }

    /// <summary>Eintrag allein auswählen, sichtbar machen und den Fokus auf ihn setzen (z. B. eine neue Kopie).</summary>
    private void onSelectRequested(object? sender, HistoryItem item) {
        list.SelectedItems.Clear();
        list.SelectedItem = item;
        list.ScrollIntoView(item);
        if(!IsVisible) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
            if(list.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container) container.Focus();
        }));
    }

    private void onSettingsChanged(object? sender, EventArgs e) {
        viewModel.OnStorageMaybeChanged();
    }

    private void onClosing(object? sender, CancelEventArgs e) {
        if(hideOnClose) savePlacement();
        if(hideOnClose && !allowClose) {
            e.Cancel = true;
            Hide();
        }
    }

    private void onClosed(object? sender, EventArgs e) {
        viewModel.BlockLoaded -= onBlockLoaded;
        viewModel.SelectRequested -= onSelectRequested;
        AppServices.Workflow.CaptureStored -= onCaptureStored;
        AppServices.Workflow.CaptureCopied -= onCaptureCopied;
        AppServices.Workflow.EditorClosed -= onEditorClosed;
        update.Detach();
        AppServices.Settings.Changed -= onSettingsChanged;
        thumbnails.Clear();
    }

    /// <summary>
    /// Gemerkte Lage wiederherstellen, wenn sie noch (zumindest teilweise) auf dem Bildschirm liegt.
    /// </summary>
    private void restorePlacement() {
        WindowPlacement? placement = AppServices.Settings.Settings.HistoryWindow;
        if(placement == null || !placement.IsValid) return;
        Rect bounds = new Rect(placement.Left, placement.Top, placement.Width, placement.Height);
        Rect screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        Rect visible = Rect.Intersect(bounds, screen);
        if(visible.IsEmpty || visible.Width < 100 || visible.Height < 50) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;
        if(placement.Maximized) WindowState = WindowState.Maximized;
    }

    private void savePlacement() {
        try {
            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            if(bounds.IsEmpty || double.IsNaN(bounds.Left) || double.IsInfinity(bounds.Left)) return;
            WindowPlacement placement = new WindowPlacement {
                Left = Math.Round(bounds.Left),
                Top = Math.Round(bounds.Top),
                Width = Math.Round(bounds.Width),
                Height = Math.Round(bounds.Height),
                Maximized = WindowState == WindowState.Maximized
            };
            if(!placement.IsValid) return;
            WindowPlacement? old = AppServices.Settings.Settings.HistoryWindow;
            if(old != null && old.Left == placement.Left && old.Top == placement.Top && old.Width == placement.Width && old.Height == placement.Height && old.Maximized == placement.Maximized) return;
            AppServices.Settings.Settings.HistoryWindow = placement;
            AppServices.Settings.Save();
        } catch(Exception ex) {
            Log.Error(ex, "Lage des History-Fensters konnte nicht gespeichert werden");
        }
    }

    private static T? findChild<T>(DependencyObject parent) where T : DependencyObject {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for(int i = 0; i < count; i++) {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if(child is T match) return match;
            T? nested = findChild<T>(child);
            if(nested != null) return nested;
        }
        return null;
    }
}
