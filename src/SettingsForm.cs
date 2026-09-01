using System;
using System.Drawing;
using System.Windows.Forms;

namespace TokenMeter
{
    public class SettingsForm : Form
    {
        private static Color Bg { get { return Theme.Bg; } }
        private static Color Fg { get { return Theme.Text; } }
        private static Color Muted { get { return Theme.Muted; } }
        private static Color Field { get { return Theme.Card; } }
        private const string FontName = "Microsoft YaHei UI";

        private readonly AppConfig _cfg;

        private NumericUpDown _warn, _danger, _refresh;
        private CheckBox _notify, _autostart;
        private ComboBox _tz, _theme;

        public SettingsForm(AppConfig cfg)
        {
            _cfg = cfg;
            Build();
        }

        private void Build()
        {
            Text = "Claudometer 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font(FontName, 9f);
            ClientSize = new Size(470, 366);

            int y = 14;
            AddNote("只显示官方用量接口的数据，需先登录 Claude。\n" +
                    "本地只保存接口返回的读数，不做任何推测。", ref y);
            y += 6;

            var tzl = new Label();
            tzl.Text = "显示时区";
            tzl.SetBounds(16, y + 4, 210, 22);
            tzl.ForeColor = Fg;
            Controls.Add(tzl);
            _tz = new ComboBox();
            _tz.DropDownStyle = ComboBoxStyle.DropDownList;
            _tz.SetBounds(240, y, 210, 24);
            _tz.BackColor = Field;
            _tz.ForeColor = Fg;
            foreach (TimeZoneInfo z in TimeZoneInfo.GetSystemTimeZones()) _tz.Items.Add(z.Id);
            _tz.SelectedItem = _tz.Items.Contains(_cfg.TimeZoneId) ? _cfg.TimeZoneId : Tz.DefaultId;
            Controls.Add(_tz);
            y += 32;

            var thl = new Label();
            thl.Text = "主题";
            thl.SetBounds(16, y + 4, 210, 22);
            thl.ForeColor = Fg;
            Controls.Add(thl);
            _theme = new ComboBox();
            _theme.DropDownStyle = ComboBoxStyle.DropDownList;
            _theme.SetBounds(240, y, 210, 24);
            _theme.BackColor = Field;
            _theme.ForeColor = Fg;
            _theme.Items.Add("浅色");
            _theme.Items.Add("深色");
            _theme.SelectedIndex = string.Equals(_cfg.ThemeMode, "dark", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            Controls.Add(_theme);
            y += 32;

            _warn = AddNum("预警阈值 (%)", (decimal)Math.Round(_cfg.WarnPct * 100), 5, 98, 5, ref y);
            _danger = AddNum("危险阈值 (%)", (decimal)Math.Round(_cfg.DangerPct * 100), 10, 99, 5, ref y);
            _refresh = AddNum("轮询间隔 (秒)", _cfg.PollSeconds, 60, 900, 30, ref y);

            y += 6;
            _notify = AddCheck("启用阈值通知", _cfg.Notify, ref y);
            _autostart = AddCheck("开机自动启动", Autostart.IsEnabled(), ref y);

            y += 10;
            var ok = new Button();
            ok.Text = "保存";
            ok.SetBounds(ClientSize.Width - 190, y, 84, 28);
            ok.FlatStyle = FlatStyle.Flat;
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Fg;
            ok.FlatAppearance.BorderColor = Theme.Accent;
            ok.Click += OnSave;
            Controls.Add(ok);

            var cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(ClientSize.Width - 98, y, 84, 28);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Field;
            cancel.ForeColor = Fg;
            cancel.FlatAppearance.BorderColor = Theme.Border;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void AddNote(string s, ref int y)
        {
            var l = new Label();
            l.Text = s;
            l.SetBounds(16, y, ClientSize.Width - 24, 68);
            l.ForeColor = Muted;
            l.UseMnemonic = false;   // "/usage" etc. must not be read as an accelerator
            Controls.Add(l);
            y += 60;
        }

        private void Style(Button b, Color back)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = back;
            b.ForeColor = b.Enabled ? Fg : Muted;
            b.FlatAppearance.BorderColor = Theme.Border;
        }

        private NumericUpDown AddNum(string label, decimal value, decimal min, decimal max,
                                     decimal step, ref int y)
        {
            var l = new Label();
            l.Text = label;
            l.SetBounds(16, y + 4, 210, 22);
            l.ForeColor = Fg;
            Controls.Add(l);

            var n = new NumericUpDown();
            n.SetBounds(240, y, 100, 24);
            n.Minimum = min;
            n.Maximum = max;
            n.Increment = step;
            n.Value = Math.Min(max, Math.Max(min, value));
            n.BackColor = Field;
            n.ForeColor = Fg;
            n.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(n);

            y += 32;
            return n;
        }

        private CheckBox AddCheck(string label, bool value, ref int y)
        {
            var c = new CheckBox();
            c.Text = label;
            c.SetBounds(16, y, ClientSize.Width - 32, 24);
            c.Checked = value;
            c.ForeColor = Fg;
            Controls.Add(c);
            y += 26;
            return c;
        }

        private static decimal Clamp(NumericUpDown n, decimal v)
        {
            return Math.Min(n.Maximum, Math.Max(n.Minimum, v));
        }

        private void OnSave(object sender, EventArgs e)
        {
            _cfg.WarnPct = (double)_warn.Value / 100.0;
            _cfg.DangerPct = (double)_danger.Value / 100.0;
            _cfg.PollSeconds = (int)_refresh.Value;
            _cfg.Notify = _notify.Checked;
            if (_tz.SelectedItem != null)
            {
                _cfg.TimeZoneId = _tz.SelectedItem.ToString();
                _cfg.ThemeMode = _theme.SelectedIndex == 1 ? "dark" : "light";
                Tz.Use(_cfg.TimeZoneId);
            }
            try { _cfg.Save(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存设置失败：" + ex.Message, "Claudometer",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            try { Autostart.Set(_autostart.Checked); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "设置开机自启失败：" + ex.Message, "Claudometer",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Run-key autostart. A registry value is reversible and needs no COM shortcut plumbing.</summary>
    public static class Autostart
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "Claudometer";
        private const string Legacy = "TokenMeter";

        public static bool IsEnabled()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key, false))
                    return k != null && (k.GetValue(Name) != null || k.GetValue(Legacy) != null);
            }
            catch (Exception) { return false; }
        }

        public static void Set(bool on)
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key, true))
            {
                if (k == null) return;
                if (k.GetValue(Legacy) != null) k.DeleteValue(Legacy, false);   // drop the pre-rename key
                if (on)
                    k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
                else if (k.GetValue(Name) != null)
                    k.DeleteValue(Name, false);
            }
        }
    }
}
