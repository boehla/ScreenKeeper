using ScreenKeeper.Models;

namespace ScreenKeeper.Services;

/// <summary>
/// Was die History und das Aufräumen aus der JSON-Datei einer Aufnahme brauchen. Wird je Eintrag nur einmal gelesen und
/// in <see cref="CaptureEntry.Metadata"/> (bzw. im Zwischenspeicher des <see cref="StorageService"/>) gehalten.
/// </summary>
public sealed record CaptureMetadata(bool IsEdited, bool IsBroken, bool Permanent, string ForegroundProcess, string ForegroundTitle) {
    /// <summary>Aufnahme ohne JSON: nicht bearbeitet, nicht permanent, Programm unbekannt.</summary>
    public static CaptureMetadata None { get; } = new CaptureMetadata(false, false, false, "", "");

    /// <summary>JSON vorhanden, aber nicht lesbar.</summary>
    public static CaptureMetadata Broken { get; } = new CaptureMetadata(false, true, false, "", "");

    public static CaptureMetadata FromDocument(CaptureDocument document) {
        ForegroundInfo? foreground = document.Foreground;
        return new CaptureMetadata(document.IsEdited, false, document.Permanent, foreground?.Process ?? "", foreground?.Title ?? "");
    }

    public bool HasForeground => ForegroundProcess.Length > 0 || ForegroundTitle.Length > 0;
}
