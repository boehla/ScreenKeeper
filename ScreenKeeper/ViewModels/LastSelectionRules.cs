using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>Kann der Knopf „Letzten Bereich“ den gemerkten Bereich übernehmen – und wenn nicht, warum?</summary>
public enum LastSelectionState {
    /// <summary>Übernehmen möglich.</summary>
    Available,
    /// <summary>Es ist noch kein Bereich gemerkt.</summary>
    None,
    /// <summary>Die Aufnahme hat eine andere Monitor-Anordnung als die aktuelle (Bildschirmkoordinaten passen nicht).</summary>
    OtherLayout,
    /// <summary>Der gemerkte Bereich liegt ganz außerhalb des Bildes.</summary>
    OutsideImage
}

/// <summary>
/// UI-freie Regeln für „Letzten Bereich“: Umrechnung Bildpixel ↔ Bildschirmkoordinaten des virtuellen Desktops,
/// Begrenzung auf das Bild, wann der Knopf aktiv ist und wann ein Bereich gemerkt wird.
/// </summary>
public static class LastSelectionRules {
    /// <summary>Bereich in Bildpixeln → Bildschirmkoordinaten (Ursprung des virtuellen Bildschirms der Aufnahme addieren).</summary>
    public static PixelRect ToScreen(PixelRect selection, PixelRect virtualScreen) {
        PixelPoint topLeft = ScreenCoordinates.ImageToScreen(new PixelPoint(selection.X, selection.Y), virtualScreen);
        return new PixelRect(topLeft.X, topLeft.Y, selection.Width, selection.Height);
    }

    /// <summary>
    /// Gemerkter Bereich (Bildschirmkoordinaten) → Bildpixel der aktuellen Aufnahme, auf das Bild begrenzt; null, wenn er
    /// das Bild nicht schneidet.
    /// </summary>
    public static PixelRect? ToImage(PixelRect screenRegion, PixelRect virtualScreen, PixelRect imageBounds) {
        PixelPoint topLeft = ScreenCoordinates.ScreenToImage(screenRegion.X, screenRegion.Y, virtualScreen);
        return SelectionGeometry.ClampRect(new PixelRect(topLeft.X, topLeft.Y, screenRegion.Width, screenRegion.Height), imageBounds);
    }

    /// <summary>
    /// Zustand des Knopfs: aktiv nur mit gemerktem Bereich, passender Monitor-Anordnung und Schnitt mit dem Bild.
    /// </summary>
    public static LastSelectionState GetState(PixelRect? screenRegion, bool sameLayout, PixelRect virtualScreen, PixelRect imageBounds) {
        if(!screenRegion.HasValue || !AppSettings.IsValidLastSelection(screenRegion.Value)) return LastSelectionState.None;
        if(!sameLayout) return LastSelectionState.OtherLayout;
        if(!ToImage(screenRegion.Value, virtualScreen, imageBounds).HasValue) return LastSelectionState.OutsideImage;
        return LastSelectionState.Available;
    }

    /// <summary>
    /// Bereich beim Schließen merken? Nur bei passender Monitor-Anordnung (sonst stimmen die Bildschirmkoordinaten nicht),
    /// nicht beim Verwerfen, nur mit Bereich und nur, wenn er in dieser Sitzung geändert wurde – eine alte Aufnahme aus
    /// der History, die nur angesehen wird, soll den zuletzt gewählten Bereich nicht überschreiben.
    /// </summary>
    public static bool ShouldRemember(PixelRect? initialSelection, PixelRect? finalSelection, bool sameLayout, bool discarded) {
        if(discarded || !sameLayout || !finalSelection.HasValue || finalSelection.Value.IsEmpty) return false;
        return initialSelection != finalSelection;
    }
}
