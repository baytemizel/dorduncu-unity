// Unity implementations of the client core's platform seams: UnityWebRequest, a frame-driven scheduler, PlayerPrefs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Dorduncu.Client;
using UnityEngine;
using UnityEngine.Networking;

namespace Dorduncu.UI
{
    /// <summary>HTTP over UnityWebRequest; callbacks run on the main thread (coroutines).</summary>
    public sealed class UnityHttp : IHttp
    {
        private readonly MonoBehaviour _host;
        public int TimeoutSec = 15;
        public UnityHttp(MonoBehaviour host) { _host = host; }

        public void Send(string method, string url, string jsonBody, Dictionary<string, string> headers, Action<HttpResponse> done) =>
            _host.StartCoroutine(Run(method, url, jsonBody, headers, done));

        private IEnumerator Run(string method, string url, string body, Dictionary<string, string> headers, Action<HttpResponse> done)
        {
            using (var req = new UnityWebRequest(url, method))
            {
                if (body != null) req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = TimeoutSec;
                UnityWebRequestAsyncOperation op;
                try
                {
                    foreach (var h in headers) req.SetRequestHeader(h.Key, h.Value);
                    op = req.SendWebRequest();          // throws e.g. for plain http when insecure HTTP is not allowed
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Dördüncü] " + e.Message);
                    done(new HttpResponse { Status = 0 });
                    yield break;
                }
                yield return op;
                bool network = req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError;
                done(new HttpResponse { Status = network ? 0 : (int)req.responseCode, Body = req.downloadHandler?.text });
            }
        }
    }

    /// <summary>Timers driven by Update(); NowMs is wall-clock epoch milliseconds (server deadlines are epoch ms too).</summary>
    public sealed class UnityScheduler : MonoBehaviour, IScheduler
    {
        private sealed class T : ITimer { public long Due; public Action Fn; public bool Dead; public void Cancel() => Dead = true; }
        private readonly List<T> _timers = new List<T>();
        public long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public ITimer After(int ms, Action fn)
        {
            var t = new T { Due = NowMs + Math.Max(0, ms), Fn = fn };
            _timers.Add(t);
            return t;
        }

        private void Update()
        {
            if (_timers.Count == 0) return;
            long now = NowMs;
            var due = _timers.FindAll(t => !t.Dead && t.Due <= now);
            _timers.RemoveAll(t => t.Dead || t.Due <= now);
            due.Sort((a, b) => a.Due.CompareTo(b.Due));
            foreach (var t in due)
            {
                if (t.Dead) continue;
                try { t.Fn(); } catch (Exception e) { Debug.LogException(e); }
            }
        }
    }

    public sealed class PlayerPrefsStore : IKeyValueStore
    {
        public string Get(string key) => PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;

        public void Set(string key, string value)
        {
            if (value == null) PlayerPrefs.DeleteKey(key); else PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
        }
    }
}
