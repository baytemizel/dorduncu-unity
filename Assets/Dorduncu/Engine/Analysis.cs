using System;
using System.Collections.Generic;
using System.Text;

namespace Dorduncu.Engine
{
    public sealed class Arrangement
    {
        public List<List<Tile>> Groups;  // arranged groups (actual tiles)
        public List<Tile> Loose;         // tiles not in any group
        public int LooseCount;
    }

    /// <summary>Hand analysis — best arrangement into runs/sets, loose-tile count, pairs. Used by bots, Seri/Çift Diz and hints.</summary>
    public static class Analysis
    {
        // identity: -1 = wildcard (real okey), else color*16 + value (fake okey = the okey's identity)
        private static int Identity(Tile t, OkeyDef o)
        {
            if (Core.IsOkeyTile(t, o)) return -1;
            if (t.Fake) return Core.ColorOrder(o.Color) * 16 + o.Value;
            return Core.ColorOrder(t.Color) * 16 + t.Value.Value;
        }

        private sealed class Plan { public List<int> Slots; }   // slot: identity or -1 (wildcard)
        private sealed class Res { public int Loose; public List<Plan> Plans; }

        private static Res Search(int[,] c, int wild, RuleSet rules, Dictionary<string, Res> memo)
        {
            int color = -1, value = 0;
            for (int v = 1; v <= 13 && color < 0; v++)
                for (int col = 0; col < 4; col++)
                    if (c[col, v] > 0) { color = col; value = v; break; }
            if (color < 0) return new Res { Loose = 0, Plans = new List<Plan>() };
            var sb = new StringBuilder(64);
            for (int col = 0; col < 4; col++)
            {
                if (col > 0) sb.Append('|');
                for (int v = 1; v <= 13; v++) sb.Append(c[col, v]);
            }
            sb.Append('#').Append(wild);
            string key = sb.ToString();
            if (memo.TryGetValue(key, out var hit)) return hit;

            int first = color * 16 + value;
            c[color, value]--;
            // Option 0: leave `first` loose
            var r0 = Search(c, wild, rules, memo);
            var best = new Res { Loose = r0.Loose + 1, Plans = r0.Plans };

            // Option A: sets
            var others = new List<int>();
            for (int col = 0; col < 4; col++) if (col != color && c[col, value] > 0) others.Add(col);
            var subsets = new List<List<int>> { new List<int>() };
            foreach (var col in others)
            {
                int n = subsets.Count;
                for (int k = 0; k < n; k++) subsets.Add(new List<int>(subsets[k]) { col });
            }
            foreach (var sub in subsets)
            {
                for (int j = 0; j <= wild; j++)
                {
                    int size = 1 + sub.Count + j;
                    if (size < 3 || size > 4) continue;
                    foreach (var col in sub) c[col, value]--;
                    var r = Search(c, wild - j, rules, memo);
                    if (r.Loose < best.Loose)
                    {
                        var slots = new List<int> { first };
                        foreach (var col in sub) slots.Add(col * 16 + value);
                        for (int k = 0; k < j; k++) slots.Add(-1);
                        var plans = new List<Plan> { new Plan { Slots = slots } };
                        plans.AddRange(r.Plans);
                        best = new Res { Loose = r.Loose, Plans = plans };
                    }
                    foreach (var col in sub) c[col, value]++;
                    if (best.Loose == 0) break;
                }
            }

            // Option B: runs
            int maxPos = rules.AllowThirteenOne ? 14 : 13;
            int[] positions = value == 1 && rules.AllowThirteenOne ? new[] { 1, 14 } : new[] { value };
            foreach (int pos in positions)
            {
                int minStart = pos == 14 ? 2 : Math.Max(1, pos - wild);
                for (int start = minStart; start <= pos; start++)
                {
                    for (int end = Math.Max(pos, start + 2); end <= maxPos; end++)
                    {
                        if (pos != 14 && end == 14 && start <= 1) continue;
                        var used = new List<int>();
                        var slots = new List<int>();
                        int j = 0;
                        bool ok = true;
                        for (int p = start; p <= end; p++)
                        {
                            if (p == pos) { slots.Add(first); continue; }
                            int v = p == 14 ? 1 : p;
                            if (c[color, v] > 0) { c[color, v]--; used.Add(v); slots.Add(color * 16 + v); }
                            else if (j < wild) { j++; slots.Add(-1); }
                            else { ok = false; break; }
                        }
                        if (ok)
                        {
                            var r = Search(c, wild - j, rules, memo);
                            if (r.Loose < best.Loose)
                            {
                                var plans = new List<Plan> { new Plan { Slots = slots } };
                                plans.AddRange(r.Plans);
                                best = new Res { Loose = r.Loose, Plans = plans };
                            }
                        }
                        foreach (var v in used) c[color, v]++;
                        if (!ok) break;
                    }
                }
            }
            c[color, value]++;
            memo[key] = best;
            return best;
        }

