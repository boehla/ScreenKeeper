using System.Globalization;
using System.Windows.Media;

namespace ScreenKeeper.Services;

/// <summary>
/// Farben der Zeichenobjekte: Umwandlung der <c>#AARRGGBB</c>-Strings des Modells in WPF-Farben und Pinsel,
/// Kontrastfarben und die Palette der Werkzeugleiste.
/// </summary>
public static class AnnotationColors {
    public const string Red = "#FFE53935";
    public const string White = "#FFFFFFFF";
    public const string Black = "#FF000000";

    /// <summary>Palette der Werkzeugleiste.</summary>
    public static IReadOnlyList<string> Palette { get; } = new[] {
        Red, "#FFFB8C00", "#FFFFEB3B", "#FF43A047", "#FF00ACC1",
        "#FF1E88E5", "#FF8E24AA", "#FFD81B60", "#FF757575", Black, White
    };

    private static readonly Dictionary<string, SolidColorBrush> brushCache = new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);
    private static readonly object sync = new object();

    /// <summary>
    /// <c>#AARRGGBB</c> oder <c>#RRGGBB</c> lesen; ungültige Werte liefern <paramref name="fallback"/>.
    /// </summary>
    public static Color Parse(string? text, Color fallback) {
        return TryParse(text, out Color color) ? color : fallback;
    }

    public static Color Parse(string? text) {
        return Parse(text, Color.FromRgb(0xE5, 0x39, 0x35));
    }

    public static bool TryParse(string? text, out Color color) {
        color = default;
        if(string.IsNullOrWhiteSpace(text)) return false;
        string value = text.Trim();
        if(value.StartsWith('#')) value = value.Substring(1);
        if(!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint number)) return false;
        if(value.Length == 6) {
            color = Color.FromArgb(0xFF, (byte)(number >> 16), (byte)(number >> 8), (byte)number);
            return true;
        }
        if(value.Length == 8) {
            color = Color.FromArgb((byte)(number >> 24), (byte)(number >> 16), (byte)(number >> 8), (byte)number);
            return true;
        }
        return false;
    }

    public static string ToHex(Color color) {
        return string.Create(CultureInfo.InvariantCulture, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");
    }

    /// <summary>
    /// Eingefrorener Pinsel (zwischengespeichert).
    /// </summary>
    public static SolidColorBrush GetBrush(string? text) {
        string key = text ?? "";
        lock(sync) {
            if(brushCache.TryGetValue(key, out SolidColorBrush? cached)) return cached;
            SolidColorBrush brush = new SolidColorBrush(Parse(text));
            brush.Freeze();
            if(brushCache.Count > 256) brushCache.Clear();
            brushCache[key] = brush;
            return brush;
        }
    }

    /// <summary>
    /// Gleiche Farbe mit anderer Deckkraft.
    /// </summary>
    public static Color WithAlpha(Color color, byte alpha) {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    /// <summary>
    /// Relative Helligkeit 0 … 1 (sRGB-Gewichtung, ohne Gamma – reicht für die Wahl einer Kontrastfarbe).
    /// </summary>
    public static double GetLuminance(Color color) {
        return (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
    }

    public static bool IsLight(string? text) {
        return GetLuminance(Parse(text)) > 0.6;
    }

    /// <summary>
    /// Hintergrund für Text in dieser Farbe: hell hinter dunklem Text, dunkel hinter hellem Text.
    /// </summary>
    public static string GetTextBackground(string? foreground) {
        return IsLight(foreground) ? "#E6202020" : "#F0FFFFFF";
    }
}
