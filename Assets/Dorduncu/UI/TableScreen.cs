// The okey table: theme background, four player plates with turn countdown, the player's ıstaka (2×15 slots, drag to arrange,
// drag to the right pile or double-click to throw), the wall and the indicator in the middle, four discard piles, the action
// bar (Desteden Çek, Yerden Al, Bitir, Gösterge, İpucu, Seri Diz, Çift Diz, Menü), chat bubbles and event toasts.
// Everything is rendered from the TableModel; online and offline tables look and behave identically.
using System;
using System.Collections.Generic;
using System.Linq;
using Dorduncu.Client;
using Dorduncu.Engine;
using UnityEngine;
using UnityEngine.UI;

namespace Dorduncu.UI
{
    public sealed class TableScreen : MonoBehaviour
    {
        // ---------------------------------------------------------------- layout (canvas pixels, origin = screen center)
        private static readonly Vector2 RackPos = new Vector2(-60, -190);
        private static readonly Vector2 RackSize = new Vector2(1400, 340);
        private const float TileW = 82, TileH = 108, SlotDx = 87.5f, SlotX0 = -612.5f;
        private static readonly float[] RowY = { 102, -70 };
        private static readonly Vector2[] PilePos = { new Vector2(470, 30), new Vector2(470, 215), new Vector2(-560, 215), new Vector2(-560, 30) };
        private static readonly Vector2[] PlatePos = { new Vector2(-560, -455), new Vector2(760, 380), new Vector2(0, 470), new Vector2(-760, 380) };
        private static readonly Vector2 WallPos = new Vector2(-110, 140), IndicatorPos = new Vector2(40, 140), PillPos = new Vector2(-35, 40);

        public TableModel Model { get; private set; }
        public Action MenuRequested, ChatRequested;
        public Func<int> HintsLeft;                 // ∞ = int.MaxValue
        public Action HintUsed;
        public Action<string> Toast;

        private RectTransform _root, _rack, _wall, _hud;
        private Image _bg, _tea;
        private Text _pill;
        private TileView _indicator;
        private readonly Dictionary<int, TileView> _rackTiles = new Dictionary<int, TileView>();
        private readonly TileView[] _pileTop = new TileView[4];
        private readonly Text[] _pileCount = new Text[4];
        private readonly Plate[] _plates = new Plate[4];
        private readonly Dictionary<string, Button> _btn = new Dictionary<string, Button>();
        private Image _okeyBanner;
        private float _bannerUntil;
        private long _turnStart, _turnDeadline;
        private int _turnSeat = -1;
        private bool _dirty;

        private int Pos(int seat) => (seat - Model.MySeat + 4) % 4;   // 0 me (bottom), 1 right, 2 top, 3 left
        private int SeatAt(int pos) => (pos + Model.MySeat) % 4;

        // ---------------------------------------------------------------- build
        public static TableScreen Create(Transform parent)
        {
            var root = Ui.Node("Table", parent).Stretch();
            var s = root.gameObject.AddComponent<TableScreen>();
            s._root = root;
            s.Build();
            return s;
        }