        public static Arrangement Arrange(IList<Tile> hand, OkeyDef o, RuleSet rules = null)
        {
            rules = rules ?? RuleSet.DuzOkeyDefault;
            var c = new int[4, 15];
            int wild = 0;
            foreach (var t in hand)
            {
                int id = Identity(t, o);
                if (id < 0) wild++; else c[id / 16, id % 16]++;
            }
            var res = Search(c, wild, rules, new Dictionary<string, Res>());
            var pool = new List<Tile>(hand);
            Func<int, Tile> take = k =>
            {
                int i = pool.FindIndex(t => Identity(t, o) == k);
                var x = pool[i];
                pool.RemoveAt(i);
                return x;
            };
            var groups = new List<List<Tile>>();
            foreach (var g in res.Plans)
            {
                var tiles = new List<Tile>();
                foreach (var k in g.Slots) tiles.Add(take(k));
                groups.Add(tiles);
            }
            var leftoverWild = pool.FindAll(t => Identity(t, o) < 0);
            var loose = pool.FindAll(t => Identity(t, o) >= 0);
            if (groups.Count > 0 && leftoverWild.Count > 0) groups[0].AddRange(leftoverWild);
            else loose.AddRange(leftoverWild);
            return new Arrangement { Groups = groups, Loose = loose, LooseCount = loose.Count };
        }

        public static int LooseCount(IList<Tile> hand, OkeyDef o, RuleSet rules = null) => Arrange(hand, o, rules).LooseCount;

        /// <summary>Complete pairs achievable (wildcards pair with singles) and the remaining singles.</summary>
        public static int PairsInfo(IList<Tile> hand, OkeyDef o, out List<Tile> singlesOut)
        {
            var order = new List<int>();
            var m = new Dictionary<int, List<Tile>>();
            int wild = 0;
            foreach (var t in hand)
            {
                int id = Identity(t, o);
                if (id < 0) { wild++; continue; }
                if (!m.TryGetValue(id, out var l)) { l = new List<Tile>(); m[id] = l; order.Add(id); }
                l.Add(t);
            }
            int pairs = 0;
            var singles = new List<Tile>();
            foreach (var id in order)
            {
                var arr = m[id];
                pairs += arr.Count / 2;
                if (arr.Count % 2 == 1) singles.Add(arr[0]);
            }
            int usedW = Math.Min(wild, singles.Count);
            pairs += usedW + (wild - usedW) / 2;
            singlesOut = singles.GetRange(usedW, singles.Count - usedW);
            return pairs;
        }

        public static int PairsCount(IList<Tile> hand, OkeyDef o) => PairsInfo(hand, o, out _);

        /// <summary>Auto-arrange for the ıstaka: groups first, loose tiles sorted after.</summary>
        public static List<List<Tile>> AutoArrangeRuns(IList<Tile> hand, OkeyDef o, RuleSet rules = null)
        {
            var a = Arrange(hand, o, rules);
            var loose = Js.StableSort(a.Loose, (x, y) => SortKey(x).CompareTo(SortKey(y)));
            var outList = new List<List<Tile>>(a.Groups);
            if (loose.Count > 0) outList.Add(loose);
            return outList;
        }

        private static int SortKey(Tile t) => t.Fake ? 99 : Core.ColorOrder(t.Color) * 20 + t.Value.Value;

        public static List<List<Tile>> AutoArrangePairs(IList<Tile> hand, OkeyDef o)
        {
            var m = new SortedDictionary<string, List<Tile>>(StringComparer.Ordinal);
            var wild = new List<Tile>();
            foreach (var t in hand)
            {
                int id = Identity(t, o);
                if (id < 0) { wild.Add(t); continue; }
                string k = Core.Colors[id / 16] + (id % 16).ToString("00");
                if (!m.TryGetValue(k, out var l)) { l = new List<Tile>(); m[k] = l; }
                l.Add(t);
            }
            var pairs = new List<List<Tile>>();
            var singles = new List<Tile>();
            foreach (var kv in m)
            {
                var arr = new List<Tile>(kv.Value);
                while (arr.Count >= 2) { pairs.Add(arr.GetRange(0, 2)); arr.RemoveRange(0, 2); }
                if (arr.Count > 0) singles.Add(arr[0]);
            }
            while (wild.Count > 0 && singles.Count > 0)
            {
                pairs.Add(new List<Tile> { singles[0], wild[0] });
                singles.RemoveAt(0); wild.RemoveAt(0);
            }
            if (wild.Count >= 2) { pairs.Add(wild.GetRange(0, 2)); wild.RemoveRange(0, 2); }
            if (singles.Count > 0 || wild.Count > 0)
            {
                var rest = new List<Tile>(singles);
                rest.AddRange(wild);
                pairs.Add(rest);
            }
            return pairs;
        }
    }
}
