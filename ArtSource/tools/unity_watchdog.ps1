# Unity hang watchdog for BulletHeavenArt (11-14, Kido after the 11-13 23:23 freeze: "이런일 없게해"; standing approval
# to force-restart a hung editor without asking).
#
# Every 30 s: if the BulletHeavenArt editor has shown "Hold on (busy for m:ss)" for 5 minutes or more (or has not
# responded for 5 minutes), it is hung. Then:
#   1. the last 300 lines of Editor.log go to ArtSource/Tools/hangs/hang_<time>.log (to find the cause),
#   2. every pending ArtSource/**/*_cmd.txt is moved to ArtSource/Tools/hangs/ (the command that hung must not replay),
#   3. Unity is killed and started again on the project,
#   4. the hung session's scene backups are copied to ArtSource/Tools/hangs/<time>_Backupscenes and removed, so Unity
#      starts without the "Scene backups detected" dialog (a hidden process cannot answer it).
# Log: ArtSource/Tools/watchdog_log.txt. Only one watchdog runs (pid file). Start:
#   powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File ArtSource\Tools\unity_watchdog.ps1
param([int]$StaleMinutes = 5, [int]$PollSeconds = 30)

$Project = "F:\UnityProject\BulletHeavenArt"
$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe"
$EditorLog = Join-Path $env:LOCALAPPDATA "Unity\Editor\Editor.log"
$Dir = Join-Path $Project "ArtSource\Tools"
$Hangs = Join-Path $Dir "hangs"
$Log = Join-Path $Dir "watchdog_log.txt"
$PidFile = Join-Path $Dir "watchdog.pid"
New-Item -ItemType Directory -Force $Hangs | Out-Null

function Note($s) { $line = "[{0:MM-dd HH:mm:ss}] {1}" -f (Get-Date), $s; Add-Content -Path $Log -Value $line -Encoding UTF8 }

# single instance
if (Test-Path $PidFile) {
    $old = Get-Content $PidFile -ErrorAction SilentlyContinue
    if ($old -and (Get-Process -Id $old -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "powershell*" })) { exit }
}
Set-Content -Path $PidFile -Value $PID
Note "watchdog started (pid $PID, stale $StaleMinutes min)"

function ProjectUnity {
    Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -like "*BulletHeavenArt*" -and $_.CommandLine -notlike "*-batchmode*" -and $_.CommandLine -notlike "*AssetImportWorker*" } |
        ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue } |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}

function AnswerRecoveryDialog([int]$procId) {
    $w = New-Object -ComObject WScript.Shell
    for ($i = 0; $i -lt 120; $i++) {
        Start-Sleep 2
        $p = Get-Process -Id $procId -ErrorAction SilentlyContinue
        if (-not $p) { return }
        if ($p.MainWindowTitle -like "*Recovering Scene Backups*") {
            $w.AppActivate($procId) | Out-Null; Start-Sleep -Milliseconds 500; $w.SendKeys("{ENTER}")   # Yes: keep backups in Assets/_Recovery
            Note "answered the scene-backup dialog (Yes, kept in Assets/_Recovery)"; return
        }
        if ($p.MainWindowTitle -like "*BulletHeavenArt*") { return }
    }
}

while ($true) {
    try {
        $u = ProjectUnity
        if ($u) {
            $u.Refresh()
            # Editor.log is shared by every open Unity editor (another project writing keeps it fresh), so the hang test
            # is the editor's own "Hold on (busy for m:ss)" title: busy for StaleMinutes or more = hung. A window that
            # stops responding without that title counts from when it was first seen not responding.
            $age = 0
            if ($u.MainWindowTitle -match "busy for (?:(\d+):)?(\d+):(\d+)") { $age = [int]$Matches[1] * 60 + [int]$Matches[2] + [int]$Matches[3] / 60.0 }
            elseif (-not $u.Responding) { if (-not $script:nrSince) { $script:nrSince = Get-Date }; $age = ((Get-Date) - $script:nrSince).TotalMinutes }
            else { $script:nrSince = $null }
            if ($age -ge $StaleMinutes) {
                $stamp = Get-Date -Format "MMdd_HHmmss"
                Note ("HUNG: pid {0} '{1}', Editor.log quiet {2:0.0} min -> restarting" -f $u.Id, $u.MainWindowTitle, $age)
                Get-Content $EditorLog -Tail 300 | Set-Content (Join-Path $Hangs "hang_$stamp.log") -Encoding UTF8
                Get-ChildItem (Join-Path $Project "ArtSource") -Recurse -Filter "*_cmd.txt" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notlike "$Hangs*" } | ForEach-Object {
                    $dest = Join-Path $Hangs ("{0}_{1}" -f $stamp, $_.Name)
                    Move-Item $_.FullName $dest -Force
                    Note ("  parked pending command {0}: {1}" -f $_.FullName, (Get-Content $dest -Raw).Trim())
                }
                Stop-Process -Id $u.Id -Force
                Start-Sleep 8
                Remove-Item (Join-Path $Project "Temp\UnityLockfile") -Force -ErrorAction SilentlyContinue
                # the scene-backup dialog cannot be answered from this hidden process (no foreground rights): keep the
                # backups here instead and remove them, so Unity starts without asking
                $bk = Join-Path $Project "Temp\__Backupscenes"
                if (Test-Path $bk) { Copy-Item $bk (Join-Path $Hangs "$($stamp)_Backupscenes") -Recurse -Force; Remove-Item $bk -Recurse -Force; Note "  scene backups kept in hangs\$($stamp)_Backupscenes" }
                $np = Start-Process $UnityExe -ArgumentList "-projectPath `"$Project`"" -PassThru
                Note "  started Unity pid $($np.Id)"
                AnswerRecoveryDialog $np.Id
            }
        }
    } catch { Note ("error: " + $_.Exception.Message) }
    Start-Sleep $PollSeconds
}
