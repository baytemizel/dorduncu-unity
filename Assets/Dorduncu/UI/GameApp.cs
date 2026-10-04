// Dördüncü app: bootstraps the UI (canvas, event system, fonts), the API session and every screen — home menu, table,
// results, daily limit, leave confirmation, leaderboard, tournaments, profile, shop, chat. Added automatically to whatever
// scene starts first (see Bootstrap), so the project runs without any scene setup.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dorduncu.Client;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dorduncu.UI
{
    public sealed class GameApp : MonoBehaviour
    {
        public static IPlatformBridge Bridge = new NoNativeBridge();
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");
        private static readonly (string label, int level)[] Levels = { ("Kolay", 1), ("Orta", 3), ("Zor", 4), ("Usta", 5) };

        private UnityScheduler _clock;
        private ApiClient _api;
        private IKeyValueStore _store;
        private RectTransform _canvas;
        private TableScreen _table;
        private TableModel _model;
        private string _lastMode;
        private int _level = 3, _hintsUsed;
        private Theme _theme = Themes.All[0];

        // account state (from GET /v1/me)
        private string _userId;
        private bool _premium;
        private List<string> _themesOwned = new List<string>();
        private int _tickets;

        // overlays
        private GameObject _home, _busy, _result, _limit, _confirm, _lb, _tours, _profile, _shop, _chat;
        private Text _busyText, _resultTitle, _homeStatus, _lbMe, _profileBody, _shopNote, _toast;
        private RectTransform _resultRows, _lbRows, _tourList, _shopList, _themeStrip;
        private InputField _nameField, _profileName, _chatField;
        private Button _btnOnline, _btnLb, _btnTours, _btnProfile;
        private readonly List<Button> _levelButtons = new List<Button>();
        private float _toastUntil;
        private string _lbBoard = "season";
        private int _reportSeat = -1, _reportReason;

        // ---------------------------------------------------------------- bootstrap
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<GameApp>() != null) return;
            var go = new GameObject("Dördüncü");
            DontDestroyOnLoad(go);
            go.AddComponent<GameApp>();
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Ui.LoadFonts();
            _clock = gameObject.AddComponent<UnityScheduler>();
            _store = new PlayerPrefsStore();
            _api = new ApiClient(ApiUrl(), new UnityHttp(this), _store);
            _level = PlayerPrefs.GetInt("dorduncu.level", 3);
            var themeId = PlayerPrefs.GetString("dorduncu.theme", "kahvehane");
            _theme = Themes.All.FirstOrDefault(t => t.Id == themeId) ?? Themes.All[0];
            BuildCanvas();
            _table = TableScreen.Create(_canvas);
            _table.SetTheme(_theme);
            _table.MenuRequested = ConfirmLeave;
            _table.ChatRequested = () => Show(_chat);
            _table.Toast = Toast;
            _table.HintsLeft = HintsLeft;
            _table.HintUsed = () => _hintsUsed++;
            BuildOverlays();
            Show(_home);
            SignIn();
        }

        /// <summary>API address: PlayerPrefs override ("dorduncu.apiUrl"), else Resources/Dorduncu/config.json.</summary>
        private static string ApiUrl()
        {
            var o = PlayerPrefs.GetString("dorduncu.apiUrl", "");
            if (!string.IsNullOrEmpty(o)) return o;
            var cfg = Resources.Load<TextAsset>("Dorduncu/config");
            if (cfg == null) return "";
            try { return J.Obj(Json.Parse(cfg.text)).S("apiUrl") ?? ""; } catch (FormatException) { return ""; }
        }

        private void BuildCanvas()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                DontDestroyOnLoad(es);
            }
            var go = new GameObject("Canvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            go.AddComponent<GraphicRaycaster>();
            _canvas = (RectTransform)go.transform;
        }

        private void Update()
        {
            if (_toast != null && _toast.transform.parent.gameObject.activeSelf && Time.time > _toastUntil) _toast.transform.parent.gameObject.SetActive(false);
            if (_busy.activeSelf && _model != null && _model.View != null) Hide(_busy);
            if (_model != null && _model.LastError != null)
            {
                var e = _model.LastError;
                _model.LastError = null;
                Toast(ErrorText(e.Code, e.Message));
            }
        }

        // ---------------------------------------------------------------- account
        private string DeviceId()
        {
            var id = _store.Get("dorduncu.deviceId");
            if (id == null) { id = "u-" + Guid.NewGuid().ToString("N"); _store.Set("dorduncu.deviceId", id); }
            return id;
        }

        private void SignIn()
        {
            if (!_api.Configured) { UpdateHome(); return; }
            Action loadMe = () => _api.Me(r => { if (r.Ok) ApplyMe(r.Data); UpdateHome(); });
            if (_api.SignedIn) { loadMe(); return; }
            _api.Guest(DeviceId(), PlayerName(), r =>
            {
                if (!r.Ok) { _homeStatus.text = r.Status == 0 ? "Sunucuya ulaşılamadı — botlarla oynayabilirsin." : ErrorText(r.ErrorCode, r.ErrorMessage); UpdateHome(); return; }
                loadMe();
            });
        }

        private void ApplyMe(Dictionary<string, object> d)
        {
            var u = d.O("user");
            _userId = u.S("id");
            _premium = d.B("premium");
            var ent = d.O("entitlements");
            _themesOwned = ent.A("themes").Select(x => x as string).ToList();
            _tickets = ent.I("tickets");
            var today = d.O("today");
            _homeStatus.text = _premium ? "★ Premium — sınırsız oyun" : $"Bugün {today.I("used")}/{today.I("limit")} çevrimiçi oyun";
            if (string.IsNullOrEmpty(_nameField.text)) _nameField.text = u.S("displayName");
            BuildThemeStrip();
        }

        private string PlayerName()
        {
            var n = (_nameField != null ? _nameField.text : PlayerPrefs.GetString("dorduncu.name", "")).Trim();
            return n.Length == 0 ? "Misafir" : n;
        }

        private bool Online => _api.Configured && _api.SignedIn;
        private int HintsLeft() => _model == null || _model.Table.Mode == "offline" || _premium ? int.MaxValue : Math.Max(0, 3 - _hintsUsed);

        // ---------------------------------------------------------------- play
        private void PlayOffline()
        {
            SaveName();
            _lastMode = "offline";
            var t = new OfflineTable(new OfflineOptions { Name = PlayerName(), Avatar = 2, BotLevel = _level }, _clock);
            StartTable(t);
            t.Start();
        }

        private void PlayOnline()
        {
            if (!Online) { Toast("Çevrimiçi oyun için sunucu bağlantısı gerekli."); return; }
            SaveName();
            _lastMode = "online";
            _busyText.text = "Masa aranıyor…";
            Show(_busy);
            _api.UpdateProfile(new Dictionary<string, object> { ["displayName"] = PlayerName() }, _ =>
                _api.Play(r =>
                {
                    if (r.Status == 402) { Hide(_busy); Show(_limit); return; }
                    if (!r.Ok) { Hide(_busy); Toast(ErrorText(r.ErrorCode, r.ErrorMessage)); return; }
                    JoinWithTicket(r.Data.S("ticket"));
                }));
        }

        private void JoinWithTicket(string ticket)
        {
            var t = new OnlineTable(_api, _clock);
            StartTable(t);
            t.Join(ticket, OfflineTable.RandomSeed().Substring(0, 32), r =>
            {
                if (!r.Ok) { Hide(_busy); Toast(ErrorText(r.ErrorCode, r.ErrorMessage)); BackHome(); }
                else _busyText.text = "Oyuncular bekleniyor…";
            });
        }

        private void StartTable(ITableAdapter t)
        {
            _model?.Dispose();
            _hintsUsed = 0;
            _model = new TableModel(t);
            _model.Changed += () => { if (_model?.Result != null && !_result.activeSelf) ShowResult(_model.Result); };
            _model.ReportAcknowledged += s => Toast("Şikâyetin alındı. Teşekkürler.");
            t.Closed += reason => { if (reason == "abandoned" || reason == "lost") { Toast("Masa kapandı."); BackHome(); } };
            _table.Bind(_model);
            Hide(_home);
            if (t.Mode == "offline") Hide(_busy);
            RebuildChatTargets();
        }

        private void ConfirmLeave()
        {
            if (_model == null || _model.Result != null || _model.Table.Mode == "offline") { LeaveTable(); return; }
            Show(_confirm);
        }

        private void LeaveTable()
        {
            Hide(_confirm);
            _model?.Table.Leave();
            BackHome();
        }

        private void BackHome()
        {
            Hide(_result); Hide(_busy); Hide(_chat);
            Show(_home);
            if (Online) _api.Me(r => { if (r.Ok) ApplyMe(r.Data); });
        }

        // ---------------------------------------------------------------- overlays
        private static void Show(GameObject o) { o.SetActive(true); o.transform.SetAsLastSibling(); }
        private static void Hide(GameObject o) => o.SetActive(false);

        private void BuildOverlays()
        {
            BuildHome();
            var busy = Ui.Overlay(_canvas, "Busy", 600, 220, out _busy);
            _busyText = Ui.Txt(busy, "Masa aranıyor…", 40, Pal.Cream);
            _busyText.rectTransform.Place(0, 20, 560, 60);
            var cancel = Ui.Btn(busy, "Vazgeç", Ui.Style.Wood, LeaveTable, 26);
            ((RectTransform)cancel.transform).Place(0, -60, 220, 60);
            Hide(_busy);

            var res = Ui.Overlay(_canvas, "Result", 900, 720, out _result);
            _resultTitle = Ui.Title(res, "", 290, 60);
            _resultRows = Ui.Node("Rows", res).Place(0, 40, 800, 380);
            Place(Ui.Btn(res, "Tekrar Oyna", Ui.Style.Primary, () => { Hide(_result); if (_lastMode == "online") PlayOnline(); else PlayOffline(); }), 0, -220, 720, 80);
            Place(Ui.Btn(res, "Ana Menü", Ui.Style.Wood, () => { _model?.Table.Leave(); BackHome(); }), 0, -310, 720, 70);
            Hide(_result);

            var lim = Ui.Overlay(_canvas, "Limit", 900, 560, out _limit);
            Ui.Title(lim, "Bugünkü ücretsiz oyunların bitti", 200, 46);
            var lt = Ui.Txt(lim, "Premium ile sınırsız oynayabilir ya da reklam izleyerek ek oyun kazanabilirsin. Botlarla oynamak her zaman ücretsiz.", 30, Pal.Cream, TextAnchor.MiddleCenter, false);
            lt.rectTransform.Place(0, 60, 780, 160);
            Place(Ui.Btn(lim, "Reklam İzle · +1 Oyun", Ui.Style.Primary, WatchAd), 0, -90, 720, 80);
            Place(Ui.Btn(lim, "Tamam", Ui.Style.Wood, () => { Hide(_limit); BackHome(); }), 0, -190, 720, 70);
            Hide(_limit);

            var conf = Ui.Overlay(_canvas, "Confirm", 860, 480, out _confirm);
            Ui.Title(conf, "Masadan kalkılsın mı?", 160, 52);
            var ct = Ui.Txt(conf, "Çevrimiçi maçı bırakırsan yerine bot oynar ve lig puanı kaybedersin.", 30, Pal.Cream, TextAnchor.MiddleCenter, false);
            ct.rectTransform.Place(0, 50, 760, 110);
            Place(Ui.Btn(conf, "Oyuna Dön", Ui.Style.Primary, () => Hide(_confirm)), 0, -80, 700, 80);
            Place(Ui.Btn(conf, "Masadan Kalk", Ui.Style.Danger, LeaveTable), 0, -175, 700, 70);
            Hide(_confirm);

            BuildLeaderboard();
            BuildTournaments();
            BuildProfile();
            BuildShop();
            BuildChat();

            var toast = Ui.Frame(_canvas, "Toast", new Color(0.1f, 0.05f, 0.02f, 0.92f), Pal.Gold, 3).Place(0, 300, 1100, 76);
            _toast = Ui.Txt(toast, "", 30, Pal.Cream);
            _toast.rectTransform.Stretch(10);
            toast.gameObject.SetActive(false);
        }

        private static void Place(Button b, float x, float y, float w, float h) => ((RectTransform)b.transform).Place(x, y, w, h);

        public void Toast(string text)
        {
            _toast.text = text;
            var box = _toast.transform.parent.gameObject;
            box.SetActive(true);
            box.transform.SetAsLastSibling();
            _toastUntil = Time.time + 3.2f;
        }

        private void SaveName()
        {
            PlayerPrefs.SetString("dorduncu.name", _nameField.text.Trim());
            PlayerPrefs.Save();
        }

        // ---------------------------------------------------------------- home
        private void BuildHome()
        {
            var card = Ui.Overlay(_canvas, "Home", 700, 860, out _home);
            card.anchoredPosition = new Vector2(0, 40);
            _home.GetComponent<Image>().color = new Color(0, 0, 0, 0.45f);
            Ui.Title(card, "Dördüncü", 340, 96);
            var sub = Ui.Txt(card, "Okey · Türkiye'nin masası", 28, Pal.Cream, TextAnchor.MiddleCenter, false);
            sub.rectTransform.Place(0, 268, 600, 40);
            var lbl = Ui.Txt(card, "Adın", 26, Pal.Cream, TextAnchor.MiddleLeft, false);
            lbl.rectTransform.Place(0, 215, 600, 34);
            _nameField = Ui.Field(card, "Misafir", 24);
            ((RectTransform)_nameField.transform).Place(0, 165, 600, 66);
            _nameField.text = PlayerPrefs.GetString("dorduncu.name", "");
            _btnOnline = Ui.Btn(card, "Hemen Oyna", Ui.Style.Primary, PlayOnline, 36);
            Place(_btnOnline, 0, 75, 600, 84);
            var lv = Ui.Txt(card, "Botlara karşı:", 26, Pal.Cream, TextAnchor.MiddleLeft, false);
            lv.rectTransform.Place(-215, -10, 170, 50);
            for (int i = 0; i < Levels.Length; i++)
            {
                int k = i;
                var b = Ui.Btn(card, Levels[i].label, Ui.Style.Wood, () => { _level = Levels[k].level; PlayerPrefs.SetInt("dorduncu.level", _level); UpdateHome(); }, 26);
                Place(b, -50 + i * 110, -10, 100, 56);
                _levelButtons.Add(b);
            }
            Place(Ui.Btn(card, "Botlarla Oyna", Ui.Style.Wood, PlayOffline, 34), 0, -95, 600, 80);
            _btnLb = Ui.Btn(card, "Sıralama", Ui.Style.Wood, () => { Show(_lb); LoadBoard(_lbBoard); }, 26);
            _btnTours = Ui.Btn(card, "Turnuvalar", Ui.Style.Wood, () => { Show(_tours); LoadTours(); }, 26);
            _btnProfile = Ui.Btn(card, "Profil", Ui.Style.Wood, () => { Show(_profile); LoadProfile(); }, 26);
            Place(_btnLb, -205, -180, 190, 62);
            Place(_btnTours, 0, -180, 190, 62);
            Place(_btnProfile, 205, -180, 190, 62);
            Place(Ui.Btn(card, "Mağaza · Premium", Ui.Style.Gold, () => { Show(_shop); BuildShopList(); }, 32), 0, -265, 600, 78);
            _homeStatus = Ui.Txt(card, "", 24, Pal.Gold, TextAnchor.MiddleCenter, false);
            _homeStatus.rectTransform.Place(0, -335, 620, 40);
            var fine = Ui.Txt(card, "Temayı alttaki şeritten seçebilirsin.", 20, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleCenter, false);
            fine.rectTransform.Place(0, -380, 620, 30);
            _themeStrip = Ui.Node("Themes", _home.transform).Place(0, -490, 1880, 96);
            BuildThemeStrip();
        }

        private void UpdateHome()
        {
            for (int i = 0; i < _levelButtons.Count; i++)
            {
                var img = _levelButtons[i].transform.Find("Fill").GetComponent<Image>();
                img.color = Levels[i].level == _level ? Pal.Gold : Pal.WoodLight;
                _levelButtons[i].transform.Find("Label").GetComponent<Text>().color = Levels[i].level == _level ? Pal.WoodDark : Pal.Cream;
            }
            bool online = Online;
            _btnOnline.SetEnabled(online);
            _btnLb.SetEnabled(online);
            _btnTours.SetEnabled(online);
            _btnProfile.SetEnabled(online);
            if (!_api.Configured) _homeStatus.text = "Çevrimdışı sürüm — botlara karşı oyna.";
        }

        private bool ThemeOwned(Theme t) => t.Free || _premium || _themesOwned.Contains(t.Id);

        private void BuildThemeStrip()
        {
            foreach (Transform c in _themeStrip) Destroy(c.gameObject);
            for (int i = 0; i < Themes.All.Length; i++)
            {
                var th = Themes.All[i];
                bool owned = ThemeOwned(th);
                var b = Ui.Btn(_themeStrip, th.Name + (owned ? "" : "\n★ PREMIUM"), th == _theme ? Ui.Style.Gold : Ui.Style.Wood, () =>
                {
                    if (!ThemeOwned(th)) { Show(_shop); BuildShopList(); return; }
                    _theme = th;
                    PlayerPrefs.SetString("dorduncu.theme", th.Id);
                    _table.SetTheme(th);
                    BuildThemeStrip();
                }, 24);
                Place(b, -775 + i * 310, 0, 290, 90);
            }
        }

        // ---------------------------------------------------------------- result
        private void ShowResult(MatchResult r)
        {
            int me = _model.MySeat;
            bool won = r.Ranking.Length > 0 && r.Ranking[0] == me;
            _resultTitle.text = won ? "Kazandın!" : "Maç bitti";
            foreach (Transform c in _resultRows) Destroy(c.gameObject);
            for (int i = 0; i < r.Ranking.Length; i++)
            {
                int seat = r.Ranking[i];
                var s = r.Seats.FirstOrDefault(x => x.Seat == seat);
                var row = Ui.Frame(_resultRows, "Row", seat == me ? new Color(0.35f, 0.24f, 0.08f) : Pal.WoodDark, seat == me ? Pal.Gold : Pal.GoldDark, 2).Place(0, 135 - i * 90, 780, 76);
                var place = Ui.Txt(row, (i + 1) + ".", 34, Pal.Gold);
                place.rectTransform.Place(-340, 0, 60, 60);
                var name = Ui.Txt(row, (s?.Name ?? "Oyuncu") + (s != null && s.Bot ? " (bot)" : ""), 30, Pal.Cream, TextAnchor.MiddleLeft);
                name.rectTransform.Place(-60, 0, 440, 60);
                var pts = Ui.Txt(row, r.Points[seat] + " puan", 28, Pal.Cream);
                pts.rectTransform.Place(220, 0, 150, 60);
                if (_model.Table.Mode == "online" && r.LpDelta != null && seat < r.LpDelta.Length && !(s?.Bot ?? true))
                {
                    int d = r.LpDelta[seat];
                    var lp = Ui.Txt(row, (d >= 0 ? "+" : "") + d + " LP", 28, d >= 0 ? Pal.Green : Pal.Red);
                    lp.rectTransform.Place(330, 0, 110, 60);
                }
            }
            Show(_result);
        }

        private void WatchAd()
        {
            if (!Bridge.AdsAvailable || _userId == null) { Toast("Reklamlar bu platformda kullanılamıyor."); return; }
            Bridge.ShowRewardedAd(_userId, ok =>
            {
                if (!ok) { Toast("Reklam tamamlanmadı."); return; }
                Hide(_limit);
                Toast("Ek oyun hakkın tanımlanıyor…");
                _clock.After(2500, PlayOnline);   // the server grants the bonus game through the ad network's SSV callback
            });
        }

        // ---------------------------------------------------------------- leaderboard
        private void BuildLeaderboard()
        {
            var card = Ui.Overlay(_canvas, "Leaderboard", 1100, 900, out _lb);
            Ui.Title(card, "Sıralama", 380);
            var tabs = new[] { ("season", "Sezon"), ("weekly", "Bu Hafta"), ("alltime", "Tüm Zamanlar") };
            for (int i = 0; i < tabs.Length; i++)
            {
                var (id, label) = tabs[i];
                Place(Ui.Btn(card, label, Ui.Style.Wood, () => LoadBoard(id), 26), -330 + i * 330, 290, 300, 62);
            }
            _lbRows = Ui.Node("Rows", card).Place(0, -20, 1000, 560);
            _lbMe = Ui.Txt(card, "", 28, Pal.Gold);
            _lbMe.rectTransform.Place(0, -335, 1000, 44);
            Place(Ui.Btn(card, "Geri", Ui.Style.Wood, () => Hide(_lb)), 0, -400, 500, 70);
            Hide(_lb);
        }

        private void LoadBoard(string board)
        {
            _lbBoard = board;
            foreach (Transform c in _lbRows) Destroy(c.gameObject);
            _lbMe.text = "Yükleniyor…";
            _api.Leaderboard(board, 10, 0, r =>
            {
                if (!r.Ok) { _lbMe.text = ErrorText(r.ErrorCode, r.ErrorMessage); return; }
                var rows = r.Data.A("rows");
                for (int i = 0; i < rows.Count; i++)
                {
                    var x = J.Obj(rows[i]);
                    var t = Ui.Txt(_lbRows, $"{x.I("rank")}.   {x.S("name")}", 30, x.S("userId") == _userId ? Pal.Gold : Pal.Cream, TextAnchor.MiddleLeft);
                    t.rectTransform.Place(-120, 250 - i * 55, 700, 50);
                    var sc = Ui.Txt(_lbRows, x.I("score") + " LP", 30, Pal.Cream, TextAnchor.MiddleRight);
                    sc.rectTransform.Place(360, 250 - i * 55, 220, 50);
                }
                if (rows.Count == 0) { var t = Ui.Txt(_lbRows, "Henüz kimse yok — ilk sen ol!", 30, Pal.Cream); t.rectTransform.Place(0, 0, 900, 60); }
                var me = r.Data.O("me");
                _lbMe.text = me == null ? "Sıralamaya girmek için bir çevrimiçi maç bitir." : $"Senin sıran: {me.I("rank")}  ·  {me.I("score")} LP";
            });
        }

        // ---------------------------------------------------------------- tournaments
        private void BuildTournaments()
        {
            var card = Ui.Overlay(_canvas, "Tournaments", 1200, 900, out _tours);
            Ui.Title(card, "Turnuvalar", 380);
            var fine = Ui.Txt(card, "Ödüller yalnızca oyun içidir: bilet, tema ve lig puanı.", 24, Pal.Cream, TextAnchor.MiddleCenter, false);
            fine.rectTransform.Place(0, 315, 1000, 36);
            _tourList = Ui.Node("List", card).Place(0, -20, 1100, 600);
            Place(Ui.Btn(card, "Geri", Ui.Style.Wood, () => Hide(_tours)), 0, -400, 500, 70);
            Hide(_tours);
        }

        private void LoadTours()
        {
            foreach (Transform c in _tourList) Destroy(c.gameObject);
            _api.Tournaments(r =>
            {
                if (!r.Ok) { Toast(ErrorText(r.ErrorCode, r.ErrorMessage)); return; }
                var list = r.Data.A("tournaments");
                if (list.Count == 0) { var t = Ui.Txt(_tourList, "Şu an açık turnuva yok.", 30, Pal.Cream); t.rectTransform.Place(0, 0, 900, 60); }
                for (int i = 0; i < list.Count && i < 5; i++)
                {
                    var x = J.Obj(list[i]);
                    string id = x.S("id"), status = x.S("status");
                    bool reg = x.B("registered");
                    var row = Ui.Frame(_tourList, "Tournament", Pal.WoodDark, Pal.GoldDark, 2).Place(0, 230 - i * 118, 1080, 104);
                    var when = DateTime.TryParse(x.S("startsAt"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt) ? dt.ToLocalTime().ToString("d MMM HH:mm", Tr) : "";
                    var name = Ui.Txt(row, x.S("name"), 32, Pal.Gold, TextAnchor.MiddleLeft);
                    name.rectTransform.Place(-230, 18, 560, 44);
                    var info = Ui.Txt(row, $"{when}  ·  {x.I("entries")}/{x.I("capacity")} oyuncu  ·  giriş {x.I("entryTickets")} bilet  ·  {(status == "open" ? "kayıt açık" : "oynanıyor")}", 22, Pal.Cream, TextAnchor.MiddleLeft, false);
                    info.rectTransform.Place(-230, -22, 560, 34);
                    if (status == "open")
                        Place(Ui.Btn(row, reg ? "Kaydı Sil" : "Kayıt Ol", reg ? Ui.Style.Wood : Ui.Style.Primary, () =>
                        {
                            Action<ApiResult> after = rr => { if (!rr.Ok) Toast(ErrorText(rr.ErrorCode, rr.ErrorMessage)); else Toast(reg ? "Kaydın silindi, biletin iade edildi." : "Kayıt tamam!"); LoadTours(); };
                            if (reg) _api.UnregisterTournament(id, after); else _api.RegisterTournament(id, after);
                        }, 26), 400, 0, 220, 64);
                    else if (reg)
                        Place(Ui.Btn(row, "Masana Otur", Ui.Style.Primary, () => _api.TournamentSeat(id, rr =>
                        {
                            if (!rr.Ok) { Toast(ErrorText(rr.ErrorCode, rr.ErrorMessage)); return; }
                            Hide(_tours);
                            _lastMode = "tournament";
                            _busyText.text = "Turnuva masası açılıyor…";
                            Show(_busy);
                            JoinWithTicket(rr.Data.S("ticket"));
                        }), 26), 400, 0, 220, 64);
                }
                var t2 = Ui.Txt(_tourList, $"Biletlerin: {_tickets}", 26, Pal.Gold);
                t2.rectTransform.Place(0, -290, 600, 40);
            });
        }

        // ---------------------------------------------------------------- profile
        private void BuildProfile()
        {
            var card = Ui.Overlay(_canvas, "Profile", 900, 900, out _profile);
            Ui.Title(card, "Profil", 380);
            _profileName = Ui.Field(card, "Görünen ad", 24);
            ((RectTransform)_profileName.transform).Place(-90, 290, 560, 66);
            Place(Ui.Btn(card, "Kaydet", Ui.Style.Primary, () => _api.UpdateProfile(new Dictionary<string, object> { ["displayName"] = _profileName.text.Trim() }, r =>
            {
                if (r.Ok) { _nameField.text = _profileName.text.Trim(); SaveName(); Toast("Kaydedildi."); LoadProfile(); }
                else Toast(ErrorText(r.ErrorCode, r.ErrorMessage));
            }), 26), 290, 290, 200, 66);
            _profileBody = Ui.Txt(card, "", 28, Pal.Cream, TextAnchor.UpperCenter, false);
            _profileBody.rectTransform.Place(0, 130, 800, 200);
            Place(Ui.Btn(card, "Google ile bağla", Ui.Style.Wood, () => LinkProvider("google"), 24), -200, -40, 380, 64);
            Place(Ui.Btn(card, "Apple ile bağla", Ui.Style.Wood, () => LinkProvider("apple"), 24), 200, -40, 380, 64);
            var fine = Ui.Txt(card, "Hesabını bağlarsan telefon değiştirdiğinde ilerlemen kaybolmaz.", 22, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleCenter, false);
            fine.rectTransform.Place(0, -100, 800, 36);
            Place(Ui.Btn(card, "Çıkış Yap", Ui.Style.Wood, () => _api.Logout(_ => { Hide(_profile); _userId = null; SignIn(); }), 26), 0, -170, 600, 64);
            Button del = null;
            bool armed = false;
            del = Ui.Btn(card, "Hesabımı Sil", Ui.Style.Danger, () =>
            {
                if (!armed) { armed = true; del.SetLabel("Emin misin? Tekrar bas"); return; }
                armed = false;
                del.SetLabel("Hesabımı Sil");
                _api.DeleteAccount(r => { Toast(r.Ok ? "Hesabın silindi (KVKK)." : ErrorText(r.ErrorCode, r.ErrorMessage)); Hide(_profile); _store.Set("dorduncu.deviceId", null); SignIn(); });
            }, 26);
            Place(del, 0, -250, 600, 64);
            Place(Ui.Btn(card, "Geri", Ui.Style.Wood, () => { armed = false; del.SetLabel("Hesabımı Sil"); Hide(_profile); }), 0, -380, 500, 70);
            Hide(_profile);
        }

        private void LoadProfile()
        {
            _profileBody.text = "Yükleniyor…";
            _api.Me(r =>
            {
                if (!r.Ok) { _profileBody.text = ErrorText(r.ErrorCode, r.ErrorMessage); return; }
                ApplyMe(r.Data);
                var u = r.Data.O("user");
                _profileName.text = u.S("displayName");
                _profileBody.text = $"Sezon LP: {u.I("lp")}   ·   Tüm zamanlar: {u.I("lpAllTime")}\nOyun: {u.I("gamesPlayed")}   ·   Galibiyet: {u.I("gamesWon")}\n" +
                                    $"Bilet: {_tickets}   ·   {(_premium ? "★ Premium" : "Ücretsiz üyelik")}";
            });
        }

        private void LinkProvider(string provider)
        {
            if (!Bridge.SignInAvailable(provider)) { Toast("Bu platformda " + provider + " girişi bağlı değil."); return; }
            Bridge.SignIn(provider, token => _api.Link(provider, token, r => Toast(r.Ok ? "Hesabın bağlandı." : ErrorText(r.ErrorCode, r.ErrorMessage))), Toast);
        }

        // ---------------------------------------------------------------- shop
        private static readonly (string id, string name)[] Catalog =
        {
            ("premium.monthly", "Premium · 1 ay (sınırsız oyun, tüm temalar)"), ("premium.yearly", "Premium · 1 yıl"),
            ("theme.ev", "Ev Akşamı teması"), ("theme.cinar", "Çınar Altı teması"), ("theme.bogaz", "Boğaz Terası teması"),
            ("theme.kis", "Kış Köşkü teması"), ("theme.ege", "Ege Kıyısı teması"), ("tickets.5", "5 turnuva bileti"), ("tickets.20", "20 turnuva bileti"),
        };

        private void BuildShop()
        {
            var card = Ui.Overlay(_canvas, "Shop", 1100, 960, out _shop);
            Ui.Title(card, "Mağaza", 410);
            _shopNote = Ui.Txt(card, "", 24, Pal.Cream, TextAnchor.MiddleCenter, false);
            _shopNote.rectTransform.Place(0, 345, 1000, 36);
            _shopList = Ui.Node("List", card).Place(0, -10, 1000, 640);
            Place(Ui.Btn(card, "Geri", Ui.Style.Wood, () => Hide(_shop)), 0, -420, 500, 70);
            Hide(_shop);
        }

        private void BuildShopList()
        {
            foreach (Transform c in _shopList) Destroy(c.gameObject);
            _shopNote.text = Bridge.StoreAvailable ? "Satın almalar mağazanın imzasıyla sunucuda doğrulanır." : "Bu platformda mağaza bağlı değil (Unity IAP köprüsünü ekleyin).";
            for (int i = 0; i < Catalog.Length; i++)
            {
                var (id, name) = Catalog[i];
                bool owned = id.StartsWith("theme.") && _themesOwned.Contains(id.Substring(6)) || id.StartsWith("premium") && _premium;
                var row = Ui.Frame(_shopList, "Product", Pal.WoodDark, Pal.GoldDark, 2).Place(0, 285 - i * 71, 980, 62);
                var t = Ui.Txt(row, name, 26, Pal.Cream, TextAnchor.MiddleLeft, false);
                t.rectTransform.Place(-140, 0, 660, 50);
                var b = Ui.Btn(row, owned ? "Sende" : "Satın Al", owned ? Ui.Style.Wood : Ui.Style.Gold, () => Buy(id), 22);
                Place(b, 380, 0, 190, 50);
                b.SetEnabled(!owned);
            }
        }

        private void Buy(string productId)
        {
            if (!Online) { Toast("Satın alma için sunucu bağlantısı gerekli."); return; }
            Bridge.Purchase(productId, body => _api.VerifyPurchase(body, r =>
            {
                if (!r.Ok) { Toast(ErrorText(r.ErrorCode, r.ErrorMessage)); return; }
                Toast("Satın alma tamam. Teşekkürler!");
                _api.Me(m => { if (m.Ok) ApplyMe(m.Data); BuildShopList(); });
            }), Toast);
        }

        // ---------------------------------------------------------------- chat
        private RectTransform _reportBox;

        private void BuildChat()
        {
            var card = Ui.Overlay(_canvas, "Chat", 1000, 760, out _chat);
            Ui.Title(card, "Sohbet", 320, 56);
            string[] quick = { "Selam!", "İyi oyunlar!", "Hadi ama!", "Güzel el!", "Çaylar benden!", "Şanslı günündesin." };
            for (int i = 0; i < quick.Length; i++)
            {
                string q = quick[i];
                Place(Ui.Btn(card, q, Ui.Style.Wood, () => { _model?.Say(q); Hide(_chat); }, 22), -310 + (i % 3) * 310, 220 - (i / 3) * 70, 290, 58);
            }
            for (int i = 0; i < Dorduncu.Client.Protocol.Emotes.Length; i++)
            {
                string e = Dorduncu.Client.Protocol.Emotes[i];
                Place(Ui.Btn(card, TableScreen.EmoteLabel(e), Ui.Style.Ghost, () => { _model?.SendEmote(e); Hide(_chat); }, 20), -360 + (i % 4) * 240, 70 - (i / 4) * 62, 225, 52);
            }
            _chatField = Ui.Field(card, "Mesaj yaz…", 200);
            ((RectTransform)_chatField.transform).Place(-110, -80, 700, 64);
            Place(Ui.Btn(card, "Gönder", Ui.Style.Primary, () =>
            {
                var text = _chatField.text.Trim();
                if (text.Length > 0) _model?.Say(text);
                _chatField.text = "";
                Hide(_chat);
            }, 26), 360, -80, 200, 64);
            _reportBox = Ui.Node("Report", card).Place(0, -200, 940, 120);
            Place(Ui.Btn(card, "Kapat", Ui.Style.Wood, () => Hide(_chat)), 0, -320, 400, 64);
            Hide(_chat);
        }

        private static readonly string[] ReasonLabels = { "Hakaret / küfür", "Hile", "Oynamıyor", "Uygunsuz isim", "Diğer" };

        private void RebuildChatTargets()
        {
            foreach (Transform c in _reportBox) Destroy(c.gameObject);
            _reportSeat = -1;
            Button seatBtn = null, reasonBtn = null;
            seatBtn = Ui.Btn(_reportBox, "Oyuncu seç", Ui.Style.Ghost, () =>
            {
                var humans = _model?.Seats.Where(s => !s.Bot && s.Seat != _model.MySeat).ToList() ?? new List<SeatInfo>();
                if (humans.Count == 0) { Toast("Şikâyet edilebilecek oyuncu yok."); return; }
                int idx = humans.FindIndex(s => s.Seat == _reportSeat);
                var next = humans[(idx + 1) % humans.Count];
                _reportSeat = next.Seat;
                seatBtn.SetLabel(next.Name);
            }, 22);
            Place(seatBtn, -320, 0, 280, 58);
            reasonBtn = Ui.Btn(_reportBox, ReasonLabels[_reportReason], Ui.Style.Ghost, () =>
            {
                _reportReason = (_reportReason + 1) % ReasonLabels.Length;
                reasonBtn.SetLabel(ReasonLabels[_reportReason]);
            }, 22);
            Place(reasonBtn, 0, 0, 300, 58);
            Place(Ui.Btn(_reportBox, "Şikâyet Et", Ui.Style.Danger, () =>
            {
                if (_reportSeat < 0) { Toast("Önce oyuncu seç."); return; }
                _model?.Report(_reportSeat, Dorduncu.Client.Protocol.ReportReasons[_reportReason]);
            }, 22), 320, 0, 260, 58);
        }

        // ---------------------------------------------------------------- texts
        public static string ErrorText(string code, string message)
        {
            switch (code)
            {
                case "network": return "Sunucuya ulaşılamadı.";
                case "not_your_turn": return "Sıra sende değil.";
                case "bad_phase": return "Şu an bu hamle yapılamaz.";
                case "not_winning": return "Bu el bitmiyor.";
                case "tile_not_in_hand": return "Bu taş elinde değil.";
                case "empty_source": return "Çekilecek taş yok.";
                case "cannot_show_indicator": return "Göstergeyi şu an gösteremezsin.";
                case "rate_limited": return "Biraz yavaş!";
                case "muted": return "Sohbetin geçici olarak kapalı.";
                case "daily_limit": return "Bugünkü ücretsiz oyunların bitti.";
                case "already_reported": return "Bu oyuncuyu zaten şikâyet ettin.";
                case "no_tickets": return "Yeterli biletin yok.";
                case "full": return "Turnuva dolu.";
                case "closed": return "Kayıtlar kapandı.";
                case "no_table": return "Şu an oynayacağın bir masa yok.";
                case "banned": return "Hesabın kapatıldı.";
                case "unauthorized": return "Oturum süresi doldu, tekrar giriş yapılıyor.";
                default: return string.IsNullOrEmpty(message) ? "Bir hata oluştu." : message;
            }
        }
    }
}
