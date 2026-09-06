  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// GuiFormCurve — graphical editor for a fan program's temperature -> speed curve.
// X = temperature, Y = fan level (~rpm/100). Two independent lines (CPU / GPU) that
// share the breakpoint temperatures. Left-click empty space adds a breakpoint,
// drag moves the nearest point, right-click removes a breakpoint. Save writes the
// points back into Config.FanProgram and persists OmenMon.xml.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OmenMon.Hardware.Platform;
using OmenMon.Library;

namespace OmenMon.AppGui {

    internal sealed class GuiFormCurve : Form {

        private readonly GuiTray Ctx;
        private readonly ComboBox cmbProg = new ComboBox();
        private readonly Button btnApply = new Button();
        private readonly Button btnSave  = new Button();
        private readonly Button btnClose = new Button();
        private readonly Panel  graph    = new Panel();
        private readonly Label  hint     = new Label();

        private readonly int tMin = 20, tMax = 95;
        private readonly int lMin = Math.Max(0, Config.FanLevelMin);
        private readonly int lMax = Config.FanLevelMax;

        // Working copy: each row = { tempC, cpuLevel, gpuLevel }
        private List<int[]> pts = new List<int[]>();
        private int dragIdx = -1;
        private bool dragGpu;

        public GuiFormCurve(GuiTray ctx) {
            this.Ctx = ctx;
            this.Text = "Fan curve editor";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false; this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(560, 420);
            this.BackColor = GuiTheme.Bg; this.ForeColor = GuiTheme.Text;
            try { this.Font = new Font("Segoe UI", 9F); } catch { }
            this.Icon = OmenMon.Resources.Icon;

            cmbProg.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbProg.Location = new Point(12, 12); cmbProg.Size = new Size(180, 24);
            cmbProg.FlatStyle = FlatStyle.Flat; cmbProg.BackColor = GuiTheme.Panel; cmbProg.ForeColor = GuiTheme.Text;
            foreach(string n in Config.FanProgram.Keys) cmbProg.Items.Add(n);
            cmbProg.SelectedIndexChanged += (s, e) => LoadProgram(cmbProg.SelectedItem as string);

            StyleButton(btnApply, "Apply now", new Point(206, 11));
            StyleButton(btnSave,  "Save",      new Point(306, 11));
            StyleButton(btnClose, "Close",     new Point(386, 11));
            btnApply.Click += (s, e) => { Save(); ApplyIfRunning(); };
            btnSave.Click  += (s, e) => Save();
            btnClose.Click += (s, e) => Close();

            graph.Location = new Point(12, 46);
            graph.Size = new Size(this.ClientSize.Width - 24, this.ClientSize.Height - 80);
            graph.BackColor = GuiTheme.Panel;
            graph.Paint += GraphPaint;
            graph.MouseDown += GraphMouseDown;
            graph.MouseMove += GraphMouseMove;
            graph.MouseUp   += (s, e) => { dragIdx = -1; };
            graph.MouseLeave += (s, e) => { dragIdx = -1; };

            hint.AutoSize = false;
            hint.Location = new Point(12, this.ClientSize.Height - 28);
            hint.Size = new Size(this.ClientSize.Width - 24, 20);
            hint.ForeColor = GuiTheme.Muted;
            hint.Text = "Left-click empty area: add point   ·   drag: move   ·   right-click a point: remove   ·   orange = CPU, cyan = GPU";

            this.Controls.AddRange(new Control[] { cmbProg, btnApply, btnSave, btnClose, graph, hint });

            if(cmbProg.Items.Count > 0) cmbProg.SelectedIndex = 0;
        }

        private void StyleButton(Button b, string text, Point at) {
            b.Text = text; b.Location = at; b.Size = new Size(text.Length > 6 ? 90 : 72, 26);
            b.FlatStyle = FlatStyle.Flat; b.BackColor = GuiTheme.PanelHi; b.ForeColor = GuiTheme.Text;
            b.FlatAppearance.BorderColor = GuiTheme.Border;
        }

        private void LoadProgram(string name) {
            pts.Clear();
            if(name != null && Config.FanProgram.ContainsKey(name)) {
                foreach(var kv in Config.FanProgram[name].Level)
                    pts.Add(new int[] { kv.Key, kv.Value.Length > 0 ? kv.Value[0] : lMin,
                                                  kv.Value.Length > 1 ? kv.Value[1] : lMin });
            }
            if(pts.Count < 2) {
                pts.Clear();
                pts.Add(new int[] { tMin, lMin, lMin });
                pts.Add(new int[] { tMax, lMax, lMax });
            }
            Sort();
            graph.Invalidate();
        }

        private void Sort() { pts = pts.OrderBy(p => p[0]).ToList(); }

        private Rectangle Plot => new Rectangle(44, 10, graph.Width - 58, graph.Height - 40);

