using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace DazeMediaOsd.Core.Media;

/// <summary>
/// A media session's app id (AUMID) as the name the Start menu shows: "Telegram", "Zen", "Microsoft Edge"
/// (D-042). Shell lookups are cached per id; the result never changes while the app runs.
/// </summary>
public static partial class AppNames
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Empty when the id names nothing the user would recognise.</summary>
    public static string Resolve(string appId)
    {
        ArgumentNullException.ThrowIfNull(appId);
        return appId.Length == 0 ? "" : Cache.GetOrAdd(appId, id => ShellName(id) ?? FromExe(id));
    }

    /// <summary>An unpackaged app without a Start menu AUMID reports its exe ("Spotify.exe" → "Spotify").</summary>
    internal static string FromExe(string appId) =>
        appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && appId.IndexOfAny(['\\', '/']) < 0
            ? appId[..^4]
            : "";

    // shell:AppsFolder resolves packaged apps and Start menu shortcuts that carry an AUMID (Chromium browsers'
    // hashed ids too). Vtable call instead of a COM interface: one method, no marshalling layer.
    private static unsafe string? ShellName(string appId)
    {
        var iid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"); // IShellItem
        try
        {
            if (SHCreateItemFromParsingName(@"shell:AppsFolder\" + appId, 0, iid, out var item) < 0 || item == 0)
                return null;
            try
            {
                // IUnknown (3), BindToHandler, GetParent, then GetDisplayName.
                var getDisplayName = (delegate* unmanaged[Stdcall]<nint, uint, char**, int>)(*(nint**)item)[5];
                char* name = null;
                if (getDisplayName(item, 0 /* SIGDN_NORMALDISPLAY */, &name) < 0 || name is null)
                    return null;
                try
                {
                    var text = new string(name);
                    return text.Length == 0 ? null : text;
                }
                finally
                {
                    Marshal.FreeCoTaskMem((nint)name);
                }
            }
            finally
            {
                Marshal.Release(item);
            }
        }
        catch (Exception e) when (e is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null; // only costs the name: the line falls back to the exe or "Media"
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid riid, out nint item);
}
