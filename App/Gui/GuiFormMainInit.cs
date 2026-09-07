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
        private Button BtnUpdate;             // update check, reporting state in its own label
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

        // Longest of the button's states, so its width is fixed once at startup
        private const string UpdateTextIdle = "Check for updates";

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
        internal GuiColorPicker KbdPicker;    // inline SV square + hue strip
        private Button BtnKbdPipette;         // screen colour sampler
        private NumericUpDown NumKbdR, NumKbdG, NumKbdB;
        private Label LblKbdR, LblKbdG, LblKbdB;
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

            // The form's font is set HERE, before anything is measured or sized.
            //
            // It used to be assigned near the end of this method, after every control had
            // been laid out. FitButton measures a button's label in `b.Font ?? this.Font`,
            // so until that assignment it was measuring in the default Windows font — and
            // Inter is wider. Every button sized from its own text came out too narrow:
            // "Check for updates" was the visible one, but Apply, Save and the keyboard
            // preset buttons were all measured against the wrong face.
            //
            // Controls created after this line also inherit it, which is what makes the
            // measurement and the rendering agree.
            try { this.Font = capFont; } catch { }

#region Instantiation
            this.BarFan0Rte = new ProgressBarEx();
            this.BarFan1Rte = new ProgressBarEx();
            this.BtnFanSet = new ButtonEx();
            this.BtnCurveEdit = new Button();
            this.BtnProfAdd = new Button();
            this.BtnProfDel = new Button();
            this.BtnMenu = new Button();
            this.BtnUpdate = new Button();
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
            this.KbdPicker = new GuiColorPicker();
            this.BtnKbdPipette = new Button();
            this.NumKbdR = new NumericUpDown(); this.NumKbdG = new NumericUpDown(); this.NumKbdB = new NumericUpDown();
            this.LblKbdR = new Label(); this.LblKbdG = new Label(); this.LblKbdB = new Label();
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

            // Quad layout: a square window divided into four areas, two columns by two
            // rows, every panel tiled and nothing overlapping or hidden.
            //
            // This replaces a single 560 px column that had to be either scrolled or
            // squeezed. The content was ~1375 px tall against 1112 px of screen while the
            // window used 560 px of a 2048 px display — the height problem was really a
            // refusal to use the width. Two columns halve the stack and the graphs stop
            // being the thing that pays for every other section.
            //
            //     +---------------------+---------------------+
            //     |  History (graph)    |  Fan (profile+curve)|   monitoring / control
            //     +---------------------+---------------------+
            //     |  Keyboard           |  Sensors            |
            //     |                     |  Power              |
            //     |                     |  System             |
            //     +---------------------+---------------------+
            //
            const int WIN = 960;                                 // square, client area
            const int CROSS = 20;                                // the black cross between quadrants
            const int GAP = 8;                                   // minor gap, within a quadrant
            const int PAD_ = 10;                                 // window margin (GuiTheme.Pad)

            // Exact squares: 960 = 10 + 460 + 20 + 460 + 10, both ways. The 20 px cross
            // and the 10 px border are the window's black ground showing through between
            // the panels, which are the only thing painted in the panel colour.
            const int W = (WIN - 2 * PAD_ - CROSS) / 2;          // one column = 460
            const int ROW = (WIN - 2 * PAD_ - CROSS) / 2;        // one row    = 460
            const int HDR = GuiTheme.CaptionBand; // first content row, below the caption rule
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

            // The bottom-right quadrant is three panels that must total exactly one row,
            // gaps included, so its square matches the other three. The surplus over what
            // the controls need is shared out rather than left at the bottom: each panel
            // gets a third, and the rows inside it spread to fill (see the sections).
            const int H_TMP_MIN = HDR + 100;   // hero readout: second row at HDR+58, 40 tall
            const int H_PWR_MIN = HDR + 176;   // two radio rows, the wattage row, four hint lines
            const int H_SYS_MIN = HDR + 88;    // autostart, buttons, three lines of Consolas

            const int H_SLACK = (ROW - 2 * GAP - H_TMP_MIN - H_PWR_MIN - H_SYS_MIN) / 3;

            const int H_TMP = H_TMP_MIN + H_SLACK;
            const int H_PWR = H_PWR_MIN + H_SLACK;
            // The last one takes the rounding, so the three always sum to exactly ROW
            const int H_SYS = ROW - 2 * GAP - H_TMP - H_PWR;

            // The keyboard picture spans its column, so its edges sit on the same margin
            // as every caption, rule and control. The height follows from the native
            // aspect of Resources\Keyboard.png and is not a free choice.
            const int KBD_IMG_W = 1200, KBD_IMG_H = 393;         // Resources\Keyboard.png
            const int H_KBDPIC = (W - 2 * GuiTheme.Pad) * KBD_IMG_H / KBD_IMG_W;

            // Picture at HDR + 32, then 8 px, the swatch row, the preset row and the hex
            // field (96 px in total), then the section's own bottom margin
            // The keyboard panel fills its square; the picture is centred in the space
            // above the control rows, which sit at the bottom
            const int H_KBD = ROW;


            // The two graphs fill their quadrants. They are still the elastic thing in the
            // layout, but a quadrant is a fixed share of the window rather than whatever
            // the other five sections left over — which is how they ended up at 96 px.
            //
            // The curve gives up 64 px the history does not need: the profile row above it
            // and the readout / entry row below it.
            int hChart = ROW - (GuiTheme.CaptionRuleY + 2) - PAD;   // fills its square
            int hCurve = ROW - (HDR + 58) - PAD;                    // fills the rest of its square

