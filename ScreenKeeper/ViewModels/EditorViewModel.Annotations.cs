using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels.Tools;
using ScreenKeeper.ViewModels.Undo;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Darstellung des ausgewählten Objekts im Overlay (Bildpixel): Umriss, Anfasser-Mittelpunkte und ob ein Rahmen
/// gezeichnet wird (bei Linien/Pfeilen nur die Endpunkte).
/// </summary>
public sealed record ObjectSelectionVisual(Rect Bounds, IReadOnlyList<Point> Handles, bool ShowFrame);

/// <summary>
/// Zeichenobjekte im Editor: Werkzeuge, Objektauswahl, Inline-Textbearbeitung und die Optionen der Werkzeugleiste
/// (Farbe, Strichstärke …). Optionen wirken auf das gerade bearbeitete bzw. ausgewählte Objekt (als Rückgängig-Schritt)
/// und werden zugleich als zuletzt verwendete Werte gemerkt.
/// </summary>
public sealed partial class EditorViewModel {
    /// <summary>Treffer-Zuschlag für Zeichenobjekte in Gerätepixeln.</summary>
    public const double HitToleranceDevice = 4;

    /// <summary>Versatz eines duplizierten Objekts in Bildpixeln.</summary>
    public const int DuplicateOffset = 10;

    private const double handleHitExtraDevice = 3;

    private static readonly string[] optionProperties = {
        nameof(ShowColorOption), nameof(ShowThicknessOption), nameof(ShowFillColorOption), nameof(ShowTextBackgroundOption),
        nameof(ShowLineCapOptions), nameof(ShowSmoothOption),
        nameof(ShowFontSizeOption), nameof(ShowBlockSizeOption), nameof(ShowBlurRadiusOption), nameof(ShowStampSizeOption),
        nameof(ShowCursorKindOption), nameof(ShowCursorVisibleOption), nameof(ShowOptions), nameof(HasSelectedAnnotation),
        nameof(CurrentColor), nameof(CurrentColorBrush), nameof(CurrentThickness), nameof(CurrentThicknessText),
        nameof(CurrentFillColor), nameof(CurrentFillBrush), nameof(IsTextBackgroundActive),
        nameof(CurrentStartCap), nameof(CurrentEndCap), nameof(CurrentStartCapIcon), nameof(CurrentEndCapIcon),
        nameof(IsSmoothActive), nameof(CurrentFontSizeText),
        nameof(CurrentBlockSizeText), nameof(CurrentBlurRadiusText), nameof(CurrentStampSizeText),
        nameof(CurrentCursorKindLabel), nameof(IsCursorVisible), nameof(SelectedAnnotation)
    };

    /// <summary>Deckkraft der halbtransparenten Füllfarben in der Werkzeugleiste.</summary>
    public const byte TranslucentFillAlpha = 0x80;

    private string? selectedAnnotationId;
    private TextAnnotation? editingText;
    private bool editingIsNew;
    private IReadOnlyList<OptionChoice>? colorChoices;
    private IReadOnlyList<OptionChoice>? thicknessChoices;
    private IReadOnlyList<OptionChoice>? fontSizeChoices;
    private IReadOnlyList<OptionChoice>? blockSizeChoices;
    private IReadOnlyList<OptionChoice>? blurRadiusChoices;
    private IReadOnlyList<OptionChoice>? stampSizeChoices;
    private IReadOnlyList<OptionChoice>? cursorKindChoices;
    private IReadOnlyList<OptionChoice>? fillColorChoices;
    private IReadOnlyList<OptionChoice>? startCapChoices;
    private IReadOnlyList<OptionChoice>? endCapChoices;
    private ICommand? toggleTextBackgroundCommand;
    private ICommand? toggleSmoothCommand;
    private ICommand? toggleCursorVisibleCommand;
    private ICommand? deleteSelectedCommand;

    /// <summary>Aktuelle Werkzeug-Optionen (zuletzt verwendete Werte).</summary>
    public ToolOptions Options { get; }

    /// <summary>Text- und Zeigermaße für Treffertests und Anfasser.</summary>
    public IAnnotationMeasure Measure { get; }

