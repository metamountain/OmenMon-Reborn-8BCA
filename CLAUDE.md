# CLAUDE.md — OmenMon Reborn 8BCA

Notes for an AI coding assistant working on this fork. Written to be acted on: what is
established, what was disproven and how, and what is still open.

If you are adapting this to a *different* HP laptop, read "How to investigate a new
board" at the bottom first. The register numbers here are specific to one machine;
the method is the transferable part.

## This machine

| | |
|---|---|
| Model | HP OMEN Gaming Laptop **16-xf0079ng** (product **84S07EA**) |
| Board / ProductId | **`8BCA`** |
| CPU | AMD Ryzen 7 7840HS (Zen 4, Phoenix, family 19h, 8C/16T, 54 W) |
| BIOS | **F.32** (was F.31; the fan findings hold across both) |
| OS | Windows 11 Home 26100 |
| Install | `C:\Program Files\OmenMon Reborn 8BCA` |
| Source | `C:\Users\omen\src\OmenMon-Reborn` |

## Established facts

### Fan control works through WMI. Do not switch it to the EC.

`OMENMON_BIOSTRACE=1` (see `Hardware/Bios.cs`) logs every `Send()`. All fan commands
return `rc=0`:

```
cmd=0x20008 type=0x2E in=[37 37 00 00] rc=0
```

Command types in use: `0x1A` SetFanMode, `0x2D` GetFanLevel, `0x2E` SetFanLevel,
`0x2F` GetFanTable, `0x27` SetMaxFan, `0x23` GetTemperature, `0x29` SetCpuPower,
`0x28` GetSystem. Channel is `Cmd.Default = 0x20008` (HPWMI_GM).

Verified acoustically: a level sweep measured −45 → −40 → −37 dBFS on the built-in
microphone.

### Fan RPM comes from the level, not from a tachometer

`Library/AutoCal.cs`:

```csharp
["8BCA"] = new Mapping {
    CpuReg = 0, CpuMode = EcDiffScanner.Mode.BiosLevelMirror, CpuMul = 100,
    GpuReg = 0, GpuMode = EcDiffScanner.Mode.BiosLevelMirror, GpuMul = 100,
},
```

EC `0xB0`/`0xB2` read `0x005C`/`0x0000` and never track load — they are not a
tachometer on this board. Fan levels are in units of 100 RPM, so level 40 = 4000 RPM.
Corroborated by arfelious/omen-fan-control issue #14 for the same board.

`AutoCal.Load()` is deliberately neutered: it deletes any sidecar and returns false.
Auto-calibration produced confidently wrong mappings here, and a persisted wrong
mapping is worse than none.

### CPU temperature comes from the die over the SMN

`Driver/PawnIoAmd.cs`. Loads a **second** PawnIO module, `AMDFamily17.bin`, whose
`ioctl_read_smn` (1 ulong in, 1 out) reads `THM_TCON_CUR_TMP` at SMN `0x00059800`.

```
temperature = (raw >> 21) * 0.125
if (raw & 0x80000) or ((raw & 0x30000) == 0x30000):  temperature -= 49
```

Verified: `raw 0x530B0000 -> 34.00 °C`, `raw 0x4E4B0000 -> 29.25 °C` against Core
Temp's 28 °C. Raw falls `0x04C00000` per 4.75 °C = `2^24` per degree, confirming the
scale. `RANGE_SEL` is set on this CPU, so the −49 shift applies.

Median-of-3 smoothing is applied in `GuiMonitor.Sample()` before the value reaches
`MaxTemp`: Zen boosts on any brief foreground task and the raw reading spikes ~30 °C
for one sample at idle, which would make the fan curves chase transients.

The module is embedded as `OmenMon.AMDFamily17.bin` and also accepted side-by-side.
It is byte-identical to PawnIO.Modules release 0.2.11, the same release the existing
`LpcACPIEC.bin` came from.

### The ~4 °C gap to Core Temp is a different sensor, not an error in our decode

Settled with Core Temp's own `SMN Register dump`, 2026-09-07 11:39. It dumps
`0x00059800` as `00 00 CB 53`, i.e. **`0x53CB0000`**:

