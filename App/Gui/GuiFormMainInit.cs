  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// Main window layout — rebuilt from scratch (v1.4.12-reborn): minimal dark,
// generous spacing, four stacked sections. CPU/GPU fan rpm + temperature are the
// hero readout at the top; horizontal fan sliders; keyboard section kept and
// extended with R/G/B sliders + a live swatch. Control field names are unchanged
// so the behaviour code (GuiFormMain.cs) needs no edits beyond the temp/RGB hooks.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using OmenMon.Library;

namespace OmenMon.AppGui {

    public partial class GuiFormMain : Form {

#region Form Components
        private ButtonEx BtnFanSet;
        private Button BtnCurveEdit;          // saves the edited curve
        private Button BtnProfAdd;            // new profile
        private Button BtnProfDel;            // delete profile (not the 3 standard ones)
        private Button BtnMenu;               // shows the tray menu (right-click no longer does)
        private CheckBox ChkAutoStart;        // start OmenMon with Windows
        private Button BtnKbdColorPresetDel;
        private Button BtnKbdColorPresetRen;
        private Button BtnKbdColorPresetSet;
        private CheckBox ChkKbdBacklight;
        private ComboBox CmbFanMode;
        private ComboBox CmbFanProg;
        private ComboBox CmbKbdColorPreset;
        private ComboBox CmbKbdZone;          // which zone the R/G/B sliders drive (or All)
        internal GuiChart Chart;              // live temp / rpm graph
        internal GuiCurveEditor Curve;        // inline editable fan curve
        // One power selector: each choice sets the Windows power mode AND the CPU limits.
        private RadioButton RdoPwrEco, RdoPwrBal, RdoPwrPerf, RdoPwrCustom;
        private NumericUpDown NumPwrWatt;     // custom sustained wattage
        private Label LblPwrHint;
        private GroupBox GrpChart;
        private GroupBox GrpPwr;
        private GroupBox GrpFan;
        private GroupBox GrpKbd;
        private GroupBox GrpSys;
        private GroupBox GrpTmp;
        private Label LblFan0Cap;
        private Label LblFan0Rte;
        private Label LblFan0Val;
        private Label LblFan1Cap;
        private Label LblFan1Rte;
        private Label LblFan1Val;
        private Label LblFanCountdown;
        private Label LblCurveInfo;
        private Button BtnProfRen;
        private NumericUpDown NumCurveTemp, NumCurveRpm;

        // Shown when the pointer is not over the curve
        // Shown while one of the three reference curves is selected
        private const string LockedHelpText =
            "Reference curve - read-only. Press + to make an editable copy.";

