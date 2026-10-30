using System.Runtime.InteropServices;
using System.Text;
using DazeMediaOsd.Core.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Wmi;

namespace DazeMediaOsd.Core.Brightness;

/// <summary>
/// The built-in screen through WMI in <c>ROOT\WMI</c>: <c>WmiMonitorBrightness</c> to read, the
/// <c>WmiSetBrightness</c> method of <c>WmiMonitorBrightnessMethods</c> to write. It is the interface Windows
/// offers laptop panels, so it needs no admin rights and finds no external monitor (D-039). Only the
/// <see cref="BrightnessService"/> worker, an MTA thread, calls it. Failures throw; nothing is retried here.
/// </summary>
public sealed partial class WmiBrightnessBackend : IBrightnessBackend
{
    internal const string GenericName = "Built-in screen";

    private const string Namespace = @"ROOT\WMI";
    private const string MethodsClass = "WmiMonitorBrightnessMethods";
    private const string SetMethod = "WmiSetBrightness";
    private const int NextTimeoutMs = 5000;
    private const int MaxRows = 256; // a screen or two exist; a query that keeps answering is broken
    private const int TimedOut = 0x00040004; // WBEM_S_TIMEDOUT: a success code, with nothing returned

    // Names are read once per connection; the model name is cosmetic.
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private IWbemServices? _services;

    public BrightnessPanel? Read()
    {
        var services = Connect();
        var panels = Query(services, "SELECT * FROM WmiMonitorBrightness", ReadPanel);
        var panel = panels.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        return panel is null ? null : panel with { Name = NameOf(services, panel.Id) };
    }

