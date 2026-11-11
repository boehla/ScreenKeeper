using System.Text.Json;
using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.Tests;

/// <summary>
/// Bezeichnung einer Aufnahme: Fenster mit der größten sichtbaren Fläche im Bereich statt Vordergrundfenster.
/// </summary>
public class CaptureDescriptionTests {
    private static WindowInfo window(string title, int z, int x, int y, int width, int height, string className = "Chrome_WidgetWin_1", string process = "app") {
        return new WindowInfo { Title = title, Process = process, ClassName = className, Bounds = new PixelRect(x, y, width, height), Z = z };
    }

    private static CaptureDocument document(PixelRect? selection, params WindowInfo[] windows) {
        return new CaptureDocument {
            Foreground = new ForegroundInfo { Title = "Fokus", Process = "focus" },
            Windows = windows.ToList(),
            Selection = selection
        };
    }

    [Fact]
    public void Dominant_SingleWindowCoveringArea() {
        WindowInfo a = window("A", 0, 0, 0, 100, 100);
        Assert.Same(a, CaptureDescription.FindDominantWindow(new[] { a }, new PixelRect(10, 10, 50, 50)));
    }

    [Fact]
    public void Dominant_CountsOnlyVisibleArea() {
        // B liegt unten und deckt den Bereich ganz ab, A darüber verdeckt 60 % → A gewinnt, obwohl B größer ist
        WindowInfo a = window("A", 0, 0, 0, 60, 100);
        WindowInfo b = window("B", 1, 0, 0, 1000, 1000);
        PixelRect area = new PixelRect(0, 0, 100, 100);
        Assert.Same(a, CaptureDescription.FindDominantWindow(new[] { b, a }, area));
        // A verdeckt nur 40 % → B hat 60 % sichtbar
        WindowInfo small = window("A", 0, 0, 0, 40, 100);
        Assert.Same(b, CaptureDescription.FindDominantWindow(new[] { small, b }, area));
    }

    [Fact]
    public void Dominant_HiddenWindowDoesNotCount() {
        // C liegt ganz unter A und ist im Bereich nicht sichtbar, obwohl es den Bereich geometrisch abdeckt
        WindowInfo a = window("A", 0, 0, 0, 100, 55);
        WindowInfo c = window("C", 2, 0, 0, 100, 100);
        WindowInfo b = window("B", 1, 0, 0, 100, 100);
        PixelRect area = new PixelRect(0, 0, 100, 100);
        // B (Z 1) sieht 45 %, C nichts, A 55 %
        Assert.Same(a, CaptureDescription.FindDominantWindow(new[] { c, b, a }, area));
    }

    [Fact]
    public void Dominant_HoleInTheMiddle_ExactSubtraction() {
        // A verdeckt ein Loch mitten im Bereich (36 %), B darunter sieht einen Rahmen von 64 %
        WindowInfo a = window("A", 0, 20, 20, 60, 60);
        WindowInfo b = window("B", 1, 0, 0, 100, 100);
        Assert.Same(b, CaptureDescription.FindDominantWindow(new[] { a, b }, new PixelRect(0, 0, 100, 100)));
        // Drittes Fenster C (Z 2) rechts außen sieht nichts mehr – auch nicht durch das Loch
        WindowInfo c = window("C", 2, 50, 0, 50, 100);
        Assert.Same(b, CaptureDescription.FindDominantWindow(new[] { a, b, c }, new PixelRect(0, 0, 100, 100)));
    }

    [Fact]
    public void Dominant_ThresholdIsMoreThanHalf() {
        PixelRect area = new PixelRect(0, 0, 100, 100);
        // genau 50 % reicht nicht
        Assert.Null(CaptureDescription.FindDominantWindow(new[] { window("A", 0, 0, 0, 50, 100) }, area));
        // 51 % reicht
        WindowInfo a = window("A", 0, 0, 0, 51, 100);
        Assert.Same(a, CaptureDescription.FindDominantWindow(new[] { a }, area));
        // zwei Fenster mit je 45 %, der Rest ist leer → keins gewinnt
        WindowInfo left = window("L", 0, 0, 0, 45, 100);
        WindowInfo right = window("R", 1, 55, 0, 45, 100);
        Assert.Null(CaptureDescription.FindDominantWindow(new[] { left, right }, area));
    }

    [Fact]
    public void Dominant_AreaOverTwoWindows_LargerShareWins() {
        // Bereich überdeckt zwei nebeneinanderliegende Fenster: 70 % B, 30 % A
        WindowInfo a = window("A", 0, 0, 0, 500, 400);
        WindowInfo b = window("B", 1, 500, 0, 500, 400);
        Assert.Same(b, CaptureDescription.FindDominantWindow(new[] { a, b }, new PixelRect(470, 100, 100, 100)));
        Assert.Same(a, CaptureDescription.FindDominantWindow(new[] { a, b }, new PixelRect(430, 100, 100, 100)));
    }

    [Fact]
    public void Dominant_DesktopAndTaskbarNeverWin() {
        PixelRect area = new PixelRect(100, 100, 200, 200);
        WindowInfo small = window("Klein", 0, 100, 100, 50, 50);
        foreach(string className in CaptureDescription.ShellWindowClasses) {
            WindowInfo shell = window("Program Manager", 1, 0, 0, 1920, 1080, className, "explorer");
            Assert.Null(CaptureDescription.FindDominantWindow(new[] { small, shell }, area));
        }
        // Desktop belegt nur einen kleinen Teil, ein normales Fenster den Rest → normales Fenster
        WindowInfo big = window("Groß", 0, 100, 100, 200, 180);
        WindowInfo desktop = window("Program Manager", 1, 0, 0, 1920, 1080, "Progman", "explorer");
        Assert.Same(big, CaptureDescription.FindDominantWindow(new[] { big, desktop }, area));
    }

