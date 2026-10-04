// Small, dependency-free JSON (parse → Dictionary<string, object> / List<object> / string / long / double / bool / null).
// Keeps Dorduncu.Client free of UnityEngine and third-party packages so it is also tested with plain .NET.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dorduncu.Client
{
    public static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            var v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException("trailing characters in JSON at " + i);
            return v;
        }

        private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end of JSON");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    var k = Str(s, ref i);
                    Ws(s, ref i);
                    if (s[i++] != ':') throw new FormatException("expected :");
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("expected , or }");
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("expected , or ]");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            var num = s.Substring(st, i - st);
            if (num.Length == 0) throw new FormatException("unexpected character " + c);
            if (num.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
            return double.Parse(num, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("expected string");
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
        }

        public static string Serialize(object v)
        {
            var sb = new StringBuilder();
            Write(sb, v);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s:
                    sb.Append('"');
                    foreach (char c in s)
                    {
                        if (c == '"') sb.Append("\\\"");
                        else if (c == '\\') sb.Append("\\\\");
                        else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                    }
                    sb.Append('"');
                    break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case long n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
                case IDictionary d:
                    sb.Append('{');
                    bool first = true;
                    foreach (DictionaryEntry kv in d)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(sb, kv.Key.ToString());
                        sb.Append(':');
                        Write(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable l:
                    sb.Append('[');
                    bool f1 = true;
                    foreach (var x in l) { if (!f1) sb.Append(','); f1 = false; Write(sb, x); }
                    sb.Append(']');
                    break;
                default: Write(sb, v.ToString()); break;
            }
        }
    }

    /// <summary>Typed access to parsed JSON objects.</summary>
    public static class J
    {
        public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;
        public static List<object> Arr(object o) => o as List<object> ?? new List<object>();
        public static object Get(this Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v : null;
        public static Dictionary<string, object> O(this Dictionary<string, object> d, string k) => Obj(d.Get(k));
        public static List<object> A(this Dictionary<string, object> d, string k) => Arr(d.Get(k));
        public static string S(this Dictionary<string, object> d, string k) => d.Get(k) as string;
        public static int I(this Dictionary<string, object> d, string k, int def = 0) { var v = d.Get(k); return v is long l ? (int)l : v is int n ? n : v is double x ? (int)x : def; }
        public static long L(this Dictionary<string, object> d, string k, long def = 0) { var v = d.Get(k); return v is long l ? l : v is int n ? n : v is double x ? (long)x : def; }
        public static int? NI(this Dictionary<string, object> d, string k) { var v = d.Get(k); return v is long l ? (int)l : v is int n ? n : (int?)null; }
        public static bool B(this Dictionary<string, object> d, string k) => d.Get(k) is bool b && b;
        public static bool Has(this Dictionary<string, object> d, string k) => d != null && d.ContainsKey(k) && d[k] != null;
    }
}
