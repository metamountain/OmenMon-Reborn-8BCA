  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// Inline colour picker: a saturation/value square and a hue strip, in the shape
// Windows' own picker uses.
//
// It replaces a modal ColorDialog that opened over the window — and, before it was
// given a position, opened in the top-left corner of the screen. A modal dialog is
// the wrong shape for this: picking a keyboard colour is something you do while
// looking at the keyboard picture directly above, comparing as you go, and a window
// that covers it and blocks the rest of the UI makes that impossible.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OmenMon.AppGui {

    internal sealed class GuiColorPicker : Control {

        // Width of the hue strip, and the gap between it and the square
        internal const int HueW = 18, Gap = 8;

        private float hue, sat = 1f, val = 1f;
        private bool dragSquare, dragHue;

        public event EventHandler ColorChanged;

        public GuiColorPicker() {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.ResizeRedraw, true);
        }

        // The selected colour. Setting it converts to HSV so the handles land in the
        // right place — a colour arriving from the hardware or a preset has to be
        // findable on the square, not just displayed.
        public Color Color {
            get { return FromHsv(this.hue, this.sat, this.val); }
            set {
                float h, s, v;
                ToHsv(value, out h, out s, out v);
                // A grey has no meaningful hue: keep the one already selected so the
                // square does not jump to red every time the user picks black or white.
                if(s > 0.0001f) this.hue = h;
                this.sat = s; this.val = v;
                Invalidate();
            }
        }

        private Rectangle SquareRect {
            get { return new Rectangle(0, 0, Math.Max(1, Width - HueW - Gap), Math.Max(1, Height)); }
        }

        private Rectangle HueRect {
            get { return new Rectangle(Width - HueW, 0, HueW, Math.Max(1, Height)); }
        }

#region Painting
        protected override void OnPaint(PaintEventArgs e) {

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(GuiTheme.Bg);

            Rectangle sq = SquareRect, hr = HueRect;

            // Saturation left-to-right over the pure hue, then value top-to-bottom in
            // black. Two gradients rather than a per-pixel loop: this repaints on every
            // drag step and on every hue change.
            using(var satBrush = new LinearGradientBrush(
                sq, Color.White, FromHsv(this.hue, 1f, 1f), LinearGradientMode.Horizontal))
                g.FillRectangle(satBrush, sq);

            using(var valBrush = new LinearGradientBrush(
                new Rectangle(sq.X, sq.Y - 1, sq.Width, sq.Height + 1),
                Color.FromArgb(0, 0, 0, 0), Color.Black, LinearGradientMode.Vertical))
                g.FillRectangle(valBrush, sq);

            // Hue strip, in six linear runs around the wheel
            for(int y = 0; y < hr.Height; y++) {
                float h = 360f * y / hr.Height;
                using(var pen = new Pen(FromHsv(h, 1f, 1f)))
                    g.DrawLine(pen, hr.Left, hr.Top + y, hr.Right, hr.Top + y);
            }

            using(var border = new Pen(GuiTheme.Border)) {
                g.DrawRectangle(border, sq.X, sq.Y, sq.Width - 1, sq.Height - 1);
                g.DrawRectangle(border, hr.X, hr.Y, hr.Width - 1, hr.Height - 1);
            }

            // Handles: a ring on the square, a bar across the strip. Drawn in both black
            // and white so they stay visible over any colour underneath.
            int hx = sq.X + (int) (this.sat * (sq.Width - 1));
            int hy = sq.Y + (int) ((1f - this.val) * (sq.Height - 1));
            using(var dark = new Pen(Color.FromArgb(160, 0, 0, 0)))
            using(var light = new Pen(Color.White)) {
                g.DrawEllipse(dark, hx - 6, hy - 6, 12, 12);
                g.DrawEllipse(light, hx - 5, hy - 5, 10, 10);

                int yy = hr.Top + (int) (this.hue / 360f * (hr.Height - 1));
                g.DrawRectangle(dark, hr.Left - 1, yy - 3, hr.Width + 1, 6);
                g.DrawRectangle(light, hr.Left, yy - 2, hr.Width - 1, 4);
            }

        }
#endregion

#region Input
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if(e.Button != MouseButtons.Left) return;
            if(HueRect.Contains(e.Location)) { this.dragHue = true; TrackHue(e.Y); }
            else { this.dragSquare = true; TrackSquare(e.X, e.Y); }
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            if(this.dragHue) TrackHue(e.Y);
            else if(this.dragSquare) TrackSquare(e.X, e.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            this.dragHue = this.dragSquare = false;
        }

        private void TrackSquare(int x, int y) {
            Rectangle sq = SquareRect;
            this.sat = Clamp01((float) (x - sq.X) / Math.Max(1, sq.Width - 1));
            this.val = 1f - Clamp01((float) (y - sq.Y) / Math.Max(1, sq.Height - 1));
            Changed();
        }

        private void TrackHue(int y) {
            Rectangle hr = HueRect;
            this.hue = Clamp01((float) (y - hr.Top) / Math.Max(1, hr.Height - 1)) * 360f;
            Changed();
        }

        private void Changed() {
            Invalidate();
            if(this.ColorChanged != null) this.ColorChanged(this, EventArgs.Empty);
        }
#endregion

#region Colour conversion
        private static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }

        internal static Color FromHsv(float h, float s, float v) {
            h = ((h % 360f) + 360f) % 360f;
            int i = (int) (h / 60f) % 6;
            float f = h / 60f - (int) (h / 60f);
            float p = v * (1f - s), q = v * (1f - f * s), t = v * (1f - (1f - f) * s);
            float r, g, b;
            switch(i) {
                case 0:  r = v; g = t; b = p; break;
                case 1:  r = q; g = v; b = p; break;
                case 2:  r = p; g = v; b = t; break;
                case 3:  r = p; g = q; b = v; break;
                case 4:  r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return Color.FromArgb((int) (r * 255f + 0.5f), (int) (g * 255f + 0.5f), (int) (b * 255f + 0.5f));
        }

        internal static void ToHsv(Color c, out float h, out float s, out float v) {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            float d = max - min;
            v = max;
            s = max <= 0f ? 0f : d / max;
            if(d <= 0f) { h = 0f; return; }
            if(max == r)      h = 60f * (((g - b) / d) % 6f);
            else if(max == g) h = 60f * (((b - r) / d) + 2f);
            else              h = 60f * (((r - g) / d) + 4f);
            if(h < 0f) h += 360f;
        }
#endregion

    }

}
