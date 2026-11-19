  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// GuiChart — line chart for CPU/GPU temperature and fan speed, with a labelled
// temperature axis on the left, a fan-speed axis on the right and a time axis
// along the bottom.
//
// The chart is indexed by TIME, not by sample number. That is what lets the
// window open already populated from OmenMon-telemetry.csv: the log is written
// every TelemetryEvery monitor passes and the loop runs late under load, so its
// rows land 16-47 s apart, while live pushes arrive every pass (~2-3 s). On a
// sample-indexed axis those two densities would occupy the same width and the
// axis would silently lie about when anything happened. Plotted against real
// time they simply look like what they are: a coarse past and a fine present,
// with sample markers where the samples are sparse and a break in the line
// wherever the program was not running.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using OmenMon.Library;

namespace OmenMon.AppGui {

    internal sealed class GuiChart : Control {

        // Ring capacity, in samples. Sized for the longest span the selector offers:
        // 24 h of log rows at ~20 s apart is about 4300, and live pushes on top of that
        // still fit. This is a bound on memory, not on the window — the window is a
        // time span and is chosen separately.
        private const int Cap = 6000;

        // Selectable spans. The log now reaches back hours, so the graph should too;
        // before this the window was fixed at N × SampleSeconds and there was no way to
        // ask a different question of it.
        // Slot 0 is not a fixed span but a field: click it and type a number of minutes.
        // The presets cover the shapes you ask for repeatedly; the field covers "no, I
        // want forty" without adding four more buttons to a row that has no room.
        private static readonly int[] SpanSec   = { 0, 900, 3600, 6 * 3600, 24 * 3600 };
        private static readonly string[] SpanLabel = { null, "15m", "1h", "6h", "24h" };
        private const int CustomMinMax = 999;        // three digits, as asked
        private int customMin = 5;                   // 5 min: what the old fixed buffer spanned
        private bool editing;
        private string editBuf = "";
        private int windowSec = 300;

        private int SpanAt(int k) { return k == 0 ? Math.Max(1, customMin) * 60 : SpanSec[k]; }

        private string LabelAt(int k) {
            if(k != 0) return SpanLabel[k];
            if(editing) return (editBuf.Length == 0 ? "_" : editBuf) + "m";
            return customMin + "m";
        }

        // Drag-to-zoom overrides the span while set. Borrowed from ostris/ai-toolkit's
        // JobLossGraph, which gets this right: presets answer "show me the last hour",
        // a drag answers "show me THAT", and neither substitutes for the other.
        private bool hasZoom;
        private DateTime zoomFrom, zoomTo;

        // The temperature axis auto-ranges to what is actually on screen.
        //
        // Fixed 20-100 °C spent the plot on temperatures this machine does not reach: at
        // idle every trace sat in the bottom fifth and a 6 °C drift was two pixels. The
        // window starts at 25-50, which covers idle and light load, and grows to fit
        // whatever the samples need — so a load run still shows its whole shape, and the
        // moment it ends the axis comes back down.
        private const int TFloor = 25, TCeil = 50;   // the smallest window shown
        private const int TStep = 10;                // grow in whole steps, not per sample
        private const int TAbsMax = 105;

        private int tMin = TFloor, tMax = TCeil;     // current, after auto-ranging
        private const int RMin = 0,  RMax = 6000;   // right axis rpm

        private readonly DateTime[] ts = new DateTime[Cap];
        private readonly int[] cpuT = new int[Cap];
        private readonly int[] gpuT = new int[Cap];
        private readonly int[] cpuR = new int[Cap];
        private readonly int[] gpuR = new int[Cap];
        private int count;

        // The same four series again, but as they were actually read: no bridging, no
        // smoothing, and 0 meaning "there was no believable reading at this moment".
        //
        // Both layers are drawn. The dark one underneath is the measurement — every
        // quantised step and every bit of jitter the hardware really produced — and the
        // bright one on top is the trend. Keeping them together is what stops the smooth
        // line being mistaken for data: where the two diverge, the smoothing is doing the
        // talking, and you can see exactly how much.
        private readonly int[] cpuTraw = new int[Cap];
        private readonly int[] gpuTraw = new int[Cap];
        private readonly int[] cpuRraw = new int[Cap];
        private readonly int[] gpuRraw = new int[Cap];

        // There was a microphone trace here. It has been removed, and the reason is worth
        // keeping: the external logger samples anywhere from twice a second to once every
        // 25 seconds, and this chart bridged the gaps by repeating the last value. That
        // draws flat runs joined by sudden steps — a staircase with the shape of the
        // sampling, not of the sound. Every one of those corners was an artefact, and it
        // sat on the same axes as four real measurements, which lends it a credibility it
        // had not earned.
        //
        // A measurement that is only trustworthy at an irregular subset of moments cannot
        // be drawn as a continuous line at all. If it comes back it needs its own steady
        // sample clock and should be plotted as discrete points, so a gap in the data
        // reads as a gap. The dBFS columns in OmenMon-telemetry.csv are unaffected: they
        // are blank when there is no fresh reading, which is the honest representation.
        //
        // The seeded history below is the same problem solved properly rather than
        // avoided: sparse samples get markers and gaps get breaks, so the drawing says
        // where the measurements actually were.

