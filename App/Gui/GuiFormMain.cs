  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023-2024 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/
// OmenMon-Reborn additions © 2026 seakyy

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using OmenMon.External;
using OmenMon.Hardware.Bios;
using OmenMon.Hardware.Platform;
using OmenMon.Library;

namespace OmenMon.AppGui {

    // The main GUI form
    public partial class GuiFormMain : Form {

#region Variables
        // Color picker dialog stored globally to preserve user colors
        private ColorDialogEx ColorPicker;

        // Color preset data source
        private List<Object> ColorPresets;

        // Fan mode data source
        private List<Object> FanModes;

        // Fan program data source
        private List<Object> FanPrograms;

        // Holds the custom font
        private Font FigureFont;

        // Stores the class managing the recolored keyboard drawing
        internal GuiKbd Kbd;

        // Stores the previous DPI value, for dynamically-updated scaling
        private int LastDpi;

        // Stores both parts of the system status, so that the other part
        // does not have to be regenerated every time one changes
        private string SysInfo;
        private string SysStatus;

        // Parent class reference
        private GuiTray Context;

        // Stores the component container
        private System.ComponentModel.IContainer Components;
#endregion Variables

#region Construction & Disposal
        // Constructs the form
        public GuiFormMain() {

            // Initialize the parent class reference
            this.Context = GuiTray.Context;

            // Initialize the component model container
            this.Components = new System.ComponentModel.Container();

            // Initialize the data sources
            ColorPresets = new List<Object>();
            FanModes = new List<Object>();
            FanPrograms = new List<Object>();

            // Initialize the form components
            Initialize();

            // Pre-populate the last DPI setting to the value at launch
            this.LastDpi = (int) Gui.GetDeviceContextDpi(IntPtr.Zero);

            // For keyboards that support backlight and color settings
            if(Context.Op.Platform.System.GetKbdBacklightSupport()
                && Context.Op.Platform.System.GetKbdColorSupport()) {

                // Initialize the keyboard color management class
                this.Kbd = new GuiKbd(this.Context);

                // Update the keyboard picture
                this.PicKbd.Image = Kbd.GetImage();

                // Initialize the color picker
                this.ColorPicker = new ColorDialogEx(UpdateKbdCallback);

                // Pre-populate the custom colors for the color picker
                this.ColorPicker.CustomColors = Kbd.UpdateColorPicker(Config.GuiColorPickerCustom);

            } else

                // Show a static disabled keyboard image if unsupported
                this.PicKbd.Image = OmenMon.Resources.KeyboardOff;

            // Set up the fan presets, system status data
            // and temperature readout captions (static)
            SetupFanCtl();
            SetupSys();
            SetupTmp();

            // Update the controls to reflect the initial hardware state
            UpdateAll();

            // Post a status message as a welcome
            UpdateSysMsg(
                Conv.RTF_CF6 + Config.AppName + " "
                + Conv.RTF_CF5 + Config.AppVersion + " "
                + Conv.RTF_CF2 + Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_SYS + "MsgWelcome"));

            // Show the active profile's curve in the inline editor
            EventProfilePicked(this, EventArgs.Empty);

            // Restyle: apply the minimal dark theme over the fully-built control tree.
            GuiTheme.Apply(this);

        }

        // Handles component disposal
        protected override void Dispose(bool isDisposing) {
	
            if(isDisposing && Components != null)
                Components.Dispose();
	
            // Perform the usual tasks
            base.Dispose(isDisposing);
	
        }

