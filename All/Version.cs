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
// The local default is the upstream release this fork is cut from, not 1.0.
//
// It matters now that there is an update check. Application.ProductVersion is what
// Library/Update.cs compares against the newest upstream tag, and an unstamped 1.0
// made every upstream release look newer than a build that is in fact ahead of it —
// so the button offered to "update" this fork to stock OmenMon, which would undo all
// of the board-8BCA work. Keep this at the release the fork tracks.
[assembly: AssemblyVersion("1.4.12.0")]
[assembly: AssemblyFileVersion("1.4.12.0")]
[assembly: AssemblyInformationalVersion("1.4.12")]
[assembly: AssemblyMetadata("Timestamp", "Undefined")]
