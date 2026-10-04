using System;
using System.Collections.Generic;
using System.Linq;

namespace Dorduncu.Engine
{
    /// <summary>Every rejected action throws a GameError with a stable machine-readable code (same codes as the server).</summary>
    public sealed class GameError : Exception
    {
        public readonly string Code;
        public GameError(string code, string message) : base(message) { Code = code; }
    }

    public static class Phases
    {
        public const string Draw = "draw", Discard = "discard", HandOver = "handOver", MatchOver = "matchOver";
    }

    /// <summary>
    /// Public game event (animations). Same shape as the server's JSON events:
    /// handStart {hand, starter, indicator, okey, seedCommit} · indicator {seat, penalty} · draw {seat, from, tile}
    /// · discard {seat, tile} · finish {seat, tile, finishType, okeyDiscard, penalty} · handDraw · handEnd {serverSeed, clientSeed, points}
    /// · matchEnd {ranking, points}. A tile drawn from the wall has Tile = null for everyone but the drawer.
    /// </summary>
    [Serializable]
    public sealed class GameEvent
    {
        public string Type;
        public int Seat;
        public int Hand;
        public int Starter;
        public Tile Indicator;
        public OkeyDef Okey;
        public string SeedCommit;
        public int Penalty;
        public string From;
        public Tile Tile;
        public string FinishType;   // "normal" | "pairs"
        public bool OkeyDiscard;
        public string ServerSeed;
        public string ClientSeed;
        public int[] Points;
        public int[] Ranking;

        public GameEvent Clone() => (GameEvent)MemberwiseClone();
    }

    /// <summary>What one seat may see (identical to the server's per-seat view).</summary>
    [Serializable]
    public sealed class SeatView
    {
        public int Seat;
        public int Players;
        public RuleSet Rules;
        public string Phase;
        public int Turn;
        public int Starter;
        public int HandNo;
        public int[] Points;
        public int WallCount;
        public Tile Indicator;
        public OkeyDef Okey;
        public string SeedCommit;
        public List<Tile> Hand;
        public int[] HandCounts;
        public List<List<Tile>> Discards;
        public bool[] ShowedIndicator;
        public Tile AvailableDiscard;
        public bool CanShowIndicator;
    }

    public sealed class SeatState
    {
        public List<Tile> Hand = new List<Tile>();
        public List<Tile> Discards = new List<Tile>();  // tiles this seat has thrown (top = last)
        public bool Acted;                              // has made any move this hand (gösterge window)
        public bool ShowedIndicator;
    }

    /// <summary>Authoritative turn state machine (port of packages/engine/src/game.ts). Offline play runs it on the device.</summary>
    public sealed class OkeyGame
    {
        public readonly int Players;
        public readonly RuleSet Rules;
        public List<SeatState> Seats = new List<SeatState>();
        public List<Tile> Wall = new List<Tile>();
        public Tile Indicator;
        public OkeyDef Okey;
        public int Turn;
        public string Phase = Phases.HandOver;
        public int Starter;
        public int HandNo;
        public int[] Points;
        public string SeedCommit = "";
        private string _serverSeed = "", _clientSeed = "";

        public event Action<GameEvent> OnEvent;

        public OkeyGame(int players = 4, RuleSet rules = null)
        {
            if (players < 2 || players > 4) throw new ArgumentException("players must be 2..4");
            Players = players;
            Rules = rules ?? RuleSet.DuzOkeyDefault;
            Points = Enumerable.Repeat(Rules.StartPoints, players).ToArray();
        }

        private void Emit(GameEvent e) => OnEvent?.Invoke(e);