        private const string CurveHelpText =
            "Left-click to add a point · drag to move · right-click to remove";
        private Label LblFanUnitRte;
        private Label LblFanUnitVal;
        private Label LblHdrRpm;
        private Label LblHdrTmp;
        private Label LblTmp0Cap, LblTmp0Val;
        private Label LblTmp1Cap, LblTmp1Val;
        private Label LblTmp2Cap, LblTmp2Val;
        private Label LblTmp3Cap, LblTmp3Val;
        private Label LblTmp4Cap, LblTmp4Val;
        private Label LblTmp5Cap, LblTmp5Val;
        private Label LblTmp6Cap, LblTmp6Val;
        private Label LblTmp7Cap, LblTmp7Val;
        private Label LblTmp8Cap, LblTmp8Val;
        private Panel PnlTmpHidden;
        private Panel PnlKbdSwatch;
        internal PictureBox PicKbd;
        private ProgressBarEx BarFan0Rte;
        private ProgressBarEx BarFan1Rte;
        private RadioButton RdoFanAuto;
        private RadioButton RdoFanConst;
        private RadioButton RdoFanMax;
        private RadioButton RdoFanOff;
        private RadioButton RdoFanProg;
        private RichTextBox RtfSysInfo;
        private TextBox TxtKbdColorVal;
        private ToolTip Tip;
        private TrackBar TrkFan0Lvl;
        private TrackBar TrkFan1Lvl;
#endregion

#region Initialization
        private void Initialize() {

            this.FormClosing += Config.GuiCloseWindowExit ?
                Context.Menu.EventActionExit : new FormClosingEventHandler(EventFormClosing);
            this.VisibleChanged += EventFormVisibleChanged;
            this.Shown += EventFormShown;
            this.HelpButtonClicked += EventActionHelp;

            this.FigureFont = new Font(
                GdiFont.Get("IoMon"), Config.GuiFigureFontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            Font heroFont = GuiTheme.Ui(GuiTheme.FontDisplay);
            Font capFont  = GuiTheme.Ui(GuiTheme.FontBody);

#region Instantiation
            this.BarFan0Rte = new ProgressBarEx();
            this.BarFan1Rte = new ProgressBarEx();
            this.BtnFanSet = new ButtonEx();
            this.BtnCurveEdit = new Button();
            this.BtnProfAdd = new Button();
            this.BtnProfDel = new Button();
            this.BtnMenu = new Button();
            this.BtnKbdColorPresetDel = new Button();
            this.BtnKbdColorPresetRen = new Button();
            this.BtnKbdColorPresetSet = new Button();
            this.ChkAutoStart = new CheckBox();
            this.ChkKbdBacklight = new CheckBox();
            this.CmbFanMode = new ComboBox();
            this.CmbFanProg = new ComboBox();
            this.CmbKbdColorPreset = new ComboBox();
            this.CmbKbdZone = new ComboBox();
            this.Chart = new GuiChart();
            this.Curve = new GuiCurveEditor();
            this.RdoPwrEco = new RadioButton();
            this.RdoPwrBal = new RadioButton();
            this.RdoPwrPerf = new RadioButton();
            this.RdoPwrCustom = new RadioButton();
            this.NumPwrWatt = new NumericUpDown();
            this.LblPwrHint = new Label();
            this.GrpChart = new GroupBox();
            this.GrpPwr = new GroupBox();
            this.GrpFan = new GroupBox();
            this.GrpKbd = new GroupBox();
            this.GrpSys = new GroupBox();
            this.GrpTmp = new GroupBox();
            this.LblFan0Cap = new Label();
            this.LblFan0Rte = new Label();
            this.LblFan0Val = new Label();
            this.LblFan1Cap = new Label();
            this.LblFan1Rte = new Label();
            this.LblFan1Val = new Label();
            this.LblFanCountdown = new Label();
            this.LblCurveInfo = new Label();
            this.BtnProfRen = new Button();
            this.NumCurveTemp = new NumericUpDown();
            this.NumCurveRpm = new NumericUpDown();
            this.LblFanUnitRte = new Label();
            this.LblFanUnitVal = new Label();
            this.LblHdrRpm = new Label();
            this.LblHdrTmp = new Label();
            this.LblTmp0Cap = new Label(); this.LblTmp0Val = new Label();
            this.LblTmp1Cap = new Label(); this.LblTmp1Val = new Label();
            this.LblTmp2Cap = new Label(); this.LblTmp2Val = new Label();
            this.LblTmp3Cap = new Label(); this.LblTmp3Val = new Label();
            this.LblTmp4Cap = new Label(); this.LblTmp4Val = new Label();
            this.LblTmp5Cap = new Label(); this.LblTmp5Val = new Label();
            this.LblTmp6Cap = new Label(); this.LblTmp6Val = new Label();
            this.LblTmp7Cap = new Label(); this.LblTmp7Val = new Label();
            this.LblTmp8Cap = new Label(); this.LblTmp8Val = new Label();
            this.PnlTmpHidden = new Panel();
            this.PnlKbdSwatch = new Panel();
            this.PicKbd = new PictureBox();
            this.RdoFanAuto = new RadioButton();
            this.RdoFanConst = new RadioButton();
            this.RdoFanMax = new RadioButton();
            this.RdoFanOff = new RadioButton();
            this.RdoFanProg = new RadioButton();
            this.RtfSysInfo = new RichTextBox();
            this.Tip = new ToolTip(this.Components);
            this.TrkFan0Lvl = new TrackBar();
            this.TrkFan1Lvl = new TrackBar();
            this.TxtKbdColorVal = new TextBox();
#endregion

            this.GrpChart.SuspendLayout();
            this.GrpPwr.SuspendLayout();
            this.GrpFan.SuspendLayout();
            this.GrpKbd.SuspendLayout();
            this.GrpSys.SuspendLayout();
            this.GrpTmp.SuspendLayout();
            this.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) this.PicKbd).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.TrkFan0Lvl).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.TrkFan1Lvl).BeginInit();

            const int W = 560;                 // section content width
            // Sections span the full client width, so the only horizontal margin in the
            // window is PAD. It used to be GX 16 *plus* PAD, which put text 26 px from
            // the edge and meant "the margin" was two numbers that had to be added up.
            const int GX = 0;                  // sections are full-width
            const int HDR = GuiTheme.CaptionBand; // first content row, below the caption rule
            const int GAP = 8;                 // gap between stacked sections
            const int PAD = GuiTheme.Pad;      // inner padding; the section caption and rule use it too

            // One horizontal grid for the whole window, so nothing is placed by eye.
            // Every control's left edge is PAD, COL2, or a multiple of GUT from one of
            // them; every right edge is W - PAD. The magic numbers this replaced put the
            // profile combo, the keyboard combo and the RGB sliders on four different
            // left margins, which is what made the window read as restless.
            const int GUT  = 8;                     // the single gutter between controls
            const int LBLW = 56;                    // caption column ("Profile", "CPU", ...)
            const int COL2 = PAD + LBLW + GUT;      // where a caption's control starts

            // The sensor block is two right-aligned number columns: rpm, then °C on the
            // right edge. Both the unit caption and the value below it use the same box,
            // so the digits and their label share one right edge instead of two that
            // happened to agree.
            const int RPMW = 166;                   // rpm column, starting at COL2
            const int TMPX = COL2 + RPMW + GUT;     // °C column, running to W - PAD
            Color cVal = GuiTheme.Text, cCap = GuiTheme.Muted;

            // The whole window has to fit within MAX_WINDOW_H, title bar included -- six
            // stacked sections had grown it to 1272 px, taller than the work area on a
            // 1152 px screen, so the bottom of it was simply unreachable.
            //
            // Every section except the two graphs is a fixed height, so those are
            // budgeted first and the graphs divide whatever is left. That keeps the
            // constraint true by construction: move a control and the graphs absorb it,
            // instead of the window silently growing past the screen again.
            // 1080 on a 1112 px work area. 960 was set when the sections were smaller and
            // left 32 px more headroom than the window needs; every pixel of it was coming
            // out of the graphs, which are the only elastic thing here.
            const int MAX_WINDOW_H = 1080;

            const int H_TMP = HDR + 100;   // hero readout: second row at r1 = HDR+58, 40 tall
            const int H_PWR = HDR + 74;    // radios, then the hint label at HDR+36, 34 tall

            // The keyboard picture's height, and the one knob for trading keyboard size
            // against graph size — they come out of the same 1080 px.
            //
            // Filling the content width would make it 177 px tall (540 x 393 / 1200) and
            // leave the two graphs on their 96 px floor, which is what "squeezed" was.
            // At 120 the picture is still drawn 366 x 120 — more than five times the area
            // of the 158 x 52 it started as, with each zone 90 px or wider — and the
            // graphs get the 57 px back.
            //
            // The control keeps the full content width and Zoom centres the picture in
            // it. The empty side bands are harmless now that GuiKbd.SetZone maps clicks
            // against the drawn rectangle and ignores anything outside it.
            const int H_KBDPIC = 120;

            // Picture at HDR + 32, then 8 px, the swatch row, the preset row and the hex
            // field (96 px in total), then the section's own bottom margin
            const int H_KBD = HDR + 32 + H_KBDPIC + 8 + 96 + GuiTheme.Pad;

            const int H_SYS = HDR + 88;    // autostart, exit, three lines of Consolas + margin

            // Chrome is the non-client height of a FixedSingle form with a caption
            int chrome = SystemInformation.CaptionHeight
                + 2 * SystemInformation.FixedFrameBorderSize.Height;

            // 7 gaps (above each of the six sections, plus one below the last); the
            // constant 52 is the fixed part of the two graph sections -- 2 * HDR for
            // their headers, less the 4 px the chart sits above its group bottom, plus
            // the 64 px of profile row, readout row and entry fields above the curve
            int graphBudget = MAX_WINDOW_H - chrome - 7 * GAP - 2 * HDR - 60
                - H_TMP - H_PWR - H_KBD - H_SYS;

            // Never collapse the graphs to nothing: if the fixed sections ever grow past
            // the budget, overflow the window rather than ship two unreadable slivers
            if(graphBudget < 192) graphBudget = 192;
            int hChart = graphBudget / 2;
            int hCurve = graphBudget - hChart;

