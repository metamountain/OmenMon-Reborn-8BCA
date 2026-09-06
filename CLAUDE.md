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
| CPU | AMD Ryzen 9 7945HS (Zen 4, Phoenix, family 19h) |
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

### The EC lock is usually contended by OmenMon itself

The first user-visible timeout logged `other EC users: none detected`. The monitor
thread holds `Global\Access_EC` for a whole `EcExecBatch` pass, and `Hardware/Ec.cs`'s
read backoff `Wait()`s while holding it. `Hw.EcRequest()` now retries across
`EcMutexTotalTimeout` (2500 ms) instead of making a single `EcMutexTimeout` attempt.

## Disproven — do not resurrect

These were believed, acted on, and are wrong. They are recorded because the sources
that support them are still online and still persuasive.

| Claim | Source | How it was disproven |
|---|---|---|
| HP's WMI thermal interface is firmware-broken on `8BCA`, so fan control must use direct EC writes | LKML "hp-wmi: Fan control broken on HP OMEN 16-xf0xxx (board 8BCA, BIOS F.31)"; HP community "broken ACPI tables" | BIOS trace shows `rc=0` on every fan call; microphone confirms the fans respond. Switching to EC control made fans inaudible and the UI laggy. **Reverted.** The reports describe Linux, not the Windows driver stack. |
| Fan setpoints live at EC `0x11` (CPU) / `0x14` (GPU) | Inferred from calibration-report EC dumps: those bytes tracked the load sweep | They correlate because they *mirror* the level the WMI path set, not because writing them controls anything. The WMI path was already working. |
| `FanLevelReg1` should be `0x14` instead of `0x12` | Same inference | Moot — the EC fan-level path is not used. |
| CPU temperature was fixed by disabling `CPUT` | 6 idle samples reading 44-45 against a die of 41 | Burn-in disproved it: 44 °C reported while the die was at 66.8, and 33 while the die was at 48.5. **Idle agreement is not agreement.** |

## Still open

- **Monitor thread starves under sustained full load** — 2 telemetry samples in 15
  minutes observed. This also thins `CheckThermalPanic`, which is a safety path.
  Raising the thread priority is the obvious first move; not yet done.
- **Config save occasionally fails.** The user saw "failed to save configuration data"
  while the change survived in memory. `Config.Save()` now logs the exception to
  `OmenMon-error.log` and writes via a temp file + `File.Replace`, so the next
  occurrence will name its own cause. Root cause still unknown.
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
- Deploy with `C:\Users\omen\omen-deploy.cmd` (self-elevating). The running app is
  elevated, so an unelevated session cannot overwrite the exe.

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
