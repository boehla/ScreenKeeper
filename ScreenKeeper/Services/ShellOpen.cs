using System.Diagnostics;
using ScreenKeeper.Services.Native;

namespace ScreenKeeper.Services;

/// <summary>
/// Ordner und Webseiten öffnen. Läuft ScreenKeeper mit Adminrechten (Autostart mit Adminrechten), geht das über
/// <c>explorer.exe</c>: Der vorhandene Explorer übernimmt und startet Ordnerfenster bzw. Browser ohne Adminrechte.
/// </summary>
public static class ShellOpen {
    public static string ExplorerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    public static void Open(string target) {
        using (Process? process = Process.Start(BuildStartInfo(target, ProcessElevation.IsCurrentElevated() == true))) {
        }
    }

    public static ProcessStartInfo BuildStartInfo(string target, bool elevated) {
        if(!elevated) return new ProcessStartInfo(target) { UseShellExecute = true };
        ProcessStartInfo info = new ProcessStartInfo(ExplorerPath) { UseShellExecute = false };
        info.ArgumentList.Add(target);
        return info;
    }
}
