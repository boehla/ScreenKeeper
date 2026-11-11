using ScreenKeeper.Models;

namespace ScreenKeeper.Services;

/// <summary>
/// Bezeichnung einer Aufnahme (Fenstertitel und Prozess, z. B. in der History). Ohne Bereich ist das das
/// Vordergrundfenster bei der Aufnahme; mit Bereich das Fenster, das im Bereich die meiste sichtbare Fläche belegt –
/// aber nur, wenn das mehr als die Hälfte des Bereichs ist und es weder Desktop noch Taskleiste ist und einen Titel hat.
/// Sonst bleibt es beim Vordergrundfenster. Rein rechnerisch (kein Win32); wird aus dem Dokument abgeleitet und nicht
/// gespeichert, folgt also jeder Änderung des Bereichs (auch Rückgängig).
/// </summary>
public static class CaptureDescription {
    /// <summary>Fensterklassen von Desktop und Taskleiste; sie liefern nie die Bezeichnung.</summary>
    public static IReadOnlyList<string> ShellWindowClasses { get; } = new[] { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    /// <summary>
    /// Titel und Prozess, die die Aufnahme bezeichnen; null = unbekannt (kein Vordergrundfenster und kein passendes
    /// Fenster im Bereich).
    /// </summary>
    public static ForegroundInfo? Resolve(CaptureDocument document) {
        if(document.Selection.HasValue) {
            WindowInfo? dominant = FindDominantWindow(document.Windows, document.Selection.Value);
            if(dominant != null) return new ForegroundInfo { Title = dominant.Title, Process = dominant.Process };
        }
        return document.Foreground;
    }

    /// <summary>
    /// Fenster mit der größten sichtbaren Fläche in <paramref name="area"/>: Die Fenster werden nach Z von oben nach unten
    /// durchgegangen, jedes zählt nur den Teil des Bereichs, den kein höher liegendes Fenster verdeckt (exakte
    /// Rechteck-Subtraktion). Null, wenn der Gewinner nicht mehr als 50 % des Bereichs belegt, Desktop bzw. Taskleiste
    /// ist oder keinen Titel hat. Bei gleicher Fläche gewinnt das höher liegende Fenster.
    /// </summary>
    public static WindowInfo? FindDominantWindow(IEnumerable<WindowInfo> windows, PixelRect area) {
        if(area.IsEmpty) return null;
        long total = (long)area.Width * area.Height;
        List<PixelRect> uncovered = new List<PixelRect> { area };
        WindowInfo? best = null;
        long bestArea = 0;
        foreach(WindowInfo window in windows.OrderBy(w => w.Z)) {
            if(uncovered.Count == 0) break;
            List<PixelRect> remaining = new List<PixelRect>();
            long visible = 0;
            foreach(PixelRect piece in uncovered) {
                PixelRect overlap = piece.Intersect(window.Bounds);
                if(overlap.IsEmpty) {
                    remaining.Add(piece);
                    continue;
                }
                visible += (long)overlap.Width * overlap.Height;
                subtract(piece, overlap, remaining);
            }
            uncovered = remaining;
            if(visible > bestArea) {
                best = window;
                bestArea = visible;
            }
        }
        if(best == null || bestArea * 2 <= total) return null;
        if(IsShellWindow(best) || string.IsNullOrWhiteSpace(best.Title)) return null;
        return best;
    }

    /// <summary>
    /// Desktop oder Taskleiste? Ohne bekannte Fensterklasse (ältere Dateien) wird der Desktop am Titel „Program Manager“
    /// des Explorers erkannt; Taskleiste und WorkerW haben ohnehin keinen Titel.
    /// </summary>
    public static bool IsShellWindow(WindowInfo window) {
        if(window.ClassName != null) return ShellWindowClasses.Contains(window.ClassName, StringComparer.Ordinal);
        return string.Equals(window.Process, "explorer", StringComparison.OrdinalIgnoreCase) && window.Title == "Program Manager";
    }

    /// <summary>
    /// Fügt die Teile von <paramref name="piece"/> außerhalb von <paramref name="overlap"/> (liegt ganz in
    /// <paramref name="piece"/>) als höchstens vier nicht überlappende Rechtecke an <paramref name="result"/> an.
    /// </summary>
    private static void subtract(PixelRect piece, PixelRect overlap, List<PixelRect> result) {
        // Streifen oberhalb und unterhalb über die volle Breite, links und rechts nur auf Höhe der Überlappung
        if(overlap.Y > piece.Y) result.Add(new PixelRect(piece.X, piece.Y, piece.Width, overlap.Y - piece.Y));
        if(overlap.Bottom < piece.Bottom) result.Add(new PixelRect(piece.X, overlap.Bottom, piece.Width, piece.Bottom - overlap.Bottom));
        if(overlap.X > piece.X) result.Add(new PixelRect(piece.X, overlap.Y, overlap.X - piece.X, overlap.Height));
        if(overlap.Right < piece.Right) result.Add(new PixelRect(overlap.Right, overlap.Y, piece.Right - overlap.Right, overlap.Height));
    }
}
