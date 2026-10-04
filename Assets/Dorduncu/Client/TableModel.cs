// TableModel: everything the table screen needs, derived from an adapter's messages (port of client-core/model.ts). Keeps the
// player's own rack order (drawn tiles go to a free slot, discarded ones leave), offers auto-arrange, tells whether the hand
// can finish and with which tile, and queues public events for the renderer's animations.
using System;
using System.Collections.Generic;
using System.Linq;
using Dorduncu.Engine;

namespace Dorduncu.Client
{
    public sealed class TableModel : IDisposable
    {
        public const int Rows = 2, Slots = 15;
        public readonly ITableAdapter Table;
        public List<SeatInfo> Seats = new List<SeatInfo>();
        public SeatView View;
        public TurnInfo Turn;
        public MatchResult Result;
        public readonly List<ChatLine> Chat = new List<ChatLine>();
        public TableError LastError;
        public bool Connected = true;
        public string ClosedReason;
        /// <summary>tile ids per [row, slot]; -1 = empty</summary>
        public int[,] Rack = NewRack();
        private Dictionary<int, Tile> _tiles = new Dictionary<int, Tile>();
        private readonly Queue<GameEvent> _queue = new Queue<GameEvent>();
        private int _handNo;

        public event Action Changed;
        public event Action<ChatLine> ChatReceived;
        public event Action<Emote> EmoteReceived;
        public event Action<int> ReportAcknowledged;

        public TableModel(ITableAdapter table)
        {
            Table = table;
            table.Seats += s => { Seats = s; Fire(); };
            table.View += v => { ApplyView(v); Fire(); };
            table.Event += e => { _queue.Enqueue(e); Fire(); };
            table.Turn += t => { Turn = t; Fire(); };
            table.Chat += c => { Chat.Add(c); if (Chat.Count > 100) Chat.RemoveAt(0); ChatReceived?.Invoke(c); Fire(); };
            table.EmoteShown += e => EmoteReceived?.Invoke(e);
            table.Error += e => { LastError = e; Fire(); };
            table.Result += r => { Result = r; Fire(); };
            table.Reported += s => ReportAcknowledged?.Invoke(s);
            table.Closed += r => { ClosedReason = r; Fire(); };
            table.Connection += up => { Connected = up; Fire(); };
        }

        public void Dispose() { Changed = null; }
        private void Fire() => Changed?.Invoke();
        private static int[,] NewRack() { var r = new int[Rows, Slots]; for (int i = 0; i < Rows; i++) for (int j = 0; j < Slots; j++) r[i, j] = -1; return r; }

        // ---------------------------------------------------------------- derived state
        public int MySeat => View?.Seat ?? 0;
        public bool MyTurn => View != null && View.Turn == View.Seat && (View.Phase == Phases.Draw || View.Phase == Phases.Discard) && Result == null;
        public bool CanDraw => MyTurn && View.Phase == Phases.Draw;
        public bool CanDiscard => MyTurn && View.Phase == Phases.Discard;
        public bool CanShowIndicator => View != null && View.CanShowIndicator && Result == null;
        public Tile TileById(int id) => _tiles.TryGetValue(id, out var t) ? t : null;

        /// <summary>The tile to throw to finish right now (null if the hand cannot finish).</summary>
        public Tile FinishingTile
        {
            get
            {
                if (!CanDiscard || View.Okey == null) return null;
                var f = Core.FindWinningDiscard(View.Hand, View.Okey, View.Rules);
                return f != null ? View.Hand[f.DiscardIndex] : null;
            }
        }

        /// <summary>Next animation to play, in the order the table produced them.</summary>
        public GameEvent NextEvent() => _queue.Count > 0 ? _queue.Dequeue() : null;
        public int PendingEvents => _queue.Count;

        // ---------------------------------------------------------------- rack bookkeeping
        private void ApplyView(SeatView v)
        {
            bool newHand = v.HandNo != _handNo;
            View = v;
            _handNo = v.HandNo;
            _tiles = v.Hand.ToDictionary(t => t.Id);
            if (newHand) { Arrange(false); return; }
            var placed = new HashSet<int>();
            for (int r = 0; r < Rows; r++)
                for (int i = 0; i < Slots; i++)
                {
                    int id = Rack[r, i];
                    if (id < 0) continue;
                    if (!_tiles.ContainsKey(id)) Rack[r, i] = -1; else placed.Add(id);
                }
            foreach (var t in v.Hand) if (!placed.Contains(t.Id)) PlaceFree(t.Id);
        }

        /// <summary>A drawn tile goes to the right-most free slot of the lower row, else the upper row.</summary>
        private void PlaceFree(int id)
        {
            foreach (int r in new[] { 1, 0 })
                for (int i = Slots - 1; i >= 0; i--)
                    if (Rack[r, i] < 0) { Rack[r, i] = id; return; }
        }

        public bool Locate(int id, out int row, out int slot)
        {
            for (row = 0; row < Rows; row++) for (slot = 0; slot < Slots; slot++) if (Rack[row, slot] == id) return true;
            row = slot = -1;
            return false;
        }

        /// <summary>Player drag: move a tile to (row, slot); an occupied slot swaps. Purely local.</summary>
        public bool Move(int id, int row, int slot)
        {
            if (row < 0 || row >= Rows || slot < 0 || slot >= Slots) return false;
            if (!Locate(id, out int fr, out int fs)) return false;
            int other = Rack[row, slot];
            Rack[row, slot] = id;
            Rack[fr, fs] = other;
            Fire();
            return true;
        }

        /// <summary>Lay the hand out in groups (runs/sets or pairs) with a gap between groups, wrapping to the second row.</summary>
        public void Arrange(bool pairs)
        {
            var v = View;
            if (v?.Okey == null) return;
            var groups = pairs ? Analysis.AutoArrangePairs(v.Hand, v.Okey) : Analysis.AutoArrangeRuns(v.Hand, v.Okey, v.Rules);
            var rack = NewRack();
            int r = 0, i = 0;
            foreach (var g in groups)
            {
                if (i > 0 && i + g.Count > Slots) { r++; i = 0; }
                foreach (var t in g)
                {
                    if (i >= Slots) { r++; i = 0; }
                    if (r >= Rows) break;
                    rack[r, i++] = t.Id;
                }
                i++;                                   // gap between groups
            }
            var placed = new HashSet<int>();
            for (int a = 0; a < Rows; a++) for (int b = 0; b < Slots; b++) if (rack[a, b] >= 0) placed.Add(rack[a, b]);
            Rack = rack;
            foreach (var t in v.Hand) if (!placed.Contains(t.Id)) PlaceFree(t.Id);   // anything that did not fit
            Fire();
        }

        // ---------------------------------------------------------------- actions (validated by the server / offline engine)
        public void Draw(string from) => Table.Send("draw", new Dictionary<string, object> { ["from"] = from });
        public void Discard(int tileId) => Table.Send("discard", new Dictionary<string, object> { ["tileId"] = tileId });
        public void Finish(int? tileId = null) { var t = tileId ?? FinishingTile?.Id; if (t != null) Table.Send("finish", new Dictionary<string, object> { ["tileId"] = t.Value }); }
        public void ShowIndicator() => Table.Send("indicator", new Dictionary<string, object>());
        public void Say(string text) => Table.Send("chat", new Dictionary<string, object> { ["text"] = text });
        public void SendEmote(string id) => Table.Send("emote", new Dictionary<string, object> { ["id"] = id });
        public void Report(int seat, string reason) => Table.Send("report", new Dictionary<string, object> { ["seat"] = seat, ["reason"] = reason });
    }
}
