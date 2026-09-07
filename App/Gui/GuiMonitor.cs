  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023-2024 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy

using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using OmenMon.Hardware.Bios;
using OmenMon.Hardware.Platform;
using OmenMon.Library;

namespace OmenMon.AppGui {

#region Snapshot
    // Immutable snapshot of every sensor/state value the GUI renders, produced by the
    // background monitor thread (GuiMonitor) and consumed lock-free by the WinForms UI
    // thread. The whole point of v1.4.6 (#98): the UI thread NEVER touches the EC/BIOS
    // during passive monitoring — it only reads the most recently published snapshot,
    // so the message pump can never stall behind a 200 ms EC mutex wait or a Thread.Sleep
    // backoff. Reference assignment is atomic, so publication is a single volatile write
    // and readers always observe a fully-built, internally consistent object.
    public sealed class MonitorSnapshot {

        // Fan readings (indexed by PlatformData.FanCount: 0 = CPU, 1 = GPU)
        public readonly int[] FanSpeed;
        public readonly int[] FanLevel;
        public readonly int[] FanRate;
        public int Countdown;
        public BiosData.FanMode Mode;
        public bool Max;
        public bool Off;

        // Temperatures (indexed like Platform.Temperature)
        public int[] TempValue;
        public PlatformData.ValueTrend[] TempTrend;
        public int MaxTemp;
        public int CpuTemp;  // CPUT (or BIOS fallback) — for the tray tooltip
        public int GpuTemp;  // GPTM — for the tray tooltip

        // System information (only sampled when HasFanSys, i.e. the form is open)
        public string Manufacturer = "";
        public string Product = "";
        public string Version = "";
        public string BornDate = "";
        public byte CpuPl4;
        public bool IsFullPower;
        public BiosData.AdapterStatus AdapterStatus;
        public BiosData.GpuMode GpuMode;
        public BiosData.GpuDState GpuDState;
        public BiosData.GpuCustomTgp GpuCustomTgp;
        public BiosData.GpuPpab GpuPpab;
        public BiosData.GpuPowerData GpuPower;
        public BiosData.Throttling Throttling;

        // Fan-program / thermal-panic / power state
        public bool ProgramEnabled;
        public bool ProgramAlternate;
        public string ProgramName = "";
        public bool IsThermalPanic;
        public bool IsFullPowerConfirmed;

        // True when the heavy fan + system fields above hold real data — either sampled
        // this pass (full sample: form open or an explicit RequestSample, e.g. the tray
        // menu opening) or carried forward from the last full sample. Light passes skip
        // those reads to keep the EC/BIOS footprint identical to the legacy "only read
        // what's shown" behaviour (the spurious-read avoidance that keeps OmenMon from
        // fighting HP firmware power management), but still republish the previous
        // values so consumers like the tray menus always see the best known state.
        public bool HasFanSys;

        public MonitorSnapshot() {
            this.FanSpeed = new int[PlatformData.FanCount];
            this.FanLevel = new int[PlatformData.FanCount];
            this.FanRate  = new int[PlatformData.FanCount];
        }

    }
#endregion

#region UI message queue
    // A piece of UI work the monitor thread wants performed, drained and executed on the
    // WinForms UI thread by GuiTray's timer tick. The monitor must never touch a WinForms
    // control (NotifyIcon, the main form) directly — doing so off-thread throws
    // InvalidOperationException. This mirrors the existing "raise a signal, let the UI
    // tick act on it" pattern already used for PowerModeChanged (GuiTray.PendingPowerEventSignal).
    public sealed class MonitorUiMessage {
        public enum MessageKind { Balloon, ProgramStatus }
        public MessageKind Kind;
        public string Text;
        public string Title;
        public ToolTipIcon Icon;
        public FanProgram.Severity Severity;
    }
#endregion