        private void Build()
        {
            _bg = Ui.Img(_root, "Background", null, Color.white);
            _bg.rectTransform.Stretch();
            var fitter = _bg.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 16f / 9f;

            // opponents' racks (backs) and the side racks
            var top = Ui.Art(_root, "Rack Top", "rackBack");
            top.rectTransform.Place(0, 330, 620, 143);
            var left = Ui.Art(_root, "Rack Left", "rackEnd");
            left.rectTransform.Place(-850, 140, 150, 260);
            var right = Ui.Art(_root, "Rack Right", "rackEnd");
            right.rectTransform.Place(850, 140, 150, 260);
            right.rectTransform.localScale = new Vector3(-1, 1, 1);
            _tea = Ui.Img(_root, "Tea", null, Color.white);
            _tea.preserveAspect = true;
            _tea.rectTransform.Place(830, -260, 230, 250);

            // wall (click to draw), indicator, info pill
            _wall = Ui.Node("Wall", _root).Place(WallPos.x, WallPos.y, TileW, TileH + 18);
            for (int i = 0; i < 3; i++)
            {
                var t = TileView.Create(_wall, TileW * 0.86f, TileH * 0.86f);
                t.Show(null);
                t.Rect.anchoredPosition = new Vector2(0, -12 + i * 9);
                t.Clicked = _ => { if (Model != null && Model.CanDraw) Model.Draw("wall"); };
            }
            _indicator = TileView.Create(_root, TileW * 0.86f, TileH * 0.86f);
            _indicator.Rect.anchoredPosition = IndicatorPos;
            _indicator.name = "Indicator";
            var pill = Ui.Frame(_root, "Pill", new Color(0.13f, 0.07f, 0.03f, 0.85f), Pal.GoldDark, 2);
            pill.Place(PillPos.x, PillPos.y, 420, 44);
            _pill = Ui.Txt(pill, "", 24, Pal.Cream);
            _pill.rectTransform.Stretch(4);

            // discard piles
            for (int p = 0; p < 4; p++)
            {
                int pos = p;
                var t = TileView.Create(_root, TileW * 0.86f, TileH * 0.86f);
                t.Rect.anchoredPosition = PilePos[p];
                t.Clicked = _ => { if (pos == 3 && Model != null && Model.CanDraw && Model.View.AvailableDiscard != null) Model.Draw("discard"); };
                _pileTop[p] = t;
                _pileCount[p] = Ui.Txt(_root, "", 20, Pal.Cream);
                _pileCount[p].rectTransform.Place(PilePos[p].x, PilePos[p].y - 62, 120, 26);
            }

            // my ıstaka
            var rackImg = Ui.Art(_root, "Rack", "rack", false);
            _rack = rackImg.rectTransform.Place(RackPos.x, RackPos.y, RackSize.x, RackSize.y);
            rackImg.raycastTarget = true;

            for (int p = 0; p < 4; p++) _plates[p] = Plate.Create(_root, PlatePos[p], p == 0);

            // action bar
            _hud = Ui.Node("Actions", _root).Place(560, -455, 800, 150);
            var actions = new (string id, string label, Ui.Style style)[]
            {
                ("wall", "Desteden Çek", Ui.Style.Wood), ("pile", "Yerden Al", Ui.Style.Wood), ("finish", "Bitir", Ui.Style.Primary), ("indicator", "Gösterge", Ui.Style.Wood),
                ("hint", "İpucu", Ui.Style.Wood), ("runs", "Seri Diz", Ui.Style.Wood), ("pairs", "Çift Diz", Ui.Style.Wood), ("menu", "Menü", Ui.Style.Gold),
            };
            for (int i = 0; i < actions.Length; i++)
            {
                var a = actions[i];
                var b = Ui.Btn(_hud, a.label, a.style, () => OnAction(a.id), 26);
                ((RectTransform)b.transform).Place(-300 + (i % 4) * 200, i < 4 ? 38 : -38, 188, 66);
                _btn[a.id] = b;
            }
            var chat = Ui.Btn(_root, "Sohbet", Ui.Style.Ghost, () => ChatRequested?.Invoke(), 24);
            ((RectTransform)chat.transform).Place(880, 500, 140, 56);
            _btn["chat"] = chat;

            _okeyBanner = Ui.Art(_root, "Okey Banner", "okey");
            _okeyBanner.rectTransform.Place(0, 160, 720, 273);
            _okeyBanner.enabled = false;
            SetTheme(Themes.All[0]);
        }

        public void SetTheme(Theme t)
        {
            _bg.sprite = Ui.Art(t.Table);
            _tea.sprite = Ui.Art(t.Tea);
            _tea.enabled = _tea.sprite != null;
        }

        // ---------------------------------------------------------------- binding
        public void Bind(TableModel model)
        {
            if (Model != null) Model.Changed -= MarkDirty;
            Model = model;
            foreach (var t in _rackTiles.Values) Destroy(t.gameObject);
            _rackTiles.Clear();
            _turnSeat = -1;
            Model.Changed += MarkDirty;
            Model.ChatReceived += c => _plates[Pos(c.Seat)].Bubble(c.Text);
            Model.EmoteReceived += e => _plates[Pos(e.Seat)].Bubble(EmoteLabel(e.Id));
            _btn["chat"].gameObject.SetActive(model.Table.Mode == "online");
            _dirty = true;
        }