        // Nominal seconds between live samples. Used to label nothing any more — the time
        // axis comes from the window — but still sets the threshold beyond which a step
        // between two samples counts as a gap rather than a slow tick.
        public int SampleSeconds = 3;

        private static readonly Color ColCpuT = Color.FromArgb(0xFF, 0x6B, 0x2E); // orange  CPU °C
        private static readonly Color ColGpuT = Color.FromArgb(0xF5, 0xC2, 0x0B); // yellow  GPU °C
        private static readonly Color ColCpuR = Color.FromArgb(0x3B, 0x82, 0xF6); // blue    CPU rpm
        private static readonly Color ColGpuR = Color.FromArgb(0x22, 0xD3, 0xEE); // cyan    GPU rpm

        public GuiChart() {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            // Selectable so the custom-span field can take keystrokes. Nothing else here
            // wants focus, and it is not in the tab order.
            this.SetStyle(ControlStyles.Selectable, true);
            this.TabStop = false;
            this.BackColor = GuiTheme.Bg;
        }

        // ---- data in ---------------------------------------------------------

        // Last plausible reading per series, used to bridge sensor dropouts. The EC/BIOS
        // occasionally answers 0 for GPU temperature (and 0 % for fan duty) between valid
        // samples; graphing those raw produces cliff-edge spikes that aren't real.
        private int lastCpuT, lastGpuT, lastCpuR, lastGpuR;

        private int Sane(int v, int lo, int hi, ref int last) {
            if(v >= lo && v <= hi) { last = v; return v; }
            return last;            // hold the previous good value across a dropout
        }

        // Believable or not — nothing in between. Below the floor is the sensor answering
        // 0 because it had nothing ready; above the ceiling is a decode that went wrong.
        // Neither is a measurement, and the raw layer shows a gap rather than inventing
        // one. The smoothed layer still bridges, because a trend line with a hole in it
        // is harder to read than one that carries the last known level for two seconds.
        private static bool Believable(int v, int lo, int hi) { return v >= lo && v <= hi; }

        public void Push(int cpuTemp, int gpuTemp, int cpuRpm, int gpuRpm) {
            PushAt(DateTime.Now, cpuTemp, gpuTemp, cpuRpm, gpuRpm);
        }

        private void PushAt(DateTime when, int cpuTemp, int gpuTemp, int cpuRpm, int gpuRpm) {

            // Raw first, before the bridging overwrites it. 0 = filtered out.
            int cpuTr = Believable(cpuTemp, 1, 120)  ? cpuTemp : 0;
            int gpuTr = Believable(gpuTemp, 1, 120)  ? gpuTemp : 0;
            int cpuRr = Believable(cpuRpm,  1, 9000) ? cpuRpm  : 0;
            int gpuRr = Believable(gpuRpm,  1, 9000) ? gpuRpm  : 0;

            cpuTemp = Sane(cpuTemp, 1, 120, ref lastCpuT);
            gpuTemp = Sane(gpuTemp, 1, 120, ref lastGpuT);
            cpuRpm  = Sane(cpuRpm,  1, 9000, ref lastCpuR);
            gpuRpm  = Sane(gpuRpm,  1, 9000, ref lastGpuR);

            if(count == Cap) {
                Array.Copy(ts,   1, ts,   0, Cap - 1);
                Array.Copy(cpuT, 1, cpuT, 0, Cap - 1);
                Array.Copy(gpuT, 1, gpuT, 0, Cap - 1);
                Array.Copy(cpuR, 1, cpuR, 0, Cap - 1);
                Array.Copy(gpuR, 1, gpuR, 0, Cap - 1);
                Array.Copy(cpuTraw, 1, cpuTraw, 0, Cap - 1);
                Array.Copy(gpuTraw, 1, gpuTraw, 0, Cap - 1);
                Array.Copy(cpuRraw, 1, cpuRraw, 0, Cap - 1);
                Array.Copy(gpuRraw, 1, gpuRraw, 0, Cap - 1);
                count--;
            }
            ts[count] = when;
            cpuT[count] = cpuTemp; gpuT[count] = gpuTemp;
            cpuR[count] = cpuRpm;  gpuR[count] = gpuRpm;
            cpuTraw[count] = cpuTr; gpuTraw[count] = gpuTr;
            cpuRraw[count] = cpuRr; gpuRraw[count] = gpuRr;
            count++;

            if(IsHandleCreated) Invalidate();
        }

