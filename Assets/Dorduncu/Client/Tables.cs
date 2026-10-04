// A table the UI plays at. Online (REST room on the Laravel API) and offline (engine + bots on the device) implement the same
// contract and raise exactly the same messages, so the UI never needs to know which one it is talking to.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Dorduncu.Engine;

namespace Dorduncu.Client
{
    public interface ITableAdapter
    {
        string Mode { get; }   // "online" | "offline"
        event Action<List<SeatInfo>> Seats;
        event Action<SeatView> View;
        event Action<GameEvent> Event;
        event Action<TurnInfo> Turn;
        event Action<ChatLine> Chat;
        event Action<Emote> EmoteShown;
        event Action<TableError> Error;
        event Action<MatchResult> Result;
        event Action<int> Reported;
        event Action<string> Closed;
        event Action<bool> Connection;   // online only: false = dropped (retrying), true = back
        void Send(string type, Dictionary<string, object> payload);
        void Leave();
    }

    public abstract class TableBase : ITableAdapter
    {
        public abstract string Mode { get; }
        public event Action<List<SeatInfo>> Seats;
        public event Action<SeatView> View;
        public event Action<GameEvent> Event;
        public event Action<TurnInfo> Turn;
        public event Action<ChatLine> Chat;
        public event Action<Emote> EmoteShown;
        public event Action<TableError> Error;
        public event Action<MatchResult> Result;
        public event Action<int> Reported;
        public event Action<string> Closed;
        public event Action<bool> Connection;
        public abstract void Send(string type, Dictionary<string, object> payload);
        public abstract void Leave();

        protected void RaiseSeats(List<SeatInfo> s) => Seats?.Invoke(s);
        protected void RaiseView(SeatView v) => View?.Invoke(v);
        protected void RaiseEvent(GameEvent e) => Event?.Invoke(e);
        protected void RaiseTurn(TurnInfo t) => Turn?.Invoke(t);
        protected void RaiseChat(ChatLine c) => Chat?.Invoke(c);
        protected void RaiseEmote(Emote e) => EmoteShown?.Invoke(e);
        protected void RaiseError(string code, string msg) => Error?.Invoke(new TableError { Code = code, Message = msg });
        protected void RaiseResult(MatchResult r) => Result?.Invoke(r);
        protected void RaiseReported(int seat) => Reported?.Invoke(seat);
        protected void RaiseClosed(string reason) => Closed?.Invoke(reason);
        protected void RaiseConnection(bool up) => Connection?.Invoke(up);
    }

    public sealed class OfflineOptions
    {
        public string Name = "Misafir";
        public int Avatar = 2;
        public int BotLevel = 3;
        public string[] BotNames = { "Kerem", "Hüsnü Usta", "Derya" };
        public int[] BotAvatars = { 3, 0, 1 };
        public Func<string, int> BotDelayMs;     // phase → ms (human-like pacing); 0 in tests
        public int NextHandMs = 3500;
        public int TurnMs = 30000;
        public Func<string> Seed;                // deterministic tests
    }

    /// <summary>Offline table: the full game on the device against bots (port of client-core/offline.ts).</summary>
    public sealed class OfflineTable : TableBase
    {
        public override string Mode => "offline";
        public const int Me = 0;
        private readonly OkeyGame _game = new OkeyGame(4);
        private readonly PickupTracker _tracker = new PickupTracker(4);
        private readonly List<Bot> _bots;
        private readonly List<SeatInfo> _seats;
        private readonly OfflineOptions _o;
        private readonly IScheduler _clock;
        private ITimer _timer;
        private bool _closed;
        private static readonly Random R = new Random();

        public OfflineTable(OfflineOptions o, IScheduler clock)
        {
            _o = o;
            _clock = clock;
            _o.BotDelayMs = _o.BotDelayMs ?? (p => (p == "draw" ? 600 : 900) + R.Next(900));
            _o.Seed = _o.Seed ?? RandomSeed;
            _bots = Enumerable.Range(0, 4).Select(_ => new Bot(o.BotLevel)).ToList();
            _seats = Enumerable.Range(0, 4).Select(s => s == Me
                ? new SeatInfo { Seat = s, Name = o.Name, Avatar = o.Avatar, Bot = false, Connected = true }
                : new SeatInfo { Seat = s, Name = o.BotNames[s - 1], Avatar = o.BotAvatars[s - 1], Bot = true, Connected = true }).ToList();
            _game.OnEvent += OnGameEvent;
        }

