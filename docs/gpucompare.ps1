<#
  Correlate OmenMon's GPU sensor (GPTM, EC 0xB7) against the GPU's own die sensor
  over the load window, and say plainly whether GPTM tracks it.

  The test is not "are the numbers close" - an EC sensor can legitimately sit a few
  degrees off the die because it measures a different point on the board. The test
  is whether it MOVES: a sensor that cannot rise under a 100 W load is not a sensor,
  and the fan curve keys on it.
#>

$here  = Split-Path -Parent $PSCommandPath
$nv    = Join-Path $here 'gputest-nvidia.csv'
$tele  = 'C:\Program Files\OmenMon Reborn 8BCA\OmenMon-telemetry.csv'

if (-not (Test-Path $nv))   { Write-Host "no $nv - run gputest.ps1 first" -ForegroundColor Red; return }
if (-not (Test-Path $tele)) { Write-Host "no telemetry at $tele" -ForegroundColor Red; return }

$n = Import-Csv $nv
if ($n.Count -lt 2) { Write-Host "only $($n.Count) nvidia samples" -ForegroundColor Red; return }

$t0 = [datetime]::ParseExact($n[0].time, 'yyyy-MM-dd HH:mm:ss', $null).AddMinutes(-2)
$t1 = [datetime]::ParseExact($n[-1].time, 'yyyy-MM-dd HH:mm:ss', $null).AddMinutes(2)

$rows = Import-Csv $tele | Where-Object {
    $d = $null
    [datetime]::TryParseExact($_.time, 'yyyy-MM-dd HH:mm:ss', $null, 'None', [ref]$d) | Out-Null
    $d -ge $t0 -and $d -le $t1
}

Write-Host ""
Write-Host "=== GPU die (nvidia-smi) ===" -ForegroundColor Cyan
$dieMin = ($n | Measure-Object -Property gpu_die_c -Minimum).Minimum
$dieMax = ($n | Measure-Object -Property gpu_die_c -Maximum).Maximum
$wMax   = ($n | Measure-Object -Property watt -Maximum).Maximum
$uMax   = ($n | Measure-Object -Property util_pct -Maximum).Maximum
"  min {0} C   max {1} C   rise {2} C   peak {3} W   max util {4}%" -f $dieMin, $dieMax, ($dieMax - $dieMin), $wMax, $uMax

Write-Host ""
Write-Host "=== OmenMon GPTM (EC) over the same window ===" -ForegroundColor Cyan
if ($rows.Count -eq 0) { Write-Host "  no telemetry rows in the window" -ForegroundColor Red; return }
$ecMin = ($rows | Measure-Object -Property gpu_c -Minimum).Minimum
$ecMax = ($rows | Measure-Object -Property gpu_c -Maximum).Maximum
"  min {0} C   max {1} C   rise {2} C   ({3} samples)" -f $ecMin, $ecMax, ($ecMax - $ecMin), $rows.Count

Write-Host ""
Write-Host "=== paired samples ===" -ForegroundColor Cyan
"  {0,-20} {1,>8} {2,>8} {3,>8}" -f 'time', 'die C', 'GPTM C', 'delta'
foreach ($r in $rows) {
    $rt = [datetime]::ParseExact($r.time, 'yyyy-MM-dd HH:mm:ss', $null)
    $near = $n | Sort-Object { [Math]::Abs(([datetime]::ParseExact($_.time,'yyyy-MM-dd HH:mm:ss',$null) - $rt).TotalSeconds) } |
            Select-Object -First 1
    "  {0,-20} {1,8} {2,8} {3,8}" -f $r.time, $near.gpu_die_c, $r.gpu_c, ([int]$r.gpu_c - [int]$near.gpu_die_c)
}

Write-Host ""
$dieRise = $dieMax - $dieMin
$ecRise  = $ecMax - $ecMin
if ($dieRise -lt 8) {
    Write-Host "INCONCLUSIVE: the die itself only moved $dieRise C - the load was too light to test with." -ForegroundColor Yellow
} elseif ($ecRise -ge ($dieRise * 0.5)) {
    Write-Host "GPTM TRACKS: die rose $dieRise C, GPTM rose $ecRise C. The sensor is real." -ForegroundColor Green
} elseif ($ecRise -ge 3) {
    Write-Host "GPTM IS DAMPED: die rose $dieRise C but GPTM only $ecRise C." -ForegroundColor Yellow
    Write-Host "It responds, but understates the load - the fan curve will react late." -ForegroundColor Yellow
} else {
    Write-Host "GPTM IS DEAD: die rose $dieRise C, GPTM moved $ecRise C." -ForegroundColor Red
    Write-Host "The fan curve runs on max(CPU, GPU) - it cannot see the GPU heating." -ForegroundColor Red
}
