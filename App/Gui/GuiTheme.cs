  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// GuiTheme — minimal dark theme for the WinForms UI.
// Applied recursively to a form's control tree after Initialize(). Keeps the
// existing layout; only colours, borders and a few control styles change.

using System;
using System.Drawing;
using System.Windows.Forms;
using OmenMon.Library;

namespace OmenMon.AppGui {

    internal static class GuiTheme {

        // Monochrome. Colour is reserved for the places where it carries meaning that
        // grey cannot: the graph series (four lines that must be told apart at a glance)
        // and the keyboard swatch (which *is* the colour it shows). Everything else —
        // panels, borders, buttons, captions, selection — is neutral.
        //
        // The palette was zinc-tinted rather than neutral (0x18181B, 0x24242 8, 0x3A3A42:
        // every step carried a little blue) with a blue accent and an amber warning on
        // top. Selection now reads as inversion — light ground, dark text — which is
        // unmistakable without a hue and survives being looked at on a bad panel.
        // The ground the tiled panels sit on. Black, so the 20 px gaps between the four
        // quadrants read as a cross and the 10 px border frames them, without anything
        // having to draw a line.
        public static readonly Color Ground  = Color.Black;

        public static readonly Color Bg      = Color.FromArgb(0x18, 0x18, 0x18); // panels
        public static readonly Color Panel   = Color.FromArgb(0x24, 0x24, 0x24); // raised areas / inputs
        public static readonly Color PanelHi = Color.FromArgb(0x2E, 0x2E, 0x2E); // hover
        public static readonly Color Border  = Color.FromArgb(0x3C, 0x3C, 0x3C);
        public static readonly Color Text    = Color.FromArgb(0xF4, 0xF4, 0xF4); // primary
        public static readonly Color Muted   = Color.FromArgb(0x9A, 0x9A, 0x9A); // captions / secondary

        // Selected / active: the inverse of the window, not a colour
        public static readonly Color Accent  = Color.FromArgb(0xF4, 0xF4, 0xF4);
        public static readonly Color OnAccent = Color.FromArgb(0x18, 0x18, 0x18);

        // Kept so the few genuinely warning-shaped callers still compile and still stand
        // out, but as near-white rather than amber
        public static readonly Color Warm    = Color.FromArgb(0xE8, 0xE8, 0xE8);

        // Pure black caption so the title bar merges into the window instead of
        // being the one bright strip on screen.
        public static readonly Color Caption = Color.Black;

        // Type scale. Three sizes plus a monospace one, rather than the five that had
        // accumulated across the window (33 px, 9 pt, 8.25 pt, 7.5 pt and a
        // config-driven figure size). Every extra size is one more thing for the eye to
        // sort out, and none of the ones removed were carrying meaning the others did
        // not already convey through position and colour.
        //
        // Three sizes. Not four, not five — three.
        //
        // The window had five (24 px hero, 10.5, 9, 7.5, 8.25 pt) and mixed pixels with
        // points, so half the type scaled with the system DPI and half did not. Every
        // size is in points now, and hierarchy above body comes from *weight*, not from
        // inventing a fourth size: Inter SemiBold at FontBody is a heading, Inter Regular
        // at FontBody is text. That is the whole scale.
        public const float FontDisplay = 18F;  // pt - the CPU/GPU sensor readouts
        public const float FontBody    = 9F;   // pt - labels, buttons, headings, text
        public const float FontCaption = 7.5F; // pt - column captions, axis ticks, legends

        // Inter, shipped with the program (Resources\Inter-Regular.ttf, OFL 1.1). Named
        // rather than indexed: PrivateFontCollection sorts its families, so GdiFont.Get(0)
        // meant IoMon before Inter was added and would mean Inter afterwards.
        public const string FaceUi     = "Inter";
        public const string FaceUiBold = "Inter SemiBold";

        // The system-information strip stays monospaced: it is a column of aligned
        // key/value pairs, and Inter has no fixed-pitch figures reachable through GDI+.
        public const string FaceMono   = "Consolas";

