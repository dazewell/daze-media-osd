# Dev helper: presses keys through SendInput so the app (raw input) and Windows see them.
# Virtual-key codes: 173 = Mute, 174 = Volume Down, 175 = Volume Up. Example: pwsh scripts/press-key.ps1 175,174
param([Parameter(Mandatory)][int[]]$Keys)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class K {
  [StructLayout(LayoutKind.Sequential)] public struct KI { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
  [StructLayout(LayoutKind.Sequential)] public struct IN { public uint type; public KI ki; long pad; }
  [DllImport("user32", SetLastError = true)] public static extern uint SendInput(uint n, IN[] i, int size);
  public static void Press(ushort vk) {
    var a = new IN[2]; a[0].type = 1; a[0].ki.vk = vk; a[1].type = 1; a[1].ki.vk = vk; a[1].ki.flags = 2;
    // IN is 40 bytes on x64 (union at offset 8, padded to MOUSEINPUT's size), as SendInput requires.
    if (SendInput(2, a, Marshal.SizeOf<IN>()) != 2)
      throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SendInput injected nothing");
  }
}
"@
foreach ($k in $Keys) { [K]::Press([uint16]$k); Start-Sleep -Milliseconds 400 }