#region Section: Graph
            // Pulled up into the caption band to give the plot height, but never above the
            // rule: derived from CaptionRuleY rather than the old magic "HDR - 8", which
            // happened to sit one pixel over the rule and hid it.
            this.Chart.Location = new Point(PAD, GuiTheme.CaptionRuleY + 2);
            this.Chart.Size = new Size(W - 2 * PAD, hChart);
            this.Chart.SampleSeconds = Math.Max(1, Config.UpdateMonitorInterval);
            this.GrpChart.Controls.Add(this.Chart);
            this.GrpChart.Size = new Size(W, HDR + hChart - 4);
            this.GrpChart.TabStop = false;
            this.GrpChart.Text = "History";
#endregion

#region Section: Sensors (hero readout)
            this.LblHdrRpm.AutoSize = false;
            this.LblHdrRpm.Font = capFont; this.LblHdrRpm.ForeColor = cCap;
            this.LblHdrRpm.Location = new Point(COL2, HDR);
            this.LblHdrRpm.Size = new Size(RPMW, 16);
            this.LblHdrRpm.TextAlign = ContentAlignment.MiddleRight;
            this.LblHdrRpm.Text = "rpm";

            this.LblHdrTmp.AutoSize = false;
            this.LblHdrTmp.Font = capFont; this.LblHdrTmp.ForeColor = cCap;
            this.LblHdrTmp.Location = new Point(TMPX, HDR);
            this.LblHdrTmp.Size = new Size(W - PAD - TMPX, 16);
            this.LblHdrTmp.TextAlign = ContentAlignment.MiddleRight;
            // Follows the unit setting, because the values below no longer carry one
            this.LblHdrTmp.Text = Config.TemperatureUseFahrenheit ? "°F" : "°C";

            int r0 = HDR + 18, r1 = r0 + 40;
            this.LblFan0Cap.Font = capFont; this.LblFan0Cap.ForeColor = cCap;
            this.LblFan0Cap.Location = new Point(PAD, r0 + 12);
            this.LblFan0Cap.Size = new Size(LBLW, 30);
            this.LblFan0Cap.Text = "CPU";

            this.LblFan0Val.Font = heroFont; this.LblFan0Val.ForeColor = cVal;
            this.LblFan0Val.Location = new Point(COL2, r0);
            this.LblFan0Val.Size = new Size(RPMW, 40);
            this.LblFan0Val.TextAlign = ContentAlignment.MiddleRight;

            this.LblTmp0Val.Font = heroFont; this.LblTmp0Val.ForeColor = cVal;
            this.LblTmp0Val.Location = new Point(TMPX, r0);
            this.LblTmp0Val.Size = new Size(W - PAD - TMPX, 40);
            this.LblTmp0Val.TextAlign = ContentAlignment.MiddleRight;

            this.LblFan1Cap.Font = capFont; this.LblFan1Cap.ForeColor = cCap;
            this.LblFan1Cap.Location = new Point(PAD, r1 + 12);
            this.LblFan1Cap.Size = new Size(LBLW, 30);
            this.LblFan1Cap.Text = "GPU";

            this.LblFan1Val.Font = heroFont; this.LblFan1Val.ForeColor = cVal;
            this.LblFan1Val.Location = new Point(COL2, r1);
            this.LblFan1Val.Size = new Size(RPMW, 40);
            this.LblFan1Val.TextAlign = ContentAlignment.MiddleRight;

            this.LblTmp1Val.Font = heroFont; this.LblTmp1Val.ForeColor = cVal;
            this.LblTmp1Val.Location = new Point(TMPX, r1);
            this.LblTmp1Val.Size = new Size(W - PAD - TMPX, 40);
            this.LblTmp1Val.TextAlign = ContentAlignment.MiddleRight;

            this.PnlTmpHidden.Location = new Point(0, 0);
            this.PnlTmpHidden.Size = new Size(1, 1);
            this.PnlTmpHidden.Visible = false;
            this.LblFanUnitVal.Visible = false; this.LblFanUnitRte.Visible = false;
            foreach(Label l in new[] {
                LblTmp0Cap, LblTmp1Cap, LblTmp2Cap, LblTmp2Val, LblTmp3Cap, LblTmp3Val,
                LblTmp4Cap, LblTmp4Val, LblTmp5Cap, LblTmp5Val, LblTmp6Cap, LblTmp6Val,
                LblTmp7Cap, LblTmp7Val, LblTmp8Cap, LblTmp8Val })
                this.PnlTmpHidden.Controls.Add(l);

            this.GrpTmp.Controls.Add(this.LblHdrRpm);
            this.GrpTmp.Controls.Add(this.LblHdrTmp);
            this.GrpTmp.Controls.Add(this.LblFan0Cap);
            this.GrpTmp.Controls.Add(this.LblFan0Val);
            this.GrpTmp.Controls.Add(this.LblTmp0Val);
            this.GrpTmp.Controls.Add(this.LblFan1Cap);
            this.GrpTmp.Controls.Add(this.LblFan1Val);
            this.GrpTmp.Controls.Add(this.LblTmp1Val);
            this.GrpTmp.Controls.Add(this.PnlTmpHidden);
            this.GrpTmp.Size = new Size(W, H_TMP);
            this.GrpTmp.TabStop = false;
            this.GrpTmp.Text = "Sensors";
#endregion

