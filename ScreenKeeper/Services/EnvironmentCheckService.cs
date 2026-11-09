using System.Diagnostics;
using Microsoft.Win32;
using ScreenKeeper.Resources;

namespace ScreenKeeper.Services;

public interface IEnvironmentCheckService {
    /// <summary>Hinweistexte zu Einstellungen/Programmen, die die Druck-Taste belegen könnten (nur Hinweise, es wird nichts geändert).</summary>
    IReadOnlyList<string> Check();
}

/// <summary>
/// Erkennt die Windows-Einstellung „Druck-Taste öffnet Bildschirmausschnitt“ und laufende andere Screenshot-Tools.
/// </summary>
public sealed class EnvironmentCheckService : IEnvironmentCheckService {
    private const string keyboardKeyPath = @"Control Panel\Keyboard";
    private const string snippingValueName = "PrintScreenKeyForSnippingEnabled";

    /// <summary>Prozessnamen bekannter Screenshot-Tools, die die Druck-Taste belegen.</summary>
    private static readonly string[] competingProcesses = { "Greenshot", "ShareX", "Lightshot" };

    public IReadOnlyList<string> Check() {
        List<string> hints = new List<string>();
        if(IsSnippingKeyEnabled()) hints.Add(Strings.HintSnippingEnabled);
        foreach(string name in competingProcesses) {
            if(isProcessRunning(name)) hints.Add(Strings.Format(Strings.HintProcessRunning, name));
        }
        return hints;
    }

    public static bool IsSnippingKeyEnabled() {
        try {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyboardKeyPath, false)) {
                object? value = key?.GetValue(snippingValueName);
                return value is int number && number == 1;
            }
        } catch(Exception ex) {
            Log.Error(ex, "Snipping-Einstellung konnte nicht gelesen werden");
            return false;
        }
    }

    private static bool isProcessRunning(string name) {
        Process[] processes = Process.GetProcessesByName(name);
        try {
            return processes.Length > 0;
        } finally {
            foreach(Process process in processes) process.Dispose();
        }
    }
}