    public ObjectSelectTool ObjectSelectTool => (ObjectSelectTool)FindTool(ObjectSelectTool.ToolKey)!;

    /// <summary>Treffer-Zuschlag in Bildpixeln (konstant in Gerätepixeln).</summary>
    public double HitTolerance => HitToleranceDevice / Math.Max(0.01, DevicePixelsPerImagePixel);

    /// <summary>Halbe Trefferfläche eines Objekt-Anfassers in Bildpixeln.</summary>
    public double HandleRadius => (SelectionTool.HandleSizeDevice / 2 + handleHitExtraDevice) / Math.Max(0.01, DevicePixelsPerImagePixel);

    /// <summary>Rahmen, den ein Werkzeug gerade aufzieht, ohne schon ein Objekt zu sein (Textfeld); null = keiner.</summary>
    public PixelRect? ToolPreviewRect { get; set; }

    /// <summary>Gibt es einen aufgenommenen Zeiger (nur dann wird „aufgenommener Zeiger“ angeboten)?</summary>
    public bool HasCapturedCursor => !string.IsNullOrEmpty(Document.Cursor?.Png);

    /// <summary>Zeigerart für neue virtuelle Mäuse (ohne aufgenommenen Zeiger: Pfeil statt „aufgenommen“).</summary>
    public string EffectiveCursorKind => Options.CursorKind == CursorKinds.Captured && !HasCapturedCursor ? CursorKinds.Arrow : Options.CursorKind;

    /// <summary>Wird ausgelöst, wenn die Text-Bearbeitung beginnt, endet oder sich ihr Aussehen ändert.</summary>
    public event EventHandler? TextEditChanged;

    public IEditorTool? FindTool(string key) {
        foreach(ToolItem item in Tools) {
            if(item.Tool.Key == key) return item.Tool;
        }
        return null;
    }

    /// <summary>Werkzeug über seinen Schlüssel wählen (Tastenkürzel); false, wenn es keins gibt.</summary>
    public bool SelectToolByKey(string key) {
        IEditorTool? tool = FindTool(key);
        if(tool == null) return false;
        SelectTool(tool);
        return true;
    }

    /// <summary>
    /// Zeichenobjekte wurden live geändert (Vorschau beim Ziehen): Zeichnung und Overlay neu aufbauen.
    /// </summary>
    public void NotifyAnnotationsChanged() {
        DocumentChanged?.Invoke(this, DocumentChangeKind.Annotations);
        NotifyOverlayChanged();
    }

    /// <summary>Wird das Objekt gezeichnet? (Der gerade bearbeitete Text wird vom Textfeld dargestellt.)</summary>
    public bool IsAnnotationDrawn(Annotation annotation) {
        return editingText == null || editingIsNew || annotation.Id != editingText.Id;
    }

    // ---------- Objektauswahl ----------

    public string? SelectedAnnotationId => selectedAnnotationId;

    public Annotation? SelectedAnnotation => AnnotationList.Find(Document, selectedAnnotationId);

    public bool HasSelectedAnnotation => SelectedAnnotation != null;

    public void SelectAnnotation(Annotation? annotation) {
        string? id = annotation?.Id;
        if(id == selectedAnnotationId) return;
        selectedAnnotationId = id;
        refreshOptions();
        NotifyOverlayChanged();
    }

    /// <summary>
    /// Oberstes Objekt unter der Maus (auch ausgeblendete Zeiger, damit man sie wieder einblenden kann).
    /// </summary>
    public Annotation? HitTestAnnotation(PointerInput input) {
        return AnnotationHitTesting.FindTopmost(Document.Annotations, input.X, input.Y, HitTolerance, Measure);
    }

    public void DeleteSelectedAnnotation() {
        Annotation? selected = SelectedAnnotation;
        if(selected == null) return;
        CancelGesture();
        selectedAnnotationId = null;
        UndoStack.Execute(new RemoveAnnotationCommand(Document, selected));
    }

