  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// UserPrefs — the handful of choices the user makes in the window that should
// survive a restart (which fan profile, which power preset, the custom wattage).
//
// Deliberately a separate key=value file rather than new <Config> elements: adding
// a setting to OmenMon.xml needs a matching field in ConfigData plus load and save
// plumbing in Config.cs, and the app rewrites that file wholesale on save. A small
// sidecar keeps user state independent of the shipped configuration.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OmenMon.Library {

    public static class UserPrefs {

        public const string FileName = "OmenMon-user.conf";

        public const string KeyPowerPreset = "PowerPreset";   // Eco | Balanced | Performance | Custom
        public const string KeyCustomWatts = "CustomWatts";   // sustained W for the Custom preset
        public const string KeyFanProfile  = "FanProfile";    // name of the fan profile last applied

        private static readonly object gate = new object();
        private static Dictionary<string, string> values;
        private static string path;

        private static string Path() {
            if(path != null) return path;
            try {
                string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
                File.AppendAllText(p, string.Empty);        // writability probe
                path = p;
            } catch {
                try {
                    string dir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmenMon");
                    Directory.CreateDirectory(dir);
                    path = System.IO.Path.Combine(dir, FileName);
                } catch { path = null; }
            }
            return path;
        }

        private static void EnsureLoaded() {
            if(values != null) return;
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string p = Path();
            if(p == null || !File.Exists(p)) return;
            try {
                foreach(string line in File.ReadAllLines(p)) {
                    string s = line.Trim();
                    if(s.Length == 0 || s[0] == '#') continue;
                    int eq = s.IndexOf('=');
                    if(eq <= 0) continue;
                    values[s.Substring(0, eq).Trim()] = s.Substring(eq + 1).Trim();
                }
            } catch { }
        }

        public static string Get(string key, string fallback) {
            lock(gate) {
                EnsureLoaded();
                string v;
                return values.TryGetValue(key, out v) && !string.IsNullOrEmpty(v) ? v : fallback;
            }
        }

        public static int GetInt(string key, int fallback) {
            int n;
            return int.TryParse(Get(key, null), out n) ? n : fallback;
        }

        public static void Set(string key, string value) {
            lock(gate) {
                EnsureLoaded();
                values[key] = value ?? "";
                string p = Path();
                if(p == null) return;
                try {
                    var sb = new StringBuilder();
                    sb.Append("# OmenMon Reborn — user settings, rewritten automatically\n");
                    foreach(var kv in values)
                        sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
                    File.WriteAllText(p, sb.ToString());
                } catch { }
            }
        }

        public static void Set(string key, int value) {
            Set(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    // Shared definition of the power presets so the startup path and the window
    // cannot drift apart on what "Eco" means.
    public static class PowerPresets {

        public struct Def {
            public Guid Overlay;
            public byte Pl1, Pl2, Pl4;
            public string WinMode;
            public string Character;
        }

        public const string Eco = "Eco", Balanced = "Balanced",
                            Performance = "Performance", Custom = "Custom";

        // Custom takes one number (sustained W) and derives the rest, so the CPU
        // limits and the Windows power mode can never contradict each other.
        public static Def Resolve(string name, int customWatts) {
            Def d = new Def();
            if(string.Equals(name, Eco, StringComparison.OrdinalIgnoreCase)) {
                d.Overlay = External.PowrProf.OVERLAY_EFFICIENCY;
                d.Pl1 = 30; d.Pl2 = 45; d.Pl4 = 90;
                d.WinMode = "Best efficiency";
                d.Character = "quietest and coolest, longest battery";
            } else if(string.Equals(name, Performance, StringComparison.OrdinalIgnoreCase)) {
                d.Overlay = External.PowrProf.OVERLAY_PERFORMANCE;
                d.Pl1 = 54; d.Pl2 = 80; d.Pl4 = 190;
                d.WinMode = "Best performance";
                d.Character = "full 54 W cTDP ceiling, hotter and louder";
            } else if(string.Equals(name, Custom, StringComparison.OrdinalIgnoreCase)) {
                int w = customWatts < 25 ? 25 : customWatts > 54 ? 54 : customWatts;
                d.Pl1 = (byte) w;
                d.Pl2 = (byte) Math.Min(80, (int) Math.Round(w * 1.45));
                d.Pl4 = (byte) Math.Min(190, w * 3);
                if(w <= 35)      { d.Overlay = External.PowrProf.OVERLAY_EFFICIENCY;  d.WinMode = "Best efficiency"; }
                else if(w >= 50) { d.Overlay = External.PowrProf.OVERLAY_PERFORMANCE; d.WinMode = "Best performance"; }
                else             { d.Overlay = External.PowrProf.OVERLAY_BALANCED;    d.WinMode = "Balanced"; }
                d.Character = "derived from " + w + " W sustained";
            } else {
                d.Overlay = External.PowrProf.OVERLAY_BALANCED;
                d.Pl1 = 45; d.Pl2 = 65; d.Pl4 = 140;
                d.WinMode = "Balanced";
                d.Character = "the 7840HS stock 45 W TDP";
            }
            return d;
        }
    }
}
