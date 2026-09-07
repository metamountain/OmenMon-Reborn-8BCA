  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023-2024 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// Nvml — the GPU's own temperature sensor, via NVIDIA's management library.
//
// Why this exists: the EC sensor GPTM (0xB7) does not track this GPU. Measured on
// board 8BCA with an RTX 4070 Laptop under a sustained 78 W WebGL load, the die went
// 34 °C -> 67 °C while GPTM moved 26 °C -> 28 °C. It is not merely damped; it barely
// responds at all.
//
// That is more dangerous than the CPU sensor problem was. Fan programs run on
// max(CPU, GPU), so a GPU temperature that cannot rise contributes nothing to the
// decision — during that test the fans sat at the 1700 rpm Silent floor while the GPU
// passed 67 °C, because the only sensor that moved was the CPU at 48 °C, still under
// the profile's 52 °C threshold.
//
// nvml.dll ships with the NVIDIA driver and lives in System32. It is the same library
// nvidia-smi uses, needs no kernel driver and no elevation, and reports the die
// temperature the GPU itself acts on. Everything here is best-effort: on a machine
// with no NVIDIA GPU, or with the library missing, IsAvailable stays false and callers
// fall back to the EC sensor.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OmenMon.Driver {

    public static class Nvml {

        private const string DllName = "nvml.dll";

        // NVML_TEMPERATURE_GPU — the die sensor
        private const uint SensorGpu = 0;

        // NVML_SUCCESS
        private const int Success = 0;

        private static IntPtr device = IntPtr.Zero;
        private static bool initialized;
        private static bool tried;
        private static string status = "not initialized";
        private static string deviceName = "";

        public static bool IsAvailable => initialized && device != IntPtr.Zero;
        public static string GetStatus() => status;
        public static string GetDeviceName() => deviceName;

#region P/Invoke
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string fileName);

        [DllImport(DllName, ExactSpelling = true)]
        private static extern int nvmlInit_v2();

        [DllImport(DllName, ExactSpelling = true)]
        private static extern int nvmlShutdown();

        [DllImport(DllName, ExactSpelling = true)]
        private static extern int nvmlDeviceGetCount_v2(out uint count);

        [DllImport(DllName, ExactSpelling = true)]
        private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

        [DllImport(DllName, ExactSpelling = true)]
        private static extern int nvmlDeviceGetTemperature(IntPtr device, uint sensorType, out uint temp);

        // The limit actually in force, in milliwatts — what the board will let the GPU
        // draw right now, after cTGP and PPAB have been applied. Read rather than derived
        // from those two flags: the flags say which knobs are on, not what they came to.
        //
        // It moves: 80 000 with the GPU parked, 105 000 with Dynamic Boost available. That
        // is why it is reported alongside the default below rather than on its own — a
        // single "the limit" is a question with two right answers depending on when it is
        // asked, and showing one of them makes the other look wrong.
        [DllImport(DllName, ExactSpelling = true)]
        private static extern int nvmlDeviceGetEnforcedPowerLimit(IntPtr device, out uint milliwatts);

        // The board's configured TGP, without Dynamic Boost. This one does not move, so
        // it is the half of the pair that can always be stated.
        [DllImport(DllName, ExactSpelling = true)]
        private static extern int nvmlDeviceGetPowerManagementDefaultLimit(IntPtr device, out uint milliwatts);

        [StructLayout(LayoutKind.Sequential)]
        private struct Utilization { public uint Gpu; public uint Memory; }

        // The honest one. See TryGetGpuTemperature().
        [DllImport(DllName, ExactSpelling = true)]
        private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization util);

        [DllImport(DllName, ExactSpelling = true, CharSet = CharSet.Ansi, BestFitMapping = false)]
        private static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);
