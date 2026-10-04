// Dördüncü engine — C# port of packages/engine (TypeScript). Pure C#: no UnityEngine references, so it also builds and is
// tested with plain .NET. A port is correct when it reproduces every golden vector (Assets/Dorduncu/Tests/EditMode/Golden).
using System;
using System.Collections.Generic;

namespace Dorduncu.Engine
{
    /// <summary>One tile. Color "R" | "Y" | "B" | "K" (kırmızı, sarı, mavi, siyah); null (and Value null) only for the fake okey.</summary>
    [Serializable]
    public sealed class Tile
    {
        public int Id;          // 0..105, unique within a deck
        public string Color;    // null only for fake okey (sahte okey)
        public int? Value;      // 1..13, null only for fake okey
        public bool Fake;

        public Tile() { }
        public Tile(int id, string color, int? value, bool fake) { Id = id; Color = color; Value = value; Fake = fake; }

        public Tile Clone() => new Tile(Id, Color, Value, Fake);
        public override string ToString() => Fake ? "F" : Color + Value;
    }

    /// <summary>The okey (wildcard) of a hand: the tile one above the indicator, same color (13 → 1).</summary>
    [Serializable]
    public sealed class OkeyDef
    {
        public string Color;
        public int Value;

        public OkeyDef() { }
        public OkeyDef(string color, int value) { Color = color; Value = value; }
        public override string ToString() => Color + Value;
    }

    [Serializable]
    public sealed class RuleSet
    {
        public bool AllowThirteenOne = true;   // 12-13-1 valid (düz okey: true; 101: false)
        public int StartPoints = 20;           // düşmeli: 10 / 20 / 30
        public int PenaltyIndicator = 1;       // gösterge
        public int PenaltyNormal = 2;          // normal bitiş
        public int PenaltyPairs = 4;           // çiftten bitiş
        public int OkeyDiscardMultiplier = 2;  // okey atarak bitişte çarpan

        public static RuleSet DuzOkeyDefault => new RuleSet();
    }

    public enum FinishType { Normal, Pairs }

    public sealed class Finish
    {
        public int DiscardIndex;
        public FinishType Type;
        public bool OkeyDiscard;
    }

    public sealed class LpConfig
    {
        public int WinGain = 30;
        public double LossPerPoint = 1.5;
        public int NewbieGames = 10;
        public double QuitMultiplier = 1.5;
        public static readonly LpConfig Default = new LpConfig();
    }

    internal static class Js
    {
        /// <summary>JavaScript Math.round (halves round towards +∞).</summary>
        public static int Round(double x) => (int)Math.Floor(x + 0.5);

        /// <summary>Stable sort (Array.prototype.sort is stable; List.Sort is not).</summary>
        public static List<T> StableSort<T>(IEnumerable<T> items, Comparison<T> cmp)
        {
            var list = new List<T>(items);
            var idx = new List<KeyValuePair<int, T>>(list.Count);
            for (int i = 0; i < list.Count; i++) idx.Add(new KeyValuePair<int, T>(i, list[i]));
            idx.Sort((a, b) => { int c = cmp(a.Value, b.Value); return c != 0 ? c : a.Key.CompareTo(b.Key); });
            var outList = new List<T>(idx.Count);
            foreach (var kv in idx) outList.Add(kv.Value);
            return outList;
        }
    }
}
