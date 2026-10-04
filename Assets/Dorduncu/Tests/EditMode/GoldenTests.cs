// The C# engine must reproduce every golden vector generated from the original TypeScript engine (the PHP API passes the
// same files): crypto, RNG, 200 deals, 1500 hands, league points, tournament brackets, simulated tables, hand arrangement
// and complete bot-vs-bot matches move by move. Runs in the Unity Test Runner (EditMode) and with plain `dotnet test`.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Dorduncu.Engine;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Dorduncu.Tests
{
    public class GoldenTests
    {
        private static string Dir([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here), "Golden");
        private static JToken G(string name) => JToken.Parse(File.ReadAllText(Path.Combine(Dir(), name + ".json")));

        private static Tile T(string s, int id = 0) => s == "F" ? new Tile(104, null, null, true) : new Tile(id, s.Substring(0, 1), int.Parse(s.Substring(1)), false);
        private static OkeyDef O(string s) => new OkeyDef(s.Substring(0, 1), int.Parse(s.Substring(1)));
        private static int[] Ints(JToken t) => t.Select(x => (int)x).ToArray();

        [Test]
        public void Crypto()
        {
            foreach (var c in G("crypto"))
            {
                Assert.AreEqual((string)c["sha256"], SeededRng.Sha256Hex((string)c["msg"]));
                Assert.AreEqual((string)c["hmac"], SeededRng.HmacSha256Hex((string)c["key"], (string)c["msg"]));
            }
        }

        [Test]
        public void Rng()
        {
            foreach (var r in G("rng"))
            {
                var a = new SeededRng((string)r["serverSeed"], (string)r["clientSeed"]);
                CollectionAssert.AreEqual(r["uint32"].Select(x => (uint)(long)x).ToArray(), Enumerable.Range(0, 12).Select(_ => a.NextUint32()).ToArray());
                // the generator continues on the same instance
                CollectionAssert.AreEqual(Ints(r["int7"]), Enumerable.Range(0, 12).Select(_ => a.NextInt(7)).ToArray());
            }
        }

        [Test]
        public void Deals()
        {
            foreach (var d in G("deals"))
            {
                var sh = Core.Shuffle(Core.CreateDeck(), (string)d["serverSeed"], (string)d["clientSeed"]);
                CollectionAssert.AreEqual(Ints(d["shuffledIds"]), sh.Select(t => t.Id).ToArray());
                var deal = Core.Deal(sh, 4, (int)d["starter"]);
                Assert.AreEqual((string)d["indicator"], deal.Indicator.ToString());
                Assert.AreEqual((string)d["okey"], deal.Okey.ToString());
                for (int p = 0; p < 4; p++) CollectionAssert.AreEqual(Ints(d["hands"][p]), deal.Hands[p].Select(t => t.Id).ToArray());
                CollectionAssert.AreEqual(Ints(d["wallIds"]), deal.Wall.Select(t => t.Id).ToArray());
            }
        }

        [Test]
        public void Hands()
        {
            int i = 0;
            foreach (var h in G("hands"))
            {
                var o = O((string)h["okey"]);
                var h14 = h["hand14"].Select(s => T((string)s)).ToList();
                var h15 = h["hand15"].Select(s => T((string)s)).ToList();
                Assert.AreEqual((bool)h["normalWin"], Core.IsNormalWin(h14, o), "normalWin #" + i);
                Assert.AreEqual((bool)h["pairsWin"], Core.IsPairsWin(h14, o), "pairsWin #" + i);
                var f = Core.FindWinningDiscard(h15, o);
                var w = h["winningDiscard"];
                if (w.Type == JTokenType.Null) Assert.IsNull(f, "winningDiscard #" + i);
                else
                {
                    Assert.IsNotNull(f, "winningDiscard #" + i);
                    Assert.AreEqual((int)w["index"], f.DiscardIndex);
                    Assert.AreEqual((string)w["type"], f.Type == FinishType.Pairs ? "pairs" : "normal");
                    Assert.AreEqual((bool)w["okeyDiscard"], f.OkeyDiscard);
                    Assert.AreEqual((int)w["penalty"], Core.FinishPenalty(f.Type, f.OkeyDiscard));
                }
                i++;
            }
        }

        [Test]
        public void LeaguePoints()
        {
            foreach (var l in G("lp"))
                Assert.AreEqual((int)l["delta"], Core.LpDelta((int)l["lost"], (bool)l["win"], (int)l["games"], (bool)l["quit"]), l.ToString());
        }

        [Test]
        public void Tournaments()
        {
            foreach (var t in G("tournaments"))
            {
                string seed = (string)t["seed"];
                var tables = Tournament.DrawFirstRound(t["humans"].Select(x => (string)x).ToList(), (int)t["capacity"], seed);
                var all = new List<TTable>();
                var res = new List<TableResult>();
                foreach (var round in t["rounds"])
                {
                    for (int k = 0; k < tables.Count; k++)
                        CollectionAssert.AreEqual(round["tables"][k].Select(x => (string)x).ToArray(), tables[k].Seats.Select(s => s.Id).ToArray());
                    all.AddRange(tables);
                    var rs = tables.Select((tb, k) => new TableResult { Round = tb.Round, Index = tb.Index, Ranking = round["rankings"][k].Select(x => (string)x).ToList() }).ToList();
                    res.AddRange(rs);
                    if (tables.Count > 1) tables = Tournament.NextRound(tables, rs, seed);
                }
                CollectionAssert.AreEqual(t["standings"].Select(x => (string)x).ToArray(), Tournament.Standings(all, res).Select(s => s.Id).ToArray());
            }
        }

        [Test]
        public void SimulatedTables()
        {
            foreach (var s in G("bots")["sims"].Take(6))
            {
                var ids = s["seats"].Select(x => (string)x).ToList();
                var seats = ids.Select((id, k) => new Entrant { Id = id, Bot = k % 2 == 0 }).ToList();
                CollectionAssert.AreEqual(s["ranking"].Select(x => (string)x).ToArray(), Tournament.SimulateTable(seats, (string)s["seed"]).ToArray(), (string)s["seed"]);
            }
        }

        [Test]
        public void Arrangement()
        {
            var deck = Core.CreateDeck();
            int i = 0;
            foreach (var a in G("bots")["arrange"])
            {
                var o = new OkeyDef((string)a["okey"]["color"], (int)a["okey"]["value"]);
                var hand = a["handIds"].Select(id => deck[(int)id]).ToList();
                var runs = Analysis.AutoArrangeRuns(hand, o).Select(g => g.Select(t => t.Id).ToArray()).ToList();
                var pairs = Analysis.AutoArrangePairs(hand, o).Select(g => g.Select(t => t.Id).ToArray()).ToList();
                Assert.AreEqual(a["runs"].Count(), runs.Count, "runs #" + i);
                for (int k = 0; k < runs.Count; k++) CollectionAssert.AreEqual(Ints(a["runs"][k]), runs[k], "runs #" + i);
                Assert.AreEqual(a["pairs"].Count(), pairs.Count, "pairs #" + i);
                for (int k = 0; k < pairs.Count; k++) CollectionAssert.AreEqual(Ints(a["pairs"][k]), pairs[k], "pairs #" + i);
                Assert.AreEqual((int)a["looseCount"], Analysis.LooseCount(hand, o));
                Assert.AreEqual((int)a["pairsCount"], Analysis.PairsCount(hand, o));
                i++;
            }
        }

        [Test]
        public void BotMatchesMoveByMove()
        {
            foreach (var gm in G("bots")["games"])
            {
                int i = (int)gm["index"];
                var g = new OkeyGame(4);
                var rng = new SeededRng("game-" + i, "bots");
                var tr = new PickupTracker(4);
                g.OnEvent += tr.Handle;
                var bots = new[] { 1, 2, 4, 5 }.Select(l => new Bot((l + i) % 5 + 1, rng.NextFloat)).ToList();
                CollectionAssert.AreEqual(Ints(gm["levels"]), bots.Select(b => b.Level).ToArray());
                var moves = new List<string>();
                for (int n = 0; g.Phase != Phases.MatchOver && n < 4000; n++)
                {
                    if (g.Phase == Phases.HandOver)
                    {
                        g.StartHand("game-" + i + "-hand-" + (g.HandNo + 1) + "-xxxxxxxx", "c");
                        for (int s = 0; s < 4; s++) if (g.CanShowIndicator(s) && bots[s].WantsIndicator(true)) { g.ShowIndicator(s); moves.Add("I" + s); }
                        continue;
                    }
                    int seat = g.Turn;
                    if (g.Phase == Phases.Draw) { var f = bots[seat].DecideDraw(BotView.For(g, seat, tr.Pickups)); g.Draw(seat, f); moves.Add("D" + seat + f[0]); }
                    else
                    {
                        var d = bots[seat].DecideDiscard(BotView.For(g, seat, tr.Pickups));
                        if (d.Finish) g.Finish(seat, d.TileId); else g.Discard(seat, d.TileId);
                        moves.Add((d.Finish ? "F" : "X") + seat + ":" + d.TileId);
                    }
                    Assert.AreEqual(106, g.TileCount());
                    if (g.Phase == Phases.MatchOver) break;
                }
                CollectionAssert.AreEqual(gm["moves"].Select(x => (string)x).ToArray(), moves.ToArray(), "game " + i);
                CollectionAssert.AreEqual(Ints(gm["points"]), g.Points, "game " + i);
            }
        }
    }
}
