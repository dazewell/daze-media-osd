param([string[]]$Pages = @('Pop-up','Shortcuts'), [string]$Prefix)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class W2 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  public struct POINT { public int X, Y; }
  public struct RECT { public int L, T, R, B; }
  public static IntPtr Find(uint pid, string title) { IntPtr f = IntPtr.Zero; EnumWindows((h,l)=>{ uint p; GetWindowThreadProcessId(h, out p); var t=new StringBuilder(256); GetWindowText(h,t,256); if (p==pid && t.ToString()==title) f=h; return true; }, IntPtr.Zero); return f; }
}
'@
[void][W2]::SetProcessDpiAwarenessContext([IntPtr]-4)
$app = Get-CimInstance Win32_Process -Filter "Name='DazeMediaOsd.exe'" | Where-Object { $_.CommandLine -match '--app' }
$h = [W2]::Find([uint32]$app.ProcessId, 'Surtitle')
if ($h -eq [IntPtr]::Zero) {
  $tray = [W2]::FindWindow('DazeMediaOsd.Tray', $null)
  [void][W2]::PostMessage($tray, 0x8001, [IntPtr]0, [IntPtr](0x400 -bor (1 -shl 16)))
  Start-Sleep -Seconds 3
  $h = [W2]::Find([uint32]$app.ProcessId, 'Surtitle')
}
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
foreach ($page in $Pages) {
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $page)
  $item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
  $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 1200
  $r = New-Object W2+RECT; [void][W2]::GetWindowRect($h, [ref]$r)
  # A real click on the page title: selecting by UI Automation leaves a keyboard focus frame on the nav item
  # whenever the last input was a key. Cursor and foreground window are put back.
  $p = New-Object W2+POINT; [void][W2]::GetCursorPos([ref]$p); $fg = [W2]::GetForegroundWindow()
  [void][W2]::SetForegroundWindow($h); [void][W2]::SetCursorPos($r.L + 1100, $r.T + 140)
  [W2]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [W2]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 500
  $bmp = New-Object System.Drawing.Bitmap ($r.R-$r.L), ($r.B-$r.T)
  $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc(); [void][W2]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc); $g.Dispose()
  [void][W2]::SetCursorPos($p.X, $p.Y); [void][W2]::SetForegroundWindow($fg)
  $out = "$Prefix-$($page.ToLower()).png"; $bmp.Save($out); "$out $($bmp.Width)x$($bmp.Height)"
}