#region Section: Fan  (profiles only, with the curve edited inline)
            int fa = HDR;

            MakeMini("Profile", new Point(PAD, fa + 3), capFont, this.GrpFan);
            this.CmbFanProg.DropDownStyle = ComboBoxStyle.DropDownList;
            this.CmbFanProg.Location = new Point(COL2, fa);
            this.CmbFanProg.Size = new Size(156, 24);

            Action<Button, string, int, int> mkFanBtn = (b, t, x, w2) => {
                b.Text = t; b.Location = new Point(x, fa - 1); b.Size = new Size(w2, 26);
                b.FlatStyle = FlatStyle.Flat; b.BackColor = GuiTheme.PanelHi;
                b.ForeColor = GuiTheme.Text; b.FlatAppearance.BorderColor = GuiTheme.Border;
            };
            // The profile row, laid out left to right on the shared 8 px gutter:
            // caption, the profile itself, then the three things you can do to it
            // (+ copy, − delete, ✎ rename), then apply, then save, then the countdown.
            // The three single-glyph buttons stay narrow so a rename button fits without
            // pushing the countdown off the right edge.
            mkFanBtn(this.BtnProfAdd, "+", 244, 28);
            mkFanBtn(this.BtnProfDel, "−", 280, 28);
            mkFanBtn(this.BtnProfRen, "✎", 316, 28);
            this.Tip.SetToolTip(this.BtnProfAdd, "Copy this profile under a new name — the copy is editable.");
            this.Tip.SetToolTip(this.BtnProfDel, "Delete this profile. The three reference profiles cannot be deleted.");
            this.Tip.SetToolTip(this.BtnProfRen, "Rename this profile. The three reference profiles cannot be renamed.");
            mkFanBtn(this.BtnCurveEdit, "Save curve", 428, 78);

            this.BtnFanSet.HighlightColorDark = GuiTheme.Accent;
            this.BtnFanSet.HighlightColorLight = GuiTheme.Accent;
            this.BtnFanSet.HighlightWidth = 2;
            this.BtnFanSet.HighlightRadius = 2;
            this.BtnFanSet.ForeColor = GuiTheme.Text;
            this.BtnFanSet.BackColor = GuiTheme.PanelHi;
            // Sized from its own text, like the preset buttons. 68 px was 7 px short of
            // what "Apply" needs — in Segoe UI as well as Inter, so this predates the
            // font change and was simply never measured.
            this.BtnFanSet.Text = "Apply";
            this.BtnFanSet.Location = new Point(352, fa - 1);
            this.BtnFanSet.Size = new Size(FitButton(this.BtnFanSet, 68), 26);

            this.LblFanCountdown.Font = capFont; this.LblFanCountdown.ForeColor = cCap;
            this.LblFanCountdown.Location = new Point(W - PAD - 30, fa + 3);
            this.LblFanCountdown.Size = new Size(30, 20);
            this.LblFanCountdown.TextAlign = ContentAlignment.MiddleRight;

            // The row above the curve does two jobs: it says what the pointer is over,
            // and it lets the same point be typed in exactly.
            //
            // Dragging a handle is quick but cannot land on 65 °C / 3200 rpm precisely,
            // and until now the only feedback was the shape of the line. The readout
            // replaces a static instruction that did not fit on one line anyway — it was
            // rendering as "... right-click a point to" with the rest cut off.
            // The rpm box holds four digits plus the spinner arrows, so it is wider than
            // the temperature box next to it - at a shared 52 px "5800" was clipped.
            const int PRPMW = 70;
            const int PTMPW = 54;
            const int PUNITW = 26;
            const int PRPMX = W - PAD - PRPMW;
            const int PTMPX = PRPMX - GUT - PUNITW - GUT - PTMPW;

            this.LblCurveInfo.AutoSize = false;
            this.LblCurveInfo.Font = capFont;
            this.LblCurveInfo.ForeColor = GuiTheme.Muted;
            this.LblCurveInfo.Location = new Point(PAD, fa + 32);
            this.LblCurveInfo.Size = new Size(PTMPX - GUT - PAD, 20);
            this.LblCurveInfo.TextAlign = ContentAlignment.MiddleLeft;
            this.LblCurveInfo.Text = CurveHelpText;
            this.GrpFan.Controls.Add(this.LblCurveInfo);

            Action<NumericUpDown, int, int, int, int> mkPtNum = (n, x, w, lo, hi) => {
                n.Minimum = lo; n.Maximum = hi; n.Increment = 1;
                n.Location = new Point(x, fa + 30);
                n.Size = new Size(w, 24);
                n.TextAlign = HorizontalAlignment.Center;
                n.BorderStyle = BorderStyle.FixedSingle;
                n.BackColor = GuiTheme.Panel;
                n.ForeColor = GuiTheme.Text;
                n.Enabled = false;
                this.GrpFan.Controls.Add(n);
            };
            mkPtNum(this.NumCurveTemp, PTMPX, PTMPW, 30, 95);
            mkPtNum(this.NumCurveRpm, PRPMX, PRPMW, Config.FanLevelMin * 100, Config.FanLevelMax * 100);
            this.NumCurveRpm.Increment = 100;
            MakeMiniRight("°C", new Point(PTMPX + PTMPW, fa + 33), PUNITW, capFont, this.GrpFan);
            this.Tip.SetToolTip(this.NumCurveTemp,
                "Click a point on the curve, then type its exact temperature here.");
            this.Tip.SetToolTip(this.NumCurveRpm,
                "The selected point's fan speed in rpm, in steps of 100 — the hardware's own resolution.");

            // Inline curve editor, drawn like the history graph above
            this.Curve.Location = new Point(PAD, fa + 58);
            this.Curve.Size = new Size(W - 2 * PAD, hCurve);

            // Everything else the fan logic still reads is kept alive but off-screen:
            // the five mode radios (Profile is forced on), the constant-speed sliders,
            // the rate bars and the BIOS mode combo.
            this.RdoFanProg.Checked = true;
            this.CmbFanMode.DropDownStyle = ComboBoxStyle.DropDownList;
            this.TrkFan0Lvl.Minimum = Config.FanLevelMin; this.TrkFan0Lvl.Maximum = Config.FanLevelMax;
            this.TrkFan1Lvl.Minimum = Config.FanLevelMin; this.TrkFan1Lvl.Maximum = Config.FanLevelMax;

            this.GrpFan.Controls.Add(this.CmbFanProg);
            this.GrpFan.Controls.Add(this.BtnProfAdd);
            this.GrpFan.Controls.Add(this.BtnProfDel);
            this.GrpFan.Controls.Add(this.BtnFanSet);
            this.GrpFan.Controls.Add(this.BtnCurveEdit);
            this.GrpFan.Controls.Add(this.BtnProfRen);
            this.GrpFan.Controls.Add(this.LblFanCountdown);
            this.GrpFan.Controls.Add(this.Curve);
            this.GrpFan.Size = new Size(W, fa + hCurve + 64);
            this.GrpFan.TabStop = false;
            this.GrpFan.Text = "Fan profile";
#endregion

