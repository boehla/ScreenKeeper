using System.Windows.Input;
using System.Windows.Threading;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;
using ScreenKeeper.ViewModels.Tools;

namespace ScreenKeeper.Views;

/// <summary>
/// Testhilfe für automatisierte Prüfungen (<c>--open &lt;json&gt; --test-annotate</c>, nicht dokumentiert): bedient die
/// echten Werkzeuge über Maus-Sequenzen (PointerDown/Move/Up), legt je ein Objekt jedes Typs an (Rechteck gefüllt, Linie,
/// Pfeil und Freihand mit Endsymbolen), verschiebt und
/// vergrößert Objekte, macht 3× Rückgängig und 3× Wiederholen, prüft das Dokument, speichert und schließt.
/// </summary>
public partial class EditorWindow {
    private static readonly string[] expectedTypes = {
        AnnotationTypes.Rectangle, AnnotationTypes.Ellipse, AnnotationTypes.Line, AnnotationTypes.Arrow, AnnotationTypes.Freehand,
        AnnotationTypes.Text, AnnotationTypes.Highlight, AnnotationTypes.Pixelate, AnnotationTypes.Blur, AnnotationTypes.NumberStamp,
        AnnotationTypes.Cursor
    };

    /// <summary>Halbtransparentes Gelb als Füllfarbe des Test-Rechtecks.</summary>
    private const string testFillColor = "#80FFEB3B";

    /// <summary>Ist die Testhilfe fehlgeschlagen? (Exit-Code 1)</summary>
    public bool TestFailed { get; private set; }

    public void RunTestAnnotate() {
        testMode = true;
        DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (s, e) => {
            timer.Stop();
            try {
                runAnnotateScript();
            } catch(Exception ex) {
                TestFailed = true;
                Log.Error(ex, "Test: Zeichenwerkzeuge fehlgeschlagen");
                Console.Error.WriteLine(ex.Message);
            }
            if(!closed) Close();
        };
        timer.Start();
    }

