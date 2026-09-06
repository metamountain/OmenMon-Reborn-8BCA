  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// TelemetryLog — rolling CSV of CPU/GPU temperature + fan speed for diagnosis.
// One line per full monitor pass, appended to OmenMon-telemetry.csv next to the
// executable (falls back to %LOCALAPPDATA%). The file self-rotates once it passes
// ~4 MB (current -> OmenMon-telemetry.prev.csv) so it never grows unbounded.

using System;
using System.IO;
using System.Text;

namespace OmenMon.Library {

    public static class TelemetryLog {

        private const long MaxBytes = 4L * 1024 * 1024;
        private const string Header =
            "time,cpu_c,gpu_c,max_c,cpu_rpm,gpu_rpm,cpu_lvl,gpu_lvl,cpu_pct,gpu_pct,mode,program,countdown_s";

        private static readonly object gate = new object();
        private static string path;
        private static bool resolved;

        // On by default; set the env var OMENMON_NOTELEMETRY to any value to disable.
        private static readonly bool enabled =
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OMENMON_NOTELEMETRY"));
        public static bool Enabled { get { return enabled; } }

        private static string Path() {
            if(resolved) return path;
            resolved = true;
            try {
                string p = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "OmenMon-telemetry.csv");
                File.AppendAllText(p, string.Empty);   // writability probe
                path = p;
            } catch {
                try {
                    string dir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmenMon");
                    Directory.CreateDirectory(dir);
                    path = System.IO.Path.Combine(dir, "OmenMon-telemetry.csv");
                } catch { path = null; }
            }
            return path;
        }

        // Append one sample. Safe to call from the monitor thread every pass.
        public static void Write(
            int cpuC, int gpuC, int maxC,
            int cpuRpm, int gpuRpm, int cpuLvl, int gpuLvl, int cpuPct, int gpuPct,
            string mode, string program, int countdown) {

            if(!Enabled) return;
            string p = Path();
            if(p == null) return;

            try {
                lock(gate) {
                    bool fresh = !File.Exists(p) || new FileInfo(p).Length == 0;
                    if(!fresh && new FileInfo(p).Length > MaxBytes) {
                        string prev = p.Replace(".csv", ".prev.csv");
                        try { if(File.Exists(prev)) File.Delete(prev); File.Move(p, prev); } catch { }
                        fresh = true;
                    }
                    var sb = new StringBuilder(160);
                    if(fresh) sb.Append(Header).Append('\n');
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(',')
                      .Append(cpuC).Append(',').Append(gpuC).Append(',').Append(maxC).Append(',')
                      .Append(cpuRpm).Append(',').Append(gpuRpm).Append(',')
                      .Append(cpuLvl).Append(',').Append(gpuLvl).Append(',')
                      .Append(cpuPct).Append(',').Append(gpuPct).Append(',')
                      .Append(mode ?? "").Append(',').Append(program ?? "").Append(',')
                      .Append(countdown).Append('\n');
                    File.AppendAllText(p, sb.ToString());
                }
            } catch { /* diagnostics must never break monitoring */ }
        }
    }
}
