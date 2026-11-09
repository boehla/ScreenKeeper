using ScreenKeeper.Models;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Tests;

public class SelectionGeometryTests {
    private static readonly PixelRect image = new PixelRect(0, 0, 1000, 800);

    [Fact]
    public void FromPoints_IsNormalizedAndInclusive() {
        Assert.Equal(new PixelRect(10, 20, 31, 41), SelectionGeometry.FromPoints(new PixelPoint(10, 20), new PixelPoint(40, 60)));
        Assert.Equal(new PixelRect(10, 20, 31, 41), SelectionGeometry.FromPoints(new PixelPoint(40, 60), new PixelPoint(10, 20)));
        Assert.Equal(new PixelRect(10, 20, 31, 41), SelectionGeometry.FromPoints(new PixelPoint(40, 20), new PixelPoint(10, 60)));
        // ein einzelner Pixel = 1 × 1
        Assert.Equal(new PixelRect(5, 5, 1, 1), SelectionGeometry.FromPoints(new PixelPoint(5, 5), new PixelPoint(5, 5)));
    }

    [Fact]
    public void Clamp_KeepsInsideImage() {
        Assert.Equal(new PixelPoint(0, 799), SelectionGeometry.ClampPoint(new PixelPoint(-5, 900), image));
        Assert.Equal(new PixelPoint(999, 0), SelectionGeometry.ClampPoint(new PixelPoint(1000, -1), image));
        Assert.Equal(new PixelRect(990, 0, 10, 10), SelectionGeometry.ClampRect(new PixelRect(990, -10, 50, 20), image));
        Assert.Null(SelectionGeometry.ClampRect(new PixelRect(2000, 0, 10, 10), image));
    }

    [Fact]
    public void MoveWithin_KeepsSizeAndStaysInImage() {
        PixelRect rect = new PixelRect(100, 100, 200, 100);
        Assert.Equal(new PixelRect(150, 90, 200, 100), SelectionGeometry.MoveWithin(rect, 50, -10, image));
        Assert.Equal(new PixelRect(0, 0, 200, 100), SelectionGeometry.MoveWithin(rect, -500, -500, image));
        Assert.Equal(new PixelRect(800, 700, 200, 100), SelectionGeometry.MoveWithin(rect, 5000, 5000, image));
    }

    [Fact]
    public void MoveCursor_StepsOneOrTenPixels() {
        PixelPoint start = new PixelPoint(100, 100);
        Assert.Equal(new PixelPoint(101, 100), SelectionGeometry.MoveCursor(start, 1, 0, false, image));
        Assert.Equal(new PixelPoint(99, 100), SelectionGeometry.MoveCursor(start, -1, 0, false, image));
        Assert.Equal(new PixelPoint(100, 110), SelectionGeometry.MoveCursor(start, 0, 1, true, image));
        Assert.Equal(new PixelPoint(100, 90), SelectionGeometry.MoveCursor(start, 0, -1, true, image));
        // am Rand bleibt der Cursor im Bild
        Assert.Equal(new PixelPoint(0, 0), SelectionGeometry.MoveCursor(new PixelPoint(3, 0), -1, -1, true, image));
        Assert.Equal(new PixelPoint(999, 799), SelectionGeometry.MoveCursor(new PixelPoint(999, 799), 1, 1, false, image));
        // Cursor außerhalb des Bildes: Schritt ab dem nächsten Bildpixel
        Assert.Equal(new PixelPoint(1, 0), SelectionGeometry.MoveCursor(new PixelPoint(-30, -50), 1, 0, false, image));
    }