    // Owns ALL periodic hardware access for the GUI: sensor sampling, the fan-program
    // tick, the thermal-panic safety net, constant-speed countdown maintenance and the
    // BIOS heartbeat. Runs on a dedicated background thread so none of this can block the
    // UI thread. Every control sequence (here and in user-initiated UI handlers) is
    // serialised by GuiOp.HardwareLock so the multi-step fan/thermal/EC-write sequences
    // never interleave across threads; raw EC port I/O stays serialised by the existing
    // cross-process Global\Access_EC mutex.
    public sealed class GuiMonitor {

#region Data
        private readonly GuiTray Context;

        private Thread Worker;
        private readonly AutoResetEvent Wake = new AutoResetEvent(false);
        private volatile bool Running;
        private volatile bool Paused;

        // Held by the monitor thread for the duration of every Pass(). Pause() takes it
        // once (empty-bodied) after setting the flag, so by the time Pause() returns any
        // in-flight pass — including its fan-program and thermal-panic writes — has fully
        // unwound and the caller (the Auto-Calibration wizard) owns the hardware alone.
        private readonly object PassGate = new object();

        // Set (off-thread, by the UI AC-flicker machine) to ask the monitor to run
        // Op.PowerChange() under the lock on its next pass, so the fan-program switch and
        // its EC/BIOS traffic never run on the UI thread.
        private volatile bool PowerApplyPending;

        // Set (off-thread, by the UI when the tray menu opens) to make the next pass
        // take one full sample regardless of the form-visibility gate, so the menu
        // sections render fresh fan/GPU state without any hardware I/O on the UI thread.
        private volatile bool FullSamplePending;

        // Published snapshot — single volatile reference, atomic swap.
        private volatile MonitorSnapshot CurrentSnapshot;
        public MonitorSnapshot Current { get { return this.CurrentSnapshot; } }

        // UI-message hand-off (monitor → UI thread)
        private readonly object QueueLock = new object();
        private readonly Queue<MonitorUiMessage> Queue = new Queue<MonitorUiMessage>();

        // Scheduling counters — only ever touched on the monitor thread
        private int MonitorTick;
        private int IconTick;
        private int ProgramTick;
        private int HeartbeatAccumMs;
#endregion

#region Construction & lifecycle
        public GuiMonitor(GuiTray context) {
            this.Context = context;
        }

        // Starts the background monitor thread
        public void Start() {
            if(this.Worker != null)
                return;
            // Prime the schedule counters to their thresholds so the very first pass
            // samples immediately — the tray tooltip and dynamic icon then show live
            // values within one timer interval of startup instead of one full update
            // interval later (matching the legacy synchronous first Update()).
            this.MonitorTick = Config.UpdateMonitorInterval;
            this.IconTick = Config.UpdateIconInterval;
            this.ProgramTick = Config.UpdateProgramInterval;
            this.Running = true;
            this.Worker = new Thread(Loop) {
                IsBackground = true,
                Name = "OmenMonMonitor",

                // Above normal, because this thread re-arms a hardware watchdog.
                //
                // The BIOS countdown at EC 0x63 hands the fans back to the firmware if it
                // is not refreshed, so this loop missing its slot is not a dropped sample
                // — it is a loss of fan control. Under a full CPU load, EC operations slow
                // sharply (Ec.cs backs off with Thread.Sleep(0) yields, which against a
                // machine full of CPU-bound processes take far longer in wall time), and a
                // pass that should take milliseconds can take seconds. Measured: with 16
                // busy processes the countdown lapsed and the firmware took the fans at
                // 78 °C.
                //
                // The thread sleeps almost all the time, so the priority costs nothing;
                // it only ensures the few milliseconds of work per second actually happen
                // when the machine is busy. Same reasoning as the thermal-panic check
                // living on this thread.
                Priority = ThreadPriority.AboveNormal
            };
            this.Worker.Start();
        }

        // Stops the monitor thread and waits briefly for it to unwind
        public void Stop() {
            this.Running = false;
            this.Wake.Set();
            try {
                if(this.Worker != null)
                    this.Worker.Join(2000);
            } catch { }
            this.Worker = null;
        }

