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
            Text = L.S("login.win.title");
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
            Note(L.S("login.win.note"), ref y, 52);

            y += 4;
            Step("1", L.S("login.step1"), ref y);
            var open = new Button();
            open.Text = L.S("login.openbtn");
            open.SetBounds(40, y, 220, 30);
            Style(open, Theme.Accent);
            open.Click += delegate { OpenBrowser(); };
            Controls.Add(open);
            y += 42;

            Step("2", L.S("login.step2"), ref y);
            _code = new TextBox();
            _code.SetBounds(40, y, 420, 24);
            _code.BackColor = Field;
            _code.ForeColor = Fg;
            _code.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_code);
            y += 34;

            _finish = new Button();
            _finish.Text = L.S("login.finish");
            _finish.SetBounds(40, y, 150, 30);
            Style(_finish, Theme.Accent);
            _finish.Click += delegate { Finish(); };
            Controls.Add(_finish);

            _cooldown.Interval = 1000;
            _cooldown.Tick += delegate { Tick(); };

            var cancel = new Button();
            cancel.Text = L.S("settings.cancel");
            cancel.SetBounds(200, y, 90, 30);
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
                Say(L.S("login.opened"), Muted);
            }
            catch (Exception ex)
            {
                Say(L.F("login.openfail", ex.Message), IconRenderer.Danger);
            }
        }

        private void Finish()
        {
            string pasted = _code.Text == null ? "" : _code.Text.Trim();
            if (pasted.Length < 8) { Say(L.S("login.pastefirst"), IconRenderer.Warn); return; }

            Say(L.S("login.verifying"), Muted);
            Enabled = false;
            try
            {
                string err; bool rateLimited;
                TokenSet t = OAuth.Exchange(pasted, _verifier, _state, out err, out rateLimited);
                if (t == null)
                {
                    Say(err ?? L.S("login.exchangefail"), IconRenderer.Danger);
                    if (rateLimited) StartCooldown(err);
                    return;
                }

                // Prove the token actually works before saving it, so a bad login fails here
                // rather than silently later.
                UsageReading reading;
                string msg;
                UsageApi.Status st = UsageApi.Fetch(t.AccessToken, out reading, out msg);
                if (st == UsageApi.Status.Unauthorized)
                {
                    Say(L.F("login.rejected", msg), IconRenderer.Danger);
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
            // Only the "retry in N s" form carries a number; the generic form has none. So a digit
            // in the message means honour it (unit-independent); otherwise back off progressively.
            var m = System.Text.RegularExpressions.Regex.Match(err ?? "", "(\\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out wait) && wait > 0 && wait <= 300)
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
                _finish.Text = L.S("login.finish");
                Say(L.S("login.ready"), Muted);
                return;
            }
            _finish.Text = L.F("login.finish.count", Fmt.Duration(TimeSpan.FromSeconds(_cooldownLeft)));
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
