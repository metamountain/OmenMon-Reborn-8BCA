  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// GuiChart — rolling line chart for CPU/GPU temperature and fan speed, with a
// labelled temperature axis on the left, a fan-speed axis on the right and a
// time axis along the bottom. One sample per monitor pass.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OmenMon.AppGui {

    internal sealed class GuiChart : Control {

        // History length, in samples. At the 2 s monitor tick 200 of these spanned about
        // six and a half minutes across roughly 490 px of plot — under 2.5 px per sample,
        // so a fan ramp or a thermal transient was a couple of pixels wide and the graph
        // showed that something had happened without showing its shape. 100 halves the
        // span to a little over three minutes and doubles the width of everything in it,
        // which is the resolution the recent past actually needs.
        private const int N = 100;                  // history length (samples)
        private const int TMin = 20, TMax = 100;    // left axis  °C
        private const int RMin = 0,  RMax = 6000;   // right axis rpm

        private readonly int[] cpuT = new int[N];
        private readonly int[] gpuT = new int[N];
        private readonly int[] cpuR = new int[N];
        private readonly int[] gpuR = new int[N];
        private int count;

        // Seconds between samples, used only to label the time axis.
        public int SampleSeconds = 3;

        private static readonly Color ColCpuT = Color.FromArgb(0xFF, 0x6B, 0x2E); // orange  CPU °C
        private static readonly Color ColGpuT = Color.FromArgb(0xF5, 0xC2, 0x0B); // yellow  GPU °C
        private static readonly Color ColCpuR = Color.FromArgb(0x3B, 0x82, 0xF6); // blue    CPU rpm
        private static readonly Color ColGpuR = Color.FromArgb(0x22, 0xD3, 0xEE); // cyan    GPU rpm

        public GuiChart() {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            this.BackColor = GuiTheme.Bg;
        }

        // Last plausible reading per series, used to bridge sensor dropouts. The EC/BIOS
        // occasionally answers 0 for GPU temperature (and 0 % for fan duty) between valid
        // samples; graphing those raw produces cliff-edge spikes that aren't real.
        private int lastCpuT, lastGpuT, lastCpuR, lastGpuR;

        private int Sane(int v, int lo, int hi, ref int last) {
            if(v >= lo && v <= hi) { last = v; return v; }
            return last;            // hold the previous good value across a dropout
        }

        public void Push(int cpuTemp, int gpuTemp, int cpuRpm, int gpuRpm) {
            cpuTemp = Sane(cpuTemp, 1, 120, ref lastCpuT);
            gpuTemp = Sane(gpuTemp, 1, 120, ref lastGpuT);
            cpuRpm  = Sane(cpuRpm,  1, 9000, ref lastCpuR);
            gpuRpm  = Sane(gpuRpm,  1, 9000, ref lastGpuR);
            if(count < N) {
                cpuT[count] = cpuTemp; gpuT[count] = gpuTemp;
                cpuR[count] = cpuRpm;  gpuR[count] = gpuRpm;
                count++;
            } else {
                Array.Copy(cpuT, 1, cpuT, 0, N - 1); cpuT[N - 1] = cpuTemp;
                Array.Copy(gpuT, 1, gpuT, 0, N - 1); gpuT[N - 1] = gpuTemp;
                Array.Copy(cpuR, 1, cpuR, 0, N - 1); cpuR[N - 1] = cpuRpm;
                Array.Copy(gpuR, 1, gpuR, 0, N - 1); gpuR[N - 1] = gpuRpm;
            }
            if(IsHandleCreated) Invalidate();
        }

        // Gap between a scale number and the plot frame it labels
        internal const int AxisGap = 5;

        // The scale gutters, measured rather than guessed.
        //
        // They were 44 and 52 — numbers wide enough for any label, which left the scale
        // numbers floating ten to fifteen pixels inside the section's text column. The
        // graph then read as indented relative to everything above it, which is most of
        // why this window looked restless. Measured from the widest label each side can
        // actually draw, the numbers start exactly on the left margin and end exactly on
        // the right one, so the graph occupies the same column as the text.
        //
        // Both gutters are shared with GuiCurveEditor, and deliberately so: the two
        // graphs are stacked, and if each sized its own the frames would not line up
        // even though both were individually correct. The curve editor has no right-hand
        // scale, but it adopts this right gutter anyway so the two frames end on one
        // pixel column.
        private static int axisLeft, axisRight;

        internal static int AxisLeft  { get { return axisLeft  > 0 ? axisLeft  : 30; } }
        internal static int AxisRight { get { return axisRight > 0 ? axisRight : 28; } }

        // Widest labels either stacked graph draws: this chart's temperature scale
        // ("100°"), and the rpm scale used by this chart's right axis and by the curve
        // editor's left axis ("6.0k"). Measured once, on the first paint that has a
        // Graphics to measure with.
        internal static void MeasureAxes(Graphics g, Font f) {
            if(axisLeft > 0) return;
            float temp = g.MeasureString(TMax + "°", f).Width;
            float rate = g.MeasureString((RMax / 1000.0).ToString("0.0") + "k", f).Width;
            axisLeft  = (int) Math.Ceiling(Math.Max(temp, rate)) + AxisGap;
            axisRight = (int) Math.Ceiling(rate) + AxisGap;
        }

        // Plot rectangle, leaving room for the three axes and the legend row.
        private Rectangle Plot {
            get { return new Rectangle(AxisLeft, 26,
                Math.Max(10, Width - AxisLeft - AxisRight), Math.Max(10, Height - 26 - 20)); }
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.Clear(GuiTheme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Before Plot is read: the gutters it returns are measured from the caption
            // font, and this is the first place with a Graphics to measure against.
            using(var fMeasure = GuiTheme.Ui(GuiTheme.FontCaption))
                MeasureAxes(g, fMeasure);

            Rectangle p = Plot;
            if(p.Width < 20 || p.Height < 20) return;

            using(var axisPen  = new Pen(GuiTheme.Border))
            using(var gridPen  = new Pen(Color.FromArgb(0x2A, 0x2A, 0x30)))
            using(var fAx      = GuiTheme.Ui(GuiTheme.FontCaption))
            using(var brMuted  = new SolidBrush(GuiTheme.Muted)) {

                // ---- left axis: temperature -------------------------------------
                for(int t = TMin; t <= TMax; t += 20) {
                    int y = YT(t, p);
                    g.DrawLine(gridPen, p.Left, y, p.Right, y);
                    string s = t + "°";
                    SizeF sz = g.MeasureString(s, fAx);
                    g.DrawString(s, fAx, brMuted, p.Left - sz.Width - 5, y - sz.Height / 2);
                }
                // ---- right axis: fan speed --------------------------------------
                for(int r = RMin; r <= RMax; r += 2000) {
                    int y = YR(r, p);
                    string s = (r / 1000.0).ToString("0.0") + "k";
                    g.DrawString(s, fAx, brMuted, p.Right + 5, y - 7);
                }
                // ---- frame ------------------------------------------------------
                g.DrawLine(axisPen, p.Left, p.Top, p.Left, p.Bottom);
                g.DrawLine(axisPen, p.Right, p.Top, p.Right, p.Bottom);
                g.DrawLine(axisPen, p.Left, p.Bottom, p.Right, p.Bottom);

                // ---- bottom axis: time -----------------------------------------
                int spanSec = (N - 1) * SampleSeconds;
                for(int k = 0; k <= 4; k++) {
                    int x = p.Left + k * p.Width / 4;
                    g.DrawLine(gridPen, x, p.Top, x, p.Bottom);
                    // Minutes once there are minutes to show, seconds below that. The
                    // integer division alone printed "-0m" for every tick under a minute,
                    // which at a three-minute span is a quarter of the axis.
                    int agoSec = spanSec - k * spanSec / 4;
                    string s = agoSec == 0 ? "now"
                        : agoSec < 60 ? "-" + agoSec + "s"
                        : "-" + (agoSec / 60) + "m" + (agoSec % 60 == 0 ? "" : (agoSec % 60).ToString("00"));
                    SizeF sz = g.MeasureString(s, fAx);
                    g.DrawString(s, fAx, brMuted, x - sz.Width / 2, p.Bottom + 3);
                }

                // No separate "°C" / "rpm" corner captions — they collided with the
                // topmost tick labels. The legend row already states both units.
            }

            if(count >= 2) {
                DrawSeries(g, p, cpuR, RMin, RMax, ColCpuR, 1.6f);
                DrawSeries(g, p, gpuR, RMin, RMax, ColGpuR, 1.6f);
                DrawSeries(g, p, cpuT, TMin, TMax, ColCpuT, 2.2f);
                DrawSeries(g, p, gpuT, TMin, TMax, ColGpuT, 2.2f);
            }

            // ---- legend with live values ---------------------------------------
            int i = count - 1;
            using(var fL = GuiTheme.Ui(GuiTheme.FontCaption)) {
                float x = p.Left;
                x = Legend(g, fL, x, ColCpuT, count > 0 ? "CPU " + cpuT[i] + "°" : "CPU –");
                x = Legend(g, fL, x, ColGpuT, count > 0 ? "GPU " + gpuT[i] + "°" : "GPU –");
                x = Legend(g, fL, x, ColCpuR, count > 0 ? "CPU " + cpuR[i] + " rpm" : "CPU – rpm");
                x = Legend(g, fL, x, ColGpuR, count > 0 ? "GPU " + gpuR[i] + " rpm" : "GPU – rpm");
            }
        }

        private float Legend(Graphics g, Font f, float x, Color c, string text) {
            using(var br = new SolidBrush(c)) {
                g.FillRectangle(br, x, 8, 9, 9);
                g.DrawString(text, f, br, x + 12, 3);
            }
            return x + 12 + g.MeasureString(text, f).Width + 14;
        }

        private int YT(int tempC, Rectangle p) {
            double f = (double)(tempC - TMin) / (TMax - TMin);
            return (int)(p.Bottom - f * p.Height);
        }
        private int YR(int rpm, Rectangle p) {
            double f = (double)(rpm - RMin) / (RMax - RMin);
            return (int)(p.Bottom - f * p.Height);
        }

        // Median of the sample and its two neighbours — kills single-sample spikes
        // without the lag a moving average would add.
        private int Median3(int[] d, int k) {
            int a = d[k > 0 ? k - 1 : k], b = d[k], c = d[k < count - 1 ? k + 1 : k];
            return Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
        }

        private void DrawSeries(Graphics g, Rectangle p, int[] data, int lo, int hi, Color c, float w) {
            var pts = new PointF[count];
            for(int k = 0; k < count; k++) {
                // Right-align: the newest sample (k == count-1) sits at "now" on the
                // right edge and history scrolls off to the left, so a partly-filled
                // buffer draws against the present instead of the start of the axis.
                double fx = (double)(k + N - count) / (N - 1);
                double v = Math.Max(lo, Math.Min(hi, Median3(data, k)));
                double fy = (v - lo) / (double)(hi - lo);
                pts[k] = new PointF((float)(p.Left + fx * p.Width), (float)(p.Bottom - fy * p.Height));
            }
            if(pts.Length < 2) return;
            using(var pen = new Pen(c, w) { LineJoin = LineJoin.Round })
                g.DrawLines(pen, pts);
        }
    }
}
