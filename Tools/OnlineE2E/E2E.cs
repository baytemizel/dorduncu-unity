using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using Dorduncu.Client;
using Dorduncu.Engine;

/// <summary>Synchronous HttpClient transport: the callback runs on the caller's (single) thread, like Unity's main thread.</summary>
sealed class SyncHttp : IHttp
{
    private static readonly HttpClient C = new HttpClient();
    public int Requests;
    public void Send(string method, string url, string body, Dictionary<string, string> headers, Action<HttpResponse> done)
    {
        Requests++;
        var req = new HttpRequestMessage(new HttpMethod(method), url);
        if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        foreach (var h in headers) if (h.Key != "Content-Type") req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        HttpResponse r;
        try { var resp = C.Send(req); r = new HttpResponse { Status = (int)resp.StatusCode, Body = resp.Content.ReadAsStringAsync().Result }; }
        catch (HttpRequestException) { r = new HttpResponse { Status = 0 }; }
        done(r);
    }
}

/// <summary>Wall-clock scheduler pumped by the main loop.</summary>
sealed class LoopClock : IScheduler
{
    private sealed class T : ITimer { public long Due; public Action Fn; public bool Dead; public void Cancel() => Dead = true; }
    private readonly List<T> _t = new List<T>();
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    public long NowMs => 1_700_000_000_000 + _sw.ElapsedMilliseconds;
    public ITimer After(int ms, Action fn) { var t = new T { Due = NowMs + ms, Fn = fn }; _t.Add(t); return t; }
    public void Pump()
    {
        _t.RemoveAll(x => x.Dead);
        foreach (var t in _t.Where(x => x.Due <= NowMs).OrderBy(x => x.Due).ToList()) { _t.Remove(t); if (!t.Dead) t.Fn(); }
    }
}

static class E2E
{
    static void Check(bool c, string what) { if (!c) throw new Exception("FAILED: " + what); Console.WriteLine("ok  " + what); }

    static ApiResult Wait(Action<Action<ApiResult>> call) { ApiResult res = null; call(r => res = r); return res; }

    static int Main(string[] args)
    {
        string baseUrl = args.Length > 0 ? args[0] : "http://127.0.0.1:8000";
        var http = new SyncHttp();
        var clock = new LoopClock();
        var api = new ApiClient(baseUrl, http, new MemoryStore());

        var login = Wait(cb => api.Guest("unity-e2e-" + Guid.NewGuid().ToString("N"), "Unity Oyuncu", cb));
        Check(login.Ok && login.Data.B("created"), "guest login creates the player");
        var me = Wait(api.Me);
        Check(me.Ok && me.Data.O("user").S("displayName") == "Unity Oyuncu", "GET /v1/me");
        var play = Wait(api.Play);
        Check(play.Ok && play.Data.S("ticket") != null, "POST /v1/play issues a seat ticket");

        var table = new OnlineTable(api, clock) { PollMs = 50 };
        var model = new TableModel(table);
        int events = 0, wallHidden = 0, errors = 0;
        table.Event += e => { events++; if (e.Type == "draw" && e.From == "wall" && e.Seat != table.MySeat && e.Tile == null) wallHidden++; };
        table.Error += e => { errors++; Console.WriteLine("   table error: " + e.Code + " " + e.Message); };
        var joined = Wait(cb => table.Join(play.Data.S("ticket"), "unity-client-seed", cb));
        Check(joined.Ok, "POST /v1/rooms/join");

        var hint = new Bot(5);
        var sw = Stopwatch.StartNew();
        int moves = 0;
        while (model.Result == null && model.ClosedReason == null && sw.Elapsed.TotalSeconds < 240)
        {
            clock.Pump();
            if (model.MyTurn)
            {
                var bv = BotView.FromSeatView(model.View);
                if (model.CanDraw) model.Draw(hint.DecideDraw(bv));
                else { var d = hint.DecideDiscard(bv); if (d.Finish) model.Finish(d.TileId); else model.Discard(d.TileId); }
                moves++;
            }
            System.Threading.Thread.Sleep(5);
        }
        Check(model.Result != null, $"match finished with a result ({moves} moves by the C# client, {events} events, {http.Requests} requests)");
        Check(model.Seats.Count == 4 && model.Seats.Count(s => s.Bot) == 3, "three bots filled the table");
        Check(wallHidden > 0, $"wall draws of the other seats arrived hidden ({wallHidden})");
        Check(errors == 0, "no rejected moves");
        Check(Enumerable.Range(0, 4).Where(i => model.Seats[i].Bot).All(i => model.Result.LpDelta[i] == 0), "bots get no league points");
        var me2 = Wait(api.Me);
        Check(me2.Data.O("user").I("gamesPlayed") == 1, "the result was recorded (gamesPlayed = 1)");
        var lb = Wait(cb => api.Leaderboard("season", 10, 0, cb));
        Check(lb.Ok && lb.Data.O("me") != null, "leaderboard shows the player");
        Console.WriteLine("ALL GREEN");
        return 0;
    }
}
