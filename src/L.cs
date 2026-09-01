using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace TokenMeter
{
    /// <summary>
    /// Localization. Every user-facing string lives here keyed by id, with one column per
    /// language (order: en, zh, fr, ru, ja). Default is English. Switching language also switches
    /// the thread culture so date/day-name formatting follows suit.
    /// </summary>
    public static class L
    {
        public static readonly string[] Codes = { "en", "zh", "fr", "ru", "ja" };
        public static readonly string[] Names = { "English", "中文", "Français", "Русский", "日本語" };
        private static readonly string[] Cultures = { "en-US", "zh-CN", "fr-FR", "ru-RU", "ja-JP" };

        private static int _i = 0;   // current language index
        private static readonly Dictionary<string, string[]> T = new Dictionary<string, string[]>();

        public static string Lang { get { return Codes[_i]; } }

        public static void Use(string code)
        {
            int idx = 0;
            for (int i = 0; i < Codes.Length; i++)
                if (string.Equals(Codes[i], code, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
            _i = idx;
            try
            {
                var ci = CultureInfo.GetCultureInfo(Cultures[_i]);
                Thread.CurrentThread.CurrentCulture = ci;
                Thread.CurrentThread.CurrentUICulture = ci;
                CultureInfo.DefaultThreadCurrentCulture = ci;
                CultureInfo.DefaultThreadCurrentUICulture = ci;
            }
            catch (Exception) { }
        }

        public static int IndexOf(string code)
        {
            for (int i = 0; i < Codes.Length; i++)
                if (string.Equals(Codes[i], code, StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }

        /// <summary>Translated string for the current language (falls back to English, then the key).</summary>
        public static string S(string key)
        {
            string[] row;
            if (!T.TryGetValue(key, out row)) return key;
            string v = _i < row.Length ? row[_i] : null;
            if (string.IsNullOrEmpty(v)) v = row.Length > 0 ? row[0] : key;
            return v ?? key;
        }

        public static string F(string key, params object[] args) { return string.Format(S(key), args); }

        private static void Add(string key, string en, string zh, string fr, string ru, string ja)
        {
            T[key] = new[] { en, zh, fr, ru, ja };
        }

        static L()
        {
            // ---- panel ----
            Add("panel.starting", "Starting…", "启动中…", "Démarrage…", "Запуск…", "起動中…");
            Add("login.title", "Sign in to Claude", "需要登录 Claude", "Connexion à Claude", "Вход в Claude", "Claude にログイン");
            Add("login.desc1", "Shows only data from the official usage API.", "本工具只显示官方用量接口的数据。", "Affiche uniquement les données de l'API d'usage officielle.", "Показывает только данные официального API использования.", "公式の使用量 API のデータのみを表示します。");
            Add("login.desc2", "You sign in in your own browser — no password passes through this app.", "登录在你自己的浏览器完成，不经手密码。", "Vous vous connectez dans votre navigateur ; aucun mot de passe ne transite par l'app.", "Вход выполняется в вашем браузере; пароль не проходит через приложение.", "ログインはご自身のブラウザで行い、パスワードはこのアプリを通りません。");
            Add("login.button", "Sign in to Claude", "登录 Claude", "Se connecter à Claude", "Войти в Claude", "Claude にログイン");
            Add("panel.fetching", "Fetching official usage…", "正在获取官方用量…", "Récupération de l'usage officiel…", "Получение данных об использовании…", "公式の使用量を取得中…");
            Add("chart.loginfirst", "Sign in to view", "登录后显示", "Connectez-vous pour afficher", "Войдите, чтобы увидеть", "ログインすると表示されます");
            Add("chart.waiting", "Fetching usage…", "正在获取用量…", "Récupération de l'usage…", "Получение данных…", "使用量を取得中…");

            Add("win5.title", "5-hour window", "5 小时窗口", "Fenêtre de 5 h", "5-часовое окно", "5 時間ウィンドウ");
            Add("reset.in", "resets in {0}", "{0}后重置", "réinit. dans {0}", "сброс через {0}", "{0}後にリセット");
            Add("reset.done", "reset", "已重置", "réinitialisé", "сброшено", "リセット済み");
            Add("reset.at", "resets {0}", "重置 {0}", "réinit. {0}", "сброс {0}", "リセット {0}");

            Add("pill.now", "official · just now", "官方 · 刚刚", "officiel · à l'instant", "офиц. · только что", "公式 · たった今");
            Add("pill.ago", "official · {0} ago", "官方 · {0}前", "officiel · il y a {0}", "офиц. · {0} назад", "公式 · {0}前");
            Add("pill.stale", "official · {0} ago (refreshing)", "官方 · {0}前（刷新中）", "officiel · il y a {0} (actualisation)", "офиц. · {0} назад (обновление)", "公式 · {0}前（更新中）");

            Add("chart.title", "Burn-up — this window", "本窗口燃起图", "Burn-up — cette fenêtre", "Burn-up — это окно", "バーンアップ（この期間）");
            Add("legend.actual", "actual", "实际", "réel", "факт", "実績");
            Add("legend.pace", "pace", "匀速", "rythme", "темп", "ペース");
            Add("legend.ceiling", "limit 100%", "上限 100%", "limite 100 %", "предел 100%", "上限 100%");
            Add("chart.samples", "{0} data points", "{0} 个采样点", "{0} points", "{0} точек", "{0} 個のサンプル");
            Add("chart.now", "now", "现在", "maintenant", "сейчас", "現在");

            Add("win7.title", "7-day window", "7 天窗口", "Fenêtre de 7 j", "7-дневное окно", "7 日間ウィンドウ");
            Add("models.title", "7-day · by model", "7 天 · 分模型", "7 j · par modèle", "7 дней · по модели", "7 日間 · モデル別");
            Add("models.none", "7-day by model: not in this reading", "7 天分模型：本次读数未返回", "7 j par modèle : absent de cette lecture", "7 дней по модели: нет в этом ответе", "7 日間モデル別：今回は返されませんでした");

            Add("footer.read", "official API · read {0}", "官方接口 · {0} 读取", "API officielle · lu {0}", "офиц. API · считано {0}", "公式 API · {0} 取得");
            Add("footer.history", "{0} local records", "{0} 条本地历史", "{0} enreg. locaux", "{0} локальных записей", "ローカル履歴 {0} 件");

            // ---- tray / menu ----
            Add("menu.panel", "Show panel", "显示面板", "Afficher le panneau", "Показать панель", "パネルを表示");
            Add("menu.refresh", "Refresh now", "立即刷新", "Actualiser", "Обновить", "今すぐ更新");
            Add("menu.login", "Sign in to Claude…", "登录 Claude…", "Se connecter à Claude…", "Войти в Claude…", "Claude にログイン…");
            Add("menu.relogin", "Sign in again…", "重新登录 Claude…", "Se reconnecter…", "Войти заново…", "再ログイン…");
            Add("menu.logout", "Sign out", "退出登录", "Se déconnecter", "Выйти", "ログアウト");
            Add("menu.settings", "Settings…", "设置…", "Paramètres…", "Настройки…", "設定…");
            Add("menu.datadir", "Open data folder", "打开数据目录", "Ouvrir le dossier de données", "Открыть папку данных", "データフォルダーを開く");
            Add("menu.about", "About", "关于", "À propos", "О программе", "情報");
            Add("menu.quit", "Quit", "退出", "Quitter", "Выход", "終了");

            Add("tray.notloggedin", "Claudometer · not signed in", "Claudometer · 未登录", "Claudometer · non connecté", "Claudometer · не выполнен вход", "Claudometer · 未ログイン");
            Add("tray.fetching", "Claudometer · fetching", "Claudometer · 获取中", "Claudometer · récupération", "Claudometer · получение", "Claudometer · 取得中");

            Add("balloon.signedin.title", "Signed in to Claude", "已登录 Claude", "Connecté à Claude", "Вход в Claude выполнен", "Claude にログインしました");
            Add("balloon.signedin.body", "Now reading official usage.", "开始读取官方用量。", "Lecture de l'usage officiel.", "Читаю официальные данные.", "公式の使用量を取得します。");
            Add("balloon.signedout.title", "Signed out", "已退出登录", "Déconnecté", "Выход выполнен", "ログアウトしました");
            Add("balloon.signedout.body", "Sign in to show usage.", "登录后才能显示用量。", "Connectez-vous pour afficher l'usage.", "Войдите, чтобы видеть использование.", "ログインすると使用量を表示します。");
            Add("balloon.full.title", "5-hour limit reached", "5 小时额度已用满", "Limite de 5 h atteinte", "5-часовой лимит исчерпан", "5 時間の上限に達しました");
            Add("balloon.reset.body", "Resets in {0}.", "{0}后重置。", "Réinit. dans {0}.", "Сброс через {0}.", "{0}後にリセットされます。");
            Add("balloon.used.title", "5-hour window at {0}%", "5 小时窗口已用 {0}%", "Fenêtre de 5 h à {0} %", "5-часовое окно: {0}%", "5 時間ウィンドウ {0}%");

            Add("status.ratelimited", "Rate-limited, retrying in {0} min", "接口限流，{0} 分钟后重试", "Limité, nouvel essai dans {0} min", "Лимит запросов, повтор через {0} мин", "レート制限中、{0} 分後に再試行");
            Add("status.unauth", "Session expired — sign in again", "登录已失效，请重新登录", "Session expirée — reconnectez-vous", "Сессия истекла — войдите заново", "セッション切れ — 再ログインしてください");
            Add("status.error", "Connection failed, retrying", "接口连接失败，重试中", "Échec de connexion, nouvel essai", "Ошибка соединения, повтор", "接続に失敗、再試行中");
            Add("status.tokenexpired", "Session expired — sign in again", "登录已过期，请重新登录", "Session expirée — reconnectez-vous", "Сессия истекла — войдите заново", "セッション切れ — 再ログインしてください");

            Add("instance.running", "Claudometer is already running (see the notification area).", "Claudometer 已经在运行（见任务栏通知区域）。", "Claudometer est déjà en cours (voir la zone de notification).", "Claudometer уже запущен (см. область уведомлений).", "Claudometer は既に実行中です（通知領域を確認）。");

            Add("about.title", "About Claudometer", "关于 Claudometer", "À propos de Claudometer", "О Claudometer", "Claudometer について");
            Add("about.body",
                "Claudometer — your Claude usage limits.\n\nShows only what the official usage API (api.anthropic.com/api/oauth/usage) returns. You sign in in your own browser; the token is stored encrypted on this machine. Local storage keeps only the API's readings — nothing is inferred.\n\n{0} local records",
                "Claudometer — Claude 用量\n\n只显示官方用量接口（api.anthropic.com/api/oauth/usage）返回的数据。登录在你自己的浏览器完成，令牌加密存于本机。本地只保存接口返回的读数，不做任何推测。\n\n{0} 条本地历史读数",
                "Claudometer — vos limites d'usage Claude.\n\nAffiche uniquement ce que renvoie l'API officielle (api.anthropic.com/api/oauth/usage). Connexion dans votre navigateur ; le jeton est chiffré sur cette machine. Le stockage local ne garde que les lectures de l'API — rien n'est déduit.\n\n{0} enregistrements locaux",
                "Claudometer — ваши лимиты использования Claude.\n\nПоказывает только то, что возвращает официальный API (api.anthropic.com/api/oauth/usage). Вход в вашем браузере; токен хранится зашифрованным на этом компьютере. Локально сохраняются только ответы API — ничего не додумывается.\n\n{0} локальных записей",
                "Claudometer — Claude の使用量上限。\n\n公式 API（api.anthropic.com/api/oauth/usage）が返す値のみを表示します。ログインはご自身のブラウザで行い、トークンは本機に暗号化して保存します。ローカルには API の応答のみを保存し、推測はしません。\n\nローカル履歴 {0} 件");

            // ---- settings ----
            Add("settings.title", "Claudometer Settings", "Claudometer 设置", "Paramètres Claudometer", "Настройки Claudometer", "Claudometer 設定");
            Add("settings.note", "Shows only data from the official usage API — sign in to Claude first.\nLocal storage keeps only the API's readings; nothing is inferred.", "只显示官方用量接口的数据，需先登录 Claude。\n本地只保存接口返回的读数，不做任何推测。", "Affiche uniquement les données de l'API officielle — connectez-vous d'abord.\nLe stockage local ne garde que les lectures de l'API ; rien n'est déduit.", "Показывает только данные официального API — сначала войдите.\nЛокально хранятся только ответы API; ничего не додумывается.", "公式 API のデータのみを表示します。まず Claude にログインしてください。\nローカルには API の応答のみを保存し、推測はしません。");
            Add("settings.tz", "Display timezone", "显示时区", "Fuseau horaire", "Часовой пояс", "表示タイムゾーン");
            Add("settings.theme", "Theme", "主题", "Thème", "Тема", "テーマ");
            Add("settings.theme.light", "Light", "浅色", "Clair", "Светлая", "ライト");
            Add("settings.theme.dark", "Dark", "深色", "Sombre", "Тёмная", "ダーク");
            Add("settings.lang", "Language", "语言", "Langue", "Язык", "言語");
            Add("settings.warn", "Warn threshold (%)", "预警阈值 (%)", "Seuil d'alerte (%)", "Порог предупреждения (%)", "警告しきい値 (%)");
            Add("settings.danger", "Danger threshold (%)", "危险阈值 (%)", "Seuil critique (%)", "Критический порог (%)", "危険しきい値 (%)");
            Add("settings.poll", "Poll interval (s)", "轮询间隔 (秒)", "Intervalle de sondage (s)", "Интервал опроса (с)", "取得間隔 (秒)");
            Add("settings.notify", "Threshold notifications", "启用阈值通知", "Notifications de seuil", "Уведомления о порогах", "しきい値通知");
            Add("settings.autostart", "Start with Windows", "开机自动启动", "Démarrer avec Windows", "Запуск с Windows", "Windows 起動時に開始");
            Add("settings.save", "Save", "保存", "Enregistrer", "Сохранить", "保存");
            Add("settings.cancel", "Cancel", "取消", "Annuler", "Отмена", "キャンセル");
            Add("settings.saveerr", "Failed to save settings: {0}", "保存设置失败：{0}", "Échec de l'enregistrement : {0}", "Не удалось сохранить настройки: {0}", "設定の保存に失敗：{0}");
            Add("settings.autostarterr", "Failed to set autostart: {0}", "设置开机自启失败：{0}", "Échec du démarrage auto : {0}", "Не удалось настроить автозапуск: {0}", "自動起動の設定に失敗：{0}");

            // ---- login dialog ----
            Add("login.win.title", "Sign in to Claude", "登录 Claude", "Connexion à Claude", "Вход в Claude", "Claude にログイン");
            Add("login.win.note", "Sign in in your own browser — on Anthropic's own page. This app never sees your password; it only receives a usage-query token, stored encrypted on this machine (only your Windows user can decrypt it).", "用你自己的浏览器完成登录。登录在 Anthropic 官方页面进行，本程序看不到你的密码，只会拿到一个用量查询用的令牌，加密保存在本机（仅当前 Windows 用户可解）。", "Connectez-vous dans votre navigateur, sur la page officielle d'Anthropic. Cette app ne voit jamais votre mot de passe ; elle reçoit seulement un jeton de consultation d'usage, chiffré sur cette machine.", "Войдите в своём браузере — на официальной странице Anthropic. Приложение не видит ваш пароль; оно получает лишь токен для запроса использования, хранящийся зашифрованным на этом компьютере.", "ご自身のブラウザ（Anthropic 公式ページ）でログインします。本アプリはパスワードを見ることはなく、使用量照会用のトークンのみを受け取り、本機に暗号化して保存します。");
            Add("login.step1", "Open the authorization page and sign in / consent", "打开授权页并登录 / 同意", "Ouvrez la page d'autorisation et connectez-vous", "Откройте страницу авторизации и войдите", "認可ページを開いてログイン / 同意");
            Add("login.step2", "Paste the code the page gives you here", "把页面给出的 code 粘贴到这里", "Collez ici le code fourni par la page", "Вставьте сюда код со страницы", "ページに表示された code をここに貼り付け");
            Add("login.openbtn", "Open sign-in page in browser", "在浏览器中打开登录页", "Ouvrir la page de connexion", "Открыть страницу входа", "ブラウザでログインページを開く");
            Add("login.finish", "Finish sign-in", "完成登录", "Terminer", "Завершить вход", "ログイン完了");
            Add("login.finish.count", "Finish ({0})", "完成登录 ({0})", "Terminer ({0})", "Завершить ({0})", "ログイン完了 ({0})");
            Add("login.opened", "Browser opened. After signing in, the page shows a code — copy it and paste it in step 2.", "已打开浏览器。登录并同意后，页面会显示一段 code —— 复制它，粘贴到上面第 2 步。", "Navigateur ouvert. Après connexion, la page affiche un code — copiez-le dans l'étape 2.", "Браузер открыт. После входа страница покажет код — скопируйте его в шаг 2.", "ブラウザを開きました。ログイン後にページへ表示される code をコピーし、手順 2 に貼り付けてください。");
            Add("login.openfail", "Failed to open browser: {0}", "打开浏览器失败：{0}", "Échec de l'ouverture du navigateur : {0}", "Не удалось открыть браузер: {0}", "ブラウザを開けませんでした：{0}");
            Add("login.pastefirst", "Paste the code from the page first.", "请先粘贴页面给出的 code。", "Collez d'abord le code de la page.", "Сначала вставьте код со страницы.", "先にページの code を貼り付けてください。");
            Add("login.verifying", "Verifying and exchanging the token…", "正在校验并换取令牌…", "Vérification et échange du jeton…", "Проверка и обмен токена…", "トークンを検証・交換中…");
            Add("login.exchangefail", "Failed to exchange the token.", "换取令牌失败。", "Échec de l'échange du jeton.", "Не удалось обменять токен.", "トークンの交換に失敗しました。");
            Add("login.rejected", "Token rejected ({0}). Please try again.", "令牌被拒（{0}）。请重试登录。", "Jeton refusé ({0}). Réessayez.", "Токен отклонён ({0}). Повторите вход.", "トークンが拒否されました（{0}）。再試行してください。");
            Add("login.ready", "You can press Finish again (same code; if it expired, sign in again).", "可以再次点「完成登录」了（用同一个 code；若 code 已过期就重新登录）。", "Vous pouvez recommencer (même code ; s'il a expiré, reconnectez-vous).", "Можно нажать «Завершить» снова (тот же код; если истёк — войдите заново).", "もう一度「完了」を押せます（同じ code；期限切れなら再ログイン）。");

            // ---- oauth errors (shown in the login dialog) ----
            Add("oauth.ratelimit.retry", "Token service is rate-limited; try again in about {0}s (the code is still valid).", "令牌服务限流，约 {0} 秒后可再试（code 仍有效）。", "Service de jetons limité ; réessayez dans ~{0}s (le code reste valide).", "Сервис токенов ограничивает запросы; повторите через ~{0}с (код ещё действителен).", "トークンサービスがレート制限中です。約 {0} 秒後に再試行してください（code は有効）。");
            Add("oauth.ratelimit.wait", "Token service is rate-limited; wait a minute or two, then press Finish with the same code.", "令牌服务限流，请稍等一两分钟再用同一个 code 点一次「完成登录」。", "Service de jetons limité ; patientez une minute ou deux, puis recommencez avec le même code.", "Сервис токенов ограничивает запросы; подождите пару минут и повторите с тем же кодом.", "トークンサービスがレート制限中です。1〜2 分待ってから同じ code で再試行してください。");
            Add("oauth.rejected", "The token service rejected the request: {0}", "令牌服务拒绝了请求：{0}", "Le service de jetons a rejeté la requête : {0}", "Сервис токенов отклонил запрос: {0}", "トークンサービスがリクエストを拒否しました：{0}");
            Add("oauth.exchangefail", "Token exchange failed: {0}", "换取令牌失败：{0}", "Échec de l'échange du jeton : {0}", "Обмен токена не удался: {0}", "トークン交換に失敗：{0}");
            Add("oauth.noaccess", "No access_token in the response. {0}", "令牌响应里没有 access_token。{0}", "Pas d'access_token dans la réponse. {0}", "В ответе нет access_token. {0}", "応答に access_token がありません。{0}");
            Add("oauth.statemismatch", "The pasted code doesn't match this sign-in (state mismatch). Please start sign-in again.", "粘贴的 code 与本次登录不匹配（state 不一致），请重新发起登录。", "Le code collé ne correspond pas à cette connexion (état différent). Recommencez.", "Вставленный код не соответствует этому входу (несовпадение state). Начните вход заново.", "貼り付けた code が今回のログインと一致しません（state 不一致）。もう一度ログインしてください。");

            // ---- durations ----
            Add("dur.dh", "{0}d {1}h", "{0}天{1}小时", "{0}j {1}h", "{0}д {1}ч", "{0}日{1}時間");
            Add("dur.hm", "{0}h {1}m", "{0}小时{1}分", "{0}h {1}min", "{0}ч {1}м", "{0}時間{1}分");
            Add("dur.m", "{0}m", "{0}分钟", "{0}min", "{0}мин", "{0}分");
            Add("dur.s", "{0}s", "{0}秒", "{0}s", "{0}с", "{0}秒");
        }
    }
}
