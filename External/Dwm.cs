  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// Dwm — Desktop Window Manager attributes used to darken the non-client area
// (title bar, border). Without these a dark WinForms app still gets the default
// light caption, which is the one bright strip left on screen.

using System;
using System.Runtime.InteropServices;

namespace OmenMon.External {

    public static class Dwm {

        private const string DllName = "dwmapi.dll";

        // Win10 1809..1903 used attribute 19; 2004 and later (incl. Win11) use 20.
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_PRE20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE         = 20;

        // Windows 11 22000+ : explicit non-client colours (COLORREF = 0x00BBGGRR)
        private const int DWMWA_BORDER_COLOR  = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR    = 36;

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int value, int size);

        private static int ToColorRef(System.Drawing.Color c) {
            return c.R | (c.G << 8) | (c.B << 16);   // 0x00BBGGRR
        }

        // Applies a dark title bar to the window. Caption/text/border colours are only
        // honoured on Windows 11 22000+; the immersive-dark-mode flag covers Win10.
        // Every call is best-effort — an unsupported attribute just returns non-zero.
        public static void UseDarkTitleBar(
            IntPtr hwnd,
            System.Drawing.Color caption,
            System.Drawing.Color text,
            System.Drawing.Color border) {

            if(hwnd == IntPtr.Zero) return;

            int on = 1;
            try { DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)); } catch { }
            try { DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_PRE20H1, ref on, sizeof(int)); } catch { }

            int cap = ToColorRef(caption), txt = ToColorRef(text), bdr = ToColorRef(border);
            try { DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref cap, sizeof(int)); } catch { }
            try { DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR,    ref txt, sizeof(int)); } catch { }
            try { DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR,  ref bdr, sizeof(int)); } catch { }
        }
    }
}
