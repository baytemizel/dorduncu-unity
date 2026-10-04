// Account / game API client (port of packages/client-core/src/api.ts): keeps the session, refreshes the access token
// transparently once on 401 (rotation), exposes typed calls. Every call completes with an ApiResult on the main thread.
using System;
using System.Collections.Generic;

namespace Dorduncu.Client
{
    public sealed class ApiResult
    {
        public int Status;
        public Dictionary<string, object> Data;     // parsed JSON object (null for 204 / failures)
        public string ErrorCode;                    // server error code, "network" or "http_<status>"
        public string ErrorMessage;
        public bool Ok => Status >= 200 && Status < 300;
    }

    public sealed class ApiClient
    {
        private const string KeyAccess = "dorduncu.accessToken", KeyRefresh = "dorduncu.refreshToken";
        private readonly string _base;
        private readonly IHttp _http;
        private readonly IKeyValueStore _store;
        private readonly List<Action<bool>> _refreshWaiters = new List<Action<bool>>();
        private bool _refreshing;

        public ApiClient(string baseUrl, IHttp http, IKeyValueStore store)
        {
            _base = (baseUrl ?? "").TrimEnd('/');
            _http = http;
            _store = store;
        }

        public bool Configured => _base.Length > 0;
        public bool SignedIn => !string.IsNullOrEmpty(_store.Get(KeyRefresh));

        private void SaveSession(Dictionary<string, object> d)
        {
            _store.Set(KeyAccess, d.S("accessToken"));
            _store.Set(KeyRefresh, d.S("refreshToken"));
        }

        public void ClearSession() { _store.Set(KeyAccess, null); _store.Set(KeyRefresh, null); }

        // ---------------------------------------------------------------- auth
        public void Guest(string deviceId, string name, Action<ApiResult> done)
        {
            var body = new Dictionary<string, object> { ["deviceId"] = deviceId };
            if (!string.IsNullOrEmpty(name)) body["name"] = name;
            Login("/v1/auth/guest", body, done);
        }

        public void Social(string provider, string token, Action<ApiResult> done) =>
            Login("/v1/auth/social", new Dictionary<string, object> { ["provider"] = provider, ["token"] = token }, done);

        private void Login(string path, Dictionary<string, object> body, Action<ApiResult> done) =>
            Raw("POST", path, body, false, r => { if (r.Ok) SaveSession(r.Data); done(r); });

        public void Logout(Action<ApiResult> done)
        {
            var rt = _store.Get(KeyRefresh);
            ClearSession();
            if (rt == null) { done(new ApiResult { Status = 204 }); return; }
            Raw("POST", "/v1/auth/logout", new Dictionary<string, object> { ["refreshToken"] = rt }, false, done);
        }

        public void Link(string provider, string token, Action<ApiResult> done) =>
            Call("POST", "/v1/auth/link", new Dictionary<string, object> { ["provider"] = provider, ["token"] = token }, done);

        // ---------------------------------------------------------------- account
        public void Me(Action<ApiResult> done) => Call("GET", "/v1/me", null, done);
        public void UpdateProfile(Dictionary<string, object> p, Action<ApiResult> done) => Call("PATCH", "/v1/me", p, done);
        public void DeleteAccount(Action<ApiResult> done) => Call("DELETE", "/v1/me", null, r => { if (r.Ok) ClearSession(); done(r); });
        public void Leaderboard(string board, int limit, int offset, Action<ApiResult> done) => Call("GET", $"/v1/leaderboard/{board}?limit={limit}&offset={offset}", null, done);
        public void Play(Action<ApiResult> done) => Call("POST", "/v1/play", null, done);
        public void VerifyPurchase(Dictionary<string, object> body, Action<ApiResult> done) => Call("POST", "/v1/purchases/verify", body, done);

