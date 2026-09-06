<#
  Verify OmenMon's GPU temperature (EC sensor GPTM) against the GPU's own die
  sensor, under load.

  The CPU sensor on this board looked plausible at idle and turned out to be
  ASCII string data; GPTM has never been checked the same way. It matters more
  than the CPU one, because the fan curve runs on max(CPU, GPU) and the 4070
  draws up to 140 W - a GPU temperature that cannot rise means fans that never
  spin up while the GPU cooks.

  Loads the GPU with a WebGL fragment shader in Edge, samples nvidia-smi, and
  writes a CSV to compare against OmenMon-telemetry.csv afterwards.

  Aborts on ABORT_C. Needs no elevation - nvidia-smi and Edge both run as the user.
#>

param(
    [int] $Seconds  = 420,
    [int] $Interval = 5,
    [int] $AbortC   = 85
)

$here   = Split-Path -Parent $PSCommandPath
$page   = Join-Path $here 'gpuload.html'
$outCsv = Join-Path $here 'gputest-nvidia.csv'

if (-not (Test-Path $page)) { Write-Host "missing $page" -ForegroundColor Red; return }

function Read-Gpu {
    $line = & nvidia-smi --query-gpu=temperature.gpu,power.draw,utilization.gpu,clocks.sm `
                         --format=csv,noheader,nounits 2>$null
    if (-not $line) { return $null }
    $f = ($line -split ',') | ForEach-Object { $_.Trim() }
    [pscustomobject]@{ TempC = [int]$f[0]; Watt = [double]$f[1]; Util = [int]$f[2]; Mhz = [int]$f[3] }
}

$b = Read-Gpu
if (-not $b) { Write-Host "nvidia-smi returned nothing" -ForegroundColor Red; return }
Write-Host ("baseline  {0} C  {1} W  {2}% util" -f $b.TempC, $b.Watt, $b.Util) -ForegroundColor Cyan

# --force_high_performance_gpu matters on a hybrid-graphics laptop: without it
# Edge may render on the iGPU and the dGPU never sees the load at all.
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edge)) { $edge = "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe" }
if (-not (Test-Path $edge)) { Write-Host "Edge not found" -ForegroundColor Red; return }

$profile = Join-Path $here 'edge-profile'
$args = @(
    "--user-data-dir=`"$profile`"",
    '--no-first-run', '--no-default-browser-check',
    '--force_high_performance_gpu',
    '--disable-frame-rate-limit',
    "--app=file:///$($page -replace '\\','/')"
)
$proc = Start-Process $edge -ArgumentList $args -PassThru
Write-Host "load started (Edge pid $($proc.Id)) - close nothing, this script cleans up" -ForegroundColor Yellow

"time,elapsed_s,gpu_die_c,watt,util_pct,mhz" | Set-Content $outCsv -Encoding UTF8

$t0 = Get-Date
$peak = 0; $peakW = 0; $maxUtil = 0
$aborted = $false
try {
    while (((Get-Date) - $t0).TotalSeconds -lt $Seconds) {
        Start-Sleep -Seconds $Interval
        $g = Read-Gpu
        if (-not $g) { continue }
        $el = [int]((Get-Date) - $t0).TotalSeconds
        "{0},{1},{2},{3},{4},{5}" -f (Get-Date -f 'yyyy-MM-dd HH:mm:ss'), $el, $g.TempC, $g.Watt, $g.Util, $g.Mhz |
            Add-Content $outCsv -Encoding UTF8
        if ($g.TempC -gt $peak)  { $peak = $g.TempC }
        if ($g.Watt  -gt $peakW) { $peakW = $g.Watt }
        if ($g.Util  -gt $maxUtil) { $maxUtil = $g.Util }
        Write-Host ("{0,4}s  die {1,3} C   {2,6:N1} W   util {3,3}%   {4} MHz" -f $el, $g.TempC, $g.Watt, $g.Util, $g.Mhz)
        if ($g.TempC -ge $AbortC) {
            Write-Host "ABORT: $($g.TempC) C >= $AbortC" -ForegroundColor Red
            $aborted = $true; break
        }
    }
} finally {
    try { Stop-Process -Id $proc.Id -Force -EA SilentlyContinue } catch {}
    Get-Process msedge -EA SilentlyContinue |
        Where-Object { $_.Path -like '*Edge*' } |
        Where-Object { $_.StartTime -gt $t0 } |
        Stop-Process -Force -EA SilentlyContinue
}

Write-Host ""
Write-Host ("load stopped{0}   peak die {1} C   peak {2} W   max util {3}%" -f `
    $(if ($aborted) { ' (aborted)' } else { '' }), $peak, $peakW, $maxUtil) -ForegroundColor Green
Write-Host "samples: $outCsv"
Start-Sleep -Seconds 20
$c = Read-Gpu
Write-Host ("cooldown  {0} C  {1} W" -f $c.TempC, $c.Watt)