```
raw >> 21 = 670   ->  670 x 0.125 = 83.75 C
bit 19 (RANGE_SEL) = 1, bits 16-17 (TJ_SEL) = 1,1   ->  -49
                                              =  34.75 C
```

The telemetry rows at `11:39:09` and `11:39:42` both read **34**. So our decode of
`THM_TCON_CUR_TMP` is correct to well inside a degree, and the median-of-3 costs
0.75 °C at steady state — not 4. **Neither smoothing nor the decode explains the gap.**

**Disproved: "Core Temp shows the hottest core, we show the package."** Phoenix is
monolithic and has no per-CCD temperature registers — `0x00059940`-`0x00059950` are all
zero in the dump, where LibreHardwareMonitor expects `F17H_M70H_CCD_TEMP` at
`0x00059954`. There is no hotter per-core sensor for it to be reading.

What is left is that Core Temp reads a *different* sensor. Its SMU power-table dump has
limit/value pairs, and among the values are 33.72, 31.75, 34.44 (limits 100 °C) and
33.16, **37.89** (limits 80 °C) against our 34.75. The 37.89 is about the reported gap.
Treat the pairing as inferred, not documented — but the conclusion above does not rest
on it, only on the register.

**Keep using Tctl for the fan curves.** It is the value AMD's own firmware regulates
against, and the platform's thermal limits are defined in the same terms. A number that
reads a few degrees higher is not a better one to control on.

### GPU temperature comes from NVML, not from the EC

`Driver/Nvml.cs`. `nvml.dll` ships with the NVIDIA driver, lives in `System32`, is what
`nvidia-smi` itself calls, and needs neither a kernel driver nor elevation.
`nvmlDeviceGetTemperature(dev, NVML_TEMPERATURE_GPU)` gives the die temperature.

`GPTM` (EC `0xB7`) does **not** track this GPU. Measured with an RTX 4070 Laptop held at
100% and ~79 W:

| die | GPTM | delta |
|---|---|---|
| 51 °C | 26 °C | −25 |
| 65 °C | 28 °C | −37 |
| 75 °C | 33 °C | −42 |

Die rose 42 °C (34 → 76); `GPTM` moved 7 °C, and the gap *widens* with heat, so it is
not an offset that could be corrected. Unlike `CPUT` it is not string data — it is a
real sensor, just not one that measures anything useful for fan control.

Fan programs key on `max(CPU, GPU)`. At the 65 °C sample that maximum was 48 °C, below
the Silent profile's 52 °C threshold, so **the fans stayed at the 1700 rpm floor with
the GPU at 65 °C on 79 W**. The CPU only rescued it because a browser was generating the
load; a game would not have.

Not median-filtered, unlike the CPU path: NVML is already stable and any smoothing would
delay a rise, which is the wrong way to err here.

### Everything else CPU-temperature-shaped on this board is a string

**This is the most important fact in this file.** `8BCA` has firmware string data
where the canonical EC layout has registers. Confirmed at `0x57` (CPUT), `0xB0`/`0xB2`
(tachometer), `0x95` (mode; reads `0x43` = `'C'`), and the WMI `GetTemperature`
(`0x23`) call.

Every CPU temperature ever reported from those sources was ASCII punctuation:
`33=0x21 '!'`, `42=0x2A '*'`, `43=0x2B '+'`, `44=0x2C ','`, `45=0x2D '-'`,
`54=0x36 '6'`.

`ModeReg = 0x59` (from Linux `omen_v1_thermal_params`) reads a constant 0 — also
wrong.

`CPUT` is therefore set `Use="false"` in `OmenMon.xml`, and `GuiMonitor` honours the
per-sensor `Use` flag (it previously did not, so a disabled sensor still fed
`CpuTemp`).

### The EC lock contender is real, and the log used to hide it