        /// <summary>Start a new hand. serverSeed stays secret until handEnd; its commit is published immediately.</summary>
        public void StartHand(string serverSeed, string clientSeed)
        {
            if (Phase != Phases.HandOver) throw new GameError("hand_in_progress", Phase == Phases.MatchOver ? "match is over" : "hand in progress");
            if (serverSeed == null || serverSeed.Length < 16) throw new GameError("bad_seed", "server seed must be ≥16 chars");
            if (clientSeed == null || clientSeed.Length > 256) throw new GameError("bad_seed", "client seed must be a string ≤256 chars");
            HandNo++;
            if (HandNo > 1) Starter = (Starter + 1) % Players;
            _serverSeed = serverSeed;
            _clientSeed = clientSeed;
            SeedCommit = Core.CommitOf(serverSeed);
            var d = Core.Deal(Core.Shuffle(Core.CreateDeck(), serverSeed, clientSeed), Players, Starter);
            Seats = d.Hands.Select(h => new SeatState { Hand = h }).ToList();
            Wall = d.Wall;
            Indicator = d.Indicator;
            Okey = d.Okey;
            Turn = Starter;
            Phase = Phases.Discard; // starter holds 15 tiles and opens by discarding
            Emit(new GameEvent { Type = "handStart", Hand = HandNo, Starter = Starter, Indicator = d.Indicator, Okey = d.Okey, SeedCommit = SeedCommit });
        }

        public int PrevSeat(int seat) => (seat - 1 + Players) % Players;
        public int NextSeat(int seat) => (seat + 1) % Players;

        /// <summary>The discard a seat may pick up: top of the previous seat's pile.</summary>
        public Tile AvailableDiscard(int seat)
        {
            if (Seats.Count == 0) return null;
            var pile = Seats[PrevSeat(seat)].Discards;
            return pile.Count > 0 ? pile[pile.Count - 1] : null;
        }

        private void SeatIndex(int seat)
        {
            if (seat < 0 || seat >= Players) throw new GameError("bad_seat", "invalid seat " + seat);
        }

        private void AssertTurn(int seat, string phase)
        {
            SeatIndex(seat);
            if (Phase != phase) throw new GameError("bad_phase", "expected phase " + phase + ", got " + Phase);
            if (seat != Turn) throw new GameError("not_your_turn", "not seat " + seat + "'s turn");
        }

        public bool CanShowIndicator(int seat)
        {
            if (Phase != Phases.Draw && Phase != Phases.Discard) return false;
            if (seat < 0 || seat >= Players) return false;
            var s = Seats[seat];
            if (s.Acted || s.ShowedIndicator || Rules.PenaltyIndicator <= 0) return false;
            return s.Hand.Any(t => !t.Fake && t.Color == Indicator.Color && t.Value == Indicator.Value);
        }

        public void ShowIndicator(int seat)
        {
            SeatIndex(seat);
            if (!CanShowIndicator(seat)) throw new GameError("cannot_show_indicator", "cannot show indicator");
            Seats[seat].ShowedIndicator = true;
            int pen = Rules.PenaltyIndicator;
            for (int i = 0; i < Players; i++) if (i != seat) Points[i] -= pen;
            Emit(new GameEvent { Type = "indicator", Seat = seat, Penalty = pen });
            CheckMatchEndAfterPoints();
        }

        public Tile Draw(int seat, string from)
        {
            AssertTurn(seat, Phases.Draw);
            if (from != "wall" && from != "discard") throw new GameError("bad_source", "invalid draw source " + from);
            Tile tile = null;
            if (from == "wall") { if (Wall.Count > 0) { tile = Wall[0]; Wall.RemoveAt(0); } }
            else
            {
                var pile = Seats[PrevSeat(seat)].Discards;
                if (pile.Count > 0) { tile = pile[pile.Count - 1]; pile.RemoveAt(pile.Count - 1); }
            }
            if (tile == null) throw new GameError("empty_source", "nothing to draw from " + from);
            Seats[seat].Hand.Add(tile);
            Seats[seat].Acted = true;
            Phase = Phases.Discard;
            Emit(new GameEvent { Type = "draw", Seat = seat, From = from, Tile = tile });
            return tile;
        }

        private static void TileId(int tileId)
        {
            if (tileId < 0 || tileId > 105) throw new GameError("bad_tile", "invalid tile id " + tileId);
        }

        public void Discard(int seat, int tileId)
        {
            AssertTurn(seat, Phases.Discard);
            TileId(tileId);
            var s = Seats[seat];
            int i = s.Hand.FindIndex(t => t.Id == tileId);
            if (i < 0) throw new GameError("tile_not_in_hand", "tile not in hand");
            var tile = s.Hand[i];
            s.Hand.RemoveAt(i);
            s.Discards.Add(tile);
            s.Acted = true;
            Emit(new GameEvent { Type = "discard", Seat = seat, Tile = tile });
            Turn = NextSeat(seat);
            // The hand ends in a draw when the next player must draw and the wall is empty.
            if (Wall.Count == 0) { Emit(new GameEvent { Type = "handDraw" }); EndHand(); return; }
            Phase = Phases.Draw;
        }