    public void Write(string id, int level)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 100);
        var services = Connect();
        IWbemClassObject definition = null!, signature = null!, output = null!, parameters = null!;
        IWbemCallResult callResult = null!; // stays empty: the call completes before it returns
        try
        {
            using (var className = new Bstr(MethodsClass))
                services.GetObject(className.Handle, WBEM_GENERIC_FLAG_TYPE.WBEM_FLAG_RETURN_WBEM_COMPLETE, null!,
                    ref definition, ref callResult);
            definition.GetMethod(SetMethod, 0, out signature, out output);
            signature.SpawnInstance(0, out parameters);
            // Timeout 0 = no revert timer: Windows documents it as "until changed", and it measured as such (D-039).
            object timeout = 0, brightness = (byte)level;
            parameters.Put("Timeout", 0, in timeout, 0);
            parameters.Put("Brightness", 0, in brightness, 0);
            using var path = new Bstr($"{MethodsClass}.InstanceName=\"{Escape(id)}\"");
            using var method = new Bstr(SetMethod);
            services.ExecMethod(path.Handle, method.Handle, WBEM_GENERIC_FLAG_TYPE.WBEM_FLAG_RETURN_WBEM_COMPLETE, null!, parameters);
        }
        finally
        {
            Release(parameters);
            Release(output);
            Release(signature);
            Release(definition);
            Release(callResult);
        }
    }

    public void Reset()
    {
        _names.Clear();
        if (_services is { } services)
        {
            _services = null;
            Marshal.ReleaseComObject(services);
        }
    }

    public void Dispose() => Reset();

    private IWbemServices Connect()
    {
        if (_services is { } existing)
            return existing;
        var locator = (IWbemLocator)new WbemLocator();
        try
        {
            using var ns = new Bstr(Namespace);
            locator.ConnectServer(ns.Value, default, default, default, 0, default, null!, out var services);
            try
            {
                Blanket(services);
            }
            catch
            {
                Marshal.ReleaseComObject(services);
                throw;
            }
            return _services = services;
        }
        finally
        {
            Marshal.ReleaseComObject(locator);
        }
    }

    // Lets WMI act as us while it runs a call, as its documentation asks of every client. A COM proxy keeps its
    // blanket per interface and once more for IUnknown; CsWin32's wrapper takes an object and so reaches only
    // IUnknown, which WMI then refuses with "access denied". Hence the interface pointer, set by hand (D-039).
    private static void Blanket<T>(T proxy) where T : class
    {
        var unknown = Marshal.GetIUnknownForObject(proxy);
        var itf = Marshal.GetComInterfaceForObject(proxy, typeof(T));
        try
        {
            SetBlanket(itf);
            SetBlanket(unknown);
        }
        finally
        {
            Marshal.Release(itf);
            Marshal.Release(unknown);
        }
    }

    private static void SetBlanket(nint proxy) =>
        Marshal.ThrowExceptionForHR(CoSetProxyBlanket(proxy, PInvoke.RPC_C_AUTHN_WINNT, PInvoke.RPC_C_AUTHZ_NONE, 0,
            (uint)RPC_C_AUTHN_LEVEL.RPC_C_AUTHN_LEVEL_CALL, (uint)RPC_C_IMP_LEVEL.RPC_C_IMP_LEVEL_IMPERSONATE, 0,
            (uint)EOLE_AUTHENTICATION_CAPABILITIES.EOAC_NONE));

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoSetProxyBlanket(nint proxy, uint authnService, uint authzService, nint serverPrincipal,
        uint authnLevel, uint impersonationLevel, nint authInfo, uint capabilities);

    private static BrightnessPanel? ReadPanel(IWbemClassObject screen)
    {
        if (Get(screen, "Active") is not true || Get(screen, "InstanceName") is not string { Length: > 0 } id)
            return null;
        if (ToInt(Get(screen, "CurrentBrightness")) is not { } level || level is < 0 or > 100)
            throw new InvalidDataException($"the level of {id} can't be read");
        var levels = new List<int>();
        if (Get(screen, "Level") is Array supported)
        {
            foreach (var item in supported)
            {
                if (ToInt(item) is { } percent && percent is >= 0 and <= 100)
                    levels.Add(percent);
            }
        }
        levels.Sort();
        return new BrightnessPanel(id, GenericName, level, levels);
    }

    private string NameOf(IWbemServices services, string id)
    {
        if (_names.TryGetValue(id, out var known))
            return known;
        var name = GenericName;
        try
        {
            var models = Query(services, "SELECT * FROM WmiMonitorID", monitor =>
                string.Equals(Get(monitor, "InstanceName") as string, id, StringComparison.OrdinalIgnoreCase)
                    ? Text(Get(monitor, "UserFriendlyName"))
                    : null);
            if (models.FirstOrDefault(m => m.Length > 0) is { } model)
                name = $"{GenericName} ({model})";
        }
        catch (Exception e) when (e is COMException or TimeoutException or InvalidDataException)
        {
            Log.Error("brightness: reading the screen's model name; using the generic name", e);
        }
        return _names[id] = name;
    }

    // The maker's model name as EDID has it: an array of character codes ending in 0.
    private static string Text(object? codes)
    {
        var text = new StringBuilder();
        if (codes is Array array)
        {
            foreach (var item in array)
            {
                if (ToInt(item) is not { } code || code == 0)
                    break;
                if (code is >= 32 and < 127)
                    text.Append((char)code);
            }
        }
        return text.ToString().Trim();
    }

    private static List<T> Query<T>(IWbemServices services, string wql, Func<IWbemClassObject, T?> read) where T : class
    {
        using var language = new Bstr("WQL");
        using var query = new Bstr(wql);
        services.ExecQuery(language.Value, query.Value,
            WBEM_GENERIC_FLAG_TYPE.WBEM_FLAG_RETURN_IMMEDIATELY | WBEM_GENERIC_FLAG_TYPE.WBEM_FLAG_FORWARD_ONLY, null!, out var rows);
        try
        {
            Blanket(rows); // the enumerator is a proxy of its own, and WMI refuses it without the blanket
            var results = new List<T>();
            var row = new IWbemClassObject[1];
            for (var seen = 0; ; seen++)
            {
                var hr = rows.Next(NextTimeoutMs, 1, row, out var returned);
                hr.ThrowOnFailure();
                if (hr.Value == TimedOut)
                    throw new TimeoutException($"WMI did not answer \"{wql}\" in time");
                if (returned == 0)
                    return results;
                if (seen >= MaxRows)
                    throw new InvalidDataException($"WMI keeps answering \"{wql}\"");
                var current = row[0];
                row[0] = null!;
                try
                {
                    if (read(current) is { } item)
                        results.Add(item);
                }
                finally
                {
                    Release(current);
                }
            }
        }
        finally
        {
            Release(rows);
        }
    }

    private static object? Get(IWbemClassObject row, string name)
    {
        object value = null!;
        row.Get(name, 0, ref value);
        return value;
    }

    // WMI hands numbers over as whatever its CIM type maps to: uint8 as byte, uint16 and uint32 as int.
    private static int? ToInt(object? value) => value switch
    {
        byte b => b,
        short s => s,
        ushort s => s,
        int i => i,
        uint u when u <= int.MaxValue => (int)u,
        _ => null,
    };

    private static string Escape(string text) => text.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void Release(object? com)
    {
        if (com is not null)
            Marshal.ReleaseComObject(com);
    }

    /// <summary>A BSTR for one call, freed after it.</summary>
    private sealed class Bstr(string text) : IDisposable
    {
        private readonly SysFreeStringSafeHandle _handle = new(Marshal.StringToBSTR(text), ownsHandle: true);

        /// <summary>For the generated overloads that take a <see cref="SafeHandle"/>.</summary>
        public SafeHandle Handle => _handle;

        /// <summary>For the raw interface methods.</summary>
        public BSTR Value => (BSTR)_handle.DangerousGetHandle();

        public void Dispose() => _handle.Dispose();
    }
}
