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

        private const int N = 200;                  // history length (samples)
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

        // Plot rectangle, leaving room for the three axes and the legend row.
        private Rectangle Plot {
            get { return new Rectangle(40, 26, Math.Max(10, Width - 40 - 52), Math.Max(10, Height - 26 - 20)); }
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.Clear(GuiTheme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle p = Plot;
            if(p.Width < 20 || p.Height < 20) return;

            using(var axisPen  = new Pen(GuiTheme.Border))
            using(var gridPen  = new Pen(Color.FromArgb(0x2A, 0x2A, 0x30)))
            using(var fAx      = new Font("Segoe UI", GuiTheme.FontSmall))
            using(var brMuted  = new SolidBrush(GuiTheme.Muted)) {

                // ---- left axis: temperature -------------------------------------
                for(int t = TMin; t <= TMax; t += 20) {
                    int y = YT(t, p);
                    g.DrawLine(gridPen, p.Left, y, p.Right, y);
                    string s = t + "°";
                    SizeF sz = g.MeasureString(s, fAx);
                    g.DrawString(s, fAx, brMuted, p.Left - sz.Width - 4, y - sz.Height / 2);
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
                    int agoSec = spanSec - k * spanSec / 4;
                    string s = agoSec == 0 ? "now" : "-" + (agoSec / 60) + "m";
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
            using(var fL = new Font("Segoe UI", GuiTheme.FontSmall)) {
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