        // One border weight everywhere. Buttons, spinners, text boxes, section rules and
        // the window edge were previously set independently and drifted apart.
        public const int BorderWidth = 1;

        // Where a section's hairline sits, and the only thing that decides it.
        //
        // It used to be g.Font.Height + 2 — a font metric — while the History chart was
        // placed at the magic offset HDR - 8. Two numbers derived from different things
        // and required to stay in order: with Segoe UI 10.5 pt the rule landed at 21 and
        // the chart's top edge at 20, so History's rule was drawn *underneath its own
        // chart* and vanished, while every other section, whose content starts lower,
        // kept its own. Any font or DPI change moves one and not the other.
        //
        // Fixed, so the relationship is structural rather than arithmetic: the layout's
        // HDR and the chart's pull-up are both derived from this below, and no caption
        // font can push the rule into the content again.
        public const int CaptionRuleY = 18;

        // First content row of a section, clearing the caption and its rule
        public const int CaptionBand = CaptionRuleY + 10;

        // The one way to ask for type. Every call site goes through here, so the three
        // sizes above are the only three the window can render.
        public static Font Ui(float size, bool bold = false) {
            return GdiFont.Get(bold ? FaceUiBold : FaceUi, size);
        }

        // The one margin. Every left edge in the window is this far from the section
        // edge and every right edge is this far from the other side — captions, rules,
        // controls, and both graphs, whose scale numbers now start on this column too
        // rather than floating ten pixels inside it.
        //
        // 10 rather than 16: at 16 the window carried more margin than content in the
        // narrow rows, and a slimmer, strictly uniform border reads calmer than a wide
        // one that anything is allowed to deviate from.
        public const int Pad = 10;

        // Entry point: theme a whole form, non-client area included.
        public static void Apply(Form form) {
            if(form == null) return;
            // Black, so the gaps between the tiled panels read as a cross. The panels
            // paint themselves in Bg (see PaintGroupBox), and the only places this shows
            // through are the 20 px cross between the four quadrants and the 10 px border
            // around them. Nothing draws the cross — it is the ground.
            form.BackColor = Ground;
            form.ForeColor = Text;
            foreach(Control c in form.Controls)
                ApplyTo(c);
            DarkenTitleBar(form);
        }

        // The DWM attributes only stick once the window exists, and they are lost when
        // the handle is recreated, so re-apply on every HandleCreated.
        public static void DarkenTitleBar(Form form) {
            if(form == null) return;
            EventHandler apply = delegate {
                if(form.IsHandleCreated)
                    OmenMon.External.Dwm.UseDarkTitleBar(form.Handle, Caption, Text, Caption);
            };
            form.HandleCreated -= apply;
            form.HandleCreated += apply;
            apply(form, EventArgs.Empty);
        }