#region Section: Graph
            // Pulled up into the caption band to give the plot height, but never above the
            // rule: derived from CaptionRuleY rather than the old magic "HDR - 8", which
            // happened to sit one pixel over the rule and hid it.
            this.Chart.Location = new Point(PAD, GuiTheme.CaptionRuleY + 2);
            this.Chart.Size = new Size(W - 2 * PAD, hChart);
            this.Chart.SampleSeconds = Math.Max(1, Config.UpdateMonitorInterval);
            this.GrpChart.Controls.Add(this.Chart);
            this.GrpChart.Size = new Size(W, ROW);
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
            this.CmbFanProg.Size = new Size(120, 24);

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
            // Laid out left to right on the shared gutter rather than at five written-down
            // x values. Those were 244 / 280 / 316 / 352 / 428, spaced 36, 36, 36 and 76
            // — three even steps and then whatever was left — and they assumed Apply was
            // 68 px wide. Sizing Apply from its text moved its right edge to 427, one
            // pixel from Save curve at 428, which is what "not centred" was: the two
            // buttons had no gutter between them while everything else in the row did.
            int fx = COL2 + 120 + GUT;                       // after the profile combo
            foreach(Button b in new Button[] {
                this.BtnProfAdd, this.BtnProfDel, this.BtnProfRen }) {
                mkFanBtn(b, b == this.BtnProfAdd ? "+" : b == this.BtnProfDel ? "−" : "✎", fx, 28);
                fx += 28 + GUT;
            }
            this.Tip.SetToolTip(this.BtnProfAdd, "Copy this profile under a new name — the copy is editable.");
            this.Tip.SetToolTip(this.BtnProfDel, "Delete this profile. The three reference profiles cannot be deleted.");
            this.Tip.SetToolTip(this.BtnProfRen, "Rename this profile. The three reference profiles cannot be renamed.");

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
            this.BtnFanSet.Location = new Point(fx, fa - 1);
            this.BtnFanSet.Size = new Size(FitButton(this.BtnFanSet, 68), 26);
            fx += this.BtnFanSet.Width + GUT;

            // Restores a button that went missing when this row was rewritten onto the
            // gutter: the call that positioned and labelled it was dropped, so it sat at
            // (0, 0) with no text, on top of the Profile caption. "Save" rather than
            // "Save curve" because the curve it saves is directly below it.
            mkFanBtn(this.BtnCurveEdit, "Save", fx, FitButton(this.BtnCurveEdit, 56));
            this.Tip.SetToolTip(this.BtnCurveEdit,
                "Save the edited curve to this profile. Reference profiles are read-only.");

            // The countdown lives in the panel's caption band, right-aligned. That strip
            // is empty in every panel, the value is a status readout rather than a
            // control, and the profile row needs its width for buttons.
            this.LblFanCountdown.Font = capFont; this.LblFanCountdown.ForeColor = cCap;
            this.LblFanCountdown.Location = new Point(W - PAD - 44, 0);
            this.LblFanCountdown.Size = new Size(44, GuiTheme.CaptionRuleY);
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
            this.GrpFan.Size = new Size(W, ROW);
            this.GrpFan.TabStop = false;
            this.GrpFan.Text = "Fan profile";
