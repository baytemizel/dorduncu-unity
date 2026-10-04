using System;
using System.Collections.Generic;
using System.Linq;

namespace Dorduncu.Engine
{
    public sealed class Deal
    {
        public List<List<Tile>> Hands;   // hands[starter] has 15, others 14
        public Tile Indicator;           // gösterge
        public OkeyDef Okey;
        public List<Tile> Wall;          // ortadaki deste
        public int Starter;
    }

    /// <summary>Okey core — rules, provably-fair shuffle, hand evaluation, scoring (port of packages/engine/src/core.ts).</summary>
    public static class Core
    {
        public static readonly string[] Colors = { "R", "Y", "B", "K" }; // kırmızı, sarı, mavi, siyah

        private static int ColorIndex(string c) => c == "R" ? 0 : c == "Y" ? 1 : c == "B" ? 2 : 3;

        // ------------------------------------------------------------------ deck
        public static List<Tile> CreateDeck()
        {
            var tiles = new List<Tile>(106);
            int id = 0;
            foreach (var color in Colors)
                for (int value = 1; value <= 13; value++)
                    for (int copy = 0; copy < 2; copy++)
                        tiles.Add(new Tile(id++, color, value, false));
            tiles.Add(new Tile(id++, null, null, true));
            tiles.Add(new Tile(id, null, null, true));
            return tiles;
        }

        /// <summary>Commit published before the deal: sha256(serverSeed) as lowercase hex.</summary>
        public static string CommitOf(string serverSeed) => SeededRng.Sha256Hex(serverSeed);

        public static List<Tile> Shuffle(IList<Tile> deck, string serverSeed, string clientSeed)
        {
            var outList = new List<Tile>(deck);
            var rng = new SeededRng(serverSeed, clientSeed);
            for (int i = outList.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                var t = outList[i]; outList[i] = outList[j]; outList[j] = t;
            }
            return outList;
        }

        // ------------------------------------------------------------------ deal
        public static OkeyDef OkeyFromIndicator(Tile ind)
        {
            if (ind.Fake || ind.Color == null || ind.Value == null) throw new ArgumentException("indicator cannot be fake okey");
            return new OkeyDef(ind.Color, ind.Value == 13 ? 1 : ind.Value.Value + 1);
        }

        public static Deal Deal(IList<Tile> shuffled, int players = 4, int starter = 0)
        {
            var pool = new List<Tile>(shuffled);
            if (players < 2 || players > 4) throw new ArgumentException("players must be 2..4");
            if (pool.Count != 106) throw new ArgumentException("deal needs a full 106-tile deck, got " + pool.Count);
            int indIdx = pool.FindIndex(t => !t.Fake);
            var indicator = pool[indIdx];
            pool.RemoveAt(indIdx);
            var hands = new List<List<Tile>>();
            for (int p = 0; p < players; p++)
            {
                int n = p == starter ? 15 : 14;
                hands.Add(pool.GetRange(0, n));
                pool.RemoveRange(0, n);
            }
            return new Deal { Hands = hands, Indicator = indicator, Okey = OkeyFromIndicator(indicator), Wall = pool, Starter = starter };
        }

        // ------------------------------------------------------------------ hand evaluation
        public static bool IsOkeyTile(Tile t, OkeyDef o) => !t.Fake && t.Color == o.Color && t.Value == o.Value;

        /// <summary>Counts [color 0..3][value 0..13] of concrete tiles (fake okey = okey identity) + wildcard count.</summary>
        private static int[,] Normalize(IList<Tile> hand, OkeyDef o, out int wild)
        {
            var c = new int[4, 15];
            wild = 0;
            foreach (var t in hand)
            {
                if (IsOkeyTile(t, o)) wild++;
                else if (t.Fake) c[ColorIndex(o.Color), o.Value]++;
                else c[ColorIndex(t.Color), t.Value.Value]++;
            }
            return c;
        }

        private struct Group { public int Size, MaxSize; }

        private static bool Absorb(List<Group> groups, int wild)
        {
            if (wild == 0) return true;
            int room = 0;
            foreach (var g in groups) room += g.MaxSize - g.Size;
            return room >= wild;
        }

        private static List<List<int>> Subsets(List<int> others)
        {
            var subsets = new List<List<int>> { new List<int>() };
            foreach (var col in others)
            {
                int n = subsets.Count;
                for (int k = 0; k < n; k++) subsets.Add(new List<int>(subsets[k]) { col });
            }
            return subsets;
        }

