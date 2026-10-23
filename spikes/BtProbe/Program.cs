// D-023 probe: can an audio endpoint's Bluetooth device be (dis)connected through KSPROPSETID_BtAudio?
// Not product code — re-run it after a Windows or audio driver update.
//
//   dotnet run --project spikes/BtProbe -- list               every endpoint: state, KS filter, BtAudio support (read-only)
//   dotnet run --project spikes/BtProbe -- connect "<id>" [s]     KSPROPERTY_ONESHOT_RECONNECT, then watch the state (15 s)
//   dotnet run --project spikes/BtProbe -- disconnect "<id>" [s]  KSPROPERTY_ONESHOT_DISCONNECT, then watch the state (15 s)
//
// Disconnect through the hands-free ("Headset") endpoint: on Intel Smart Sound the A2DP one accepts it and
// does nothing. Earbuds may reconnect on their own ~20 s later.
using System.Diagnostics;
using System.Runtime.InteropServices;

static class Program
{
    static readonly Guid BtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    const uint OneshotReconnect = 0, OneshotDisconnect = 1;
    const uint TypeGet = 1, TypeBasicSupport = 0x200;
    const uint StateAll = 0xF;

    static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "list";
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        switch (mode)
        {
            case "list":
                foreach (var flow in new[] { 0, 1 })
                {
                    Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(flow, StateAll, out var devices));
                    devices.GetCount(out var count);
                    for (uint i = 0; i < count; i++)
                    {
                        devices.Item(i, out var device);
                        Describe(enumerator, device);
                    }
                }
                return 0;
            case "connect" or "disconnect" when args.Length > 1:
                Marshal.ThrowExceptionForHR(enumerator.GetDevice(args[1], out var target));
                Describe(enumerator, target);
                var control = Control(enumerator, target, out _);
                var clock = Stopwatch.StartNew();
                var hr = Send(control, mode == "connect" ? OneshotReconnect : OneshotDisconnect, TypeGet);
                Console.WriteLine($"{mode}: hr 0x{hr:X8} after {clock.ElapsedMilliseconds} ms");
                target.GetState(out var last);
                Console.WriteLine($"  {clock.ElapsedMilliseconds,5} ms state {last}");
                var watch = TimeSpan.FromSeconds(args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 15);
                while (clock.Elapsed < watch)
                {
                    Thread.Sleep(50);
                    target.GetState(out var now);
                    if (now != last)
                        Console.WriteLine($"  {clock.ElapsedMilliseconds,5} ms state {now}");
                    last = now;
                }
                return hr < 0 ? 1 : 0;
            default:
                Console.Error.WriteLine("usage: list | connect <endpoint id> | disconnect <endpoint id>");
                return 2;
        }
    }

    static void Describe(IMMDeviceEnumerator enumerator, IMMDevice device)
    {
        device.GetId(out var id);
        device.GetState(out var state);
        Console.WriteLine($"{id}  state {state}  \"{Name(device)}\"  container {Container(device)}");
        try
        {
            var control = Control(enumerator, device, out var filterId);
            Console.WriteLine($"    filter {filterId}");
            Console.WriteLine($"    BtAudio basic support: reconnect 0x{Send(control, OneshotReconnect, TypeBasicSupport):X8}, " +
                $"disconnect 0x{Send(control, OneshotDisconnect, TypeBasicSupport):X8}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"    no KS control: {e.GetType().Name} 0x{e.HResult:X8} {e.Message}");
        }
    }

    static IKsControl Control(IMMDeviceEnumerator enumerator, IMMDevice device, out string filterId)
    {
        var iid = typeof(IDeviceTopology).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid, 0x17, 0, out var obj));
        var topology = (IDeviceTopology)obj;
        Marshal.ThrowExceptionForHR(topology.GetConnector(0, out var connector));
        Marshal.ThrowExceptionForHR(connector.GetDeviceIdConnectedTo(out filterId));
        Marshal.ThrowExceptionForHR(enumerator.GetDevice(filterId, out var filter));
        iid = typeof(IKsControl).GUID;
        Marshal.ThrowExceptionForHR(filter.Activate(ref iid, 0x17, 0, out obj));
        return (IKsControl)obj;
    }

    static int Send(IKsControl control, uint id, uint flags)
    {
        var property = new KsProperty { Set = BtAudio, Id = id, Flags = flags };
        // Basic support answers with access flags / a KSPROPERTY_DESCRIPTION: give it room.
        var data = new byte[64];
        return flags == TypeGet
            ? control.KsProperty(ref property, (uint)Marshal.SizeOf<KsProperty>(), null, 0, out _)
            : control.KsProperty(ref property, (uint)Marshal.SizeOf<KsProperty>(), data, (uint)data.Length, out _);
    }

    // PKEY_Device_ContainerId: one per physical device, shared by its endpoints (and its Bluetooth node).
    static string Container(IMMDevice device)
    {
        if (device.OpenPropertyStore(0, out var store) < 0)
            return "?";
        var key = new PropertyKey { Fmtid = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), Pid = 2 };
        var value = new PropVariant();
        if (store.GetValue(ref key, ref value) < 0 || value.Vt != 72)
            return $"? (vt {value.Vt})";
        return Marshal.PtrToStructure<Guid>(value.Pointer).ToString();
    }

    static string Name(IMMDevice device)
    {
        if (device.OpenPropertyStore(0, out var store) < 0)
            return "?";
        var key = new PropertyKey { Fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Pid = 14 };
        var value = new PropVariant();
        if (store.GetValue(ref key, ref value) < 0 || value.Vt != 31)
            return "?";
        return Marshal.PtrToStringUni(value.Pointer) ?? "?";
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KsProperty { public Guid Set; public uint Id; public uint Flags; }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid Fmtid; public uint Pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant { public ushort Vt; public ushort R1, R2, R3; public nint Pointer; public nint Pad; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, ref PropVariant value);
    }

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
    }

    [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IConnector
    {
        [PreserveSig] int GetType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector other);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected(out int connected);
        [PreserveSig] int GetConnectedTo(out IConnector other);
        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IKsControl
    {
        // A raw buffer, not the SAFEARRAY an unannotated array becomes on a COM interface.
        [PreserveSig]
        int KsProperty(ref KsProperty property, uint propertyLength,
            [In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] byte[]? data, uint dataLength, out uint returned);
    }
}
