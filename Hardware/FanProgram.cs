  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023-2024 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/

using System;
using System.Collections.Generic;
using OmenMon.Hardware.Bios;
using OmenMon.Library;

namespace OmenMon.Hardware.Platform {

#region Data
    // Stores fan program data
    public class FanProgramData {

        // Fan mode to maintain while the program is running
        public BiosData.FanMode FanMode;

        // GPU power level to set when the program starts
        public BiosData.GpuPowerLevel GpuPower;

        // Stores the mapping of temperature thresholds
        // to fan levels for each fan
        public SortedDictionary<byte, byte[]> Level;

        // Fan program name
        public string Name;

        // Constructs a fan program data instance
        public FanProgramData(
            string name,
            BiosData.FanMode fanMode,
            BiosData.GpuPowerLevel gpuPower,
            SortedDictionary<byte, byte[]> level) {

            this.Name = name;
            this.FanMode = fanMode;
            this.GpuPower = gpuPower;
            this.Level = level;

        }

    }
#endregion

    // Stores fan program logic
    public class FanProgram {

#region Variables & Initialization
        // Status callback importance rating
        public enum Severity {
            Verbose,    // Only for reference
            Notice,     // Might be shown if possible
            Important   // Need to be shown to the user
        }

        // Callback method for status updates
        private Action<FanProgram.Severity, string> Callback;

        // GPU power data for the current program
        private BiosData.GpuPowerData GpuPowerData;

        // State flags
        public bool IsAlternate { get; private set; }
        public bool IsEnabled { get; private set; }
        public bool IsSuspended { get; private set; }

        // Consecutive ticks with no trustworthy CPU temperature. See Update().
        private int untrustedTicks;

        // True while the CPU reading is not trustworthy. Replaces the old handedBack
        // flag; the fans are never handed to the firmware now, so there is nothing to
        // hand back from.
        private bool cpuUntrusted;
        private int cpuUntrustedLevel;

        // Ticks of a bad CPU reading before the floor starts rising. Five at the 2 s tick
        // is ten seconds - long enough that a single collision on the Access_PCI mutex,
        // which PawnIoAmd retries through, never gets this far.
        private const int UntrustedTicksBeforeFloor = 5;

        // Where the floor is heading: the level the active curve uses at this
        // temperature. Taken from the curve rather than hard-coded, so a Silent profile
        // stays quieter than a Performance one even in this state. 75 °C is warm enough
        // to be protective and cool enough not to sound like a fault.
        private const byte UntrustedTargetC = 75;

        // One level - 100 rpm - per tick, in both the CPU and GPU untrusted ramps
        private const int UntrustedRampStep = 1;

        // Attempts at the countdown write within a single pass.
        //
        // Two, not three, and the reason is latency rather than reliability. Each attempt
        // already contains EcMutexTotalTimeout (2500 ms) of internal retry inside
        // Hw.EcRequest, and SetCountdown is a write plus a read-back, so an attempt can
        // cost five seconds on a contended EC. Three of those is fifteen — and this runs
        // inside FanProgram.Update(), which ApplyFanSettings calls on the UI thread when
        // the user picks a profile. That was a visibly stuck window. A second attempt is
        // worth having; a third buys almost nothing and costs another five seconds.
        private const int CountdownWriteAttempts = 2;

        // Consecutive dropped countdown writes, for the log line that reports recovery
        private int countdownWriteFailures;

        // The GPU side of the same problem, but ramping the other way: see Update(). The
        // held GPU value can only be too high, so its floor walks down rather than up.
        private int gpuUntrustedLevel;
        private int gpuUntrustedTarget;

        // 2500 rpm: clearly audible, clearly moving air, and well short of the 3800 rpm
        // the curve reaches under real load. A GPU that is actually hot is not cooled by
        // this alone, but nothing here substitutes for a working sensor - it is what
        // keeps the card ventilated until one comes back. Never raises the fan: if the
        // reading is lost while already below this, the lower level is kept.
        private const int GpuUntrustedFloor = 25;

        // For the log line, so the number and its meaning cannot drift apart
        private const string UntrustedGpuGraceDescription = "longer than the NVML rebuild interval";

        // Kept for callers that ask "is the curve in charge right now?" - it is, always,
        // but not following the curve while the CPU reading is missing.
        public bool IsHandedBack { get { return this.cpuUntrusted; } }

