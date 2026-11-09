using System.Windows;
using ScreenKeeper.Models;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Tests;

public class PlacementTests {
    private static readonly Rect monitor = new Rect(0, 0, 1920, 1080);
    private static readonly Size lens = new Size(170, 190);

    [Fact]
    public void Magnifier_DefaultsToBottomRightOfCursor() {
        Point position = MagnifierPlacement.Place(new Point(500, 400), lens, monitor, 20);
        Assert.Equal(new Point(520, 420), position);
    }

    [Fact]
    public void Magnifier_SwitchesCornerWhenNoRoom() {
        Point right = MagnifierPlacement.Place(new Point(1850, 400), lens, monitor, 20);
        Assert.Equal(new Point(1850 - 20 - 170, 420), right);
        Point bottom = MagnifierPlacement.Place(new Point(500, 1000), lens, monitor, 20);
        Assert.Equal(new Point(520, 1000 - 20 - 190), bottom);
        Point corner = MagnifierPlacement.Place(new Point(1900, 1070), lens, monitor, 20);
        Assert.Equal(new Point(1900 - 20 - 170, 1070 - 20 - 190), corner);
    }

    [Fact]
    public void Magnifier_StaysInsideMonitorAndAvoidsCursor() {
        // zweiter Monitor rechts, Cursor-Positionen über den ganzen Monitor
        Rect second = new Rect(1920, 0, 1280, 1024);
        for(double x = second.Left; x < second.Right; x += 37) {
            for(double y = second.Top; y < second.Bottom; y += 41) {
                Point position = MagnifierPlacement.Place(new Point(x, y), lens, second, 20);
                Rect placed = new Rect(position, lens);
                Assert.True(second.Contains(placed), $"Lupe {placed} verlässt den Monitor bei Cursor {x},{y}");
                Assert.False(placed.Contains(new Point(x, y)), $"Lupe verdeckt den Cursor {x},{y}");
            }
        }
    }

    [Fact]
    public void Magnifier_LargerThanMonitor_IsClampedToTopLeft() {
        Point position = MagnifierPlacement.Place(new Point(50, 50), new Size(300, 300), new Rect(0, 0, 200, 200), 20);
        Assert.Equal(new Point(0, 0), position);
    }

    [Fact]
    public void Magnifier_CellCountIsOdd() {
        Assert.Equal(21, MagnifierPlacement.GetCellCount(160, 8));
        Assert.Equal(41, MagnifierPlacement.GetCellCount(160, 4));
        Assert.Equal(11, MagnifierPlacement.GetCellCount(160, 16));
        Assert.Equal(25, MagnifierPlacement.GetCellCount(200, 8));
        Assert.Equal(3, MagnifierPlacement.GetCellCount(5, 16));
    }

    [Fact]
    public void Magnifier_FormatsColorAndInfo() {
        Assert.Equal("#FF0080", MagnifierPlacement.FormatColor(255, 0, 128));
        Assert.Equal("#0A0B0C", MagnifierPlacement.FormatColor(10, 11, 12));
        Assert.Equal("12, 34 · 640 × 480 · #FFFFFF", MagnifierPlacement.FormatInfo(new PixelPoint(12, 34), new PixelRect(0, 0, 640, 480), "#FFFFFF"));
        Assert.Equal("-5, 7 · #000000", MagnifierPlacement.FormatInfo(new PixelPoint(-5, 7), null, "#000000"));
    }

    [Fact]
    public void Toolbar_WithoutSelection_TopCenterOfPrimary() {
        Rect primary = new Rect(1920, 0, 1920, 1080);
        Point position = ToolbarPlacement.Place(null, new Size(400, 44), primary, 10);
        Assert.Equal(new Point(1920 + 760, 10), position);
    }

    [Fact]
    public void Toolbar_BelowAboveOrInside() {
        Size size = new Size(400, 44);
        Point below = ToolbarPlacement.Place(new Rect(500, 300, 200, 100), size, monitor, 10);
        Assert.Equal(new Point(400, 410), below);
        Point above = ToolbarPlacement.Place(new Rect(500, 1000, 200, 60), size, monitor, 10);
        Assert.Equal(new Point(400, 1000 - 10 - 44), above);
        Point inside = ToolbarPlacement.Place(new Rect(0, 20, 1920, 1040), size, monitor, 10);
        Assert.Equal(new Point(760, 1060 - 10 - 44), inside);
        // am linken Rand: in den Monitor geschoben
        Point clamped = ToolbarPlacement.Place(new Rect(0, 100, 20, 20), size, monitor, 10);
        Assert.Equal(0, clamped.X);
    }

    [Fact]
    public void ViewTransform_FloorWithTolerance() {
        // Vollbild bei 125 %: Zoom = 1 / 1,25; Gerätepixel d liegt bei DIP d / 1,25 → Bildpixel d
        ViewTransform transform = new ViewTransform { Zoom = 1 / 1.25 };
        for(int device = 0; device < 4000; device++) {
            Point dip = new Point(device / 1.25, device / 1.25);
            Assert.Equal(new PixelPoint(device, device), transform.ToPixel(dip));
        }
        ViewTransform zoomed = new ViewTransform { Zoom = 8, PanX = 100, PanY = 50 };
        Assert.Equal(new PixelPoint(0, 0), zoomed.ToPixel(new Point(107.9, 57.9)));
        Assert.Equal(new PixelPoint(1, -1), zoomed.ToPixel(new Point(108, 49)));
        Assert.Equal(new Rect(180, 90, 80, 160), zoomed.ToView(new PixelRect(10, 5, 10, 20)));
    }

    [Fact]
    public void ViewTransform_ZoomAtKeepsPointFixed() {
        ViewTransform transform = new ViewTransform { Zoom = 0.5, PanX = 30, PanY = 40 };
        Point mouse = new Point(400, 300);
        Point before = transform.ToImage(mouse);
        transform.ZoomAt(mouse, 2);
        Point after = transform.ToImage(mouse);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
        Assert.Equal(0.5, ViewTransform.FitZoom(4000, 2000, 2000, 1200, 0.1, 1));
        Assert.Equal(1, ViewTransform.FitZoom(400, 200, 2000, 1200, 0.1, 1));
    }
}