        public static string RandomSeed()
        {
            var b = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return SeededRng.Hex(b);
        }

        /// <summary>Call once the UI has subscribed.</summary>
        public void Start() { RaiseSeats(_seats); StartHand(); }

        public override void Send(string type, Dictionary<string, object> p)
        {
            if (_closed) return;
            try
            {
                switch (type)
                {
                    case "chat": case "emote": case "report": return;     // nobody to talk to offline
                    case "draw": _game.Draw(Me, p.S("from")); break;
                    case "discard": _game.Discard(Me, p.I("tileId", -1)); break;
                    case "finish": _game.Finish(Me, p.I("tileId", -1)); break;
                    case "indicator": _game.ShowIndicator(Me); break;
                    default: RaiseError("unknown_message", "unknown message " + type); return;
                }
                AfterChange();
            }
            catch (GameError e) { RaiseError(e.Code, e.Message); }
        }

        public override void Leave() { if (_closed) return; _closed = true; _timer?.Cancel(); RaiseClosed("left"); }

        private void StartHand()
        {
            _game.StartHand(_o.Seed(), "offline");
            for (int s = 1; s < 4; s++) if (_game.CanShowIndicator(s) && _bots[s].WantsIndicator(true)) _game.ShowIndicator(s);
            AfterChange();
        }

        private void OnGameEvent(GameEvent e)
        {
            _tracker.Handle(e);
            if (e.Type == "draw" && e.From == "wall" && e.Seat != Me) { var c = e.Clone(); c.Tile = null; RaiseEvent(c); }
            else RaiseEvent(e);
        }

        private void AfterChange()
        {
            _timer?.Cancel(); _timer = null;
            if (_closed) return;
            if (_game.HandNo > 0) RaiseView(_game.View(Me));
            if (_game.Phase == Phases.MatchOver) { Finish(); return; }
            if (_game.Phase == Phases.HandOver) { _timer = _clock.After(_o.NextHandMs, StartHand); return; }
            RaiseTurn(new TurnInfo { Seat = _game.Turn, Deadline = _clock.NowMs + _o.TurnMs, Phase = _game.Phase });
            if (_game.Turn != Me) _timer = _clock.After(_o.BotDelayMs(_game.Phase), BotStep);
        }

        private void BotStep()
        {
            int s = _game.Turn;
            var v = BotView.For(_game, s, _tracker.Pickups);
            if (_game.Phase == Phases.Draw) _game.Draw(s, _bots[s].DecideDraw(v));
            else { var d = _bots[s].DecideDiscard(v); if (d.Finish) _game.Finish(s, d.TileId); else _game.Discard(s, d.TileId); }
            AfterChange();
        }

        private void Finish()
        {
            RaiseResult(new MatchResult { Ranking = _game.Ranking(), Points = (int[])_game.Points.Clone(), LpDelta = new int[4], Seats = _seats });
            _closed = true;
        }
    }

    /// <summary>
    /// Online table: a REST room of the Laravel API polled about once a second (`GET /v1/rooms/{id}?since=`). Moves are POSTed;
    /// each response carries the latest view and the messages since the last one, applied in order.
    /// </summary>
    public sealed class OnlineTable : TableBase
    {
        public override string Mode => "online";
        public string RoomId { get; private set; }
        public int MySeat { get; private set; } = -1;
        public int PollMs = 900;
        /// <summary>server clock − local clock (turn deadlines are converted to the local clock).</summary>
        public long ServerOffsetMs { get; private set; }
        private readonly ApiClient _api;
        private readonly IScheduler _clock;
        private long _lastSeq;
        private ITimer _poll;
        private bool _closed, _dropped, _resultSent, _inFlight;
        private string _seatsKey = "";
        private int _failures;