    private void runAnnotateScript() {
        PixelRect image = viewModel.ImageBounds;
        int cell = Math.Max(40, Math.Min(image.Width / 6, image.Height / 4));
        PixelPoint origin(int column, int row) => new PixelPoint(image.X + cell / 4 + column * cell, image.Y + cell / 4 + row * cell);
        int size = cell / 2;
        int startCount = document.Annotations.Count;

        // Das gerade gezeichnete Objekt bleibt ausgewählt: Füllfarbe und Linienenden wirken darauf (je ein Rückgängig-Schritt)
        PixelPoint rectangle = origin(0, 0);
        testDrag(RectangleTool.ToolKey, rectangle, offset(rectangle, size, size * 2 / 3), ModifierKeys.None);
        viewModel.SetFillColor(testFillColor);
        PixelPoint ellipse = origin(1, 0);
        testDrag(EllipseTool.ToolKey, ellipse, offset(ellipse, size, size * 2 / 3), ModifierKeys.None);
        PixelPoint line = origin(2, 0);
        testDrag(LineTool.ToolKey, line, offset(line, size, size / 3), ModifierKeys.None);
        viewModel.SetStartCap(LineCaps.Dot);
        viewModel.SetEndCap(LineCaps.Square);
        PixelPoint arrow = origin(3, 0);
        testDrag(ArrowTool.ToolKey, arrow, offset(arrow, size, size / 3 + 3), ModifierKeys.Shift);
        viewModel.SetStartCap(LineCaps.Triangle);
        PixelPoint freehand = origin(4, 0);
        List<PixelPoint> path = new List<PixelPoint>();
        for(int i = 0; i <= 24; i++) path.Add(offset(freehand, i * size / 24, (int)Math.Round(Math.Sin(i / 3.0) * size / 4) + size / 4));
        testPath(FreehandTool.ToolKey, path, ModifierKeys.None);
        viewModel.SetStartCap(LineCaps.Dot);
        viewModel.SetEndCap(LineCaps.Arrow);

        PixelPoint text = origin(0, 1);
        testClick(TextTool.ToolKey, text);
        if(!viewModel.IsTextEditing) throw new InvalidOperationException("Das Text-Werkzeug hat keine Bearbeitung geöffnet.");
        viewModel.UpdateEditingText("Test 123");
        viewModel.CommitTextEdit();

        PixelPoint highlight = origin(1, 1);
        testDrag(HighlightTool.ToolKey, highlight, offset(highlight, size, size / 3), ModifierKeys.None);
        PixelPoint pixelate = origin(2, 1);
        testDrag(PixelateTool.ToolKey, pixelate, offset(pixelate, size, size / 2), ModifierKeys.None);
        PixelPoint blur = origin(3, 1);
        testDrag(BlurTool.ToolKey, blur, offset(blur, size, size / 2), ModifierKeys.None);
        PixelPoint stamp = origin(4, 1);
        testClick(NumberStampTool.ToolKey, offset(stamp, size / 4, size / 4));
        testClick(NumberStampTool.ToolKey, offset(stamp, size * 3 / 4, size / 4));
        viewModel.SetCursorKind(CursorKinds.Hand);
        testClick(CursorTool.ToolKey, origin(0, 2));

        // Auswahl: Rechteck am linken Rand greifen und verschieben, Ellipse am Anfasser rechts unten vergrößern
        PixelPoint rectangleEdge = offset(rectangle, 0, size / 3);
        testDrag(ObjectSelectTool.ToolKey, rectangleEdge, offset(rectangleEdge, 15, 10), ModifierKeys.None);
        PixelPoint ellipseEdge = offset(ellipse, size / 2, 0);
        testClick(ObjectSelectTool.ToolKey, ellipseEdge);
        if(viewModel.SelectedAnnotation is not EllipseAnnotation) throw new InvalidOperationException("Die Ellipse wurde nicht ausgewählt.");
        PixelPoint corner = offset(ellipse, size + 1, size * 2 / 3 + 1);
        testDrag(ObjectSelectTool.ToolKey, corner, offset(corner, 20, 12), ModifierKeys.None);
        viewModel.SelectAnnotation(null);

        int steps = viewModel.UndoStack.UndoCount;
        for(int i = 0; i < 3; i++) viewModel.Undo();
        for(int i = 0; i < 3; i++) viewModel.Redo();

        List<string> missing = expectedTypes.Where(t => !document.Annotations.Any(a => a.Type == t)).ToList();
        int added = document.Annotations.Count - startCount;
        Log.Info($"Test: {added} Objekte hinzugefügt, {steps} Rückgängig-Schritte, Typen: {string.Join(", ", document.Annotations.Select(a => a.Type).Distinct())}");
        if(missing.Count > 0) {
            TestFailed = true;
            string message = $"Test: Annotation-Typen fehlen: {string.Join(", ", missing)}";
            Log.Error(new InvalidOperationException(message), message);
            Console.Error.WriteLine(message);
        }
        // 12 neue Objekte (2 Stempel) + Füllfarbe + 5 Linienenden + Verschieben + Größe ändern
        if(steps < 20) {
            TestFailed = true;
            Log.Warn($"Test: nur {steps} Rückgängig-Schritte (erwartet mindestens 20)");
        }
        List<string> problems = new List<string>();
        if(!document.Annotations.OfType<RectangleAnnotation>().Any(r => r.Fill == testFillColor)) problems.Add("kein Rechteck mit Füllfarbe");
        if(!document.Annotations.OfType<LineAnnotation>().Any(l => l.StartCap == LineCaps.Dot && l.EndCap == LineCaps.Square)) problems.Add("keine Linie mit Endsymbolen");
        if(!document.Annotations.OfType<ArrowAnnotation>().Any(a => a.StartCap == LineCaps.Triangle && a.EndCap == LineCaps.Arrow)) problems.Add("kein Pfeil mit Dreieck am Anfang");
        if(!document.Annotations.OfType<FreehandAnnotation>().Any(f => f.StartCap == LineCaps.Dot && f.EndCap == LineCaps.Arrow)) problems.Add("keine Freihand-Linie mit Endsymbolen");
        if(problems.Count > 0) {
            TestFailed = true;
            string message = $"Test: {string.Join(", ", problems)}";
            Log.Error(new InvalidOperationException(message), message);
            Console.Error.WriteLine(message);
        }
        // Export über denselben Render-Pfad (darf nicht werfen)
        AppServices.Render.Render(document, original, viewModel.ImageBounds);
    }

    private void testDrag(string toolKey, PixelPoint from, PixelPoint to, ModifierKeys modifiers) {
        List<PixelPoint> path = new List<PixelPoint>();
        for(int i = 0; i <= 8; i++) path.Add(new PixelPoint(from.X + (to.X - from.X) * i / 8, from.Y + (to.Y - from.Y) * i / 8));
        testPath(toolKey, path, modifiers);
    }

    private void testPath(string toolKey, IReadOnlyList<PixelPoint> path, ModifierKeys modifiers) {
        viewModel.SelectToolByKey(toolKey);
        viewModel.PointerDown(PointerInput.AtPixel(path[0], modifiers) with { ClickCount = 1 });
        for(int i = 1; i < path.Count; i++) viewModel.PointerMove(PointerInput.AtPixel(path[i], modifiers));
        viewModel.PointerUp(PointerInput.AtPixel(path[path.Count - 1], modifiers));
    }

    private void testClick(string toolKey, PixelPoint at) {
        viewModel.SelectToolByKey(toolKey);
        viewModel.PointerDown(PointerInput.AtPixel(at, ModifierKeys.None) with { ClickCount = 1 });
        viewModel.PointerUp(PointerInput.AtPixel(at, ModifierKeys.None));
    }

    private static PixelPoint offset(PixelPoint point, int dx, int dy) {
        return new PixelPoint(point.X + dx, point.Y + dy);
    }
}
