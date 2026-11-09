using Microsoft.Win32;

namespace ScreenKeeper.Services;

public interface IAutostartService {
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

/// <summary>
/// Autostart über <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, Wert <c>ScreenKeeper</c>.
/// </summary>
public sealed class AutostartService : IAutostartService {
    private const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string valueName = "ScreenKeeper";

    public bool IsEnabled {
        get {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKeyPath, false)) {
                return key?.GetValue(valueName) is string value && value.Length > 0;
            }
        }
    }

    public void SetEnabled(bool enabled) {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(runKeyPath, true)) {
            if(enabled) {
                string exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Pfad der EXE ist nicht bekannt.");
                key.SetValue(valueName, $"\"{exePath}\"", RegistryValueKind.String);
            } else if(key.GetValue(valueName) != null) {
                key.DeleteValue(valueName, false);
            }
        }
    }
}