#endregion

#region Section: Power
            // One selector. Each choice sets the Windows power mode *and* the CPU
            // wattage limits together — the user shouldn't have to know they are two
            // different layers. Selected = solid accent fill, so it is unmistakable.
            // Two by two, not four across.
            //
            // Four in a row gave each 104 px, and "Performance" was clipped in it. The
            // measurement said it fitted with 12 px to spare — and the same measurement
            // said "Backlight" fitted in its 90 px, which it did not either. A
            // toggle-styled RadioButton reports a smaller preferred size than it actually
            // paints, so a ten-pixel margin is not a margin. Two columns give each button
            // 216 px, which is not a margin that needs measuring to trust, and the panel
            // has the height for a second row now that the quadrant shares its surplus.
            const int PWCOLS = 2;
            int pw = (W - 2 * PAD - GUT) / PWCOLS;
            const int PWH = 28;

            Action<RadioButton, string, int> mkPwr = (rb, t, i) => {
                rb.Text = t;
                rb.Appearance = Appearance.Button;
                rb.FlatStyle = FlatStyle.Flat;
                rb.TextAlign = ContentAlignment.MiddleCenter;
                rb.Location = new Point(
                    PAD + (i % PWCOLS) * (pw + GUT),
                    HDR + (i / PWCOLS) * (PWH + GUT));
                rb.Size = new Size(pw, PWH);
                rb.BackColor = GuiTheme.Panel;
                rb.ForeColor = GuiTheme.Text;
                rb.FlatAppearance.BorderColor = GuiTheme.Border;
                rb.FlatAppearance.BorderSize = 1;
                rb.FlatAppearance.CheckedBackColor = GuiTheme.Accent;
                rb.FlatAppearance.MouseOverBackColor = GuiTheme.PanelHi;

                // Selection is an inversion, not a colour: light ground, dark text.
                // FlatAppearance has a CheckedBackColor but no CheckedForeColor, so the
                // text has to be flipped by hand — without this the label would be
                // near-white on the near-white checked ground and simply disappear.
                EventHandler invert = delegate {
                    rb.ForeColor = rb.Checked ? GuiTheme.OnAccent : GuiTheme.Text;
                };
                rb.CheckedChanged += invert;
                invert(rb, EventArgs.Empty);
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

            MakeMiniRight("Sustained W", new Point(PCAPX, HDR + 74), PCAPW, capFont, this.GrpPwr);
            this.NumPwrWatt.Minimum = 25;
            this.NumPwrWatt.Maximum = 54;
            this.NumPwrWatt.Value = 45;
            this.NumPwrWatt.Increment = 1;
            // Below the two button rows (2 x 28 + one gutter = 64)
            this.NumPwrWatt.Location = new Point(NUMX, HDR + 72);
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
            // Full width, below the wattage row — beside the spinner it was 286 px wide
            // and the CPU-limits line was simply cut off, which is why the panel appeared
            // to say nothing but the Windows mode.
            this.LblPwrHint.Location = new Point(PAD, HDR + 104);
            this.LblPwrHint.Size = new Size(W - 2 * PAD, 62);
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
            // Width comes from the control, not from a number written here. The 90 px this
            // replaces was measured once, against a different font, and "Backlight" was
            // rendering as "Backlig". Nothing downstream assumes a width either: the zone
            // combo starts after whatever the checkbox turned out to need.
            this.ChkKbdBacklight.Location = new Point(PAD, HDR + 2);
            this.ChkKbdBacklight.AutoCheck = false;
            this.ChkKbdBacklight.Text = "Backlight";
            this.ChkKbdBacklight.AutoSize = true;
            int KBDX = PAD + this.ChkKbdBacklight.PreferredSize.Width + GUT;

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

            // The colour picker lives here, under the keyboard it colours, instead of in
            // a modal dialog over the window. Picking a zone colour means looking at the
            // picture directly above while you drag — a dialog that covers it (and, until
            // it was given a position, opened in the corner of the screen) makes the one
            // comparison that matters impossible.
            //
            // Same parts as the Windows picker: a saturation/value square with a hue
            // strip, R/G/B boxes, a hex field, a swatch and an eyedropper.
            const int SWW = 34, H_PRESETROW = 27, H_HEXROW = 24, H_RGBROW = 24;
            const int H_PICKER = 108;

            int ry = HDR + 32 + H_KBDPIC + GUT;

            // Square + hue strip on the left; the numbers stack down the right
            const int PKRIGHTW = 148;
            this.KbdPicker.Location = new Point(PAD, ry);
            this.KbdPicker.Size = new Size(W - 2 * PAD - GUT - PKRIGHTW, H_PICKER);
            this.GrpKbd.Controls.Add(this.KbdPicker);

            int rx = W - PAD - PKRIGHTW;

            // Swatch and eyedropper share the top line of the right-hand column
            this.PnlKbdSwatch.Location = new Point(rx, ry);
            this.PnlKbdSwatch.Size = new Size(SWW, H_RGBROW);
            this.PnlKbdSwatch.BorderStyle = BorderStyle.FixedSingle;

            this.BtnKbdPipette.Text = "⛏";
            this.BtnKbdPipette.FlatStyle = FlatStyle.Flat;
            this.BtnKbdPipette.BackColor = GuiTheme.PanelHi;
            this.BtnKbdPipette.ForeColor = GuiTheme.Text;
            this.BtnKbdPipette.FlatAppearance.BorderColor = GuiTheme.Border;
            this.BtnKbdPipette.Location = new Point(rx + SWW + GUT, ry - 1);
            this.BtnKbdPipette.Size = new Size(30, 26);
            this.GrpKbd.Controls.Add(this.BtnKbdPipette);
            this.Tip.SetToolTip(this.BtnKbdPipette,
                "Pick a colour from anywhere on the screen. Click to sample, Esc to cancel.");

            // R / G / B, one per line, right under the swatch
            Action<Label, NumericUpDown, string, int> mkRgb = (lbl, num, name, i) => {
                int y = ry + H_RGBROW + GUT + i * (H_RGBROW + 4);
                lbl.Text = name; lbl.Font = capFont; lbl.ForeColor = cCap;
                lbl.AutoSize = false;
                lbl.Location = new Point(rx, y + 3);
                lbl.Size = new Size(16, 20);
                this.GrpKbd.Controls.Add(lbl);

                num.Minimum = 0; num.Maximum = 255; num.Increment = 1;
                num.Location = new Point(rx + 20, y);
                num.Size = new Size(56, H_RGBROW);
                num.TextAlign = HorizontalAlignment.Center;
                num.BorderStyle = BorderStyle.FixedSingle;
                num.BackColor = GuiTheme.Panel;
                num.ForeColor = GuiTheme.Text;
                num.ValueChanged += EventKbdRgbNum;
                this.GrpKbd.Controls.Add(num);
            };
            mkRgb(this.LblKbdR, this.NumKbdR, "R", 0);
            mkRgb(this.LblKbdG, this.NumKbdG, "G", 1);
            mkRgb(this.LblKbdB, this.NumKbdB, "B", 2);

            int py = ry + H_PICKER + GUT;
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
            this.TxtKbdColorVal.Location = new Point(PAD, py + H_PRESETROW + GUT);
            this.TxtKbdColorVal.MaxLength = 27;
            this.TxtKbdColorVal.Size = new Size(W - 2 * PAD, H_HEXROW);
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
            // AutoSize, not a written-down 220. At 220 it spanned x 10-230 and, being added
            // to the panel before the update button, sat on top of it in the z-order and
            // covered its first three characters - "Check for updates" rendered as
            // "ck for updates".
            this.ChkAutoStart.AutoSize = true;
            this.ChkAutoStart.Location = new Point(PAD, HDR);
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

            // The update button says what it is doing in its own label rather than
            // needing a status line beside it: "Check for updates" -> "Checking..." ->
            // "Up to date" or "Downloading..." -> "Install vX.Y.Z". Sized once for the
            // longest of those so the row does not reflow as the state changes.
            this.BtnUpdate.Text = UpdateTextIdle;
            this.BtnUpdate.FlatStyle = FlatStyle.Flat;
            this.BtnUpdate.BackColor = GuiTheme.PanelHi;
            this.BtnUpdate.ForeColor = GuiTheme.Text;
            this.BtnUpdate.FlatAppearance.BorderColor = GuiTheme.Border;
            this.BtnUpdate.Size = new Size(FitButton(this.BtnUpdate, 130), 27);
            this.BtnUpdate.Location = new Point(
                W - PAD - 90 - GUT - this.BtnUpdate.Width, HDR - 3);
            this.BtnUpdate.Click += EventUpdateClick;

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
            this.GrpSys.Controls.Add(this.BtnUpdate);
            this.GrpSys.Controls.Add(this.RtfSysInfo);
            this.GrpSys.Size = new Size(W, H_SYS);
            this.GrpSys.TabStop = false;
            this.GrpSys.Text = Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_SYS);   // see GrpKbd above