        // Suspends periodic sampling and control. Used by the Auto-Calibration wizard so
        // its RPM measurements and fan-control writes are not perturbed by a concurrent
        // monitor pass. Blocks until any in-flight pass has unwound (PassGate barrier),
        // so after this returns the monitor is guaranteed quiescent — only the BIOS
        // keep-alive heartbeat continues, which is WMI-only and does not touch the EC.
        public void Pause() {
            this.Paused = true;
            lock(this.PassGate) { }
        }
        public void Resume() {
            this.Paused = false;
            this.Wake.Set();
        }

        // Asks the monitor to apply a debounced AC power-state change on its next pass
        // (off the UI thread). Called by the GuiTray AC-flicker state machine.
        public void RequestPowerApply() {
            this.PowerApplyPending = true;
            this.Wake.Set();
        }

        // Asks the monitor for one immediate full sample (off the UI thread). Called
        // when the tray menu opens: by the time the user reaches a submenu, the
        // published snapshot reflects current hardware, and the menu render itself
        // never blocks on the EC mutex (issue #98).
        public void RequestSample() {
            this.FullSamplePending = true;
            this.Wake.Set();
        }

        // Delays the next fan-program tick by a full UpdateProgramInterval. Called from
        // the UI thread right after a user manually starts a program, which has already
        // applied the curve — the periodic re-application can wait a whole interval
        // (mirrors the legacy UpdateProgramTick reset). The unsynchronised write can at
        // worst lose one concurrent increment, shifting the next tick by one timer beat.
        public void ResetProgramTick() {
            this.ProgramTick = 0;
        }
#endregion

#region UI message hand-off
        // Enqueued by the monitor thread; drained by the UI thread.
        public void EnqueueBalloon(string text, string title = null, ToolTipIcon icon = ToolTipIcon.None) {
            lock(this.QueueLock)
                this.Queue.Enqueue(new MonitorUiMessage {
                    Kind = MonitorUiMessage.MessageKind.Balloon,
                    Text = text, Title = title, Icon = icon });
        }

        public void EnqueueProgramStatus(FanProgram.Severity severity, string message) {
            lock(this.QueueLock)
                this.Queue.Enqueue(new MonitorUiMessage {
                    Kind = MonitorUiMessage.MessageKind.ProgramStatus,
                    Severity = severity, Text = message });
        }

