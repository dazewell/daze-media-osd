using System.Runtime.InteropServices;

namespace DazeMediaOsd.Core.Interop;

internal static partial class UndocumentedMethods
{
    // Not in the Win32 metadata. Returns the z-band; the volume flyout host lives in ZBID_ABOVELOCK_UX.
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowBand(nint hwnd, out uint band);
}
