using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels.Tools;
using ScreenKeeper.ViewModels.Undo;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Logik des Editors: Dokument, Rückgängig-Stapel, Werkzeuge, Bereich (inkl. Vorschau während des Ziehens) und
/// Ausgabe-Befehle. Arbeitet in Bildpixeln; Umrechnung und Eingabe macht das Fenster.
/// Zeichenobjekte, Objektauswahl, Text-Bearbeitung und Werkzeug-Optionen: siehe <c>EditorViewModel.Annotations.cs</c>.
/// </summary>
public sealed partial class EditorViewModel : ViewModelBase {
    public const int JpegQuality = 90;

    private readonly IEditorHost host;
    private IEditorTool activeTool;
    private PixelRect? previewSelection;
    private PixelPoint cursorPixel;
    private PixelRect? lastSelection;
    private bool isSameLayout;

    public EditorViewModel(CaptureDocument document, BitmapSource original, IEditorHost host)
        : this(document, original, host, null) {
    }

    /// <param name="settings">Quelle der Werkzeug-Optionen; null = Einstellungen der App (ohne App: Standardwerte)</param>
    public EditorViewModel(CaptureDocument document, BitmapSource original, IEditorHost host, AppSettings? settings) {
        Document = document;
        Original = original;
        this.host = host;
        ImageBounds = new PixelRect(0, 0, original.PixelWidth, original.PixelHeight);
        InitialSelection = document.Selection;
        UndoStack = new UndoStack();
        UndoStack.Changed += onUndoStackChanged;

        SelectionTool = new SelectionTool(this);
        Tools = new ObservableCollection<ToolItem> { new ToolItem(SelectionTool) };
        activeTool = SelectionTool;
        Tools[0].IsActive = true;
        AppSettings source = settings ?? (AppServices.IsInitialized ? AppServices.Settings.Settings : new AppSettings());
        Options = ToolOptions.FromSettings(source);
        lastSelection = source.LastSelection;
        Measure = new AnnotationMeasure(document);
        initializeAnnotationTools();

        CopySelectionCommand = new RelayCommand(CopySelection, () => HasSelection);
        SaveSelectionCommand = new RelayCommand(SaveSelection, () => HasSelection);
        SaveSelectionAsCommand = new RelayCommand(SaveSelectionAs, () => HasSelection);
        CopyFullCommand = new RelayCommand(CopyFull);
        CopyMonitorCommand = new RelayCommand(CopyMonitor);
        UndoCommand = new RelayCommand(() => Undo(), () => UndoStack.CanUndo);
        RedoCommand = new RelayCommand(() => Redo(), () => UndoStack.CanRedo);
        CancelCommand = new RelayCommand(host.CloseEditor);
        DiscardCommand = new RelayCommand(Discard);
        DuplicateCommand = new RelayCommand(() => {
            CancelGesture();
            DuplicateRequested?.Invoke(this, EventArgs.Empty);
        });
        TogglePermanentCommand = new RelayCommand(() => IsPermanent = !IsPermanent);
        ApplyLastSelectionCommand = new RelayCommand(() => ApplyLastSelection(), () => LastSelectionState == LastSelectionState.Available);
        EditCommentCommand = new RelayCommand(() => {
            CancelGesture();
            CommentEditRequested?.Invoke(this, EventArgs.Empty);
        });
        SelectToolCommand = new RelayCommand(p => {
            if(p is ToolItem item) SelectTool(item.Tool);
        });
    }

    public CaptureDocument Document { get; }

    public BitmapSource Original { get; }

    /// <summary>Das ganze Bild (0, 0, Breite, Höhe).</summary>
    public PixelRect ImageBounds { get; }

    public UndoStack UndoStack { get; }

    public SelectionTool SelectionTool { get; }

    /// <summary>Werkzeug-Knöpfe der Werkzeugleiste: Bereich, Auswahl und die Zeichenwerkzeuge.</summary>
    public ObservableCollection<ToolItem> Tools { get; }

    public IEditorTool ActiveTool => activeTool;