**Corrects an earlier note here**, which read "the EC lock is usually contended by
OmenMon itself" on the strength of a timeout that logged `other EC users: none
detected`. That was a reporting bug: `EcContenderNames` matched process names
*exactly*, and `OmenCap` and `HPSystemEventUtilityBackground` were running throughout.
Matching is now by substring, and the log names them.

The self-contention is real too and still worth knowing: the monitor thread holds
`Global\Access_EC` for a whole `EcExecBatch` pass, and `Hardware/Ec.cs`'s read backoff
`Wait()`s while holding it. `Hw.EcRequest()` now retries across `EcMutexTotalTimeout`
(2500 ms) instead of making a single `EcMutexTimeout` attempt.

### The CPU die read has its own contender: any other monitoring tool

`PawnIoAmd` reads `THM_TCON_CUR_TMP` through the cross-process
`\BaseNamedObjects\Access_PCI` mutex. Core Temp, HWiNFO and Ryzen Master poll the same
register through the same mutex. Root cause of a session where the die reading died and
the curve silently fell back to `CPUT`: **Core Temp was running**, and the tick rate had
gone from 15 s to 2 s with two SMN reads per tick, making collisions likely. There was
no retry, so the first collision was permanent. `PawnIoAmd` now retries and reopens the
module after three consecutive failures (30 s cooldown). Close other monitors before
drawing conclusions from a die-read failure.

### Fan programs have no hysteresis, and tick every `UpdateProgramInterval` seconds

`FanProgram.GetTemperatureLevel()` is a plain binary search over the threshold list.
There is no dead band. A temperature sitting on a threshold therefore flips the fan
between two levels every tick — **2 s** in this config, down from the 15 s default,
which was far too slow to catch a rise.

This is the second reason curves need dense points, and the less obvious one. With
14 C gaps between rows the oscillation is a ~1000 rpm pump; at 2 C spacing it is 200 rpm
and inaudible. Densifying is not only about smooth response - it is what makes the
missing hysteresis stop mattering.
### Silent caps the GPU at 80 W

Each fan program carries a `<GpuPower>` element. Silent and "My profile" set `Minimum`,
Default and Performance set `Maximum`. Confirmed from the driver side:

```
power.default_limit    80 W     <- what Silent leaves in force
enforced.power.limit  140 W     <- after switching to Performance
power.max_limit       140 W
```

So the profile does not merely change fan behaviour: **Silent halves the GPU power
budget**, and nothing in the UI says so. It also means Silent's worst case is much
milder than Performance's - its curve only ever has to cope with an 80 W GPU.

### Measured thermal anchors (GPU at 80 W)

| fan | steady GPU temperature |
|---|---|
| 1700 rpm (floor) | 76 C, still climbing when stopped |
| 3800 rpm | ~68 C, stable |

Two points, but real ones - enough to choose a target temperature instead of guessing
at rpm. A proper characterisation (hold 80 W, step the fan, record steady state) would
turn curve design into arithmetic; every curve number written so far has been an
educated guess, and two of those guesses were wrong.

### Fan response, verified end to end

With the sensors fixed and the Performance profile active:

```
 5 s   gpu 68 C -> gpu fan 3800 rpm    cpu 45 C -> cpu fan 2000 rpm
