using System.Runtime.InteropServices;

namespace ScreenKeeper.Services.Native;

/// <summary>
/// P/Invoke-Deklarationen aus advapi32.dll.
/// </summary>
public static class Advapi32 {
    public const uint TOKEN_QUERY = 0x0008;
    public const int TokenElevationType = 18;
    public const int TokenElevation = 20;

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, out int tokenInformation, int tokenInformationLength, out int returnLength);
}

/// <summary>
/// Prüft, ob ein Prozess mit erhöhten Rechten (als Administrator) läuft. Wichtig für den Tastatur-Hook: Hat das
/// Vordergrundfenster höhere Rechte als ScreenKeeper, bekommt der Hook wegen UIPI keine Tasten.
/// </summary>
public static class ProcessElevation {
    /// <summary>True/false, oder null, wenn es sich nicht feststellen lässt (z. B. Zugriff verweigert).</summary>
    public static bool? IsElevated(uint processId) {
        IntPtr process = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if(process == IntPtr.Zero) return null;
        try {
            return isElevated(process);
        } finally {
            Kernel32.CloseHandle(process);
        }
    }

    public static bool? IsCurrentElevated() {
        // Pseudo-Handle, muss nicht geschlossen werden
        return isElevated(Kernel32.GetCurrentProcess());
    }

    /// <summary>
    /// Art des eigenen Tokens (<c>TOKEN_ELEVATION_TYPE</c>): 1 = Default (kein geteiltes Token – Standardbenutzer, UAC aus
    /// oder eingebauter Administrator), 2 = Full (erhöht), 3 = Limited (Admin mit UAC, nicht erhöht). Null = nicht ermittelbar.
    /// </summary>
    public static int? GetCurrentElevationType() {
        if(!Advapi32.OpenProcessToken(Kernel32.GetCurrentProcess(), Advapi32.TOKEN_QUERY, out IntPtr token)) return null;
        try {
            if(!Advapi32.GetTokenInformation(token, Advapi32.TokenElevationType, out int type, sizeof(int), out _)) return null;
            return type;
        } finally {
            Kernel32.CloseHandle(token);
        }
    }

    private static bool? isElevated(IntPtr process) {
        if(!Advapi32.OpenProcessToken(process, Advapi32.TOKEN_QUERY, out IntPtr token)) return null;
        try {
            if(!Advapi32.GetTokenInformation(token, Advapi32.TokenElevation, out int elevated, sizeof(int), out _)) return null;
            return elevated != 0;
        } finally {
            Kernel32.CloseHandle(token);
        }
    }
}
