  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy
//
// GuiFormError — the error dialog.
//
// Replaces the About-style error pop-up and the plain MessageBox. Both rendered
// their text as a label, so an error could only be retyped by hand; the exception
// details that make a report useful were the part hardest to retype. Here the whole
// report sits in a read-only, selectable text box (Ctrl+C works, Ctrl+A selects all)
// with a Copy button that puts it on the clipboard in one click.
//
// The report also carries the machine context — board, BIOS, app version, config
// path — because that is what any bug report needs and what the user would
// otherwise have to look up separately.

using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using OmenMon.Library;

namespace OmenMon.AppGui {

    public class GuiFormError : Form {

        private TextBox TxtReport;
        private Button BtnCopy;
        private Button BtnClose;
        private Label LblHint;

        // Builds the full report text: headline, then context, then exception detail
        private static string Compose(string message, Exception e) {

            StringBuilder s = new StringBuilder();
            s.AppendLine(message);
            s.AppendLine();
            s.AppendLine("--- Context " + new string('-', 48));
            s.AppendLine("Time      : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            try {
                s.AppendLine("App       : " + Config.DisplayName + " " + Application.ProductVersion
                    + "  (assembly " + Config.AppName + ")");
            } catch { }

            try {
                s.AppendLine("Board     : " + (Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS",
                    "BaseBoardProduct", null) as string ?? "?").Trim()
                    + "   Model: " + (Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS",
                    "SystemProductName", null) as string ?? "?").Trim()
                    + "   BIOS: " + (Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS",
                    "BIOSVersion", null) as string ?? "?").Trim());
            } catch { }

            try {
                s.AppendLine("OS        : " + Environment.OSVersion.VersionString
                    + (Environment.Is64BitProcess ? "  (64-bit process)" : "  (32-bit process)"));
            } catch { }

            try {
                s.AppendLine("Config    : " + Config.FilePath);
            } catch { }

            if(e != null) {
                s.AppendLine();
                s.AppendLine("--- Exception " + new string('-', 46));
                for(Exception x = e; x != null; x = x.InnerException) {
                    s.AppendLine(x.GetType().FullName + ": " + x.Message);
                    if(!string.IsNullOrEmpty(x.Source) || x.TargetSite != null)
                        s.AppendLine("  at " + x.Source + ": " + x.TargetSite);
                    if(!string.IsNullOrEmpty(x.StackTrace))
                        s.AppendLine(x.StackTrace);
                    if(x.InnerException != null)
                        s.AppendLine("  --- caused by ---");
                }
            }

            return s.ToString();

        }

        public GuiFormError(string title, string message, Exception e = null) {

            string report = Compose(message, e);

            this.TxtReport = new TextBox();
            this.TxtReport.Multiline = true;
            this.TxtReport.ReadOnly = true;
            this.TxtReport.ScrollBars = ScrollBars.Vertical;
            this.TxtReport.WordWrap = false;
            this.TxtReport.Location = new Point(14, 14);
            this.TxtReport.Size = new Size(612, 262);
            this.TxtReport.Text = report;
            this.TxtReport.BackColor = GuiTheme.Panel;
            this.TxtReport.ForeColor = GuiTheme.Text;
            this.TxtReport.BorderStyle = BorderStyle.FixedSingle;
            try { this.TxtReport.Font = new Font("Consolas", 8.75F); } catch { }

            this.LblHint = new Label();
            this.LblHint.AutoSize = true;
            this.LblHint.Location = new Point(14, 288);
            this.LblHint.ForeColor = GuiTheme.Muted;
            this.LblHint.Text = "Select and press Ctrl+C, or use Copy — the whole report goes to the clipboard.";

            this.BtnCopy = new Button();
            this.BtnCopy.Text = "Copy";
            this.BtnCopy.Size = new Size(86, 26);
            this.BtnCopy.Location = new Point(444, 284);
            this.BtnCopy.Click += delegate {
                try {
                    Clipboard.SetText(this.TxtReport.Text);
                    this.BtnCopy.Text = "Copied";
                } catch {
                    this.BtnCopy.Text = "Copy failed";
                }
            };

            this.BtnClose = new Button();
            this.BtnClose.Text = "Close";
            this.BtnClose.Size = new Size(86, 26);
            this.BtnClose.Location = new Point(540, 284);
            this.BtnClose.DialogResult = DialogResult.OK;

            this.Controls.Add(this.TxtReport);
            this.Controls.Add(this.LblHint);
            this.Controls.Add(this.BtnCopy);
            this.Controls.Add(this.BtnClose);

            try { this.Font = GuiTheme.Ui(GuiTheme.FontBody); } catch { }
            this.AcceptButton = this.BtnClose;
            this.CancelButton = this.BtnClose;
            this.ClientSize = new Size(640, 322);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = title;
            this.TopMost = true;

            try { this.Icon = OmenMon.Resources.Icon; } catch { }

            GuiTheme.Apply(this);

            // Apply() styles inputs as editable fields; keep the report visually flat
            this.TxtReport.BackColor = GuiTheme.Panel;
            this.TxtReport.ForeColor = GuiTheme.Text;

            // Land on the report so Ctrl+C works without clicking first,
            // with everything already selected
            this.Shown += delegate {
                this.TxtReport.Focus();
                this.TxtReport.SelectAll();
            };

        }

        // Shows the dialog modally, swallowing any failure to display it:
        // an error reporting an error helps nobody
        public static void Show(string title, string message, Exception e = null) {
            try {
                using(GuiFormError form = new GuiFormError(title, message, e))
                    form.ShowDialog();
            } catch {
                try {
                    MessageBox.Show(Compose(message, e), title,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                } catch { }
            }
        }

    }

}