    /// <summary>Gespeicherter Bereich (im Dokument).</summary>
    public PixelRect? Selection => Document.Selection;

    public bool HasSelection => Document.Selection.HasValue;

    /// <summary>Bereich während eines Ziehvorgangs (noch nicht im Dokument); null = keiner.</summary>
    public PixelRect? PreviewSelection {
        get => previewSelection;
        set => previewSelection = value;
    }

    /// <summary>Angezeigter Bereich: Vorschau, sonst der gespeicherte.</summary>
    public PixelRect? DisplayedSelection => previewSelection ?? Document.Selection;

    /// <summary>Letzte bekannte Cursor-Position in Bildpixeln (Maus oder Pfeiltasten).</summary>
    public PixelPoint CursorPixel => cursorPixel;

    /// <summary>Gerätepixel je Bildpixel (vom Fenster gesetzt; für Trefferflächen in konstanter Bildschirmgröße).</summary>
    public double DevicePixelsPerImagePixel { get; set; } = 1;

    public bool IsGestureActive => activeTool.IsGestureActive;

    public PixelPoint? MagnifierFocus => activeTool.MagnifierFocus;

    public ICommand CopySelectionCommand { get; }

    public ICommand SaveSelectionCommand { get; }

    public ICommand SaveSelectionAsCommand { get; }

    public ICommand CopyFullCommand { get; }

    public ICommand CopyMonitorCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>„Aufnahme verwerfen“: Editor schließen, nichts speichern, Dateien in den Papierkorb (macht das Fenster).</summary>
    public ICommand DiscardCommand { get; }

    /// <summary>„Kopie anlegen“: aktuellen Stand als neue Aufnahme speichern und dort weiterarbeiten (macht das Fenster).</summary>
    public ICommand DuplicateCommand { get; }

    public ICommand TogglePermanentCommand { get; }

