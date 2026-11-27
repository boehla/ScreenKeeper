using System.Windows;
using System.Windows.Media;

namespace ScreenKeeper.Views;

/// <summary>
/// Monitor-Rahmen im Vollbild-Editor (ab 0.16): solange kein Bereich angezeigt wird, zeigt ein deckend blauer Rahmen
/// (3 Gerätepixel, innen an der Kante) jeden Monitor an – so sieht man, dass der Editor offen ist und wo die Monitore
/// liegen. Zeichnet in Ansichts-DIPs (nicht gezoomt), fängt keine Klicks ab und liegt nur über der Anzeige – nie im
/// ausgegebenen Bild. Die Balken berechnet <see cref="ViewModels.MonitorFramePlacement"/>.
/// </summary>
public sealed class MonitorFrameOverlay : FrameworkElement {
    private static readonly Brush frameBrush = freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)));

    private IReadOnlyList<Rect> bars = Array.Empty<Rect>();

    public MonitorFrameOverlay() {
        IsHitTestVisible = false;
        Focusable = false;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    /// <param name="bars">Balken in Ansichts-DIPs; leer = kein Rahmen</param>
    public void Update(IReadOnlyList<Rect> bars) {
        if(bars.SequenceEqual(this.bars)) return;
        this.bars = bars.ToArray();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc) {
        foreach(Rect bar in bars) dc.DrawRectangle(frameBrush, null, bar);
    }

    private static Brush freeze(Brush brush) {
        brush.Freeze();
        return brush;
    }
}