        private static void ApplyTo(Control c) {
            switch(c) {

                case GroupBox g:
                    g.ForeColor = Muted;
                    g.BackColor = Bg;

                    // Do NOT set g.Font here.
                    //
                    // A section caption is body size in SemiBold — weight carries the
                    // hierarchy, not a fourth size — but assigning that to the GroupBox
                    // set it on every control inside it too, because WinForms children
                    // inherit their parent's font unless they have one of their own. The
                    // whole window was rendering in SemiBold, every label and checkbox
                    // was wider than the width it had been given, and text was clipped.
                    // "Backlight" losing its last letters is what this was.
                    //
                    // The caption is the only thing that wants the weight, and
                    // PaintGroupBox draws the caption, so it makes its own font there.
                    g.Paint -= PaintGroupBox;
                    g.Paint += PaintGroupBox;
                    break;

                case Button b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.BackColor = Panel;
                    b.ForeColor = Text;
                    b.FlatAppearance.BorderColor = Border;
                    b.FlatAppearance.BorderSize = 1;
                    b.FlatAppearance.MouseOverBackColor = PanelHi;
                    b.FlatAppearance.MouseDownBackColor = Border;
                    b.UseVisualStyleBackColor = false;
                    break;

                case ComboBox cb:
                    cb.FlatStyle = FlatStyle.Flat;
                    cb.BackColor = Panel;
                    cb.ForeColor = Text;
                    if(cb.DrawMode != DrawMode.OwnerDrawFixed) {
                        cb.DrawMode = DrawMode.OwnerDrawFixed;
                        cb.DrawItem -= PaintComboItem;
                        cb.DrawItem += PaintComboItem;
                    }
                    break;

                case NumericUpDown nud:
                    // The spin control hosts an inner TextBox that keeps the system
                    // window colour (a white block) unless it is recoloured too.
                    nud.BorderStyle = BorderStyle.FixedSingle;
                    nud.BackColor = Panel;
                    nud.ForeColor = Text;
                    foreach(Control inner in nud.Controls) {
                        inner.BackColor = Panel;
                        inner.ForeColor = Text;
                    }
                    break;

                case TextBox tx:
                    tx.BorderStyle = BorderStyle.FixedSingle;
                    tx.BackColor = Panel;
                    tx.ForeColor = Text;
                    break;

                case RichTextBox rtf:
                    rtf.BackColor = Bg;
                    rtf.ForeColor = Text;
                    break;

                case CheckBox ck:
                    ck.FlatStyle = FlatStyle.Flat;
                    ck.ForeColor = Text;
                    ck.BackColor = Bg;
                    ck.FlatAppearance.BorderColor = Border;
                    break;

                case RadioButton rb:
                    // Leave toggle-button-styled radios alone — they carry their own
                    // fill / border / checked colours (the fan-mode selector).
                    if(rb.Appearance == Appearance.Button)
                        break;
                    rb.FlatStyle = FlatStyle.Flat;
                    rb.ForeColor = Text;
                    rb.BackColor = Bg;
                    rb.FlatAppearance.BorderColor = Border;
                    break;

                case Label lbl:
                    // Value read-outs stay bright; short caption labels dim to Muted.
                    lbl.BackColor = Color.Transparent;
                    lbl.ForeColor = (lbl.Font != null && lbl.Font.SizeInPoints >= 12) ? Text : Muted;
                    break;

                case TrackBar tb:
                    tb.BackColor = Bg;
                    break;

                case ProgressBar pbar:
                    // ProgressBarEx paints from these two colours.
                    pbar.BackColor = Panel;
                    pbar.ForeColor = Accent;
                    break;

                case PictureBox pb:
                    pb.BackColor = Bg;
                    break;

                default:
                    c.BackColor = Bg;
                    c.ForeColor = Text;
                    break;
            }

            foreach(Control child in c.Controls)
                ApplyTo(child);
        }

        // GroupBox: a caption and a hairline under it, instead of a frame. Both are
        // inset by Pad so they share the left edge with the section's own controls and
        // stop at the same right edge — a rule that runs the full width past content
        // that does not is what made the window look like it stepped in and out.
        private static void PaintGroupBox(object sender, PaintEventArgs e) {
            var g = (GroupBox) sender;
            e.Graphics.Clear(g.BackColor);
            // The caption carries the weight, and only the caption — see ApplyTo, where
            // setting this on the GroupBox itself put every child control in SemiBold
            using(var f = Ui(FontBody, true))
            using(var br = new SolidBrush(Muted))
                e.Graphics.DrawString(g.Text, f, br, Pad, 0);
            using(var pen = new Pen(Border))
                e.Graphics.DrawLine(pen, Pad, CaptionRuleY, g.Width - Pad, CaptionRuleY);
        }

        // Dark drop-down list items.
        private static void PaintComboItem(object sender, DrawItemEventArgs e) {
            var cb = (ComboBox) sender;
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using(var bg = new SolidBrush(sel ? PanelHi : Panel))
                e.Graphics.FillRectangle(bg, e.Bounds);
            string txt = e.Index >= 0 ? cb.GetItemText(cb.Items[e.Index]) : cb.Text;
            TextRenderer.DrawText(e.Graphics, txt, cb.Font, e.Bounds, Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }
}
