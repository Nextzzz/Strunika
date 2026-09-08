using System.Runtime.InteropServices;

namespace Strunika.Mobile.Platforms.Windows;

/// <summary>
/// The classic Win32 open dialog (comdlg32), for when WinUI's FileOpenPicker
/// fails outright — it throws E_FAIL from an elevated process (Visual Studio
/// run as administrator), which is how the dev head is often started. This
/// one has no such condition.
/// </summary>
public static class Win32FileDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

    private const int OFN_FILEMUSTEXIST = 0x1000, OFN_PATHMUSTEXIST = 0x800, OFN_NOCHANGEDIR = 0x8, OFN_EXPLORER = 0x80000;

    /// <summary>Full path of the chosen file, or null when cancelled.</summary>
    public static string? Open(string title, string filterName, IEnumerable<string> extensions)
    {
        string pattern = string.Join(";", extensions.Select(e => "*" + e));
        var buffer = Marshal.AllocHGlobal(2 * 4096);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                hwndOwner = OwnerWindow(),
                lpstrFilter = $"{filterName} ({pattern})\0{pattern}\0\0",
                lpstrFile = buffer,
                nMaxFile = 4096,
                lpstrTitle = title,
                Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER,
            };
            return GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static IntPtr OwnerWindow()
    {
        try
        {
            if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window window)
                return WinRT.Interop.WindowNative.GetWindowHandle(window);
        }
        catch { /* no owner: the dialog still opens */ }
        return IntPtr.Zero;
    }
}