        private static bool SolveGroups(int[,] c, int wild, RuleSet rules, List<Group> groups)
        {
            int color = -1, value = 0;
            for (int v = 1; v <= 13 && color < 0; v++)
                for (int col = 0; col < 4; col++)
                    if (c[col, v] > 0) { color = col; value = v; break; }
            if (color < 0) return Absorb(groups, wild);
            c[color, value]--;

            // Option A: set (same value, distinct colors, size 3..4)
            var others = new List<int>();
            for (int col = 0; col < 4; col++) if (col != color && c[col, value] > 0) others.Add(col);
            foreach (var sub in Subsets(others))
            {
                for (int jokers = 0; jokers <= wild; jokers++)
                {
                    int size = 1 + sub.Count + jokers;
                    if (size < 3 || size > 4) continue;
                    foreach (var col in sub) c[col, value]--;
                    groups.Add(new Group { Size = size, MaxSize = 4 });
                    if (SolveGroups(c, wild - jokers, rules, groups)) return true;
                    groups.RemoveAt(groups.Count - 1);
                    foreach (var col in sub) c[col, value]++;
                }
            }

            // Option B: run (same color, consecutive). Position 14 = high ace (12-13-1).
            int maxPos = rules.AllowThirteenOne ? 14 : 13;
            int[] positions = value == 1 && rules.AllowThirteenOne ? new[] { 1, 14 } : new[] { value };
            foreach (int pos in positions)
            {
                int minStart = pos == 14 ? 2 : Math.Max(1, pos - wild);
                for (int start = minStart; start <= pos; start++)
                {
                    for (int end = Math.Max(pos, start + 2); end <= maxPos; end++)
                    {
                        if (pos != 14 && end == 14 && start <= 1) continue; // 1 can't be both low and high
                        var used = new List<int>();
                        int jokers = 0;
                        bool ok = true;
                        for (int p = start; p <= end; p++)
                        {
                            if (p == pos) continue;
                            int v = p == 14 ? 1 : p;
                            if (c[color, v] > 0) { c[color, v]--; used.Add(v); }
                            else if (jokers < wild) jokers++;
                            else { ok = false; break; }
                        }
                        if (ok)
                        {
                            groups.Add(new Group { Size = end - start + 1, MaxSize = 13 });
                            if (SolveGroups(c, wild - jokers, rules, groups)) return true;
                            groups.RemoveAt(groups.Count - 1);
                        }
                        foreach (var v in used) c[color, v]++;
                        if (!ok) break; // longer runs from this start won't succeed either
                    }
                }
            }

            c[color, value]++;
            return false;
        }

        /// <summary>14 tiles fully arranged into sets/runs (seri).</summary>
        public static bool IsNormalWin(IList<Tile> hand14, OkeyDef o, RuleSet rules = null)
        {
            if (hand14.Count != 14) return false;
            var c = Normalize(hand14, o, out int wild);
            return SolveGroups(c, wild, rules ?? RuleSet.DuzOkeyDefault, new List<Group>());
        }

        /// <summary>14 tiles as 7 identical pairs (çift). Wildcards pair with anything.</summary>
        public static bool IsPairsWin(IList<Tile> hand14, OkeyDef o)
        {
            if (hand14.Count != 14) return false;
            var c = Normalize(hand14, o, out int wild);
            int singles = 0;
            for (int col = 0; col < 4; col++) for (int v = 1; v <= 13; v++) singles += c[col, v] % 2;
            return singles <= wild && (wild - singles) % 2 == 0;
        }

        /// <summary>Given a 15-tile hand, find the best winning discard (okey discard preferred).</summary>
        public static Finish FindWinningDiscard(IList<Tile> hand15, OkeyDef o, RuleSet rules = null)
        {
            rules = rules ?? RuleSet.DuzOkeyDefault;
            Finish best = null;
            Func<Finish, int> score = f => (f.Type == FinishType.Pairs ? 2 : 1) * (f.OkeyDiscard ? 2 : 1);
            for (int i = 0; i < hand15.Count; i++)
            {
                var rest = new List<Tile>(hand15);
                rest.RemoveAt(i);
                bool okeyDiscard = IsOkeyTile(hand15[i], o);
                var candidates = new List<FinishType>();
                if (IsPairsWin(rest, o)) candidates.Add(FinishType.Pairs);
                if (IsNormalWin(rest, o, rules)) candidates.Add(FinishType.Normal);
                foreach (var type in candidates)
                {
                    var f = new Finish { DiscardIndex = i, Type = type, OkeyDiscard = okeyDiscard };
                    if (best == null || score(f) > score(best)) best = f;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ scoring (düşmeli)
        public static int FinishPenalty(FinishType type, bool okeyDiscard, RuleSet r = null)
        {
            r = r ?? RuleSet.DuzOkeyDefault;
            int b = type == FinishType.Pairs ? r.PenaltyPairs : r.PenaltyNormal;
            return okeyDiscard ? b * r.OkeyDiscardMultiplier : b;
        }

        /// <summary>Kara Çetele league points: the winner gains; everyone else loses in proportion to the points they lost.</summary>
        public static int LpDelta(int pointsLost, bool isWinner, int gamesPlayed, bool quit, LpConfig cfg = null)
        {
            cfg = cfg ?? LpConfig.Default;
            if (isWinner && !quit) return cfg.WinGain;
            int loss = Js.Round(pointsLost * cfg.LossPerPoint);
            if (quit) loss = Js.Round(Math.Max(loss, 20 * cfg.LossPerPoint) * cfg.QuitMultiplier);
            if (gamesPlayed < cfg.NewbieGames) loss = Js.Round(loss / 2.0);
            return -loss;
        }

        public static string ColorNameTr(string c) => c == "R" ? "Kırmızı" : c == "Y" ? "Sarı" : c == "B" ? "Mavi" : c == "K" ? "Siyah" : "";

        internal static int ColorOrder(string c) => ColorIndex(c);
        internal static IEnumerable<int> Range(int n) => Enumerable.Range(0, n);
    }
}
