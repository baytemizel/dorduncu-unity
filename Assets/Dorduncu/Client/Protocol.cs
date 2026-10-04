// Server → client messages of a table (same shapes as the original protocol package) and their JSON mapping.
using System.Collections.Generic;
using System.Linq;
using Dorduncu.Engine;

namespace Dorduncu.Client
{
    public sealed class SeatInfo { public int Seat; public string Name; public int Avatar; public bool Bot; public bool Connected; }
    public sealed class TurnInfo { public int Seat; public long Deadline; public string Phase; }
    public sealed class ChatLine { public int Seat; public string Name; public string Text; public long Ts; }
    public sealed class Emote { public int Seat; public string Id; }
    public sealed class MatchResult { public int[] Ranking; public int[] Points; public int[] LpDelta; public List<SeatInfo> Seats; }
    public sealed class TableError { public string Code; public string Message; }

    public static class Protocol
    {
        public static readonly string[] Emotes = { "wave", "laugh", "wow", "think", "clap", "angry", "tea", "gg" };
        public static readonly string[] ReportReasons = { "chat_abuse", "cheating", "afk", "name", "other" };

        // ---------------------------------------------------------------- JSON → objects
        public static Tile Tile(object o)
        {
            var d = J.Obj(o);
            if (d == null) return null;
            return new Tile(d.I("id"), d.S("color"), d.NI("value"), d.B("fake"));
        }

        public static List<Tile> Tiles(object o) => J.Arr(o).Select(Tile).ToList();
        public static int[] Ints(object o) => J.Arr(o).Select(x => x is long l ? (int)l : 0).ToArray();

        public static OkeyDef Okey(object o)
        {
            var d = J.Obj(o);
            return d == null ? null : new OkeyDef(d.S("color"), d.I("value"));
        }

        public static RuleSet Rules(object o)
        {
            var d = J.Obj(o);
            if (d == null) return RuleSet.DuzOkeyDefault;
            return new RuleSet
            {
                AllowThirteenOne = d.B("allowThirteenOne"), StartPoints = d.I("startPoints", 20), PenaltyIndicator = d.I("penaltyIndicator", 1),
                PenaltyNormal = d.I("penaltyNormal", 2), PenaltyPairs = d.I("penaltyPairs", 4), OkeyDiscardMultiplier = d.I("okeyDiscardMultiplier", 2),
            };
        }

        public static SeatView View(object o)
        {
            var d = J.Obj(o);
            if (d == null) return null;
            return new SeatView
            {
                Seat = d.I("seat"), Players = d.I("players", 4), Rules = Rules(d.Get("rules")), Phase = d.S("phase"), Turn = d.I("turn"),
                Starter = d.I("starter"), HandNo = d.I("handNo"), Points = Ints(d.Get("points")), WallCount = d.I("wallCount"),
                Indicator = Tile(d.Get("indicator")), Okey = Okey(d.Get("okey")), SeedCommit = d.S("seedCommit"), Hand = Tiles(d.Get("hand")),
                HandCounts = Ints(d.Get("handCounts")), Discards = d.A("discards").Select(Tiles).ToList(),
                ShowedIndicator = d.A("showedIndicator").Select(x => x is bool b && b).ToArray(),
                AvailableDiscard = Tile(d.Get("availableDiscard")), CanShowIndicator = d.B("canShowIndicator"),
            };
        }

        public static GameEvent Event(object o)
        {
            var d = J.Obj(o);
            return new GameEvent
            {
                Type = d.S("type"), Seat = d.I("seat"), Hand = d.I("hand"), Starter = d.I("starter"), Indicator = Tile(d.Get("indicator")),
                Okey = Okey(d.Get("okey")), SeedCommit = d.S("seedCommit"), Penalty = d.I("penalty"), From = d.S("from"), Tile = Tile(d.Get("tile")),
                FinishType = d.S("finishType"), OkeyDiscard = d.B("okeyDiscard"), ServerSeed = d.S("serverSeed"), ClientSeed = d.S("clientSeed"),
                Points = d.Has("points") ? Ints(d.Get("points")) : null, Ranking = d.Has("ranking") ? Ints(d.Get("ranking")) : null,
            };
        }

        public static List<SeatInfo> Seats(object o) => J.Arr(o).Select(x =>
        {
            var d = J.Obj(x);
            return new SeatInfo { Seat = d.I("seat"), Name = d.S("name"), Avatar = d.I("avatar"), Bot = d.B("bot"), Connected = d.B("connected") };
        }).ToList();

        public static TurnInfo Turn(object o)
        {
            var d = J.Obj(o);
            return d == null ? null : new TurnInfo { Seat = d.I("seat"), Deadline = d.L("deadline"), Phase = d.S("phase") };
        }

        public static ChatLine Chat(object o)
        {
            var d = J.Obj(o);
            return new ChatLine { Seat = d.I("seat"), Name = d.S("name"), Text = d.S("text"), Ts = d.L("ts") };
        }

        public static MatchResult Result(object o)
        {
            var d = J.Obj(o);
            if (d == null) return null;
            return new MatchResult { Ranking = Ints(d.Get("ranking")), Points = Ints(d.Get("points")), LpDelta = Ints(d.Get("lpDelta")), Seats = Seats(d.Get("seats")) };
        }
    }
}
