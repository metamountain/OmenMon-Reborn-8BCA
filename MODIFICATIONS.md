# OmenMon-Reborn: Modifications to the Original Work

This document outlines the modifications made to the original `OmenMon` project to comply with Section 5(a) of the GNU General Public License Version 3 (GPLv3).

**Original Work:** OmenMon (Copyright © 2023-2024 Piotr Szczepański)  
**Modified Work:** OmenMon-Reborn (Fork additions © 2026 seakyy)  
**Date of Fork/Modifications:** April 2026  

## Summary of Architectural Changes
The original OmenMon application hardcoded the Embedded Controller (EC) register layout for 2023 HP Omen models, leading to compatibility issues and hardware bugs (e.g., fans stuck at 100%) on newer devices. 

OmenMon-Reborn extracts this logic into a dynamic, configuration-driven model database (`OmenMon.xml`) and introduces a safe, heuristic read-only hardware scanner to auto-detect and configure unknown models.

## Modified Files
The following files from the original codebase were modified by seakyy in April 2026. A prominent copyright notice (`// OmenMon-Reborn additions © 2026 seakyy`) has been added to the header of each substantially modified or newly created file:

### New Files Created
* `Hardware/PlatformPreset.cs`: Defines the data structure for EC register layouts.
* `Hardware/AutoDetector.cs`: Implements the read-only heuristic EC scanner for unknown hardware.
* `App/Cli/CliOpProbe.cs`: Implements the `-Probe` CLI verb and the hardware markdown dump logic.

## Phase 7 — RGB Preset Hotkey (Colour Cycling via Omen Key)

A new `CycleColorPresets()` method was added to `App/Gui/GuiOp.cs`.  When enabled, pressing the Omen key cycles through all `<ColorPresets>` entries defined in `OmenMon.xml` in alphabetical order, wrapping back to the first after the last.  The active preset is detected by comparing the live hardware colour table against the preset list; if no match is found (i.e. a custom colour is set) the cycle starts at the first preset.

Two new XML configuration keys control the feature:
| Key | Default | Meaning |
|-----|---------|---------|
| `KeyToggleColorPreset` | `false` | Enable/disable the feature |
| `KeyToggleColorPresetSilent` | `false` | Suppress the balloon-tip notification |

The feature takes priority over `KeyToggleFanProgram` and `KeyCustomAction` in the Omen-key handler chain.

---

### Modified Files
* **Core Logic & DB:** 
  * `Library/Config.cs` & `Library/ConfigData.cs`: Added XML parsing and serialization for the `<Models>` database; disabled TNT2-TNT5 sensors by default.
  * `Hardware/Platform.cs`: Removed hardcoded `switch` statement for device IDs; implemented dynamic preset loading.
* **Hardware Interfacing:**
  * `Hardware/FanArray.cs`: Modified `SetOff()` to use BIOS calls instead of EC when `FanLevelUseEc` is false.
  * `Driver/Ring0.cs`: Rewritten in v1.4.0 to delegate to PawnIO instead of WinRing0 (see Phase 8 below). Public API preserved.
  * `Library/Os.cs`: Added `GetAvailableRefreshRates` using `EnumDisplaySettings`.
* **User Interface:**
  * `App/Gui/GuiFormMain.cs` & `App/Gui/GuiFormMainInit.cs`: Added startup hook to trigger `AutoDetector` if the device is unknown.
  * `App/Gui/GuiMenu.cs`: Added the "Auto-Calibrate & Diagnose..." tray entry that launches the Auto-Calibration Wizard (`App/Gui/GuiFormCalibration.cs`). The wizard runs an active 4-step fan stress sweep, scans the EC dumps via `Hardware/EcDiffScanner.cs` to discover RPM-tachometer registers (16-bit LE / period-encoded / direct-multiplier), applies the result to the live session through `Library/AutoCal.cs`, persists it to a sidecar XML, and copies a Markdown report to the clipboard for upstream contribution. Replaces the older static "Contribute Hardware Data" dump-and-paste flow.
  * `App/Gui/GuiTray.cs`: Implemented a background heartbeat timer to prevent Performance Control from sleeping.