10 s   cpu 66 C -> cpu fan 3700 rpm    gpu 49 C -> gpu fan 2500 rpm
```

Each fan follows its own component, independently and in opposite directions at the
same moment, and both step back down on cooldown (3500 -> 2500 -> 2000). This is what
`FanProgram` intends and what was broken while the sensor overrides sat in the GUI
layer.

### Generating GPU load without installing anything

Edge with WebGPU compute reaches 95-98% utilisation and ~80 W. Notes that cost time:

- **WebGL alone tops out near half that**, because rendering is tied to frame
  presentation - the GPU idles between presents. Compute has no such ceiling.
- **Chromium throttles `requestAnimationFrame` when a window is occluded**, which
  killed a load mid-test (47 W at 25 s, 15 W and 0% by 56 s, Edge still running).
  Needs `--disable-backgrounding-occluded-windows`,
  `--disable-background-timer-throttling`, `--disable-renderer-backgrounding`,
  `--disable-features=CalculateNativeWinOcclusion`, plus `--disable-gpu-vsync`.
- **Do not run WebGPU compute and WebGL rendering together.** It reset the GPU driver
  (TDR) twice at the same ~15 s mark at two very different load sizes, so it is the
  combination, not the magnitude. Compute alone sustained 98% for over a minute.
- ~80 W is the honest ceiling for a browser load on a card allowed 140 W, with
  `SW Power Cap: Not Active`. A browser cannot produce the mixed ALU/texture/ROP/memory
  load a 3D engine does. Reaching the real budget needs a game or a dedicated burner.

## Disproven — do not resurrect

These were believed, acted on, and are wrong. They are recorded because the sources
that support them are still online and still persuasive.

| Claim | Source | How it was disproven |
|---|---|---|
| HP's WMI thermal interface is firmware-broken on `8BCA`, so fan control must use direct EC writes | LKML "hp-wmi: Fan control broken on HP OMEN 16-xf0xxx (board 8BCA, BIOS F.31)"; HP community "broken ACPI tables" | BIOS trace shows `rc=0` on every fan call; microphone confirms the fans respond. Switching to EC control made fans inaudible and the UI laggy. **Reverted.** The reports describe Linux, not the Windows driver stack. |
| Fan setpoints live at EC `0x11` (CPU) / `0x14` (GPU) | Inferred from calibration-report EC dumps: those bytes tracked the load sweep | They correlate because they *mirror* the level the WMI path set, not because writing them controls anything. The WMI path was already working. |
| `FanLevelReg1` should be `0x14` instead of `0x12` | Same inference | Moot — the EC fan-level path is not used. |
| CPU temperature was fixed by disabling `CPUT` | 6 idle samples reading 44-45 against a die of 41 | Burn-in disproved it: 44 °C reported while the die was at 66.8, and 33 while the die was at 48.5. **Idle agreement is not agreement.** |
| `GPTM` (EC `0xB7`) is a valid GPU sensor | It moved a little, the idle value looked right, and the fan curves appeared to work | A 79 W GPU load: die 34 → 76 °C, `GPTM` 26 → 33 °C, gap widening from 25 to 42 °C. Same trap as the row above, found only because the user asked whether the GPU had ever been checked. It had not. |

## Traps that cost real time

### The fan curves read Platform, not the GUI snapshot

`FanProgram.Update()` calls `Platform.GetCpuTemperature()` and
`GetGpuTemperature()` directly. Both sensor overrides were first written into
`GuiMonitor.Sample()`, which meant the corrected temperatures reached the window and
`OmenMon-telemetry.csv` while the fan curves, `CheckThermalPanic` and the tray icon all
still ran on the EC sensors that had been measured to be wrong. The symptom was a
telemetry row reading `max 76` with `cpu_lvl 17` - fans at the floor while the log said
76 C.

Both overrides now live in the `Platform` accessors, and are folded into
`GetMaxTemperature()`. `GuiMonitor` reads the same accessors, so display and fans cannot
diverge again. **Any new sensor source belongs in `Platform`, not in the monitor.**

### NVML goes stale when the dGPU powers down — detect it, then rebuild the session

Hybrid graphics: when nothing needs the discrete GPU, rendering moves to the AMD
integrated one and the dGPU powers off. NVML then keeps returning the last temperature
it recorded before that - with `NVML_SUCCESS`, indefinitely. Seen as a frozen 76 C (the
peak of a load test that had ended minutes earlier) while the die was at 38 C. Running
`nvidia-smi` wakes the GPU and yields one correct reading before it freezes again, which
is what made this hard to pin down - every attempt to measure it destroyed the state
being measured.

Measured properly (10 s load, 60 s of total NVML silence, one read, then `nvidia-smi`
last as ground truth): **4 of 5 cycles froze**, errors +18 to +27 C, and **none of the
four recovered** across 30 s of continuous polling (150 reads).

**The detector is the return code of `nvmlDeviceGetUtilizationRates`, which is 999
(`NVML_ERROR_UNKNOWN`) in the stale state.** Nothing else reports the condition:

| field | stale session | truth | `hr` |
|---|---|---|---|
| temperature | 60 C | 41 C | **0** |
| utilisation | 0 % | 0 % | **999** |
| power | 590 W | 12.3 W | 0 |
| pstate | P0 | P0 | 0 |
| SM clock | 1320 MHz | 1980 MHz | 0 |

Note it is the *return code*, not the utilisation value: a parked GPU and a stale
session both read 0 %.

**The recovery is `nvmlShutdown()` + `nvmlInit_v2()` in-process.** Confirmed twice,
immediately and exactly (41 vs 41, 44 vs 44). No external process is needed. An existing
session cannot wake the GPU however hard it polls, which is why `nvidia-smi` appeared to
"fix" it — a *fresh* session is the whole mechanism.

`Nvml` does both on every read, rate-limited to one rebuild per 10 s
(`ReinitCooldownSec`) so a permanent fault cannot become a re-init loop. Rebuilds are
logged as `Nvml.Stale`.

**Confirmed in the field, with Task Manager as the independent witness.** 2026-09-07
11:51:57, one moment, three readers of the same silicon:

| source | reads | |
|---|---|---|
| Windows Task Manager | **57 °C** | frozen, no mitigation |
| OmenMon | **41 °C** | rebuilds the session |
| `nvidia-smi`, fresh session | **41 °C** | ground truth |

Task Manager was **+16 °C** wrong and stayed there. This is worth keeping for two
reasons. It shows the fix working against a control that does not have it — the same
device, the same instant, one reader stale and one correct. And it settles where the
stale value lives: **in the session, not in the sensor.** Each consumer goes stale on
its own terms, which is why a fresh session is a complete fix and why "even Task Manager
is wrong" is evidence *for* the rebuild rather than against it.

**Disproves a caution raised while investigating this**, that an independently-wrong
Task Manager would mean the reading was poisoned below NVML and no session-level fix
could help. The opposite is true, and the table above is why. Do not weaken the rebuild
on that reasoning.

An earlier sighting in the same session — Task Manager dropping 68 → 57 "in a ms" —
looked like a stale value snapping to truth but was not: it coincided exactly with the
end of an 81 W load burst, and a small die with the fan already spun up genuinely sheds
that much in a second or two. The freeze signature is the opposite shape, a value that
*refuses* to fall while the machine demonstrably cools. Compare against a ground-truth
read before calling a fast drop a recovery.

**Corrects an earlier note here.** The previous guard rejected a value repeated 120
times running unless it was at or above 60 C. It could never fire: the stale values
*are* the last ones seen under load, i.e. 60-69 C, above its own exception. The premise
was also wrong — a frozen reading is not detectable from the temperature at all, since a
genuinely pinned GPU produces an identical sequence. Do not reintroduce a repeat-count
heuristic here.

### Untrusted-sensor fallbacks differ by component, deliberately

Both sensors can stop being trustworthy, and the right response is opposite in each case.

**CPU — hand the fans back to the firmware.** `Platform.IsCpuTemperatureTrusted` is
false when the die read fails and `GetCpuTemperature` would otherwise fall through to
`CPUT` (EC `0x57`), which is *string data* and cannot rise. After
`UntrustedTicksBeforeHandback` (5) ticks `FanProgram` stops calling `UpdateCountdown()`;
the EC watchdog at `0x63` then expires and the BIOS resumes fan control in ~120 s.
Precedent: `thinkpad-acpi`'s fan watchdog, Framework's `autofan`, NBFC's porting guide.
**But note the cost**: the firmware curve let the CPU reach 94 C under sustained
all-core load in testing. This is a safe failure mode, not a good operating mode — the
retry in `PawnIoAmd` matters more than the hand-back does.

**GPU — hold the value and ease the fan down.** Handing back is wrong here, and so is
falling through to `GPTM`, which reads ~28 C under a 79 W load: taking it would look
like the GPU had gone cold and would drop the GPU fan at the exact moment the reading
was lost. Instead `Platform` keeps `LastGpuTemperature` and clears
`IsGpuTemperatureTrusted`; `FanProgram` then eases the GPU fan down `GpuUntrustedRampStep`
(1 level = 100 rpm) per tick to `GpuUntrustedFloor` (25 = 2500 rpm) and holds there. At
the 2 s tick that is ~40 s from a 4500 rpm peak: the GPU finishes cooling on the way
down, and a permanently dead sensor still leaves the card ventilated. `nvmlWasSource`
latches on the first good NVML read so a machine that never had a die sensor keeps the
old GPTM behaviour.

### A dropped EC write is silent, and that is how the watchdog lapsed

`Hw.EcExec` runs its callback only if it can take `Global\Access_EC` within
`EcMutexTotalTimeout`. If it cannot, the callback never runs — and the write overload
returns `void`. A dropped read is a missing sample; a dropped write to the countdown at
`0x63` is a loss of fan control about `FanCountdownExtendInterval` later, reported
nowhere.

It got worse than that. `UpdateCountdown()` was guarded on `FanProgramModeCheckFirst`,
which is **false** in the shipped config, so it never ran at all: the only thing feeding
the watchdog was the incidental countdown reset inside `UpdateFanMode`'s forced write.
One dropped write and nothing was feeding it. The call is now first in `Update()`,
unconditional, and checks `Hw.EcLockTimeoutCount` across the write to see whether it
landed, retrying `CountdownWriteAttempts` times and logging when it does not.

**The lapse is real, but most `countdown == 0` rows in the telemetry are not it.** In a
failed EC batch every EC-derived column reads 0 together — `cpu_lvl`, `gpu_lvl`,
`cpu_pct`, `gpu_pct` and `countdown` — while `cpu_rpm` shows the fans still doing what
the curve asked. 145 of 1895 rows read `countdown == 0`; nearly all are that. The one
genuine lapse is distinguishable because **the fan level itself collapses to something
the curve would never choose**: at 21:29:29, `cpu_lvl` 44 → 15 (4400 → 1500 rpm) with
the CPU at 90 °C, then 94 °C, recovering to level 45 at 21:29:44 as the countdown read
120 again. Read the level column, not the countdown column.

## Still open

- **Monitor thread starves under sustained full load** — 2 telemetry samples in 15
  minutes observed. Thread priority is now `AboveNormal` and `TelemetryEvery` is 8
  (was 30). Measured after both: at 2026-09-06 21:22 the telemetry interval was still
  44-47 s against a nominal 8, so the loop was running about 5x slow. That is survivable
  now that the countdown no longer depends on the pass finishing, but it still thins
  `CheckThermalPanic`.
- **No independent CPU cross-check is available.** `MSAcpi_ThermalZoneTemperature` is
  access-denied unelevated, and running Core Temp breaks this program's own SMN read
  (see the contention note above). Verification currently depends on one source.
- **`FanLevelMax` is 55 in the config while every profile tops out at 57-58.**
  Unreconciled; find out which one is authoritative before changing either.
- **Config save occasionally fails.** The user saw "failed to save configuration data"
  while the change survived in memory. `Config.Save()` now logs the exception to
  `OmenMon-error.log` and writes via a temp file + `File.Replace`, so the next
  occurrence will name its own cause. Root cause still unknown.
- **The GPU untrusted ramp has not been exercised in the field.** It only runs when the
  NVML session goes stale *and* the rebuild fails, which the rebuild makes rare by
  design. Worth forcing once to confirm the ramp behaves.
- **Tray icon no longer signals hot/cold**, because that signal *was* the colour. If
  it should come back monochrome, invert above the threshold: white diamond, black
  digits.

## Conventions in this fork

- **The assembly name must stay `OmenMon`.** `Config.AppName` derives the settings-XML
  root element `<OmenMon>`, the scheduled-task names and the mutex names from it.
  Renaming it orphans the user's configuration.
- **Display identity goes through `Config.DisplayName`** (`ProductName` + `BoardId`,
  = "OmenMon Reborn 8BCA"). The window title, About box and error report all use it,
  so they cannot drift apart. `BoardId` reads `BaseBoardProduct`, falls back to the
  DMI model, and is overridable with `OMENMON_MODEL`.
- **Errors**: `App.Error()` logs everything to `OmenMon-error.log` and shows
  `GuiFormError`, which is selectable and copyable. Do not add a bare `MessageBox` for
  an error — the whole point is that the user can paste it.
- **Swallowed exceptions**: if you must catch broadly, call `Config.ErrorLog(context,
  e)` before swallowing. A bare `catch {}` is what made the config-save bug
  undiagnosable for hours.
- **Fan programs are step functions.** `GetTemperatureLevel` returns the highest
  threshold ≤ temperature. There is no interpolation.
- **Both temperature sources are outside the EC now**, and both self-disable rather than
  return a wrong number. Before trusting any new sensor, load the machine and check it
  against an independent reading — both of this board's built-in ones passed at idle and
  failed under load.
- Deploy with `C:\Users\omen\omen-deploy.cmd` (self-elevating). The running app is
  elevated, so an unelevated session cannot overwrite the exe.
- **Build with `make build`, and do not trust a bare `msbuild` on PATH.** This machine's
  Build Tools live under *Program Files (x86)*; `make.cmd` used to search only
  `%ProgramFiles%` and fell through to the .NET Framework MSBuild in
  `C:\Windows\Microsoft.NET\Framework64`, which cannot compile `langversion 11` and
  fails with a bewildering `CS1617`. `make.cmd` now asks `vswhere` first. The dotnet
  SDK's MSBuild is no substitute either — it cannot resolve the
  `Microsoft.Management.Infrastructure` GAC reference that `Hardware/Bios.cs` needs.
- **`Tests/OmenMon.Tests` is SDK-style and needs its own restore.** After a clean
  checkout `make build` stops at `NETSDK1004` until
  `dotnet restore Tests\OmenMon.Tests\OmenMon.Tests.csproj` has run once. 93 tests,
  `dotnet test Tests\OmenMon.Tests\OmenMon.Tests.csproj -c Release --no-build`.

## Useful commands

| Command | Purpose |
|---|---|
| `OmenMon.exe -Diag` | Live fan telemetry: active register, mode, multiplier, BIOS fan count |
| `OmenMon.exe -Probe` | WMI + BIOS + EC snapshot as Markdown |
| `OmenMon.exe -Ec` | Live EC monitor |
| `OMENMON_BIOSTRACE=1` | Log every WMI `Send()` with arguments and return code |
| `C:\Users\omen\omen-tdie.cmd` | Read Tctl/Tdie directly, to compare against Core Temp |
| `C:\Users\omen\omen-fantest.cmd` | Fan level sweep |
| `C:\Users\omen\micmeter.ps1` | Microphone dBFS meter, to hear whether fans responded |

## How to investigate a new board

1. **Trace before theorising.** `OMENMON_BIOSTRACE=1` first. A day was lost here to a
   Linux bug report that was accurate about Linux and irrelevant to Windows.
2. **Dump the EC across a load sweep.** Bytes that move with load *and* return to idle
   are candidates. Bytes that stay constant, or that sit in the ASCII printable range
   (`0x20`-`0x7E`), are string data — the giveaway on this board.
3. **Validate under load, never at idle.** Plausible-looking idle numbers are how a
   wrong sensor survives. Compare against Core Temp or HWiNFO *while the machine is
   hot*, and require that the two move together.
4. **Use a microphone.** It answers "did the fan actually change?" independently of
   any register. Disable the mic's noise suppression first — Windows audio
   enhancements cancel steady fan noise and pin the reading at the noise floor.
5. **If the EC has nothing usable, go around it.** The CPU, GPU and PCH publish their
   own sensors. `Resources/PAWN_BUILD.md` lists which signed PawnIO module covers
   MSR, SMN, PCI config and LPC. Adding one is ~150 lines
   (`Driver/PawnIoAmd.cs` is the worked example) and keeps the driver
   Microsoft-signed.
6. **Write down what you disproved**, not just what you concluded. The wrong theories
   here were all supported by real, still-reachable sources; without the disproof
   recorded, the next person re-derives them.

## Reference

- LKML bug report (describes Linux, not Windows): https://lkml.iu.edu/2602.3/05799.html
- HP community thread: https://h30434.www3.hp.com/t5/Gaming-Notebooks/Fan-control-completely-non-functional-on-Linux-due-to-broken/td-p/9632498
- HP OMEN EC map: https://github.com/alou-S/omen-fan/blob/main/docs/probes.md
- Same board, fan RPM corroboration: https://github.com/arfelious/omen-fan-control/issues/14
- Zen SMN temperature reference: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LibreHardwareMonitorLib/Hardware/Cpu/Amd17Cpu.cs
- Signed PawnIO modules: https://github.com/namazso/PawnIO.Modules/releases
- Upstream: https://github.com/seakyy/OmenMon-Reborn
