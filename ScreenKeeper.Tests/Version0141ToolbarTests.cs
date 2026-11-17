using System.Windows;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Tests;

/// <summary>
/// 0.14.1: Beim Werkzeug „Bereich“ ohne Bereich weicht die Werkzeugleiste (oben mittig) dem Zeiger an die untere Kante
/// desselben Monitors aus, damit man auch unter ihr einen Bereich beginnen kann – und von dort wieder nach oben. Mit
/// gedrückter Strg-Taste bleibt sie stehen (erreichbar). Mit einem Bereich gilt die normale Platzierung unter/über dem Bereich.
/// </summary>
public class ToolbarEvadeTests {
    private const double gap = 10;
    private const double margin = 12;
    private static readonly Rect monitor = new Rect(1920, 0, 1920, 1080);
    private static readonly Size size = new Size(400, 44);
    // Leiste oben mittig: 2680..3080 × 10..54, ausgewichen unten mittig: 2680..3080 × 1026..1070
    private static readonly Point overTop = new Point(2880, 30);
    private static readonly Point overBottom = new Point(2880, 1040);
    private static readonly Point middle = new Point(2880, 540);

    private static bool update(bool flipped, Point pointer, bool canEvade = true, Rect? selection = null, Rect? bounds = null) {
        return ToolbarPlacement.UpdateFlipped(flipped, canEvade, selection, pointer, size, bounds ?? monitor, gap, margin);
    }

    [Fact]
    public void Flipped_WithoutSelection_BottomCenterOfSameMonitor() {
        Assert.Equal(new Point(2680, 10), ToolbarPlacement.Place(null, size, monitor, gap, false));
        Assert.Equal(new Point(2680, 1080 - 10 - 44), ToolbarPlacement.Place(null, size, monitor, gap, true));
    }

    [Fact]
    public void Flipped_WithSelection_IsIgnored() {
        Rect selection = new Rect(2400, 300, 200, 100);
        Assert.Equal(ToolbarPlacement.Place(selection, size, monitor, gap), ToolbarPlacement.Place(selection, size, monitor, gap, true));
    }

    [Fact]
    public void Evades_WhenPointerOverOrNearToolbar() {
        Assert.True(update(false, overTop));
        // knapp neben bzw. unter der Leiste (innerhalb des Rands)
        Assert.True(update(false, new Point(2680 - 10, 30)));
        Assert.True(update(false, new Point(2880, 54 + 11)));
        // außerhalb des Rands bzw. weit weg
        Assert.False(update(false, new Point(2880, 54 + 14)));
        Assert.False(update(false, new Point(2680 - 14, 30)));
        Assert.False(update(false, middle));
        // anderer Monitor
        Assert.False(update(false, new Point(960, 30)));
    }

    [Fact]
    public void OnlyWithRegionToolAndWithoutSelection() {
        Assert.False(update(false, overTop, canEvade: false));
        Assert.False(update(false, overTop, selection: new Rect(2400, 300, 200, 100)));
    }

    [Fact]
    public void EvadesBackUp_WhenPointerComesToTheBottom() {
        Assert.False(update(true, overBottom));
        Assert.False(update(true, new Point(2880, 1026 - 11)));
        // woanders bleibt sie unten – auch dort, wo sie oben stand
        Assert.True(update(true, overTop));
        Assert.True(update(true, middle));
        Assert.True(update(true, new Point(2880, 1026 - 14)));
    }

    [Fact]
    public void StaysWhereItIs_WithOtherToolOrCtrl() {
        Assert.True(update(true, overBottom, canEvade: false));
        Assert.True(update(true, middle, canEvade: false));
        Assert.False(update(false, overTop, canEvade: false));
    }

    [Fact]
    public void BackToNormal_OnceThereIsASelection() {
        Rect selection = new Rect(2400, 300, 200, 100);
        Assert.False(update(true, middle, selection: selection));
        Assert.False(update(true, overBottom, selection: selection));
    }

    [Fact]
    public void StaysWhenPointerWouldBeNearBothPositions() {
        // sehr niedriger Monitor: unten wäre die Leiste ebenso nah am Zeiger → bleibt oben
        Rect low = new Rect(0, 0, 800, 100);
        Assert.False(update(false, new Point(400, 50), bounds: low));
    }

    [Fact]
    public void EvadedPosition_NeverNearThePointerThatTriggeredIt() {
        Rect top = new Rect(ToolbarPlacement.Place(null, size, monitor, gap), size);
        top.Inflate(margin, margin);
        for(double x = top.Left; x <= top.Right; x += 7) {
            for(double y = top.Top; y <= top.Bottom; y += 3) {
                Point pointer = new Point(x, y);
                Assert.True(update(false, pointer), $"weicht bei {x},{y} nicht aus");
                Rect placed = new Rect(ToolbarPlacement.Place(null, size, monitor, gap, true), size);
                placed.Inflate(margin, margin);
                Assert.False(placed.Contains(pointer), $"ausgewichene Leiste liegt bei {x},{y} am Zeiger");
                Assert.True(monitor.Contains(new Rect(ToolbarPlacement.Place(null, size, monitor, gap, true), size)));
            }
        }
    }
}
