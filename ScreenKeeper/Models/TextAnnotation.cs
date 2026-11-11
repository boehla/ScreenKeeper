using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Text; <see cref="X"/>/<see cref="Y"/> ist die linke obere Ecke des Textfelds.
/// Zwei Arten: <b>freier Text</b> (<see cref="Height"/> fehlt; wächst mit dem Text, optional mit Umbruchbreite
/// <see cref="Width"/>) und <b>festes Textfeld</b> (<see cref="Width"/> und <see cref="Height"/> gesetzt; Text wird im
/// Feld umbrochen, ausgerichtet und am Feldrand abgeschnitten, Schriftgröße fest oder automatisch).
/// </summary>
public sealed class TextAnnotation : Annotation {
    [JsonIgnore]
    public override string Type => AnnotationTypes.Text;

    public int X { get; set; }

    public int Y { get; set; }

    public string Text { get; set; } = "";

    public string FontFamily { get; set; } = "Segoe UI";

    /// <summary>
    /// Schriftgröße in Bildpixeln. Bei <see cref="AutoFontSize"/> (nur festes Feld) wird sie nicht verwendet, bleibt aber
    /// als zuletzt gewählte feste Größe erhalten.
    /// </summary>
    public double FontSize { get; set; } = 24;

    public bool Bold { get; set; }

    /// <summary>Textfarbe als #AARRGGBB.</summary>
    public string Foreground { get; set; } = "#FFE53935";

    /// <summary>Hintergrundfarbe als #AARRGGBB; null = kein Hintergrund.</summary>
    public string? Background { get; set; }

    /// <summary>
    /// Breite in Bildpixeln. Freier Text (<see cref="Height"/> null): Umbruchbreite <b>ohne</b> Innenabstand (wie bis 0.9),
    /// null = ohne Umbruch, so breit wie der Text. Festes Feld (<see cref="Height"/> gesetzt): <b>äußere</b> Breite des
    /// Felds inklusive Innenabstand – so bleibt das Feld stehen, auch wenn sich der Innenabstand mit der (automatischen)
    /// Schriftgröße ändert.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Äußere Höhe des festen Textfelds in Bildpixeln (inklusive Innenabstand); null = freier Text ohne feste Höhe
    /// (ältere Dateien). Wirkt nur zusammen mit <see cref="Width"/>.
    /// </summary>
    public int? Height { get; set; }

    /// <summary>
    /// Schriftgröße automatisch: so groß wie möglich, dass der umbrochene Text ganz ins Feld passt. Wirkt nur beim festen
    /// Feld; freier Text verwendet immer <see cref="FontSize"/>.
    /// </summary>
    public bool AutoFontSize { get; set; }

    /// <summary>
    /// Ausrichtung (siehe <see cref="TextAlignments"/>): horizontal die Zeilenausrichtung, vertikal die Lage des Textblocks im
    /// festen Feld (bei freiem Text ohne Wirkung). Unbekannte Werte bleiben erhalten und wirken wie oben links.
    /// </summary>
    public string Alignment { get; set; } = TextAlignments.TopLeft;
}

/// <summary>
/// Werte für die Ausrichtung eines Texts (<c>alignment</c>): Zeile (oben/Mitte/unten) × Spalte (links/Mitte/rechts).
/// </summary>
public static class TextAlignments {
    public const string TopLeft = "topLeft";
    public const string TopCenter = "topCenter";
    public const string TopRight = "topRight";
    public const string MiddleLeft = "middleLeft";
    public const string MiddleCenter = "middleCenter";
    public const string MiddleRight = "middleRight";
    public const string BottomLeft = "bottomLeft";
    public const string BottomCenter = "bottomCenter";
    public const string BottomRight = "bottomRight";

    /// <summary>Alle Werte zeilenweise (oben links … unten rechts), wie im 3×3-Raster der Werkzeugleiste.</summary>
    public static IReadOnlyList<string> All { get; } = new[] { TopLeft, TopCenter, TopRight, MiddleLeft, MiddleCenter, MiddleRight, BottomLeft, BottomCenter, BottomRight };

    public static bool IsValid(string? alignment) {
        return alignment != null && All.Contains(alignment);
    }

    /// <summary>Gültiger Wert oder <paramref name="fallback"/>.</summary>
    public static string Normalize(string? alignment, string fallback) {
        return IsValid(alignment) ? alignment! : fallback;
    }

    /// <summary>Spalte 0 = links, 1 = Mitte, 2 = rechts (unbekannt: links).</summary>
    public static int GetColumn(string? alignment) {
        int index = alignment == null ? -1 : indexOf(alignment);
        return index < 0 ? 0 : index % 3;
    }

    /// <summary>Zeile 0 = oben, 1 = Mitte, 2 = unten (unbekannt: oben).</summary>
    public static int GetRow(string? alignment) {
        int index = alignment == null ? -1 : indexOf(alignment);
        return index < 0 ? 0 : index / 3;
    }

    private static int indexOf(string alignment) {
        for(int i = 0; i < All.Count; i++) {
            if(All[i] == alignment) return i;
        }
        return -1;
    }
}