    /// <summary>
    /// Kopie des ausgewählten Objekts, leicht versetzt, direkt darüber; die Kopie wird ausgewählt.
    /// </summary>
    public void DuplicateSelectedAnnotation() {
        Annotation? selected = SelectedAnnotation;
        if(selected == null || selected is UnknownAnnotation) return;
        CancelGesture();
        Annotation copy = AnnotationEditing.Moved(selected, DuplicateOffset, DuplicateOffset);
        copy.Id = Guid.NewGuid().ToString("N");
        if(copy is NumberStampAnnotation stamp) stamp.Number = NumberStampTool.GetNextNumber(Document.Annotations);
        int index = AnnotationList.IndexOf(Document, selected.Id) + 1;
        UndoStack.Execute(new AddAnnotationCommand(Document, copy, index, Strings.UndoDuplicateAnnotation));
        SelectAnnotation(copy);
    }

    /// <summary>
    /// Rahmen und Anfasser des ausgewählten Objekts (null: nichts ausgewählt oder Text wird bearbeitet).
    /// </summary>
    public ObjectSelectionVisual? GetSelectionVisual() {
        if(editingText != null) return null;
        Annotation? selected = SelectedAnnotation;
        if(selected == null) return null;
        Rect bounds = AnnotationEditing.GetBounds(selected, Measure);
        List<Point> handles = AnnotationEditing.GetHandles(selected).Select(h => AnnotationEditing.GetHandlePoint(selected, h, Measure)).ToList();
        bool frame = selected is not LineAnnotation && selected is not ArrowAnnotation;
        return new ObjectSelectionVisual(bounds, handles, frame);
    }

    /// <summary>
    /// Umrisse der ausgeblendeten Zeiger (das Overlay deutet sie an, damit man sie auswählen und einblenden kann).
    /// </summary>
    public IReadOnlyList<Rect> GetHiddenCursorBounds() {
        List<Rect> result = new List<Rect>();
        foreach(Annotation annotation in Document.Annotations) {
            if(annotation is CursorAnnotation cursor && !cursor.Visible) result.Add(Measure.GetCursorBounds(cursor));
        }
        return result;
    }

    // ---------- Text-Bearbeitung ----------

    /// <summary>Der gerade bearbeitete Text (Arbeitskopie); null = keine Bearbeitung.</summary>
    public TextAnnotation? EditingText => editingText;

    public bool IsTextEditing => editingText != null;

    public bool IsEditingNewText => editingText != null && editingIsNew;

    /// <summary>
    /// Inline-Bearbeitung beginnen: mit einem neuen Text (<paramref name="isNew"/>) oder einem vorhandenen.
    /// Das Fenster zeigt dafür ein Textfeld an der Stelle; der vorhandene Text wird solange nicht gezeichnet.
    /// </summary>
    public void BeginTextEdit(TextAnnotation annotation, bool isNew) {
        if(editingText != null) CommitTextEdit();
        editingText = (TextAnnotation)annotation.Clone();
        editingIsNew = isNew;
        selectedAnnotationId = isNew || !isSelectingTool(activeTool) ? null : annotation.Id;
        TextEditChanged?.Invoke(this, EventArgs.Empty);
        refreshOptions();
        NotifyAnnotationsChanged();
    }

    public void UpdateEditingText(string text) {
        if(editingText != null) editingText.Text = text;
    }

    /// <summary>
    /// Text übernehmen (Enter, Klick daneben): neu → einfügen; geändert → ein Rückgängig-Schritt; leer → Objekt löschen.
    /// </summary>
    public void CommitTextEdit() {
        TextAnnotation? text = editingText;
        if(text == null) return;
        bool isNew = editingIsNew;
        editingText = null;
        editingIsNew = false;
        bool empty = string.IsNullOrWhiteSpace(text.Text);
        if(isNew) {
            if(!empty) {
                UndoStack.Execute(new AddAnnotationCommand(Document, text));
                selectedAnnotationId = text.Id;
            }
        } else {
            Annotation? original = AnnotationList.Find(Document, text.Id);
            if(original != null) {
                if(empty) {
                    if(selectedAnnotationId == original.Id) selectedAnnotationId = null;
                    UndoStack.Execute(new RemoveAnnotationCommand(Document, original));
                } else if(original.ToJson() != text.ToJson()) {
                    UndoStack.Execute(new ChangeAnnotationCommand(Document, original, text, Strings.UndoEditText));
                }
            }
        }
        TextEditChanged?.Invoke(this, EventArgs.Empty);
        refreshOptions();
        NotifyAnnotationsChanged();
    }

