namespace ScreenKeeper.Models;

/// <summary>
/// Werte für die Linienenden (<c>startCap</c>/<c>endCap</c>) von Linie, Pfeil und Freihand.
/// Unbekannte Werte (z. B. aus neueren Versionen) bleiben im Modell erhalten und werden wie <see cref="None"/> gezeichnet.
/// </summary>
public static class LineCaps {
    public const string None = "none";
    public const string Arrow = "arrow";
    public const string Dot = "dot";
    public const string Square = "square";
    public const string Triangle = "triangle";

    /// <summary>Alle Werte in der Reihenfolge der Werkzeugleiste.</summary>
    public static IReadOnlyList<string> All { get; } = new[] { None, Arrow, Dot, Square, Triangle };

    public static bool IsValid(string? cap) {
        return cap != null && All.Contains(cap);
    }

    /// <summary>Gültiger Wert oder <paramref name="fallback"/>.</summary>
    public static string Normalize(string? cap, string fallback) {
        return IsValid(cap) ? cap! : fallback;
    }
}

/// <summary>
/// Gemeinsame Sicht auf Zeichenobjekte mit Linienenden (Linie, Pfeil, Freihand).
/// </summary>
public interface ILineCapAnnotation {
    List<PixelPoint> Points { get; set; }

    string Stroke { get; set; }

    double Thickness { get; set; }

    /// <summary>Symbol am ersten Punkt (siehe <see cref="LineCaps"/>).</summary>
    string StartCap { get; set; }

    /// <summary>Symbol am letzten Punkt (siehe <see cref="LineCaps"/>).</summary>
    string EndCap { get; set; }
}
