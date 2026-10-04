// Minimal stand-ins for NUnit and Newtonsoft.Json.Linq (NuGet is not reachable here) so the exact Unity test file runs on .NET.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class TestAttribute : Attribute { }
    public static class Assert
    {
        public static void AreEqual(object e, object a, string m = null) { if (!Equals(e, a)) throw new Exception($"expected {e} got {a} {m}"); }
        public static void IsNull(object a, string m = null) { if (a != null) throw new Exception("expected null " + m); }
        public static void IsNotNull(object a, string m = null) { if (a == null) throw new Exception("expected not null " + m); }
        public static void IsTrue(bool c, string m = null) { if (!c) throw new Exception("expected true " + m); }
    }
    public static class CollectionAssert
    {
        public static void AreEqual(IEnumerable e, IEnumerable a, string m = null)
        {
            var x = e.Cast<object>().ToList(); var y = a.Cast<object>().ToList();
            if (x.Count != y.Count || x.Where((v, i) => !Equals(v, y[i])).Any())
                throw new Exception($"collections differ {m}: [{string.Join(",", x)}] vs [{string.Join(",", y)}]");
        }
    }
}

namespace Newtonsoft.Json.Linq
{
    public enum JTokenType { Null, Other }
    public sealed class JToken : IEnumerable<JToken>
    {
        private readonly JsonElement _e;
        private JToken(JsonElement e) { _e = e; }
        public static JToken Parse(string s) => new JToken(JsonDocument.Parse(s).RootElement.Clone());
        public JTokenType Type => _e.ValueKind == JsonValueKind.Null ? JTokenType.Null : JTokenType.Other;
        public JToken this[string k] => new JToken(_e.GetProperty(k));
        public JToken this[int i] => new JToken(_e[i]);
        public IEnumerator<JToken> GetEnumerator() => _e.EnumerateArray().Select(x => new JToken(x)).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public static explicit operator string(JToken t) => t._e.ValueKind == JsonValueKind.Null ? null : t._e.GetString();
        public static explicit operator int(JToken t) => t._e.GetInt32();
        public static explicit operator long(JToken t) => t._e.GetInt64();
        public static explicit operator bool(JToken t) => t._e.GetBoolean();
        public override string ToString() => _e.GetRawText();
    }
}

public static class Runner
{
    public static int Main()
    {
        int fail = 0;
        foreach (var type in new[] { typeof(Dorduncu.Tests.GoldenTests), typeof(Dorduncu.Tests.ClientTests) })
        foreach (var m in type.GetMethods().Where(x => x.GetCustomAttribute<NUnit.Framework.TestAttribute>() != null))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { m.Invoke(Activator.CreateInstance(type), null); Console.WriteLine($"PASS {type.Name}.{m.Name} ({sw.ElapsedMilliseconds} ms)"); }
            catch (TargetInvocationException e) { fail++; Console.WriteLine($"FAIL {m.Name}: {e.InnerException.Message[..Math.Min(600, e.InnerException.Message.Length)]}"); }
        }
        return fail;
    }
}