#region Section: Power
            // One selector. Each choice sets the Windows power mode *and* the CPU
            // wattage limits together — the user shouldn't have to know they are two
            // different layers. Selected = solid accent fill, so it is unmistakable.
            int pw = (W - 2 * PAD - 24) / 4;
            Action<RadioButton, string, int> mkPwr = (rb, t, i) => {
                rb.Text = t;
                rb.Appearance = Appearance.Button;
                rb.FlatStyle = FlatStyle.Flat;
                rb.TextAlign = ContentAlignment.MiddleCenter;
                rb.Location = new Point(PAD + i * (pw + 8), HDR);
                rb.Size = new Size(pw, 30);
                rb.BackColor = GuiTheme.Panel;
                rb.ForeColor = GuiTheme.Text;
                rb.FlatAppearance.BorderColor = GuiTheme.Border;
                rb.FlatAppearance.BorderSize = 1;
                rb.FlatAppearance.CheckedBackColor = GuiTheme.Accent;
                rb.FlatAppearance.MouseOverBackColor = GuiTheme.PanelHi;
            };
            mkPwr(this.RdoPwrEco,    "Eco",         0);
            mkPwr(this.RdoPwrBal,    "Balanced",    1);
            mkPwr(this.RdoPwrPerf,   "Performance", 2);
            mkPwr(this.RdoPwrCustom, "Custom",      3);

            // Custom takes ONE number — sustained watts. Boost, peak and the Windows
            // power mode are derived from it so the three can never disagree.
            // 25..54 W: the 7840HS cTDP window (45 W stock); the BIOS clamps above 54,
            // and below ~25 the part throttles without getting meaningfully cooler.
            // The spinner sits on the right edge; its caption is right-aligned against
            // it, and the hint takes everything left of that.
            //
            // This row had two overlaps. A "W" label was placed at W - 50, which is
            // inside the spinner that spans W - PAD - 54 to W - PAD, so the unit was
            // drawn on top of the number. And the hint was W - PAD - 174 wide, ending
            // past the "Sustained" caption at W - 196. Both are gone: the unit is part
            // of the caption, and every edge below is derived, not guessed.
            const int NUMW = 54;
            const int NUMX = W - PAD - NUMW;
            const int PCAPW = 84;
            const int PCAPX = NUMX - GUT - PCAPW;

            MakeMiniRight("Sustained W", new Point(PCAPX, HDR + 42), PCAPW, capFont, this.GrpPwr);
            this.NumPwrWatt.Minimum = 25;
            this.NumPwrWatt.Maximum = 54;
            this.NumPwrWatt.Value = 45;
            this.NumPwrWatt.Increment = 1;
            this.NumPwrWatt.Location = new Point(NUMX, HDR + 40);
            this.NumPwrWatt.Size = new Size(NUMW, 24);
            this.NumPwrWatt.TextAlign = HorizontalAlignment.Center;
            this.NumPwrWatt.BorderStyle = BorderStyle.FixedSingle;
            this.NumPwrWatt.BackColor = GuiTheme.Panel;
            this.NumPwrWatt.ForeColor = GuiTheme.Text;
            this.NumPwrWatt.Enabled = false;

            // Live description of what the selected preset actually did
            this.LblPwrHint.AutoSize = false;
            this.LblPwrHint.Font = capFont;
            this.LblPwrHint.ForeColor = GuiTheme.Muted;
            this.LblPwrHint.Location = new Point(PAD, HDR + 36);
            this.LblPwrHint.Size = new Size(PCAPX - GUT - PAD, 34);
            this.LblPwrHint.Text = "";

            this.Tip.SetToolTip(this.RdoPwrEco,
                "Windows power mode: Best efficiency.\nCPU: 30 W sustained, 45 W boost, 90 W peak.\nQuietest and coolest; longest battery life. Slower under sustained load.");
            this.Tip.SetToolTip(this.RdoPwrBal,
                "Windows power mode: Balanced.\nCPU: 45 W sustained, 65 W boost, 140 W peak.\nThe 7840HS stock 45 W TDP — the normal setting.");
            this.Tip.SetToolTip(this.RdoPwrPerf,
                "Windows power mode: Best performance.\nCPU: 54 W sustained, 80 W boost, 190 W peak.\nThe 54 W cTDP ceiling and your BIOS's full peak. Hotter and louder.");
            this.Tip.SetToolTip(this.RdoPwrCustom,
                "Set the sustained wattage yourself (25–54 W).\nBoost (~1.45x, max 80 W), peak (~3x, max 190 W) and the Windows\npower mode are derived from it, so they always stay consistent.");
            this.Tip.SetToolTip(this.NumPwrWatt,
                "Sustained CPU power. 45 W is the 7840HS stock TDP; 35–54 W is its cTDP window.");

            this.GrpPwr.Controls.Add(this.RdoPwrEco);
            this.GrpPwr.Controls.Add(this.RdoPwrBal);
            this.GrpPwr.Controls.Add(this.RdoPwrPerf);
            this.GrpPwr.Controls.Add(this.RdoPwrCustom);
            this.GrpPwr.Controls.Add(this.NumPwrWatt);
            this.GrpPwr.Controls.Add(this.LblPwrHint);
            this.GrpPwr.Size = new Size(W, H_PWR);
            this.GrpPwr.TabStop = false;
            this.GrpPwr.Text = "Power";
#endregion