        // Makes the F1 key open the About dialog
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if(keyData == Keys.F1) {
                GuiOp.About();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Makes the Esc key close the form
        protected override bool ProcessDialogKey(Keys keyData) {
            if(Form.ModifierKeys == Keys.None && keyData == Keys.Escape) {
                this.Close();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        // Overrides the window procedure to capture a DPI-change event
        // Catching this via the .NET API is conditional upon a configuration setting,
        // which would require a *.exe.config file to be present in the same location
        // Much better to keep everything contained to a single file, so not an option
        protected override void WndProc(ref Message m) {

            // Alternatively, override DefWndProc to intercept WM_GETDPISCALEDSIZE,
            // which gets dispatched before any DPI changes will have happened
            if(m.Msg == User32.WM_DPICHANGED)

                    // Update the form to match the new scaling
                    // Parameter is the horizontal DPI, but vertical is always the same
                    UpdateDpi(m.WParam.ToInt32() & 0xFFFF);

            // Run the base procedure
            base.WndProc(ref m);

        }
#endregion

#region Event Actions
        // Runs once when the form is first shown; checks for unknown hardware and offers auto-detection
        private void EventFormShown(object sender, EventArgs e) {
            this.Shown -= EventFormShown;
            CheckUnknownModel();
        }

        // Prompts the user to run heuristic auto-detection if the current product ID is not in the model DB
        private void CheckUnknownModel() {
            try {
                string product = Context.Op.Platform.System.GetProduct();
                if(product == "?" || Config.Models.ContainsKey(product))
                    return;

                var answer = MessageBox.Show(
                    $"Unknown Omen model detected ({product}).\n\nRun a safe read-only hardware scan to auto-configure sensors?",
                    Config.AppName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if(answer != DialogResult.Yes)
                    return;

                if(Hw.Ec == null || !Hw.Ec.IsInitialized) {
                    MessageBox.Show(
                        "The Embedded Controller is not available — cannot run hardware scan.\n\nCheck that the WinRing0 driver loaded correctly.",
                        Config.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var preset = OmenMon.Hardware.Platform.AutoDetector.DetectHeuristic(product);
                if(preset != null) {
                    Config.SaveModel(preset);
                    MessageBox.Show(
                        $"Auto-configuration saved for {product}.\n\nThe standard 2022+ register layout was confirmed on your device.",
                        Config.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                } else {
                    MessageBox.Show(
                        $"Could not auto-detect register layout for {product}.\n\nPlease use 'Auto-Calibrate & Diagnose...' from the tray menu to identify the correct EC registers for your hardware and report them to the project.",
                        Config.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            } catch { }
        }

        // Toggles the keyboard backlight on or off
        // The three built-in profiles cannot be deleted.
        private static readonly string[] StandardProfiles = { "Performance", "Default", "Silent" };
        private static bool IsStandardProfile(string n) {
            return n != null && Array.Exists(StandardProfiles,
                s => string.Equals(s, n, StringComparison.OrdinalIgnoreCase));
        }

        // Selecting a profile shows its curve AND applies it straight away — picking
        // one used to only highlight "Apply", so nothing reached the hardware until a
        // second click. Run() writes the fan levels unconditionally, so the change is
        // immediate; no waiting for the next 15 s program tick.
        private void EventProfilePicked(object sender, EventArgs e) {
            string name = this.CmbFanProg.SelectedValue as string;

            // At startup the combo may not have a selection yet: the snapshot that
            // UpdateFan() uses to set it does not exist until the first monitor pass,
            // and AutoConfig starts the default program asynchronously. Fall back to
            // the running program, then to the configured default, so the profile and
            // its curve are always shown rather than coming up blank.
            if(string.IsNullOrEmpty(name)) {
                try { name = Context.Op.Program.GetName(); } catch { }
                if(string.IsNullOrEmpty(name)) name = Config.FanProgramDefault;
                if(string.IsNullOrEmpty(name) && Config.FanProgram.Count > 0)
                    name = Config.FanProgram.Keys[0];
                if(string.IsNullOrEmpty(name)) return;
                try { this.CmbFanProg.SelectedValue = name; } catch { }
            }

            this.Curve.LoadProgram(name);
            this.BtnProfDel.Enabled = !IsStandardProfile(name);

            // Only auto-apply for a real user pick — the constructor calls this too,
            // and AutoConfig has already started the default profile by then.
            if(!ReferenceEquals(sender, this.CmbFanProg))
                return;

            this.RdoFanProg.Checked = true;
            EventActionFanSet(sender, EventArgs.Empty);

            // Remember it so the next launch starts on this profile
            UserPrefs.Set(UserPrefs.KeyFanProfile, name);

            // Republish at once so the readouts show the new levels immediately
            // instead of lagging until the next monitor pass.
            Context.Monitor?.SampleNow();
            UpdateFan();
            UpdateSysMsg("Profile \"" + name + "\" applied.");
        }

        // Persist the edited curve back into the profile
        private void EventCurveSave(object sender, EventArgs e) {
            if(!this.Curve.SaveProgram()) {
                UpdateSysMsg("No profile selected — nothing to save.");
                return;
            }
            try { Config.Save(); } catch { }
            string name = this.Curve.ProgramName;
            try {
                if(Context.Op.Program.GetName() == name)
                    lock(Context.Op.HardwareLock) Context.Op.Program.Run(name);
            } catch { }
            UpdateSysMsg("Curve saved to profile \"" + name + "\".");
        }

        // New profile: copies the current curve under a new name
        private void EventProfileAdd(object sender, EventArgs e) {
            string name = Gui.ShowPromptInputText("Name for the new fan profile:", "My profile", this);
            if(string.IsNullOrEmpty(name)) return;
            if(Config.FanProgram.ContainsKey(name)) {
                MessageBox.Show(this, "A profile with that name already exists.", "New profile",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string src = this.CmbFanProg.SelectedValue as string;
            var levels = new System.Collections.Generic.SortedDictionary<byte, byte[]>();
            if(src != null && Config.FanProgram.ContainsKey(src))
                foreach(var kv in Config.FanProgram[src].Level)
                    levels[kv.Key] = new byte[] { kv.Value[0], kv.Value[1] };
            FanProgramData baseProg =
                src != null && Config.FanProgram.ContainsKey(src) ? Config.FanProgram[src] : null;
            Config.FanProgram[name] = new FanProgramData(name,
                baseProg != null ? baseProg.FanMode : BiosData.FanMode.Default,
                baseProg != null ? baseProg.GpuPower : BiosData.GpuPowerLevel.Maximum,
                levels);
            try { Config.Save(); } catch { }
            Context.Menu.Create();
            SetupFanCtl();
            try { this.CmbFanProg.SelectedValue = name; } catch { }
            EventProfilePicked(sender, e);
            UpdateSysMsg("Profile \"" + name + "\" created.");
        }

        // Delete a user profile (the three standard ones are protected)
        private void EventProfileDel(object sender, EventArgs e) {
            string name = this.CmbFanProg.SelectedValue as string;
            if(name == null || !Config.FanProgram.ContainsKey(name)) return;
            if(IsStandardProfile(name)) {
                MessageBox.Show(this, "\"" + name + "\" is a standard profile and cannot be deleted.",
                    "Delete profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if(MessageBox.Show(this, "Delete the profile \"" + name + "\"?", "Delete profile",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Config.FanProgram.Remove(name);
            try { Config.Save(); } catch { }
            Context.Menu.Create();
            SetupFanCtl();
            EventProfilePicked(sender, e);
            UpdateSysMsg("Profile \"" + name + "\" deleted.");
        }

        // Guard so reflecting the current state doesn't re-apply it
        private bool pwrSyncing;

        // One power preset = Windows power mode + CPU wattage limits, applied together
        private void EventPwrPreset(object sender, EventArgs e) {
            if(pwrSyncing) return;
            RadioButton rb = sender as RadioButton;
            if(rb == null || !rb.Checked) return;      // only act on the newly-selected one

            this.NumPwrWatt.Enabled = this.RdoPwrCustom.Checked;

            string label = rb == this.RdoPwrEco    ? PowerPresets.Eco
                         : rb == this.RdoPwrPerf   ? PowerPresets.Performance
                         : rb == this.RdoPwrCustom ? PowerPresets.Custom
                                                   : PowerPresets.Balanced;

            PowerPresets.Def d = PowerPresets.Resolve(label, (int) this.NumPwrWatt.Value);

            bool winOk = false;
            try { winOk = PowrProf.PowerSetActiveOverlayScheme(d.Overlay) == 0; } catch { }
            SetCpuLimits(d.Pl1, d.Pl2, d.Pl4, label);
            ShowPwrState(d.WinMode, d.Pl1, d.Pl2, d.Pl4, d.Character, winOk);

            // Remember the choice so the next launch restores it
            UserPrefs.Set(UserPrefs.KeyPowerPreset, label);
            if(label == PowerPresets.Custom)
                UserPrefs.Set(UserPrefs.KeyCustomWatts, (int) this.NumPwrWatt.Value);
        }

        // Changing the wattage re-applies immediately while Custom is selected
        private void EventPwrWattChanged(object sender, EventArgs e) {
            if(pwrSyncing || !this.RdoPwrCustom.Checked) return;
            EventPwrPreset(this.RdoPwrCustom, EventArgs.Empty);
        }

        // Spells out, in the section itself, exactly what the active preset did.
        // The Windows mode line carries its wattage band so it is obvious which
        // power mode belongs to which CPU limit.
        private void ShowPwrState(string winMode, byte pl1, byte pl2, byte pl4,
                                  string character, bool winOk) {
            string band = pl1 <= 35 ? "≤ 35 W band"
                        : pl1 >= 50 ? "≥ 50 W band"
                                    : "36–49 W band";
            this.LblPwrHint.Text = string.Format(
                "Windows power mode: {0}  ({1}){2}\nCPU limits: {3} W sustained · {4} W boost · {5} W peak — {6}",
                winMode, band, winOk ? "" : "  — Windows refused the change",
                pl1, pl2, pl4, character);
        }

        // Reflect the stored preset in the selector without re-applying it. The CPU
        // limits cannot be read back from the BIOS, so the saved preset is the source
        // of truth; the live Windows overlay is only the fallback on a first run.
        private void SyncPwrMode() {
            try {
                pwrSyncing = true;

                this.NumPwrWatt.Value = Math.Max(this.NumPwrWatt.Minimum,
                    Math.Min(this.NumPwrWatt.Maximum,
                        UserPrefs.GetInt(UserPrefs.KeyCustomWatts, 45)));

                string preset = UserPrefs.Get(UserPrefs.KeyPowerPreset, null);
                if(string.IsNullOrEmpty(preset)) {
                    Guid cur;
                    if(PowrProf.PowerGetActualOverlayScheme(out cur) != 0)
                        cur = PowrProf.OVERLAY_BALANCED;
                    preset = cur == PowrProf.OVERLAY_EFFICIENCY  ? PowerPresets.Eco
                           : cur == PowrProf.OVERLAY_PERFORMANCE ? PowerPresets.Performance
                                                                 : PowerPresets.Balanced;
                }

                if(preset == PowerPresets.Eco)              this.RdoPwrEco.Checked = true;
                else if(preset == PowerPresets.Performance) this.RdoPwrPerf.Checked = true;
                else if(preset == PowerPresets.Custom)      this.RdoPwrCustom.Checked = true;
                else                                        this.RdoPwrBal.Checked = true;

                this.NumPwrWatt.Enabled = this.RdoPwrCustom.Checked;

                PowerPresets.Def d = PowerPresets.Resolve(preset, (int) this.NumPwrWatt.Value);
                ShowPwrState(d.WinMode, d.Pl1, d.Pl2, d.Pl4, d.Character, true);
            } catch { } finally { pwrSyncing = false; }
        }

        // CPU power limits via the HP BIOS (WMI cmd 0x29). 0xFF means "leave alone".
        private void SetCpuLimits(byte pl1, byte pl2, byte pl4, string label) {
            try {
                BiosData.CpuPowerData d = new BiosData.CpuPowerData();
                d.Limit1 = pl1; d.Limit2 = pl2; d.Limit4 = pl4;
                Hw.BiosExec(bios => bios.SetCpuPower(d), Hw.Bios);
                UpdateSysMsg(string.Format("{0}: CPU {1} W sustained, {2} W boost, {3} W peak",
                    label, pl1, pl2, pl4));
            } catch {
                UpdateSysMsg("CPU power limit call was rejected by the BIOS.");
            }
        }

        // "Start with Windows" checkbox -> the GUI logon task
        private void EventActionAutoStart(object sender, EventArgs e) {
            try {
                Hw.TaskSet(Config.TaskId.Gui, !this.ChkAutoStart.Checked);
                this.ChkAutoStart.Checked = Hw.TaskGet(Config.TaskId.Gui);
            } catch { }
        }

        private void EventActionBacklight(object sender, EventArgs e) {

            if(Kbd != null) // Use the keyboard class
                Kbd.SetBacklight(!this.ChkKbdBacklight.Checked);

            else // Fallback case for no customizable backlight color, only backlight toggle
                Context.Op.Platform.System.SetKbdBacklight(!this.ChkKbdBacklight.Checked);

            UpdateKbd();
        }

        // Deletes a color preset
        private void EventActionColorPresetDel(object sender, EventArgs e) {

            // No preset selected
            if(this.CmbKbdColorPreset.SelectedValue == null || (string) this.CmbKbdColorPreset.SelectedValue == "")
                MessageBox.Show(
                    this, // Modal
                    Config.Locale.Get(Config.L_GUI_MAIN + "KbdColorPresetDelNoSel"),
                    Config.Locale.Get(Config.L_GUI_MAIN + "KbdColorPresetDel"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Exclamation);

            else if(MessageBox.Show(
                    this, // Modal
                    Config.Locale.Get(Config.L_GUI_MAIN + "KbdColorPresetDelPrompt") + ": "
                        + ((object) this.CmbKbdColorPreset.SelectedItem)
                            .GetType()
                            .GetProperty("Text")
                            .GetValue(this.CmbKbdColorPreset.SelectedItem, null)
                        + Environment.NewLine
                        + Config.Locale.Get(Config.L_GUI_MAIN + "KbdColorPresetDelConfirm"),
                    Config.Locale.Get(Config.L_GUI_MAIN + "KbdColorPresetDel"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Asterisk,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes)

                Config.ColorPreset.Remove((string) this.CmbKbdColorPreset.SelectedValue);

            // Update the user interface
            Context.Menu.Create();
            this.CmbKbdColorPreset.DataSource = null;
            UpdateKbd();

            // Save the configuration
            Config.Save();

        }

        // Renames the selected color preset
        private void EventActionColorPresetRen(object sender, EventArgs e) {
            string old = this.CmbKbdColorPreset.SelectedValue as string;
            if(string.IsNullOrEmpty(old) || !Config.ColorPreset.ContainsKey(old)) {
                MessageBox.Show(this, "Select a preset to rename first.", "Rename preset",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string name = Gui.ShowPromptInputText(
                Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_KBD + "ColorPresetAdd"), old, this);
            if(string.IsNullOrEmpty(name) || name == old)
                return;

            Config.ColorPreset[name] = Config.ColorPreset[old];
            Config.ColorPreset.Remove(old);

            Context.Menu.Create();
            this.CmbKbdColorPreset.DataSource = null;
            UpdateKbd();
            try { this.CmbKbdColorPreset.SelectedValue = name; } catch { }
            Config.Save();
        }

        // Saves a color preset
        private void EventActionColorPresetSet(object sender, EventArgs e) {
            string name;

            // Ask for a name
            if((name = Gui.ShowPromptInputText(
                Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_KBD + "ColorPresetAdd"),
                Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_KBD + "ColorPresetAddValueDefault"),
                this)) != "")

                // Save the preset (possibly overwriting it)
                Config.ColorPreset[name] = new BiosData.ColorTable(Kbd.GetColors(), true);

            // Update the user interface
            Context.Menu.Create();
            this.CmbKbdColorPreset.DataSource = null;
            UpdateKbd();

            // Save the configuration
            Config.Save();

        }

        // Handles the fan settings button being clicked
        private void EventActionFanSet(object sender, EventArgs e) {

            // Query fan state
            bool isFanMax = Context.Op.Platform.Fans.GetMax();
            bool isFanOff = Context.Op.Platform.Fans.GetOff();

            // Pause the monitor's constant-speed countdown maintenance while we reconfigure,
            // so it cannot issue a SetMode/SetCountdown in the middle of this change. It is
            // re-evaluated from the resulting radio state at the end of the method.
            Context.Op.ConstantSpeedActive = false;

            // Enable fan program
            if(this.RdoFanProg.Checked) {

                if(this.CmbFanProg.SelectedValue != null
                    && (string) this.CmbFanProg.SelectedValue != ""
                    && Config.FanProgram.ContainsKey(
                        (string) this.CmbFanProg.SelectedValue)) {

                    if(isFanOff) // Re-enable fan if off first
                        Context.Op.Platform.Fans.SetOff(false);

                    if(isFanMax) // Disable maximum speed first
                        Context.Op.Platform.Fans.SetMax(false);

                    // Start the selected program (locked: serialise with the monitor's
                    // fan-program tick so FanProgram state is never mutated concurrently)
                    lock(Context.Op.HardwareLock)
                        Context.Op.Program.Run((string) this.CmbFanProg.SelectedValue);

                    // Delay the monitor's next program tick — Run() above already applied the curve
                    Context.Monitor?.ResetProgramTick();

                } else {

                    MessageBox.Show(
                        this, // Modal
                        Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_FAN + "ProgSetNoSel"),
                        Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_FAN + "ProgSet"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Exclamation);

                }

            // Switch off the fan
            } else if(this.RdoFanOff.Checked) {

                // Terminate any running fan program (locked: serialise with the monitor's
                // fan-program tick so FanProgram state is never mutated concurrently)
                lock(Context.Op.HardwareLock)
                    Context.Op.Program.Terminate();

                if(!isFanOff) { // Skip if already off

                    if(isFanMax) // Disable maximum speed first
                        Context.Op.Platform.Fans.SetMax(false);

                    // Switch off the fan
                    Context.Op.Platform.Fans.SetOff(true);

                }

            // Set fan to maximum speed
            } else if(this.RdoFanMax.Checked) {

                // Terminate any running fan program (locked: serialise with the monitor's
                // fan-program tick so FanProgram state is never mutated concurrently)
                lock(Context.Op.HardwareLock)
                    Context.Op.Program.Terminate();

                if(!isFanMax) { // Skip if already maximum speed

                     // Centralised safety warning for models with known 100% fan freeze.
                     if(!GuiOp.ConfirmMaxFanIfRisky(Context.Op.Platform.System.GetProduct())) {
                         // User declined — revert fan UI to previous state
                         UpdateFanCtl();
                         return;
                     }

                     if(isFanOff) // Re-enable fan if off first
                         Context.Op.Platform.Fans.SetOff(false);

                     // Set the fan to maximum speed
                     Context.Op.Platform.Fans.SetMax(true);

                }

            // Enable fan constant speed
            } else if(this.RdoFanConst.Checked) {

                // Terminate any running fan program (locked: serialise with the monitor's
                // fan-program tick so FanProgram state is never mutated concurrently)
                lock(Context.Op.HardwareLock)
                    Context.Op.Program.Terminate();

                // The BIOS mandates that at least one fan
                // be left running at any given time, so if the user
                // wants both of them off, we use another way to do so
                if(this.TrkFan0Lvl.Value == this.TrkFan0Lvl.Minimum
                    && this.TrkFan1Lvl.Value == this.TrkFan1Lvl.Minimum) {

                    // Switch the fans off
                    if(!isFanOff) // If not already off
                        Context.Op.Platform.Fans.SetOff(true);

                // Conversely, if the user wants maximum speed setting
                // for both fans, we just set it explicitly instead
                } else if(this.TrkFan0Lvl.Value == this.TrkFan0Lvl.Maximum
                    && this.TrkFan1Lvl.Value == this.TrkFan1Lvl.Maximum) {

                    // Centralised safety warning for models with known 100% fan freeze.
                    if(!GuiOp.ConfirmMaxFanIfRisky(Context.Op.Platform.System.GetProduct())) {
                        // User declined — revert trackbars to safe values (70%)
                        int safeMax = (int)(this.TrkFan0Lvl.Maximum * 0.7);
                        this.TrkFan0Lvl.Value = safeMax;
                        this.TrkFan1Lvl.Value = safeMax;
                        // Fall through to set the safe levels below
                    }

                    // Set the fans to maximum speed (or safe level if user cancelled above)
                    if(!isFanMax && this.TrkFan0Lvl.Value == this.TrkFan0Lvl.Maximum
                        && this.TrkFan1Lvl.Value == this.TrkFan1Lvl.Maximum) // If not already at maximum speed
                        Context.Op.Platform.Fans.SetMax(true);
                    else if(this.TrkFan0Lvl.Value != this.TrkFan0Lvl.Maximum
                        || this.TrkFan1Lvl.Value != this.TrkFan1Lvl.Maximum) {
                        // User chose safe levels — apply them
                        if(isFanMax)
                            Context.Op.Platform.Fans.SetMax(false);
                        if(isFanOff)
                            Context.Op.Platform.Fans.SetOff(false);
                        Context.Op.Platform.Fans.SetLevels(new byte[] {
                            (byte) this.TrkFan0Lvl.Value,
                            (byte) this.TrkFan1Lvl.Value});
                        // Reset the fan mode so the new levels are latched
                        Context.Op.Platform.Fans.SetMode(
                            Context.Op.Platform.Fans.GetMode());
                    }

                // Otherwise, we just set the speed levels normally
                } else {

                    if(isFanMax) // Disable maximum speed first
                        Context.Op.Platform.Fans.SetMax(false);
		    
                    if(isFanOff) // Re-enable fan if off first
                        Context.Op.Platform.Fans.SetOff(false);

                    // Set each fan to the user-selected level
                    // or to zero, if the minimum value is selected
                    Context.Op.Platform.Fans.SetLevels(new byte[] {
                        this.TrkFan0Lvl.Value == this.TrkFan0Lvl.Minimum ? (byte) 0 : (byte) this.TrkFan0Lvl.Value,
                        this.TrkFan1Lvl.Value == this.TrkFan1Lvl.Minimum ? (byte) 0 : (byte) this.TrkFan1Lvl.Value});
                    // Note: this won't work for setting both fans to zero
                    // but that case will have already been handled at this point

                    // Reset the fan mode so that the settings are applied
                    Context.Op.Platform.Fans.SetMode(
                        Context.Op.Platform.Fans.GetMode());

                }

            // Enable automatic fan mode (default)
            } else if(this.RdoFanAuto.Checked) {

                // Terminate any running fan program (locked: serialise with the monitor's
                // fan-program tick so FanProgram state is never mutated concurrently)
                lock(Context.Op.HardwareLock)
                    Context.Op.Program.Terminate();

                // Query the current and requested mode
                BiosData.FanMode fanModeNow = Context.Op.Platform.Fans.GetMode();
                BiosData.FanMode fanModeAsk = (BiosData.FanMode) Enum.Parse(
                    typeof(BiosData.FanMode), 
                    (string) this.CmbFanMode.SelectedValue);

                if(isFanOff) // Re-enable fan if off first
                    Context.Op.Platform.Fans.SetOff(false);

                if(isFanMax) // Disable maximum speed first
                    Context.Op.Platform.Fans.SetMax(false);

                // Set the levels to 0xFF to clear any custom speed settings
                Context.Op.Platform.Fans.SetLevels(new byte[] {Byte.MaxValue, Byte.MaxValue});

                // Enable automatic fan in the selected mode
                Context.Op.Platform.Fans.SetMode(fanModeAsk);

            }

            // Restore the default button look
            this.BtnFanSet.Checked = false;

            // Re-evaluate constant-speed maintenance for the monitor from the resulting
            // mode, then publish a fresh snapshot so the form reflects the change at once.
            Context.Op.ConstantSpeedActive = this.RdoFanConst.Checked;
            Context.Monitor?.SampleNow();

        }

        // Handles the help button being clicked
        private void EventActionHelp(object sender, EventArgs e) {

            // Show the About dialog
            GuiOp.About();

            // Cancel the help cursor
            ((System.ComponentModel.CancelEventArgs) e).Cancel = true;
 
        }
#endregion

#region Events
        // Handles the event of a color parameter being input into the text box
        private void EventColorInput(object sender, EventArgs e) {
            try {
                Kbd.SetColors(new BiosData.ColorTable(this.TxtKbdColorVal.Text));
                this.TxtKbdColorVal.ForeColor = Color.Empty;
            } catch {
                this.TxtKbdColorVal.ForeColor = Color.Red;
            }
        }

        // Handles the event of a color zone being clicked
        private void EventColorPick(object sender, MouseEventArgs e) {

            // No action if backlight off or no support
            if(Kbd == null || !Kbd.GetBacklight())
                return;

            // Determine the clicked zone from co-ordinates
            // while also setting the dialog title in one go
            BiosData.KbdZone picked = Kbd.SetZone(e.X, e.Y);
            this.ColorPicker.Title = Config.Locale.Get(Config.L_GUI_MAIN + "KbdColorPick" + picked.ToString());

            // Reflect the clicked zone in the selector + sliders (guard the change echo)
            try {
                kbdRgbSyncing = true;
                this.CmbKbdZone.SelectedIndex = ComboIndexForZone(picked);
            } catch { } finally { kbdRgbSyncing = false; }
            SyncKbdRgb();

            // Set the start color to the current color
            this.ColorPicker.Color = Color.FromArgb(Kbd.GetColor());

            // Update the current backlight color in the custom colors
            this.ColorPicker.CustomColors = Kbd.UpdateColorPicker(this.ColorPicker.CustomColors);

            // Show the dialog
            this.ColorPicker.ShowDialog();

            // Note: Color is updated in real time,
            // so there is nothing more to check here

        }

        // Handles the event of a color preset being selected from a drop-down list
        private void EventColorPreset(object sender, EventArgs e) {

            // Apply the new color preset
            Context.FormMain.Kbd.SetColors(
                Config.ColorPreset[(string) ((ComboBox) sender).SelectedValue]);

            // Update the parameter textbox and the R/G/B sliders / swatch
            this.TxtKbdColorVal.Text = Kbd.GetParam();
            SyncKbdRgb();

        }

        // Handles the event when the form is about to be closed
        private void EventFormClosing(object sender, FormClosingEventArgs e) {

            // If the reason is user request
            if (e.CloseReason == CloseReason.UserClosing) {

                // Cancel the application closure
                // and hide the window instead
                e.Cancel = true;
                this.Hide();

            }

        }

        // Handles the event when the fan mode list has been interacted with
        private void EventFanModeChanged(object sender, EventArgs e) {

            // Highlight the Set button to remind
            // the user of a pending unapplied change
            this.BtnFanSet.Checked = true;

            // Switch the selected radio button to Auto
            this.RdoFanAuto.Checked = true;

        }

        // Handles the event when the fan program list has been interacted with
        private void EventFanProgramChanged(object sender, EventArgs e) {

            // Highlight the Set button to remind
            // the user of a pending unapplied change
            this.BtnFanSet.Checked = true;

            // Switch the selected radio button to Program
            this.RdoFanProg.Checked = true;

        }

        // Handles the event when the fan radio button has been selected or deselected
        private void EventFanRdoChanged(object sender, EventArgs e) {

            if(((Control) sender).Name ==  Gui.T_RDO + Gui.G_FAN + "Const")

                // If the radio button is set to constant speed,
                // unlock the trackbars so that the speed can be set
                if(((RadioButton) sender).Checked) {
                    this.TrkFan0Lvl.Enabled = true;
                    this.TrkFan1Lvl.Enabled = true;
	        
                // The trackbars are locked in any other situation
                } else {
                    this.TrkFan0Lvl.Enabled = false;
                    this.TrkFan1Lvl.Enabled = false;
                }

            // Highlight the Set button to remind
            // the user of a pending unapplied change
            this.BtnFanSet.Checked = true;

        }

        // Handles the event when the fan radio button has been selected or deselected
        private void EventFanTrkChanged(object sender, EventArgs e) {

            // Only if the trackbar is enabled
            // for constant fan speed mode setting
            if(((Control) sender).Enabled)
                this.BtnFanSet.Checked = true;

        }

        // Handles the event when the form visibility changes
        private void EventFormVisibleChanged(object sender, EventArgs e) {

            // Tell the monitor whether to sample the heavier fan/system fields — these are
            // only read while the form is open, preserving the legacy "read only what is
            // shown" footprint (no spurious EC/BIOS traffic when the form is hidden).
            Context.FormVisible = this.Visible;

            // Reset the update counter,
            // the form is always updated immediately as it opens
            Context.UpdateMonitorTick = 0;

            // Update everything
            if(this.Visible)
                UpdateAll();

        }
#endregion

#region Setup
        // Set up the fan control combo boxes
        public void SetupFanCtl() {

            // Trackbars are disabled by default, until
            // constant-speed mode is explicitly enabled
            this.TrkFan0Lvl.Enabled = false;
            this.TrkFan1Lvl.Enabled = false;

            // Clear the fan mode list
            this.CmbFanMode.BeginUpdate();
            this.CmbFanMode.DataSource = null;
            FanModes.Clear();

            // Populate the fan mode list
            // The most useful modes are on top,
            // the rest (legacy modes) is sorted alphabetically
            List<string> fanModes = Config.FanModesSticky;
            string[] fanModesMore = Enum.GetNames(typeof(BiosData.FanMode));
            Array.Sort(fanModesMore);
            fanModes.AddRange(fanModesMore);
            foreach(string name in new HashSet<string>(fanModes))
                FanModes.Add(new {
                    Text = Config.Locale.Get(Config.L_GUI_MENU + Gui.M_ACT + Gui.G_FAN + "Mode" + name),
                    Value = name });
            this.CmbFanMode.DataSource = FanModes;
            this.CmbFanMode.DisplayMember = "Text";
            this.CmbFanMode.ValueMember = "Value";
            this.CmbFanMode.EndUpdate();

            // Clear the fan mode list
            this.CmbFanProg.BeginUpdate();
            this.CmbFanProg.DataSource = null;
            FanPrograms.Clear();

            // Populate the fan program list
            foreach(string name in Config.FanProgram.Keys)
                FanPrograms.Add( new { Text = name, Value = name } );
            if(FanPrograms.Count > 0)
               this.CmbFanProg.DataSource = FanPrograms;
            this.CmbFanProg.DisplayMember = "Text";
            this.CmbFanProg.ValueMember = "Value";
            this.CmbFanProg.EndUpdate();

        }

        // Set up the system information group
        public void SetupSys() {

            // Set both system information and status to empty
            this.SysInfo = "";
            this.SysStatus = "";

            // Reflect the current "start with Windows" task state
            try { this.ChkAutoStart.Checked = Hw.TaskGet(Config.TaskId.Gui); } catch { }

            // Reflect the active Windows power mode
            SyncPwrMode();

            // Apply the update
            this.UpdateSysRtf();

        }

        // Set up the temperature readout description (only has to be done once)
        public void SetupTmp() {

            // Iterate through all temperature sensor and initialize each
            for(int i = 0; i < Context.Op.Platform.Temperature.Length; i++)
                SetupTmpItem(i);

        }

        // Set up the description of an item within the temperature group
        public void SetupTmpItem(int index) {

            // Retrieve caption candidates
            string indexString = index.ToString();
            string captionOriginal = Context.Op.Platform.Temperature[index].GetName();
            string captionLocaleId = Config.L_GUI_MAIN + Gui.G_TMP + captionOriginal;
            string captionLocalized = Config.Locale.Get(captionLocaleId);

            // Locate the pertinent caption label
            // Rebuilt layout: the per-sensor grid is parked off-screen and its labels
            // carry no Name, so this lookup finds nothing — nothing to set up per item.
            Control[] found = this.GrpTmp.Controls.Find(
                Gui.T_LBL + Gui.G_TMP + indexString + Gui.S_CAP, false);
            if(found.Length == 0)
                return;
            Label label = (Label) found[0];

            // Also locate the value label
            Label labelValue =
                ((Label) this.GrpTmp.Controls[this.GrpTmp.Controls.IndexOf(label) + 1]);

            // Strike-through sensors set not to be used
            if(!Context.Op.Platform.TemperatureUse[index])
                label.Font = new Font(label.Font, FontStyle.Strikeout);

            // Determine the best caption and apply it
            label.Text = captionLocalized == captionLocaleId ?
                captionOriginal : captionLocalized;

            // Check if a specific localized tooltip is available,
            // and set it; otherwise use the default fallback tooltip
            string toolTipLocaleId = Config.L_GUI_TIP + Gui.G_TMP + captionOriginal;
            string toolTipLocalized = Config.Locale.Get(toolTipLocaleId);
            string toolTip = toolTipLocalized != toolTipLocaleId ?
                toolTipLocalized : Config.Locale.Get(Config.L_GUI_TIP + Gui.G_TMP + "Unknown");

            this.Tip.SetToolTip(label, toolTip);
            this.Tip.SetToolTip(labelValue, toolTip);

        }
#endregion

#region Updates
        // Updates all of the form
        public void UpdateAll() {

            // Only pay for a synchronous hardware read when there is nothing to render
            // yet. The monitor thread republishes every UpdateMonitorInterval seconds,
            // so an already-published snapshot is at most a few seconds old — not worth
            // blocking the UI thread (and the window opening) on a BIOS + EC round-trip.
            if(Context.Monitor != null && Context.Monitor.Current == null)
                Context.Monitor.SampleNow();

            // Update form dimensions following a scaling change
            UpdateDpi(this.LastDpi);

            // Update the fan group monitoring section
            UpdateFan();

            // Update the fan group controls section
            UpdateFanCtl();

            // Update the keyboard group
            UpdateKbd();

            // Update the system status group
            UpdateSys();

            // Update the temperature group
            UpdateTmp();

            // Feed the live graph
            UpdateChart();

        }

        // Public entry for the tray timer tick (per Config.UpdateMonitorInterval)
        public void UpdateChartTick() { UpdateChart(); }

        // Pushes the latest snapshot into the top graph
        private void UpdateChart() {
            MonitorSnapshot s = Context.Monitor != null ? Context.Monitor.Current : null;
            if(s == null || this.Chart == null)
                return;
            int cpuR = (s.FanSpeed != null && s.FanSpeed.Length > 0) ? s.FanSpeed[0] : 0;
            int gpuR = (s.FanSpeed != null && s.FanSpeed.Length > 1) ? s.FanSpeed[1] : 0;
            this.Chart.Push(s.CpuTemp, s.GpuTemp, cpuR, gpuR);
        }

        // Updates the form dimensions following a scaling change
        private void UpdateDpi(int dpi) {

            // Skip if configured not to resize
            if(Config.GuiDpiChangeResize) {

                // Suspend the layout
                this.SuspendLayout();
                this.AutoSize = false;

                // Adjust the client size to account for differences
                // if the form gets scaled dynamically to another DPI setting
                this.Size = new Size(
                    (int) (1080 + ((dpi - 96) * Config.DpiSizeAdjFactorX / 100)),
                    (int) (441 + ((dpi - 96) * Config.DpiSizeAdjFactorY / 100)));

                // Resume the layout
                this.AutoSize = true;
                this.ResumeLayout(false);

                // Update the last DPI value for the next rescaling
                this.LastDpi = dpi;

            }

        }

        // Updates the fan group monitoring section — renders the latest monitor snapshot,
        // no hardware I/O on the UI thread (issue #98).
        public void UpdateFan() {

            MonitorSnapshot s = Context.Monitor != null ? Context.Monitor.Current : null;
            if(s == null)
                return;

            // Update the fan speed [rpm].
            //
            // A zero is shown as "auto", not as a number. On this board the rpm figure is
            // BiosLevelMirror x100 — the level OmenMon last commanded, read back — so it
            // reads 0 whenever nothing is being commanded: at startup, and whenever the
            // BIOS countdown lapses and the firmware takes fan control back. The fans are
            // spinning perfectly well in that state; we simply do not know how fast.
            // Printing "0 rpm" for "no reading" claims the fans have stopped, which is
            // both false and alarming.
            try {
                this.LblFan0Val.Text = s.FanSpeed[0] > 0
                    ? s.FanSpeed[0].ToString(Config.FormatFanSpeed) : "auto";
                this.LblFan1Val.Text = s.FanSpeed[1] > 0
                    ? s.FanSpeed[1].ToString(Config.FormatFanSpeed) : "auto";
            } catch { }

            // Update the fan level [krpm]
            // Hold if the controls are not unlocked for the user to set the speed manually
            try {
                if(!this.TrkFan0Lvl.Enabled)
                    this.TrkFan0Lvl.Value = Conv.GetConstrained(
                        s.FanLevel[0], this.TrkFan0Lvl.Minimum, this.TrkFan1Lvl.Maximum);
                if(!this.TrkFan1Lvl.Enabled)
                    this.TrkFan1Lvl.Value = Conv.GetConstrained(
                        s.FanLevel[1], this.TrkFan1Lvl.Minimum, this.TrkFan1Lvl.Maximum);
            } catch { }

            // Update the fan rate [%]
            try {
                this.BarFan0Rte.Value = s.FanRate[0];
                this.BarFan1Rte.Value = s.FanRate[1];
                this.LblFan0Rte.Text = this.BarFan0Rte.Value.ToString();
                this.LblFan1Rte.Text = this.BarFan1Rte.Value.ToString();
            } catch { }

            // Show the countdown, if applicable
            this.LblFanCountdown.Text = s.Countdown > 0 ?
                s.Countdown.ToString() + Config.Locale.Get(Config.L_UNIT + "TimeSecond" + Config.LS_CUSTOM_FONT) : "";

            // (Constant-speed countdown maintenance now runs on the monitor thread, gated by
            // Op.ConstantSpeedActive — set from EventActionFanSet / UpdateFanCtl. See GuiMonitor.Pass.)

            // Update the current fan mode
            // Hold if the Set button is already highlighted or the list is currently open
            if(!this.BtnFanSet.Checked && !this.CmbFanMode.DroppedDown)
            try {
                this.CmbFanMode.SelectedValue = Enum.GetName(typeof(BiosData.FanMode), s.Mode);
            } catch { }

            // Update the current fan program
            // Hold if the Set button is already highlighted or the list is currently open
            if(!this.BtnFanSet.Checked && !this.CmbFanProg.DroppedDown)
            try {
                this.CmbFanProg.SelectedValue = s.ProgramName;

                // Keep the inline curve in step with whatever profile is actually
                // running — at startup the program name only becomes known once the
                // first snapshot lands, after the constructor has already run.
                if(!string.IsNullOrEmpty(s.ProgramName)
                    && this.Curve != null && this.Curve.ProgramName != s.ProgramName)
                    this.Curve.LoadProgram(s.ProgramName);
            } catch { }


        }

        // Updates the fan group controls section. Renders from the monitor snapshot —
        // callers that change fan state (user actions) publish a fresh snapshot via
        // SampleNow() first; the cancel/refresh paths intentionally render the last
        // published (unchanged) state.
        public void UpdateFanCtl() {

            MonitorSnapshot s = Context.Monitor != null ? Context.Monitor.Current : null;
            if(s == null)
                return;

            // Query and retrieve fan control state
            bool isFanMax = s.Max;
            bool isFanOff = s.Off;

            // Fan program is active if a flag to that effect is set
            // This takes precedence over all the other queries
            if(s.ProgramEnabled)
                this.RdoFanProg.Checked = true;

            // If the fan is switched off, the setting should reflect that
            else if(isFanOff)
                this.RdoFanOff.Checked = true;

            // If the fan is set to maximum mode, the setting should reflect that
            else if(isFanMax)
                this.RdoFanMax.Checked = true;

            // If trackbars are unlocked, we are in constant fan speed mode
            else if(this.TrkFan0Lvl.Enabled)
                this.RdoFanConst.Checked = true;

            // If none of the above, the fan is in the default automatic state
            else
                this.RdoFanAuto.Checked = true;

            // Restore the Set button default look
            this.BtnFanSet.Checked = false;

            // Keep the monitor's constant-speed countdown maintenance in sync with the
            // displayed mode (the monitor cannot read the radio button off-thread).
            Context.Op.ConstantSpeedActive = this.RdoFanConst.Checked;

        }

        // Updates the keyboard group
        public void UpdateKbd() {

            // Restore the default color of the color as parameter text box
            this.TxtKbdColorVal.ForeColor = Color.Empty;

            // Disable the backlight toggle for unsupported devices,
            // otherwise update the keyboard backlight status
            if(!Context.Op.Platform.System.GetKbdBacklightSupport()) {
                this.ChkKbdBacklight.Checked = false;
                this.ChkKbdBacklight.Enabled = false;
            } else if(Kbd != null) // Use the keyboard class
                this.ChkKbdBacklight.Checked = Kbd.GetBacklight();
            else // Fallback case for no customizable backlight color
                this.ChkKbdBacklight.Checked =
                    Context.Op.Platform.System.GetKbdBacklight() == BiosData.Backlight.On ? true : false;

            // Disable the interface when backlight is off or no support
            if(Kbd == null || !Kbd.GetBacklight()) {

                this.CmbKbdColorPreset.DataSource = null;
                this.CmbKbdColorPreset.Enabled = false;
                this.TxtKbdColorVal.Enabled = false;
                this.TxtKbdColorVal.Text = "";
                this.BtnKbdColorPresetDel.Enabled = false;
                this.BtnKbdColorPresetSet.Enabled = false;
                this.PicKbd.Cursor = Cursors.Default;

            } else {

                // Enable the interface when backlight is on
                this.CmbKbdColorPreset.BeginUpdate();
                ColorPresets.Clear();
                foreach(string name in Config.ColorPreset.Keys)
                    ColorPresets.Add( new { Text = name.StartsWith(Config.ColorPresetDefaultPrefix) ?
                        Config.Locale.Get(Config.L_GUI_MENU + Gui.M_ACT + Gui.G_KBD + "ColorPreset" + name) : name, Value = name } );
                this.CmbKbdColorPreset.DataSource = ColorPresets;
                this.CmbKbdColorPreset.DisplayMember = "Text";
                this.CmbKbdColorPreset.ValueMember = "Value";
                this.CmbKbdColorPreset.SelectedValue = Kbd.GetPreset();
                this.CmbKbdColorPreset.Enabled = true;
                this.CmbKbdColorPreset.EndUpdate();
                this.TxtKbdColorVal.Enabled = true;
                this.TxtKbdColorVal.Text = Kbd.GetParam();
                this.BtnKbdColorPresetDel.Enabled = true;
                this.BtnKbdColorPresetSet.Enabled = true;
                this.PicKbd.Cursor = Cursors.Hand;

            }

            bool on = Kbd != null && Kbd.GetBacklight();
            this.TrkKbdR.Enabled = this.TrkKbdG.Enabled = this.TrkKbdB.Enabled = on;
            SyncKbdRgb();

        }

        // Last plausible temperature readings, used to bridge sensor dropouts
        private int lastGoodCpuTemp, lastGoodGpuTemp;

        // Reentrancy guard for the R/G/B sliders and the zone selector
        private bool kbdRgbSyncing;

        // CmbKbdZone item order <-> BiosData.KbdZone (enum: Right=0, Middle=1, Left=2, Wasd=3)
        private static readonly BiosData.KbdZone[] KbdZoneByCombo = {
            BiosData.KbdZone.Left, BiosData.KbdZone.Left, BiosData.KbdZone.Middle,
            BiosData.KbdZone.Right, BiosData.KbdZone.Wasd
        };
        private static int ComboIndexForZone(BiosData.KbdZone z) {
            switch(z) {
                case BiosData.KbdZone.Left:   return 1;
                case BiosData.KbdZone.Middle: return 2;
                case BiosData.KbdZone.Right:  return 3;
                case BiosData.KbdZone.Wasd:   return 4;
                default: return 0;
            }
        }
        private bool KbdUniform => this.CmbKbdZone.SelectedIndex <= 0;

        // Zone selector changed -> point the sliders at that zone (or leave in "All" mode)
        private void EventKbdZoneSelect(object sender, EventArgs e) {
            if(Kbd == null)
                return;
            if(!KbdUniform)
                Kbd.SetZone(KbdZoneByCombo[this.CmbKbdZone.SelectedIndex]);
            SyncKbdRgb();
        }

        // R/G/B sliders -> whole keyboard (uniform) or the selected zone
        private void EventKbdRgbScroll(object sender, EventArgs e) {
            if(kbdRgbSyncing || Kbd == null || !Kbd.GetBacklight())
                return;
            Color c = Color.FromArgb(this.TrkKbdR.Value, this.TrkKbdG.Value, this.TrkKbdB.Value);
            int zi = this.CmbKbdZone.SelectedIndex;
            if(zi <= 0) {
                Kbd.SetColors(c.ToArgb());                    // All zones (uniform)
            } else {
                Kbd.SetColor(KbdZoneByCombo[zi], c.ToArgb()); // target the picked zone directly
            }
            this.PnlKbdSwatch.BackColor = c;
            this.TxtKbdColorVal.Text = Kbd.GetParam();
            try { this.CmbKbdColorPreset.SelectedValue = Kbd.GetPreset(); } catch { }
        }

        // Push the current colour into the sliders + swatch without echoing events.
        // In "All" mode the Right zone's colour represents the (uniform) keyboard.
        private void SyncKbdRgb() {
            if(Kbd == null)
                return;
            try {
                kbdRgbSyncing = true;
                int zi = this.CmbKbdZone.SelectedIndex;
                Color c = Color.FromArgb(Kbd.GetColor(
                    zi <= 0 ? BiosData.KbdZone.Right : KbdZoneByCombo[zi]));
                this.TrkKbdR.Value = c.R;
                this.TrkKbdG.Value = c.G;
                this.TrkKbdB.Value = c.B;
                this.PnlKbdSwatch.BackColor = c;
            } catch { } finally {
                kbdRgbSyncing = false;
            }
        }

        // Keeps updating the color as it changes in the Color Picker dialog
        public void UpdateKbdCallback(int color) {
            Kbd.SetColor(ColorTranslator.FromWin32(color).ToArgb());
            this.TxtKbdColorVal.Text = Kbd.GetParam();
            this.CmbKbdColorPreset.SelectedValue = Kbd.GetPreset();
            SyncKbdRgb();
        }

        // Update the system information while preserving the status message.
        // Renders from the monitor snapshot — no BIOS calls on the UI thread (issue #98).
        public void UpdateSys() {

            MonitorSnapshot s = Context.Monitor != null ? Context.Monitor.Current : null;
            if(s == null)
                return;

            // Update the system info string.
            // Same fields as before, but the groups are fenced off with a dim middle dot
            // instead of a plain space. The content is intentionally terse; the separator
            // is what lets the eye find where one fact ends and the next begins.
            const string SEP = Conv.RTF_CF1 + " · ";

            this.SysInfo = ""
                + Conv.RTF_CF6 + s.Manufacturer + " "
                + Conv.RTF_CF5 + s.Product + " "
                + Conv.RTF_CF1 + s.Version
                + SEP
                + Conv.RTF_CF1 + Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_SYS + "Born") + " "
                    + Conv.RTF_CF5 + s.BornDate
                + (s.CpuPl4 == 0 ? ""
                    : SEP
                    + Conv.RTF_CF1 + Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_SYS + "CpuPl4") + " "
                    + Conv.RTF_CF5 + s.CpuPl4.ToString()
                    + Conv.RTF_CF1 + Config.Locale.Get(Config.L_UNIT + "Power"))
                + SEP
                + Conv.RTF_CF1 + (s.IsFullPower ?
                    Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_SYS + "Adapter"
                        + Enum.GetName(typeof(BiosData.AdapterStatus), s.AdapterStatus))
                    : Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_SYS + "AdapterBatteryPower"))
                    + Conv.RTF_LINE
                + Conv.RTF_CF1 + Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_SYS + "Gpu") + " "
                    + Conv.RTF_CF5 + Enum.GetName(typeof(BiosData.GpuMode), s.GpuMode)
                + SEP
                + Conv.RTF_CF1 + Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_SYS + "GpuDState") + " "
                    + Conv.RTF_CF5 + Enum.GetName(typeof(BiosData.GpuDState), s.GpuDState)
                + SEP
                + (s.GpuCustomTgp == BiosData.GpuCustomTgp.On ?
                    Conv.RTF_CF6 + Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_SYS + "GpuCustomTgp")
                    : Conv.RTF_CF1 + Conv.RTF_STRIKE1 + Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_SYS + "GpuCustomTgp") + Conv.RTF_STRIKE0) + " "
                + (s.GpuPpab == BiosData.GpuPpab.On ?
                    Conv.RTF_CF6 + Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_SYS + "GpuPpab")
                    : Conv.RTF_CF1 + Conv.RTF_STRIKE1 + Config.Locale.Get(Config.L_GUI_MAIN + Gui.G_SYS + "GpuPpab") + Conv.RTF_STRIKE0)
                + SEP
                + Conv.RTF_CF1 + Config.Locale.Get(
                    Config.L_GUI_MAIN + Gui.G_SYS + "Throttling"
                        + Enum.GetName(typeof(BiosData.Throttling), s.Throttling)) + Conv.RTF_LINE
                + Conv.RTF_CF2;

            // Apply the update
            UpdateSysRtf();

        }

        // Update the system status message only
        public void UpdateSysMsg(string message = "") {

            // Add timestamp if the message is not empty
            if(message != "")
                message = message + Conv.RTF_CF1 + " @ " + DateTime.Now.ToString(Config.TimestampFormat);

            // Set the status message
            this.SysStatus = message;

            // Apply the update
            UpdateSysRtf();

        }

        // Update the system status rich-text field
        private void UpdateSysRtf() {
            this.RtfSysInfo.Rtf =
                Config.SysInfoRtfHeader
                + Conv.GetUnicodeStringRtf(this.SysInfo)
                + Conv.GetUnicodeStringRtf(this.SysStatus)
                + Config.SysInfoRtfFooter;
        }

        // Updates the temperature group — renders from the monitor snapshot (the monitor
        // already performed the batched EC read), no hardware I/O on the UI thread.
        public void UpdateTmp() {

            MonitorSnapshot s = Context.Monitor != null ? Context.Monitor.Current : null;
            if(s == null || s.TempValue == null)
                return;

            // Rebuilt layout: the two hero cells show named sensors chosen for this board;
            // the per-sensor labels are parked off-screen but kept fed for completeness.
            Label[] parked = {
                LblTmp2Val, LblTmp3Val, LblTmp4Val, LblTmp5Val,
                LblTmp6Val, LblTmp7Val, LblTmp8Val };
            for(int i = 2; i < Context.Op.Platform.Temperature.Length && i < s.TempValue.Length && (i - 2) < parked.Length; i++)
                parked[i - 2].Text = s.TempValue[i] > 0 ? s.TempValue[i].ToString() : "";

            // CpuTemp/GpuTemp are already resolved in the monitor snapshot with the
            // board-aware policy (max of EC CPUT and the WMI BIOS sensor, etc.).
            // Bridge sensor dropouts (the GPU sensor intermittently answers 0) by
            // holding the last plausible reading rather than showing a false 0.
            int cpu = s.CpuTemp, gpu = s.GpuTemp;
            if(cpu > 0 && cpu < 120) lastGoodCpuTemp = cpu; else cpu = lastGoodCpuTemp;
            if(gpu > 0 && gpu < 120) lastGoodGpuTemp = gpu; else gpu = lastGoodGpuTemp;

            string u = Config.TemperatureUseFahrenheit ? "°F" : "°C";
            this.LblTmp0Val.Text = cpu > 0 ? FmtTemp(cpu) + " " + u : "—";
            this.LblTmp1Val.Text = gpu > 0 ? FmtTemp(gpu) + " " + u : "—";

        }

        private int FmtTemp(int c) {
            return Config.TemperatureUseFahrenheit ? (c * 9 / 5) + 32 : c;
        }
#endregion
 
    }

}