        // Drains pending UI messages — MUST run on the UI thread (GuiTray timer tick).
        public void DrainUiMessages() {
            while(true) {
                MonitorUiMessage msg;
                lock(this.QueueLock) {
                    if(this.Queue.Count == 0)
                        return;
                    msg = this.Queue.Dequeue();
                }
                try {
                    Context.HandleMonitorUiMessage(msg);
                } catch { }
            }
        }
#endregion

#region Sampling
        // Builds a fresh snapshot. MUST be called while holding Op.HardwareLock so the
        // multi-step reads cannot interleave with a concurrent control sequence. When
        // full == false (form hidden) only the values the tray icon/tooltip need are read.
        // tempsFresh == true skips the sensor sweep because the caller just refreshed it
        // (the fan-program tick reads all sensors via GetCpuTemperature(true)).
        private MonitorSnapshot Sample(bool full, bool tempsFresh = false) {

            Platform plat = Context.Op.Platform;
            GuiOp op = Context.Op;
            MonitorSnapshot s = new MonitorSnapshot();

            // Temperatures — UpdateTemperature batches every sensor read under a single
            // EC open + mutex hold (Hw.EcExecBatch), then GetValue()/GetValueTrend() are
            // cheap cached reads.
            if(!tempsFresh)
                plat.UpdateTemperature();
            int n = plat.Temperature.Length;
            s.TempValue = new int[n];
            s.TempTrend = new PlatformData.ValueTrend[n];
            int cpu = 0, gpu = 0, bios = 0;
            for(int i = 0; i < n; i++) {
                string name = plat.Temperature[i].GetName();
                int val = plat.Temperature[i].GetValue();
                s.TempValue[i] = val;
                s.TempTrend[i] = plat.Temperature[i].GetValueTrend();

                // Honour the per-sensor Use flag from OmenMon.xml. Without this a
                // sensor disabled in the configuration still fed CpuTemp/GpuTemp —
                // which on 8BCA meant EC CPUT (firmware string data, values in the
                // ASCII punctuation range: 0x36=54, 0x2B=43, 0x2A=42 …) kept winning
                // the max() below and the reported CPU temperature was uncorrelated
                // with the real die temperature.
                if(i < plat.TemperatureUse.Length && !plat.TemperatureUse[i])
                    continue;

                if(name == "CPUT" && val > 0) cpu = val;
                else if(name == "GPTM" && val > 0) gpu = val;
                else if(name == "BIOS" && val > 0) bios = val;
            }
            // The per-sensor values above are for the individual sensor readout only.
            // The three headline temperatures come from the platform accessors, which is
            // where the die/NVML overrides now live.
            //
            // They used to be applied here instead, and that was the bug: FanProgram
            // calls Platform.GetCpuTemperature()/GetGpuTemperature() directly and never
            // sees this snapshot, so the corrected temperatures reached the window and
            // the telemetry CSV while the fan curves, the thermal-panic check and the
            // tray icon all still ran on the EC sensors that were measured to be wrong.
            // Reading the same accessors here keeps the display honest about what the
            // fans are actually acting on — if the two ever disagree again, the display
            // is no longer the place it would show up.
            s.CpuTemp = plat.GetCpuTemperature(false); // sensors already refreshed above
            s.GpuTemp = plat.GetGpuTemperature(false);
            s.MaxTemp = plat.GetMaxTemperature(false);

            // The dynamic-icon warm/cool background needs the fan mode even with the form
            // hidden, so it is always sampled.
            s.Mode = plat.Fans.GetMode();

            // Cheap state (no EC except the BIOS-on-battery branch of IsFullPowerConfirmed)
            s.ProgramEnabled   = op.Program.IsEnabled;
            s.ProgramAlternate = op.Program.IsAlternate;
            s.ProgramName      = op.Program.GetName();
            s.IsThermalPanic   = op.IsThermalPanic;
            s.IsFullPower      = plat.System.IsFullPower();
            s.IsFullPowerConfirmed = plat.System.IsFullPowerConfirmed();

            if(full) {
                for(int i = 0; i < PlatformData.FanCount; i++) {
                    s.FanSpeed[i] = plat.Fans.Fan[i].GetSpeed();
                    s.FanLevel[i] = plat.Fans.Fan[i].GetLevel();
                    s.FanRate[i]  = plat.Fans.Fan[i].GetRate();
                }
                s.Countdown = plat.Fans.GetCountdown();
                s.Max = plat.Fans.GetMax();
                s.Off = plat.Fans.GetOff();

                // Bridge EC dropouts before anything downstream sees them.
                //
                // Three HP processes on this machine take Global\Access_EC —
                // HPSystemEventUtilityHost, OmenCap, HPSystemEventUtilityBackground — and
                // a pass that loses the race returns 0 for everything in it rather than
                // failing. Measured at idle: rpm held a steady 1500 while the level column
                // read 0 on most ticks, with the countdown and power fields blinking to 0
                // in the same rows. Nothing was wrong with the fans; the reads simply did
                // not happen.
                //
                // The telemetry CSV already bridged this for rpm (LogSane) — but only for
                // the CSV, so the window and the tray still showed the raw zero, which
                // the fan-speed readout rendered as "auto". Bridging here fixes all three
                // at once, and the CSV's own bridge becomes a second line rather than the
                // only one.
                //
                // Guarded on s.Off: when the fans really have been switched off, zero is
                // the truth and must show.
                MonitorSnapshot last = this.CurrentSnapshot;
                if(last != null && last.HasFanSys && !s.Off) {
                    for(int i = 0; i < PlatformData.FanCount; i++) {
                        if(s.FanSpeed[i] == 0 && last.FanSpeed[i] > 0) s.FanSpeed[i] = last.FanSpeed[i];
                        if(s.FanLevel[i] == 0 && last.FanLevel[i] > 0) s.FanLevel[i] = last.FanLevel[i];
                        if(s.FanRate[i]  == 0 && last.FanRate[i]  > 0) s.FanRate[i]  = last.FanRate[i];
                    }
                    if(s.Countdown == 0 && last.Countdown > 0) s.Countdown = last.Countdown;
                }

                ISettings sys = plat.System;
                s.Manufacturer = sys.GetManufacturer();
                s.Product      = sys.GetProduct();
                s.Version      = sys.GetVersion();
                s.BornDate     = sys.GetBornDate();
                s.CpuPl4       = sys.GetDefaultCpuPowerLimit4();
                // Adapter status is only rendered while on AC — keep the read footprint
                // identical to UpdateSys (one BIOS call, and only on full power).
                s.AdapterStatus = s.IsFullPower ? sys.GetAdapterStatus() : default(BiosData.AdapterStatus);
                s.GpuMode      = sys.GetGpuMode(true);
                s.GpuDState    = sys.GetGpuDState(true);   // forces one fresh GpuPower read…
                s.GpuCustomTgp = sys.GetGpuCustomTgp();    // …reused by these two
                s.GpuPpab      = sys.GetGpuPpab();
                s.GpuPower     = sys.GetGpuPower();        // …and cached for the GPU menu
                s.Throttling   = sys.GetThrottling();
                s.HasFanSys = true;

                // Rolling diagnostic CSV — one row per full pass. Sensor dropouts are
                // bridged with the last plausible value so the log is analysable; a
                // logged 0 is indistinguishable from a genuinely cold sensor otherwise.
                try {
                    TelemetryLog.Write(
                        LogSane(s.CpuTemp, 1, 120, ref logCpuT),
                        LogSane(s.GpuTemp, 1, 120, ref logGpuT),
                        LogSane(s.MaxTemp, 1, 120, ref logMaxT),
                        LogSane(s.FanSpeed[0], 1, 9000, ref logCpuR),
                        LogSane(s.FanSpeed[1], 1, 9000, ref logGpuR),
                        s.FanLevel[0], s.FanLevel[1],
                        s.FanRate[0], s.FanRate[1],
                        s.Mode.ToString(), s.ProgramName, s.Countdown);
                } catch { }

            } else {

                // Light pass: no fan/system reads, but republish the previous full
                // sample's values so the tray menus (which render Max/Off/GpuMode
                // between full passes) keep seeing the best known state instead of
                // zeroed defaults.
                MonitorSnapshot prev = this.CurrentSnapshot;
                if(prev != null && prev.HasFanSys) {
                    for(int i = 0; i < PlatformData.FanCount; i++) {
                        s.FanSpeed[i] = prev.FanSpeed[i];
                        s.FanLevel[i] = prev.FanLevel[i];
                        s.FanRate[i]  = prev.FanRate[i];
                    }
                    s.Countdown     = prev.Countdown;
                    s.Max           = prev.Max;
                    s.Off           = prev.Off;
                    s.Manufacturer  = prev.Manufacturer;
                    s.Product       = prev.Product;
                    s.Version       = prev.Version;
                    s.BornDate      = prev.BornDate;
                    s.CpuPl4        = prev.CpuPl4;
                    s.AdapterStatus = prev.AdapterStatus;
                    s.GpuMode       = prev.GpuMode;
                    s.GpuDState     = prev.GpuDState;
                    s.GpuCustomTgp  = prev.GpuCustomTgp;
                    s.GpuPpab       = prev.GpuPpab;
                    s.GpuPower      = prev.GpuPower;
                    s.Throttling    = prev.Throttling;
                    s.HasFanSys = true;
                }

            }

            return s;
        }