    /// <summary>Bearbeitung verwerfen (Esc): neuer Text entfällt, vorhandener bleibt unverändert.</summary>
    public void CancelTextEdit() {
        if(editingText == null) return;
        editingText = null;
        editingIsNew = false;
        TextEditChanged?.Invoke(this, EventArgs.Empty);
        refreshOptions();
        NotifyAnnotationsChanged();
    }

    // ---------- Optionen der Werkzeugleiste ----------

    public IReadOnlyList<OptionChoice> ColorChoices => colorChoices ??= AnnotationColors.Palette
        .Select(c => new OptionChoice(OptionKind.Color, c, c, AnnotationColors.GetBrush(c))).ToList();

    public IReadOnlyList<OptionChoice> ThicknessChoices => thicknessChoices ??= ToolOptions.Thicknesses
        .Select(t => new OptionChoice(OptionKind.Thickness, t, formatPixels(t))).ToList();

    public IReadOnlyList<OptionChoice> FontSizeChoices => fontSizeChoices ??= ToolOptions.FontSizes
        .Select(s => new OptionChoice(OptionKind.FontSize, s, formatPixels(s))).ToList();

    public IReadOnlyList<OptionChoice> BlockSizeChoices => blockSizeChoices ??= ToolOptions.BlockSizes
        .Select(s => new OptionChoice(OptionKind.BlockSize, s, formatPixels(s))).ToList();

    public IReadOnlyList<OptionChoice> BlurRadiusChoices => blurRadiusChoices ??= ToolOptions.BlurRadii
        .Select(r => new OptionChoice(OptionKind.BlurRadius, r, formatPixels(r))).ToList();

    public IReadOnlyList<OptionChoice> StampSizeChoices => stampSizeChoices ??= ToolOptions.StampSizes
        .Select(s => new OptionChoice(OptionKind.StampSize, s, formatPixels(s))).ToList();

    /// <summary>Zeigerarten; „aufgenommener Zeiger“ nur, wenn bei der Aufnahme ein Zeigerbild erfasst wurde.</summary>
    public IReadOnlyList<OptionChoice> CursorKindChoices => cursorKindChoices ??= createCursorKindChoices();

    /// <summary>Füllfarben: keine, die Palette deckend und die Palette mit 50 % Deckkraft.</summary>
    public IReadOnlyList<OptionChoice> FillColorChoices => fillColorChoices ??= createFillColorChoices();

    public IReadOnlyList<OptionChoice> StartCapChoices => startCapChoices ??= LineCaps.All
        .Select(c => new OptionChoice(OptionKind.StartCap, c, LineCapIcons.GetLabel(c), null, LineCapIcons.Get(c, true))).ToList();

    public IReadOnlyList<OptionChoice> EndCapChoices => endCapChoices ??= LineCaps.All
        .Select(c => new OptionChoice(OptionKind.EndCap, c, LineCapIcons.GetLabel(c), null, LineCapIcons.Get(c, false))).ToList();

    public ICommand ToggleTextBackgroundCommand => toggleTextBackgroundCommand ??= new RelayCommand(ToggleTextBackground);

    public ICommand ToggleSmoothCommand => toggleSmoothCommand ??= new RelayCommand(ToggleSmooth);

    public ICommand ToggleCursorVisibleCommand => toggleCursorVisibleCommand ??= new RelayCommand(ToggleCursorVisible);

    public ICommand DeleteSelectedCommand => deleteSelectedCommand ??= new RelayCommand(DeleteSelectedAnnotation);

    public bool ShowColorOption => isTarget(AnnotationTypes.Rectangle, AnnotationTypes.Ellipse, AnnotationTypes.Line, AnnotationTypes.Arrow,
        AnnotationTypes.Freehand, AnnotationTypes.Text, AnnotationTypes.Highlight, AnnotationTypes.NumberStamp);

    public bool ShowThicknessOption => isTarget(AnnotationTypes.Rectangle, AnnotationTypes.Ellipse, AnnotationTypes.Line, AnnotationTypes.Arrow, AnnotationTypes.Freehand);

