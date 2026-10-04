using System;
using System.Collections.Generic;
using System.Linq;

namespace Dorduncu.Engine
{
    /// <summary>What a bot (or a human) at the table can see.</summary>
    public sealed class BotView
    {
        public int Seat;
        public int Players;
        public List<Tile> Hand;
        public OkeyDef Okey;
        public Tile Indicator;
        public List<List<Tile>> Discards;   // per seat
        public Tile Available;              // the discard I may take
        public List<List<Tile>> Pickups;    // per seat: tiles that seat took from the discard pile
        public RuleSet Rules;

        public static BotView For(OkeyGame g, int seat, List<List<Tile>> pickups) => new BotView
        {
            Seat = seat, Players = g.Players, Hand = new List<Tile>(g.Seats[seat].Hand), Okey = g.Okey, Indicator = g.Indicator,
            Discards = g.Seats.Select(s => new List<Tile>(s.Discards)).ToList(), Available = g.AvailableDiscard(seat), Pickups = pickups, Rules = g.Rules,
        };

        /// <summary>From the network view a client receives (hints / autopilot).</summary>
        public static BotView FromSeatView(SeatView v, List<List<Tile>> pickups = null) => new BotView
        {
            Seat = v.Seat, Players = v.Players, Hand = new List<Tile>(v.Hand), Okey = v.Okey, Indicator = v.Indicator,
            Discards = v.Discards.Select(d => new List<Tile>(d)).ToList(), Available = v.AvailableDiscard,
            Pickups = pickups ?? Enumerable.Range(0, v.Players).Select(_ => new List<Tile>()).ToList(), Rules = v.Rules ?? RuleSet.DuzOkeyDefault,
        };
    }

    public struct DiscardDecision { public int TileId; public bool Finish; }

    /// <summary>Human-like bots, levels 1 (Kolay) … 5 (Usta). Port of packages/engine/src/bot.ts.</summary>
    public sealed class Bot
    {
        private sealed class LevelCfg { public double Mistake, IndicatorRecall; public bool Pairs, Track, Defend; public double Noise; }

        private static readonly Dictionary<int, LevelCfg> Levels = new Dictionary<int, LevelCfg>
        {
            { 1, new LevelCfg { Mistake = 0.28, IndicatorRecall = 0.55, Pairs = false, Track = false, Defend = false, Noise = 60 } },
            { 2, new LevelCfg { Mistake = 0.14, IndicatorRecall = 0.8, Pairs = false, Track = false, Defend = false, Noise = 35 } },
            { 3, new LevelCfg { Mistake = 0.06, IndicatorRecall = 0.95, Pairs = true, Track = true, Defend = false, Noise = 15 } },
            { 4, new LevelCfg { Mistake = 0.02, IndicatorRecall = 1, Pairs = true, Track = true, Defend = true, Noise = 6 } },
            { 5, new LevelCfg { Mistake = 0, IndicatorRecall = 1, Pairs = true, Track = true, Defend = true, Noise = 0 } },
        };

        public readonly int Level;
        private readonly LevelCfg _cfg;
        private readonly Func<double> _rand;
        private static readonly Random Shared = new Random();

        public Bot(int level, Func<double> rand = null)
        {
            if (!Levels.TryGetValue(level, out _cfg)) throw new ArgumentException("bot level must be 1..5");
            Level = level;
            _rand = rand ?? (() => { lock (Shared) return Shared.NextDouble(); });
        }

        /// <summary>Human-like think time in ms (log-normal, longer for harder decisions).</summary>
        public int ThinkMs(int complexity)
        {
            double u = Math.Max(1e-6, _rand()), v = _rand();
            double z = Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
            double median = 1700 + complexity * 650;
            return Js.Round(Math.Min(7000, Math.Max(900, median * Math.Exp(0.35 * z))));
        }

        public bool WantsIndicator(bool canShow) => canShow && _rand() < _cfg.IndicatorRecall;

        private static Func<string, int, int> Unseen(BotView v)
        {
            var seen = new Dictionary<string, int>();
            Action<Tile> add = t => { if (t != null && !t.Fake) { var k = t.Color + t.Value; seen[k] = (seen.TryGetValue(k, out var n) ? n : 0) + 1; } };
            v.Hand.ForEach(add);
            foreach (var d in v.Discards) d.ForEach(add);
            add(v.Indicator);
            return (c, n) => n < 1 || n > 13 ? 0 : Math.Max(0, 2 - (seen.TryGetValue(c + n, out var x) ? x : 0));
        }

        private bool PairsMode(BotView v, List<Tile> hand)
        {
            if (!_cfg.Pairs) return false;
            int p = Analysis.PairsCount(hand, v.Okey);
            int loose = Analysis.Arrange(hand, v.Okey, v.Rules).LooseCount;
            return p >= 5 || (p >= 4 && loose >= 7);
        }