        // Synchronous sample + publish, for the UI thread to call when it needs the
        // display to reflect hardware *now* (form opening, immediately after a user
        // action). Runs the read pass on the calling thread — acceptable because it is
        // user-initiated and bounded, unlike the per-tick passive polling this class
        // removes from the UI thread. Re-entrant against Op.HardwareLock.
        // Last plausible logged values, used to bridge sensor dropouts in the CSV
        private int logCpuT, logGpuT, logMaxT, logCpuR, logGpuR;

        // Background telemetry cadence, in monitor loop iterations (~1 s each).
        //
        // 30 was too coarse to record what the machine does. Measured: the GPU shed 35 °C
        // in the minute after a load stopped, and the log caught it in a single row —
        // 74 °C, then 39 °C, with nothing between. The reading was correct at both ends
        // and the fall was real, but a history that samples twice across a whole thermal
        // transient cannot show a curve, only a cliff, and cannot be used to judge how
        // the fans responded either.
        //
        // 8 s costs one CSV line every eight seconds — nothing, next to a log that
        // rotates at 4 MB — and is fine enough that a cooldown reads as a slope.
        private const int TelemetryEvery = 8;
        private int TelemetryTick;


        private static int LogSane(int v, int lo, int hi, ref int last) {
            if(v >= lo && v <= hi) { last = v; return v; }
            return last;
        }