        private Point ToScreen(int tempC, int level) {
            var p = Plot;
            float fx = (float)(tempC - tMin) / (tMax - tMin);
            float fy = (float)(level - lMin) / Math.Max(1, lMax - lMin);
            return new Point((int)(p.Left + fx * p.Width), (int)(p.Bottom - fy * p.Height));
        }
        private int ToTemp(int sx) {
            var p = Plot;
            return Clamp((int)Math.Round(tMin + (sx - p.Left) / (float)p.Width * (tMax - tMin)), tMin, tMax);
        }
        private int ToLevel(int sy) {
            var p = Plot;
            return Clamp((int)Math.Round(lMin + (p.Bottom - sy) / (float)p.Height * (lMax - lMin)), lMin, lMax);
        }
        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        private void GraphPaint(object sender, PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.Clear(GuiTheme.Panel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var p = Plot;

            using(var axis = new Pen(GuiTheme.Border))
            using(var f = new Font("Segoe UI", 7.5f))
            using(var br = new SolidBrush(GuiTheme.Muted)) {
                for(int t = tMin; t <= tMax; t += 15) {
                    int x = ToScreen(t, lMin).X;
                    g.DrawLine(axis, x, p.Top, x, p.Bottom);
                    g.DrawString(t + "°", f, br, x - 8, p.Bottom + 4);
                }
                for(int l = lMin; l <= lMax; l += 10) {
                    int y = ToScreen(tMin, l).Y;
                    g.DrawLine(axis, p.Left, y, p.Right, y);
                    g.DrawString((l * 100).ToString(), f, br, 4, y - 6);
                }
                g.DrawString("rpm", f, br, 4, p.Top - 14);
            }

            DrawLine(g, false, Color.FromArgb(0xFF, 0x6B, 0x2E)); // CPU
            DrawLine(g, true,  Color.FromArgb(0x22, 0xD3, 0xEE)); // GPU
        }

        private void DrawLine(Graphics g, bool gpu, Color c) {
            if(pts.Count < 2) return;
            int yi = gpu ? 2 : 1;
            var scr = pts.Select(pt => (PointF)ToScreen(pt[0], pt[yi])).ToArray();
            using(var pen = new Pen(c, 2f) { LineJoin = LineJoin.Round })
                g.DrawLines(pen, scr);
            using(var br = new SolidBrush(c))
                foreach(var s in scr)
                    g.FillEllipse(br, s.X - 4, s.Y - 4, 8, 8);
        }

        private int HitTest(Point m, out bool gpu) {
            gpu = false; int best = -1; double bd = 12 * 12;
            for(int i = 0; i < pts.Count; i++) {
                foreach(bool g2 in new[] { false, true }) {
                    var s = ToScreen(pts[i][0], pts[i][g2 ? 2 : 1]);
                    double d = (s.X - m.X) * (s.X - m.X) + (s.Y - m.Y) * (s.Y - m.Y);
                    if(d < bd) { bd = d; best = i; gpu = g2; }
                }
            }
            return best;
        }

        private void GraphMouseDown(object sender, MouseEventArgs e) {
            int hit = HitTest(e.Location, out bool gpu);
            if(e.Button == MouseButtons.Right) {
                if(hit >= 0 && pts.Count > 2) { pts.RemoveAt(hit); Sort(); graph.Invalidate(); }
                return;
            }
            if(e.Button != MouseButtons.Left) return;
            if(hit >= 0) { dragIdx = hit; dragGpu = gpu; return; }
            // add a breakpoint at the clicked temperature, levels interpolated
            int t = ToTemp(e.X);
            if(pts.Any(pp => pp[0] == t)) return;
            pts.Add(new int[] { t, InterpAt(t, 1), InterpAt(t, 2) });
            Sort();
            dragIdx = pts.FindIndex(pp => pp[0] == t);
            dragGpu = (Math.Abs(ToScreen(t, InterpAt(t, 2)).Y - e.Y) < Math.Abs(ToScreen(t, InterpAt(t, 1)).Y - e.Y));
            graph.Invalidate();
        }

        private int InterpAt(int t, int yi) {
            if(pts.Count == 0) return lMin;
            var o = pts.OrderBy(p => p[0]).ToList();
            if(t <= o[0][0]) return o[0][yi];
            if(t >= o[o.Count - 1][0]) return o[o.Count - 1][yi];
            for(int i = 1; i < o.Count; i++)
                if(t <= o[i][0]) {
                    double f = (double)(t - o[i - 1][0]) / (o[i][0] - o[i - 1][0]);
                    return (int)Math.Round(o[i - 1][yi] + f * (o[i][yi] - o[i - 1][yi]));
                }
            return lMin;
        }

        private void GraphMouseMove(object sender, MouseEventArgs e) {
            if(dragIdx < 0 || dragIdx >= pts.Count) return;
            var pt = pts[dragIdx];
            pt[dragGpu ? 2 : 1] = ToLevel(e.Y);
            // allow moving temperature too, but not past neighbours
            int nt = ToTemp(e.X);
            int lo = dragIdx > 0 ? pts[dragIdx - 1][0] + 1 : tMin;
            int hi = dragIdx < pts.Count - 1 ? pts[dragIdx + 1][0] - 1 : tMax;
            pt[0] = Clamp(nt, lo, hi);
            graph.Invalidate();
        }

        private void Save() {
            string name = cmbProg.SelectedItem as string;
            if(name == null || !Config.FanProgram.ContainsKey(name)) return;
            Sort();
            var lvl = new SortedDictionary<byte, byte[]>();
            foreach(var pt in pts)
                lvl[(byte)Clamp(pt[0], 0, 255)] = new byte[] {
                    (byte)Clamp(pt[1], 0, 255), (byte)Clamp(pt[2], 0, 255) };
            Config.FanProgram[name].Level = lvl;
            try { Config.Save(); } catch { }
        }

        private void ApplyIfRunning() {
            string name = cmbProg.SelectedItem as string;
            if(name == null) return;
            try {
                if(Ctx.Op.Program.GetName() == name)
                    lock(Ctx.Op.HardwareLock) Ctx.Op.Program.Run(name);
            } catch { }
        }
    }
}