    [Fact]
    public void HitTest_FindsHandlesEdgesInsideAndNone() {
        PixelRect rect = new PixelRect(100, 100, 200, 100);
        double radius = 5;
        double edge = 3;
        Assert.Equal(SelectionHandle.TopLeft, SelectionGeometry.HitTest(rect, 101, 99, radius, edge));
        Assert.Equal(SelectionHandle.TopRight, SelectionGeometry.HitTest(rect, 300, 100, radius, edge));
        Assert.Equal(SelectionHandle.BottomRight, SelectionGeometry.HitTest(rect, 303, 203, radius, edge));
        Assert.Equal(SelectionHandle.BottomLeft, SelectionGeometry.HitTest(rect, 98, 200, radius, edge));
        Assert.Equal(SelectionHandle.Top, SelectionGeometry.HitTest(rect, 200, 100, radius, edge));
        Assert.Equal(SelectionHandle.Right, SelectionGeometry.HitTest(rect, 300, 150, radius, edge));
        Assert.Equal(SelectionHandle.Bottom, SelectionGeometry.HitTest(rect, 200, 200, radius, edge));
        Assert.Equal(SelectionHandle.Left, SelectionGeometry.HitTest(rect, 100, 150, radius, edge));
        // Kantenlinie außerhalb der Anfasser
        Assert.Equal(SelectionHandle.Left, SelectionGeometry.HitTest(rect, 101.5, 120, radius, edge));
        Assert.Equal(SelectionHandle.Bottom, SelectionGeometry.HitTest(rect, 140, 198, radius, edge));
        Assert.Equal(SelectionHandle.Move, SelectionGeometry.HitTest(rect, 150, 150, radius, edge));
        Assert.Equal(SelectionHandle.None, SelectionGeometry.HitTest(rect, 50, 50, radius, edge));
        Assert.Equal(SelectionHandle.None, SelectionGeometry.HitTest(rect, 200, 250, radius, edge));
    }

    [Fact]
    public void HandlePoints_LieOnEdges() {
        PixelRect rect = new PixelRect(10, 20, 100, 50);
        Assert.Equal(8, SelectionGeometry.Handles.Count);
        Assert.Equal((10.0, 20.0), SelectionGeometry.GetHandlePoint(rect, SelectionHandle.TopLeft));
        Assert.Equal((110.0, 70.0), SelectionGeometry.GetHandlePoint(rect, SelectionHandle.BottomRight));
        Assert.Equal((60.0, 20.0), SelectionGeometry.GetHandlePoint(rect, SelectionHandle.Top));
        Assert.Equal((110.0, 45.0), SelectionGeometry.GetHandlePoint(rect, SelectionHandle.Right));
        Assert.Equal(EditorCursor.SizeNWSE, SelectionGeometry.GetCursor(SelectionHandle.TopLeft));
        Assert.Equal(EditorCursor.SizeNESW, SelectionGeometry.GetCursor(SelectionHandle.BottomLeft));
        Assert.Equal(EditorCursor.SizeWE, SelectionGeometry.GetCursor(SelectionHandle.Right));
        Assert.Equal(EditorCursor.SizeNS, SelectionGeometry.GetCursor(SelectionHandle.Top));
        Assert.Equal(EditorCursor.SizeAll, SelectionGeometry.GetCursor(SelectionHandle.Move));
        Assert.Equal(EditorCursor.Cross, SelectionGeometry.GetCursor(SelectionHandle.None));
    }

    [Fact]
    public void HandleDrag_RightEdge_MovesByExactPixels() {
        PixelRect rect = new PixelRect(100, 100, 200, 100);
        // Greifen 2 Pixel neben der Kante: der Abstand bleibt erhalten
        HandleDrag drag = HandleDrag.Begin(rect, SelectionHandle.Right, new PixelPoint(301, 150));
        Assert.Equal(new PixelRect(100, 100, 200, 100), drag.Update(new PixelPoint(301, 150), image).Rect);
        (PixelRect plusOne, PixelPoint focus) = drag.Update(new PixelPoint(302, 170), image);
        Assert.Equal(new PixelRect(100, 100, 201, 100), plusOne);
        Assert.Equal(new PixelPoint(300, 170), focus);
        Assert.Equal(new PixelRect(100, 100, 210, 100), drag.Update(new PixelPoint(311, 150), image).Rect);
    }

