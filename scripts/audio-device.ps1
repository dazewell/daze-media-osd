# Dev helper: lists render endpoints, makes one the default for all roles, or enables/disables any
# endpoint (Sound settings' "Allow"/"Don't allow": a real device connect/disconnect for the app).
# Uses the undocumented IPolicyConfig (what Sound settings uses); dev-only, never ship it.
# Example: pwsh scripts/audio-device.ps1 -All; pwsh scripts/audio-device.ps1 -SetDefault "{0.0.0.00000000}.{guid}"
#          pwsh scripts/audio-device.ps1 -Enable "{0.0.1.00000000}.{guid}"   (then -Disable to put it back)
param([string]$SetDefault, [string]$Enable, [string]$Disable, [switch]$All)
Add-Type @"
using System; using System.Runtime.InteropServices;
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator { int EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection c); int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice d); }
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection { int GetCount(out int n); int Item(int i, out IMMDevice d); }
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice { int Activate(IntPtr a, int b, IntPtr c, out IntPtr d); int OpenPropertyStore(int a, out IntPtr s); int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id); int GetState(out int s); }
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig { int a(); int b(); int c(); int d(); int e(); int f(); int g(); int h(); int i(); int j();
  int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
  int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible); }
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator {}
[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] class PolicyConfigClient {}
public static class A {
  public static string Default() { IMMDevice d; ((IMMDeviceEnumerator)new MMDeviceEnumerator()).GetDefaultAudioEndpoint(0, 0, out d); string id; d.GetId(out id); return id; }
  public static string[] Render() { IMMDeviceCollection c; ((IMMDeviceEnumerator)new MMDeviceEnumerator()).EnumAudioEndpoints(0, 1, out c); int n; c.GetCount(out n);
    var r = new string[n]; for (int i = 0; i < n; i++) { IMMDevice d; c.Item(i, out d); d.GetId(out r[i]); } return r; }
  // State: 1 active, 2 disabled, 4 not present, 8 unplugged.
  public static string[] Every() { IMMDeviceCollection c; ((IMMDeviceEnumerator)new MMDeviceEnumerator()).EnumAudioEndpoints(2, 15, out c); int n; c.GetCount(out n);
    var r = new string[n]; for (int i = 0; i < n; i++) { IMMDevice d; c.Item(i, out d); string id; d.GetId(out id); int st; d.GetState(out st); r[i] = st + " " + id; } return r; }
  public static int Visible(string id, bool on) { return ((IPolicyConfig)new PolicyConfigClient()).SetEndpointVisibility(id, on ? 1 : 0); }
  public static int Set(string id) { var p = (IPolicyConfig)new PolicyConfigClient(); int hr = 0; for (int role = 0; role < 3; role++) hr |= p.SetDefaultEndpoint(id, role); return hr; }
}
"@
if ($SetDefault) { "set hr=" + [A]::Set($SetDefault) }
elseif ($Enable) { "enable hr=" + [A]::Visible($Enable, $true) }
elseif ($Disable) { "disable hr=" + [A]::Visible($Disable, $false) }
elseif ($All) { [A]::Every() | ForEach-Object { "state $_" } }
else { "default: " + [A]::Default(); [A]::Render() | ForEach-Object { "render: $_" } }