#endregion

            // Tile the four quadrants. Left column monitors and edits — the two graphs;
            // right column configures. Nothing overlaps, nothing scrolls, and every panel
            // is on screen at once.
            int colL = PAD, colR = PAD + W + CROSS;
            int rowT = PAD, rowB = PAD + ROW + CROSS;

            this.GrpChart.Location = new Point(colL, rowT);   // history
            this.GrpFan.Location   = new Point(colR, rowT);   // profile + curve
            this.GrpKbd.Location   = new Point(colL, rowB);   // keyboard

            // The bottom-right quadrant holds three short panels. Their heights were set
            // above to sum, with the two minor gaps, to exactly one row — so the quadrant
            // is the same square as the other three and the cross stays even.
            this.GrpTmp.Location = new Point(colR, rowB);
            this.GrpPwr.Location = new Point(colR, this.GrpTmp.Bottom + GAP);
            this.GrpSys.Location = new Point(colR, this.GrpPwr.Bottom + GAP);

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
            // Square, and no scrolling. Everything is on screen: the quadrants are sized
            // from WIN, so the window cannot grow past the display by a section being
            // added to the stack — there is no stack any more.
            this.AutoScroll = false;
            this.ClientSize = new Size(WIN, WIN);
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
            this.KbdPicker.ColorChanged += EventKbdPickerChanged;
            this.BtnKbdPipette.Click += EventKbdPipetteClick;
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

                // Two estimates, and take the larger of them.
                //
                // Neither is reliable alone. TextRenderer.MeasureText knows the string but
                // not the control's chrome; GetPreferredSize knows the chrome but reports
                // less than the control actually paints — that is how "Backlight" fitted
                // in 90 px on paper and rendered as "Backlig", and how "Performance"
                // fitted in 104 and did not. Padding each and taking the maximum stops a
                // ten-pixel theoretical margin from being treated as a real one.
                int text = TextRenderer.MeasureText(b.Text, b.Font ?? this.Font).Width + 28;
                int pref = b.GetPreferredSize(Size.Empty).Width + 10;

                return Math.Max(minimum, Math.Max(text, pref));

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