        // Fill from OmenMon-telemetry.csv so the window opens showing the recent past
        // rather than an empty frame that takes a whole span to fill.
        //
        // Seeded for the longest selectable span, once, at startup: switching to 24 h
        // afterwards must not have to touch the disk, and re-reading on every switch
        // would put file IO behind a UI click.
        //
        // Also re-run every time the window goes from hidden back to visible (see the
        // call in GuiFormMain.EventFormVisibleChanged) — and that is why this skips
        // anything at or before the newest sample already held. GuiTray.Update() only
        // calls Chart.Push() while FormMain.Visible (GuiTray.cs, "Render the main form
        // ... only if visible"), which is right for the EC/BIOS-bound UI fields but
        // wrong for this ring buffer: it means live samples simply stop accumulating
        // the moment the window is hidden, for as long as it stays hidden. The disk
        // log does not stop — GuiMonitor.Pass() writes a telemetry heartbeat row every
        // TelemetryEvery seconds "even with the window closed" — so on 2026-09-23,
        // after 25 min with the window closed and then reopened on the 5 m span, the
        // chart showed only the 2-3 live pushes since reopening: the seeded backfill
        // was a single snapshot from construction time, 25 min outside that window,
        // and nothing had refilled the gap since. Re-seeding on reopen replays exactly
        // that gap from the log. Filtering to strictly-newer timestamps keeps a second
        // call from re-pushing rows the first one (or a previous reopen) already added
        // — this ring has no other way to de-duplicate a sample.
        public void SeedFromLog() {
            try {
                DateTime after = count > 0 ? ts[count - 1] : DateTime.MinValue;
                List<TelemetryLog.Sample> rows =
                    TelemetryLog.ReadRecent(TimeSpan.FromSeconds(SpanSec[SpanSec.Length - 1]), Cap);
                foreach(TelemetryLog.Sample s in rows)
                    if(s.Time > after) PushAt(s.Time, s.CpuC, s.GpuC, s.CpuRpm, s.GpuRpm);
            } catch { /* an unreadable log must not stop the window opening */ }
        }

        // ---- the visible window ----------------------------------------------

        // A step longer than this between neighbouring samples is a gap, not a slow tick:
        // the line breaks and the smoother refuses to reach across it. Generous relative
        // to the live rate because the telemetry log's own rows are 16-47 s apart and
        // those are continuous history, not gaps — only a stopped program should break
        // the trace.
        private double GapSeconds {
            get { return Math.Max(4 * Math.Max(1, SampleSeconds), 90); }
        }

        private void ViewRange(out DateTime from, out DateTime to) {
            if(hasZoom) { from = zoomFrom; to = zoomTo; return; }
            to = count > 0 && ts[count - 1] > DateTime.Now ? ts[count - 1] : DateTime.Now;
            from = to.AddSeconds(-windowSec);
        }

        // First and last sample inside [from, to], inclusive. One sample either side is
        // included when it exists so a trace enters and leaves the frame at the edge
        // instead of starting a few pixels in.
        private void VisibleRange(DateTime from, DateTime to, out int i0, out int i1) {
            i0 = 0; i1 = count - 1;
            if(count == 0) { i1 = -1; return; }
            // Last sample before the window, so the trace enters at the left edge
            for(int i = 0; i < count; i++) { if(ts[i] < from) i0 = i; else break; }
            // First sample after it, so it leaves at the right edge
            for(int i = count - 1; i >= 0; i--) { if(ts[i] > to) i1 = i; else break; }
            // Everything on record is older than the window — the program was off. Draw
            // nothing rather than pinning stale samples to the edge.
            if(i1 < i0) i1 = i0;
        }

        // Fit the temperature axis to the samples actually on screen, never smaller than
        // TFloor..TCeil and always on whole TStep boundaries.
        //
        // Snapping to steps is what makes this usable rather than distracting: ranging to
        // the exact min and max would move the axis on nearly every sample, so the traces
        // would crawl while the numbers beside them changed, and nothing would hold still
        // long enough to read. On a step boundary the axis is stable for a whole 10 °C of
        // drift and only jumps when the data genuinely leaves the window.
        //
        // Deliberately NOT the 2nd-98th percentile clipping ostris uses. On a loss curve
        // an outlier is noise worth hiding; here the 94 °C peak of a render is the single
        // thing you opened the window to see, and an axis that clipped it would be hiding
        // the answer. True min and max, and the Sane() guard above is what keeps a bad
        // read out of the range in the first place.
        private void AutoRange(int i0, int i1) {

            if(i1 < i0) return;

            int lo = int.MaxValue, hi = int.MinValue;
            for(int i = i0; i <= i1; i++) {
                if(cpuT[i] > 0) { if(cpuT[i] < lo) lo = cpuT[i]; if(cpuT[i] > hi) hi = cpuT[i]; }
                if(gpuT[i] > 0) { if(gpuT[i] < lo) lo = gpuT[i]; if(gpuT[i] > hi) hi = gpuT[i]; }
            }
            if(lo > hi) return;

            // A little air above and below, so a trace never runs along the frame
            lo -= 2; hi += 2;

            int nMin = Math.Min(TFloor, (int) (Math.Floor(lo / (double) TStep) * TStep));
            int nMax = Math.Max(TCeil,  (int) (Math.Ceiling(hi / (double) TStep) * TStep));

            if(nMax > TAbsMax) nMax = TAbsMax;
            if(nMin < 0) nMin = 0;

            this.tMin = nMin;
            this.tMax = nMax;

        }

