using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using DazeMediaOsd.Core.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
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
    private IMMDeviceEnumerator? _enumerator;
    private volatile AudioSnapshot _current = AudioSnapshot.Empty;

    public CoreAudioService()
    {
        _notifications = new NotificationClient(this);
        _worker = new Thread(RunWorker) { IsBackground = true, Name = "CoreAudio" };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();

        using var ready = new ManualResetEventSlim();
        Post(() =>
        {
            try
            {
                Initialize();
            }
            finally
            {
                ready.Set();
            }
        });
        ready.Wait();
    }

    public AudioSnapshot Current => _current;

    public event EventHandler<AudioChangedEventArgs>? Changed;

    private void Initialize()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(_notifications);
        Attach(_output);
        Attach(_input);
    }

    private void Post(Action action)
    {
        if (!_work.IsAddingCompleted)
            _work.Add(action);
    }

    private void RunWorker()
    {
        foreach (var action in _work.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception e) when (e is COMException or InvalidCastException or InvalidOperationException)
            {
                Log.Error("Core Audio", e);
            }
        }
    }

    private Endpoint EndpointFor(EndpointFlow flow) => flow == EndpointFlow.Output ? _output : _input;

    private unsafe void Attach(Endpoint endpoint)
    {
        Detach(endpoint);
        EndpointState? state = null;
        try
        {
            var dataFlow = endpoint.Flow == EndpointFlow.Output ? EDataFlow.eRender : EDataFlow.eCapture;
            _enumerator!.GetDefaultAudioEndpoint(dataFlow, ERole.eConsole, out var device);
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
                state = new EndpointState(id, FriendlyName(device), level, muted);
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        catch (COMException e) when (e.HResult == (int)HRESULT.E_NOTFOUND)
        {
            // No endpoint of this flow (e.g. no microphone): state stays null.
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

    private void Raise(AudioSnapshot previous, EndpointFlow flow, AudioChangeKind kind)
    {
        var args = new AudioChangedEventArgs(previous, _current, flow, kind);
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

    private void OnDefaultDevice(EndpointFlow flow)
    {
        var previous = _current;
        Attach(EndpointFor(flow));
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

    private static unsafe string FriendlyName(IMMDevice device)
    {
        device.OpenPropertyStore(STGM.STGM_READ, out var store);
        try
        {
            var key = PInvoke.PKEY_Device_FriendlyName;
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
        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, PCWSTR defaultDeviceId)
        {
            if (role != ERole.eConsole)
                return;
            var endpoint = flow == EDataFlow.eRender ? EndpointFlow.Output : EndpointFlow.Input;
            owner.Post(() => owner.OnDefaultDevice(endpoint));
        }

        public void OnPropertyValueChanged(PCWSTR deviceId, PROPERTYKEY key)
        {
            var friendlyName = PInvoke.PKEY_Device_FriendlyName;
            if (key.fmtid != friendlyName.fmtid || key.pid != friendlyName.pid)
                return;
            var id = deviceId.ToString(); // copy: the pointer dies with this call
            owner.Post(() => owner.OnRenamed(id));
        }

        public void OnDeviceStateChanged(PCWSTR deviceId, DEVICE_STATE newState)
        {
        }

        public void OnDeviceAdded(PCWSTR deviceId)
        {
        }

        public void OnDeviceRemoved(PCWSTR deviceId)
        {
        }
    }
}
