  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// GuiCurveEditor — inline, editable fan curve. Drawn in the same visual language as
// GuiChart (dark ground, grid, labelled axes, legend). X = temperature, Y = fan speed.
// Two independent lines, CPU and GPU, sharing the breakpoint temperatures — which is
// exactly how FanProgramData stores them (SortedDictionary<temp, byte[]{cpu, gpu}>).
//
// Left-click empty space adds a breakpoint, drag moves the nearest handle,
// right-click removes a breakpoint.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OmenMon.Library;

namespace OmenMon.AppGui {

    internal sealed class GuiCurveEditor : Control {

        private const int TMin = 20, TMax = 95;      // °C  (X)
        private int LMin = 0, LMax = 60;             // level (Y), ×100 = rpm

        private static readonly Color ColCpu = Color.FromArgb(0xFF, 0x6B, 0x2E);
        private static readonly Color ColGpu = Color.FromArgb(0x22, 0xD3, 0xEE);

        // rows of { tempC, cpuLevel, gpuLevel }
        private List<int[]> pts = new List<int[]>();
        private int dragIdx = -1;
        private bool dragGpu;

        public string ProgramName { get; private set; }
        public event EventHandler CurveChanged;

        public GuiCurveEditor() {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            this.BackColor = GuiTheme.Bg;
            this.LMin = Math.Max(0, Config.FanLevelMin - 2);
            this.LMax = Config.FanLevelMax + 3;
        }

        public void LoadProgram(string name) {
            ProgramName = name;
            pts.Clear();
            if(name != null && Config.FanProgram.ContainsKey(name))
                foreach(var kv in Config.FanProgram[name].Level)
                    pts.Add(new int[] { kv.Key,
                        kv.Value.Length > 0 ? kv.Value[0] : LMin,
                        kv.Value.Length > 1 ? kv.Value[1] : LMin });
            if(pts.Count < 2) {
                pts.Clear();
                pts.Add(new int[] { TMin, LMin, LMin });
                pts.Add(new int[] { TMax, LMax, LMax });
            }
            Sort();
            Invalidate();
        }

        // Writes the edited points back into the program (caller persists Config.Save()).
        public bool SaveProgram() {
            if(ProgramName == null || !Config.FanProgram.ContainsKey(ProgramName)) return false;
            Sort();
            var lvl = new SortedDictionary<byte, byte[]>();
            foreach(var p in pts)
                lvl[(byte)Clamp(p[0], 0, 255)] =
                    new byte[] { (byte)Clamp(p[1], 0, 255), (byte)Clamp(p[2], 0, 255) };
            Config.FanProgram[ProgramName].Level = lvl;
            return true;
        }

        private void Sort() { pts = pts.OrderBy(p => p[0]).ToList(); }
        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        private Rectangle Plot {
            get { return new Rectangle(46, 26, Math.Max(10, Width - 60), Math.Max(10, Height - 48)); }
        }