        public OnlineTable(ApiClient api, IScheduler clock) { _api = api; _clock = clock; }

        /// <summary>Join with a seat ticket (from /v1/play or a tournament seat). `ready` reports success or the error.</summary>
        public void Join(string ticket, string clientSeed, Action<ApiResult> ready)
        {
            _api.JoinRoom(ticket, clientSeed, r =>
            {
                if (r.Ok) { RoomId = r.Data.S("roomId"); _lastSeq = 0; Apply(r.Data); SchedulePoll(0); }
                ready(r);
            });
        }

        public override void Send(string type, Dictionary<string, object> payload)
        {
            if (_closed || RoomId == null) return;
            _inFlight = true;
            _api.Act(RoomId, type, payload, _lastSeq, r =>
            {
                _inFlight = false;
                if (r.Ok) Apply(r.Data);
                else if (r.Status == 0) RaiseError("network", "bağlantı yok");
                else RaiseError(r.ErrorCode, r.ErrorMessage);
                SchedulePoll(PollMs);
            });
        }

        public override void Leave()
        {
            if (_closed) return;
            _closed = true;
            _poll?.Cancel();
            if (RoomId != null) _api.LeaveRoom(RoomId, _ => RaiseClosed("left"));
            else RaiseClosed("left");
        }

        private void SchedulePoll(int ms)
        {
            _poll?.Cancel();
            if (_closed) return;
            _poll = _clock.After(ms, Poll);
        }

        private void Poll()
        {
            if (_closed) return;
            if (_inFlight) { SchedulePoll(PollMs); return; }
            _api.PollRoom(RoomId, _lastSeq, r =>
            {
                if (_closed) return;
                if (r.Ok)
                {
                    _failures = 0;
                    if (_dropped) { _dropped = false; RaiseConnection(true); }
                    Apply(r.Data);
                }
                else if (r.Status == 0 || r.Status >= 500 || r.Status == 429)
                {
                    _failures++;
                    if (!_dropped) { _dropped = true; RaiseConnection(false); }
                }
                else { RaiseError(r.ErrorCode, r.ErrorMessage); if (r.Status == 403 || r.Status == 404) { _closed = true; RaiseClosed("lost"); return; } }
                if (!_closed) SchedulePoll(_dropped ? Math.Min(8000, PollMs * (1 << Math.Min(_failures, 4))) : PollMs);
            });
        }

        private void Apply(Dictionary<string, object> d)
        {
            if (d.Has("seat")) MySeat = d.I("seat");
            var seatsObj = d.Get("seats");
            var key = Json.Serialize(seatsObj);
            if (key != _seatsKey) { _seatsKey = key; RaiseSeats(Protocol.Seats(seatsObj)); }
            foreach (var mo in d.A("messages"))
            {
                var m = J.Obj(mo);
                long seq = m.L("seq");
                if (seq <= _lastSeq) continue;
                _lastSeq = seq;
                var data = m.Get("data");
                switch (m.S("type"))
                {
                    case "event": RaiseEvent(Protocol.Event(data)); break;
                    case "chat": RaiseChat(Protocol.Chat(data)); break;
                    case "emote": { var e = J.Obj(data); RaiseEmote(new Emote { Seat = e.I("seat"), Id = e.S("id") }); break; }
                    case "reported": RaiseReported(J.Obj(data).I("seat")); break;
                }
            }
            _lastSeq = Math.Max(_lastSeq, d.L("lastSeq"));
            if (d.Has("view")) RaiseView(Protocol.View(d.Get("view")));
            if (d.Has("serverTime")) ServerOffsetMs = d.L("serverTime") - _clock.NowMs;
            var turn = Protocol.Turn(d.Get("turn"));
            if (turn != null) { turn.Deadline -= ServerOffsetMs; RaiseTurn(turn); }   // deadline on the local clock
            if (d.Has("result") && !_resultSent) { _resultSent = true; RaiseResult(Protocol.Result(d.Get("result"))); }
            if (d.S("status") == "finished")
            {
                _closed = true;
                _poll?.Cancel();
                if (!_resultSent) RaiseClosed("abandoned");
            }
        }
    }
}
