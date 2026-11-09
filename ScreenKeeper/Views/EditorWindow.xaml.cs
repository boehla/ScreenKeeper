using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;
using ScreenKeeper.Services.Native;
using ScreenKeeper.ViewModels;
using ScreenKeeper.ViewModels.Undo;

namespace ScreenKeeper.Views;

public enum EditorMode {
    /// <summary>Rahmenlos und oberstes Fenster über den virtuellen Bildschirm, 1:1 physische Pixel.</summary>
    Fullscreen,
    /// <summary>Maximiertes Fenster mit Zoom und Verschieben (z. B. bei geänderter Monitor-Anordnung).</summary>
    Windowed
}

/// <summary>
/// Editor-Fenster für beide Modi (gleiche Oberfläche). Dieser Teil: Aufbau, Zoom/Pan, Overlay-Positionen,
/// Fensterdienste für das ViewModel und Speichern beim Schließen. Eingaben siehe <c>EditorWindow.Input.cs</c>,
/// Inline-Textbearbeitung <c>EditorWindow.TextEditing.cs</c>, Testhilfen <c>EditorWindow.Testing.cs</c>.
/// </summary>
public partial class EditorWindow : Window, IEditorHost {
    private const double minZoomFactor = 0.1;
    private const double maxZoomFactor = 8;
    private const double toolbarGap = 10;
    private const double magnifierGap = 26;

    private readonly CaptureDocument document;
    private readonly BitmapSource original;
    private readonly string jsonPath;
    private readonly EditorMode mode;
    private readonly long startTimestamp;
    private readonly EditorViewModel viewModel;
    private readonly ViewTransform transform = new ViewTransform();
    private double deviceScale = 1;
    private bool viewInitialized;
    private bool testMode;
    private bool warmUp;
    private bool closed;

    public EditorWindow(CaptureDocument doc, BitmapSource original, string jsonPath, EditorMode mode, long startTimestamp = 0) {
        InitializeComponent();
        document = doc;
        this.original = original;
        this.jsonPath = jsonPath;
        this.mode = mode;
        this.startTimestamp = startTimestamp != 0 ? startTimestamp : Stopwatch.GetTimestamp();
        viewModel = new EditorViewModel(doc, original, this);
        DataContext = viewModel;

        surface.Width = original.PixelWidth;
        surface.Height = original.PixelHeight;
        originalImage.Source = original;
        originalImage.Width = original.PixelWidth;
        originalImage.Height = original.PixelHeight;
        annotationHost.Width = original.PixelWidth;
        annotationHost.Height = original.PixelHeight;
        RefreshAnnotations();

        if(mode == EditorMode.Fullscreen) {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            SourceInitialized += (s, e) => placeOverVirtualScreen();
        } else {
            WindowState = WindowState.Maximized;
            viewport.Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
        }

        Loaded += onLoaded;
        ContentRendered += onContentRendered;
        DpiChanged += onDpiChanged;
        Closing += onClosing;
        Closed += (s, e) => closed = true;
        root.SizeChanged += onRootSizeChanged;
        viewModel.OverlayChanged += (s, e) => updateOverlay();
        viewModel.DocumentChanged += onDocumentChanged;
        viewModel.DuplicateRequested += onDuplicateRequested;
        attachInput();
        attachTextEditor();
    }

    public CaptureDocument Document => document;

    public BitmapSource Original => original;

    public string JsonPath => jsonPath;

    public EditorMode Mode => mode;

    public EditorViewModel ViewModel => viewModel;

    /// <summary>
    /// Vorwärmen beim Programmstart (Tray-Betrieb, im Leerlauf): einen winzigen Editor außerhalb des Bildschirms anzeigen
    /// und nach dem ersten Zeichnen wieder schließen. Lädt Typen, XAML, Vorlagen, JSON-/PNG-Kodierung und die
    /// Render-Pipeline, damit schon die erste Aufnahme nach dem Start so schnell erscheint wie jede weitere (kalt sonst
    /// ca. 0,4–0,6 s mehr). Speichert nichts und holt nichts in den Vordergrund. Der Task endet, wenn das Fenster zu ist.
    /// </summary>
    public static Task WarmUp() {
        const int size = 32;
        BitmapSource image = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, new byte[size * size * 4], size * 4);
        image.Freeze();
        PixelRect bounds = new PixelRect(0, 0, size, size);
        CaptureDocument document = new CaptureDocument {
            VirtualScreen = bounds,
            Monitors = new List<MonitorInfo> { new MonitorInfo { Bounds = bounds, Primary = true } },
            Annotations = new List<Annotation> { new CursorAnnotation { X = 1, Y = 1, Kind = CursorKinds.Arrow } }
        };
        CaptureDocumentSerializer.Deserialize(CaptureDocumentSerializer.Serialize(document));
        ImageFiles.EncodePng(image);

