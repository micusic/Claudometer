using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TokenMeter
{
    /// <summary>
    /// The one-time login. Opens the real Anthropic consent page in the user's browser; the
    /// user signs in there and pastes back the one-time code. No password passes through here.
    /// </summary>
    public class LoginForm : Form
    {
        private static Color Bg { get { return Theme.Bg; } }
        private static Color Fg { get { return Theme.Text; } }
        private static Color Muted { get { return Theme.Muted; } }
        private static Color Field { get { return Theme.Card; } }
        private const string FontName = "Microsoft YaHei UI";

        private readonly string _verifier = OAuth.NewVerifier();
        private readonly string _state = OAuth.NewState();

        private TextBox _code;
        private Label _status;
        private Button _finish;
        private readonly System.Windows.Forms.Timer _cooldown = new System.Windows.Forms.Timer();
        private int _cooldownLeft;
        private int _rateLimitHits;   // escalates the wait each consecutive 429

        public TokenSet Result { get; private set; }

        public LoginForm()
        {
            Text = "登录 Claude";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font(FontName, 9f);
            ClientSize = new Size(500, 340);
            Build();
        }

        private void Build()
        {
            int y = 14;
            Note("用你自己的浏览器完成登录。登录在 Anthropic 官方页面进行，本程序看不到你的密码，\n" +
                 "只会拿到一个用量查询用的令牌，加密保存在本机（仅当前 Windows 用户可解）。", ref y, 52);

            y += 4;
            Step("1", "打开授权页并登录 / 同意", ref y);
            var open = new Button();
            open.Text = "在浏览器中打开登录页";
            open.SetBounds(40, y, 220, 30);
            Style(open, Theme.Accent);
            open.Click += delegate { OpenBrowser(); };
            Controls.Add(open);
            y += 42;

            Step("2", "把页面给出的 code 粘贴到这里", ref y);
            _code = new TextBox();
            _code.SetBounds(40, y, 420, 24);
            _code.BackColor = Field;
            _code.ForeColor = Fg;
            _code.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_code);
            y += 34;

            _finish = new Button();
            _finish.Text = "完成登录";
            _finish.SetBounds(40, y, 130, 30);
            Style(_finish, Theme.Accent);
            _finish.Click += delegate { Finish(); };
            Controls.Add(_finish);

            _cooldown.Interval = 1000;
            _cooldown.Tick += delegate { Tick(); };

            var cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(180, y, 90, 30);
            Style(cancel, Field);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;
            y += 40;

            _status = new Label();
            _status.SetBounds(16, y, ClientSize.Width - 32, 40);
            _status.ForeColor = Muted;
            Controls.Add(_status);
        }

        private void OpenBrowser()
        {
            try
            {
                string url = OAuth.BuildAuthorizeUrl(_verifier, _state);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                Say("已打开浏览器。登录并同意后，页面会显示一段 code —— 复制它，粘贴到上面第 2 步。", Muted);
            }
            catch (Exception ex)
            {
                Say("打开浏览器失败：" + ex.Message, IconRenderer.Danger);
            }
        }

        private void Finish()
        {
            string pasted = _code.Text == null ? "" : _code.Text.Trim();
            if (pasted.Length < 8) { Say("请先粘贴页面给出的 code。", IconRenderer.Warn); return; }

            Say("正在校验并换取令牌…", Muted);
            Enabled = false;
            try
            {
                string err;
                TokenSet t = OAuth.Exchange(pasted, _verifier, _state, out err);
                if (t == null)
                {
                    Say(err ?? "换取令牌失败。", IconRenderer.Danger);
                    if (err != null && err.Contains("限流")) StartCooldown(err);
                    return;
                }

                // Prove the token actually works before saving it, so a bad login fails here
                // rather than silently later.
                UsageReading reading;
                string msg;
                UsageApi.Status st = UsageApi.Fetch(t.AccessToken, out reading, out msg);
                if (st == UsageApi.Status.Unauthorized)
                {
                    Say("令牌被拒（" + msg + "）。请重试登录。", IconRenderer.Danger);
                    return;
                }
                // RateLimited or a transient error still means the token is valid - accept it.

                OAuth.Save(t);
                Result = t;
                DialogResult = DialogResult.OK;
                Close();
            }
            finally { Enabled = true; }
        }

        private void Say(string s, Color c) { _status.ForeColor = c; _status.Text = s; }

        /// <summary>
        /// After a 429, hold the button down - and escalate the wait each consecutive hit, because
        /// the endpoint's degraded state deepens with every attempt. Server-suggested "N 秒" is
        /// honoured when present; otherwise back off 2 → 5 → 10 → 20 minutes.
        /// </summary>
        private void StartCooldown(string err)
        {
            int wait;
            var m = System.Text.RegularExpressions.Regex.Match(err, "(\\d+)\\s*秒");
            if (m.Success && int.TryParse(m.Groups[1].Value, out wait))
            {
                wait = Math.Max(15, wait);
            }
            else
            {
                _rateLimitHits++;
                int[] mins = { 2, 5, 10, 20 };
                wait = mins[Math.Min(_rateLimitHits - 1, mins.Length - 1)] * 60;
            }
            _cooldownLeft = wait;
            _finish.Enabled = false;
            _cooldown.Start();
            Tick();
        }

        private void Tick()
        {
            if (_cooldownLeft <= 0)
            {
                _cooldown.Stop();
                _finish.Enabled = true;
                _finish.Text = "完成登录";
                Say("可以再次点「完成登录」了（用同一个 code；若 code 已过期就重新登录）。", Muted);
                return;
            }
            int mm = _cooldownLeft / 60, ss = _cooldownLeft % 60;
            _finish.Text = "完成登录 (" + (mm > 0 ? mm + "分" + ss.ToString("00") : ss + "秒") + ")";
            _cooldownLeft--;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _cooldown.Dispose();
            base.Dispose(disposing);
        }

        private void Step(string n, string text, ref int y)
        {
            var b = new Label();
            b.Text = n;
            b.Font = new Font(FontName, 9f, FontStyle.Bold);
            b.SetBounds(16, y, 20, 20);
            b.ForeColor = Theme.Accent;
            Controls.Add(b);
            var l = new Label();
            l.Text = text;
            l.SetBounds(40, y, ClientSize.Width - 56, 20);
            l.ForeColor = Fg;
            Controls.Add(l);
            y += 26;
        }

        private void Note(string s, ref int y, int h)
        {
            var l = new Label();
            l.Text = s;
            l.SetBounds(16, y, ClientSize.Width - 32, h);
            l.ForeColor = Muted;
            Controls.Add(l);
            y += h;
        }

        private void Style(Button b, Color back)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = back;
            b.ForeColor = Fg;
            b.FlatAppearance.BorderColor = Theme.Border;
        }
    }
}
