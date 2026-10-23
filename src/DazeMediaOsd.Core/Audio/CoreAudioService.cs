using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.Media.KernelStreaming;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;

namespace DazeMediaOsd.Core.Audio;

/// <summary>
/// Tracks the default output and input endpoint through Core Audio. All COM calls run on one MTA
/// worker thread: Core Audio callbacks arrive on arbitrary threads and must not re-enter
/// Register/Unregister, so they only copy their arguments and post to the worker.
/// </summary>
public sealed class CoreAudioService : IAudioService
{
    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _worker;
    private readonly NotificationClient _notifications;
    private readonly Endpoint _output = new(EndpointFlow.Output);
    private readonly Endpoint _input = new(EndpointFlow.Input);
    // Active endpoints of either flow, kept to tell a real connect/disconnect from a repeated state, and to
    // name one whose device is already gone (or refuses reads) when it disconnects.
    private readonly Dictionary<string, ActiveEndpoint> _active = [];
    private IMMDeviceEnumerator? _enumerator;
    private volatile AudioSnapshot _current = AudioSnapshot.Empty;
    private volatile Task? _listing; // the last Endpoints request handed to the worker

    /// <exception cref="InvalidOperationException">Core Audio could not be initialized.</exception>
    public CoreAudioService()
    {
        _notifications = new NotificationClient(this);
        _worker = new Thread(RunWorker) { IsBackground = true, Name = "CoreAudio" };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();

        using var ready = new ManualResetEventSlim();
        Exception? failure = null;
        Post(() =>
        {
            try
            {
                Initialize();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failure = e;
            }
            finally
            {
                ready.Set();
            }
        });
        ready.Wait();
        // Startup failure is not an ordinary queue step: the caller must not suppress the native flyout
        // on top of a service that will never report anything.
        if (failure is not null)
        {
            Dispose();
            throw new InvalidOperationException("Core Audio initialization failed.", failure);
        }
    }

    public AudioSnapshot Current => _current;

    public event EventHandler<AudioChangedEventArgs>? Changed;

    public IReadOnlyList<AudioEndpoint> Endpoints(EndpointFlow flow)
    {
        if (Thread.CurrentThread == _worker)
            return TryReadEndpoints(flow); // from a Changed handler: waiting on ourselves would deadlock
        // A worker that missed the last deadline is still stuck: answer at once, so a menu asking for both
        // flows waits at most one deadline in total.
        if (_listing is { IsCompleted: false })
            return [];
        var result = new TaskCompletionSource<IReadOnlyList<AudioEndpoint>>();
        if (!Post(() => result.SetResult(TryReadEndpoints(flow))))
            return []; // disposed
        _listing = result.Task;
        // A menu must not hang on a stuck audio service: show no devices instead.
        if (result.Task.Wait(TimeSpan.FromSeconds(2)))
            return result.Task.Result;
        Log.Info($"listing {flow} endpoints timed out");
        return [];
    }