        EditorWindow editor = new EditorWindow(document, image, "", EditorMode.Windowed) { warmUp = true };
        editor.WindowState = WindowState.Normal;
        editor.ShowActivated = false;
        editor.ShowInTaskbar = false;
        editor.Left = -32000;
        editor.Top = -32000;
        editor.Width = 400;
        editor.Height = 300;
        TaskCompletionSource closedSource = new TaskCompletionSource();
        editor.Closed += (s, e) => closedSource.TrySetResult();
        editor.ContentRendered += (s, e) => editor.Close();
        editor.Show();
        return closedSource.Task;
    }

    /// <summary>
    /// Zeichnungen neu aufbauen (nach Änderungen an den Annotationen).
    /// </summary>
    public void RefreshAnnotations() {
        annotationHost.Drawing = AppServices.Render.BuildAnnotationDrawing(document, original, viewModel.IsAnnotationDrawn);
    }

    /// <summary>
    /// Nur für automatisierte Tests (<c>--open &lt;json&gt; --test-select x,y,w,h</c>): setzt den Bereich über denselben
    /// Rückgängig-Befehl wie die Maus, kopiert ihn nach 1 s in die Zwischenablage und schließt.
    /// </summary>
    public void RunTestSelection(PixelRect selection) {
        testMode = true;
        viewModel.SetSelection(selection);
        Log.Info($"Test: Bereich gesetzt, Rückgängig-Schritte {viewModel.UndoStack.UndoCount}");
        DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (s, e) => {
            timer.Stop();
            if(viewModel.CopySelectionCommand.CanExecute(null)) viewModel.CopySelectionCommand.Execute(null);
            if(!closed) Close();
        };
        timer.Start();
    }

    PixelPoint IEditorHost.GetCursorImagePoint() {
        return transform.ToPixel(getMouseInViewport());
    }

    string? IEditorHost.AskSaveAsPath(string initialFolder, string fileName) {
        bool wasTopmost = Topmost;
        // Im Vollbild läge der Dialog sonst unter dem obersten Fenster
        Topmost = false;
        try {
            SaveFileDialog dialog = new SaveFileDialog {
                Title = Strings.SaveAsTitle,
                Filter = Strings.SaveAsFilter,
                FileName = fileName,
                DefaultExt = ".png",
                AddExtension = true,
                OverwritePrompt = true
            };
            if(Directory.Exists(initialFolder)) dialog.InitialDirectory = initialFolder;
            bool? result = dialog.ShowDialog(this);
            return result == true ? dialog.FileName : null;
        } finally {
            if(!closed) {
                Topmost = wasTopmost;
                BringToFront();
            }
        }
    }

    void IEditorHost.ShowError(string message) {
        if(testMode) {
            Log.Warn($"Test: {message}");
            Console.Error.WriteLine(message);
            return;
        }
        MessageBox.Show(this, message, Strings.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    void IEditorHost.CloseEditor() {
        if(!closed) Close();
    }

    private void onLoaded(object sender, RoutedEventArgs e) {
        deviceScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if(mode == EditorMode.Fullscreen) placeOverVirtualScreen();
        initializeView();
        configureMagnifier();
        syncPointerFromMouse();
        updateOverlay();
        BringToFront();
    }

    private void onContentRendered(object? sender, EventArgs e) {
        if(warmUp) return;
        TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
        Log.Info($"Editor sichtbar nach {elapsed.TotalMilliseconds:0} ms ({mode}, {original.PixelWidth}×{original.PixelHeight}, DPI-Faktor {deviceScale:0.##})");
        BringToFront();
    }

    private void onDpiChanged(object sender, DpiChangedEventArgs e) {
        deviceScale = e.NewDpi.DpiScaleX;
        // WPF verschiebt das Fenster bei WM_DPICHANGED auf das vorgeschlagene Rechteck → danach wieder exakt über den
        // virtuellen Bildschirm legen (physische Pixel)
        if(mode == EditorMode.Fullscreen) Dispatcher.BeginInvoke(new Action(placeOverVirtualScreen), DispatcherPriority.Loaded);
        viewInitialized = false;
        initializeView();
        configureMagnifier();
    }

    private void onRootSizeChanged(object sender, SizeChangedEventArgs e) {
        selectionOverlay.Width = root.ActualWidth;
        selectionOverlay.Height = root.ActualHeight;
        annotationOverlay.Width = root.ActualWidth;
        annotationOverlay.Height = root.ActualHeight;
        if(!viewInitialized) initializeView();
        updateOverlay();
    }

    private void onDocumentChanged(object? sender, DocumentChangeKind kind) {
        if((kind & DocumentChangeKind.Annotations) != 0) RefreshAnnotations();
    }

    private void onDuplicateRequested(object? sender, EventArgs e) {
        if(warmUp || closed) return;
        try {
            AppServices.Workflow.DuplicateAndContinue(this);
        } catch(Exception ex) {
            Log.Error(ex, $"Kopie von {jsonPath} fehlgeschlagen");
            ((IEditorHost)this).ShowError(Strings.Format(Strings.DuplicateFailed, ex.Message));
        }
    }

    private void onClosing(object? sender, CancelEventArgs e) {
        if(warmUp) return;
        // übernimmt auch einen gerade bearbeiteten Text
        viewModel.CancelGesture();
        storeToolOptions();
        try {
            long start = Stopwatch.GetTimestamp();
            AppServices.Storage.SaveDocument(document, jsonPath);
            AppServices.Storage.UpdateThumbnail(document, original, jsonPath);
            PixelRect? selection = document.Selection;
            string area = selection.HasValue ? $"{selection.Value.X},{selection.Value.Y} {selection.Value.Width}×{selection.Value.Height}" : "keiner";
            Log.Info($"Bearbeitungsstand gespeichert in {Stopwatch.GetElapsedTime(start).TotalMilliseconds:0} ms: {jsonPath} (Bereich {area})");
        } catch(Exception ex) {
            Log.Error(ex, $"Bearbeitungsstand {jsonPath} konnte nicht gespeichert werden");
            // z. B. Ablageordner schreibgeschützt oder Laufwerk getrennt: melden statt still verlieren
            ((IEditorHost)this).ShowError(Strings.Format(Strings.ErrorEditStateSaveFailed, ex.Message));
        }
    }

    /// <summary>
    /// Zuletzt verwendete Werkzeug-Optionen in den Einstellungen merken (nicht im Testmodus).
    /// </summary>
    private void storeToolOptions() {
        if(testMode) return;
        try {
            if(viewModel.StoreOptions(AppServices.Settings.Settings)) AppServices.Settings.Save();
        } catch(Exception ex) {
            Log.Error(ex, "Werkzeug-Optionen konnten nicht gespeichert werden");
        }
    }

    /// <summary>
    /// Fenster in physischen Pixeln genau über den aufgenommenen virtuellen Bildschirm legen.
    /// </summary>
    private void placeOverVirtualScreen() {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if(handle == IntPtr.Zero) return;
        PixelRect bounds = document.VirtualScreen;
        User32.SetWindowPos(handle, User32.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, User32.SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// Editor in den Vordergrund holen und ihm die Tastatur geben – auch wenn gerade ein anderes Programm (Browser,
    /// Vollbild-Anwendung) den Fokus hat, z. B. bei einem zweiten Hotkey-Druck, während der Editor schon offen ist.
    /// </summary>
    public void BringToFront() {
        if(closed || warmUp) return;
        if(WindowState == WindowState.Minimized) WindowState = mode == EditorMode.Windowed ? WindowState.Maximized : WindowState.Normal;
        WindowActivation.ForceForeground(new WindowInteropHelper(this).Handle);
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    /// <summary>
    /// Vollbild: 1 Bildpixel = 1 Gerätepixel (Zoom = 1 / DPI-Skalierung), kein Pan.
    /// Fenster: einpassen (höchstens 100 %) und zentrieren.
    /// </summary>
    private void initializeView() {
        if(mode == EditorMode.Fullscreen) {
            transform.Zoom = 1.0 / deviceScale;
            transform.PanX = 0;
            transform.PanY = 0;
            viewInitialized = true;
        } else {
            double width = viewport.ActualWidth;
            double height = viewport.ActualHeight;
            if(width <= 0 || height <= 0) return;
            transform.Zoom = ViewTransform.FitZoom(original.PixelWidth, original.PixelHeight, width, height, minZoom, 1.0 / deviceScale);
            transform.Center(original.PixelWidth, original.PixelHeight, width, height);
            viewInitialized = true;
        }
        applyTransform();
    }

    private double minZoom => minZoomFactor / deviceScale;

    private double maxZoom => maxZoomFactor / deviceScale;

    private void applyTransform() {
        zoomTransform.ScaleX = transform.Zoom;
        zoomTransform.ScaleY = transform.Zoom;
        panTransform.X = transform.PanX;
        panTransform.Y = transform.PanY;
        viewModel.DevicePixelsPerImagePixel = transform.Zoom * deviceScale;
        if(mode == EditorMode.Windowed) Title = $"{Strings.EditorTitle} – {Math.Round(transform.Zoom * deviceScale * 100)} %";
        updateOverlay();
    }

    private void configureMagnifier() {
        AppSettings settings = AppServices.Settings.Settings;
        int zoom = Math.Clamp(settings.MagnifierZoom, AppSettings.MinMagnifierZoom, AppSettings.MaxMagnifierZoom);
        magnifier.Configure(original, zoom, settings.MagnifierSize, deviceScale);
    }

    /// <summary>
    /// Overlay (Abdunkelung, Rahmen, Anfasser, Größe), Werkzeugleiste und Lupe an den aktuellen Zustand anpassen.
    /// </summary>
    private void updateOverlay() {
        if(!IsLoaded || !viewInitialized) return;
        PixelRect? displayed = viewModel.DisplayedSelection;
        Rect imageArea = transform.ToView(viewModel.ImageBounds);
        Rect? selectionView = displayed.HasValue ? transform.ToView(displayed.Value) : null;
        selectionOverlay.Update(imageArea, selectionView, displayed, deviceScale, displayed.HasValue);
        annotationOverlay.Update(transform, deviceScale, viewModel.GetSelectionVisual(), viewModel.GetHiddenCursorBounds(), viewModel.ToolPreviewRect);
        updateTextEditorLayout();
        updateToolbar(displayed, selectionView);
        updateMagnifier();
    }

    private void updateToolbar(PixelRect? displayed, Rect? selectionView) {
        // Während eines Ziehvorgangs (Vorschau vorhanden) ausblenden, damit sie nicht mitspringt
        if(viewModel.PreviewSelection.HasValue) {
            toolbar.ClosePopups();
            toolbar.Visibility = Visibility.Hidden;
            return;
        }
        toolbar.Visibility = Visibility.Visible;
        toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        int monitor = displayed.HasValue
            ? SelectionGeometry.FindMonitorIndex(document.Monitors, displayed.Value)
            : SelectionGeometry.FindPrimaryMonitorIndex(document.Monitors);
        Point position = ToolbarPlacement.Place(selectionView, toolbar.DesiredSize, getMonitorView(monitor), toolbarGap);
        setOverlayPosition(toolbar, position);
    }

    private void updateMagnifier() {
        PixelPoint? focus = viewModel.MagnifierFocus;
        bool pointerHere = viewport.IsMouseOver || viewport.IsMouseCaptured || viewModel.IsKeyboardSelecting;
        if(!focus.HasValue || !pointerHere || isPanning) {
            magnifier.Visibility = Visibility.Collapsed;
            return;
        }
        magnifier.Show(focus.Value, viewModel.DisplayedSelection);
        magnifier.Visibility = Visibility.Visible;
        magnifier.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        int monitor = SelectionGeometry.FindMonitorIndex(document.Monitors, viewModel.CursorPixel);
        Point position = MagnifierPlacement.Place(getMouseInViewport(), magnifier.DesiredSize, getMonitorView(monitor), magnifierGap);
        setOverlayPosition(magnifier, position);
    }

    /// <summary>
    /// Monitor in Ansichts-DIPs, auf den sichtbaren Teil der Fläche begrenzt (ohne Monitor: die ganze Fläche).
    /// </summary>
    private Rect getMonitorView(int monitorIndex) {
        Rect visible = new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight);
        if(monitorIndex < 0 || monitorIndex >= document.Monitors.Count) return visible;
        Rect monitor = transform.ToView(document.Monitors[monitorIndex].Bounds);
        monitor.Intersect(visible);
        return monitor.IsEmpty || monitor.Width < 1 || monitor.Height < 1 ? visible : monitor;
    }

    private void setOverlayPosition(UIElement element, Point position) {
        // auf Gerätepixel runden, damit Text und Linien scharf bleiben
        Canvas.SetLeft(element, Math.Round(position.X * deviceScale) / deviceScale);
        Canvas.SetTop(element, Math.Round(position.Y * deviceScale) / deviceScale);
    }

    /// <summary>
    /// Mausposition relativ zur Fläche (über GetCursorPos, funktioniert auch ohne vorheriges Mausereignis).
    /// </summary>
    private Point getMouseInViewport() {
        if(PresentationSource.FromVisual(viewport) == null) return Mouse.GetPosition(viewport);
        if(!User32.GetCursorPos(out POINT point)) return Mouse.GetPosition(viewport);
        return viewport.PointFromScreen(new Point(point.X, point.Y));
    }
}
