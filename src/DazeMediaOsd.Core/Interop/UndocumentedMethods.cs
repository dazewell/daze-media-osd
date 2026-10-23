using System.Runtime.InteropServices;

namespace DazeMediaOsd.Core.Interop;

internal static partial class UndocumentedMethods
{
    // Not in the Win32 metadata. Returns the z-band; the volume flyout host lives in ZBID_ABOVELOCK_UX.
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowBand(nint hwnd, out uint band);

    // uxtheme ordinals (1903+), as used by Notepad++/Explorer for dark Win32 popup menus. Process-wide.
    internal enum PreferredAppMode { Default, AllowDark, ForceDark, ForceLight }

    [LibraryImport("uxtheme.dll", EntryPoint = "#135")]
    internal static partial PreferredAppMode SetPreferredAppMode(PreferredAppMode mode);

    [LibraryImport("uxtheme.dll", EntryPoint = "#136")]
    internal static partial void FlushMenuThemes();
}
