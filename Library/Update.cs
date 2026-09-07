  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// Update check against the upstream GitHub releases.
//
// THE RULE THIS FILE EXISTS TO KEEP: an update replaces OmenMon.exe and nothing
// else. OmenMon.xml holds the user's fan curves and keyboard colour presets in
// the same file as the shipped defaults, and OmenMon-user.conf holds the
// selected profile and power settings. Shipping a new copy of either would
// silently delete work the user cannot get back. So the installer below
// extracts exactly one entry from the release archive, by name, and every other
// entry is ignored -- including a newer OmenMon.xml, which is deliberately NOT
// merged. New model-database entries are not worth a destroyed profile list.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace OmenMon.Library {

    public static class Update {

        // Upstream release feed
        private const string ReleaseApi =
            "https://api.github.com/repos/seakyy/OmenMon-Reborn/releases/latest";

        // GitHub rejects requests without one
        private const string UserAgent = "OmenMon-Reborn-UpdateCheck";

        // The only file an update is ever allowed to replace
        private const string PayloadName = "OmenMon.exe";

        // What a check found
        public sealed class Info {
            public bool Ok;                  // the check itself completed
            public string Error;             // why it did not, for the log
            public string Tag;               // e.g. "v1.4.13"
            public Version Latest;           // parsed from Tag, null if unparseable
            public bool IsNewer;             // Latest > Current
            public string ArchiveUrl;        // release asset to fetch
            public long ArchiveSize;
        }

        // The running build's version
        public static Version Current {
            get {
                try {
                    return new Version(Application.ProductVersion.Split('-')[0]);
                } catch {
                    return new Version(0, 0);
                }
            }
        }

        // Where downloads are staged. Deliberately not the install directory: nothing
        // lands beside OmenMon.xml until the user has asked for it to be installed.
        public static string StageDir {
            get {
                return Path.Combine(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OmenMon"), "update");
            }
        }

        // Asks GitHub what the newest release is. Blocking; call it off the UI thread.
        public static Info Check() {

            Info info = new Info();

            try {

                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

                var request = (HttpWebRequest) WebRequest.Create(ReleaseApi);
                request.UserAgent = UserAgent;
                request.Accept = "application/vnd.github+json";
                request.Timeout = 15000;

                string json;
                using(var response = (HttpWebResponse) request.GetResponse())
                using(var reader = new StreamReader(response.GetResponseStream()))
                    json = reader.ReadToEnd();

                // Picked out with expressions rather than a JSON parser: this project
                // targets .NET Framework and carries no serialization dependency, and
                // two fields do not justify adding one. A shape change upstream shows up
                // as "could not read the release feed", not as a wrong answer.
                info.Tag = Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                info.ArchiveUrl = Match(json,
                    "\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.zip)\"");

                string size = Match(json, "\"size\"\\s*:\\s*(\\d+)");
                if(size != null) long.TryParse(size, out info.ArchiveSize);

                if(info.Tag == null) {
                    info.Error = "could not read the release feed";
                    return info;
                }

                info.Latest = ParseVersion(info.Tag);
                info.IsNewer = info.Latest != null && info.Latest > Current;
                info.Ok = true;

            } catch(Exception e) {
                info.Error = e.Message;
                Config.ErrorLog("Update.Check", e);
            }

            return info;

        }

        // Fetches the release archive into the staging directory.
        // Returns the archive path, or null with the reason in error.
        public static string Download(Info info, out string error) {

            error = null;

            try {

                if(info == null || string.IsNullOrEmpty(info.ArchiveUrl)) {
                    error = "the release has no downloadable archive";
                    return null;
                }

                Directory.CreateDirectory(StageDir);
                string path = Path.Combine(StageDir, "OmenMon-" + info.Tag + ".zip");

                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

                using(var client = new WebClient()) {
                    client.Headers.Add("User-Agent", UserAgent);
                    client.DownloadFile(info.ArchiveUrl, path);
                }

                // A release archive that does not contain the one file we install is not
                // one we can use, and it is better to say so now than after a swap
                using(ZipArchive zip = ZipFile.OpenRead(path))
                    if(FindPayload(zip) == null) {
                        error = "the archive does not contain " + PayloadName;
                        return null;
                    }

                return path;

            } catch(Exception e) {
                error = e.Message;
                Config.ErrorLog("Update.Download", e);
                return null;
            }

        }

        // Extracts OmenMon.exe from the archive and hands the swap to a helper script,
        // because a running executable cannot replace itself. The script waits for this
        // process to exit, copies the one file, and starts it again.
        //
        // Nothing here writes to OmenMon.xml or OmenMon-user.conf, and the script copies
        // a single named file rather than expanding the archive over the directory.
        public static bool Install(string archivePath, out string error) {

            error = null;

            try {

                string installDir = Path.GetDirectoryName(Application.ExecutablePath);
                string staged = Path.Combine(StageDir, PayloadName);

                using(ZipArchive zip = ZipFile.OpenRead(archivePath)) {
                    ZipArchiveEntry entry = FindPayload(zip);
                    if(entry == null) {
                        error = "the archive does not contain " + PayloadName;
                        return false;
                    }
                    if(File.Exists(staged)) File.Delete(staged);
                    entry.ExtractToFile(staged);
                }

                // Keep the outgoing build: an update that turns out worse than what it
                // replaced should be one copy away from being undone
                string backup = Path.Combine(installDir, PayloadName + ".prev");

                string script = Path.Combine(StageDir, "install.cmd");
                File.WriteAllText(script,
                    "@echo off\r\n"
                    + "rem Written by OmenMon. Replaces only " + PayloadName + ".\r\n"
                    + "rem OmenMon.xml and OmenMon-user.conf are deliberately untouched:\r\n"
                    + "rem they hold the user's fan curves, keyboard presets and settings.\r\n"
                    + ":wait\r\n"
                    + "tasklist /fi \"imagename eq " + PayloadName + "\" | find /i \""
                        + PayloadName + "\" >nul && (\r\n"
                    + "  ping -n 2 127.0.0.1 >nul\r\n"
                    + "  goto wait\r\n"
                    + ")\r\n"
                    + "copy /y \"" + Path.Combine(installDir, PayloadName) + "\" \""
                        + backup + "\" >nul\r\n"
                    + "copy /y \"" + staged + "\" \"" + Path.Combine(installDir, PayloadName)
                        + "\" >nul\r\n"
                    + "start \"\" \"" + Path.Combine(installDir, PayloadName) + "\"\r\n");

                Process.Start(new ProcessStartInfo {
                    FileName = script,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = true
                });

                return true;

            } catch(Exception e) {
                error = e.Message;
                Config.ErrorLog("Update.Install", e);
                return false;
            }

        }

        // The archive lays the build out under a directory, so match on the leaf name
        private static ZipArchiveEntry FindPayload(ZipArchive zip) {
            foreach(ZipArchiveEntry entry in zip.Entries)
                if(string.Equals(entry.Name, PayloadName, StringComparison.OrdinalIgnoreCase))
                    return entry;
            return null;
        }

        private static string Match(string text, string pattern) {
            Match m = Regex.Match(text, pattern);
            return m.Success ? m.Groups[1].Value : null;
        }

        // "v1.4.13-reborn" -> 1.4.13
        private static Version ParseVersion(string tag) {
            try {
                Match m = Regex.Match(tag, "(\\d+(?:\\.\\d+){1,3})");
                return m.Success ? new Version(m.Groups[1].Value) : null;
            } catch {
                return null;
            }
        }

    }

}
