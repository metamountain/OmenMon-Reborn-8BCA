  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023-2024 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/

using System;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

// Project metadata (except version information)
// Note: the *assembly name* stays "OmenMon" — Config derives the settings-XML root
// element (<OmenMon>), the scheduled-task names and the mutex names from it, so
// renaming that would break the configuration. Only the display identity changes.
[assembly: AssemblyCompany("OmenMon Reborn")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCopyright("© 2023-2024 Piotr Szczepański · fork © 2026 seakyy")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyDescription("HP OMEN hardware monitoring & control")]
[assembly: AssemblyTitle("OmenMon Reborn")]
[assembly: AssemblyProduct("OmenMon Reborn")]
[assembly: AssemblyTrademark("")]
[assembly: CLSCompliant(false)]
[assembly: ComVisible(false)]
[assembly: Guid("2C01055A-1511-CEDB-EEF5-EAF00D5A1AD5")]
[assembly: NeutralResourcesLanguage("en")]