#region Section: Keyboard
            this.ChkKbdBacklight.Location = new Point(PAD, HDR + 2);
            this.ChkKbdBacklight.AutoCheck = false;
            this.ChkKbdBacklight.Size = new Size(90, 24);   // "Backlight" needs this much
            const int KBDX = PAD + 90 + GUT;                // where the zone combo starts
            this.ChkKbdBacklight.Text = "Backlight";

            // Which zone the R/G/B sliders apply to (index 0 = All = uniform colour)
            this.CmbKbdZone.DropDownStyle = ComboBoxStyle.DropDownList;
            this.CmbKbdZone.Location = new Point(KBDX, HDR);
            this.CmbKbdZone.Size = new Size(W - PAD - KBDX, 26);
            this.CmbKbdZone.Items.AddRange(new object[] {
                "All zones (uniform)", "Left  (F1–F5)", "Middle  (F6–F12)", "Right  (nav / arrows)", "WASD" });
            this.CmbKbdZone.SelectedIndex = 0;
            this.CmbKbdZone.SelectedIndexChanged += EventKbdZoneSelect;

            // The keyboard is the control, so it gets the room.
            //
            // It was 540 x 52 with SizeMode.Zoom against a 1200 x 393 image. Zoom fits by
            // the constraining axis, which was the height, so the picture was drawn only
            // 158 px wide and centred — about 191 to 349 — with the rest of the control
            // empty. Every click that landed on the visible keyboard therefore fell
            // between 35% and 64% of the *control* width, and GuiKbd.SetZone, which
            // divided by that width, answered "Middle" every single time. Only the middle
            // zone could be selected, and it was the geometry, not the hit test.
            //
            // At the image's own aspect ratio the picture now fills the width exactly, so
            // there is no letterboxing left to mis-map, and each zone is a large target.
            this.PicKbd.Location = new Point(PAD, HDR + 32);
            this.PicKbd.Size = new Size(W - 2 * PAD, H_KBDPIC);
            this.PicKbd.SizeMode = PictureBoxSizeMode.Zoom;
            this.PicKbd.TabStop = false;

            // The R/G/B sliders are gone. Picking a colour by dragging three sliders is
            // worse than the colour dialog the swatch already opens, and they cost the
            // vertical room the keyboard needed to be clickable at all. The swatch shows
            // the selected zone's colour and opens the picker; the hex field below still
            // takes a typed value.
            int ry = HDR + 40 + H_KBDPIC;
            const int SWW = 24;

            this.PnlKbdSwatch.Location = new Point(W - PAD - SWW, ry + 3);
            this.PnlKbdSwatch.Size = new Size(SWW, 24);
            this.PnlKbdSwatch.BorderStyle = BorderStyle.FixedSingle;

            int py = ry + 40;
            this.CmbKbdColorPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            this.CmbKbdColorPreset.Location = new Point(PAD, py);
            this.CmbKbdColorPreset.Size = new Size(132, 26);

            // Laid out from the measured text: the fixed 56/60/56 px widths showed
            // "Rena" and "Delet". Each button takes what its label needs plus padding,
            // and the next one starts after it.
            this.BtnKbdColorPresetSet.Text = "Save…";
            this.BtnKbdColorPresetRen.Text = "Rename";
            this.BtnKbdColorPresetDel.Text = "Delete";

            int bx = PAD + 132 + GUT;
            foreach(Button b in new Button[] {
                this.BtnKbdColorPresetSet, this.BtnKbdColorPresetRen, this.BtnKbdColorPresetDel }) {
                int bw = 56;
                try {
                    bw = Math.Max(bw, TextRenderer.MeasureText(b.Text, this.Font).Width + 22);
                } catch { }
                b.Location = new Point(bx, py - 1);
                b.Size = new Size(bw, 27);
                bx += bw + GUT;
            }

            // Hex value on its own line under the preset row
            this.TxtKbdColorVal.CharacterCasing = CharacterCasing.Upper;
            this.TxtKbdColorVal.Location = new Point(PAD, py + 32);
            this.TxtKbdColorVal.MaxLength = 27;
            this.TxtKbdColorVal.Size = new Size(W - 2 * PAD, 24);
            this.TxtKbdColorVal.TextAlign = HorizontalAlignment.Center;

            this.GrpKbd.Controls.Add(this.ChkKbdBacklight);
            this.GrpKbd.Controls.Add(this.CmbKbdZone);
            this.GrpKbd.Controls.Add(this.PicKbd);
            this.GrpKbd.Controls.Add(this.PnlKbdSwatch);
            this.GrpKbd.Controls.Add(this.CmbKbdColorPreset);
            this.GrpKbd.Controls.Add(this.BtnKbdColorPresetSet);
            this.GrpKbd.Controls.Add(this.BtnKbdColorPresetRen);
            this.GrpKbd.Controls.Add(this.BtnKbdColorPresetDel);
            this.GrpKbd.Controls.Add(this.TxtKbdColorVal);
            this.GrpKbd.Size = new Size(W, H_KBD);
            this.GrpKbd.TabStop = false;
            // Drawn by PaintGroupBox with Graphics.DrawString, which does not treat "&"
            // as a mnemonic - so the old strip-the-ampersand dance deleted the "&" from
            // "Keyboard Backlight & Color" and left two spaces in the caption.
            this.GrpKbd.Text = Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_KBD);
#endregion

