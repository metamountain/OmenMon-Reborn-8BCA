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

        // GroupBox: hairline under the (already-positioned) caption text instead of the frame.
        private static void PaintGroupBox(object sender, PaintEventArgs e) {
            var g = (GroupBox) sender;
            e.Graphics.Clear(g.BackColor);
            using(var br = new SolidBrush(Muted))
                e.Graphics.DrawString(g.Text, g.Font, br, 0, 0);
            int y = g.Font.Height + 2;
            using(var pen = new Pen(Border))
                e.Graphics.DrawLine(pen, 0, y, g.Width, y);
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