    [Fact]
    public void HandleDrag_PastOppositeEdge_Flips() {
        PixelRect rect = new PixelRect(100, 100, 200, 100);
        HandleDrag drag = HandleDrag.Begin(rect, SelectionHandle.Left, new PixelPoint(100, 150));
        Assert.Equal(new PixelRect(90, 100, 210, 100), drag.Update(new PixelPoint(90, 150), image).Rect);
        // über die rechte Kante (Pixel 299) hinaus: der Bereich klappt um
        Assert.Equal(new PixelRect(299, 100, 51, 100), drag.Update(new PixelPoint(349, 150), image).Rect);
    }

    [Fact]
    public void HandleDrag_Corner_ChangesBothAxesAndClamps() {
        PixelRect rect = new PixelRect(100, 100, 200, 100);
        HandleDrag drag = HandleDrag.Begin(rect, SelectionHandle.BottomRight, new PixelPoint(299, 199));
        Assert.Equal(new PixelRect(100, 100, 206, 103), drag.Update(new PixelPoint(305, 202), image).Rect);
        Assert.Equal(new PixelRect(100, 100, 900, 700), drag.Update(new PixelPoint(5000, 5000), image).Rect);
        HandleDrag top = HandleDrag.Begin(rect, SelectionHandle.Top, new PixelPoint(200, 100));
        Assert.Equal(new PixelRect(100, 0, 200, 200), top.Update(new PixelPoint(250, -50), image).Rect);
    }

    [Fact]
    public void FindMonitorIndex_ForPoint() {
        // links ein Monitor mit negativen Bildschirmkoordinaten → in Bildpixeln 0..1919, rechts der primäre
        List<MonitorInfo> monitors = new List<MonitorInfo> {
            new MonitorInfo { Device = "B", Bounds = new PixelRect(1920, 0, 1920, 1080), Primary = true },
            new MonitorInfo { Device = "A", Bounds = new PixelRect(0, 200, 1920, 1080) }
        };
        Assert.Equal(1, SelectionGeometry.FindMonitorIndex(monitors, new PixelPoint(0, 200)));
        Assert.Equal(1, SelectionGeometry.FindMonitorIndex(monitors, new PixelPoint(1919, 1279)));
        Assert.Equal(0, SelectionGeometry.FindMonitorIndex(monitors, new PixelPoint(1920, 0)));
        Assert.Equal(0, SelectionGeometry.FindMonitorIndex(monitors, new PixelPoint(3839, 1079)));
        // Lücke links oben (kein Monitor) → nächster Monitor
        Assert.Equal(1, SelectionGeometry.FindMonitorIndex(monitors, new PixelPoint(10, 5)));
        Assert.Equal(0, SelectionGeometry.FindMonitorIndex(monitors, new PixelPoint(3000, 1250)));
        Assert.Equal(-1, SelectionGeometry.FindMonitorIndex(new List<MonitorInfo>(), new PixelPoint(0, 0)));
        Assert.Equal(0, SelectionGeometry.FindPrimaryMonitorIndex(monitors));
    }

    [Fact]
    public void FindMonitorIndex_ForRect_UsesLargestOverlap() {
        List<MonitorInfo> monitors = new List<MonitorInfo> {
            new MonitorInfo { Bounds = new PixelRect(0, 0, 1920, 1080) },
            new MonitorInfo { Bounds = new PixelRect(1920, 0, 1920, 1080), Primary = true }
        };
        Assert.Equal(0, SelectionGeometry.FindMonitorIndex(monitors, new PixelRect(1800, 10, 200, 10)));
        Assert.Equal(1, SelectionGeometry.FindMonitorIndex(monitors, new PixelRect(1900, 10, 200, 10)));
        Assert.Equal(1, SelectionGeometry.FindPrimaryMonitorIndex(monitors));
    }
}
