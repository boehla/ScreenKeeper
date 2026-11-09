using System.Runtime.InteropServices;

namespace ScreenKeeper.Services.Native;

/// <summary>
/// P/Invoke-Deklarationen aus shell32.dll.
/// </summary>
public static class Shell32 {
    public const uint FO_DELETE = 0x0003;
    public const ushort FOF_SILENT = 0x0004;
    public const ushort FOF_NOCONFIRMATION = 0x0010;
    public const ushort FOF_ALLOWUNDO = 0x0040;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