* **Build System:**
  * `OmenMon.csproj`: Included new `.cs` files in the compilation target. v1.4.0 swaps the embedded resource `OmenMon.Driver.sys.gz` (WinRing0) for `OmenMon.LpcACPIEC.bin` (PawnIO).

## Phase 8 — WinRing0 → PawnIO Migration (v1.4.0)

Replaces the WinRing0 kernel driver with [PawnIO](https://pawnio.eu/), whose Microsoft-signed driver no longer triggers Windows Defender warnings. The migration is invisible to higher layers: `Driver/Ring0.cs` keeps its original public API and `Hardware/Ec.cs` is unchanged.

### New Files Created

* `Driver/PawnIo.cs`: User-mode wrapper around `PawnIOLib.dll`. Locates the library via the registry / Program Files, pre-loads it via `LoadLibraryW`, opens a PawnIO executor handle, loads the embedded module blob, and exposes a `PawnIo.Execute(name, in[], out[])` entry point used by `Ring0.cs`.
* `Resources/LpcACPIEC.bin`: The official namazso-signed PawnIO module from [PawnIO.Modules releases](https://github.com/namazso/PawnIO.Modules/releases). Embedded as resource `OmenMon.LpcACPIEC.bin`. Provides byte-granular ACPI EC port I/O (ports `0x62` / `0x66` only). LGPL-2.1-or-later, redistribution allowed unmodified — see `Resources/PAWN_BUILD.md` for module rotation procedure.
* `Resources/PAWN_BUILD.md`: Operational documentation for the embedded PawnIO module (where it comes from, how to update it, troubleshooting).
* `docs/DEV_NOTES_v1.4.0.md`: Full architecture and rotation notes for the PawnIO migration.

### Removed Files

* `Driver/Driver.cs`: WinRing0 service installation / kernel-driver IOCTL plumbing — obsolete.
* `Resources/Driver.sys.gz`: Compressed WinRing0 kernel driver — obsolete; the user installs the signed PawnIO driver themselves.

## Phase 9 — Post-PawnIO Regression Sweep (v1.4.0)

The first week of v1.4.0 field testing surfaced four GUI / fan-control regressions and one hardware compatibility crash that don't belong to the PawnIO migration itself but were uncovered by it. They are bundled into the same v1.4.0 release rather than a 1.4.1 because the migration and these fixes are inseparable from a user's perspective (the migration is what made the regressions visible).

### Modified Files

* `App/Cli/CliOpCalibration.cs`: `ApplyLevel` now disengages BIOS manual mode immediately before calling `SetMaxFan(true)` on the 100% step, then re-engages it on any subsequent sub-100% step. Without this, manual mode pins the fans at `Config.FanLevelMax` (~3.4-3.8 kRPM) and the wizard's calibration plateau is systematically ~30% lower than the fan's physical ceiling (issues #40 / #41 / #52).
* `App/Gui/GuiFormMain.cs`: Replaced a non-existent `UpdateFanMode()` call (introduced by the 8C30 safety dialog and breaking the build on a clean checkout) with the existing `UpdateFanCtl()`. Also added the missing `SetMode(GetMode())` mode-refresh step in the "user cancelled max → fall back to safe levels" branch, matching the normal constant-speed path.
* `App/Gui/GuiMenu.cs`: Added missing `using OmenMon.Hardware.Platform;` for the `FanArray.HasMaxFanFreeze` reference (compile-blocking on a clean checkout). Added `SetOff(false)` before `SetMax(true)` in the tray "Max fan" toggle so the BIOS honours the command when the fan-off latch is set.
* `Hardware/FanProgram.cs`: `SetFanLevel` clears a stale `Fan Off` latch (only when actually set) before writing levels, fixing the GPU fan getting stuck off on some boards while CPU continues to ramp with the curve (issue #39, 8D07). The global `Max Fan` latch is intentionally *not* touched here — clearing it would defeat Thermal Panic's safety override, which is asserted only on the transition into panic and not re-asserted every tick.
* `Hardware/Bios.cs`: `Check()` no longer throws on BIOS return codes 1, 4, 6 and 46. The original author had noted these codes "were also observed but their exact meaning is not understood"; every reported instance has been a benign "command not supported on this platform" — most recently an Omen Transcend 14 (fb0118TX) crashing the GUI on open with `BIOS call failed: Unknown response from BIOS: 4`. The unknown codes are now treated the same as code 3 (command not available): soft-fail, let the rest of the GUI come up.
* `Library/AutoCal.cs`: Added a built-in `KnownBoards` mapping for 8DD0 (LE16 RPM at 0xB0 / 0xB2). The Auto-Calibration heuristic occasionally locks onto bogus PeriodEncoded8 offsets on this board (issue #33's "50k RPM" display); the existing >16-byte sidecar distance sanity check now falls through to this known-good layout.
* `OmenMon.xml`: Added native model database entries for `8D26` (HP Omen 16-ap0007ns, 2026) and `88EB` (HP Victus 16, 2021). Both use the standard 2023+ layout, confirmed via the Auto-Calibration Wizard (issues #52, #48).

### New Files Created

* `.github/workflows/draft-release.yml`: Tag-driven automation. When `v*.*.*` is pushed, extracts the matching `## [version]` / `## [version-reborn]` section from `CHANGELOG.md` and creates a *draft* GitHub release with those notes. The maintainer attaches the locally-built `OmenMon.exe` and publishes manually — at which point the existing `release.yml` builds + zips + attaches the public archive. Idempotent (re-runs refresh the draft body instead of duplicating).
* `App/Crash.cs`: Telemetry-free crash dumper. Hooks `AppDomain.UnhandledException` and `Application.ThreadException`; writes a self-contained `OmenMon-crash-*.log` next to the executable with the full exception chain, process metadata, and the diagnostic bundle. Falls back to `%LOCALAPPDATA%` when the install directory isn't writable.
* `App/Cli/CliOpDiag.cs`: One-shot diagnostic Markdown generator. Used by the `-Diag` CLI verb, the tray "Copy Diagnostic Info" menu entry, and embedded into every crash log. Bundles environment, driver status, resolved model preset, AutoCal sidecar contents, recent EC trace, crash-log inventory, and a `-Probe` snapshot.
* `Library/EcTrace.cs`: Lock-free 1024-entry circular buffer of recent EC reads/writes (timestamp + register + value + op kind). Hooked from `Hardware/Ec.ReadByte` / `WriteByte`; consumed by the crash dumper and `-Diag`. Helps diagnose intermittent issues like #49 (random fan spikes) where the symptom is gone by the time the user opens a bug report.

## Phase 10 — First-Week Field-Report Sweep (v1.4.1)

Patch release addressing the cluster of issues (#32 / #56 / #57 / #58 / #59) that arrived once the v1.4.0 Auto-Calibration Wizard reached users on entry-level Victus 15/16 SKUs whose physical fan ceiling sits below the BIOS rate-limiter's threshold, and a hibernation symptom on the 8C30 cohort where Windows transiently reports battery <5 % under heavy load.

### New Files Created

* `Library/PowerGuard.cs`: Battery-glitch hibernation guard (issue #59). Watches `SystemInformation.PowerStatus` on the GUI tray's 1 s timer tick. When the OS-reported battery percentage drops by >= `Config.BatteryGlitchDropPercent` within `Config.BatteryGlitchWindowMs` while AC is connected, treats it as a torn read from the Smart Battery System path and asserts `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)` for `Config.BatteryGlitchHoldMs` to skip Windows' Critical Battery Action. Suppressed on battery power — a real low-battery state must still hibernate normally. All four parameters configurable via `OmenMon.xml`; master switch `BatteryGlitchGuard` defaults to true.
* `wiki/Battery-Glitch-Guard.md`: User-facing documentation for the guard — what it does, how it decides "this is a glitch", config knobs, tuning guidance, verification via `powercfg /requests`, and the boundaries of what it does NOT do.

### Modified Files

* `Hardware/FanArray.cs`: `HasMaxFanFreeze` switch extended from `8C30` only to `{ 8C30, 8D07, 8BAD, 8E35 }`. Issues #56 (@ghend-oss, 8D07), #58 (@MartinSalg818, 8BAD), #57 (@ClockworkNirvana, 8E35) all reported the same BIOS-rate-limiter freeze signature — 70 % → 100 % shows zero RPM gain on the GPU and a small regression on CPU before the EC fan controller locks until reboot. Adding a ProductId to this switch propagates the safety guard to every call-site (wizard 100 %-step filter, GUI manual SetMax confirm dialog) automatically.
* `App/Cli/CliOpCalibration.cs`: Added wizard plateau detection. `CalibrationOutcome` now carries `LiveSpeeds`, `PlateauReached`, `PlateauStepPercent`, `PlateauNote`. After each commanded step the wizard reads live tachometer RPM via `Fan.GetSpeed()`, and if both fans show <150 RPM gain compared to the highest prior step with the latest reading at >=1500 RPM, the wizard concludes the board has hit its physical fan ceiling and aborts any higher steps still queued. Generic by design: catches future SKUs with the same firmware signature without requiring a `HasMaxFanFreeze` list entry. `BuildReport` was extended with a "Live RPM at Each Step" table and a hoisted plateau callout above the device section.
* `OmenMon.xml`: Added native model database entry for `8E35` (HP Victus 15, 2026 BIOS — issue #57). Register layout is the canonical Victus 15 2022-style — FanLevel at `0x34`/`0x35`, rate write at `0x2C`/`0x2D`, rate read at `0x2E`/`0x2F`, 16-bit LE RPM tachometers at `0xB0`/`0xB2`. Derived from @ClockworkNirvana's raw EC-dump analysis, not the wizard's auto-detect output (which misidentified CPU RPM at `0x30` due to locking onto a latched status word).
* `Library/ConfigData.cs`: Added `BatteryGlitchGuard` (bool, default `true`) plus `BatteryGlitchDropPercent` (30), `BatteryGlitchWindowMs` (5000), `BatteryGlitchHoldMs` (30000) config knobs for the hibernation guard. Read from `OmenMon.xml` via the existing `Config.Load()` path.
* `External/Kernel.cs`: Added `ExecutionState` flags enum and `SetThreadExecutionState` P/Invoke for the hibernation guard. Standard `kernel32.dll` API, no new dependencies.
* `App/Gui/GuiTray.cs`: Wired `PowerGuard.Initialize()` into the tray's construction path (after `SystemEvents.PowerModeChanged` registration), `PowerGuard.Tick(this)` into `EventTimerTick`, and `PowerGuard.Dispose()` into the disposal path so any held `ES_SYSTEM_REQUIRED` is released even on non-graceful exit.
* `OmenMon.csproj`: Added `<Compile Include="Library\PowerGuard.cs" />` so the classic-style project picks up the new file.
* `wiki/Home.md`: Bumped current-release line to v1.4.1-reborn and added the Battery-Glitch Guard wiki entry.
* `All/Version.cs`: 1.4.0 → 1.4.1; `AssemblyInformationalVersion` "1.4.1-reborn".
* `CHANGELOG.md`: Added 1.4.1-reborn block with issue references for #32 / #56 / #57 / #58 / #59, plus a "Deferred" section for #37 (8BCD — conflicting reports across two users on the same ProductId; cannot ship a native entry without disambiguating data from a sustained-load probe).
## Board 8BCA branch — HP OMEN 16-xf0079ng (2026-09)

A further derivative work, modifying OmenMon-Reborn rather than the original OmenMon.
Recorded here for the same GPLv3 Section 5(a) reason as the sections above.

**Modified Work:** OmenMon-Reborn 8BCA branch
**Date of Modifications:** September 2026

The engineering rationale, the measurements behind each decision and — importantly —
the theories that were tested and disproven are in `CLAUDE.md`. This section lists only
what changed.

### Summary

The board this branch targets has firmware string data where the canonical HP EC layout
has registers: `CPUT` (`0x57`), the `0xB0`/`0xB2` tachometer, `0x95`, and the WMI
`GetTemperature` call all return ASCII rather than measurements. Both real sensors
therefore had to be moved outside the EC, and the fan speed readout had to stop claiming
to be a measurement at all.

### New Files Created

* `Driver/PawnIoAmd.cs`: Loads a second PawnIO module (`AMDFamily17.bin`) and reads
  `THM_TCON_CUR_TMP` over the SMN at `0x00059800` for a true AMD die temperature.
* `Driver/Nvml.cs`: NVIDIA NVML binding for the discrete GPU die temperature, including
  detection of and recovery from the stale-session state a parked dGPU leaves behind.
* `App/Gui/GuiTheme.cs`, `App/Gui/GuiChart.cs`, `App/Gui/GuiCurveEditor.cs`,
  `App/Gui/GuiColorPicker.cs`, `App/Gui/GuiPipette.cs`: Dark theme and the rebuilt
  main-window surface.
* `Library/TelemetryLog.cs`: Rolling CSV of temperatures, fan levels and commands, plus
  a narrower fan-thread trace, both for diagnosis.
* `Resources/AMDFamily17.bin`: Signed PawnIO module, byte-identical to PawnIO.Modules
  release 0.2.11.

### Modified Files

* `Hardware/Platform.cs`: CPU and GPU temperature accessors now own the die/NVML
  overrides, with per-sensor trust flags. Previously these lived in the GUI layer, which
  meant the fan curves ran on the EC sensors while the display showed corrected values.
* `Hardware/FanProgram.cs`: Per-fan curve lookup against each component's own
  temperature; linear interpolation between curve points (`GetCurveLevels`, replacing the
  stepping `GetTemperatureLevel`); asymmetric response — rises immediate, descent rate
  limited; untrusted-sensor fallbacks that differ by component; countdown re-arm made
  unconditional and retried.
* `Hardware/Fan.cs`: `IsSpeedMeasured` flag distinguishing a tachometer reading from the
  commanded level echoed back.
* `Library/AutoCal.cs`: `KnownBoards["8BCA"]` mapped to `BiosLevelMirror x100`;
  `AutoCal.Load()` neutered, because a persisted wrong mapping is worse than none.
* `App/Gui/GuiMonitor.cs`: Fan curve moved to a dedicated thread; median filtering of the
  die reading; fan-level divergence detection.
* `Hardware/Bios.cs`: `OMENMON_BIOSTRACE` tracing of every WMI call and return code.
* `OmenMon.xml`: `CPUT` disabled; fan profiles and intervals retuned for this chassis.

### Removed

This branch is a single-machine fork and is not published, so upstream's project
apparatus was dropped. **The earlier sections of this document still refer to some of
these files.** Those references are the historical record of what seakyy changed at the
time and are left exactly as written; the files themselves are simply not present here.

* `CHANGELOG.md`: upstream release history. Its one finding that matters to this machine
  — that upstream ships an `8BCA` tachometer mapping which is wrong on this board, and
  that three field reports for that ProductId contradict each other outright — was moved
  into `CLAUDE.md` before removal.
* `.github/`: FUNDING, issue templates and the CI/release workflows for the upstream
  repository.
* `wiki/`: upstream's user wiki and its images.
* `INSTRUCTION.md`, `ARCHITECTURE.md`, `SECURITY.md`: upstream's user manual, design
  overview, and vulnerability-reporting policy, which directs reports to the upstream
  maintainer.
* `docs/RELEASING.md`, `docs/release-notes/`: upstream's release runbook — addressed to
  the maintainer by name — and release notes for versions preceding this branch.
* `docs/ARCHITECTURE_AUDIT.md`, `docs/DEV_NOTES_v1.4.0.md`: upstream's v1.5 planning
  audit and the v1.4.0 PawnIO migration write-up. The guard ledger the audit held is now
  stated in `Tests/OmenMon.Tests/GuardLedgerTests.cs` itself, where each test names the
  issue that produced its guard.

`LICENSE.md` and this file are retained as GPLv3 requires. `CLAUDE.md` carries the
engineering record for this branch.