#region Section: System
            this.ChkAutoStart.AutoCheck = false;
            this.ChkAutoStart.Location = new Point(PAD, HDR);
            this.ChkAutoStart.Size = new Size(220, 22);
            this.ChkAutoStart.Text = "Start with Windows";

            // The tray menu is gone from the UI — Exit is the one thing it was still
            // needed for, so it gets a real button. (Middle-clicking the tray icon
            // still reveals the old menu for the few legacy toggles not in the window.)
            this.BtnMenu.Text = "Exit";
            this.BtnMenu.FlatStyle = FlatStyle.Flat;
            this.BtnMenu.BackColor = GuiTheme.PanelHi;
            this.BtnMenu.ForeColor = GuiTheme.Text;
            this.BtnMenu.FlatAppearance.BorderColor = GuiTheme.Border;
            this.BtnMenu.Location = new Point(W - PAD - 90, HDR - 3);
            this.BtnMenu.Size = new Size(90, 27);
            this.BtnMenu.Click += (s, e) => Application.Exit();

            this.RtfSysInfo.BorderStyle = BorderStyle.None;
            this.RtfSysInfo.Cursor = Cursors.IBeam;
            this.RtfSysInfo.DetectUrls = false;

            // Left enabled, unlike before: a disabled RichTextBox greys its whole content,
            // which on the dark ground was most of why this strip was unreadable. ReadOnly
            // plus TabStop=false keeps it inert, and being enabled also means the line can
            // be selected and copied — useful, since this is the block worth quoting in a
            // bug report.
            this.RtfSysInfo.Enabled = true;
            this.RtfSysInfo.ReadOnly = true;
            this.RtfSysInfo.BackColor = GuiTheme.Bg;
            this.RtfSysInfo.ForeColor = GuiTheme.Text;

            this.RtfSysInfo.Location = new Point(PAD, HDR + 26);
            this.RtfSysInfo.ScrollBars = RichTextBoxScrollBars.None;
            this.RtfSysInfo.ShortcutsEnabled = false;
            this.RtfSysInfo.Size = new Size(W - 2 * PAD, 54);
            this.RtfSysInfo.TabStop = false;
            this.RtfSysInfo.WordWrap = false;

            // Monospace. The content is deliberately terse — board IDs, wattages, D-states,
            // enum names — and in a proportional face those run together into one grey
            // ribbon. A fixed pitch gives the values a column to sit in and makes the
            // three lines scannable without changing a word of what they say.
            try {
                this.RtfSysInfo.Font = new Font(GuiTheme.FaceMono, GuiTheme.FontCaption, FontStyle.Regular, GraphicsUnit.Point);
            } catch {
                if(Config.GuiSysInfoFontSize > 0)
                    this.RtfSysInfo.Font = new Font(
                        Gui.DIALOG_FONT, Config.GuiSysInfoFontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            this.GrpSys.Controls.Add(this.ChkAutoStart);
            this.GrpSys.Controls.Add(this.BtnMenu);
            this.GrpSys.Controls.Add(this.RtfSysInfo);
            this.GrpSys.Size = new Size(W, H_SYS);
            this.GrpSys.TabStop = false;
            this.GrpSys.Text = Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_SYS);   // see GrpKbd above
#endregion

            // Stack the sections with a consistent gap
            this.GrpChart.Location = new Point(GX, GuiTheme.Pad);
            this.GrpTmp.Location = new Point(GX, this.GrpChart.Bottom + GAP);
            this.GrpFan.Location = new Point(GX, this.GrpTmp.Bottom + GAP);
            this.GrpPwr.Location = new Point(GX, this.GrpFan.Bottom + GAP);
            this.GrpKbd.Location = new Point(GX, this.GrpPwr.Bottom + GAP);
            this.GrpSys.Location = new Point(GX, this.GrpKbd.Bottom + GAP);

#region Main Form
            this.Controls.Add(this.GrpChart);
            this.Controls.Add(this.GrpTmp);
            this.Controls.Add(this.GrpFan);
            this.Controls.Add(this.GrpPwr);
            this.Controls.Add(this.GrpKbd);
            this.Controls.Add(this.GrpSys);

            try { this.Font = GuiTheme.Ui(GuiTheme.FontBody); } catch { }
            this.AutoScaleMode = AutoScaleMode.None;
            this.AutoSize = false;
            // Same 10 px below the last section as above the first and beside them all
            this.ClientSize = new Size(W + 2 * GX, this.GrpSys.Bottom + GuiTheme.Pad);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.HelpButton = true;
            this.Icon = OmenMon.Resources.Icon;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = Gui.T_FRM + "Main";
            this.SizeGripStyle = SizeGripStyle.Hide;
            this.StartPosition = FormStartPosition.CenterScreen;
            // Title: "OmenMon Reborn 8BCA". Config.DisplayName is the single source for
            // the user-facing name - the About box and the error report use the same one,
            // so they cannot drift apart.
            this.Text = Config.DisplayName;
#endregion

#region Tool Tips
            this.Tip.InitialDelay = 250;
            this.Tip.ReshowDelay = 120;
            this.Tip.AutoPopDelay = 12000;

            this.Tip.SetToolTip(this.LblFan0Val, "Live CPU fan speed (rpm).");
            this.Tip.SetToolTip(this.LblFan1Val, "Live GPU fan speed (rpm). 0 = the GPU fan has parked (idle).");
            this.Tip.SetToolTip(this.LblTmp0Val, "CPU temperature.");
            this.Tip.SetToolTip(this.LblTmp1Val, "GPU temperature.");

            this.Tip.SetToolTip(this.RdoFanAuto, "Hand fan control back to the BIOS. Fans follow the firmware curve for the selected mode.");
            this.Tip.SetToolTip(this.RdoFanMax, "Force both fans to maximum speed. Loud — use briefly.");
            this.Tip.SetToolTip(this.RdoFanOff, "Stop both fans. Only when cool and idle — they resume automatically on heat.");
            this.Tip.SetToolTip(this.RdoFanConst, "Hold the fans at the speeds set by the two sliders below.");
            this.Tip.SetToolTip(this.RdoFanProg, "Run a temperature→speed curve from OmenMon.xml (chosen at right).");
            this.Tip.SetToolTip(this.CmbFanProg, "Fan-curve program to run when 'Program' is selected.");
            this.Tip.SetToolTip(this.CmbFanMode, "Firmware performance profile (Default / Performance / Cool). Applied on Set.");
            this.Tip.SetToolTip(this.TrkFan0Lvl, "CPU fan target. ~20 = quiet, 55 ≈ maximum. Needs 'Custom' + Set.");
            this.Tip.SetToolTip(this.TrkFan1Lvl, "GPU fan target. ~20 = quiet, 55 ≈ maximum. Needs 'Custom' + Set.");
            this.Tip.SetToolTip(this.LblFan0Rte, "CPU fan actual duty (%).");
            this.Tip.SetToolTip(this.LblFan1Rte, "GPU fan actual duty (%).");
            this.Tip.SetToolTip(this.LblFanCountdown, "Seconds until the firmware reclaims fan control and this manual setting expires.");
            this.Tip.SetToolTip(this.BtnFanSet, "Apply the selected fan option.");

            this.Tip.SetToolTip(this.ChkKbdBacklight, "Toggle the keyboard backlight on or off.");
            this.Tip.SetToolTip(this.CmbKbdZone, "Choose which zone the R/G/B sliders change. 'All zones' sets the whole keyboard to one uniform colour.");
            this.Tip.SetToolTip(this.PicKbd, "Click a zone to edit it (and pick its colour in the full dialog). Zones are separated by the grooves.");
            this.Tip.SetToolTip(this.PnlKbdSwatch, "Live preview of the selected zone's colour.");
            this.Tip.SetToolTip(this.CmbKbdColorPreset, "Load a saved keyboard colour preset.");
            this.Tip.SetToolTip(this.BtnKbdColorPresetSet, "Save the current colours as a new preset.");
            this.Tip.SetToolTip(this.BtnKbdColorPresetDel, "Delete the selected preset.");
            this.Tip.SetToolTip(this.TxtKbdColorVal, "Zone colours as hex (RRGGBB:RRGGBB:RRGGBB:RRGGBB). Editable.");
#endregion

#region Resume Layout
            ((System.ComponentModel.ISupportInitialize) this.PicKbd).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.TrkFan0Lvl).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.TrkFan1Lvl).EndInit();

            this.GrpChart.ResumeLayout(false);
            this.GrpPwr.ResumeLayout(false);
            this.GrpFan.ResumeLayout(false);
            this.GrpKbd.ResumeLayout(false);
            this.GrpSys.ResumeLayout(false);
            this.GrpTmp.ResumeLayout(false);
            this.ResumeLayout(false);
            this.GrpKbd.PerformLayout();


            // Control Names — some handlers (EventFanRdoChanged, UpdateFanCtl) and the
            // old lookups identify controls by Name, so restore the originals.
            this.RdoFanAuto.Name  = Gui.T_RDO + Gui.G_FAN + "Auto";
            this.RdoFanMax.Name   = Gui.T_RDO + Gui.G_FAN + "Max";
            this.RdoFanOff.Name   = Gui.T_RDO + Gui.G_FAN + "Off";
            this.RdoFanConst.Name = Gui.T_RDO + Gui.G_FAN + "Const";
            this.RdoFanProg.Name  = Gui.T_RDO + Gui.G_FAN + "Prog";
            this.CmbFanMode.Name  = Gui.T_CMB + Gui.G_FAN + "Mode";
            this.CmbFanProg.Name  = Gui.T_CMB + Gui.G_FAN + "Prog";
            this.TrkFan0Lvl.Name  = Gui.T_TRK + Gui.G_FAN + "0" + Gui.S_LVL;
            this.TrkFan1Lvl.Name  = Gui.T_TRK + Gui.G_FAN + "1" + Gui.S_LVL;
            this.CmbKbdColorPreset.Name = Gui.T_CMB + Gui.G_KBD + "ColorPreset";
            this.PicKbd.Name = Gui.T_PIC + Gui.G_KBD;

            // Control event wiring (unchanged from the original layout — restored here
            // after the from-scratch rebuild; the handlers all live in GuiFormMain.cs).
            this.CmbFanProg.SelectionChangeCommitted += EventFanProgramChanged;
            this.CmbFanProg.SelectionChangeCommitted += EventProfilePicked;
            this.BtnProfAdd.Click += EventProfileAdd;
            this.BtnProfDel.Click += EventProfileDel;
            this.BtnCurveEdit.Click += EventCurveSave;
            this.BtnProfRen.Click += EventProfileRename;
            this.CmbFanMode.SelectionChangeCommitted += EventFanModeChanged;
            this.RdoFanAuto.CheckedChanged  += EventFanRdoChanged;
            this.RdoFanConst.CheckedChanged += EventFanRdoChanged;
            this.RdoFanOff.CheckedChanged   += EventFanRdoChanged;
            this.RdoFanMax.CheckedChanged   += EventFanRdoChanged;
            this.RdoFanProg.CheckedChanged  += EventFanRdoChanged;
            this.TrkFan0Lvl.ValueChanged += EventFanTrkChanged;
            this.TrkFan1Lvl.ValueChanged += EventFanTrkChanged;
            // Profiles are the only fan mode now — force the program branch on Apply.
            this.BtnFanSet.Click += (s, e) => { this.RdoFanProg.Checked = true; };
            this.BtnFanSet.Click += EventActionFanSet;
            this.RdoPwrEco.CheckedChanged    += EventPwrPreset;
            this.RdoPwrBal.CheckedChanged    += EventPwrPreset;
            this.RdoPwrPerf.CheckedChanged   += EventPwrPreset;
            this.RdoPwrCustom.CheckedChanged += EventPwrPreset;
            this.NumPwrWatt.ValueChanged     += EventPwrWattChanged;
            this.Curve.HoverChanged      += EventCurveHover;
            this.Curve.SelectionChanged  += EventCurveSelection;
            this.NumCurveTemp.ValueChanged += EventCurvePointEdit;
            this.NumCurveRpm.ValueChanged  += EventCurvePointEdit;
            this.ChkAutoStart.Click += EventActionAutoStart;
            this.ChkKbdBacklight.Click += EventActionBacklight;
            this.CmbKbdColorPreset.SelectionChangeCommitted += EventColorPreset;
            this.TxtKbdColorVal.TextChanged += EventColorInput;
            this.BtnKbdColorPresetDel.Click += EventActionColorPresetDel;
            this.BtnKbdColorPresetRen.Click += EventActionColorPresetRen;
            this.BtnKbdColorPresetSet.Click += EventActionColorPresetSet;
            this.PicKbd.MouseClick += EventColorPick;