        // ---- axes and layout --------------------------------------------------

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
            float temp = g.MeasureString(TAbsMax + "°", f).Width;
            float rate = g.MeasureString((RMax / 1000.0).ToString("0.0") + "k", f).Width;
            axisLeft  = (int) Math.Ceiling(Math.Max(temp, rate)) + AxisGap;
            axisRight = (int) Math.Ceiling(rate) + AxisGap;
        }

        // Plot rectangle, leaving room for the three axes and the legend row.
        private Rectangle Plot {
            get { return new Rectangle(AxisLeft, 26,
                Math.Max(10, Width - AxisLeft - AxisRight), Math.Max(10, Height - 26 - 20)); }
        }

        // ---- painting ---------------------------------------------------------

        private readonly Rectangle[] spanHit = new Rectangle[SpanSec.Length];
        private Rectangle zoomHit;

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

            DateTime from, to;
            ViewRange(out from, out to);
            double spanSec = Math.Max(1.0, (to - from).TotalSeconds);

            int i0, i1;
            VisibleRange(from, to, out i0, out i1);
            AutoRange(i0, i1);

            using(var axisPen  = new Pen(GuiTheme.Border))
            using(var gridPen  = new Pen(Color.FromArgb(0x2A, 0x2A, 0x30)))
            using(var fAx      = GuiTheme.Ui(GuiTheme.FontCaption))
            using(var brMuted  = new SolidBrush(GuiTheme.Muted)) {

                // ---- left axis: temperature -------------------------------------
                for(int t = this.tMin; t <= this.tMax; t += TStep) {
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
                // Labelled from the window, not from the sample count, so it stays true
                // whatever mix of log rows and live samples is on screen.
                // Picker first, labels second: the picker's width depends on the font and
                // The axis reads left to right the way the window does: the span sits on
                // the left, where the plot begins, and "0" on the right, where it is now.
                // The span is the picker itself — the number naming how far back the left
                // edge reaches is exactly the number you would want to change — so it is
                // one control, not a label beside a control.
                //
                // No intermediate "-2m30 / -1m15" labels while a preset is active. They
                // read as more buttons next to the picker, and they say nothing the two
                // ends and four evenly-spaced gridlines do not already say. A dragged
                // zoom is different: its bounds are wall-clock times nobody can infer, so
                // there the quarter marks get HH:mm.
                int gridTop = p.Top, gridBot = p.Bottom;
                for(int k = 0; k <= 4; k++) {
                    int x = p.Left + k * p.Width / 4;
                    g.DrawLine(gridPen, x, gridTop, x, gridBot);
                }

                DrawSpanPicker(g, fAx, p, p.Bottom + 3);

                string nowLab = "0";
                SizeF nowSz = g.MeasureString(nowLab, fAx);
                g.DrawString(nowLab, fAx, brMuted, p.Right - nowSz.Width, p.Bottom + 3);

                if(hasZoom)
                    for(int k = 1; k <= 3; k++) {
                        int x = p.Left + k * p.Width / 4;
                        string s = from.AddSeconds(spanSec * k / 4.0).ToString("HH:mm");
                        SizeF sz = g.MeasureString(s, fAx);
                        g.DrawString(s, fAx, brMuted, x - sz.Width / 2, p.Bottom + 3);
                    }
            }

            if(i1 > i0) {
                // Raw underneath, all four, then the smoothed lines over them: otherwise
                // one series' measurement would sit on top of another's trend.
                DrawRaw(g, p, cpuRraw, RMin, RMax, ColCpuR, i0, i1, from, spanSec);
                DrawRaw(g, p, gpuRraw, RMin, RMax, ColGpuR, i0, i1, from, spanSec);
                DrawRaw(g, p, cpuTraw, this.tMin, this.tMax, ColCpuT, i0, i1, from, spanSec);
                DrawRaw(g, p, gpuTraw, this.tMin, this.tMax, ColGpuT, i0, i1, from, spanSec);

                DrawSeries(g, p, cpuR, RMin, RMax, ColCpuR, 1.6f, i0, i1, from, spanSec);
                DrawSeries(g, p, gpuR, RMin, RMax, ColGpuR, 1.6f, i0, i1, from, spanSec);
                DrawSeries(g, p, cpuT, this.tMin, this.tMax, ColCpuT, 2.2f, i0, i1, from, spanSec);
                DrawSeries(g, p, gpuT, this.tMin, this.tMax, ColGpuT, 2.2f, i0, i1, from, spanSec);
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

            // ---- drag-to-zoom rubber band --------------------------------------
            if(dragging && Math.Abs(dragNowX - dragFromX) > 2) {
                int a = Math.Min(dragFromX, dragNowX), b = Math.Max(dragFromX, dragNowX);
                using(var br = new SolidBrush(Color.FromArgb(40, 0xF4, 0xF4, 0xF4)))
                using(var pn = new Pen(Color.FromArgb(120, 0xF4, 0xF4, 0xF4))) {
                    g.FillRectangle(br, a, p.Top, b - a, p.Height);
                    g.DrawLine(pn, a, p.Top, a, p.Bottom);
                    g.DrawLine(pn, b, p.Top, b, p.Bottom);
                }
            }
        }

        // The span picker sits at the right-hand end of the time-axis row, under the plot.
        //
        // It was in the legend band, which was wrong: that band is not "half empty", it
        // holds four live readouts ("CPU 37° GPU 36° CPU 1900 rpm GPU 1700 rpm") and at
        // this width they run the whole way across, so the picker landed on top of the
        // temperature figures. The axis row below has only four labels on quarter marks
        // and genuinely ends empty, and a time control belongs beside the time axis
        // anyway. No extra layout either way — the quad below depends on this control
        // keeping its exact size.
        // Returns the leftmost pixel it occupied, so the caller knows what room is left
        // for axis labels.
        private int DrawSpanPicker(Graphics g, Font f, Rectangle p, int y) {

            // Stale rects are live rects: hit-testing runs against whatever was last
            // stored, so anything not drawn this pass has to be cleared or it keeps
            // catching clicks at a position nothing is shown.
            for(int k = 0; k < spanHit.Length; k++) spanHit[k] = Rectangle.Empty;
            zoomHit = Rectangle.Empty;

            float x = p.Left;

            if(hasZoom) {
                string z = "✕ zoom";
                SizeF zs = g.MeasureString(z, f);
                zoomHit = new Rectangle((int) x, y - 1, (int) zs.Width + 2, 15);
                using(var br = new SolidBrush(GuiTheme.Text))
                    g.DrawString(z, f, br, x, y);
                return (int)(x + zs.Width);
            }

            // Never let the picker eat more than the left ~70 %: past that it stops
            // reading as a control on the axis and starts reading as the axis.
            float limit = p.Left + p.Width * 0.72f;

            for(int k = 0; k < SpanSec.Length; k++) {

                bool custom = k == 0;
                string lab = LabelAt(k);
                SizeF sz = g.MeasureString(lab, f);

                // The field keeps the width of its widest possible value, so the row does
                // not shuffle sideways under the cursor while a number is being typed.
                float w   = custom ? Math.Max(sz.Width, g.MeasureString(CustomMinMax + "m", f).Width) : sz.Width;
                float pad = custom ? 3f : 0f;
                if(x + w + 2 * pad > limit) break;

                bool on = windowSec == SpanAt(k);

                if(custom) {
                    // Always a bordered chip, never a bare word: this one is a field, not
                    // a button — clicking it asks for a number rather than picking one —
                    // and the box plus the lighter ink are the only things that say so.
                    // Fill brightens in three steps: dim when it is just sitting there,
                    // mid when its value is the active window, bright while being typed.
                    Color fill = editing ? GuiTheme.PanelHi : on ? GuiTheme.Panel : GuiTheme.Bg;
                    var box = new RectangleF(x, y - 1, w + 2 * pad, 14);
                    using(var bg = new SolidBrush(fill))
                        g.FillRectangle(bg, box);
                    using(var bd = new Pen(editing ? GuiTheme.Text : on ? GuiTheme.Warm : GuiTheme.Border))
                        g.DrawRectangle(bd, box.X, box.Y, box.Width, box.Height);
                }

                Color ink = custom ? (editing || on ? GuiTheme.Text : GuiTheme.Warm)
                                   : (on ? GuiTheme.Text : GuiTheme.Muted);
                using(var br = new SolidBrush(ink))
                    g.DrawString(lab, f, br, x + pad, y);

                if(custom && editing) {
                    // Caret at the end of the buffer
                    float cx = x + pad + g.MeasureString(LabelAt(0).TrimEnd('m'), f).Width;
                    using(var cpen = new Pen(GuiTheme.Text))
                        g.DrawLine(cpen, cx, y + 1, cx, y + 11);
                }

                // The selected preset is underlined. The field is not — its box already
                // marks it, and an underline there would just say "focused" twice.
                if(on && !custom)
                    using(var un = new Pen(ink))
                        g.DrawLine(un, x + pad, y + 12, x + pad + sz.Width, y + 12);

                spanHit[k] = new Rectangle((int) x, y - 1, (int)(w + 2 * pad) + 1, 15);
                x += w + 2 * pad + 7;
            }
            return (int)(x - 7);
        }

        private float Legend(Graphics g, Font f, float x, Color c, string text) {
            using(var br = new SolidBrush(c)) {
                g.FillRectangle(br, x, 8, 9, 9);
                g.DrawString(text, f, br, x + 12, 3);
            }
            return x + 12 + g.MeasureString(text, f).Width + 14;
        }

        private int YT(int tempC, Rectangle p) {
            double f = (double)(tempC - this.tMin) / (this.tMax - this.tMin);
            return (int)(p.Bottom - f * p.Height);
        }
        private int YR(int rpm, Rectangle p) {
            double f = (double)(rpm - RMin) / (RMax - RMin);
            return (int)(p.Bottom - f * p.Height);
        }

        // ---- smoothing --------------------------------------------------------

        // Median of the sample and its two neighbours — kills single-sample spikes
        // without the lag a moving average would add. Neighbours are only taken from
        // inside the same segment, so a sample next to a gap is not medianed against a
        // reading from before the program was restarted.
        private int Median3(int[] d, int k, int i0, int i1) {
            int lo = (k > i0 && !IsBreak(k)) ? k - 1 : k;
            int hi = (k < i1 && !IsBreak(k + 1)) ? k + 1 : k;
            int a = d[lo], b = d[k], c = d[hi];
            return Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
        }

        private bool IsBreak(int k) {
            return k > 0 && k < count && (ts[k] - ts[k - 1]).TotalSeconds > GapSeconds;
        }

        // Display smoothing, applied to all four traces alike.
        //
        // Two separate reasons these read as steps, and each needs its own answer. The
        // values are coarse — temperatures are whole degrees, and rpm on this board is
        // the fan level times 100, so it can only ever move in hundreds. And the samples
        // are joined by straight segments, which turns every one of those quantised steps
        // into a visible corner.
        //
        // This is presentation only. Nothing here feeds a fan curve — FanProgram reads
        // Platform.GetCpuTemperature() directly and never sees this array — so smoothing
        // it changes how the past looks, never what the fans do. That separation is the
        // reason it is safe to smooth as hard as legibility wants.
        //
        // The smoother is a zero-phase forward-backward EMA, taken from
        // ostris/ai-toolkit's JobLossGraph (emaWithNulls). Two bias-corrected EMA passes,
        // one from each end, blended at every point by their own confidence weight
        // w = 1 - (1-a)^n. Why this rather than the 1-2-3-2-1 kernel that was here: both
        // are lag-free, but a fixed five-tap kernel can only ever smooth five samples, and
        // at a 24 h window there are thousands of samples per pixel. The EMA's window
        // follows alpha, so it can be set from the on-screen density instead of the array
        // index — and the bias correction is what keeps the two ends from sagging toward
        // zero, which is the usual reason people avoid EMAs at the edges of a plot.
        //
        // The median still runs first, and the order still matters: a median removes a
        // lone outlier outright, where a mean would only dilute it, smearing one bad
        // sample across its whole window.
        private double[] Smooth(int[] d, int i0, int i1, double alpha) {

            int n = i1 - i0 + 1;
            var med = new double[n];
            for(int k = 0; k < n; k++) med[k] = Median3(d, i0 + k, i0, i1);

            var fv = new double[n]; var fw = new double[n];
            var bv = new double[n]; var bw = new double[n];

            EmaPass(med, alpha, false, i0, fv, fw);
            EmaPass(med, alpha, true,  i0, bv, bw);

            var outv = new double[n];
            for(int k = 0; k < n; k++) {
                double wsum = fw[k] + bw[k];
                outv[k] = wsum > 0 ? (fw[k] * fv[k] + bw[k] * bv[k]) / wsum : med[k];
            }
            return outv;
        }

        // One bias-corrected EMA pass. The accumulator resets at a segment boundary: a
        // gap means the program was not running, and carrying the level across it would
        // draw the old value leaking into the new run.
        private void EmaPass(double[] v, double alpha, bool reverse, int i0,
                             double[] vals, double[] weights) {
            double s = 0; int n = 0;
            int start = reverse ? v.Length - 1 : 0;
            int step  = reverse ? -1 : 1;
            for(int k = start; k >= 0 && k < v.Length; k += step) {
                // Boundary between k and its predecessor in reading order
                int edge = reverse ? i0 + k + 1 : i0 + k;
                if(IsBreak(edge)) { s = 0; n = 0; }
                s = alpha * v[k] + (1 - alpha) * s;
                n++;
                double w = 1 - Math.Pow(1 - alpha, n);
                vals[k] = s / w;
                weights[k] = w;
            }
        }

        private void DrawSeries(Graphics g, Rectangle p, int[] data, int lo, int hi,
                                Color c, float w, int i0, int i1, DateTime from, double spanSec) {

            int n = i1 - i0 + 1;
            if(n < 2) return;

            // Smoothing width follows what is on screen, not the array: aim for roughly
            // four pixels' worth of samples, so a 5 min window barely smooths and a 24 h
            // window — where thousands of samples share a pixel column — smooths hard.
            double perPx = n / (double) Math.Max(1, p.Width);
            double span  = Math.Max(3.0, perPx * 4.0);
            double alpha = Math.Max(0.05, Math.Min(0.6, 2.0 / (span + 1.0)));

            double[] sm = Smooth(data, i0, i1, alpha);

            var seg = new List<PointF>(n);
            var pts = new List<PointF[]>();
            var marks = new List<PointF>();

            float lastX = float.NaN;
            for(int k = 0; k < n; k++) {
                int idx = i0 + k;
                double fx = (ts[idx] - from).TotalSeconds / spanSec;
                double v  = Math.Max(lo, Math.Min(hi, sm[k]));
                double fy = (v - lo) / (double)(hi - lo);
                var pt = new PointF((float)(p.Left + fx * p.Width), (float)(p.Bottom - fy * p.Height));

                if(k > 0 && IsBreak(idx)) {              // program was not running here
                    if(seg.Count > 0) { pts.Add(seg.ToArray()); seg = new List<PointF>(); }
                    lastX = float.NaN;
                }

                // A sample more than a few pixels from the previous one is sparse enough
                // that the line between them is interpolation, not measurement. Marking
                // it says where the readings actually are — which is most of the seeded
                // history, and none of the live tail.
                if(!float.IsNaN(lastX) && pt.X - lastX > 8f) { marks.Add(pt); }
                lastX = pt.X;

                seg.Add(pt);
            }
            if(seg.Count > 0) pts.Add(seg.ToArray());

            using(var pen = new Pen(c, w) { LineJoin = LineJoin.Round })
            using(var br = new SolidBrush(c)) {
                var clip = g.Clip;
                g.SetClip(p);
                foreach(PointF[] s in pts) {
                    if(s.Length >= 3)
                        // A cardinal spline overshoots around a sharp change, and an
                        // overshoot here would draw a temperature the machine never
                        // reached. Clipping to the plot keeps that inside the frame, and
                        // the low tension keeps it small in the first place.
                        g.DrawCurve(pen, s, CurveTension);
                    else if(s.Length == 2)
                        g.DrawLines(pen, s);
                }
                foreach(PointF m in marks)
                    g.FillEllipse(br, m.X - 1.6f, m.Y - 1.6f, 3.2f, 3.2f);
                g.Clip = clip;
            }
        }

        private const float CurveTension = 0.35f;

        // Same hue, a third of the light. Dimming rather than picking a separate colour
        // keeps each measurement tied to its own trend line — the blue rpm reading stays
        // recognisably the blue series — and leaves the bright lines owning the fore-
        // ground, which is what makes eight traces in one frame still readable.
        private static Color Dim(Color c, float f) {
            return Color.FromArgb((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
        }

        // The measurement, exactly as it arrived: no median, no EMA, straight segments
        // rather than a spline, and a break wherever the reading was not believable or
        // the program was not running. Straight segments are the point — a spline here
        // would round off the quantisation steps, and those steps are the honest shape
        // of a sensor that only reports whole degrees and hundreds of rpm.
        private void DrawRaw(Graphics g, Rectangle p, int[] raw, int lo, int hi, Color c,
                             int i0, int i1, DateTime from, double spanSec) {

            var seg = new List<PointF>();
            var segs = new List<PointF[]>();

            for(int k = i0; k <= i1; k++) {
                if(raw[k] <= 0 || IsBreak(k)) {
                    if(seg.Count > 1) segs.Add(seg.ToArray());
                    seg.Clear();
                    if(raw[k] <= 0) continue;       // filtered out — leave the hole
                }
                double fx = (ts[k] - from).TotalSeconds / spanSec;
                double v  = Math.Max(lo, Math.Min(hi, raw[k]));
                double fy = (v - lo) / (double)(hi - lo);
                seg.Add(new PointF((float)(p.Left + fx * p.Width),
                                   (float)(p.Bottom - fy * p.Height)));
            }
            if(seg.Count > 1) segs.Add(seg.ToArray());
            if(segs.Count == 0) return;

            using(var pen = new Pen(Dim(c, 0.42f), 1f)) {
                var clip = g.Clip;
                g.SetClip(p);
                foreach(PointF[] s in segs) g.DrawLines(pen, s);
                g.Clip = clip;
            }
        }

        // ---- interaction ------------------------------------------------------

        private bool dragging;
        private int dragFromX, dragNowX;

        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);

            if(e.Button == MouseButtons.Right) {         // right-click anywhere: back to the span
                CancelEdit();
                if(hasZoom) { hasZoom = false; Invalidate(); }
                return;
            }
            if(e.Button != MouseButtons.Left) return;

            if(zoomHit != Rectangle.Empty && zoomHit.Contains(e.Location)) {
                CancelEdit(); hasZoom = false; Invalidate(); return;
            }
            for(int k = 0; k < spanHit.Length; k++)
                if(spanHit[k] != Rectangle.Empty && spanHit[k].Contains(e.Location)) {
                    if(k == 0) {
                        // The field: a click means "let me type", not "pick 5m". Seed the
                        // buffer with the current value so a small tweak is a keystroke,
                        // not a full re-entry, and take focus so keys arrive.
                        editing = true;
                        editBuf = customMin.ToString();
                        Focus();
                    } else {
                        CancelEdit();
                        windowSec = SpanAt(k);
                    }
                    hasZoom = false; Invalidate(); return;
                }

            CancelEdit();
            Rectangle p = Plot;
            if(p.Contains(e.Location)) { dragging = true; dragFromX = dragNowX = e.X; }
        }

        // ---- the custom-span field -----------------------------------------

        // Arrows and Return would otherwise never reach OnKeyDown on a Control.
        protected override bool IsInputKey(Keys k) {
            if(editing && (k == Keys.Return || k == Keys.Escape || k == Keys.Back
                || (k >= Keys.D0 && k <= Keys.D9) || (k >= Keys.NumPad0 && k <= Keys.NumPad9)))
                return true;
            return base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if(!editing) return;

            if(e.KeyCode == Keys.Escape) { CancelEdit(); e.Handled = true; return; }
            if(e.KeyCode == Keys.Return) { CommitEdit(); e.Handled = true; return; }
            if(e.KeyCode == Keys.Back) {
                if(editBuf.Length > 0) editBuf = editBuf.Substring(0, editBuf.Length - 1);
                e.Handled = true; Invalidate(); return;
            }

            int d = -1;
            if(e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) d = e.KeyCode - Keys.D0;
            else if(e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) d = e.KeyCode - Keys.NumPad0;
            if(d >= 0 && editBuf.Length < 3) {       // three digits, as asked
                editBuf += (char)('0' + d);
                e.Handled = true; Invalidate();
            }
        }

        // Clicking away commits rather than discards: the half-typed number is almost
        // always what was wanted, and losing it to a stray click is the more annoying
        // outcome. Escape is there for the other case.
        protected override void OnLostFocus(EventArgs e) {
            base.OnLostFocus(e);
            if(editing) CommitEdit();
        }

        private void CommitEdit() {
            if(!editing) return;
            editing = false;
            int m;
            if(int.TryParse(editBuf, out m) && m >= 1) {
                customMin = Math.Min(CustomMinMax, m);
                windowSec = SpanAt(0);
                hasZoom = false;
            }
            editBuf = "";
            Invalidate();
        }

        private void CancelEdit() {
            if(!editing) return;
            editing = false; editBuf = "";
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            if(dragging) { dragNowX = e.X; Invalidate(); return; }

            bool overPick = (zoomHit != Rectangle.Empty && zoomHit.Contains(e.Location));
            if(!overPick)
                for(int k = 0; k < spanHit.Length; k++)
                    if(spanHit[k] != Rectangle.Empty && spanHit[k].Contains(e.Location)) { overPick = true; break; }
            this.Cursor = overPick ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            if(!dragging) return;
            dragging = false;

            Rectangle p = Plot;
            int a = Math.Min(dragFromX, dragNowX), b = Math.Max(dragFromX, dragNowX);
            // A click is not a zoom. Below this the selection is narrower than the
            // gutters and almost certainly a misclick on the plot.
            if(b - a < 8) { Invalidate(); return; }

            DateTime from, to;
            ViewRange(out from, out to);
            double spanSec = Math.Max(1.0, (to - from).TotalSeconds);

            double fa = (a - p.Left) / (double) p.Width;
            double fb = (b - p.Left) / (double) p.Width;
            fa = Math.Max(0, Math.Min(1, fa));
            fb = Math.Max(0, Math.Min(1, fb));

            zoomFrom = from.AddSeconds(spanSec * fa);
            zoomTo   = from.AddSeconds(spanSec * fb);
            hasZoom  = true;
            Invalidate();
        }
    }
}