        // Last fan mode and GPU power data before the program started
        private BiosData.FanMode LastFanMode;
        private BiosData.GpuPowerData LastGpuPowerData;

        // Level list for the current program
        // to facilitate threshold look-ups
        private List<byte> Levels;

        // Name of the last running program
        private string Name;

        // Parent class reference
        private Platform Platform;

        // Constructs a fan program instance
        public FanProgram(
            Platform platform,
            Action<FanProgram.Severity, string> callback) {

            this.Callback = callback;
            this.GpuPowerData = default(BiosData.GpuPowerData);
            this.IsAlternate = false;
            this.IsEnabled = false;
            this.IsSuspended = false;
            this.LastFanMode = BiosData.FanMode.Default;
            this.LastGpuPowerData = default(BiosData.GpuPowerData);
            this.Levels = new List<byte>();
            this.Name = "";
            this.Platform = platform;

        }
#endregion

#region Public Methods
        // Retrieves the name of the current fan program
        public string GetName() {

            return this.Name;

        }

        // Re-enable fan program
        // following a resume from suspend
        public bool Resume() {

            // Fail if no program active
            // or if not suspended
            if(!this.IsEnabled || !this.IsSuspended)
                return false;

            // Set the state flag
            this.IsSuspended = false;

            // Update the program
            Update();

            // Report success
            return true;

        }

        // Starts a fan program given its name
        public bool Run(string name, bool isAlternate = false) {

            // Note: no need to terminate
            // the previous program first, if any

            // Try to set up the new program
            // bail out if not succesful
            if(!Setup(name))
                return false;

            // Enable manual fan mode
            if(Config.FanLevelNeedManual)
                Platform.Fans.SetManual(true);

            // Set the alternate flag
            this.IsAlternate = isAlternate;

            // Set the state flag
            this.IsEnabled = true;

            // Save the last fan mode
            this.LastFanMode = Platform.Fans.GetMode();

            // Save the last GPU power state
            this.LastGpuPowerData = Platform.System.GetGpuPower();

            // Update the program
            Update();

            // Report success
            return true;

        }

        // Suspend the running fan program
        public bool Suspend() {

            // Fail if no program active
            // or if already suspended
            if(!this.IsEnabled || this.IsSuspended)
                return false;

            // Set the state flag
            this.IsSuspended = true;

            // Reset fan speed
            SetFanLevel(new byte[] { Byte.MaxValue, Byte.MaxValue } );

            // Disable manual fan mode
            if(Config.FanLevelNeedManual)
                Platform.Fans.SetManual(false);

            // Restore the previous fan mode
            UpdateFanMode(true, this.LastFanMode);

            // Restore the previous GPU power settings
            UpdateGpuPower(true, this.LastGpuPowerData);

            // Report success
            return true;

        }

        // Terminates the running fan program, if any is running
        public bool Terminate() {

            // Fail if no program active
            if(!this.IsEnabled)
                return false;

            // Reset fan speed
            SetFanLevel(new byte[] { Byte.MaxValue, Byte.MaxValue } );

            // Disable manual fan mode
            if(Config.FanLevelNeedManual)
                Platform.Fans.SetManual(false);

            // Restore the previous fan mode
            UpdateFanMode(true, this.LastFanMode);

            // Restore the previous GPU power settings
            UpdateGpuPower(true, this.LastGpuPowerData);

            // Set the state flags
            this.IsAlternate = false;
            this.IsEnabled = false;
            this.IsSuspended = false;

            // Reset data
            Reset();

            // Update the status
            Status(Severity.Notice, Config.Locale.Get(Config.L_PROG + "End"));

            // Report success
            return true;

        }

