  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023-2024 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// PawnIoAmd — second PawnIO module, for the CPU's own temperature sensor.
//
// Why this exists: on board 8BCA every EC and WMI CPU-temperature source returns
// firmware string data instead of a register (EC 0x57 CPUT and the WMI
// GetTemperature call both produce ASCII punctuation: 0x2C ',' reads as "44 °C"),
// so OmenMon has no trustworthy CPU temperature on this machine. The CPU itself
// still knows: Zen publishes THM_TCON_CUR_TMP on the SMN (System Management
// Network), reached through PCI configuration indices 0x60 / 0x64.
//
// OmenMon's LpcACPIEC module cannot get there — it whitelists the ACPI EC ports
// 0x62/0x66 and nothing else. namazso's AMDFamily17 module, from the same signed
// release, exports ioctl_read_smn, which can. Per Resources/PAWN_BUILD.md the way
// to extend the kernel-mode surface is exactly this: open a second handle and load
// an additional officially-signed module, so the driver stays Microsoft-signed and
// Defender stays quiet.
//
// Everything here is optional and best-effort. If PawnIO is missing, the module
// fails to load, or the CPU is not AMD, IsAvailable stays false and callers fall
// back to whatever they used before.

using System;
using System.IO;
using System.Reflection;

namespace OmenMon.Driver {

    public static class PawnIoAmd {

        // Export of namazso's AMDFamily17 module: in = 1 ulong (SMN offset),
        // out = 1 ulong (register value)
        private const string FnReadSmn = "ioctl_read_smn";

        private const string EmbeddedBlobName = "OmenMon.AMDFamily17.bin";
        private const string SideBySideBlobName = "AMDFamily17.bin";

        // Zen temperature register and its decoding, as in LibreHardwareMonitor's
        // Amd17Cpu: bits 31:21 hold the reading in 1/8 °C steps, and either select
        // bit means the scale is shifted down by 49 °C.
        //
        // Verified on this machine (Ryzen 9 7945HS, board 8BCA) against Core Temp:
        //
        //     raw 0x530B0000 -> 34.00 °C     raw 0x4E4B0000 -> 29.25 °C
        //     (Core Temp read 28 °C at the same moment as the second sample)
        //
        // Two things worth keeping: the raw values fall by 0x04C00000 per 4.75 °C,
        // i.e. 2^24 per degree, which confirms the bits 31:21 / 0.125 °C scale; and
        // RANGE_SEL really is set here (0x0B in the third byte -> bit 19), so the
        // -49 °C shift applies. Skip it and the CPU reads 83 °C sitting idle.
        private const uint SmnThmTconCurTmp = 0x00059800;
        private const uint TempRangeSelMask = 0x00080000;
        private const uint TempTjSelMask    = 0x00030000;

        private static IntPtr handle = IntPtr.Zero;
        private static bool tried;
        private static string status = "not initialized";

        public static bool IsAvailable => handle != IntPtr.Zero;
        public static string GetStatus() => status;

        // Opens a handle of its own rather than sharing the EC one: a PawnIO handle
        // carries a single loaded module
        public static void Open() {

            if(tried)
                return;
            tried = true;

            // Only AMD has an SMN to read
            if(!IsAmd()) {
                status = "not an AMD processor";
                return;
            }

            string libPath = PawnIo.LocatePawnIoLib();
            if(libPath == null) {
                status = "PawnIOLib.dll not found (install PawnIO from https://pawnio.eu/)";
                return;
            }
            if(PawnIo.LoadLibraryW(libPath) == IntPtr.Zero) {
                status = "failed to load " + libPath;
                return;
            }

            byte[] blob = LoadModuleBlob();
            if(blob == null) {
                status = "AMDFamily17.bin not found (embedded resource " + EmbeddedBlobName
                    + ", or place it next to OmenMon.exe)";
                return;
            }

            IntPtr h;
            int hr;
            try {
                hr = PawnIo.pawnio_open(out h);
            } catch(Exception e) {
                status = "pawnio_open threw: " + e.Message;
                return;
            }
            if(hr != 0 || h == IntPtr.Zero) {
                status = string.Format("pawnio_open returned 0x{0:X8}"
                    + (hr == unchecked((int) 0x80070005) ? " (access denied — run as administrator)" : ""), hr);
                return;
            }

            hr = PawnIo.pawnio_load(h, blob, (IntPtr) blob.Length);
            if(hr != 0) {
                status = string.Format("pawnio_load(AMDFamily17) returned 0x{0:X8}", hr);
                try { PawnIo.pawnio_close(h); } catch { }
                return;
            }

            handle = h;
            status = "AMDFamily17 loaded";

            // Prove the register actually answers before advertising availability:
            // a module that loads but reads back nothing is worse than no module
            double probe;
            if(!TryGetCpuTemperature(out probe)) {
                status = "AMDFamily17 loaded but " + FnReadSmn + " failed";
                Close();
            } else if(probe <= 0.0 || probe > 125.0) {
                status = string.Format("AMDFamily17 loaded but returned an implausible {0:F1} °C", probe);
                Close();
            }

            // Record the outcome. Whether the CPU temperature is the die or a fallback is
            // exactly what needs to be known after a deploy, and it is invisible from the
            // window — a failed die read looked like a healthy 34 °C idle while the CPU
            // was actually above 90.
            OmenMon.Library.Config.ErrorLog("PawnIoAmd.Open", null, status);

        }

