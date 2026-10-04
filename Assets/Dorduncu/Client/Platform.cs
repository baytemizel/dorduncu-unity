// Seams between the client core and the platform: HTTP, timers and persistent storage. Unity implements them with
// UnityWebRequest / Update() / PlayerPrefs (Dorduncu.UI); tests implement them with HttpClient and a manual clock.
using System;
using System.Collections.Generic;

namespace Dorduncu.Client
{
    public sealed class HttpResponse
    {
        public int Status;              // 0 = network failure
        public string Body;
    }

    public interface IHttp
    {
        /// <summary>Send a request; `done` must be invoked on the main thread.</summary>
        void Send(string method, string url, string jsonBody, Dictionary<string, string> headers, Action<HttpResponse> done);
    }

    public interface ITimer { void Cancel(); }

    public interface IScheduler
    {
        long NowMs { get; }
        ITimer After(int ms, Action fn);
    }

    public interface IKeyValueStore
    {
        string Get(string key);
        void Set(string key, string value);   // null deletes
    }

    /// <summary>In-memory store (tests, editor previews).</summary>
    public sealed class MemoryStore : IKeyValueStore
    {
        private readonly Dictionary<string, string> _d = new Dictionary<string, string>();
        public string Get(string key) => _d.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value) { if (value == null) _d.Remove(key); else _d[key] = value; }
    }
}