    public bool ShowFillColorOption => isTarget(AnnotationTypes.Rectangle, AnnotationTypes.Ellipse);

    public bool ShowTextBackgroundOption => isTarget(AnnotationTypes.Text);

    /// <summary>„Anfang“ und „Ende“ (Linienenden) für Linie, Pfeil und Freihand.</summary>
    public bool ShowLineCapOptions => isTarget(AnnotationTypes.Line, AnnotationTypes.Arrow, AnnotationTypes.Freehand);

    public bool ShowSmoothOption => isTarget(AnnotationTypes.Freehand);

    public bool ShowFontSizeOption => isTarget(AnnotationTypes.Text);

    public bool ShowBlockSizeOption => isTarget(AnnotationTypes.Pixelate);

    public bool ShowBlurRadiusOption => isTarget(AnnotationTypes.Blur);

    public bool ShowStampSizeOption => isTarget(AnnotationTypes.NumberStamp);

    public bool ShowCursorKindOption => isTarget(AnnotationTypes.Cursor);

    /// <summary>Ein-/Ausblenden gibt es nur für eine ausgewählte virtuelle Maus.</summary>
    public bool ShowCursorVisibleOption => editingText == null && SelectedAnnotation is CursorAnnotation;

    /// <summary>Gibt es überhaupt Optionen oder einen Löschen-Knopf (für das Trennzeichen in der Werkzeugleiste)?</summary>
    public bool ShowOptions => optionTargetType != null;

    public string CurrentColor {
        get {
            switch(optionTargetAnnotation) {
                case RectangleAnnotation rectangle: return rectangle.Stroke;
                case EllipseAnnotation ellipse: return ellipse.Stroke;
                case LineAnnotation line: return line.Stroke;
                case ArrowAnnotation arrow: return arrow.Stroke;
                case FreehandAnnotation freehand: return freehand.Stroke;
                case TextAnnotation text: return text.Foreground;
                case HighlightAnnotation highlight: return highlight.Color;
                case NumberStampAnnotation stamp: return stamp.Color;
            }
            return optionTargetType == AnnotationTypes.Highlight ? Options.HighlightColor : Options.Color;
        }
    }

    public Brush CurrentColorBrush => AnnotationColors.GetBrush(CurrentColor);

    public double CurrentThickness {
        get {
            switch(optionTargetAnnotation) {
                case RectangleAnnotation rectangle: return rectangle.Thickness;
                case EllipseAnnotation ellipse: return ellipse.Thickness;
                case LineAnnotation line: return line.Thickness;
                case ArrowAnnotation arrow: return arrow.Thickness;
                case FreehandAnnotation freehand: return freehand.Thickness;
            }
            return Options.Thickness;
        }
    }

    public string CurrentThicknessText => formatPixels(CurrentThickness);

    /// <summary>Füllfarbe von Rechteck/Ellipse (#AARRGGBB); leer = keine Füllung.</summary>
    public string CurrentFillColor {
        get {
            switch(optionTargetAnnotation) {
                case RectangleAnnotation rectangle: return rectangle.Fill ?? "";
                case EllipseAnnotation ellipse: return ellipse.Fill ?? "";
            }
            return Options.FillColor;
        }
    }

    public Brush CurrentFillBrush => LineCapIcons.GetFillBrush(CurrentFillColor);

    /// <summary>Hintergrund hinter Text aktiv?</summary>
    public bool IsTextBackgroundActive => optionTargetAnnotation is TextAnnotation text ? !string.IsNullOrEmpty(text.Background) : Options.TextBackground;

    /// <summary>Symbol am Anfang von Linie/Pfeil/Freihand (ausgewähltes Objekt bzw. Standard des Werkzeugs).</summary>
    public string CurrentStartCap => optionTargetAnnotation is ILineCapAnnotation line ? line.StartCap : Options.GetStartCap(optionTargetType);

    public string CurrentEndCap => optionTargetAnnotation is ILineCapAnnotation line ? line.EndCap : Options.GetEndCap(optionTargetType);

    public Geometry CurrentStartCapIcon => LineCapIcons.Get(CurrentStartCap, true);

    public Geometry CurrentEndCapIcon => LineCapIcons.Get(CurrentEndCap, false);

