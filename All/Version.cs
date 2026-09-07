  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/

using System.Reflection;

// Project version metadata is set dynamically.
// These are just the defaults used for local builds. In CI the AddVersion
// target in OmenMon.csproj rewrites this file: AssemblyVersion is stamped
// with only the first three segments (X.Y.Z, .NET pads to X.Y.Z.0 — kept
// stable to avoid breaking assembly binding), while AssemblyFileVersion
// gets the full four segments including BUILD_NUMBER (auto-incremented by
// .github/workflows/build_bump.yml), so shipped binaries appear as
// X.Y.Z.<build#> in File Explorer / Get-FileHash properties.
// Numbered as the upstream release this fork is cut from, plus a suffix saying it is
// not that release.
//
// Two things pull in opposite directions here. Library/Update.cs compares
// Application.ProductVersion against the newest upstream tag, so the numeric part has
// to be the release the fork tracks: an unstamped 1.0 made every upstream release look
// newer than a build that is in fact ahead of it, and the update button offered to
// "update" this fork to stock OmenMon — undoing the whole board-8BCA effort. But a bare
// "1.4.12" then claims to *be* upstream 1.4.12, which it is not: it carries the EC fan
// watchdog, the NVML session rebuild and the SMN die temperature, none of which upstream
// has.
//
// So the numeric part stays 1.4.12 and the suffix says the rest. Update.Current splits
// on the dash before parsing, so the comparison is unaffected and an untouched upstream
// still reads as "Up to date".
[assembly: AssemblyVersion("1.4.12.0")]
[assembly: AssemblyFileVersion("1.4.12.0")]
[assembly: AssemblyInformationalVersion("1.4.12-8bca")]
[assembly: AssemblyMetadata("Timestamp", "Undefined")]
