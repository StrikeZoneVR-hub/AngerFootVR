param([string]$Out = "$env:TEMP\afvr_wincap.png", [string]$Name = "Anger Foot")
# Captures the game's window as it appears on screen (PrintWindow, full content), for checking what the monitor shows.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
[W]::SetProcessDPIAware() | Out-Null # (else a scaled display reports the window smaller than it is, and only part is captured)
$p = Get-Process | Where-Object { $_.ProcessName -eq $Name -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Output "no game window"; exit 1 }
$r = New-Object W+RECT
[W]::GetClientRect($p.MainWindowHandle, [ref]$r) | Out-Null
$w = [Math]::Max(1, $r.R - $r.L); $h = [Math]::Max(1, $r.B - $r.T)
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[W]::PrintWindow($p.MainWindowHandle, $dc, 3) | Out-Null
$g.ReleaseHdc($dc); $g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "saved $Out ${w}x${h}"