    public bool IsSmoothActive => optionTargetAnnotation is FreehandAnnotation freehand ? freehand.Smooth : Options.Smooth;

    public string CurrentFontSizeText => formatPixels(optionTargetAnnotation is TextAnnotation text ? text.FontSize : Options.FontSize);

    public string CurrentBlockSizeText => formatPixels(optionTargetAnnotation is PixelateAnnotation pixelate ? pixelate.BlockSize : Options.BlockSize);

    public string CurrentBlurRadiusText => formatPixels(optionTargetAnnotation is BlurAnnotation blur ? blur.Radius : Options.BlurRadius);

    public string CurrentStampSizeText => formatPixels(optionTargetAnnotation is NumberStampAnnotation stamp ? stamp.Size : Options.StampSize);

    public string CurrentCursorKindLabel => GetCursorKindLabel(optionTargetAnnotation is CursorAnnotation cursor ? cursor.Kind : EffectiveCursorKind);

    public bool IsCursorVisible => SelectedAnnotation is CursorAnnotation cursor && cursor.Visible;

    public static string GetCursorKindLabel(string kind) {
        switch(kind) {
            case CursorKinds.Captured: return Strings.CursorKindCaptured;
            case CursorKinds.Hand: return Strings.CursorKindHand;
            case CursorKinds.IBeam: return Strings.CursorKindIBeam;
        }
        return Strings.CursorKindArrow;
    }

    public void ApplyChoice(OptionChoice choice) {
        switch(choice.Kind) {
            case OptionKind.Color: SetColor((string)choice.Value); break;
            case OptionKind.Thickness: SetThickness((double)choice.Value); break;
            case OptionKind.FontSize: SetFontSize((double)choice.Value); break;
            case OptionKind.BlockSize: SetBlockSize((int)choice.Value); break;
            case OptionKind.BlurRadius: SetBlurRadius((int)choice.Value); break;
            case OptionKind.StampSize: SetStampSize((double)choice.Value); break;
            case OptionKind.CursorKind: SetCursorKind((string)choice.Value); break;
            case OptionKind.FillColor: SetFillColor((string)choice.Value); break;
            case OptionKind.StartCap: SetStartCap((string)choice.Value); break;
            case OptionKind.EndCap: SetEndCap((string)choice.Value); break;
        }
    }

    public void SetColor(string color) {
        if(optionTargetType == AnnotationTypes.Highlight) {
            Options.HighlightColor = color;
        } else {
            Options.Color = color;
        }
        changeTarget(a => applyColor(a, color));
    }

    public void SetThickness(double thickness) {
        Options.Thickness = thickness;
        changeTarget(a => {
            switch(a) {
                case RectangleAnnotation rectangle: rectangle.Thickness = thickness; break;
                case EllipseAnnotation ellipse: ellipse.Thickness = thickness; break;
                case LineAnnotation line: line.Thickness = thickness; break;
                case ArrowAnnotation arrow: arrow.Thickness = thickness; break;
                case FreehandAnnotation freehand: freehand.Thickness = thickness; break;
            }
        });
    }

    public void SetFontSize(double fontSize) {
        Options.FontSize = fontSize;
        changeTarget(a => {
            if(a is TextAnnotation text) text.FontSize = fontSize;
        });
    }

    public void SetBlockSize(int blockSize) {
        Options.BlockSize = blockSize;
        changeTarget(a => {
            if(a is PixelateAnnotation pixelate) pixelate.BlockSize = blockSize;
        });
    }

    public void SetBlurRadius(int radius) {
        Options.BlurRadius = radius;
        changeTarget(a => {
            if(a is BlurAnnotation blur) blur.Radius = radius;
        });
    }

    public void SetStampSize(double size) {
        Options.StampSize = size;
        changeTarget(a => {
            if(a is NumberStampAnnotation stamp) stamp.Size = size;
        });
    }

    public void SetCursorKind(string kind) {
        Options.CursorKind = kind;
        changeTarget(a => {
            if(a is CursorAnnotation cursor) cursor.Kind = kind;
        });
    }