        // Updates the fan program, if any is running
        public bool Update() {

            // Fail if no program active
            // or program is suspended
            if(!this.IsEnabled || this.IsSuspended)
                return false;

            // Re-arm the BIOS countdown first, before anything that can be slow.
            //
            // The countdown at EC 0x63 hands the fans to the firmware if it is not
            // refreshed within FanCountdownExtendInterval. Measured lapse, 2026-09-06
            // 21:29:29: the CPU fan fell from 4400 rpm (level 44) to 1500 rpm (level 15)
            // while the CPU was at 90 °C, which the curve would never ask for; the CPU
            // reached 94 °C and only recovered at 21:29:44, the moment the countdown read
            // 120 again and the level jumped back to 45.
            //
            // Two things made that possible. The refresh used to be the *last* step of
            // this pass, behind every sensor read, the level write, the mode check and
            // the GPU power update — and each EC operation in front of it can retry for
            // EcMutexTotalTimeout before giving up. And the call was guarded on
            // FanProgramModeCheckFirst, which is false in the shipped config, so
            // UpdateCountdown() was never reached at all: the only thing refreshing the
            // countdown was the side effect of UpdateFanMode's forced write. One dropped
            // write and the watchdog was simply not fed, with nothing to notice.
            //
            // So it goes first, it goes unconditionally, and it verifies. Raising
            // FanCountdownExtendThreshold and the monitor thread's priority did not
            // prevent the second lapse because neither addressed either half of this.
            UpdateCountdown(true);

            // Read individual CPU and GPU temperatures.
            // GetCpuTemperature updates all sensors once; GetGpuTemperature
            // re-reads the cached values without a second hardware pass.
            byte cpuTemp = Platform.GetCpuTemperature(true);
            byte gpuTemp = Platform.GetGpuTemperature(false);

            // When the CPU temperature cannot be trusted, keep control and raise the
            // floor. Do NOT hand the fans back to the firmware.
            //
            // Handing back was tried and rejected. The reasoning was sound on paper —
            // thinkpad-acpi's fan watchdog re-enables firmware control on expiry "to make
            // sure the fan is never left set to an unsafe level because of userspace
            // problems", Framework's EC has autofan for the same reason, NBFC's porting
            // guide tells config authors to find the register value that returns control
            // to the EC firmware. But on this board the firmware's idea of automatic is
            // to stop the fans entirely, and it let the CPU reach 94 °C under sustained
            // all-core load while we watched. Silence is not a safe default here.
            //
            // Why the reading is lost at all: the die goes through the cross-process
            // Access_PCI mutex, and anything else polling the same AMD SMN register —
            // Core Temp, HWiNFO, Ryzen Master — can make a read fail. Measured: the die
            // reading died mid-session, the curve then ran on EC 0x57 (firmware string
            // data, plausible-looking and unable to rise), and the CPU passed 90 °C with
            // the fans at their 1700 rpm floor. PawnIoAmd now retries and reopens, so
            // this should be rare; what follows is for when it happens anyway.
            //
            // The response is the mirror of the GPU one. There the held value can only be
            // too high, so the fan eases down to a floor. Here we have no temperature at
            // all and the machine may be heating, so the floor walks *up* — one step per
            // tick towards the level the active curve would use at UntrustedTargetC. That
            // is audible within a few seconds, reaches a genuinely protective speed inside
            // a minute, and drops straight back to the curve the moment a real reading
            // returns. It never parks the fans and never goes silent.
            if(!Platform.IsCpuTemperatureTrusted) {

                if(++this.untrustedTicks >= UntrustedTicksBeforeFloor && !this.cpuUntrusted) {
                    this.cpuUntrusted = true;
                    Status(Severity.Important,
                        "No trustworthy CPU temperature - raising the fan floor, curve suspended");
                    Config.ErrorLog("FanProgram.CpuUntrusted", null,
                        "CPU temperature untrusted for " + this.untrustedTicks
                            + " ticks; holding fan control and walking the floor up towards the "
                            + UntrustedTargetC + " °C row (fans are NOT handed to the firmware)");
                }

            } else if(this.cpuUntrusted || this.untrustedTicks > 0) {

                if(this.cpuUntrusted) {
                    this.cpuUntrusted = false;
                    this.cpuUntrustedLevel = 0;
                    Status(Severity.Notice, "CPU temperature recovered - resuming the curve");
                    Config.ErrorLog("FanProgram.CpuUntrusted", null, "CPU temperature recovered, resuming the curve");
                }
                this.untrustedTicks = 0;

            }

            // GPU-temperature fallback for boards without a real GPU temp sensor.
            // The two cases we need to distinguish when gpuTemp == 0:
            //   (a) discrete GPU exists but is currently powered off (issue #66) —
            //       want the GPU fan to stay at the idle row of the curve, NOT
            //       ramp up under CPU load.
            //   (b) board has no usable GPU temp sensor at all (issue #62) — want
            //       the GPU fan to track CPU temp so it never sits idle while
            //       the system actually needs cooling.
            // Platform.HasObservedGpuTemperature() returns true once GPTM has
            // produced a single non-zero reading since startup, which is the
            // honest discriminator: a real sensor latches the flag true on its
            // first valid sample, after which subsequent zero readings are
            // unambiguously the GPU-powered-off case (a) and the fallback stays
            // skipped. Without the flag ever latching, case (b) holds and the
            // fallback fires. This is the corrected version of the v1.4.2 check
            // — the original `HasGpuTemperatureSensor()` matched against the
            // configured sensor list, which the default OmenMon.xml always
            // populated with "GPTM" regardless of hardware (Copilot review #1
            // on the v1.4.2 PR).
            if(gpuTemp == 0 && !this.Platform.HasObservedGpuTemperature()) gpuTemp = cpuTemp;

            // Independent level lookups: each fan reacts to its own
            // component's temperature instead of the global maximum.
            byte cpuLevel = GetTemperatureLevel(cpuTemp);
            byte gpuLevel = GetTemperatureLevel(gpuTemp);

            // Assemble the fan speed array: CPU fan speed from the CPU-temp
            // level row, GPU fan speed from the GPU-temp level row.
            byte[] cpuFans = GetFanLevel(cpuLevel);
            byte[] gpuFans = GetFanLevel(gpuLevel);
            byte[] fans = new byte[] { cpuFans[0], gpuFans[1] };

            // The CPU floor from above, walked up one step per tick. Applied here rather
            // than earlier so it can never lower what the curve asked for — if the GPU
            // side or a still-valid reading wants more air, that wins.
            if(this.cpuUntrusted) {

                byte target = GetFanLevel(GetTemperatureLevel(UntrustedTargetC))[0];
                if(this.cpuUntrustedLevel == 0)
                    this.cpuUntrustedLevel = fans[0];
                if(this.cpuUntrustedLevel < target)
                    this.cpuUntrustedLevel += UntrustedRampStep;
                if(this.cpuUntrustedLevel > target)
                    this.cpuUntrustedLevel = target;

                if(fans[0] < this.cpuUntrustedLevel)
                    fans[0] = (byte) this.cpuUntrustedLevel;

            }

            // Second line of defence for the GPU, for the case where the die reading is
            // lost and cannot be recovered (Nvml rebuilds the session on the tick it goes
            // stale, so getting here means even that failed).
            //
            // Platform holds the last die value rather than falling back to GPTM, so the
            // curve above is running on a number that was true a moment ago and is drifting
            // out of date. Freezing the fans there would be wrong in the other direction —
            // if it happened at the end of a load the GPU would be held loud for nothing.
            //
            // So: don't hold, and don't drop. Wind down one step — 100 rpm — per tick
            // towards a floor that still moves real air, and stop there until a
            // trustworthy reading comes back. The GPU finishes cooling on the way down,
            // and the floor means even a permanently dead sensor leaves the card
            // ventilated instead of silently unattended.
            //
            // Strictly downward, never upward. A parked GPU at idle is indistinguishable
            // from a lost sensor from in here, and an earlier version of this treated the
            // floor as a minimum — which spun an idle machine's GPU fan up to 2500 rpm for
            // no reason at all. Platform's grace period makes that state rare; this makes
            // it harmless when it happens anyway.
            if(!Platform.IsGpuTemperatureTrusted) {

                if(this.gpuUntrustedLevel == 0) {

                    // Enter at whatever the fan is doing right now, and never above it.
                    // This is a wind-down, not a boost: if the reading is lost while the
                    // fan is already at its idle floor, the floor is where it stays.
                    // Getting this wrong once made an idle machine spin up to 2500 rpm
                    // because a parked GPU looks exactly like a lost sensor.
                    this.gpuUntrustedLevel = fans[1];
                    this.gpuUntrustedTarget = Math.Min((int) fans[1], GpuUntrustedFloor);

                    if(this.gpuUntrustedLevel > this.gpuUntrustedTarget) {
                        Status(Severity.Important,
                            "GPU temperature untrusted - easing the GPU fan down to "
                                + this.gpuUntrustedTarget * 100 + " rpm");
                        Config.ErrorLog("FanProgram.GpuUntrusted", null,
                            "NVML die reading lost for " + UntrustedGpuGraceDescription
                                + "; ramping the GPU fan from " + this.gpuUntrustedLevel * 100
                                + " rpm towards " + this.gpuUntrustedTarget * 100 + " rpm");
                    } else {
                        Config.ErrorLog("FanProgram.GpuUntrusted", null,
                            "NVML die reading lost; GPU fan held at its current "
                                + this.gpuUntrustedLevel * 100 + " rpm");
                    }

                }

                if(this.gpuUntrustedLevel > this.gpuUntrustedTarget)
                    this.gpuUntrustedLevel -= UntrustedRampStep;
                if(this.gpuUntrustedLevel < this.gpuUntrustedTarget)
                    this.gpuUntrustedLevel = this.gpuUntrustedTarget;

                // Never below what the curve already asked for: if the CPU side or a
                // stale-but-hot held value wants more air, it gets it.
                if(fans[1] < this.gpuUntrustedLevel)
                    fans[1] = (byte) this.gpuUntrustedLevel;

            } else if(this.gpuUntrustedLevel != 0) {

                this.gpuUntrustedLevel = 0;
                this.gpuUntrustedTarget = 0;
                Status(Severity.Notice, "GPU temperature recovered - resuming the GPU curve");

            }

            // Keep GetMaxTemperature populated for thermal-panic and tray icon
            // (sensors are already up-to-date, no extra EC reads)
            Platform.GetMaxTemperature(false);

            // Report both temperatures so the user can see independent tracking
            Status(Severity.Notice,
                Config.Locale.Get(Config.L_PROG + "T") + " "
                + "CPU " + Conv.GetString(cpuTemp, 2, 10) + Config.Locale.Get(Config.L_UNIT + "Temperature")
                + " GPU " + Conv.GetString(gpuTemp, 2, 10) + Config.Locale.Get(Config.L_UNIT + "Temperature") + " "
                + Config.Locale.Get(Config.L_PROG + "Fans") + " "
                + Conv.GetString(fans[0], 2, 10) + ", " + Conv.GetString(fans[1], 2, 10));

            // Slow the way down, never the way up
            EaseDown(fans);

            // Set fan levels
            SetFanLevel(fans);

            // Perform other updates, only if necessary
            // or, in case of the fan mode, configured to do so
            // without checking, so as to reduce the EC burden
            UpdateFanMode(!Config.FanProgramModeCheckFirst);
            UpdateGpuPower();

            // Report success
            return true;

        }
#endregion

#region Private Methods
        // Obtains the fan levels for the given temperature level
        private byte[] GetFanLevel(byte level) {

            // Retrieve the value from the configuration data
            return Config.FanProgram[this.Name].Level[level];

        }

