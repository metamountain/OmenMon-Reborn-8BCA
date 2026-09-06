# OmenMon Reborn 8BCA

**Working fan control, real fan RPM and a real CPU temperature for the HP OMEN 16
(16-xf0xxx, baseboard `8BCA`, Ryzen 7040HS series) on Windows.**

A fork of [seakyy/OmenMon-Reborn](https://github.com/seakyy/OmenMon-Reborn), which is
itself a fork of [OmenMon](https://omenmon.github.io/) by Piotr Szczepański.

If you own an OMEN 16-xf0xxx and OmenMon shows you a fan speed near 50 RPM, a CPU
temperature that never moves under load, or fan settings that appear to do nothing —
this fork is about exactly that machine. If you own something else, start upstream;
what is written below is specific to one board and is documented so you can repeat the
method, not copy the numbers.

---

## The three findings

### 1. Fan control was never broken

Long-standing reports — an LKML thread, an HP community thread — say HP's WMI thermal
interface is firmware-broken on board `8BCA`, with `GTPS`, `RDCF`, `WHCM` and `WMAA`
returning `AE_NOT_FOUND`, and conclude that fan control must go through direct EC
register writes instead.

**That is not what happens on this machine under Windows.** A trace of every
`Bios.Send()` call (`OMENMON_BIOSTRACE=1`, see `Hardware/Bios.cs`) shows the fan
commands succeeding:

```
cmd=0x20008 type=0x2E in=[37 37 00 00] rc=0      SetFanLevel, both fans to 55
```

`rc=0` on every call. Confirmed acoustically too: a microphone measurement across a
level sweep gave −45 → −40 → −37 dBFS, so the fans really do respond.

Switching this fork to EC-based fan control — which those reports imply is necessary —
made things *worse*: fans went inaudible and the UI became laggy. That change was
reverted. **On Windows, use the WMI path.** The Linux reports are not wrong about
Linux; they simply do not describe the Windows driver stack.

Cost of getting this wrong: a day. If you are diagnosing a similar board, trace the
calls before believing a diagnosis written for a different operating system.

### 2. Fan RPM: `0xB0`/`0xB2` is not a tachometer here

`AutoCal.KnownBoards["8BCA"]` mapped fan speed to EC `0xB0`/`0xB2`, read as
little-endian 16-bit. On this board those registers read `0x005C` and `0x0000` —
"92 RPM" and "0 RPM" — and never move with load.

The working mapping is `BiosLevelMirror` with a ×100 multiplier, because fan *level*
is already expressed in units of 100 RPM:

```csharp
["8BCA"] = new Mapping {
    CpuReg = 0, CpuMode = EcDiffScanner.Mode.BiosLevelMirror, CpuMul = 100,
    GpuReg = 0, GpuMode = EcDiffScanner.Mode.BiosLevelMirror, GpuMul = 100,
},
```

This yields 4000/4300 RPM under load, matching what the machine sounds like.
Independently corroborated by
[arfelious/omen-fan-control#14](https://github.com/arfelious/omen-fan-control/issues/14),
which reports the same board idling near 2000 RPM and reaching ~6100 under load,
through a code path that returns `fan_data[fan] * 100`.

### 3. CPU temperature: the EC returns text, not numbers

This one cost the most time, and is the most likely to bite another board.

**Every EC and WMI CPU-temperature source on `8BCA` returns firmware string data.**
Not a broken sensor — bytes of an ASCII string sitting where a register is expected.
Every CPU temperature this machine ever reported was ASCII punctuation:

| Reported "°C" | Byte | Character |
|---|---|---|
| 33 | `0x21` | `!` |
| 42 | `0x2A` | `*` |
| 43 | `0x2B` | `+` |
| 44 | `0x2C` | `,` |
| 45 | `0x2D` | `-` |
| 54 | `0x36` | `6` |

Confirmed at four separate locations: `0x57` (CPUT), `0xB0`/`0xB2` (tachometer),
`0x95` (mode register, reads `0x43` = `'C'`), and the WMI `GetTemperature` (`0x23`)
call itself. `ModeReg = 0x59`, taken from Linux's `omen_v1_thermal_params`, reads a
constant 0 — also wrong for this board.

The trap is that the values look plausible. 44 °C at idle is believable, which is why
this survived so long. It was only disproven by a burn-in: OmenMon read **44 °C while
the die was at 66.8 °C**, and **33 °C while the die was at 48.5 °C**. A handful of
idle samples had looked like agreement, and were not.

**If you take one thing from this document:** never validate a temperature sensor at
idle. Load the machine and check that the reading *moves with* an independent one.

#### The fix: read the die directly

OmenMon reaches hardware through [PawnIO](https://pawnio.eu/) and loads exactly one
module — `LpcACPIEC`, which whitelists ACPI EC ports `0x62`/`0x66` and nothing else.
With every source behind those ports returning string data, there was nothing left to
read. `Driver/Ring0.cs` exposes `ReadPciConfig`, but it is a no-op stub.

The CPU itself still knows. Zen publishes `THM_TCON_CUR_TMP` on the SMN (System
Management Network) at `0x00059800`, reached through PCI configuration indices
`0x60`/`0x64`. `Resources/PAWN_BUILD.md` already documents the way there: load a
*second* officially-signed module.
[`AMDFamily17.bin`](https://github.com/namazso/PawnIO.Modules) exports
`ioctl_read_smn` — one `ulong` in, one out — and is the module LibreHardwareMonitor
uses for Ryzen Tdie. The `LpcACPIEC.bin` already shipped here is byte-identical to the
one in release 0.2.11, so taking `AMDFamily17.bin` from that same release requires no
new trust and keeps the driver Microsoft-signed.

Decoding, as in LibreHardwareMonitor's `Amd17Cpu`: bits 31:21 hold the reading in
1/8 °C steps, and either select bit means the scale is shifted down by 49 °C.

Verified on this machine against Core Temp:

```
raw 0x530B0000 -> 34.00 °C        raw 0x4E4B0000 -> 29.25 °C
                                  (Core Temp read 28 °C at that moment)
```

Two checks worth repeating on your own board: the raw values fall by `0x04C00000` per
4.75 °C — `2^24` per degree — which confirms the bits-31:21 / 0.125 °C scale; and
`RANGE_SEL` really is set here (`0x0B` in the third byte puts bit 19 high), so the
−49 °C shift applies. Skip that shift and the CPU appears to idle at 83 °C.

Implementation: `Driver/PawnIoAmd.cs`. It self-probes on open and disables itself if
the value is implausible, so on a non-AMD machine or without PawnIO it simply stays
unavailable and the EC path is untouched.

One consequence: Zen boosts on any brief foreground task, so the raw die reading
spikes ~30 °C for a single sample at idle. The EC value had been smoothed in firmware.
A median-of-3 is applied before the value reaches the fan programs, or the curves
chase every transient.

---

## Other things learned about this board

- **Fan programs apply as step functions, not interpolated ramps.**
  `GetTemperatureLevel` picks the highest threshold at or below the current
  temperature, so a "curve" with sparse points is a staircase. Put points where you
  want the steps.
- **Fan levels are in units of 100 RPM.** Level 18 is 1800 RPM. The practical floor on
  this chassis is around 1700; below that the fans stall rather than spin slowly.
- **Fan curves work off `max(CPU, GPU)`.** Before the die fix the curves were
  functioning only because `GPTM` (EC `0xB7`) is a valid GPU sensor and the `max()`
  carried it. The CPU term contributed nothing at all.
- **The EC lock is usually contended by OmenMon itself**, not by another application.
  `Global\Access_EC` is held for a whole `EcExecBatch` sensor pass, and `Ec.cs`'s read
  backoff `Wait()`s while holding it, so a single 600 ms attempt from the UI thread
  loses. Acquisition now retries across a budget (`EcMutexTotalTimeout`, default
  2500 ms). The error log names the competing process — on the first timeout observed
  here, there was none.
- **The monitor thread starves under sustained full load** — 2 telemetry samples in
  15 minutes was observed — which also thins out `CheckThermalPanic`. Not yet fixed.
- **Burn-in result:** peak **73.2 °C** after ~15 minutes of all-core load on the
  Silent profile, comfortably below the 90 °C limit.
- **HP reuses ProductId `8BCA` across CPU and regional variants with conflicting EC
  layouts** (upstream issues #76/#85/#114/#142 are deferred for this reason).
  Everything above is specific to *this* machine.

---

## What changed in the application

| Area | Change |
|---|---|
| **CPU temperature** | AMD Tctl/Tdie over SMN via a second PawnIO module, median-of-3 smoothed (`Driver/PawnIoAmd.cs`) |
| **Fan RPM** | `BiosLevelMirror` ×100 mapping for `8BCA` (`Library/AutoCal.cs`) |
| **Fan profiles** | Reduced to Performance / Default / Silent, with `+` to add. The three standard ones cannot be deleted. Selecting one applies immediately — no second click, no hysteresis wait |
| **Fan curve** | Inline editable: left-click adds a point, right-click removes one, drag to move (`App/Gui/GuiCurveEditor.cs`) |
| **Live graph** | 200-sample rolling temperature and RPM history with real axes, dropout bridging and spike filtering (`App/Gui/GuiChart.cs`) |
| **UI** | Dark throughout, including the DWM title bar; tray context menu replaced by in-window controls (`App/Gui/GuiTheme.cs`, `External/Dwm.cs`) |
| **Power** | Windows power mode overlay and CPU wattage as a single selector, with custom wattage derived so it stays consistent with the chosen profile (`Library/UserPrefs.cs`, `External/PowrProf.cs`) |
| **Persistence** | Applied settings survive a restart via an `OmenMon-user.conf` sidecar (`Library/UserPrefs.cs`) |
| **Telemetry** | `OmenMon-telemetry.csv`, written on a heartbeat so logging continues with the window closed (`Library/TelemetryLog.cs`) |
| **Errors** | Selectable, copyable error dialog carrying board, BIOS, OS and the full exception chain; everything shown to the user is also appended to `OmenMon-error.log` (`App/Gui/GuiFormError.cs`) |
| **Config saving** | Written to a temporary file and swapped in, so a failed save cannot leave a half-written `OmenMon.xml` |
| **Icons** | Monochrome and flat: white OMEN diamond, black mark, black temperature digits, no outline |
| **Calibration** | Auto-calibration and its sidecar removed — on this board it produced confidently wrong mappings |

The assembly name stays `OmenMon`. `Config.AppName` derives the settings-XML root
element `<OmenMon>`, the scheduled-task names and the mutex names from it, so renaming
it would orphan an existing configuration. Only the display identity changes, through
`Config.DisplayName`.

---

## Requirements

- Windows 10/11, x64
- [PawnIO](https://pawnio.eu/) installed — its kernel driver is Microsoft-signed, so
  Defender stays quiet
- Administrator rights: EC and SMN access both require elevation
- .NET Framework 4.8

## Build

```
msbuild OmenMon.csproj /p:Configuration=Release /p:Platform=x64
```

## Diagnostics

| Command | What it gives you |
|---|---|
| `OmenMon.exe -Diag` | Live fan telemetry: active register, mode, multiplier, BIOS fan count |
| `OmenMon.exe -Probe` | WMI + BIOS + EC snapshot as Markdown |
| `OmenMon.exe -Ec` | Live EC monitor |
| `OMENMON_BIOSTRACE=1` | Logs every WMI `Send()` with its arguments and return code |

`OmenMon-error.log` and `OmenMon-telemetry.csv` are written next to `OmenMon.xml`.

---

## Adapting this to your own laptop

The method, in the order that actually worked:

1. **Trace before theorising.** `OMENMON_BIOSTRACE=1` shows whether the WMI calls
   return `rc=0`. A day was lost here to a Linux bug report describing a different
   driver stack.
2. **Dump the EC across a load sweep** and look for bytes that *move with* load and
   return to idle. Bytes that hold constant, or sit in the ASCII printable range, are
   string data.
3. **Never validate a sensor at idle.** Load the machine and compare against an
   independent reading — Core Temp, HWiNFO, a thermocouple. Idle agreement is not
   agreement.
4. **A microphone is a real instrument.** A phone mic will tell you whether the fans
   responded to a command. Turn off the microphone's noise suppression first: Windows
   "audio enhancements" cancel steady fan noise, which pinned readings at the noise
   floor until it was disabled.
5. **When the EC has nothing usable, go around it.** The CPU, GPU and PCH all publish
   their own sensors. PawnIO has signed modules for MSR, SMN, PCI config and LPC; the
   table in `Resources/PAWN_BUILD.md` says which module covers what.

`CLAUDE.md` holds the machine-specific detail in the form an AI coding assistant can
act on, including the theories that turned out to be wrong and how they were
disproven.

---

## Danksagungen · Credits

This project is three layers of other people's work. In order:

**[Piotr Szczepański](https://github.com/OmenMon)** — author of the original
[OmenMon](https://omenmon.github.io/). The WMI BIOS interface, the EC access layer,
the fan program engine, the keyboard backlight control, the localisation system and
the tray application are all his. Everything here rests on that foundation, and the
architecture held up under changes he never anticipated. Licensed GPL-3.0.

**[seakyy](https://github.com/seakyy)** — author of
[OmenMon-Reborn](https://github.com/seakyy/OmenMon-Reborn), which replaced the
hardcoded 2023 EC layout with an XML-driven model database, added the heuristic
auto-detector and the `AutoCal.KnownBoards` table, and migrated the driver from
WinRing0 to PawnIO. Without the `KnownBoards` mechanism there would have been nowhere
to put the `8BCA` fan-RPM fix. Consider
[buying them a coffee](https://buymeacoffee.com/seakyy).

**[namazso](https://github.com/namazso)** — [PawnIO](https://pawnio.eu/) and
[PawnIO.Modules](https://github.com/namazso/PawnIO.Modules). A Microsoft-signed kernel
driver with signed, sandboxed modules is what makes hardware access on modern Windows
possible without a fight with Defender. The `AMDFamily17` module is what finally
produced a correct CPU temperature on this machine.

**[LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)**
— `Amd17Cpu.cs` is the reference for the Zen SMN temperature register and its
decoding, including the −49 °C range-select shift.

**[arfelious/omen-fan-control](https://github.com/arfelious/omen-fan-control)** and
**[alou-S/omen-fan](https://github.com/alou-S/omen-fan)** — the Linux OMEN tools.
`alou-S`'s [`docs/probes.md`](https://github.com/alou-S/omen-fan/blob/main/docs/probes.md)
is the canonical EC register map, and issue #14 on `arfelious`'s tracker independently
corroborated the fan-RPM finding on this same board.

**[Core Temp](https://www.alcpu.com/CoreTemp/)** — the independent reading that
disproved the EC temperature and then confirmed the SMN one. A second opinion from a
tool sharing no code with yours is worth more than any amount of reasoning.

The hardware investigation, the diagnostic tooling and the code in this fork were
produced in a pair-programming session with
[Claude Code](https://claude.com/claude-code) (Anthropic).

---

## License

**OmenMon** Copyright © 2023-2024 [Piotr Szczepański](https://piotr.szczepanski.name/)
**OmenMon-Reborn** modifications Copyright © 2026 [seakyy](https://github.com/seakyy)
**8BCA fork** modifications Copyright © 2026 metamountain

This application is _free software_: you can redistribute it and/or modify it under
the terms of the
[GNU General Public License Version 3](https://www.gnu.org/licenses/gpl-3.0.html#license-text)
as published by the [Free Software Foundation](https://www.fsf.org/). The full text is
in [LICENSE.md](LICENSE.md); the GPLv3 change record is in
[MODIFICATIONS.md](MODIFICATIONS.md).

**OmenMon** builds upon the work of several other projects; see the
[acknowledgements](https://omenmon.github.io/more#acknowledgements).

_This software is not affiliated with or endorsed by HP. Any brand names are used for
informational purposes only._