        /// <summary>Score a 14-tile hand: lower is better.</summary>
        private double HandScore(BotView v, List<Tile> hand, bool pairs, Func<string, int, int> live)
        {
            if (pairs)
            {
                int pc = Analysis.PairsInfo(hand, v.Okey, out var singles);
                double s0 = (7 - pc) * 100;
                foreach (var t in singles) if (!t.Fake && _cfg.Track && live(t.Color, t.Value.Value) == 0) s0 += 25; // dead single
                return s0;
            }
            var a = Analysis.Arrange(hand, v.Okey, v.Rules);
            double s = a.LooseCount * 100;
            Func<string, int, int> liveOf = (cc, nn) => _cfg.Track ? live(cc, nn) : 1;
            foreach (var t in a.Loose)
            {
                if (t.Fake || t.Color == null) continue;
                string c = t.Color;
                int n = t.Value.Value;
                double pot = liveOf(c, n - 1) + liveOf(c, n + 1) + 0.5 * (liveOf(c, n - 2) + liveOf(c, n + 2));
                foreach (var cc in Core.Colors) if (cc != c) pot += 0.6 * liveOf(cc, n);
                s -= pot * 4;
            }
            return s;
        }

        private double Danger(BotView v, Tile t)
        {
            if (!_cfg.Defend || t.Fake) return 0;
            int next = (v.Seat + 1) % v.Players;
            int d = 0;
            if (v.Pickups != null && next < v.Pickups.Count)
                foreach (var p in v.Pickups[next])
                {
                    if (p.Fake || p.Color == null) continue;
                    if (p.Color == t.Color && Math.Abs(p.Value.Value - t.Value.Value) <= 2) d++;
                    if (p.Value == t.Value && p.Color != t.Color) d++;
                }
            return d * 18;
        }

        /// <summary>"wall" or "discard".</summary>
        public string DecideDraw(BotView v)
        {
            var d = v.Available;
            if (d == null) return "wall";
            if (Core.IsOkeyTile(d, v.Okey)) return Level >= 2 ? "discard" : _rand() < 0.5 ? "discard" : "wall";
            var live = Unseen(v);
            bool pairs = PairsMode(v, v.Hand);
            double now = HandScore(v, v.Hand, pairs, live);
            var withD = new List<Tile>(v.Hand) { d };
            double best = double.PositiveInfinity;
            foreach (var x in withD)
            {
                if (x.Id == d.Id || Core.IsOkeyTile(x, v.Okey)) continue;
                best = Math.Min(best, HandScore(v, withD.Where(t => t.Id != x.Id).ToList(), pairs, live));
            }
            bool take = best < now - 60; // must genuinely remove a loose tile
            if (_rand() < _cfg.Mistake) return take ? "wall" : "discard";
            return take ? "discard" : "wall";
        }

        /// <summary>The tile to throw and whether it finishes the hand.</summary>
        public DiscardDecision DecideDiscard(BotView v)
        {
            var win = Core.FindWinningDiscard(v.Hand, v.Okey, v.Rules);
            if (win != null) return new DiscardDecision { TileId = v.Hand[win.DiscardIndex].Id, Finish = true };
            var live = Unseen(v);
            bool pairs = PairsMode(v, v.Hand);
            var scored = new List<KeyValuePair<Tile, double>>();
            foreach (var t in v.Hand)
            {
                if (Core.IsOkeyTile(t, v.Okey)) continue;
                var rest = v.Hand.Where(x => x.Id != t.Id).ToList();
                double s = HandScore(v, rest, pairs, live) + Danger(v, t) + (_cfg.Noise != 0 ? _rand() * _cfg.Noise : 0);
                scored.Add(new KeyValuePair<Tile, double>(t, s));
            }
            scored = Js.StableSort(scored, (a, b) => a.Value.CompareTo(b.Value));
            if (scored.Count == 0) return new DiscardDecision { TileId = v.Hand[0].Id, Finish = false };
            var pick = scored[0];
            if (_rand() < _cfg.Mistake && scored.Count > 2) pick = scored[1 + (int)Math.Floor(_rand() * Math.Min(3, scored.Count - 1))];
            return new DiscardDecision { TileId = pick.Key.Id, Finish = false };
        }
    }

    /// <summary>Tracks discard pickups for every seat from game events (what any observer at the table can see).</summary>
    public sealed class PickupTracker
    {
        public List<List<Tile>> Pickups;
        public PickupTracker(int players) { Pickups = Enumerable.Range(0, players).Select(_ => new List<Tile>()).ToList(); }

        public void Handle(GameEvent e)
        {
            if (e.Type == "handStart") foreach (var p in Pickups) p.Clear();
            if (e.Type == "draw" && e.From == "discard" && e.Tile != null) Pickups[e.Seat].Add(e.Tile);
        }
    }
}