    public void SetDefault(string id, EndpointFlow flow)
    {
        ArgumentNullException.ThrowIfNull(id);
        Post(() =>
        {
            // eConsole + eMultimedia, as Sound settings does; eCommunications stays the user's choice (D-019).
            var policy = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                foreach (var role in (ReadOnlySpan<ERole>)[ERole.eConsole, ERole.eMultimedia])
                {
                    var hr = policy.SetDefaultEndpoint(id, (int)role);
                    if (hr < 0)
                        Log.Info($"setting default {flow} ({role}) to {id} failed: 0x{hr:X8}");
                }
            }
            finally
            {
                Marshal.ReleaseComObject(policy);
            }
        });
    }

    public ConnectResult Connect(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (Thread.CurrentThread == _worker)
            return TryConnect(id);
        var result = new TaskCompletionSource<ConnectResult>();
        if (!Post(() => result.SetResult(TryConnect(id))))
            return ConnectResult.NotSupported; // disposed
        if (result.Task.Wait(TimeSpan.FromSeconds(2)))
            return result.Task.Result;
        // Still queued or still asking: the request may yet go out, so a later connect still switches.
        Log.Info($"connecting {id} timed out");
        return ConnectResult.Requested;
    }

    private ConnectResult TryConnect(string id)
    {
        try
        {
            return RequestConnect(id);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // The endpoint or a sibling vanished while being read.
            Log.Info($"connecting {id}: failed ({e.GetType().Name} 0x{e.HResult:X8})");
            return ConnectResult.NotSupported;
        }
    }

    // Asks the endpoint and its inactive siblings (same container: the same headset's other profile). On
    // Intel Smart Sound the offloaded A2DP endpoint accepts the request and does nothing; the hands-free one
    // connects the whole device. A disconnected device's endpoints flip between unplugged and not present
    // (after a Windows "Disconnect" too), and both take the request (D-023). An unpaired device's
    // endpoints fail it, so not present is tried too.
    private ConnectResult RequestConnect(string id)
    {
        var device = DeviceById(id);
        if (device is null)
            return ConnectResult.NotSupported; // never existed here, or unpaired and gone
        Guid? container;
        try
        {
            device.GetState(out var state);
            if (state == DEVICE_STATE.DEVICE_STATE_ACTIVE)
                return ConnectResult.AlreadyConnected;
            if (state == DEVICE_STATE.DEVICE_STATE_DISABLED)
            {
                Log.Info($"connecting {id}: disabled, left alone");
                return ConnectResult.NotSupported;
            }
            container = ContainerId(device);
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }

        var asked = new List<string> { id };
        if (container is { } shared && shared != LocalMachine)
        {
            _enumerator!.EnumAudioEndpoints(
                EDataFlow.eAll, DEVICE_STATE.DEVICE_STATE_UNPLUGGED | DEVICE_STATE.DEVICE_STATE_NOTPRESENT, out var devices);
            try
            {
                devices.GetCount(out var count);
                for (uint i = 0; i < count; i++)
                {
                    devices.Item(i, out var sibling);
                    try
                    {
                        if (ContainerId(sibling) == shared && DeviceId(sibling) is var siblingId && siblingId != id)
                            asked.Add(siblingId);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(sibling);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(devices);
            }
        }

        var requested = 0;
        foreach (var endpoint in asked)
        {
            try
            {
                SendReconnect(endpoint);
                requested++;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // A driver without KSPROPSETID_BtAudio (wired, USB) answers with an error HRESULT; an old
                // pairing's offload filter is gone (0x80070002/3, which .NET throws as IO exceptions).
                Log.Info($"connecting {endpoint}: not supported ({e.GetType().Name} 0x{e.HResult:X8})");
            }
        }
        Log.Info($"connecting {id}: requested on {requested} of {asked.Count} endpoint(s)");
        return requested > 0 ? ConnectResult.Requested : ConnectResult.NotSupported;
    }

    // The documented route to a filter's IKsControl: endpoint topology → connector → the adapter's KS filter
    // as an IMMDevice → Activate. KSPROPERTY_ONESHOT_RECONNECT is what Sound control panel's "Connect" sends;
    // it only starts the connection and returns at once.
    private unsafe void SendReconnect(string id)
    {
        IMMDevice? device = null;
        IDeviceTopology? topology = null;
        IConnector? connector = null;
        IMMDevice? filter = null;
        IKsControl? control = null;
        try
        {
            _enumerator!.GetDevice(id, out device);
            var iid = typeof(IDeviceTopology).GUID;
            device.Activate(&iid, CLSCTX.CLSCTX_ALL, null, out var activated);
            topology = (IDeviceTopology)activated;
            topology.GetConnector(0, out connector);
            PWSTR filterId;
            connector.GetDeviceIdConnectedTo(&filterId);
            try
            {
                _enumerator.GetDevice(filterId.ToString(), out filter);
            }
            finally
            {
                Marshal.FreeCoTaskMem((nint)filterId.Value);
            }
            iid = typeof(IKsControl).GUID;
            filter.Activate(&iid, CLSCTX.CLSCTX_ALL, null, out activated);
            control = (IKsControl)activated;

            var property = default(KSIDENTIFIER);
            property.Set = typeof(KSPROPSETID_BtAudio).GUID;
            property.Id = (uint)KSPROPERTY_BTAUDIO.KSPROPERTY_ONESHOT_RECONNECT;
            property.Flags = PInvoke.KSPROPERTY_TYPE_GET;
            control.KsProperty(&property, (uint)sizeof(KSIDENTIFIER), null, 0, out _);
        }
        finally
        {
            foreach (var com in (ReadOnlySpan<object?>)[control, filter, connector, topology, device])
            {
                if (com is not null)
                    Marshal.ReleaseComObject(com);
            }
        }
    }

    // Built-in devices all share this container; it says nothing about which endpoints belong together.
    private static readonly Guid LocalMachine = new("00000000-0000-0000-ffff-ffffffffffff");

    /// <summary>PKEY_Device_ContainerId: one per physical device, shared by its endpoints; null if unreadable.</summary>
    private static unsafe Guid? ContainerId(IMMDevice device)
    {
        try
        {
            device.OpenPropertyStore(STGM.STGM_READ, out var store);
            try
            {
                var key = PInvoke.PKEY_Device_ContainerId;
                store.GetValue(&key, out var value);
                try
                {
                    return value.vt == Windows.Win32.System.Variant.VARENUM.VT_CLSID && value.puuid is not null
                        ? *value.puuid
                        : null;
                }
                finally
                {
                    PInvoke.PropVariantClear(ref value);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    private List<AudioEndpoint> TryReadEndpoints(EndpointFlow flow)
    {
        try
        {
            return ReadEndpoints(flow);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Includes an enumerator already released by Dispose.
            Log.Error($"listing {flow} endpoints", e);
            return [];
        }
    }

    private List<AudioEndpoint> ReadEndpoints(EndpointFlow flow)
    {
        var dataFlow = flow == EndpointFlow.Output ? EDataFlow.eRender : EDataFlow.eCapture;
        _enumerator!.EnumAudioEndpoints(dataFlow, DEVICE_STATE.DEVICE_STATE_ACTIVE, out var devices);
        try
        {
            devices.GetCount(out var count);
            var result = new List<AudioEndpoint>((int)count);
            for (uint i = 0; i < count; i++)
            {
                devices.Item(i, out var device);
                try
                {
                    var (_, name, bluetooth, kind) = Describe(device);
                    result.Add(new AudioEndpoint(DeviceId(device), name, kind, bluetooth));
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
            return result;
        }
        finally
        {
            Marshal.ReleaseComObject(devices);
        }
    }

    private void Initialize()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(_notifications);
        _enumerator.EnumAudioEndpoints(EDataFlow.eAll, DEVICE_STATE.DEVICE_STATE_ACTIVE, out var devices);
        try
        {
            devices.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                devices.Item(i, out var device);
                try
                {
                    _active[DeviceId(device)] = Describe(device);
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(devices);
        }
        Attach(_output, DefaultDevice(EndpointFlow.Output));
        Attach(_input, DefaultDevice(EndpointFlow.Input));
    }

    // Callbacks can still be in flight while Dispose completes the queue: late posts are dropped (false).
    private bool Post(Action action)
    {
        try
        {
            _work.Add(action);
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    private void RunWorker()
    {
        foreach (var action in _work.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // One failed step (e.g. a device vanishing mid-attach) must not kill the worker, and with
                // it the process: log it and keep serving the queue.
                Log.Error("Core Audio", e);
            }
        }
    }

    private Endpoint EndpointFor(EndpointFlow flow) => flow == EndpointFlow.Output ? _output : _input;

    private IMMDevice? DefaultDevice(EndpointFlow flow)
    {
        try
        {
            var dataFlow = flow == EndpointFlow.Output ? EDataFlow.eRender : EDataFlow.eCapture;
            _enumerator!.GetDefaultAudioEndpoint(dataFlow, ERole.eConsole, out var device);
            return device;
        }
        catch (COMException e) when (e.HResult == (int)HRESULT.E_NOTFOUND)
        {
            return null; // no endpoint of this flow, e.g. no microphone
        }
    }

    private IMMDevice? DeviceById(string? id)
    {
        if (id is null)
            return null; // the notification says there is no default endpoint anymore
        try
        {
            _enumerator!.GetDevice(id, out var device);
            return device;
        }
        catch (COMException e) when (e.HResult == (int)HRESULT.E_NOTFOUND)
        {
            return null; // gone again before we got to it; a later notification follows
        }
    }

    /// <summary>Tracks <paramref name="device"/> (released here) as this flow's endpoint; null clears it.</summary>
    private unsafe void Attach(Endpoint endpoint, IMMDevice? device)
    {
        Detach(endpoint);
        EndpointState? state = null;
        if (device is not null)
        {
            try
            {
                var id = DeviceId(device);
                var iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(&iid, CLSCTX.CLSCTX_ALL, null, out var activated);
                var volume = (IAudioEndpointVolume)activated;
                volume.GetMasterVolumeLevelScalar(out var level);
                BOOL muted;
                volume.GetMute(&muted);

                var callback = new VolumeCallback(this, endpoint.Flow, id);
                volume.RegisterControlChangeNotify(callback);
                (endpoint.Volume, endpoint.Callback, endpoint.Id) = (volume, callback, id);
                state = new EndpointState(id, FriendlyName(device), level, muted, Kind(device));
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        _current = _current.With(endpoint.Flow, state);
    }

    private static void Detach(Endpoint endpoint)
    {
        if (endpoint.Volume is { } volume)
        {
            try
            {
                volume.UnregisterControlChangeNotify(endpoint.Callback!);
            }
            catch (COMException)
            {
                // Endpoint already gone (device unplugged); nothing to unregister from.
            }
            Marshal.ReleaseComObject(volume);
        }
        (endpoint.Volume, endpoint.Callback, endpoint.Id) = (null, null, null);
    }

    private void Raise(AudioSnapshot previous, EndpointFlow flow, AudioChangeKind kind, DeviceEvent? device = null)
    {
        var args = new AudioChangedEventArgs(previous, _current, flow, kind, device);
        try
        {
            Changed?.Invoke(this, args);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error($"Audio change handler ({flow} {kind})", e);
        }
    }

    private void OnVolume(EndpointFlow flow, string id, float level, bool muted)
    {
        var previous = _current;
        // A late notification from the endpoint we just detached carries the old id.
        if (previous.Get(flow) is not { } state || state.Id != id)
            return;
        if (state.Volume == level && state.Muted == muted)
            return;
        _current = previous.With(flow, state with { Volume = level, Muted = muted });
        Raise(previous, flow, AudioChangeKind.VolumeOrMute);
    }

    // Uses the id from the notification: re-querying the default could still return the old device.
    private void OnDefaultDevice(EndpointFlow flow, string? deviceId)
    {
        var previous = _current;
        Attach(EndpointFor(flow), DeviceById(deviceId));
        // Windows reports each role separately and sometimes repeats itself.
        if (previous.Get(flow)?.Id != _current.Get(flow)?.Id)
            Raise(previous, flow, AudioChangeKind.DefaultDevice);
    }

    private void OnRenamed(string id)
    {
        foreach (var flow in (ReadOnlySpan<EndpointFlow>)[EndpointFlow.Output, EndpointFlow.Input])
        {
            var previous = _current;
            if (previous.Get(flow) is not { } state || state.Id != id)
                continue;
            _enumerator!.GetDevice(id, out var device);
            try
            {
                var name = FriendlyName(device);
                if (name == state.Name)
                    continue;
                _current = previous.With(flow, state with { Name = name });
                Raise(previous, flow, AudioChangeKind.DeviceRenamed);
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
    }

    // Added, removed and state-change notifications all end here: whatever they claim, the device's state
    // now decides. A Bluetooth headset is several endpoints, and Windows repeats states.
    private void OnDeviceState(string id)
    {
        var device = DeviceById(id);
        try
        {
            var wasActive = _active.TryGetValue(id, out var known);
            var isActive = false;
            if (device is not null)
            {
                device.GetState(out var state);
                isActive = state == DEVICE_STATE.DEVICE_STATE_ACTIVE;
            }
            if (isActive == wasActive)
                return;
            // Before touching the set: a failed property read must not swallow the transition. A removed
            // device can't be read at all: what we knew while it was active names it.
            var (flow, name, bluetooth, kind) = isActive ? Describe(device!) : known!;
            if (!isActive && device is not null)
            {
                var current = TryProperty(device, PInvoke.PKEY_Device_FriendlyName);
                name = current.Length == 0 ? name : current;
                bluetooth = bluetooth || IsBluetooth(device);
            }
            if (isActive)
                _active[id] = new ActiveEndpoint(flow, name, bluetooth, kind);
            else
                _active.Remove(id);
            Log.Info($"audio device {(isActive ? "connected" : "disconnected")}: {flow} \"{name}\"{(bluetooth ? " bluetooth" : "")}");
            Raise(_current, flow, isActive ? AudioChangeKind.DeviceConnected : AudioChangeKind.DeviceDisconnected,
                new DeviceEvent(name, flow, isActive, bluetooth, kind, id));
        }
        finally
        {
            if (device is not null)
                Marshal.ReleaseComObject(device);
        }
    }

    private static ActiveEndpoint Describe(IMMDevice device) =>
        new(Flow(device), TryProperty(device, PInvoke.PKEY_Device_FriendlyName), IsBluetooth(device), Kind(device));

    private sealed record ActiveEndpoint(EndpointFlow Flow, string Name, bool Bluetooth, DeviceKind Kind);

    /// <summary>For the glyph only: a missing or unreadable form factor is Other.</summary>
    private static unsafe DeviceKind Kind(IMMDevice device)
    {
        try
        {
            device.OpenPropertyStore(STGM.STGM_READ, out var store);
            try
            {
                var key = PInvoke.PKEY_AudioEndpoint_FormFactor;
                store.GetValue(&key, out var value);
                try
                {
                    return value.vt == Windows.Win32.System.Variant.VARENUM.VT_UI4
                        ? DeviceKinds.FromFormFactor(value.Anonymous.Anonymous.Anonymous.ulVal)
                        : DeviceKind.Other;
                }
                finally
                {
                    PInvoke.PropVariantClear(ref value);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch (COMException)
        {
            return DeviceKind.Other;
        }
    }

    // Undocumented endpoint keys holding the underlying device's instance id: native Bluetooth endpoints
    // have "{1}.BTHENUM\…" in pid 2; endpoints offloaded to the audio DSP (Intel Smart Sound) have the audio
    // adapter there and "{1}.BTHENUM\…" in pid 39, while their EnumeratorName says INTELAUDIO (26300).
    private static readonly Guid EndpointDeviceKeys = new("b3f8fa53-0004-438e-9003-51a46e139bfc");

    /// <summary>For the glyph only: a miss shows the speaker/mic glyph instead.</summary>
    private static bool IsBluetooth(IMMDevice device)
    {
        ReadOnlySpan<PROPERTYKEY> keys =
        [
            PInvoke.PKEY_Device_EnumeratorName,
            new() { fmtid = EndpointDeviceKeys, pid = 2 },
            new() { fmtid = EndpointDeviceKeys, pid = 39 },
        ];
        foreach (var key in keys)
        {
            var value = TryProperty(device, key);
            // BTHENUM (A2DP, offloaded hands-free), BTHHFENUM (hands-free), BTHLEDEVICE / BTHLE (LE Audio).
            if (value.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
                || value.Contains("BTHHFENUM", StringComparison.OrdinalIgnoreCase)
                || value.Contains("BTHLE", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string TryProperty(IMMDevice device, PROPERTYKEY key)
    {
        try
        {
            return StringProperty(device, key);
        }
        catch (COMException)
        {
            return ""; // a disconnecting device may already refuse; the OSD falls back to a generic label
        }
    }

    private static EndpointFlow Flow(IMMDevice device)
    {
        var endpoint = (IMMEndpoint)device; // QueryInterface; the RCW is shared with device
        endpoint.GetDataFlow(out var flow);
        return flow == EDataFlow.eRender ? EndpointFlow.Output : EndpointFlow.Input;
    }

    private static unsafe string DeviceId(IMMDevice device)
    {
        PWSTR id;
        device.GetId(&id);
        try
        {
            return id.ToString();
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)id.Value);
        }
    }

    private static string FriendlyName(IMMDevice device) => StringProperty(device, PInvoke.PKEY_Device_FriendlyName);

    private static unsafe string StringProperty(IMMDevice device, PROPERTYKEY key)
    {
        device.OpenPropertyStore(STGM.STGM_READ, out var store);
        try
        {
            store.GetValue(&key, out var value);
            try
            {
                return value.vt == Windows.Win32.System.Variant.VARENUM.VT_LPWSTR ? value.pwszVal.ToString() : "";
            }
            finally
            {
                PInvoke.PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    public void Dispose()
    {
        if (_work.IsAddingCompleted)
            return;
        // Unregistering happens on the worker; callbacks that race it hit the tolerant Post above.
        Post(() =>
        {
            Detach(_output);
            Detach(_input);
            if (_enumerator is not null)
            {
                _enumerator.UnregisterEndpointNotificationCallback(_notifications);
                Marshal.ReleaseComObject(_enumerator);
                _enumerator = null;
            }
        });
        _work.CompleteAdding();
        _worker.Join();
        _work.Dispose();
    }

    private sealed class Endpoint(EndpointFlow flow)
    {
        public EndpointFlow Flow { get; } = flow;
        public IAudioEndpointVolume? Volume { get; set; }
        public VolumeCallback? Callback { get; set; }
        public string? Id { get; set; }
    }

    private sealed class VolumeCallback(CoreAudioService owner, EndpointFlow flow, string id) : IAudioEndpointVolumeCallback
    {
        public unsafe void OnNotify(AUDIO_VOLUME_NOTIFICATION_DATA* data)
        {
            // data is only valid during this call.
            var (level, muted) = (data->fMasterVolume, (bool)data->bMuted);
            owner.Post(() => owner.OnVolume(flow, id, level, muted));
        }
    }

    private sealed class NotificationClient(CoreAudioService owner) : IMMNotificationClient
    {
        public unsafe void OnDefaultDeviceChanged(EDataFlow flow, ERole role, PCWSTR defaultDeviceId)
        {
            if (role != ERole.eConsole)
                return;
            var endpoint = flow == EDataFlow.eRender ? EndpointFlow.Output : EndpointFlow.Input;
            var id = defaultDeviceId.Value is null ? null : defaultDeviceId.ToString(); // copy: dies with the call
            owner.Post(() => owner.OnDefaultDevice(endpoint, id));
        }

        public void OnPropertyValueChanged(PCWSTR deviceId, PROPERTYKEY key)
        {
            var friendlyName = PInvoke.PKEY_Device_FriendlyName;
            if (key.fmtid != friendlyName.fmtid || key.pid != friendlyName.pid)
                return;
            var id = deviceId.ToString(); // copy: the pointer dies with this call
            owner.Post(() => owner.OnRenamed(id));
        }

        public void OnDeviceStateChanged(PCWSTR deviceId, DEVICE_STATE newState) => Post(deviceId);

        public void OnDeviceAdded(PCWSTR deviceId) => Post(deviceId);

        public void OnDeviceRemoved(PCWSTR deviceId) => Post(deviceId);

        private void Post(PCWSTR deviceId)
        {
            var id = deviceId.ToString(); // copy: the pointer dies with this call
            owner.Post(() => owner.OnDeviceState(id));
        }
    }
}