        private Point ToScreen(int tempC, int level) {
            Rectangle p = Plot;
            float fx = (float)(tempC - TMin) / (TMax - TMin);
            float fy = (float)(level - LMin) / Math.Max(1, LMax - LMin);
            return new Point((int)(p.Left + fx * p.Width), (int)(p.Bottom - fy * p.Height));
        }
        private int ToTemp(int sx) {
            Rectangle p = Plot;
            return Clamp((int)Math.Round(TMin + (sx - p.Left) / (float)p.Width * (TMax - TMin)), TMin, TMax);
        }
        private int ToLevel(int sy) {
            Rectangle p = Plot;
            return Clamp((int)Math.Round(LMin + (p.Bottom - sy) / (float)p.Height * (LMax - LMin)), LMin, LMax);
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.Clear(GuiTheme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle p = Plot;
            if(p.Width < 20 || p.Height < 20) return;

            using(var axisPen = new Pen(GuiTheme.Border))
            using(var gridPen = new Pen(Color.FromArgb(0x2A, 0x2A, 0x30)))
            using(var f = new Font("Segoe UI", 7.5f))
            using(var brM = new SolidBrush(GuiTheme.Muted)) {

                // Y: fan speed (rpm)
                for(int l = 0; l <= LMax; l += 10) {
                    if(l < LMin) continue;
                    int y = ToScreen(TMin, l).Y;
                    g.DrawLine(gridPen, p.Left, y, p.Right, y);
                    string s = (l / 10.0).ToString("0.0") + "k";
                    SizeF sz = g.MeasureString(s, f);
                    g.DrawString(s, f, brM, p.Left - sz.Width - 5, y - sz.Height / 2);
                }
                // X: temperature
                for(int t = TMin; t <= TMax; t += 15) {
                    int x = ToScreen(t, LMin).X;
                    g.DrawLine(gridPen, x, p.Top, x, p.Bottom);
                    string s = t + "°";
                    SizeF sz = g.MeasureString(s, f);
                    g.DrawString(s, f, brM, x - sz.Width / 2, p.Bottom + 3);
                }
                g.DrawLine(axisPen, p.Left, p.Top, p.Left, p.Bottom);
                g.DrawLine(axisPen, p.Left, p.Bottom, p.Right, p.Bottom);
                g.DrawString("rpm", f, brM, 4, p.Top - 15);
            }

            DrawLine(g, 1, ColCpu);
            DrawLine(g, 2, ColGpu);

            // Just the series key — the interaction is explained in the section itself
            using(var fL = new Font("Segoe UI", 8.25f)) {
                float x = p.Left;
                x = Legend(g, fL, x, ColCpu, "CPU");
                x = Legend(g, fL, x, ColGpu, "GPU");
            }
        }

        private float Legend(Graphics g, Font f, float x, Color c, string t) {
            using(var br = new SolidBrush(c)) {
                g.FillRectangle(br, x, 8, 9, 9);
                g.DrawString(t, f, br, x + 12, 3);
            }
            return x + 12 + g.MeasureString(t, f).Width + 12;
        }

        private void DrawLine(Graphics g, int yi, Color c) {
            if(pts.Count < 2) return;
            var scr = pts.Select(pt => (PointF)ToScreen(pt[0], pt[yi])).ToArray();
            using(var pen = new Pen(c, 2f) { LineJoin = LineJoin.Round })
                g.DrawLines(pen, scr);
            using(var br = new SolidBrush(c))
                foreach(PointF s in scr)
                    g.FillEllipse(br, s.X - 4, s.Y - 4, 8, 8);
        }

        private int HitTest(Point m, out bool gpu) {
            gpu = false; int best = -1; double bd = 144;
            for(int i = 0; i < pts.Count; i++)
                for(int k = 0; k < 2; k++) {
                    bool isGpu = k == 1;
                    Point s = ToScreen(pts[i][0], pts[i][isGpu ? 2 : 1]);
                    double d = (s.X - m.X) * (s.X - m.X) + (s.Y - m.Y) * (s.Y - m.Y);
                    if(d < bd) { bd = d; best = i; gpu = isGpu; }
                }
            return best;
        }

        private int InterpAt(int t, int yi) {
            if(pts.Count == 0) return LMin;
            var o = pts.OrderBy(p => p[0]).ToList();
            if(t <= o[0][0]) return o[0][yi];
            if(t >= o[o.Count - 1][0]) return o[o.Count - 1][yi];
            for(int i = 1; i < o.Count; i++)
                if(t <= o[i][0]) {
                    double f = (double)(t - o[i - 1][0]) / (o[i][0] - o[i - 1][0]);
                    return (int)Math.Round(o[i - 1][yi] + f * (o[i][yi] - o[i - 1][yi]));
                }
            return LMin;
        }

        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            bool gpu;
            int hit = HitTest(e.Location, out gpu);
            if(e.Button == MouseButtons.Right) {
                if(hit >= 0 && pts.Count > 2) { pts.RemoveAt(hit); Sort(); Changed(); }
                return;
            }
            if(e.Button != MouseButtons.Left) return;
            if(hit >= 0) { dragIdx = hit; dragGpu = gpu; return; }
            int t = ToTemp(e.X);
            if(pts.Any(q => q[0] == t)) return;
            pts.Add(new int[] { t, InterpAt(t, 1), InterpAt(t, 2) });
            Sort();
            dragIdx = pts.FindIndex(q => q[0] == t);
            dragGpu = Math.Abs(ToScreen(t, InterpAt(t, 2)).Y - e.Y)
                    < Math.Abs(ToScreen(t, InterpAt(t, 1)).Y - e.Y);
            Changed();
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            if(dragIdx < 0 || dragIdx >= pts.Count) return;
            int[] pt = pts[dragIdx];
            pt[dragGpu ? 2 : 1] = ToLevel(e.Y);
            int lo = dragIdx > 0 ? pts[dragIdx - 1][0] + 1 : TMin;
            int hi = dragIdx < pts.Count - 1 ? pts[dragIdx + 1][0] - 1 : TMax;
            pt[0] = Clamp(ToTemp(e.X), lo, hi);
            Changed();
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragIdx = -1; }

        private void Changed() {
            Invalidate();
            if(CurveChanged != null) CurveChanged(this, EventArgs.Empty);
        }
    }
}