        // Obtains the program level for the given temperature
        private byte GetTemperatureLevel(byte temperature) {
            int value;

            // Binary-search the level list
            // If the result is non-negative, an index was found
            if((value = Levels.BinarySearch(temperature)) >= 0)

                // Return the item at the index directly
                return this.Levels[value];

            // The result is a bitwise complement of the next larger item index,
            // or the index of the last element of the list if no larger item exists
            else {

                // Return the item at the binary complement index less one,
                // clamped to a minimum of 0 to avoid OutOfBoundsException if the
                // temperature is lower than the lowest defined threshold in the curve.
                int index = ~value - 1;
                return this.Levels[index < 0 ? 0 : index];

            }

        }

        // Resets the state data when terminating a program
        private void Reset() {

            // Clear the GPU power data
            this.GpuPowerData = default(BiosData.GpuPowerData);

            // Clear the last fan mode
            this.LastFanMode = BiosData.FanMode.Default;

            // Note: do not clear the last program name since leaving it
            // is harmless, and also can be used to show a more meaningful
            // status message using GetName() even after the program ended

            // Clear the level keys
            this.Levels = new List<byte>();

        }

        // Set the fan levels to the given parameter
        private void SetFanLevel(byte[] level) {

            // Clear a stale fan-off latch before writing levels. On some boards
            // (e.g. 8D07 — issue #39) the GPU fan stays parked because a prior
            // SetOff(true) is still in effect; SetLevels would then succeed for
            // the CPU fan but silently no-op for the GPU. Only clear when the
            // latch is actually set so we don't trample on intentional state
            // managed by the user / GUI in between program ticks.
            // Deliberately do NOT clear the "Max Fan" latch here — that is the
            // safety override used by Thermal Panic, and clearing it on every
            // tick would defeat it (GuiOp.CheckThermalPanic asserts max only on
            // the transition into panic, not on every refresh).
            try {
                if(this.Platform.Fans.GetOff())
                    this.Platform.Fans.SetOff(false);
            } catch { }

            // Set the fan levels
            this.Platform.Fans.SetLevels(level);

            // Note: this does not currently handle the case when both fans are being set to 0
            // since this is not allowed by the hardware through this call, and would depend
            // on calling Fans.SetOff(true) instead; not implemented due to possible long-term
            // implications of the fans being switched off for extended periods of time

        }

