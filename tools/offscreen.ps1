# Dev: keeps a dev run's game window off the user's screens for the whole run (tools/game.sh start runs it unless
# AFVR_ONSCREEN=1). The desktop's screens can sit left of the main one (a window at negative x is ON a screen), so the
# window goes past the right edge of the whole virtual screen; the focus goes back to whatever had it. Unity re-places
# its window as it applies its resolution, so it's checked again every half second until the game quits.
param([int]$ProcId, [int]$Seconds = 3600)
Add-Type -Namespace AFVR -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
public struct RECT { public int Left, Top, Right, Bottom; }
'@
$before = [AFVR.Win]::GetForegroundWindow()
$until = (Get-Date).AddSeconds($Seconds)
$moved = 0
while ((Get-Date) -lt $until) {
    $p = Get-Process -Id $ProcId -ErrorAction SilentlyContinue
    if (-not $p) { break }
    $h = $p.MainWindowHandle
    if ($h -ne [System.IntPtr]::Zero) {
        $edge = [AFVR.Win]::GetSystemMetrics(76) + [AFVR.Win]::GetSystemMetrics(78)
        $r = New-Object AFVR.Win+RECT
        [void][AFVR.Win]::GetWindowRect($h, [ref]$r)
        if ($r.Left -lt $edge) {
            # NOSIZE | NOZORDER | NOACTIVATE
            [void][AFVR.Win]::SetWindowPos($h, [System.IntPtr]::Zero, $edge + 200, 100, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
            if ($before -ne [System.IntPtr]::Zero -and $before -ne $h) { [void][AFVR.Win]::SetForegroundWindow($before) }
            $moved++
        }
    }
    Start-Sleep -Milliseconds $(if ($moved -eq 0) { 50 } else { 500 })
}
