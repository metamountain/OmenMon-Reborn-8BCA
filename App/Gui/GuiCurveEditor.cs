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

        // The axis starts where the machine actually lives. Below 30 °C nothing on this
        // laptop asks for cooling, and a plot that began at 20 spent its first sixth on
        // a temperature the CPU only sees when the machine is off. FanProgram clamps any
        // temperature under the lowest row to that row, so the lowest row still covers
        // everything below it — no cooling behaviour changes.
        private const int TMin = 30, TMax = 95;      // °C  (X)
        private int LMin = 0, LMax = 60;             // level (Y), ×100 = rpm

        private static readonly Color ColCpu = Color.FromArgb(0xFF, 0x6B, 0x2E);
        private static readonly Color ColGpu = Color.FromArgb(0x22, 0xD3, 0xEE);

        // rows of { tempC, cpuLevel, gpuLevel }
        private List<int[]> pts = new List<int[]>();
        private int dragIdx = -1;
        private bool dragGpu;

        // Read-only curves. Default, Silent and Performance are the three references the
        // rest of this work is measured against — they came from upstream's own tables
        // and from the acoustic and thermal tests documented in the README, and a curve
        // edited by accident is not something you notice until the machine is too hot or
        // too loud. They can be selected, applied and copied; they cannot be changed.
        // "+" makes an editable copy under a new name, which is where experiments belong.
        public bool ReadOnly { get; set; }

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
            // Rows below the axis start (typically the 0 °C floor row that older configs
            // carry) are folded onto TMin rather than drawn off-canvas. Safe because
            // FanProgram.GetTemperatureLevel() clamps anything under the lowest threshold
            // to the lowest row, so a 30 °C row governs 0-30 °C exactly as a 0 °C row did.
            if(pts.Count > 0) {
                var below = pts.Where(p => p[0] < TMin).OrderBy(p => p[0]).ToList();
                if(below.Count > 0) {
                    int[] keep = below[below.Count - 1];
                    pts.RemoveAll(p => p[0] < TMin);
                    if(!pts.Any(p => p[0] == TMin))
                        pts.Add(new int[] { TMin, keep[1], keep[2] });
                }
            }
            // Guarantee an anchor at each end. A curve stored as 36..87 °C drew a line
            // that stopped in mid-air at both sides, while FanProgram was in fact applying
            // the 36 °C row from 30 °C down and the 87 °C row all the way to 95. The
            // anchors make the picture agree with that, at the same levels, so adding
            // them changes nothing about how the fans behave.
            if(pts.Count > 0) {
                pts.Sort((x, y) => x[0].CompareTo(y[0]));
                if(pts[0][0] != TMin)
                    pts.Insert(0, new int[] { TMin, pts[0][1], pts[0][2] });
                int[] last = pts[pts.Count - 1];
                if(last[0] != TMax)
                    pts.Add(new int[] { TMax, last[1], last[2] });
            }
            Sort();
            Simplify();
            Invalidate();
        }

        // How far, in fan levels, a point may sit off the line between its neighbours and
        // still be dropped. One level is 100 rpm — below what anyone can hear or wants a
        // handle for.
        private const int SimplifyTolerance = 1;

        // Drops points that lie on the straight line between their neighbours, so a curve
        // comes back as the handful of handles that define its shape.
        //
        // The counterpart to the fill-in in SaveProgram(): what is stored is dense, because
        // the engine only does step functions, but dense points are not what anyone wants
        // to edit — a saved curve would otherwise reload with twenty-odd handles on top of
        // each other. Save writes the curve the hardware needs, load recovers the curve the
        // user drew, and neither has to compromise for the other.
        private void Simplify() {

            if(pts.Count < 3)
                return;

            var keep = new List<int[]>();
            keep.Add(pts[0]);

            for(int i = 1; i < pts.Count - 1; i++) {

                int[] prev = keep[keep.Count - 1], cur = pts[i], next = pts[i + 1];
                int span = next[0] - prev[0];
                if(span <= 0) { keep.Add(cur); continue; }

                // Where the point would fall if it sat on the line between its neighbours
                double f = (double)(cur[0] - prev[0]) / span;
                double cpu = prev[1] + f * (next[1] - prev[1]);
                double gpu = prev[2] + f * (next[2] - prev[2]);

                if(Math.Abs(cur[1] - cpu) > SimplifyTolerance
                    || Math.Abs(cur[2] - gpu) > SimplifyTolerance)
                    keep.Add(cur);

            }

            keep.Add(pts[pts.Count - 1]);
            pts = keep;

        }

        // Spacing, in °C, of the points written out between the handles the user placed.
        // Fan levels are in units of 100 rpm, so 4 °C keeps each step small enough to be
        // inaudible on a curve of any realistic steepness.
        private const int SaveStepC = 4;

        // Writes the edited points back into the program (caller persists Config.Save()).
        //
        // The saved curve is filled in between the handles, because the engine applies a
        // program as a step function: GetTemperatureLevel() takes the highest threshold at
        // or below the current temperature, and never interpolates. Handles alone are
        // therefore not the curve the hardware runs — a Silent profile with points at 0,
        // 52 and 92 °C meant 1800 rpm everywhere from 52 to 91 °C, forty degrees with no
        // response at all, while this editor drew a confident diagonal across it.
        //
        // Filling in on save keeps both halves honest without compromising either. The
        // editor stays what it should be, a few handles and a straight line that is easy
        // to reason about, and the steps the hardware actually executes become small
        // enough to disappear into it.
        public bool SaveProgram() {
            if(ReadOnly)
                return false;
            if(ProgramName == null || !Config.FanProgram.ContainsKey(ProgramName)) return false;
            Sort();

            var lvl = new SortedDictionary<byte, byte[]>();

            for(int i = 0; i < pts.Count; i++) {

                // Always emit the handle itself, so a point the user placed lands exactly
                lvl[(byte)Clamp(pts[i][0], 0, 255)] =
                    new byte[] { (byte)Clamp(pts[i][1], 0, 255), (byte)Clamp(pts[i][2], 0, 255) };

                if(i + 1 >= pts.Count)
                    continue;

                int t0 = pts[i][0], t1 = pts[i + 1][0];
                int span = t1 - t0;
                if(span <= SaveStepC)
                    continue;

                // Interpolate the intermediate thresholds along the straight line the
                // editor draws between the two handles
                for(int t = t0 + SaveStepC; t < t1; t += SaveStepC) {
                    double f = (double)(t - t0) / span;
                    int cpu = (int)Math.Round(pts[i][1] + f * (pts[i + 1][1] - pts[i][1]));
                    int gpu = (int)Math.Round(pts[i][2] + f * (pts[i + 1][2] - pts[i][2]));
                    lvl[(byte)Clamp(t, 0, 255)] =
                        new byte[] { (byte)Clamp(cpu, 0, 255), (byte)Clamp(gpu, 0, 255) };
                }

            }

            Config.FanProgram[ProgramName].Level = lvl;
            return true;
        }

        private void Sort() { pts = pts.OrderBy(p => p[0]).ToList(); }
        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        private Rectangle Plot {
            get { return new Rectangle(GuiChart.AxisLeft, 26,
                Math.Max(10, Width - GuiChart.AxisLeft - 14), Math.Max(10, Height - 48)); }
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
            using(var f = GuiTheme.Ui(GuiTheme.FontCaption))
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
            using(var fL = GuiTheme.Ui(GuiTheme.FontCaption)) {
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

            // Ring the selected handle, so the numbers in the entry fields visibly belong
            // to one point on the graph rather than to whichever was touched last.
            bool isGpuLine = yi == 2;
            if(selIdx >= 0 && selIdx < scr.Length && selGpu == isGpuLine)
                using(var pen = new Pen(GuiTheme.Text, 2f))
                    g.DrawEllipse(pen, scr[selIdx].X - 7, scr[selIdx].Y - 7, 14, 14);
        }

        // What the pointer is over, or what is selected, for the readout above the graph.
        // Curve editing by dragging is quick but imprecise — you cannot land on exactly
        // 65 °C / 3200 rpm with a mouse — so the same point is also editable as numbers.
        internal sealed class PointInfo : EventArgs {
            public bool Valid;      // false = pointer is off the plot, or nothing selected
            public bool OnPoint;    // over an existing handle rather than open space
            public bool Gpu;        // which of the two curves
            public int TempC;
            public int Level;       // ×100 = rpm
        }

        public event EventHandler HoverChanged;
        public event EventHandler SelectionChanged;

        private int selIdx = -1;
        private bool selGpu;
        private PointInfo hover = new PointInfo();

        internal PointInfo Hover { get { return this.hover; } }

        internal PointInfo Selection {
            get {
                if(this.selIdx < 0 || this.selIdx >= this.pts.Count)
                    return new PointInfo();
                int[] p = this.pts[this.selIdx];
                return new PointInfo {
                    Valid = true, OnPoint = true, Gpu = this.selGpu,
                    TempC = p[0], Level = p[this.selGpu ? 2 : 1]
                };
            }
        }

        // Move the selected point to an exact temperature and level, applying the same
        // constraints dragging does: a point may not pass its neighbours, and the level
        // stays inside the axis. Returns false and changes nothing if there is no
        // selection, so a stray keystroke in the entry field cannot corrupt the curve.
        internal bool TrySetSelected(int tempC, int level) {

            if(ReadOnly)
                return false;

            if(this.selIdx < 0 || this.selIdx >= this.pts.Count)
                return false;

            int lo, hi;
            DragRange(this.selIdx, out lo, out hi);

            int[] pt = this.pts[this.selIdx];
            pt[0] = Clamp(tempC, lo, hi);
            pt[this.selGpu ? 2 : 1] = Clamp(level, Config.FanLevelMin, Config.FanLevelMax);

            Changed();
            if(this.SelectionChanged != null) this.SelectionChanged(this, EventArgs.Empty);
            return true;

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
            if(ReadOnly) return;
            bool gpu;
            int hit = HitTest(e.Location, out gpu);
            if(e.Button == MouseButtons.Right) {
                // The first and last handles anchor the curve to the axis ends and are
                // not removable: without them the plotted line stops short of the
                // edge and the curve no longer says what happens there.
                if(hit >= 0 && pts.Count > 2 && !IsEndpoint(hit)) {
                    pts.RemoveAt(hit);
                    if(selIdx == hit) selIdx = -1; else if(selIdx > hit) selIdx--;
                    Sort(); Changed(); RaiseSelection();
                }
                return;
            }
            if(e.Button != MouseButtons.Left) return;
            if(hit >= 0) {
                dragIdx = hit; dragGpu = gpu;
                Select(hit, gpu);
                return;
            }
            int t = ToTemp(e.X);
            if(pts.Any(q => q[0] == t)) return;
            pts.Add(new int[] { t, InterpAt(t, 1), InterpAt(t, 2) });
            Sort();
            dragIdx = pts.FindIndex(q => q[0] == t);
            dragGpu = Math.Abs(ToScreen(t, InterpAt(t, 2)).Y - e.Y)
                    < Math.Abs(ToScreen(t, InterpAt(t, 1)).Y - e.Y);
            Select(dragIdx, dragGpu);
            Changed();
        }
        // The two handles that pin the curve to the axis ends. They may be dragged up and
        // down freely, but not sideways and not away: a curve whose leftmost point sat at
        // 50 °C said nothing at all about 30-50 °C, and FanProgram would silently apply the
        // 50 °C row down there instead. Anchoring both ends keeps the picture and the
        // behaviour the same thing.
        private bool IsEndpoint(int idx) {
            return idx <= 0 || idx >= pts.Count - 1;
        }

        // How far a handle may travel horizontally. Endpoints: nowhere.
        private void DragRange(int idx, out int lo, out int hi) {
            if(idx <= 0)                 { lo = hi = TMin; return; }
            if(idx >= pts.Count - 1)     { lo = hi = TMax; return; }
            lo = pts[idx - 1][0] + 1;
            hi = pts[idx + 1][0] - 1;
        }


        private void Select(int idx, bool gpu) {
            this.selIdx = idx; this.selGpu = gpu;
            Invalidate();
            RaiseSelection();
        }

        private void RaiseSelection() {
            if(this.SelectionChanged != null) this.SelectionChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);

            // Report what the pointer is over, whether or not a drag is in progress —
            // during a drag this is the live value of the point being moved, which is
            // exactly what you want to see while placing it.
            bool hgpu;
            int hhit = HitTest(e.Location, out hgpu);
            PointInfo hi = new PointInfo { Valid = true, OnPoint = hhit >= 0 };
            if(dragIdx >= 0 && dragIdx < pts.Count) {
                hi.Gpu = dragGpu; hi.TempC = pts[dragIdx][0];
                hi.Level = pts[dragIdx][dragGpu ? 2 : 1];
            } else if(hhit >= 0) {
                hi.Gpu = hgpu; hi.TempC = pts[hhit][0];
                hi.Level = pts[hhit][hgpu ? 2 : 1];
            } else {
                hi.TempC = ToTemp(e.X);
                hi.Level = ToLevel(e.Y);
                // Off the plot area entirely: say nothing rather than a clamped number
                if(hi.TempC < TMin || hi.TempC > TMax) hi.Valid = false;
                hi.Gpu = Math.Abs(ToScreen(hi.TempC, InterpAt(hi.TempC, 2)).Y - e.Y)
                       < Math.Abs(ToScreen(hi.TempC, InterpAt(hi.TempC, 1)).Y - e.Y);
            }
            this.hover = hi;
            if(this.HoverChanged != null) this.HoverChanged(this, EventArgs.Empty);

            if(dragIdx < 0 || dragIdx >= pts.Count) return;
            int[] pt = pts[dragIdx];
            pt[dragGpu ? 2 : 1] = ToLevel(e.Y);
            int lo, hi2;
            DragRange(dragIdx, out lo, out hi2);
            pt[0] = Clamp(ToTemp(e.X), lo, hi2);
            selIdx = dragIdx; selGpu = dragGpu;
            Changed();
            RaiseSelection();
        }

        protected override void OnMouseLeave(EventArgs e) {
            base.OnMouseLeave(e);
            this.hover = new PointInfo();
            if(this.HoverChanged != null) this.HoverChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragIdx = -1; }

        private void Changed() {
            Invalidate();
            if(CurveChanged != null) CurveChanged(this, EventArgs.Empty);
        }
    }
}
