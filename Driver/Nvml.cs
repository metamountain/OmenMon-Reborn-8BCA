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

        // Consecutive reads that returned the identical value, for the staleness check
        private static int lastValue = -1;
        private static int repeatCount;

        // A reading repeated this many times running is treated as stale. At roughly one
        // sample a second that is about two minutes of a temperature that has not moved
        // by even a degree — which a live sensor, even an idle one, does not do.
        private const int StaleRepeatLimit = 120;

        // Never suspect a reading at or above this. See TryGetGpuTemperature().
        private const int StaleAlwaysTrustC = 60;

        // Current GPU die temperature in whole degrees Celsius.
        // Opens on first use; Open() returns immediately once tried, so the probe it
        // runs cannot recurse back into here.
        //
        // Guards against a stale value. This laptop is hybrid-graphics: when nothing needs
        // the discrete GPU, rendering moves to the AMD integrated one and the dGPU powers
        // down — and NVML then keeps returning the last temperature it recorded before
        // that, with NVML_SUCCESS, indefinitely. Observed in the field as a frozen 76 °C,
        // the peak from a load test that had finished minutes earlier, while the real die
        // sat at 38 °C. Running nvidia-smi woke the GPU and produced one correct reading
        // before it froze again, which is what made this so confusing to pin down.
        //
        // The guard can only ever reject a reading, never invent one, and rejection falls
        // back to the previous behaviour — so it cannot make anything worse than it was.
        //
        // The one case that would be dangerous is a GPU genuinely pinned at a constant
        // temperature under sustained load, where discarding the reading would drop the
        // fans exactly when they are needed. Hence StaleAlwaysTrustC: anything at or above
        // 60 °C is trusted unconditionally, however long it has been steady. A stale value
        // is only ever the last one seen before the GPU went to sleep, and below 60 °C
        // discarding it costs nothing.
        public static bool TryGetGpuTemperature(out int celsius) {
            celsius = 0;
            if(!tried)
                Open();
            if(device == IntPtr.Zero)
                return false;
            try {
                uint temp;
                if(nvmlDeviceGetTemperature(device, SensorGpu, out temp) != Success)
                    return false;

                int value = (int) temp;
                if(value == lastValue) {
                    if(repeatCount < int.MaxValue)
                        repeatCount++;
                } else {
                    lastValue = value;
                    repeatCount = 0;
                }

                if(value < StaleAlwaysTrustC && repeatCount >= StaleRepeatLimit)
                    return false;

                celsius = value;
                return true;
            } catch {
                return false;
            }
        }

        // Whether the last reading looked stale — for diagnostics
        public static bool IsLikelyStale {
            get { return lastValue < StaleAlwaysTrustC && repeatCount >= StaleRepeatLimit; }
        }

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
