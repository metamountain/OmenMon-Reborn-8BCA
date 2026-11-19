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
            "time,cpu_c,gpu_c,max_c,cpu_rpm,gpu_rpm,cpu_lvl,gpu_lvl,cpu_pct,gpu_pct,mode,program,countdown_s,cpu_cmd,gpu_cmd,rms_dbfs,peak_hz";

        private static readonly object gate = new object();
        private static string path;
        private static bool resolved;
        private static bool headerChecked;

        // On by default; set the env var OMENMON_NOTELEMETRY to any value to disable.
        private static readonly bool enabled =
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OMENMON_NOTELEMETRY"));
        public static bool Enabled { get { return enabled; } }

        // Microphone level, folded into the main log during the test phase.
        //
        // Why this exists: the 20:46 and 21:05 dropouts were indistinguishable here — both
        // printed 0/0 — and only the audio channel separated them. One showed a real
        // spin-up ramp, the other no acoustic change at all. So the level has to sit on
        // the same line as the fan data, not in a file that has to be joined afterwards.
        //
        // Why a drop-file and not a microphone in this process: OmenMon runs elevated and
        // starts with Windows, and a system utility that opens a microphone is not a thing
        // to add. The unprivileged logger writes its newest reading to a small file; this
        // only reads it. No capture code ever runs elevated, and with the logger stopped
        // the columns are simply blank.
        //
        // Stale readings are dropped rather than carried forward: a value held over from a
        // logger that died would look like steady fan noise straight through a dropout,
        // which is exactly the wrong answer to the question this column exists to settle.
        private const double AudioMaxAgeSeconds = 6.0;
        private static string audioDropPath;
        private static bool audioResolved;

        // Same reading, as numbers, for the live graph. False when no logger is running,
        // which the chart draws as "no trace" rather than as a level of zero.
        public static bool TryGetAudio(out double rms, out int peakHz) {
            rms = 0; peakHz = 0;
            string a, b;
            ReadAudio(out a, out b);
            if(a.Length == 0) return false;
            return double.TryParse(a, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out rms)
                && int.TryParse(b, System.Globalization.NumberStyles.Integer,
                       System.Globalization.CultureInfo.InvariantCulture, out peakHz);
        }

        private static void ReadAudio(out string rms, out string peakHz) {
            rms = ""; peakHz = "";
            if(!audioResolved) {
                audioResolved = true;
                try {
                    audioDropPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "OmenMon", "OmenMon-audio-latest.txt");
                } catch { audioDropPath = null; }
            }
            if(audioDropPath == null) return;
            try {
                if(!File.Exists(audioDropPath)) return;
                if((DateTime.Now - File.GetLastWriteTime(audioDropPath)).TotalSeconds > AudioMaxAgeSeconds)
                    return;
                string line;
                using(var fs = new FileStream(audioDropPath, FileMode.Open,
                          FileAccess.Read, FileShare.ReadWrite))
                using(var sr = new StreamReader(fs))
                    line = sr.ReadLine();
                if(string.IsNullOrEmpty(line)) return;
                // rms_dbfs,band200_1500_db,band1500_6000_db,peak_hz,peak_db
                string[] f = line.Split(',');
                if(f.Length >= 4) { rms = f[0].Trim(); peakHz = f[3].Trim(); }
            } catch { }
        }

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

        // A second, narrower log written by the fan thread on every pass.
        //
        // OmenMon-telemetry.csv is written from the monitor loop every TelemetryEvery
        // passes, and that loop is 10-20 s late under load — useless for looking at a
        // five-second event. This one rides the fan thread, which does nothing else and
        // ticks on time, so it samples the two things that matter for fan behaviour at
        // the rate the control loop actually runs: the temperature it decided on, and the
        // level it asked for.
        private static string tracePath;
        private static bool traceResolved;

        public static void WriteFanTrace(int cpuC, int gpuC, int cpuCmd, int gpuCmd, int readCpu, int readGpu) {

            if(!Enabled) return;

            if(!traceResolved) {
                traceResolved = true;
                try {
                    string p = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "OmenMon-fantrace.csv");
                    File.AppendAllText(p, string.Empty);
                    tracePath = p;
                } catch {
                    try {
                        string dir = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmenMon");
                        Directory.CreateDirectory(dir);
                        tracePath = System.IO.Path.Combine(dir, "OmenMon-fantrace.csv");
                    } catch { tracePath = null; }
                }
            }
            if(tracePath == null) return;

            try {
                lock(gate) {
                    bool fresh = !File.Exists(tracePath) || new FileInfo(tracePath).Length == 0;
                    if(!fresh && new FileInfo(tracePath).Length > MaxBytes) {
                        try { File.Delete(tracePath); } catch { }
                        fresh = true;
                    }
                    var sb = new StringBuilder(96);
                    if(fresh) sb.Append("time,cpu_c,gpu_c,cpu_cmd,gpu_cmd,cpu_read,gpu_read\n");
                    sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(',')
                      .Append(cpuC).Append(',').Append(gpuC).Append(',')
                      .Append(cpuCmd).Append(',').Append(gpuCmd).Append(',')
                      .Append(readCpu).Append(',').Append(readGpu).Append('\n');
                    File.AppendAllText(tracePath, sb.ToString());
                }
            } catch { }

        }

        // Append one sample. Safe to call from the monitor thread every pass.
        public static void Write(
            int cpuC, int gpuC, int maxC,
            int cpuRpm, int gpuRpm, int cpuLvl, int gpuLvl, int cpuPct, int gpuPct,
            string mode, string program, int countdown, int cpuCmd, int gpuCmd) {

            if(!Enabled) return;
            string p = Path();
            if(p == null) return;

            string rmsDb, peakHz;
            ReadAudio(out rmsDb, out peakHz);

            try {
                lock(gate) {
                    bool fresh = !File.Exists(p) || new FileInfo(p).Length == 0;
                    // A file written before the audio columns existed has a narrower
                    // header; appending to it would put two different row shapes in one
                    // file, which every reader downstream would get wrong. Retire it once.
                    if(!fresh && !headerChecked) {
                        headerChecked = true;
                        try {
                            string first;
                            using(var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using(var sr = new StreamReader(fs))
                                first = sr.ReadLine();
                            if(first != null && first.TrimEnd() != Header) {
                                string old = p.Replace(".csv", ".prev.csv");
                                try {
                                    if(File.Exists(old)) File.Delete(old);
                                    File.Move(p, old);
                                    fresh = true;
                                } catch { }
                            }
                        } catch { }
                    }
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
                      .Append(countdown).Append(',')
                      // What the curve last asked for, beside what was read back. Without
                      // this pair the log cannot answer the only question that matters
                      // about a dip: did we command it, or did the hardware do it?
                      .Append(cpuCmd).Append(',').Append(gpuCmd).Append(',')
                      // Blank, not zero, when no logger is running — zero is a level the
                      // microphone could legitimately report, so it must not double as
                      // "no measurement".
                      .Append(rmsDb).Append(',').Append(peakHz).Append('\n');
                    File.AppendAllText(p, sb.ToString());
                }
            } catch { /* diagnostics must never break monitoring */ }
        }

        // ---- reading back, so the history graph opens populated ----------------
        //
        // The graph used to start empty and take its whole window to fill, so the first
        // minutes after opening showed less than the log on disk already knew.
        //
        // Note the rate mismatch, because it dictates how the chart must draw these: this
        // file is written once every TelemetryEvery monitor passes and that loop runs late
        // under load, so rows land 16-47 s apart, while the live chart pushes every pass
        // (~2-3 s). Seeded history is therefore genuinely coarser than what follows it.
        // That is only honest if the chart plots against real time and breaks the line
        // across gaps — which is why GuiChart is time-indexed rather than sample-indexed.
        // Do not "fix" the coarseness by interpolating onto a fixed grid: that draws a
        // shape the machine never made, which is the same mistake the microphone trace
        // made before it was removed.

        public struct Sample {
            public DateTime Time;
            public int CpuC, GpuC, CpuRpm, GpuRpm;
        }

        // Oldest first, newest last. Reads the rotated .prev.csv too, so a span that
        // reaches back past a rotation still returns a continuous history.
        public static System.Collections.Generic.List<Sample> ReadRecent(TimeSpan span, int maxRows) {

            var result = new System.Collections.Generic.List<Sample>();
            string p = Path();
            if(p == null) return result;

            DateTime cutoff = DateTime.Now - span;

            ReadInto(p.Replace(".csv", ".prev.csv"), cutoff, result);   // older file first
            ReadInto(p, cutoff, result);

            // Keep the newest maxRows: trimming the front rather than refusing to read
            // means an oversized log still yields its most recent slice.
            if(maxRows > 0 && result.Count > maxRows)
                result.RemoveRange(0, result.Count - maxRows);

            return result;
        }

        // Column positions come from the header rather than fixed indices. The header has
        // already changed once (the audio columns), and Write() rotates a file whose
        // header does not match, so the .prev.csv on disk may legitimately be the older,
        // narrower shape.
        private static void ReadInto(
            string file, DateTime cutoff, System.Collections.Generic.List<Sample> into) {

            if(string.IsNullOrEmpty(file)) return;
            try {
                if(!File.Exists(file)) return;

                int iTime = -1, iCpuC = -1, iGpuC = -1, iCpuR = -1, iGpuR = -1;

                // FileShare.ReadWrite: the monitor thread is appending to this file while
                // we read it, and a startup that threw here would be a startup that fails
                // because diagnostics were working.
                using(var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using(var sr = new StreamReader(fs)) {

                    string header = sr.ReadLine();
                    if(header == null) return;
                    string[] cols = header.Split(',');
                    for(int i = 0; i < cols.Length; i++)
                        switch(cols[i].Trim()) {
                            case "time":    iTime = i; break;
                            case "cpu_c":   iCpuC = i; break;
                            case "gpu_c":   iGpuC = i; break;
                            case "cpu_rpm": iCpuR = i; break;
                            case "gpu_rpm": iGpuR = i; break;
                        }
                    if(iTime < 0 || iCpuC < 0 || iGpuC < 0 || iCpuR < 0 || iGpuR < 0) return;

                    int need = Math.Max(Math.Max(iTime, iCpuC), Math.Max(Math.Max(iGpuC, iCpuR), iGpuR));

                    string line;
                    while((line = sr.ReadLine()) != null) {
                        if(line.Length == 0) continue;
                        string[] f = line.Split(',');
                        if(f.Length <= need) continue;      // truncated last line, mid-append

                        DateTime t;
                        if(!DateTime.TryParseExact(f[iTime].Trim(), "yyyy-MM-dd HH:mm:ss",
                               System.Globalization.CultureInfo.InvariantCulture,
                               System.Globalization.DateTimeStyles.None, out t))
                            continue;
                        if(t < cutoff) continue;

                        Sample s;
                        s.Time = t;
                        if(!int.TryParse(f[iCpuC].Trim(), out s.CpuC)) continue;
                        if(!int.TryParse(f[iGpuC].Trim(), out s.GpuC)) continue;
                        if(!int.TryParse(f[iCpuR].Trim(), out s.CpuRpm)) continue;
                        if(!int.TryParse(f[iGpuR].Trim(), out s.GpuRpm)) continue;
                        into.Add(s);
                    }
                }
            } catch { /* a log that cannot be read is not a reason to fail to open */ }
        }
    }
}
