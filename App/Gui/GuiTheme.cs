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

namespace OmenMon.AppGui {

    internal static class GuiTheme {

        // Palette (zinc-ish dark)
        public static readonly Color Bg      = Color.FromArgb(0x18, 0x18, 0x1B); // window
        public static readonly Color Panel   = Color.FromArgb(0x24, 0x24, 0x28); // raised areas / inputs
        public static readonly Color PanelHi = Color.FromArgb(0x2E, 0x2E, 0x34); // hover
        public static readonly Color Border  = Color.FromArgb(0x3A, 0x3A, 0x42);
        public static readonly Color Text    = Color.FromArgb(0xF4, 0xF4, 0xF5); // primary
        public static readonly Color Muted   = Color.FromArgb(0x9A, 0x9A, 0xA4); // captions / secondary
        public static readonly Color Accent  = Color.FromArgb(0x3B, 0x82, 0xF6); // blue
        public static readonly Color Warm    = Color.FromArgb(0xF5, 0x9E, 0x0B); // amber

        // Pure black caption so the title bar merges into the window instead of
        // being the one bright strip on screen.
        public static readonly Color Caption = Color.Black;

        // Type scale. Three sizes plus a monospace one, rather than the five that had
        // accumulated across the window (33 px, 9 pt, 8.25 pt, 7.5 pt and a
        // config-driven figure size). Every extra size is one more thing for the eye to
        // sort out, and none of the ones removed were carrying meaning the others did
        // not already convey through position and colour.
        //
        // Hero is in pixels because it uses the bundled IoMon face, a bitmap design that
        // only looks right at exact pixel sizes.
        // Four sizes, and a section heading that is clearly a heading.
        // The sensor readout came down from 28 px: at that size the two rows dominated
        // the window and the numbers read as the point of the program rather than as one
        // of its six sections.
        public const float FontHero    = 24F;   // px  - the CPU/GPU sensor readouts
        public const float FontHeading = 10.5F; // pt  - section captions
        public const float FontBody    = 9F;    // pt  - labels, buttons, section text
        public const float FontSmall = 7.5F;  // pt  - graph axis ticks and legends
        public const float FontMono  = 8.25F; // pt  - the system information strip

        // Inner padding for section content, and for the caption and rule that head it.
        // Shared with the layout code: the caption and its hairline used to be drawn at
        // x = 0 while every control inside sat at 16, so left edges alternated between
        // the two all the way down the window and each rule overhung its own content.
        public const int Pad = 16;

        // Entry point: theme a whole form, non-client area included.
        public static void Apply(Form form) {
            if(form == null) return;
            form.BackColor = Bg;
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
                    // One size up from body text, so a section caption is visibly a
                    // heading rather than another label that happens to sit at the top.
                    // Set here rather than per-section so all six cannot drift apart.
                    try {
                        if(g.Font == null || Math.Abs(g.Font.SizeInPoints - FontHeading) > 0.01f)
                            g.Font = new Font(g.Font != null ? g.Font.FontFamily.Name : "Segoe UI",
                                FontHeading, FontStyle.Regular, GraphicsUnit.Point);
                    } catch { }
                    // Replace the etched 3-D frame with a single hairline under the caption.
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
            using(var br = new SolidBrush(Muted))
                e.Graphics.DrawString(g.Text, g.Font, br, Pad, 0);
            int y = g.Font.Height + 2;
            using(var pen = new Pen(Border))
                e.Graphics.DrawLine(pen, Pad, y, g.Width - Pad, y);
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