        /// <summary>Discarding tileId must leave a winning 14-tile hand. Returns (type, okeyDiscard) or null.</summary>
        public Finish EvaluateFinish(int seat, int tileId)
        {
            if (seat < 0 || seat >= Players || (Phase != Phases.Discard && Phase != Phases.Draw)) return null;
            var s = Seats[seat];
            if (s.Hand.Count != 15) return null;
            var tile = s.Hand.Find(t => t.Id == tileId);
            if (tile == null) return null;
            var rest = s.Hand.Where(t => t.Id != tileId).ToList();
            bool okeyDiscard = Core.IsOkeyTile(tile, Okey);
            if (Core.IsPairsWin(rest, Okey)) return new Finish { Type = FinishType.Pairs, OkeyDiscard = okeyDiscard };
            if (Core.IsNormalWin(rest, Okey, Rules)) return new Finish { Type = FinishType.Normal, OkeyDiscard = okeyDiscard };
            return null;
        }

        public void Finish(int seat, int tileId)
        {
            AssertTurn(seat, Phases.Discard);
            TileId(tileId);
            var f = EvaluateFinish(seat, tileId) ?? throw new GameError("not_winning", "not a winning hand");
            var s = Seats[seat];
            var tile = s.Hand.Find(t => t.Id == tileId);
            s.Hand.Remove(tile);
            s.Discards.Add(tile); // finishing tile is laid on the pile (keeps tile conservation)
            int penalty = Core.FinishPenalty(f.Type, f.OkeyDiscard, Rules);
            for (int i = 0; i < Players; i++) if (i != seat) Points[i] -= penalty;
            Emit(new GameEvent { Type = "finish", Seat = seat, Tile = tile, FinishType = f.Type == FinishType.Pairs ? "pairs" : "normal", OkeyDiscard = f.OkeyDiscard, Penalty = penalty });
            EndHand();
        }

        private void EndHand()
        {
            Phase = Phases.HandOver;
            Emit(new GameEvent { Type = "handEnd", ServerSeed = _serverSeed, ClientSeed = _clientSeed, Points = (int[])Points.Clone() });
            CheckMatchEndAfterPoints();
        }

        private void CheckMatchEndAfterPoints()
        {
            if (Points.Any(p => p <= 0))
            {
                Phase = Phases.MatchOver;
                Emit(new GameEvent { Type = "matchEnd", Ranking = Ranking(), Points = (int[])Points.Clone() });
            }
        }

        /// <summary>Seats best-first (stable: equal points keep seat order).</summary>
        public int[] Ranking() => Js.StableSort(Enumerable.Range(0, Players), (a, b) => Points[b].CompareTo(Points[a])).ToArray();

        /// <summary>Per-seat view: never contains another seat's tiles or the unrevealed seed.</summary>
        public SeatView View(int seat)
        {
            SeatIndex(seat);
            bool has = HandNo > 0;
            return new SeatView
            {
                Seat = seat, Players = Players, Rules = Rules, Phase = Phase, Turn = Turn, Starter = Starter, HandNo = HandNo,
                Points = (int[])Points.Clone(), WallCount = Wall.Count, Indicator = has ? Indicator : null, Okey = has ? Okey : null,
                SeedCommit = SeedCommit, Hand = has ? new List<Tile>(Seats[seat].Hand) : new List<Tile>(),
                HandCounts = Seats.Select(s => s.Hand.Count).ToArray(), Discards = Seats.Select(s => new List<Tile>(s.Discards)).ToList(),
                ShowedIndicator = Seats.Select(s => s.ShowedIndicator).ToArray(), AvailableDiscard = has ? AvailableDiscard(seat) : null,
                CanShowIndicator = CanShowIndicator(seat),
            };
        }

        /// <summary>Total tile count for invariant checks (must be 106).</summary>
        public int TileCount() => Wall.Count + 1 + Seats.Sum(s => s.Hand.Count + s.Discards.Count);
    }
}
