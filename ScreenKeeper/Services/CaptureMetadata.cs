using ScreenKeeper.Models;

namespace ScreenKeeper.Services;

/// <summary>
/// Was die History und das Aufräumen aus der JSON-Datei einer Aufnahme brauchen. Wird je Eintrag nur einmal gelesen und
/// in <see cref="CaptureEntry.Metadata"/> (bzw. im Zwischenspeicher des <see cref="StorageService"/>) gehalten.
/// <see cref="Comment"/> ist der Kommentar der Aufnahme ("" = keiner); er wird mit durchsucht.
/// </summary>
public sealed record CaptureMetadata(bool IsEdited, bool IsBroken, bool Permanent, string ForegroundProcess, string ForegroundTitle, string Comment = "") {
    /// <summary>Aufnahme ohne JSON: nicht bearbeitet, nicht permanent, Programm unbekannt.</summary>
    public static CaptureMetadata None { get; } = new CaptureMetadata(false, false, false, "", "");

    /// <summary>JSON vorhanden, aber nicht lesbar.</summary>
    public static CaptureMetadata Broken { get; } = new CaptureMetadata(false, true, false, "", "");

    /// <summary>
    /// <see cref="ForegroundProcess"/>/<see cref="ForegroundTitle"/> bezeichnen die Aufnahme: bei gewähltem Bereich ggf.
    /// das Fenster, das ihn überwiegend füllt, sonst das Vordergrundfenster (siehe <see cref="CaptureDescription"/>).
    /// </summary>
    public static CaptureMetadata FromDocument(CaptureDocument document) {
        ForegroundInfo? foreground = CaptureDescription.Resolve(document);
        return new CaptureMetadata(document.IsEdited, false, document.Permanent, foreground?.Process ?? "", foreground?.Title ?? "", document.Comment ?? "");
    }

    public bool HasForeground => ForegroundProcess.Length > 0 || ForegroundTitle.Length > 0;

    public bool HasComment => Comment.Length > 0;
}
