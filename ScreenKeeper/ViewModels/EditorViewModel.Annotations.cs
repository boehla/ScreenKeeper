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

    /// <summary>Kleinste bzw. größte Nummer, die „−“/„+“ am Nummern-Stempel einstellen.</summary>
    public const int MinStampNumber = 1;

    public const int MaxStampNumber = 999;

    private static readonly string[] optionProperties = {
        nameof(ShowColorOption), nameof(ShowThicknessOption), nameof(ShowFillColorOption), nameof(ShowTextBackgroundOption),
        nameof(ShowLineCapOptions), nameof(ShowSmoothOption),
        nameof(ShowFontSizeOption), nameof(ShowBlockSizeOption), nameof(ShowBlurRadiusOption), nameof(ShowStampSizeOption),
        nameof(ShowCursorKindOption), nameof(ShowCursorVisibleOption), nameof(ShowOptions), nameof(HasSelectedAnnotation),
        nameof(CurrentColor), nameof(CurrentColorBrush), nameof(CurrentThickness), nameof(CurrentThicknessText),
        nameof(CurrentFillColor), nameof(CurrentFillBrush), nameof(IsTextBackgroundActive),
        nameof(CurrentStartCap), nameof(CurrentEndCap), nameof(CurrentStartCapIcon), nameof(CurrentEndCapIcon),
        nameof(IsSmoothActive), nameof(CurrentFontSizeText), nameof(CurrentFontSizeValueText), nameof(IsAutoFontSizeActive),
        nameof(ShowTextAlignmentOption), nameof(CurrentTextAlignment), nameof(CurrentTextAlignmentIcon),
        nameof(CurrentBlockSizeText), nameof(CurrentBlurRadiusText), nameof(CurrentStampSizeText),
        nameof(CurrentCursorKindLabel), nameof(IsCursorVisible), nameof(SelectedAnnotation),
        nameof(ShowStampNumberOption), nameof(CurrentStampNumberText), nameof(CanChangeStampNumber), nameof(StampNumberToolTip),
        nameof(CanToggleCursorVisible)
    };

    /// <summary>Deckkraft der halbtransparenten Füllfarben in der Werkzeugleiste.</summary>
    public const byte TranslucentFillAlpha = 0x80;

    private readonly ObjectManipulator selectedObject;
    private string? selectedAnnotationId;
    private TextAnnotation? editingText;
    private bool editingIsNew;
    // Größe-Ändern des bearbeiteten festen Felds: Stand beim Drücken (null = kein Vorgang), Anfasser und Startpixel
    private TextAnnotation? editingResizeBefore;
    private ObjectHandle editingResizeHandle;
    private PixelPoint editingResizeDown;
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
    private IReadOnlyList<OptionChoice>? textAlignmentChoices;
    private ICommand? toggleTextBackgroundCommand;
    private ICommand? toggleSmoothCommand;
    private ICommand? toggleCursorVisibleCommand;
    private ICommand? deleteSelectedCommand;
    private ICommand? increaseStampNumberCommand;
    private ICommand? decreaseStampNumberCommand;

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
        // bearbeitetes festes Feld: Rahmen und Anfasser der Arbeitskopie (Größe ändern während der Eingabe)
        if(editingText != null && !TextLayout.IsField(editingText)) return null;
        Annotation? selected = (Annotation?)editingText ?? SelectedAnnotation;
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
        if(editingText == null) return;
        editingText.Text = text;
        // automatische Schriftgröße: die Anzeige in der Werkzeugleiste folgt dem Text
        if(editingText.AutoFontSize && TextLayout.IsField(editingText)) OnPropertyChanged(nameof(CurrentFontSizeText));
    }

    /// <summary>Wird gerade die Größe des bearbeiteten Textfelds gezogen?</summary>
    public bool IsEditingResizeActive => editingResizeBefore != null;

    /// <summary>
    /// Anfasser des gerade bearbeiteten festen Textfelds unter der Maus bzw. der gerade gezogene
    /// (<see cref="ObjectHandle.None"/>: keiner, keine Bearbeitung oder freier Text – der wächst mit dem Inhalt).
    /// </summary>
    public ObjectHandle HitEditingTextHandle(PointerInput input) {
        if(editingResizeBefore != null) return editingResizeHandle;
        if(editingText == null || !TextLayout.IsField(editingText)) return ObjectHandle.None;
        return AnnotationEditing.HitHandle(editingText, input.X, input.Y, HandleRadius, Measure);
    }

    /// <summary>
    /// Größe-Ändern des bearbeiteten festen Felds beginnen (die Bearbeitung läuft weiter); false, wenn kein Anfasser
    /// getroffen wurde.
    /// </summary>
    private bool tryBeginEditingResize(PointerInput input) {
        ObjectHandle hit = HitEditingTextHandle(input);
        if(hit == ObjectHandle.None || editingText == null) return false;
        editingResizeBefore = (TextAnnotation)editingText.Clone();
        editingResizeHandle = hit;
        editingResizeDown = input.Pixel;
        return true;
    }

    private void moveEditingResize(PointerInput input) {
        if(editingResizeBefore == null || editingText == null) return;
        if(AnnotationEditing.Resized(editingResizeBefore, editingResizeHandle, editingResizeDown, input.Pixel, input.Shift, Measure) is not TextAnnotation resized) return;
        if(resized.X == editingText.X && resized.Y == editingText.Y && resized.Width == editingText.Width && resized.Height == editingText.Height) return;
        // nur die Geometrie übernehmen; der Text gehört dem Eingabefeld
        editingText.X = resized.X;
        editingText.Y = resized.Y;
        editingText.Width = resized.Width;
        editingText.Height = resized.Height;
        TextEditChanged?.Invoke(this, EventArgs.Empty);
        if(editingText.AutoFontSize) OnPropertyChanged(nameof(CurrentFontSizeText));
        NotifyOverlayChanged();
    }

    private void endEditingResize() {
        editingResizeBefore = null;
    }

    /// <summary>
    /// Text übernehmen (Strg+Enter, Klick daneben): neu → einfügen; geändert → ein Rückgängig-Schritt; leer → Objekt löschen.
    /// </summary>
    public void CommitTextEdit() {
        TextAnnotation? text = editingText;
        if(text == null) return;
        bool isNew = editingIsNew;
        editingResizeBefore = null;
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
        editingResizeBefore = null;
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

    /// <summary>„Auto“ und die festen Schriftgrößen.</summary>
    public IReadOnlyList<OptionChoice> FontSizeChoices => fontSizeChoices ??= new[] { new OptionChoice(OptionKind.AutoFontSize, true, Strings.FontSizeAuto) }
        .Concat(ToolOptions.FontSizes.Select(s => new OptionChoice(OptionKind.FontSize, s, formatPixels(s)))).ToList();

    /// <summary>Die 9 Ausrichtungen zeilenweise (für das 3×3-Raster).</summary>
    public IReadOnlyList<OptionChoice> TextAlignmentChoices => textAlignmentChoices ??= TextAlignments.All
        .Select(a => new OptionChoice(OptionKind.TextAlignment, a, TextAlignmentIcons.GetLabel(a), null, TextAlignmentIcons.Get(a))).ToList();

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

    public ICommand ToggleCursorVisibleCommand => toggleCursorVisibleCommand ??= new RelayCommand(ToggleCursorVisible, () => CanToggleCursorVisible);

    /// <summary>„Objekt löschen“: ohne ausgewähltes Objekt deaktiviert (der Knopf bleibt sichtbar und ausgegraut).</summary>
    public ICommand DeleteSelectedCommand => deleteSelectedCommand ??= new RelayCommand(DeleteSelectedAnnotation, () => HasSelectedAnnotation);

    /// <summary>„+“ am Nummern-Stempel; ohne ausgewählten Stempel deaktiviert (ausgegraut).</summary>
    public ICommand IncreaseStampNumberCommand => increaseStampNumberCommand ??= new RelayCommand(() => ChangeSelectedStampNumber(1), () => CanChangeStampNumber);

    public ICommand DecreaseStampNumberCommand => decreaseStampNumberCommand ??= new RelayCommand(() => ChangeSelectedStampNumber(-1), () => CanChangeStampNumber);

    public bool ShowColorOption => isTarget(AnnotationTypes.Rectangle, AnnotationTypes.Ellipse, AnnotationTypes.Line, AnnotationTypes.Arrow,
        AnnotationTypes.Freehand, AnnotationTypes.Text, AnnotationTypes.Highlight, AnnotationTypes.NumberStamp);

    public bool ShowThicknessOption => isTarget(AnnotationTypes.Rectangle, AnnotationTypes.Ellipse, AnnotationTypes.Line, AnnotationTypes.Arrow, AnnotationTypes.Freehand);

    public bool ShowFillColorOption => isTarget(AnnotationTypes.Rectangle, AnnotationTypes.Ellipse);

    public bool ShowTextBackgroundOption => isTarget(AnnotationTypes.Text);

    /// <summary>„Anfang“ und „Ende“ (Linienenden) für Linie, Pfeil und Freihand.</summary>
    public bool ShowLineCapOptions => isTarget(AnnotationTypes.Line, AnnotationTypes.Arrow, AnnotationTypes.Freehand);

    public bool ShowSmoothOption => isTarget(AnnotationTypes.Freehand);

    public bool ShowFontSizeOption => isTarget(AnnotationTypes.Text);

    public bool ShowTextAlignmentOption => isTarget(AnnotationTypes.Text);

    public bool ShowBlockSizeOption => isTarget(AnnotationTypes.Pixelate);

    public bool ShowBlurRadiusOption => isTarget(AnnotationTypes.Blur);

    public bool ShowStampSizeOption => isTarget(AnnotationTypes.NumberStamp);

    /// <summary>
    /// „−“ Nummer „+“ stehen immer zusammen mit den Stempel-Optionen (Werkzeug oder ausgewählter Stempel), damit sich die
    /// Werkzeugleiste beim Stempeln nicht ändert; wirksam sind sie nur bei einem ausgewählten Stempel
    /// (<see cref="CanChangeStampNumber"/>).
    /// </summary>
    public bool ShowStampNumberOption => isTarget(AnnotationTypes.NumberStamp);

    /// <summary>Ist ein Nummern-Stempel ausgewählt, dessen Nummer „−“/„+“ (Knöpfe bzw. Tasten) ändern?</summary>
    public bool CanChangeStampNumber => editingText == null && SelectedAnnotation is NumberStampAnnotation;

    public bool ShowCursorKindOption => isTarget(AnnotationTypes.Cursor);

    /// <summary>
    /// Der Augen-Knopf steht immer bei den Optionen der virtuellen Maus; wirksam ist er nur bei einer ausgewählten Maus
    /// (<see cref="CanToggleCursorVisible"/>), sonst ausgegraut.
    /// </summary>
    public bool ShowCursorVisibleOption => isTarget(AnnotationTypes.Cursor);

    public bool CanToggleCursorVisible => editingText == null && SelectedAnnotation is CursorAnnotation;

    /// <summary>Gibt es überhaupt Optionen für das Werkzeug bzw. ausgewählte Objekt?</summary>
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

    /// <summary>
    /// Automatische Schriftgröße aktiv? Am Text nur bei einem festen Feld (freier Text hat immer eine feste Größe),
    /// sonst die Einstellung für neue Textfelder.
    /// </summary>
    public bool IsAutoFontSizeActive => optionTargetAnnotation is TextAnnotation text ? text.AutoFontSize && TextLayout.IsField(text) : Options.TextAutoFontSize;

    /// <summary>Anzeige im Knopf: feste Größe, „Auto“ bzw. am Textfeld „Auto (wirksame Größe)“.</summary>
    public string CurrentFontSizeText {
        get {
            if(optionTargetAnnotation is TextAnnotation text) {
                if(!IsAutoFontSizeActive) return formatPixels(text.FontSize);
                return Strings.Format(Strings.FontSizeAutoValue, formatPixels(TextLayout.GetEffectiveFontSize(text)));
            }
            return Options.TextAutoFontSize ? Strings.FontSizeAuto : formatPixels(Options.FontSize);
        }
    }

    /// <summary>Feste Schriftgröße als Zahl (Vorbelegung der eigenen Eingabe).</summary>
    public string CurrentFontSizeValueText => formatNumber(optionTargetAnnotation is TextAnnotation text ? text.FontSize : Options.FontSize);

    public string CurrentTextAlignment => TextAlignments.Normalize(optionTargetAnnotation is TextAnnotation text ? text.Alignment : Options.TextAlignment, TextAlignments.TopLeft);

    public Geometry CurrentTextAlignmentIcon => TextAlignmentIcons.Get(CurrentTextAlignment);

    public string CurrentBlockSizeText => formatPixels(optionTargetAnnotation is PixelateAnnotation pixelate ? pixelate.BlockSize : Options.BlockSize);

    public string CurrentBlurRadiusText => formatPixels(optionTargetAnnotation is BlurAnnotation blur ? blur.Radius : Options.BlurRadius);

    public string CurrentStampSizeText => formatPixels(optionTargetAnnotation is NumberStampAnnotation stamp ? stamp.Size : Options.StampSize);

    /// <summary>
    /// Nummer des ausgewählten Stempels; beim Stempel-Werkzeug ohne Auswahl die Nummer des nächsten Stempels (gedimmt
    /// angezeigt), sonst leer.
    /// </summary>
    public string CurrentStampNumberText {
        get {
            if(CanChangeStampNumber && SelectedAnnotation is NumberStampAnnotation stamp) return stamp.Number.ToString(CultureInfo.CurrentCulture);
            if(ShowStampNumberOption) return NumberStampTool.GetNextNumber(Document.Annotations).ToString(CultureInfo.CurrentCulture);
            return "";
        }
    }

    public string StampNumberToolTip => CanChangeStampNumber ? Strings.OptionStampNumber : Strings.OptionStampNumberNext;

    // Unsichtbare Platzhalter in der Werkzeugleiste: legen die Breite der Werte-Anzeigen auf den breitesten möglichen
    // Wert fest, damit nachfolgende Knöpfe beim Ändern eines Werts nicht wandern (Ziffern sind gleich breit)

    /// <summary>Platzhalter für Pixelwerte (Strichstärke, Blockgröße, Radius, Stempelgröße – alle zweistellig).</summary>
    public string PixelValueSample => formatPixels(88);

    /// <summary>Platzhalter für die Schriftgröße („Auto (888,8 px)“).</summary>
    public string FontSizeTextSample => Strings.Format(Strings.FontSizeAutoValue, formatPixels(888.8));

    /// <summary>Platzhalter für die Stempelnummer (drei Ziffern, siehe <see cref="MaxStampNumber"/>).</summary>
    public string StampNumberTextSample => MaxStampNumber.ToString(CultureInfo.CurrentCulture);

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
            case OptionKind.AutoFontSize: SetAutoFontSize(); break;
            case OptionKind.TextAlignment: SetTextAlignment((string)choice.Value); break;
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

    /// <summary>Feste Schriftgröße setzen (begrenzt auf 6–400 px); schaltet die automatische Größe aus.</summary>
    public void SetFontSize(double fontSize) {
        double value = Math.Clamp(fontSize, TextLayout.MinFontSize, TextLayout.MaxFontSize);
        Options.FontSize = value;
        Options.TextAutoFontSize = false;
        changeTarget(a => {
            if(a is TextAnnotation text) {
                text.FontSize = value;
                text.AutoFontSize = false;
            }
        });
    }

    /// <summary>
    /// Automatische Schriftgröße einschalten: für neue Textfelder und am festen Feld (freier Text behält seine feste Größe).
    /// </summary>
    public void SetAutoFontSize() {
        Options.TextAutoFontSize = true;
        changeTarget(a => {
            if(a is TextAnnotation text && TextLayout.IsField(text)) text.AutoFontSize = true;
        });
    }

    /// <summary>
    /// Eigene Eingabe der Schriftgröße übernehmen (siehe <see cref="TryParseFontSize"/>); false bei ungültiger Eingabe.
    /// </summary>
    public bool ApplyFontSizeInput(string? input) {
        if(!TryParseFontSize(input, out double fontSize)) return false;
        SetFontSize(fontSize);
        return true;
    }

    /// <summary>
    /// Eingabe wie „18“, „18,5“, „18.5“ oder „18 px“ lesen; auf 0,5 gerundet und auf 6–400 begrenzt. False bei leerer,
    /// nicht lesbarer oder nicht positiver Eingabe.
    /// </summary>
    public static bool TryParseFontSize(string? input, out double fontSize) {
        fontSize = 0;
        string text = (input ?? "").Trim();
        if(text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text.Substring(0, text.Length - 2).Trim();
        text = text.Replace(',', '.');
        if(!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return false;
        if(double.IsNaN(value) || double.IsInfinity(value) || value <= 0) return false;
        fontSize = Math.Clamp(Math.Round(value * 2) / 2, TextLayout.MinFontSize, TextLayout.MaxFontSize);
        return true;
    }

    /// <summary>Ausrichtung setzen (gemerkt für neue Texte).</summary>
    public void SetTextAlignment(string alignment) {
        string value = TextAlignments.Normalize(alignment, TextAlignments.TopLeft);
        Options.TextAlignment = value;
        changeTarget(a => {
            if(a is TextAnnotation text) text.Alignment = value;
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

    /// <summary>
    /// Nummer des ausgewählten Stempels um <paramref name="delta"/> ändern (begrenzt auf 1–999, ein Rückgängig-Schritt;
    /// am Rand passiert nichts). False, wenn kein Stempel ausgewählt ist.
    /// </summary>
    public bool ChangeSelectedStampNumber(int delta) {
        if(!CanChangeStampNumber || SelectedAnnotation is not NumberStampAnnotation selected) return false;
        int value = Math.Clamp(selected.Number + delta, MinStampNumber, MaxStampNumber);
        changeTarget(a => {
            if(a is NumberStampAnnotation stamp) stamp.Number = value;
        });
        return true;
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
    /// Fängt das ausgewählte Objekt Klicks und Mauszeiger ab? Nur bei Zeichenwerkzeugen (Rechteck … Nummern-Stempel,
    /// virtuelle Maus, Text) – „Auswahl“ und „Bereich“ erledigen das selbst –, nicht während der Textbearbeitung und nicht,
    /// solange das Werkzeug selbst einen Vorgang hat. So kann man das gerade gezeichnete Objekt verschieben bzw. an den
    /// Anfassern ändern, ohne das Werkzeug zu wechseln; Klicks auf andere Objekte oder Leerraum zeichnen weiter neu.
    /// </summary>
    private bool canGrabSelectedObject => activeTool is IAnnotationTool && editingText == null && !activeTool.IsGestureActive;

    /// <summary>
    /// Anfasser bzw. Körper des ausgewählten Objekts getroffen: Größe ändern bzw. Verschieben beginnen (gleicher Ablauf und
    /// Rückgängig-Schritt wie bei „Auswahl“). False: das Werkzeug bekommt den Klick.
    /// </summary>
    private bool tryBeginOnSelectedObject(PointerInput input) {
        return canGrabSelectedObject && selectedObject.TryBeginOnSelected(input);
    }

    private void endOnSelectedObject(PointerInput input) {
        bool clicked = selectedObject.Up(input);
        // Text-Werkzeug: ein Klick (ohne Ziehen) auf den ausgewählten Text öffnet ihn wie jeden anderen Text zum Bearbeiten
        if(clicked && activeTool is TextTool && SelectedAnnotation is TextAnnotation text) BeginTextEdit(text, false);
    }

    /// <summary>
    /// Mauszeiger über dem ausgewählten Objekt bzw. während es gezogen wird – mit derselben Trefferprüfung wie
    /// <see cref="tryBeginOnSelectedObject"/>; null: der Zeiger des Werkzeugs.
    /// </summary>
    private EditorCursor? getSelectedObjectCursor(PointerInput input) {
        if(!selectedObject.IsActive && !canGrabSelectedObject) return null;
        return selectedObject.GetCursor(input);
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
        // Auswahl kann sich geändert haben: „Objekt löschen“ (CanExecute) neu prüfen lassen
        CommandManager.InvalidateRequerySuggested();
    }

    private static string formatPixels(double value) {
        return Strings.Format(Strings.OptionValuePixels, formatNumber(value));
    }

    private static string formatNumber(double value) {
        return value.ToString("0.#", CultureInfo.CurrentCulture);
    }
}