        public MonitorSnapshot SampleNow(bool full = true) {
            // A bulk sensor refresh is always recoverable: if the EC mutex is held by
            // someone else (HP services, the ACPI driver, our own monitor thread) the
            // right answer is to keep the previous snapshot for one cycle, not to throw
            // a modal "failed to acquire embedded controller exclusive lock" box at the
            // user. Genuine single-shot user writes (fan set, keyboard colour) stay loud.
            bool wasQuiet = Hw.EcLockQuiet;
            Hw.EcLockQuiet = true;
            try {
                lock(Context.Op.HardwareLock) {
                    MonitorSnapshot s = Sample(full);
                    this.CurrentSnapshot = s;
                    return s;
                }
            } finally {
                Hw.EcLockQuiet = wasQuiet;
            }
        }
#endregion

#region Loop
        private void Loop() {

            // Periodic EC traffic on this thread is recoverable by design — a mutex
            // timeout means "skip this tick, keep the last good value", never a modal
            // error box (issue #94: one box per startup / several per calibration, and
            // raised from this thread it would also stall the thermal-panic safety net
            // until dismissed).
            Hw.EcLockQuiet = true;

            while(this.Running) {
                try {
                    // The Paused check sits INSIDE the gate: Pause() sets the flag and
                    // then takes the gate once, so after it returns every subsequent
                    // gate acquisition here observes Paused == true — checking outside
                    // left a window where a pass could still start after Pause()
                    // returned and race the calibration's fan writes.
                    bool ran = false;
                    lock(this.PassGate)
                        if(!this.Paused) {
                            Pass();
                            ran = true;
                        }
                    if(!ran)
                        // Calibration owns the fans/EC while paused, but the BIOS
                        // keep-alive must go on: it is WMI-only, does not touch the EC,
                        // and Performance Control would otherwise lapse during a long
                        // wizard run (the legacy heartbeat timer also kept firing).
                        TickHeartbeat();
                } catch { }
                // Sleep until the next tick, a wake (shutdown / resume / power-apply /
                // immediate-sample request), whichever comes first.
                this.Wake.WaitOne(Config.GuiTimerInterval);
            }
        }

