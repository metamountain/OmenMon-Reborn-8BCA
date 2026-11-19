  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// Screen colour sampler for the inline picker — the eyedropper Windows' own colour
// picker has.
//
// Reads the colour under the pointer straight from the screen DC, so it can sample
// anything on the desktop: a wallpaper, a photo, another application's UI. The
// keyboard's four zones are being matched to something the user is looking at more
// often than to a number they have in mind.

using System;
using System.Drawing;
using System.Windows.Forms;
using OmenMon.External;

namespace OmenMon.AppGui {

    internal sealed class GuiPipette : IDisposable {

        private readonly Control owner;
        private readonly Action<Color> preview;   // live, as the pointer moves
        private readonly Action<Color> commit;    // on click
        private Timer timer;
        private bool active;

        public GuiPipette(Control owner, Action<Color> preview, Action<Color> commit) {
            this.owner = owner;
            this.preview = preview;
            this.commit = commit;
        }

        public bool IsActive { get { return this.active; } }

        // Starts sampling. The pointer is captured by the owning control so the click
        // that ends it does not land on whatever is underneath, and Escape cancels.
        public void Start() {

            if(this.active) return;
            this.active = true;

            this.owner.Capture = true;
            this.owner.Cursor = Cursors.Cross;

            this.timer = new Timer { Interval = 40 };
            this.timer.Tick += delegate { Sample(); };
            this.timer.Start();

            this.owner.MouseDown += OnMouseDown;
            this.owner.KeyDown += OnKeyDown;
            this.owner.Focus();

        }

        public void Stop(bool cancelled) {

            if(!this.active) return;
            this.active = false;

            if(this.timer != null) {
                this.timer.Stop();
                this.timer.Dispose();
                this.timer = null;
            }

            this.owner.MouseDown -= OnMouseDown;
            this.owner.KeyDown -= OnKeyDown;
            this.owner.Capture = false;
            this.owner.Cursor = Cursors.Default;

        }

        // Colour under the pointer, read from the screen device context. Returns
        // Color.Empty rather than throwing if the DC cannot be had — sampling is a
        // convenience and must never take the window down with it.
        public static Color ColorAtCursor() {
            IntPtr dc = IntPtr.Zero;
            try {
                dc = User32.GetDC(IntPtr.Zero);
                if(dc == IntPtr.Zero) return Color.Empty;
                Point p = Cursor.Position;
                int bgr = Gdi32.GetPixel(dc, p.X, p.Y);
                if(bgr == -1) return Color.Empty;
                // GetPixel answers 0x00BBGGRR, not RGB
                return Color.FromArgb(bgr & 0xFF, (bgr >> 8) & 0xFF, (bgr >> 16) & 0xFF);
            } catch {
                return Color.Empty;
            } finally {
                if(dc != IntPtr.Zero) User32.ReleaseDC(IntPtr.Zero, dc);
            }
        }

        private void Sample() {
            Color c = ColorAtCursor();
            if(c != Color.Empty && this.preview != null) this.preview(c);
        }

        private void OnMouseDown(object sender, MouseEventArgs e) {
            Color c = ColorAtCursor();
            Stop(false);
            if(c != Color.Empty && this.commit != null) this.commit(c);
        }

        private void OnKeyDown(object sender, KeyEventArgs e) {
            if(e.KeyCode == Keys.Escape) Stop(true);
        }

        public void Dispose() { Stop(true); }

    }

}