#endregion

        public static void Open() {

            if(tried)
                return;
            tried = true;

            // Load by absolute path rather than trusting the search order: the driver
            // installs into System32, but older layouts also put a copy under
            // Program Files\NVIDIA Corporation\NVSMI
            if(LocateLibrary() == null) {
                status = "nvml.dll not found (no NVIDIA driver installed?)";
                return;
            }

            int hr;
            try {
                hr = nvmlInit_v2();
            } catch(Exception e) {
                status = "nvmlInit threw: " + e.Message;
                return;
            }
            if(hr != Success) {
                status = "nvmlInit_v2 returned " + hr;
                return;
            }
            initialized = true;

            uint count;
            if(nvmlDeviceGetCount_v2(out count) != Success || count == 0) {
                status = "NVML reports no devices";
                Close();
                return;
            }

            // Index 0 is the only GPU on a laptop like this one
            IntPtr handle;
            if(nvmlDeviceGetHandleByIndex_v2(0, out handle) != Success || handle == IntPtr.Zero) {
                status = "nvmlDeviceGetHandleByIndex_v2 failed";
                Close();
                return;
            }
            device = handle;

            try {
                StringBuilder name = new StringBuilder(96);
                if(nvmlDeviceGetName(device, name, (uint) name.Capacity) == Success)
                    deviceName = name.ToString();
            } catch { }

            // Prove the sensor answers before advertising availability, the same way
            // PawnIoAmd does — a library that loads but reads nothing is worse than none
            int probe;
            if(!TryGetGpuTemperature(out probe)) {
                status = "NVML loaded but the temperature read failed";
                Close();
            } else if(probe <= 0 || probe > 125) {
                status = "NVML returned an implausible " + probe + " °C";
                Close();
            } else {
                status = "NVML ready" + (deviceName.Length > 0 ? " (" + deviceName + ")" : "");
            }

            // Record the outcome either way. Whether the GPU temperature is coming from
            // the die or from the EC sensor that was measured to be wrong is exactly the
            // thing worth knowing after a deploy, and it is invisible from the window.
            OmenMon.Library.Config.ErrorLog("Nvml.Open", null, status);

        }

        public static void Close() {
            device = IntPtr.Zero;
            if(initialized) {
                try { nvmlShutdown(); } catch { }
                initialized = false;
            }
        }

        // Set while the session is known stale, for diagnostics and for the fan side
        private static bool stale;
        private static int staleCount;
        private static int reinitCount;
        private static DateTime lastReinit = DateTime.MinValue;

        // Last reading that came from a live session, for deciding how urgently a stale
        // one needs rebuilding
        private static int lastLive;

        // Above this, a stale session has to be rebuilt now: either it froze at a load
        // value, or a load has started and the session has not noticed. Below it, the
        // GPU is parked and cold and there is no thermal question to answer.
        //
        // Idle here measures 41-44 °C and the frozen values measured 60-69 °C, so 55
        // separates them with room on both sides.
        private const int ColdHoldC = 55;

        // Two rebuild rates, because a stale session is the *normal* idle state on a
        // hybrid-graphics laptop — the dGPU parks whenever nothing needs it, so rebuilding
        // on every stale read would mean tearing the session down every couple of seconds
        // forever, and each rebuild briefly wakes the GPU.
        //
        // Warm: rebuild almost immediately, this is the case that matters.
        // Cold: rebuild once a minute anyway, because a stale-and-cold session cannot tell
        // us whether the GPU is still parked or has woken up and started heating without
        // the session noticing. Sixty seconds bounds that blind spot; it is the one real
        // gap left in this scheme and it is deliberate rather than overlooked.
        private const int ReinitCooldownWarmSec = 10;
        private const int ReinitCooldownColdSec = 60;

        // Current GPU die temperature in whole degrees Celsius.
        // Opens on first use; Open() returns immediately once tried, so the probe it
        // runs cannot recurse back into here.
        //
        // Guards against a stale session, which on this laptop is not a corner case but
        // the normal end of every GPU load. Hybrid graphics parks the discrete GPU as soon
        // as nothing needs it, and an NVML session that was open across that transition
        // does not notice: nvmlDeviceGetTemperature keeps returning the last value it
        // recorded, with NVML_SUCCESS, for as long as the session lives. Measured on this
        // machine over five cycles — 10 s of load, 60 s of silence — the reading froze in
        // four of them, 18-27 °C above the truth, and never recovered across 150
        // consecutive polls. It reported 76 °C in the field while the die sat at 38 °C.
        //
        // An earlier guard here counted repeated identical values and distrusted a run of
        // them below 60 °C. It could never fire: the stale values are the last ones seen
        // under load, so they are 60-69 °C — above its own exception. Worse, the premise
        // was wrong; a frozen reading is not detectable from the temperature at all, since
        // a genuinely pinned GPU produces exactly the same sequence.
        //
        // The detector is nvmlDeviceGetUtilizationRates. It is the one field that reports
        // the condition honestly instead of serving a cached value: in the stale state it
        // returns an error (999, NVML_ERROR_UNKNOWN) while temperature returns 0 with a
        // wrong number, power returns 590 W, and the clock reads 1320 against a true 1980
        // MHz. Note that it is the return code that matters and not the utilisation value
        // — a parked GPU and a stale session both report 0 %, so the value distinguishes
        // nothing.
        //
        // Recovery is nvmlShutdown + nvmlInit_v2 in-process. Confirmed twice, immediately
        // and exactly: 41 against a true 41, 44 against a true 44. No external process is
        // needed — notably an existing session cannot wake the GPU, which is why polling
        // harder never helped and why running nvidia-smi appeared to "fix" it.
        //
        // Failing all that, this returns false rather than a number, and the fan side
        // decides what to do about it — see FanProgram. Note that "returns false" is the
        // ordinary idle state here, not an alarm: a parked GPU has a stale session by
        // definition, which is why the caller applies a long grace period before treating
        // it as a lost sensor.
        // The GPU power limit currently in force, in whole watts. Reports what the board
        // will actually allow, which is the number worth showing next to the CPU's: on
        // this machine it reads 80 W with cTGP and PPAB off and 140 W with them on, and
        // the fan profile is what sets them — so the figure moves with the profile, not
        // with the Windows power mode beside it.
        // Both halves of the answer: the configured TGP, and the ceiling with Dynamic
        // Boost. Either may be zero if that particular call fails; the caller decides
        // what it can say with what it got.
        //
        // Reporting one number was the mistake this replaces. "The limit" read 80 W with
        // the GPU parked and 105 W with it awake, and both were true — so whichever was
        // shown made the other look like a bug, and holding the last one read just froze
        // an arbitrary moment.
        public static bool TryGetGpuPowerLimits(out int baseWatts, out int boostWatts) {

            baseWatts = 0; boostWatts = 0;

            if(!tried)
                Open();
            if(device == IntPtr.Zero)
                return false;

            try {

                // Through the same liveness check as the temperature, and for the same
                // reason: a stale session answers every field from what it last recorded,
                // with NVML_SUCCESS. Power is one of the fields measured doing exactly
                // that — 590 W against a real 12.3 W.
                if(!IsSessionLive()) {
                    staleCount++;
                    stale = true;
                    if(!TryReinit() || !IsSessionLive())
                        return false;
                }
                stale = false;

                uint mw;
                if(nvmlDeviceGetPowerManagementDefaultLimit(device, out mw) == Success)
                    baseWatts = Sane(mw);
                if(nvmlDeviceGetEnforcedPowerLimit(device, out mw) == Success)
                    boostWatts = Sane(mw);

                return baseWatts > 0 || boostWatts > 0;

            } catch {
                return false;
            }
        }

        private static int Sane(uint milliwatts) {
            int w = (int) ((milliwatts + 500) / 1000);
            return w > 0 && w <= 400 ? w : 0;
        }

        public static bool TryGetGpuTemperature(out int celsius) {
            celsius = 0;
            if(!tried)
                Open();
            if(device == IntPtr.Zero)
                return false;
            try {

                if(!IsSessionLive()) {

                    staleCount++;
                    stale = true;

                    if(!TryReinit() || !IsSessionLive())
                        return false;

                    // Only worth a log line when it was the urgent kind. The cold-path
                    // rebuild happens once a minute for the life of the process and would
                    // otherwise fill the log with an event that means nothing.
                    if(lastLive >= ColdHoldC)
                        OmenMon.Library.Config.ErrorLog("Nvml.Stale", null,
                            "GPU session went stale at " + lastLive + " °C (utilisation read"
                            + " failed); re-initialized - stale " + staleCount + "x, re-init "
                            + reinitCount + "x");

                }
                stale = false;

                uint temp;
                if(nvmlDeviceGetTemperature(device, SensorGpu, out temp) != Success)
                    return false;

                int value = (int) temp;
                if(value <= 0 || value > 125)
                    return false;

                lastLive = value;
                celsius = value;
                return true;

            } catch {
                return false;
            }
        }

        // Whether the session is answering for real, rather than replaying what it last
        // saw. See TryGetGpuTemperature() for why this particular call is the test.
        private static bool IsSessionLive() {
            try {
                Utilization util;
                return nvmlDeviceGetUtilizationRates(device, out util) == Success;
            } catch {
                return false;
            }
        }

        // Tear the session down and build a new one. Keeps `tried` set so Open() stays
        // out of the way — the handle is re-acquired here directly.
        private static bool TryReinit() {

            int cooldown = lastLive >= ColdHoldC ? ReinitCooldownWarmSec : ReinitCooldownColdSec;
            if((DateTime.UtcNow - lastReinit).TotalSeconds < cooldown)
                return false;
            lastReinit = DateTime.UtcNow;
            reinitCount++;

            try {
                device = IntPtr.Zero;
                if(initialized) {
                    try { nvmlShutdown(); } catch { }
                    initialized = false;
                }

                if(nvmlInit_v2() != Success)
                    return false;
                initialized = true;

                IntPtr handle;
                if(nvmlDeviceGetHandleByIndex_v2(0, out handle) != Success || handle == IntPtr.Zero)
                    return false;
                device = handle;
                return true;

            } catch {
                return false;
            }
        }

        // Whether the last read found the session stale — for diagnostics and for the fan
        // side, which holds airflow up while this is true
        public static bool IsLikelyStale {
            get { return stale; }
        }

        // How often the session has gone stale and been rebuilt, for the status line
        public static int GetStaleCount() { return staleCount; }
        public static int GetReinitCount() { return reinitCount; }

        private static string LocateLibrary() {
            try {
                string[] candidates = {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), DllName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "NVIDIA Corporation", "NVSMI", DllName)
                };
                foreach(string c in candidates)
                    if(!string.IsNullOrEmpty(c) && File.Exists(c) && LoadLibraryW(c) != IntPtr.Zero)
                        return c;
            } catch { }
            return null;
        }

    }

}
