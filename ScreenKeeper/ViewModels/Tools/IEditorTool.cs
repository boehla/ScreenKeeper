using System.Windows.Input;
using ScreenKeeper.Models;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Mausposition für Werkzeuge: kontinuierlich (<see cref="X"/>/<see cref="Y"/>, für Treffertests) und als Bildpixel
/// (<see cref="Pixel"/> = floor, nicht auf das Bild begrenzt).
/// </summary>
public readonly record struct PointerInput(double X, double Y, PixelPoint Pixel, ModifierKeys Modifiers) {
    /// <summary>Anzahl der Klicks beim Drücken (2 = Doppelklick); 0 bei Bewegungen.</summary>
    public int ClickCount { get; init; }

    public static PointerInput FromImage(double x, double y, ModifierKeys modifiers) {
        return new PointerInput(x, y, new PixelPoint(ViewTransform.FloorPixel(x), ViewTransform.FloorPixel(y)), modifiers);
    }

    /// <summary>Mitte des Pixels (z. B. nach einer Tastatur-Bewegung).</summary>
    public static PointerInput AtPixel(PixelPoint pixel, ModifierKeys modifiers) {
        return new PointerInput(pixel.X + 0.5, pixel.Y + 0.5, pixel, modifiers);
    }

    public bool Shift => (Modifiers & ModifierKeys.Shift) != 0;

    public bool Control => (Modifiers & ModifierKeys.Control) != 0;
}

/// <summary>
/// Ein Werkzeug des Editors (Bereich, später Rechteck, Pfeil, Text …). Das Fenster wandelt Mausereignisse in
/// Bildkoordinaten um und reicht sie über das <see cref="EditorViewModel"/> an das aktive Werkzeug weiter.
/// Linke Taste gedrückt → <see cref="PointerDown"/>, Bewegung (mit und ohne Taste) → <see cref="PointerMove"/>,
/// Loslassen → <see cref="PointerUp"/>. Änderungen am Dokument laufen über <see cref="EditorViewModel.UndoStack"/>;
/// Live-Vorschau während eines Ziehvorgangs über <see cref="EditorViewModel.NotifyOverlayChanged"/>.
/// </summary>
public interface IEditorTool {
    /// <summary>Eindeutiger Schlüssel, z. B. „region“.</summary>
    string Key { get; }

    /// <summary>Tooltip in der Werkzeugleiste (inkl. Taste).</summary>
    string ToolTip { get; }

    /// <summary>Symbol als Pfad-Daten (Koordinaten 0–20, wird als Linie gezeichnet).</summary>
    string IconData { get; }

    /// <summary>Läuft gerade ein Vorgang (Ziehen, Tastatur-Auswahl …), den Esc abbrechen soll?</summary>
    bool IsGestureActive { get; }

    /// <summary>Pixel, um den die Lupe zentriert wird; null = keine Lupe.</summary>
    PixelPoint? MagnifierFocus { get; }

    void Activate();

    void Deactivate();

    void PointerDown(PointerInput input);

    void PointerMove(PointerInput input);

    void PointerUp(PointerInput input);

    /// <summary>Laufenden Vorgang ohne Änderung am Dokument abbrechen (Esc, Mausfang verloren, Fenster schließt).</summary>
    void CancelGesture();

    EditorCursor GetCursor(PointerInput input);
}