        // Sets up a new fan program to be run
        private bool Setup(string name) {

            // Bail out if referring to a non-existent program
            if(!Config.FanProgram.ContainsKey(name))
                return false;

            // Set up the program name
            this.Name = name;

            // Set up the level keys
            this.Levels = new List<byte>(Config.FanProgram[this.Name].Level.Keys);

            // Set up the target GPU power data
            this.GpuPowerData = new BiosData.GpuPowerData(Config.FanProgram[this.Name].GpuPower);

            // Report success
            return true;

        }

        // Reports fan program status
        private void Status(Severity severity, string message) {

            // Report status via the callback method
            Callback(severity, message);

        }

        // Rate-limits downward fan changes, and only downward.
        //
        // A fan curve is a step function with no hysteresis, so a temperature crossing a
        // threshold moves the fan the whole distance between two rows at once. Falling
        // that abruptly is audible and unlike anything the hardware does on its own; a
        // laptop coming off load winds down over a minute or so, it does not drop from
        // 5500 to 1700 rpm between one tick and the next.
        //
        // Rising is deliberately not limited. Getting air to a hot part late is the
        // failure this program exists to prevent, and on 2026-09-07 at 19:26 the CPU went
        // from 78 °C to 92 in a single twelve-second gap. Anything that delays a rise is
        // dangerous; delaying a fall costs nothing but a few seconds of extra noise.
        //
        // Measured in levels per second rather than per tick, because the tick is not
        // reliable: under a rendering load the monitor thread has been observed 11 to 27
        // seconds late. A per-tick step would make the wind-down take a quarter of an hour
        // exactly when the machine is busy, which is the opposite of what is wanted.
        private void EaseDown(byte[] fans) {

            DateTime now = DateTime.UtcNow;

            if(this.easeAt == DateTime.MinValue) {
                this.easeAt = now;
                for(int i = 0; i < fans.Length && i < this.easeLevel.Length; i++)
                    this.easeLevel[i] = fans[i];
                return;
            }

            double seconds = (now - this.easeAt).TotalSeconds;
            this.easeAt = now;

            // Never negative, and never so large after a long stall that the limit stops
            // limiting: a gap of a minute should still wind down smoothly, not teleport
            if(seconds < 0) seconds = 0;
            int maxDrop = (int) Math.Ceiling(seconds * DescendLevelsPerSecond);
            if(maxDrop < 1) maxDrop = 1;

            for(int i = 0; i < fans.Length && i < this.easeLevel.Length; i++) {

                int target = fans[i];
                int last = this.easeLevel[i];

                // Up immediately, down at the limited rate
                int applied = target >= last ? target : Math.Max(target, last - maxDrop);

                this.easeLevel[i] = applied;
                fans[i] = (byte) applied;

            }

        }