#endregion
        }

        // Width comes from the text, not from a constant. The fixed 44 px this replaces
        // clipped "Sustained" to "Susta" and "Profile" to "Profil", and would have gone
        // on clipping anything longer — including after the type scale changed.

        // Guard against the feedback loop: writing the selected point into the entry
        // fields raises ValueChanged, which would write straight back into the curve.
        private bool curveSyncing;

        // Pointer moved over the curve: say what is under it. Falls back to the help
        // text when the pointer leaves, so the row is never blank.
        private void EventCurveHover(object sender, EventArgs e) {
            var h = this.Curve.Hover;
            if(!h.Valid) {
                this.LblCurveInfo.Text = this.Curve.ReadOnly ? LockedHelpText : CurveHelpText;
                return;
            }
            this.LblCurveInfo.Text = (h.Gpu ? "GPU" : "CPU") + "  "
                + h.TempC + " °C  ·  " + (h.Level * 100) + " rpm"
                + (this.Curve.ReadOnly ? "   (read-only)" : h.OnPoint ? "" : "   (click to add a point here)");
        }

        // A point was selected or dragged: mirror it into the entry fields
        private void EventCurveSelection(object sender, EventArgs e) {
            var s = this.Curve.Selection;
            this.curveSyncing = true;
            try {
                this.NumCurveTemp.Enabled = s.Valid;
                this.NumCurveRpm.Enabled = s.Valid;
                if(s.Valid) {
                    this.NumCurveTemp.Value = Clamp(s.TempC, this.NumCurveTemp.Minimum, this.NumCurveTemp.Maximum);
                    this.NumCurveRpm.Value = Clamp(s.Level * 100, this.NumCurveRpm.Minimum, this.NumCurveRpm.Maximum);
                }
            } catch { } finally { this.curveSyncing = false; }
        }

        // A number was typed: move the selected point there
        private void EventCurvePointEdit(object sender, EventArgs e) {
            if(this.curveSyncing) return;
            try {
                this.Curve.TrySetSelected((int) this.NumCurveTemp.Value,
                    (int) Math.Round(this.NumCurveRpm.Value / 100m));
            } catch { }
        }

        private static decimal Clamp(decimal v, decimal lo, decimal hi) {
            return v < lo ? lo : v > hi ? hi : v;
        }

        private void MakeMini(string text, Point at, Font f, Control parent) {
            int w = 44;
            try {
                w = Math.Max(w, TextRenderer.MeasureText(text, f).Width + 6);
            } catch { }
            var l = new Label {
                AutoSize = false, Text = text, Font = f, ForeColor = GuiTheme.Muted,
                Location = at, Size = new Size(w, 22), TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
            parent.Controls.Add(l);
        }

        // Same as MakeMini, but the text is right-aligned inside a fixed box, so a
        // caption can be anchored against the control it labels instead of drifting
        // with its own string length.
        private void MakeMiniRight(string text, Point at, int width, Font f, Control parent) {
            var l = new Label {
                AutoSize = false, Text = text, Font = f, ForeColor = GuiTheme.Muted,
                Location = at, Size = new Size(width, 22),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };
            parent.Controls.Add(l);
        }

        // Width a button needs for its own label, never less than the layout asked for.
        // Hardcoded widths are how "Apply" ended up 7 px short and the preset buttons
        // read "Rena" and "Delet" — a width measured once against one font, in a window
        // that has since changed font twice.
        private int FitButton(Control b, int minimum) {
            try {
                return Math.Max(minimum,
                    TextRenderer.MeasureText(b.Text, b.Font ?? this.Font).Width + 22);
            } catch {
                return minimum;
            }
        }

        private void SetupRgbSlider(TrackBar t, Point at, int width) {
            t.AutoSize = false;
            t.Location = at;
            t.Minimum = 0;
            t.Maximum = 255;
            t.TickFrequency = 64;
            t.TickStyle = TickStyle.None;
            t.Orientation = Orientation.Horizontal;
            t.Size = new Size(width, 30);
            t.TabStop = false;
        }
#endregion

    }
}