    /// <summary>Füllfarbe für Rechteck und Ellipse setzen (leer = keine Füllung).</summary>
    public void SetFillColor(string fill) {
        string value = fill ?? "";
        Options.FillColor = value;
        changeTarget(a => {
            string? stored = value.Length == 0 ? null : value;
            switch(a) {
                case RectangleAnnotation rectangle: rectangle.Fill = stored; break;
                case EllipseAnnotation ellipse: ellipse.Fill = stored; break;
            }
        });
    }

    /// <summary>Symbol am Anfang setzen (gemerkt getrennt für Linie, Pfeil und Freihand).</summary>
    public void SetStartCap(string cap) {
        Options.SetStartCap(optionTargetType, cap);
        changeTarget(a => {
            if(a is ILineCapAnnotation line) line.StartCap = cap;
        });
    }

    /// <summary>Symbol am Ende setzen (gemerkt getrennt für Linie, Pfeil und Freihand).</summary>
    public void SetEndCap(string cap) {
        Options.SetEndCap(optionTargetType, cap);
        changeTarget(a => {
            if(a is ILineCapAnnotation line) line.EndCap = cap;
        });
    }

    public void ToggleTextBackground() {
        bool value = !IsTextBackgroundActive;
        Options.TextBackground = value;
        changeTarget(a => {
            if(a is TextAnnotation text) text.Background = value ? AnnotationColors.GetTextBackground(text.Foreground) : null;
        });
    }

    public void ToggleSmooth() {
        bool value = !IsSmoothActive;
        Options.Smooth = value;
        changeTarget(a => {
            if(a is FreehandAnnotation freehand) freehand.Smooth = value;
        });
    }

    /// <summary>Ausgewählte virtuelle Maus aus- bzw. einblenden (Rückgängig-Schritt).</summary>
    public void ToggleCursorVisible() {
        if(SelectedAnnotation is not CursorAnnotation selected) return;
        bool value = !selected.Visible;
        changeTarget(a => {
            if(a is CursorAnnotation cursor) cursor.Visible = value;
        });
    }

    /// <summary>
    /// Optionen als zuletzt verwendete Werte in die Einstellungen schreiben; true, wenn sich etwas geändert hat.
    /// </summary>
    public bool StoreOptions(AppSettings settings) {
        return Options.ApplyTo(settings);
    }

    private void initializeAnnotationTools() {
        Tools.Add(new ToolItem(new ObjectSelectTool(this)));
        Tools.Add(new ToolItem(new RectangleTool(this)));
        Tools.Add(new ToolItem(new EllipseTool(this)));
        Tools.Add(new ToolItem(new LineTool(this)));
        Tools.Add(new ToolItem(new ArrowTool(this)));
        Tools.Add(new ToolItem(new FreehandTool(this)));
        Tools.Add(new ToolItem(new TextTool(this)));
        Tools.Add(new ToolItem(new HighlightTool(this)));
        Tools.Add(new ToolItem(new PixelateTool(this)));
        Tools.Add(new ToolItem(new BlurTool(this)));
        Tools.Add(new ToolItem(new NumberStampTool(this)));
        Tools.Add(new ToolItem(new CursorTool(this)));
    }

    /// <summary>Ist gerade „Auswahl“ oder „Bereich“ aktiv? (Dann hebt Esc zuerst die Objektauswahl auf.)</summary>
    public bool IsSelectingToolActive => isSelectingTool(activeTool);

    /// <summary>Darf das Werkzeug Objekte auswählen? („Auswahl“ und „Bereich“)</summary>
    private static bool isSelectingTool(IEditorTool tool) {
        return tool is ObjectSelectTool || tool is SelectionTool;
    }

    /// <summary>
    /// Doppelklick auf einen Text öffnet dessen Bearbeitung (in „Auswahl“, „Bereich“ und „Text“).
    /// </summary>
    private bool tryEditTextAt(PointerInput input) {
        if(!isSelectingTool(activeTool) && activeTool is not TextTool) return false;
        if(HitTestAnnotation(input) is not TextAnnotation text) return false;
        activeTool.CancelGesture();
        BeginTextEdit(text, false);
        return true;
    }

