param([int[]]$Keys = @(175,174), [string]$Out, [int]$DelayMs = 900, [int]$GapMs = 60, [string]$Repo)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class S {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, uint rop);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public struct RECT { public int L, T, R, B; }
  public static IntPtr Find(uint pid, string cls, string title) { IntPtr f = IntPtr.Zero; EnumWindows((h,l)=>{ uint p; GetWindowThreadProcessId(h, out p); var t=new StringBuilder(256); var c=new StringBuilder(256); GetWindowText(h,t,256); GetClassName(h,c,256); if (p==pid && t.ToString()==title && c.ToString()==cls) f=h; return true; }, IntPtr.Zero); return f; }
}
'@
[void][S]::SetProcessDpiAwarenessContext([IntPtr]-4)
$app = Get-CimInstance Win32_Process -Filter "Name='DazeMediaOsd.exe'" | Where-Object { $_.CommandLine -match '--app' }
$h = [S]::Find([uint32]$app.ProcessId, 'WinUIDesktopWin32WindowClass', 'DazeMediaOsd')
if ($h -eq [IntPtr]::Zero) { throw 'OSD window not found' }
$log = Join-Path $env:LOCALAPPDATA 'DazeMediaOsd/logs/app.log'
$before = (Get-Content $log).Count
foreach ($k in $Keys) { [S]::keybd_event([byte]$k, 0, 0, [UIntPtr]::Zero); [S]::keybd_event([byte]$k, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds $GapMs }
$deadline = (Get-Date).AddSeconds(4)
do { Start-Sleep -Milliseconds 80; $new = Get-Content $log | Select-Object -Skip $before | Where-Object { $_ -match 'INFO show' } } until ($new -or (Get-Date) -gt $deadline)
Start-Sleep -Milliseconds $DelayMs
$r = New-Object S+RECT; [void][S]::GetWindowRect($h, [ref]$r)
$w = $r.R - $r.L; $ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc(); $sdc = [S]::GetDC([IntPtr]::Zero)
[void][S]::BitBlt($dc, 0, 0, $w, $ht, $sdc, $r.L, $r.T, 0x40CC0020)
[void][S]::ReleaseDC([IntPtr]::Zero, $sdc); $g.ReleaseHdc($dc); $g.Dispose()
$bmp.Save($Out)
"rect $($r.L),$($r.T) ${w}x$ht -> $Out"
$new | Select-Object -Last 1 | ForEach-Object { $_.Substring(0, [Math]::Min(260, $_.Length)) }