    [Fact]
    public void Dominant_OlderFilesWithoutClass_DesktopRecognizedByTitle() {
        WindowInfo desktop = new WindowInfo { Title = "Program Manager", Process = "explorer", Bounds = new PixelRect(0, 0, 1920, 1080), Z = 0 };
        Assert.Null(desktop.ClassName);
        Assert.True(CaptureDescription.IsShellWindow(desktop));
        Assert.Null(CaptureDescription.FindDominantWindow(new[] { desktop }, new PixelRect(10, 10, 100, 100)));
        WindowInfo other = new WindowInfo { Title = "Editor", Process = "notepad", Bounds = new PixelRect(0, 0, 500, 500), Z = 0 };
        Assert.False(CaptureDescription.IsShellWindow(other));
        desktop.Z = 1;
        Assert.Same(other, CaptureDescription.FindDominantWindow(new[] { other, desktop }, new PixelRect(10, 10, 100, 100)));
    }

    [Fact]
    public void Dominant_EmptyTitleFallsBack() {
        PixelRect area = new PixelRect(0, 0, 100, 100);
        Assert.Null(CaptureDescription.FindDominantWindow(new[] { window("", 0, 0, 0, 100, 100) }, area));
        Assert.Null(CaptureDescription.FindDominantWindow(new[] { window("   ", 0, 0, 0, 100, 100) }, area));
    }

    [Fact]
    public void Dominant_EmptyAreaOrNoWindows() {
        Assert.Null(CaptureDescription.FindDominantWindow(new[] { window("A", 0, 0, 0, 100, 100) }, new PixelRect(0, 0, 0, 10)));
        Assert.Null(CaptureDescription.FindDominantWindow(Array.Empty<WindowInfo>(), new PixelRect(0, 0, 10, 10)));
    }

    [Fact]
    public void Dominant_LargeAreasWithoutOverflow() {
        // 8K über mehrere Monitore: Flächen jenseits von int
        WindowInfo a = window("A", 0, 0, 0, 60000, 60000);
        Assert.Same(a, CaptureDescription.FindDominantWindow(new[] { a }, new PixelRect(0, 0, 50000, 50000)));
    }

    [Fact]
    public void Resolve_WithoutSelection_UsesForeground() {
        CaptureDocument doc = document(null, window("A", 0, 0, 0, 100, 100));
        ForegroundInfo? result = CaptureDescription.Resolve(doc);
        Assert.Same(doc.Foreground, result);
        Assert.Equal("focus", CaptureMetadata.FromDocument(doc).ForegroundProcess);
    }

    [Fact]
    public void Resolve_WithSelection_UsesDominantWindow() {
        CaptureDocument doc = document(new PixelRect(10, 10, 50, 50), window("Fenster Y", 0, 0, 0, 100, 100, process: "ypro"));
        CaptureMetadata metadata = CaptureMetadata.FromDocument(doc);
        Assert.Equal("ypro", metadata.ForegroundProcess);
        Assert.Equal("Fenster Y", metadata.ForegroundTitle);
        // das gespeicherte Vordergrundfenster bleibt unverändert
        Assert.Equal("focus", doc.Foreground!.Process);
    }

    [Fact]
    public void Resolve_WithSelection_FallsBackToForeground() {
        // Bereich auf dem Desktop
        CaptureDocument onDesktop = document(new PixelRect(10, 10, 50, 50), window("Program Manager", 0, 0, 0, 1920, 1080, "Progman", "explorer"));
        Assert.Equal("focus", CaptureMetadata.FromDocument(onDesktop).ForegroundProcess);
        // kein Fenster über 50 %
        CaptureDocument split = document(new PixelRect(0, 0, 100, 100), window("L", 0, 0, 0, 50, 100), window("R", 1, 50, 0, 50, 100));
        Assert.Equal("Fokus", CaptureMetadata.FromDocument(split).ForegroundTitle);
        // ohne Vordergrundfenster und ohne Gewinner: unbekannt
        split.Foreground = null;
        Assert.Null(CaptureDescription.Resolve(split));
        Assert.False(CaptureMetadata.FromDocument(split).HasForeground);
    }

    [Fact]
    public void ClassName_SerializedAndOptional() {
        CaptureDocument doc = document(null, window("A", 0, 0, 0, 10, 10, "Progman"));
        string json = CaptureDocumentSerializer.Serialize(doc);
        using(JsonDocument parsed = JsonDocument.Parse(json)) {
            Assert.Equal("Progman", parsed.RootElement.GetProperty("windows")[0].GetProperty("className").GetString());
        }
        Assert.Equal("Progman", CaptureDocumentSerializer.Deserialize(json).Windows[0].ClassName);
        // ältere Dateien ohne Klasse: lesbar, Klasse unbekannt, wird nicht geschrieben
        CaptureDocument old = CaptureDocumentSerializer.Deserialize("""{ "image": "a.png", "windows": [ { "title": "A", "process": "a", "bounds": { "x": 0, "y": 0, "width": 10, "height": 10 }, "z": 0 } ] }""");
        Assert.Null(old.Windows[0].ClassName);
        Assert.DoesNotContain("className", CaptureDocumentSerializer.Serialize(old));
    }
}