    /// <summary>Objekt, auf das die Optionen wirken: der bearbeitete Text oder das ausgewählte Objekt.</summary>
    private Annotation? optionTargetAnnotation => (Annotation?)editingText ?? SelectedAnnotation;

    /// <summary>Typ, dessen Optionen die Werkzeugleiste zeigt.</summary>
    private string? optionTargetType {
        get {
            Annotation? target = optionTargetAnnotation;
            if(target != null) return target is UnknownAnnotation ? null : target.Type;
            return (activeTool as IAnnotationTool)?.AnnotationType;
        }
    }

    private bool isTarget(params string[] types) {
        string? type = optionTargetType;
        return type != null && types.Contains(type);
    }

    /// <summary>
    /// Änderung auf den bearbeiteten Text (sofort, ohne Rückgängig-Schritt – der entsteht beim Übernehmen) bzw. auf das
    /// ausgewählte Objekt (ein Rückgängig-Schritt, nur wenn sich etwas ändert) anwenden.
    /// </summary>
    private void changeTarget(Action<Annotation> change) {
        if(editingText != null) {
            change(editingText);
            TextEditChanged?.Invoke(this, EventArgs.Empty);
            refreshOptions();
            return;
        }
        Annotation? selected = SelectedAnnotation;
        if(selected != null && selected is not UnknownAnnotation) {
            Annotation after = selected.Clone();
            change(after);
            if(after.ToJson() != selected.ToJson()) {
                UndoStack.Execute(new ChangeAnnotationCommand(Document, selected, after));
                return;
            }
        }
        refreshOptions();
    }

    private static void applyColor(Annotation annotation, string color) {
        switch(annotation) {
            // Die Füllung hat eine eigene Farbe und bleibt unverändert
            case RectangleAnnotation rectangle: rectangle.Stroke = color; break;
            case EllipseAnnotation ellipse: ellipse.Stroke = color; break;
            case LineAnnotation line: line.Stroke = color; break;
            case ArrowAnnotation arrow: arrow.Stroke = color; break;
            case FreehandAnnotation freehand: freehand.Stroke = color; break;
            case TextAnnotation text:
                text.Foreground = color;
                if(!string.IsNullOrEmpty(text.Background)) text.Background = AnnotationColors.GetTextBackground(color);
                break;
            case HighlightAnnotation highlight: highlight.Color = color; break;
            case NumberStampAnnotation stamp: stamp.Color = color; break;
        }
    }

    private static IReadOnlyList<OptionChoice> createFillColorChoices() {
        List<OptionChoice> choices = new List<OptionChoice> { new OptionChoice(OptionKind.FillColor, "", Strings.FillNone, LineCapIcons.GetFillBrush("")) };
        foreach(string color in AnnotationColors.Palette) choices.Add(new OptionChoice(OptionKind.FillColor, color, color, AnnotationColors.GetBrush(color)));
        foreach(string color in AnnotationColors.Palette) {
            string translucent = AnnotationColors.ToHex(AnnotationColors.WithAlpha(AnnotationColors.Parse(color), TranslucentFillAlpha));
            choices.Add(new OptionChoice(OptionKind.FillColor, translucent, Strings.Format(Strings.FillTranslucent, translucent), AnnotationColors.GetBrush(translucent)));
        }
        return choices;
    }

    private IReadOnlyList<OptionChoice> createCursorKindChoices() {
        List<OptionChoice> choices = new List<OptionChoice>();
        if(HasCapturedCursor) choices.Add(new OptionChoice(OptionKind.CursorKind, CursorKinds.Captured, Strings.CursorKindCaptured));
        choices.Add(new OptionChoice(OptionKind.CursorKind, CursorKinds.Arrow, Strings.CursorKindArrow));
        choices.Add(new OptionChoice(OptionKind.CursorKind, CursorKinds.Hand, Strings.CursorKindHand));
        choices.Add(new OptionChoice(OptionKind.CursorKind, CursorKinds.IBeam, Strings.CursorKindIBeam));
        return choices;
    }

    private void refreshOptions() {
        foreach(string name in optionProperties) OnPropertyChanged(name);
    }

    private static string formatPixels(double value) {
        return Strings.Format(Strings.OptionValuePixels, value.ToString("0.#", CultureInfo.CurrentCulture));
    }
}