        public static string EmoteLabel(string id) => id switch
        {
            "wave" => "Selam!", "laugh" => "Ha ha!", "wow" => "Vay be!", "think" => "Hmm…", "clap" => "Bravo!", "angry" => "Of ya!", "tea" => "Çay söyleyin!", "gg" => "İyi oyundu!", _ => id,
        };

        private void MarkDirty() => _dirty = true;

        private void Update()
        {
            if (Model == null) return;
            PlayEvents();
            if (_dirty) { _dirty = false; Render(); }
            Countdown();
            if (_okeyBanner.enabled && Time.time > _bannerUntil) _okeyBanner.enabled = false;
        }

        // ---------------------------------------------------------------- events → toasts / effects
        private string NameOf(int seat) => Model.Seats.FirstOrDefault(s => s.Seat == seat)?.Name ?? "Oyuncu";

        private void PlayEvents()
        {
            GameEvent e;
            while ((e = Model.NextEvent()) != null)
            {
                switch (e.Type)
                {
                    case "handStart":
                        Toast?.Invoke($"{e.Hand}. el — Okey: {Core.ColorNameTr(e.Okey.Color)} {e.Okey.Value}");
                        break;
                    case "indicator":
                        Toast?.Invoke($"{NameOf(e.Seat)} göstergeyi gösterdi (diğerlerine −{e.Penalty})");
                        break;
                    case "finish":
                        if (e.OkeyDiscard) { _okeyBanner.enabled = true; _bannerUntil = Time.time + 2.2f; }
                        Toast?.Invoke($"{NameOf(e.Seat)} {(e.FinishType == "pairs" ? "çiftten" : "")} bitirdi{(e.OkeyDiscard ? " — OKEY ile!" : "!")} Diğerlerine −{e.Penalty}");
                        break;
                    case "handDraw":
                        Toast?.Invoke("Deste bitti — el berabere");
                        break;
                }
            }
        }

        // ---------------------------------------------------------------- render
        private void Render()
        {
            var v = Model.View;
            // plates
            for (int p = 0; p < 4; p++)
            {
                int seat = SeatAt(p);
                var info = Model.Seats.FirstOrDefault(s => s.Seat == seat);
                int pts = v?.Points != null && seat < v.Points.Length ? v.Points[seat] : 20;
                _plates[p].Set(info?.Name ?? "…", info?.Avatar ?? 0, pts, info == null || info.Connected);
            }
            if (v == null) { _pill.text = "Masa hazırlanıyor…"; UpdateButtons(); return; }
            _pill.text = v.Okey != null ? $"Deste {v.WallCount}  ·  Okey: {Core.ColorNameTr(v.Okey.Color)} {v.Okey.Value}" : "";
            _wall.gameObject.SetActive(v.WallCount > 0);
            _indicator.gameObject.SetActive(v.Indicator != null);
            if (v.Indicator != null) _indicator.Show(v.Indicator);

            for (int p = 0; p < 4; p++)
            {
                var pile = v.Discards != null && SeatAt(p) < v.Discards.Count ? v.Discards[SeatAt(p)] : new List<Tile>();
                _pileTop[p].gameObject.SetActive(pile.Count > 0);
                if (pile.Count > 0) _pileTop[p].Show(pile[pile.Count - 1]);
                _pileCount[p].text = pile.Count > 1 ? "×" + pile.Count : "";
            }
            _pileTop[3].Glow(Model.CanDraw && v.AvailableDiscard != null);

            // rack: one TileView per tile in hand, positioned at its slot
            var inHand = new HashSet<int>(v.Hand.Select(t => t.Id));
            foreach (var id in _rackTiles.Keys.Where(id => !inHand.Contains(id)).ToList()) { Destroy(_rackTiles[id].gameObject); _rackTiles.Remove(id); }
            var finishing = Model.FinishingTile;
            for (int r = 0; r < TableModel.Rows; r++)
                for (int s = 0; s < TableModel.Slots; s++)
                {
                    int id = Model.Rack[r, s];
                    if (id < 0) continue;
                    if (!_rackTiles.TryGetValue(id, out var tv))
                    {
                        tv = TileView.Create(_rack, TileW, TileH);
                        tv.Show(Model.TileById(id));
                        tv.Draggable = true;
                        tv.DoubleClicked = OnTileDoubleClick;
                        tv.Dropped = OnTileDropped;
                        _rackTiles[id] = tv;
                    }
                    tv.Home = SlotPos(r, s);
                    tv.Rect.anchoredPosition = tv.Home;
                    tv.Glow(finishing != null && finishing.Id == id);
                }
            UpdateButtons();
        }

