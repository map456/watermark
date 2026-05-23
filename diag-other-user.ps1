# Run this as the user that can't see the watermark.
# Usage:  powershell -ExecutionPolicy Bypass -File diag-other-user.ps1

$line = '=' * 60
Write-Output $line
Write-Output ("Watermark diagnostic - " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Write-Output $line

Write-Output ''
Write-Output '[1] Current identity'
Write-Output ("  Username    : " + $env:USERNAME)
Write-Output ("  Domain      : " + $env:USERDOMAIN)
Write-Output ("  Computer    : " + $env:COMPUTERNAME)
Write-Output ("  SessionName : " + $env:SESSIONNAME)
Write-Output ("  SessionId   : " + (Get-Process -Id $PID).SessionId)

Write-Output ''
Write-Output '[2] Is this an RDP session?'
Add-Type 'using System.Runtime.InteropServices; public class M { [DllImport("user32.dll")] public static extern int GetSystemMetrics(int n); }' -ErrorAction SilentlyContinue
$isRdp = [M]::GetSystemMetrics(0x1000)
$rdpText = if ($isRdp) { 'YES (RDP session)' } else { 'NO  (local console - watermark hidden by design)' }
Write-Output ("  SM_REMOTESESSION = $isRdp  --> $rdpText")

Write-Output ''
Write-Output '[3] Files installed?'
$exePath = 'C:\Program Files\Watermark\Watermark.exe'
$cfgPath = 'C:\Program Files\Watermark\settings.json'
Write-Output ("  $exePath  ->  " + (Test-Path $exePath))
Write-Output ("  $cfgPath  ->  " + (Test-Path $cfgPath))

Write-Output ''
Write-Output '[4] HKLM Run key value'
$runVal = (Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue).Watermark
if ($runVal) { Write-Output ("  HKLM\...\Run\Watermark = $runVal") }
else         { Write-Output '  (NOT SET - this is the problem)' }

Write-Output ''
Write-Output '[5] Watermark process running?'
$procs = Get-Process Watermark -ErrorAction SilentlyContinue
if ($procs) {
    $procs | Select-Object Id, StartTime, @{N='WS_MB';E={[math]::Round($_.WorkingSet64/1MB,1)}}, SessionId | Format-Table -AutoSize
} else {
    Write-Output '  (no Watermark.exe running)'
}

Write-Output ''
Write-Output '[6] Log file (last 30 lines)'
$log = "$env:LOCALAPPDATA\Watermark\watermark.log"
if (Test-Path $log) { Get-Content $log -Tail 30 } else { Write-Output '  (no log file)' }

Write-Output ''
Write-Output '[7] Try manual launch, observe for 5 seconds'
Get-Process Watermark -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
if (Test-Path $exePath) {
    try {
        $p = Start-Process -FilePath $exePath -PassThru -ErrorAction Stop
        for ($i = 0; $i -lt 10; $i++) {
            Start-Sleep -Milliseconds 500
            $cur = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
            if ($cur) {
                Write-Output ("  [t={0,4}s] alive  WS={1}MB  SessionId={2}" -f ($i*0.5), [math]::Round($cur.WorkingSet64/1MB,1), $cur.SessionId)
            } else {
                Write-Output ("  [t={0,4}s] DEAD" -f ($i*0.5))
                break
            }
        }
        if (Test-Path $log) {
            Write-Output ''
            Write-Output '  -- log after manual launch --'
            Get-Content $log -Tail 15
        }
    } catch {
        Write-Output ("  Start-Process FAILED: " + $_.Exception.Message)
    }
} else {
    Write-Output '  skipped (exe not found)'
}

Write-Output ''
Write-Output $line
Write-Output 'Done. Please paste the full output back.'