    /// <summary>
    /// Permanent markiert (wird beim automatischen Aufräumen nie gelöscht). Eigenschaft der Datei, kein Rückgängig-Schritt;
    /// gespeichert wird sie mit dem Dokument beim Schließen.
    /// </summary>
    public bool IsPermanent {
        get => Document.Permanent;
        set {
            if(Document.Permanent == value) return;
            Document.Permanent = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Der Knopf „Kopie anlegen“ wurde gedrückt (ein laufender Vorgang ist bereits abgeschlossen).</summary>
    public event EventHandler? DuplicateRequested;

    /// <summary>„Kommentar“: Eingabedialog öffnen (macht das Fenster) und das Ergebnis mit <see cref="SetComment"/> übernehmen.</summary>
    public ICommand EditCommentCommand { get; }

    /// <summary>Der Knopf „Kommentar“ wurde gedrückt (ein laufender Vorgang ist bereits abgeschlossen).</summary>
    public event EventHandler? CommentEditRequested;

    /// <summary>Kommentar der Aufnahme; null = keiner.</summary>
    public string? Comment => Document.Comment;

    public bool HasComment => Document.HasComment;

    /// <summary>Tooltip des Knopfs „Kommentar“: ohne Kommentar die Aktion, sonst zusätzlich der Anfang des Kommentars.</summary>
    public string CommentToolTip => HasComment ? Strings.Format(Strings.ToolbarCommentExisting, CommentText.Preview(Document.Comment, CommentText.ToolTipLength)) : Strings.ToolbarComment;

    /// <summary>
    /// Kommentar über den Rückgängig-Stapel setzen (ein Schritt; leer = entfernen). False, wenn sich nichts ändert.
    /// </summary>
    public bool SetComment(string? comment) {
        string? normalized = CaptureDocument.NormalizeComment(comment);
        if(Document.Comment == normalized) return false;
        UndoStack.Execute(new SetCommentCommand(Document, Document.Comment, normalized));
        return true;
    }

    public ICommand SelectToolCommand { get; }

    /// <summary>Bereich des Dokuments beim Öffnen des Editors (zum Erkennen, ob er in dieser Sitzung geändert wurde).</summary>
    public PixelRect? InitialSelection { get; }

    /// <summary>„Letzten Bereich“: gemerkten Bereich auf diese Aufnahme übernehmen (ein Rückgängig-Schritt).</summary>
    public ICommand ApplyLastSelectionCommand { get; }

    /// <summary>
    /// Zuletzt gemerkter Bereich in Bildschirmkoordinaten (aus den Einstellungen; null = keiner).
    /// </summary>
    public PixelRect? LastSelection {
        get => lastSelection;
        set {
            if(lastSelection == value) return;
            lastSelection = value;
            onLastSelectionStateChanged();
        }
    }

    /// <summary>
    /// Monitor-Anordnung der Aufnahme = aktuelle Anordnung? Nur dann passen Bildschirmkoordinaten (setzt das Fenster:
    /// Vollbild-Modus wird genau dann gewählt).
    /// </summary>
    public bool IsSameLayout {
        get => isSameLayout;
        set {
            if(isSameLayout == value) return;
            isSameLayout = value;
            onLastSelectionStateChanged();
        }
    }

    public LastSelectionState LastSelectionState => LastSelectionRules.GetState(lastSelection, isSameLayout, Document.VirtualScreen, ImageBounds);

    /// <summary>Tooltip des Knopfs „Letzten Bereich“ (nennt den Grund, wenn er ausgegraut ist).</summary>
    public string LastSelectionToolTip {
        get {
            switch(LastSelectionState) {
                case LastSelectionState.None: return Strings.ToolbarLastSelectionNone;
                case LastSelectionState.OtherLayout: return Strings.ToolbarLastSelectionOtherLayout;
                case LastSelectionState.OutsideImage: return Strings.ToolbarLastSelectionOutside;
            }
            return Strings.ToolbarLastSelection;
        }
    }

    /// <summary>
    /// Gemerkten Bereich in Bildpixel dieser Aufnahme umrechnen, auf das Bild begrenzen und über denselben Rückgängig-Befehl
    /// wie die Maus setzen. False, wenn nicht möglich oder der Bereich schon so gesetzt ist.
    /// </summary>
    public bool ApplyLastSelection() {
        if(LastSelectionState != LastSelectionState.Available || !lastSelection.HasValue) return false;
        PixelRect? target = LastSelectionRules.ToImage(lastSelection.Value, Document.VirtualScreen, ImageBounds);
        if(!target.HasValue) return false;
        CancelGesture();
        if(Document.Selection == target) return false;
        SetSelection(target);
        return true;
    }

    /// <summary>
    /// Beim Schließen: zu merkender Bereich in Bildschirmkoordinaten, oder null, wenn nichts gemerkt werden soll
    /// (siehe <see cref="LastSelectionRules.ShouldRemember"/>).
    /// </summary>
    public PixelRect? GetSelectionToRemember(bool discarded) {
        if(!LastSelectionRules.ShouldRemember(InitialSelection, Document.Selection, isSameLayout, discarded)) return null;
        return LastSelectionRules.ToScreen(Document.Selection!.Value, Document.VirtualScreen);
    }

    private void onLastSelectionStateChanged() {
        OnPropertyChanged(nameof(LastSelection));
        OnPropertyChanged(nameof(LastSelectionState));
        OnPropertyChanged(nameof(LastSelectionToolTip));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Bereich, Vorschau, Lupe oder Werkzeugzustand haben sich geändert (Overlay neu zeichnen).</summary>
    public event EventHandler? OverlayChanged;

    /// <summary>Das Dokument wurde über den Rückgängig-Stapel geändert (bei Annotationen Zeichnung neu aufbauen).</summary>
    public event EventHandler<DocumentChangeKind>? DocumentChanged;

    public void NotifyOverlayChanged() {
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectTool(IEditorTool tool) {
        if(ReferenceEquals(tool, activeTool)) return;
        if(IsTextEditing) CommitTextEdit();
        activeTool.Deactivate();
        activeTool = tool;
        foreach(ToolItem item in Tools) item.IsActive = ReferenceEquals(item.Tool, tool);
        // Zeichenwerkzeuge zeigen ihre eigenen Optionen; die Objektauswahl gilt nur für „Auswahl“ und „Bereich“
        if(!isSelectingTool(tool)) selectedAnnotationId = null;
        tool.Activate();
        OnPropertyChanged(nameof(ActiveTool));
        refreshOptions();
        NotifyOverlayChanged();
    }

    public void PointerDown(PointerInput input) {
        cursorPixel = input.Pixel;
        // Klick neben das Textfeld übernimmt den Text
        if(IsTextEditing) CommitTextEdit();
        if(input.ClickCount >= 2 && tryEditTextAt(input)) return;
        activeTool.PointerDown(input);
    }

    public void PointerMove(PointerInput input) {
        cursorPixel = input.Pixel;
        activeTool.PointerMove(input);
    }

    public void PointerUp(PointerInput input) {
        cursorPixel = input.Pixel;
        activeTool.PointerUp(input);
    }

    public EditorCursor GetCursor(PointerInput input) {
        return activeTool.GetCursor(input);
    }

    /// <summary>
    /// Laufenden Vorgang abbrechen (Esc, Mausfang verloren, vor Rückgängig/Ausgabe/Schließen). Ein offenes Textfeld wird
    /// dabei übernommen, damit kein getippter Text verloren geht.
    /// </summary>
    public void CancelGesture() {
        if(IsTextEditing) CommitTextEdit();
        activeTool.CancelGesture();
    }

    /// <summary>Läuft eine Tastatur-Auswahl (Startpunkt gesetzt)?</summary>
    public bool IsKeyboardSelecting => ReferenceEquals(activeTool, SelectionTool) && SelectionTool.IsKeyboardSelecting;

    /// <summary>
    /// Leertaste/Enter: Start- bzw. Endpunkt der Tastatur-Auswahl am Cursor setzen (wechselt zum Werkzeug „Bereich“).
    /// </summary>
    public void KeyboardMark() {
        if(!ReferenceEquals(activeTool, SelectionTool)) SelectTool(SelectionTool);
        SelectionTool.KeyboardMark(cursorPixel);
    }

    /// <summary>
    /// Bereich über den Rückgängig-Stapel setzen (auf das Bild begrenzt). Gleicher Weg wie bei der Maus.
    /// </summary>
    public void SetSelection(PixelRect? selection) {
        PixelRect? clamped = selection.HasValue ? SelectionGeometry.ClampRect(selection.Value, ImageBounds) : null;
        CommitSelection(Document.Selection, clamped);
    }

    /// <summary>
    /// Ein Bereichswechsel als ein Rückgängig-Schritt (nichts, wenn sich nichts ändert).
    /// </summary>
    public void CommitSelection(PixelRect? oldSelection, PixelRect? newSelection) {
        if(oldSelection == newSelection) return;
        UndoStack.Execute(new SetSelectionCommand(Document, oldSelection, newSelection));
    }

    public bool Undo() {
        CancelGesture();
        return UndoStack.Undo();
    }

    public bool Redo() {
        CancelGesture();
        return UndoStack.Redo();
    }

    public void CopySelection() {
        if(!Document.Selection.HasValue) return;
        copyRegion(Document.Selection.Value);
    }

    public void CopyFull() {
        copyRegion(ImageBounds);
    }

    public void CopyMonitor() {
        copyRegion(GetMonitorRegion(host.GetCursorImagePoint()));
    }

    /// <summary>
    /// Aufnahme verwerfen. Ein laufender Vorgang wird nur abgebrochen (ein offenes Textfeld nicht übernommen – es wird ohnehin
    /// nichts gespeichert).
    /// </summary>
    public void Discard() {
        CancelTextEdit();
        activeTool.CancelGesture();
        host.DiscardCapture();
    }

    public void SaveSelection() {
        if(!Document.Selection.HasValue) return;
        string path;
        try {
            path = GetExportPath(AppServices.Storage.ExportFolder, AppServices.Settings.Settings.FileNamePattern, Document.CapturedAt.LocalDateTime);
        } catch(Exception ex) {
            Log.Error(ex, "Exportpfad konnte nicht bestimmt werden");
            host.ShowError(Strings.Format(Strings.ErrorSaveFailed, ex.Message));
            return;
        }
        saveRegion(Document.Selection.Value, path);
    }

    public void SaveSelectionAs() {
        if(!Document.Selection.HasValue) return;
        PixelRect region = Document.Selection.Value;
        CancelGesture();
        string folder = AppServices.Storage.ExportFolder;
        string fileName = StorageService.FormatFileName(AppServices.Settings.Settings.FileNamePattern, Document.CapturedAt.LocalDateTime) + ".png";
        string? path = host.AskSaveAsPath(folder, fileName);
        if(string.IsNullOrWhiteSpace(path)) return;
        saveRegion(region, path);
    }

    /// <summary>
    /// Bereich des Monitors, auf dem <paramref name="point"/> liegt (sonst des nächsten); ohne Monitore das ganze Bild.
    /// </summary>
    public PixelRect GetMonitorRegion(PixelPoint point) {
        int index = SelectionGeometry.FindMonitorIndex(Document.Monitors, point);
        if(index < 0) return ImageBounds;
        return SelectionGeometry.ClampRect(Document.Monitors[index].Bounds, ImageBounds) ?? ImageBounds;
    }

    /// <summary>
    /// Freier Dateiname im Exportordner: Dateinamen-Muster mit der Aufnahmezeit, bei Namensgleichheit _2, _3 …
    /// </summary>
    public static string GetExportPath(string exportFolder, string pattern, DateTime time) {
        string baseName = StorageService.GetUniqueBaseName(exportFolder, StorageService.FormatFileName(pattern, time));
        return Path.Combine(exportFolder, baseName + CaptureFiles.PngExtension);
    }

    public static bool IsJpegPath(string path) {
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private void copyRegion(PixelRect region) {
        CancelGesture();
        try {
            BitmapSource image = AppServices.Render.Render(Document, Original, region);
            AppServices.Clipboard.SetImage(image);
            Log.Info($"Zwischenablage: {region.Width}×{region.Height} bei {region.X},{region.Y}");
        } catch(Exception ex) {
            Log.Error(ex, "Kopieren in die Zwischenablage fehlgeschlagen");
            host.ShowError(Strings.Format(Strings.ErrorClipboardFailed, ex.Message));
            return;
        }
        host.CloseEditor();
    }

    private void saveRegion(PixelRect region, string path) {
        CancelGesture();
        try {
            string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if(!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            BitmapSource image = AppServices.Render.Render(Document, Original, region);
            if(IsJpegPath(path)) {
                ImageFiles.WriteJpeg(image, path, JpegQuality);
            } else {
                ImageFiles.WritePng(image, path);
            }
            Log.Info($"Bereich {region.Width}×{region.Height} gespeichert: {path}");
        } catch(Exception ex) {
            Log.Error(ex, $"Speichern nach {path} fehlgeschlagen");
            host.ShowError(Strings.Format(Strings.ErrorSaveFailed, ex.Message));
            return;
        }
        host.CloseEditor();
    }

    private void onUndoStackChanged(object? sender, UndoStackChangedEventArgs e) {
        DocumentChangeKind kind = e.Command is EditorCommand command ? command.Changes : DocumentChangeKind.Selection | DocumentChangeKind.Annotations;
        if((kind & DocumentChangeKind.Selection) != 0) {
            OnPropertyChanged(nameof(Selection));
            OnPropertyChanged(nameof(HasSelection));
        }
        if((kind & DocumentChangeKind.Comment) != 0) {
            OnPropertyChanged(nameof(Comment));
            OnPropertyChanged(nameof(HasComment));
            OnPropertyChanged(nameof(CommentToolTip));
        }
        if((kind & DocumentChangeKind.Annotations) != 0) {
            // ausgewähltes Objekt kann durch Rückgängig verschwunden sein
            if(selectedAnnotationId != null && SelectedAnnotation == null) selectedAnnotationId = null;
            refreshOptions();
        }
        DocumentChanged?.Invoke(this, kind);
        NotifyOverlayChanged();
        CommandManager.InvalidateRequerySuggested();
    }
}