        // One level — 100 rpm — every two seconds. Max to idle, 5500 to 1700 rpm, is then
        // a little over a minute, which is about what the firmware's own wind-down sounds
        // like and slow enough that no single step is audible as a step.
        private const double DescendLevelsPerSecond = 0.5;

        // Last level actually commanded per fan, and when — the state EaseDown works from
        private readonly int[] easeLevel = new int[PlatformData.FanCount];
        private DateTime easeAt = DateTime.MinValue;

        // What this program last told the hardware to do, so a reading can be checked
        // against an intention rather than against the previous reading.
        //
        // Borrowed from nbfc-linux, which compares the fan's current speed with its target
        // and re-applies its register writes when they diverge by more than 15 % — it
        // treats a mismatch as evidence the EC did not take the write. SetFanLevel here is
        // unconditional, so the re-assertion happens on every tick anyway; what was
        // missing was noticing, and saying so.
        public byte[] GetCommandedLevels() {
            byte[] copy = new byte[this.easeLevel.Length];
            for(int i = 0; i < copy.Length; i++)
                copy[i] = (byte) (this.easeLevel[i] < 0 ? 0
                    : this.easeLevel[i] > 255 ? 255 : this.easeLevel[i]);
            return copy;
        }

        // Whether a level has ever been commanded — before that, GetCommandedLevels() is
        // all zeroes and comparing against it would report a divergence that is really
        // just startup
        public bool HasCommandedLevels {
            get { return this.easeAt != DateTime.MinValue; }
        }