        // ---------------------------------------------------------------- tournaments
        public void Tournaments(Action<ApiResult> done) => Call("GET", "/v1/tournaments", null, done);
        public void Tournament(string id, Action<ApiResult> done) => Call("GET", "/v1/tournaments/" + id, null, done);
        public void RegisterTournament(string id, Action<ApiResult> done) => Call("POST", $"/v1/tournaments/{id}/register", null, done);
        public void UnregisterTournament(string id, Action<ApiResult> done) => Call("POST", $"/v1/tournaments/{id}/unregister", null, done);
        public void TournamentSeat(string id, Action<ApiResult> done) => Call("POST", $"/v1/tournaments/{id}/seat", null, done);

        // ---------------------------------------------------------------- tables
        public void JoinRoom(string ticket, string clientSeed, Action<ApiResult> done) =>
            Call("POST", "/v1/rooms/join", new Dictionary<string, object> { ["ticket"] = ticket, ["clientSeed"] = clientSeed ?? "" }, done);
        public void PollRoom(string roomId, long since, Action<ApiResult> done) => Call("GET", $"/v1/rooms/{roomId}?since={since}", null, done);
        public void Act(string roomId, string type, Dictionary<string, object> payload, long since, Action<ApiResult> done) =>
            Call("POST", $"/v1/rooms/{roomId}/actions", new Dictionary<string, object> { ["type"] = type, ["payload"] = payload ?? new Dictionary<string, object>(), ["since"] = since }, done);
        public void LeaveRoom(string roomId, Action<ApiResult> done) => Call("POST", $"/v1/rooms/{roomId}/leave", null, done);

        // ---------------------------------------------------------------- plumbing
        /// <summary>Authenticated call; one transparent refresh on 401, then the session is dropped.</summary>
        public void Call(string method, string path, Dictionary<string, object> body, Action<ApiResult> done)
        {
            Raw(method, path, body, true, r =>
            {
                if (r.Status != 401 || !SignedIn) { done(r); return; }
                Refresh(ok => { if (ok) Raw(method, path, body, true, done); else done(r); });
            });
        }

        private void Refresh(Action<bool> done)
        {
            _refreshWaiters.Add(done);
            if (_refreshing) return;
            _refreshing = true;
            var rt = _store.Get(KeyRefresh);
            Action<bool> finish = ok =>
            {
                _refreshing = false;
                var ws = new List<Action<bool>>(_refreshWaiters);
                _refreshWaiters.Clear();
                foreach (var w in ws) w(ok);
            };
            if (rt == null) { finish(false); return; }
            Raw("POST", "/v1/auth/refresh", new Dictionary<string, object> { ["refreshToken"] = rt }, false, r =>
            {
                if (r.Ok) SaveSession(r.Data);
                else if (r.Status == 401) ClearSession();   // rotated / revoked: sign in again
                finish(r.Ok);
            });
        }

        private void Raw(string method, string path, Dictionary<string, object> body, bool auth, Action<ApiResult> done)
        {
            var headers = new Dictionary<string, string> { ["Accept"] = "application/json" };
            if (body != null) headers["Content-Type"] = "application/json";
            if (auth) { var at = _store.Get(KeyAccess); if (at != null) headers["Authorization"] = "Bearer " + at; }
            _http.Send(method, _base + path, body != null ? Json.Serialize(body) : null, headers, resp =>
            {
                var r = new ApiResult { Status = resp.Status };
                if (resp.Status == 0) { r.ErrorCode = "network"; r.ErrorMessage = "network error"; done(r); return; }
                Dictionary<string, object> d = null;
                try { if (!string.IsNullOrEmpty(resp.Body)) d = J.Obj(Json.Parse(resp.Body)); } catch (FormatException) { }
                r.Data = d;
                if (!r.Ok)
                {
                    var e = d.O("error");
                    r.ErrorCode = e.S("code") ?? "http_" + resp.Status;
                    r.ErrorMessage = e.S("message") ?? "HTTP " + resp.Status;
                }
                done(r);
            });
        }
    }
}