        public static void Close() {
            if(handle != IntPtr.Zero) {
                try { PawnIo.pawnio_close(handle); } catch { }
                handle = IntPtr.Zero;
            }
        }

        // Reads one SMN register
        public static bool TryReadSmn(uint offset, out uint value) {
            value = 0;
            if(handle == IntPtr.Zero)
                return false;
            try {
                ulong[] inArray = new ulong[] { offset };
                ulong[] outArray = new ulong[1];
                IntPtr returned;
                int hr = PawnIo.pawnio_execute(
                    handle, FnReadSmn,
                    inArray, (IntPtr) inArray.Length,
                    outArray, (IntPtr) outArray.Length,
                    out returned);
                if(hr != 0)
                    return false;
                value = (uint) outArray[0];
                return true;
            } catch {
                return false;
            }
        }

        // Current Tctl/Tdie in degrees Celsius.
        // Opens on first use — Open() is idempotent and returns immediately once tried,
        // so the probe it runs cannot recurse back into here
        // Consecutive failed reads, and when the module was last reopened
        private static int failCount;
        private static DateTime lastReopen = DateTime.MinValue;

        public static bool TryGetCpuTemperature(out double celsius) {
            celsius = 0.0;
            if(!tried)
                Open();

            uint raw;
            if(!TryReadSmn(SmnThmTconCurTmp, out raw)) {

                // Recover instead of latching dead. Open() sets tried = true on its first
                // attempt and never runs again, so a single failed read used to disable
                // the die sensor for the whole life of the process — and the caller then
                // fell back to a sensor that cannot rise. Observed exactly that: the
                // reading worked at 20:52, failed once, and stayed at a fabricated 34 °C
                // through a burn that had the CPU above 90.
                //
                // A read goes through the cross-process Access_PCI mutex, so a failure is
                // usually contention, not a broken module — transient by nature, and worth
                // retrying. Reopening is rate-limited so a genuinely dead module is not
                // hammered once per tick.
                if(++failCount >= 3 && (DateTime.UtcNow - lastReopen).TotalSeconds >= 30) {
                    lastReopen = DateTime.UtcNow;
                    failCount = 0;
                    Close();
                    tried = false;
                    Open();
                    if(!TryReadSmn(SmnThmTconCurTmp, out raw))
                        return false;
                } else {
                    return false;
                }
            }
            failCount = 0;

            // A register that reads back all-zeroes or all-ones is not a temperature
            if(raw == 0 || raw == 0xFFFFFFFF)
                return false;

            bool offsetFlag = (raw & TempRangeSelMask) != 0
                || (raw & TempTjSelMask) == TempTjSelMask;
            celsius = ((raw >> 21) * 125) / 1000.0;
            if(offsetFlag)
                celsius -= 49.0;
            return true;
        }

        private static bool IsAmd() {
            try {
                string id = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
                return id != null && id.IndexOf("AuthenticAMD", StringComparison.OrdinalIgnoreCase) >= 0;
            } catch {
                return false;
            }
        }

        // Side-by-side file wins over the embedded copy, matching how PawnIo.cs
        // loads LpcACPIEC — it lets a user drop in a newer signed module without
        // a rebuild
        private static byte[] LoadModuleBlob() {
            try {
                string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if(!string.IsNullOrEmpty(exeDir)) {
                    string side = Path.Combine(exeDir, SideBySideBlobName);
                    if(File.Exists(side))
                        return File.ReadAllBytes(side);
                }
            } catch { }

            try {
                using(Stream stream = typeof(PawnIoAmd).Assembly
                        .GetManifestResourceStream(EmbeddedBlobName)) {
                    if(stream == null)
                        return null;
                    using(MemoryStream ms = new MemoryStream()) {
                        stream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            } catch {
                return null;
            }
        }

    }

}
