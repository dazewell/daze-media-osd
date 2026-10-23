using System.Runtime.InteropServices;

namespace DazeMediaOsd.Core.Interop;

/// <summary>
/// Undocumented <c>IPolicyConfig</c>, what Sound settings uses to set the default endpoint (D-019). Only the
/// vtable order matters: the ten slots before <see cref="SetDefaultEndpoint"/> are format/period/share-mode
/// getters and setters we never call. Layout as in <c>scripts/audio-device.ps1</c>, verified on 26300.
/// </summary>
[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat();
    [PreserveSig] int GetDeviceFormat();
    [PreserveSig] int ResetDeviceFormat();
    [PreserveSig] int SetDeviceFormat();
    [PreserveSig] int GetProcessingPeriod();
    [PreserveSig] int SetProcessingPeriod();
    [PreserveSig] int GetShareMode();
    [PreserveSig] int SetShareMode();
    [PreserveSig] int GetPropertyValue();
    [PreserveSig] int SetPropertyValue();

    /// <param name="role">ERole: 0 eConsole, 1 eMultimedia, 2 eCommunications.</param>
    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
}

[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClient
{
}