        // One periodic pass. Mirrors the legacy GuiTray.Update() scheduling (the same
        // UpdateProgram/Monitor/Icon intervals and visibility/dynamic-icon/panic gates),
        // but every hardware step now runs here on the background thread under the lock.
        private void Pass() {

            GuiOp op = Context.Op;

            // Apply a debounced AC power-state change the UI machine asked us to handle.
            if(this.PowerApplyPending) {
                this.PowerApplyPending = false;
                lock(op.HardwareLock) {
                    op.PowerChange();
                }
            }

            bool formVisible = Context.FormVisible;
            // Mirror Icon.IsDynamic without touching the (UI-owned) GuiIcon object.
            bool dynamicIcon = Config.GuiDynamicIcon;
            bool panicActive = Config.ThermalPanicEnabled || op.IsThermalPanic;

            // Fan-program tick (or countdown extend) — every UpdateProgramInterval.
            // A program update reads every sensor (GetCpuTemperature(true)), so the
            // sample below can reuse that sweep instead of issuing a second one.
            bool programRefreshedTemps = false;
            if(++this.ProgramTick >= Config.UpdateProgramInterval) {
                this.ProgramTick = 0;
                lock(op.HardwareLock) {
                    if(op.Program.IsEnabled)
                        programRefreshedTemps = op.Program.Update();
                    else if(Config.FanCountdownExtendAlways)
                        op.Program.UpdateCountdown(false, true);
                }
            }

            // Constant-speed countdown maintenance — replaces the RdoFanConst-gated logic
            // that used to live in GuiFormMain.UpdateFan (it ran on the UI thread). The
            // form now just sets Op.ConstantSpeedActive.
            if(op.ConstantSpeedActive) {
                lock(op.HardwareLock) {
                    int countdown = op.Platform.Fans.GetCountdown();
                    if(countdown < Config.UpdateMonitorInterval + Config.FanCountdownExtendThreshold) {
                        op.Platform.Fans.SetMode(op.Platform.Fans.GetMode());

                        // Through UpdateCountdown rather than SetCountdown directly, so a
                        // constant speed is defended by the same verify-and-retry as a fan
                        // program. A dropped write here has the same consequence — the
                        // firmware takes the fans when the countdown expires — and used to
                        // be just as silent.
                        op.Program.UpdateCountdown(true);
                    }
                }
            }

            // Sensor sampling + snapshot publication + thermal-panic safety net.
            bool monitorFire = (++this.MonitorTick >= Config.UpdateMonitorInterval);
            if(monitorFire) this.MonitorTick = 0;
            bool iconFire = (++this.IconTick >= Config.UpdateIconInterval);
            if(iconFire) this.IconTick = 0;

            // A pending menu-open request forces one full sample this pass.
            bool fullRequest = this.FullSamplePending;
            if(fullRequest)
                this.FullSamplePending = false;

            // Telemetry heartbeat: a full sample every TelemetryEvery seconds even with
            // the window closed, so the diagnostic log is continuous. Full samples are
            // otherwise gated on form visibility to keep the hardware-access footprint
            // small, so this runs an order of magnitude slower than the on-screen
            // refresh — enough for a temperature/rpm history, cheap enough not to add
            // meaningful EC-mutex contention.
            bool telemetryFire = TelemetryLog.Enabled
                && (++this.TelemetryTick >= TelemetryEvery);
            if(telemetryFire)
                this.TelemetryTick = 0;

            bool sample = fullRequest
                || (monitorFire && formVisible)
                || telemetryFire
                || (iconFire && (dynamicIcon || panicActive));
            if(sample) {
                lock(op.HardwareLock) {
                    MonitorSnapshot s = Sample(
                        formVisible || fullRequest || telemetryFire, programRefreshedTemps);
                    // Overtemperature protection runs on a fresh, hardware-verified reading
                    // whenever it is enabled (or still latched and needs clearing) —
                    // unchanged from the legacy icon-tick behaviour, just off the UI thread.
                    // MaxTemp is sourced from GetMaxTemperature() (a byte), so the cast is safe.
                    op.CheckThermalPanic((byte) s.MaxTemp);
                    this.CurrentSnapshot = s;
                }
            }

            TickHeartbeat();

        }

        // BIOS heartbeat — keeps Performance Control alive. Paused on battery to avoid
        // HP firmware triggering an unexpected hibernate (folds in the old HeartbeatTimer).
        // Runs from Pass() and, during a calibration pause, directly from Loop().
        private void TickHeartbeat() {
            if(Config.BiosHeartbeatInterval <= 0)
                return;
            this.HeartbeatAccumMs += Config.GuiTimerInterval;
            if(this.HeartbeatAccumMs >= Config.BiosHeartbeatInterval) {
                this.HeartbeatAccumMs = 0;
                lock(Context.Op.HardwareLock) {
                    if(!Config.BiosHeartbeatPauseOnBattery
                        || Context.Op.Platform.System.IsFullPowerConfirmed())
                        try { BiosCtl.Instance.GetFanCount(); } catch { }
                }
            }
        }
#endregion

    }

}
