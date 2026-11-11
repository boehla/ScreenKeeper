using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// UI-freie Schrittlogik der Lupe im Editor: Mausrad = Zoom (ganze Schritte, 4–16), +/- = Größe (10er-Schritte im Bereich
/// des Schiebereglers im Einstellungsfenster, 80–400 px), dazu der Text der Tastenhilfe unter der Lupe.
/// </summary>
public static class MagnifierSteps {
    public const int SizeStep = 10;

    /// <summary>Nächster Zoom: <paramref name="direction"/> &gt; 0 = mehr, &lt; 0 = weniger; immer im erlaubten Bereich.</summary>
    public static int NextZoom(int current, int direction) {
        int zoom = Math.Clamp(current, AppSettings.MinMagnifierZoom, AppSettings.MaxMagnifierZoom);
        return Math.Clamp(zoom + Math.Sign(direction), AppSettings.MinMagnifierZoom, AppSettings.MaxMagnifierZoom);
    }

    /// <summary>
    /// Nächste Größe: auf das nächste Vielfache von <see cref="SizeStep"/> in Richtung <paramref name="direction"/>,
    /// begrenzt auf <see cref="SettingsViewModel.MagnifierSizeMin"/>–<see cref="SettingsViewModel.MagnifierSizeMax"/>.
    /// Ein Wert außerhalb dieses Bereichs (die Datei erlaubt 64–600) wird in den Bereich geführt.
    /// </summary>
    public static int NextSize(int current, int direction) {
        int min = SettingsViewModel.MagnifierSizeMin;
        int max = SettingsViewModel.MagnifierSizeMax;
        if(current < min) return min;
        if(current > max) return max;
        int next;
        if(direction > 0) {
            next = (int)Math.Floor(current / (double)SizeStep) * SizeStep + SizeStep;
        } else if(direction < 0) {
            next = (int)Math.Ceiling(current / (double)SizeStep) * SizeStep - SizeStep;
        } else {
            next = current;
        }
        return Math.Clamp(next, min, max);
    }

    /// <summary>
    /// Tastenhilfe unter der Lupe mit der gerade geltenden Belegung (Vollbild: Rad, Fenster: Alt+Rad) und den aktuellen Werten.
    /// </summary>
    public static string FormatKeyHints(bool windowed, int zoom, int size) {
        return Strings.Format(windowed ? Strings.MagnifierKeyHintsWindowed : Strings.MagnifierKeyHintsFullscreen, zoom, size);
    }
}