        private static Vector2 SlotPos(int row, int slot) => new Vector2(SlotX0 + slot * SlotDx, RowY[row]);

        private void UpdateButtons()
        {
            var v = Model.View;
            bool on = v != null && Model.Result == null;
            _btn["wall"].SetEnabled(on && Model.CanDraw);
            _btn["pile"].SetEnabled(on && Model.CanDraw && v.AvailableDiscard != null);
            _btn["finish"].SetEnabled(on && Model.FinishingTile != null);
            _btn["indicator"].SetEnabled(on && Model.CanShowIndicator);
            _btn["runs"].SetEnabled(on && v.Hand.Count > 0);
            _btn["pairs"].SetEnabled(on && v.Hand.Count > 0);
            int hl = HintsLeft?.Invoke() ?? int.MaxValue;
            _btn["hint"].SetEnabled(on && Model.MyTurn && hl > 0);
            _btn["hint"].SetLabel(hl == int.MaxValue ? "İpucu" : $"İpucu ({hl})");
        }

        private void Countdown()
        {
            var t = Model.Turn;
            bool live = t != null && Model.Result == null && Model.View != null && (Model.View.Phase == Phases.Draw || Model.View.Phase == Phases.Discard);
            if (!live) { foreach (var p in _plates) p.Ring(0, false); return; }
            if (t.Seat != _turnSeat || t.Deadline != _turnDeadline)
            {
                _turnSeat = t.Seat;
                _turnDeadline = t.Deadline;
                _turnStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            float total = Mathf.Max(1, _turnDeadline - _turnStart);
            float left = Mathf.Clamp01((_turnDeadline - now) / total);
            for (int p = 0; p < 4; p++) _plates[p].Ring(left, SeatAt(p) == t.Seat);
        }

        // ---------------------------------------------------------------- gestures
        private void OnAction(string id)
        {
            if (id == "menu") { MenuRequested?.Invoke(); return; }
            if (Model == null) return;
            switch (id)
            {
                case "wall": Model.Draw("wall"); break;
                case "pile": Model.Draw("discard"); break;
                case "finish": Model.Finish(); break;
                case "indicator": Model.ShowIndicator(); break;
                case "runs": Model.Arrange(false); break;
                case "pairs": Model.Arrange(true); break;
                case "hint": GiveHint(); break;
            }
        }

        /// <summary>The strongest bot's move for the current position.</summary>
        private void GiveHint()
        {
            if (!Model.MyTurn || (HintsLeft?.Invoke() ?? 1) <= 0) return;
            HintUsed?.Invoke();
            var bot = new Bot(5);
            var bv = BotView.FromSeatView(Model.View);
            if (Model.CanDraw)
            {
                if (bot.DecideDraw(bv) == "discard" && Model.View.AvailableDiscard != null) { _pileTop[3].Glow(true, 2.5f); Toast?.Invoke("İpucu: yerdeki taşı al"); }
                else
                {
                    foreach (var t in _wall.GetComponentsInChildren<TileView>()) t.Glow(true, 2.5f);
                    Toast?.Invoke("İpucu: desteden çek");
                }
            }
            else
            {
                var d = bot.DecideDiscard(bv);
                if (_rackTiles.TryGetValue(d.TileId, out var tv)) tv.Glow(true, 3f);
                Toast?.Invoke(d.Finish ? "İpucu: bu taşla bitirebilirsin!" : "İpucu: parlayan taşı at");
            }
            UpdateButtons();
        }

        private void OnTileDoubleClick(TileView tv)
        {
            if (Model.CanDiscard) Throw(tv.Tile.Id);
        }

        private void Throw(int id)
        {
            var f = Model.FinishingTile;
            if (f != null && f.Id == id) Model.Finish(id); else Model.Discard(id);
        }

        /// <summary>Dropped on my discard pile → throw; on the indicator → finish with it; anywhere on the rack → move to the nearest slot.</summary>
        private void OnTileDropped(TileView tv)
        {
            var pos = tv.Rect.anchoredPosition + RackPos;                       // rack-local → screen-center coordinates
            if (Model.CanDiscard && Vector2.Distance(pos, PilePos[0]) < 110) { Throw(tv.Tile.Id); tv.Rect.anchoredPosition = tv.Home; return; }
            if (Model.CanDiscard && Vector2.Distance(pos, IndicatorPos) < 100) { Model.Finish(tv.Tile.Id); tv.Rect.anchoredPosition = tv.Home; return; }
            var local = tv.Rect.anchoredPosition;
            int row = Mathf.Abs(local.y - RowY[0]) < Mathf.Abs(local.y - RowY[1]) ? 0 : 1;
            int slot = Mathf.Clamp(Mathf.RoundToInt((local.x - SlotX0) / SlotDx), 0, TableModel.Slots - 1);
            bool inside = Mathf.Abs(local.x) < RackSize.x / 2 + 40 && Mathf.Abs(local.y) < RackSize.y / 2 + 60;
            if (!inside || !Model.Move(tv.Tile.Id, row, slot)) tv.Rect.anchoredPosition = tv.Home;
        }

        // ---------------------------------------------------------------- plate
        private sealed class Plate
        {
            private RectTransform _rt;
            private Text _name, _points, _bubbleText;
            private Image _avatar, _ring;
            private GameObject _bubble;
            private float _bubbleUntil;

            public static Plate Create(Transform parent, Vector2 pos, bool me)
            {
                var p = new Plate();
                p._rt = Ui.Frame(parent, "Plate", new Color(0.15f, 0.08f, 0.03f, 0.88f), Pal.Gold, 3).Place(pos.x, pos.y, 330, 84);
                p._ring = Ui.Img(p._rt, "Ring", Ui.Circle, Pal.Green);
                p._ring.rectTransform.Place(-124, 0, 86, 86);
                p._ring.type = Image.Type.Filled;
                p._ring.fillMethod = Image.FillMethod.Radial360;
                p._ring.fillOrigin = (int)Image.Origin360.Top;
                p._ring.fillClockwise = false;
                var mask = Ui.Img(p._rt, "Mask", Ui.Circle, Color.white);
                mask.rectTransform.Place(-124, 0, 72, 72);
                mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                p._avatar = Ui.Img(mask.transform, "Avatar", null, Color.white);
                p._avatar.rectTransform.Stretch();
                p._name = Ui.Txt(p._rt, "", 28, Pal.Cream, TextAnchor.MiddleLeft);
                p._name.rectTransform.Place(40, 14, 220, 36);
                p._points = Ui.Txt(p._rt, "", 22, Pal.Gold, TextAnchor.MiddleLeft, false);
                p._points.rectTransform.Place(40, -18, 220, 28);
                var bubble = Ui.Frame(p._rt, "Bubble", Pal.Cream, Pal.GoldDark, 2);
                bubble.Place(0, me ? 92 : -92, 360, 64);
                p._bubbleText = Ui.Txt(bubble, "", 24, Pal.WoodDark, TextAnchor.MiddleCenter, false);
                p._bubbleText.rectTransform.Stretch(8);
                p._bubbleText.resizeTextForBestFit = true;
                p._bubbleText.resizeTextMinSize = 12;
                p._bubbleText.resizeTextMaxSize = 24;
                p._bubble = bubble.gameObject;
                p._bubble.SetActive(false);
                p._rt.gameObject.AddComponent<PlateTicker>().Tick = p.Tick;
                return p;
            }

            public void Set(string name, int avatar, int points, bool connected)
            {
                _name.text = connected ? name : name + " (bağlantı yok)";
                _points.text = "Puan " + points;
                _avatar.sprite = Ui.Avatar(avatar);
            }

            public void Ring(float fill, bool active)
            {
                _ring.enabled = active;
                _ring.fillAmount = fill;
                _ring.color = fill > 0.3f ? Pal.Green : Pal.Red;
            }

            public void Bubble(string text)
            {
                _bubbleText.text = text;
                _bubble.SetActive(true);
                _bubbleUntil = Time.time + 4f;
            }

            private void Tick() { if (_bubble.activeSelf && Time.time > _bubbleUntil) _bubble.SetActive(false); }
        }

        private sealed class PlateTicker : MonoBehaviour
        {
            public Action Tick;
            private void Update() => Tick?.Invoke();
        }
    }
}