        // Updates the fan manual mode countdown, optionally only if necessary.
        // Returns true if the countdown was refreshed, or did not need to be.
        public bool UpdateCountdown(bool forceUpdate = false, bool skipZero = false) {
            int countdown = 1;

            // Avoid unnecessarily querying the countdown,
            // if an update is being forced regardless
            if(!forceUpdate)
                countdown = this.Platform.Fans.GetCountdown();

            // Optionally, do not update if no countdown
            if(skipZero && countdown == 0)
                return true;

            // Skip if there still is enough time to do it
            // during the next update, unless forced not to
            if(!forceUpdate
                && countdown
                    >= (Config.UpdateProgramInterval
                        + Config.FanCountdownExtendThreshold))
                return true;

            // Write the countdown, and check that the write actually landed.
            //
            // Hw.EcExec drops a write on the floor when it cannot take Global\Access_EC
            // within EcMutexTotalTimeout: the callback never runs, EcSetByte returns
            // void, and the caller is told nothing. For an ordinary sensor that is a
            // missing sample. For this register it is a loss of fan control roughly
            // FanCountdownExtendInterval later, which is how the CPU reached 94 °C on
            // 2026-09-06 with no error logged anywhere.
            //
            // Hw.EcLockTimeoutCount counts exactly those dropped operations, so a delta
            // across the write says whether it happened without costing an extra read.
            // SetCountdown's own read-back can also time out and inflate the delta, which
            // makes this err towards a retry rather than towards a false success — the
            // right way round for a watchdog, and the write is idempotent.
            for(int attempt = 1; attempt <= CountdownWriteAttempts; attempt++) {

                int timeouts = Hw.EcLockTimeoutCount;
                this.Platform.Fans.SetCountdown(Config.FanCountdownExtendInterval);

                if(Hw.EcLockTimeoutCount == timeouts) {
                    if(this.countdownWriteFailures > 0) {
                        Config.ErrorLog("FanProgram.Countdown", null,
                            "countdown re-armed after " + this.countdownWriteFailures
                                + " failed attempt(s); fan control was never handed over");
                        this.countdownWriteFailures = 0;
                    }
                    return true;
                }

            }

            // Every attempt was dropped. Say so: the fans are now on a timer that nothing
            // is feeding, and the next pass may not get the lock either.
            this.countdownWriteFailures += CountdownWriteAttempts;
            Status(Severity.Important,
                "Could not re-arm the fan countdown - the firmware may take the fans");
            Config.ErrorLog("FanProgram.Countdown", null,
                "could not write the fan countdown in " + CountdownWriteAttempts
                    + " attempts (" + this.countdownWriteFailures + " consecutive failures); "
                    + "the firmware takes the fans when it expires");

            return false;

        }

        // Updates the fan mode, optionally only if different than the desired target state
        private void UpdateFanMode(
            bool forceUpdate = false,
            BiosData.FanMode? mode = null) {

            // Set the default mode if empty
            if(mode == null)
                mode = Config.FanProgram[this.Name].FanMode;

            // Skip if the settings are the same already, unless forced not to
            if(forceUpdate
                || this.Platform.Fans.GetMode() != mode)

                // Set the fan mode
                this.Platform.Fans.SetMode((BiosData.FanMode) mode);

        }

        // Updates the GPU power, optionally only if different than the desired target state
        private void UpdateGpuPower(
            bool forceUpdate = false,
            BiosData.GpuPowerData? power = null) {

            // Set the default GPU power data if empty
            if(power == null)
                power = this.GpuPowerData;

            // Skip if the settings are the same already, unless forced not to
            if(forceUpdate
                || this.Platform.System.GetGpuCustomTgp() != this.GpuPowerData.CustomTgp
                || this.Platform.System.GetGpuPpab() != this.GpuPowerData.Ppab)

                // Set the GPU power
                this.Platform.System.SetGpuPower((BiosData.GpuPowerData) power);

        }

#endregion
    }

}
