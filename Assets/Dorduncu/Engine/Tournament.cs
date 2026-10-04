using System;
using System.Collections.Generic;
using System.Linq;

namespace Dorduncu.Engine
{
    [Serializable] public sealed class Entrant { public string Id; public bool Bot; }
    [Serializable] public sealed class TTable { public int Round; public int Index; public List<Entrant> Seats; }
    [Serializable] public sealed class TableResult { public int Round; public int Index; public List<string> Ranking; }
    [Serializable] public sealed class Standing { public string Id; public bool Bot; public int Place; }

    /// <summary>
    /// Tournament bracket — pure and deterministic (port of packages/engine/src/tournament.ts). 4-player tables, the top two
    /// advance; seats are drawn with the tournament's published seed so anyone can verify the draw.
    /// </summary>
    public static class Tournament
    {
        public static readonly int[] Capacities = { 16, 32, 64 };

        public static int RoundsFor(int capacity) => (int)Math.Round(Math.Log(capacity / 4.0, 2)) + 1;

        public static List<TTable> DrawFirstRound(IList<string> humans, int capacity, string seed)
        {
            if (!Capacities.Contains(capacity)) throw new ArgumentException("capacity must be 16, 32 or 64");
            if (humans.Distinct().Count() != humans.Count) throw new ArgumentException("duplicate entrant");
            if (humans.Count > capacity) throw new ArgumentException("more entrants than capacity");
            if (humans.Count < 2) throw new ArgumentException("a tournament needs at least two human players");
            var sorted = humans.ToList();
            sorted.Sort(StringComparer.Ordinal);
            var pool = sorted.Select(id => new Entrant { Id = id, Bot = false }).ToList();
            for (int i = 0; i < capacity - humans.Count; i++) pool.Add(new Entrant { Id = "bot:" + (i + 1), Bot = true });
            var rng = new SeededRng(seed, "tournament-draw");
            for (int i = pool.Count - 1; i > 0; i--) { int j = rng.NextInt(i + 1); var t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
            return Chunk(pool, 1);
        }

        public static List<TTable> NextRound(IList<TTable> tables, IList<TableResult> results, string seed)
        {
            int round = tables[0].Round;
            if (results.Count != tables.Count) throw new ArgumentException("every table of the round must be finished");
            var advancing = new List<Entrant[]>();
            foreach (var t in tables)
            {
                var r = results.FirstOrDefault(x => x.Round == round && x.Index == t.Index) ?? throw new ArgumentException("missing result for table " + t.Index);
                var ids = t.Seats.Select(s => s.Id).ToList();
                if (r.Ranking.Count != 4 || r.Ranking.Distinct().Count() != 4 || !r.Ranking.All(ids.Contains)) throw new ArgumentException("invalid ranking for table " + t.Index);
                advancing.Add(new[] { t.Seats.First(s => s.Id == r.Ranking[0]), t.Seats.First(s => s.Id == r.Ranking[1]) });
            }
            if (advancing.Count == 1) throw new ArgumentException("the final table is already the last round");
            var rng = new SeededRng(seed, "round-" + (round + 1));
            var order = Enumerable.Range(0, advancing.Count).ToList();
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.NextInt(i + 1); var t = order[i]; order[i] = order[j]; order[j] = t; }
            int m = order.Count;
            var W = order.Select(i => advancing[i][0]).ToList();
            var R = order.Select(i => advancing[i][1]).ToList();
            var outList = new List<Entrant>();
            for (int j = 0; j < m / 2; j++) outList.AddRange(new[] { W[2 * j], W[2 * j + 1], R[(2 * j + 2) % m], R[(2 * j + 3) % m] });
            return Chunk(outList, round + 1);
        }

        public static List<Standing> Standings(IList<TTable> allTables, IList<TableResult> allResults)
        {
            int lastRound = allTables.Max(t => t.Round);
            var finalRes = allResults.FirstOrDefault(r => r.Round == lastRound);
            if (finalRes == null || allTables.Count(t => t.Round == lastRound) != 1) throw new ArgumentException("tournament not finished");
            var byId = new Dictionary<string, Entrant>();
            foreach (var t in allTables) foreach (var s in t.Seats) byId[s.Id] = s;
            var rows = new List<(string id, bool bot, int k0, int k1)>();
            foreach (var r in allResults)
                for (int pos = 0; pos < r.Ranking.Count; pos++)
                    if (r.Round == lastRound || pos >= 2) rows.Add((r.Ranking[pos], byId[r.Ranking[pos]].Bot, -r.Round, pos));
            rows = Js.StableSort(rows, (a, b) => a.k0 != b.k0 ? a.k0.CompareTo(b.k0) : a.k1 != b.k1 ? a.k1.CompareTo(b.k1) : string.CompareOrdinal(a.id, b.id));
            return rows.Select((r, i) => new Standing { Id = r.id, Bot = r.bot, Place = i + 1 }).ToList();
        }

        private static List<TTable> Chunk(List<Entrant> pool, int round)
        {
            var tables = new List<TTable>();
            for (int i = 0; i < pool.Count; i += 4) tables.Add(new TTable { Round = round, Index = i / 4, Seats = pool.GetRange(i, 4) });
            return tables;
        }

        /// <summary>Play a whole table with bots (deterministic in its seed). Returns entrant ids best-first.</summary>
        public static List<string> SimulateTable(IList<Entrant> seats, string seed)
        {
            var g = new OkeyGame(4);
            var rng = new SeededRng(seed, "sim-table");
            var tracker = new PickupTracker(4);
            var bots = seats.Select(_ => new Bot(3, rng.NextFloat)).ToList();
            g.OnEvent += tracker.Handle;
            for (int guard = 0; g.Phase != Phases.MatchOver; guard++)
            {
                if (guard > 50000) throw new InvalidOperationException("simulated table did not finish");
                if (g.Phase == Phases.HandOver) { g.StartHand(seed + ":hand:" + (g.HandNo + 1) + ":padding-for-length", "tournament"); continue; }
                int s = g.Turn;
                if (g.Phase == Phases.Draw) g.Draw(s, bots[s].DecideDraw(BotView.For(g, s, tracker.Pickups)));
                else
                {
                    var d = bots[s].DecideDiscard(BotView.For(g, s, tracker.Pickups));
                    if (d.Finish) g.Finish(s, d.TileId); else g.Discard(s, d.TileId);
                }
            }
            return Js.StableSort(Enumerable.Range(0, 4), (a, b) => g.Points[b] != g.Points[a] ? g.Points[b].CompareTo(g.Points[a]) : a.CompareTo(b)).Select(i => seats[i].Id).ToList();
        }
    }
}
