using System.Security.Cryptography;
using System.Text;

namespace Dorduncu.Engine
{
    /// <summary>
    /// Deterministic RNG for the provably-fair shuffle: HMAC-SHA256(key = serverSeed, msg = "clientSeed:counter") blocks consumed as
    /// big-endian unsigned 32-bit integers. Identical output to the TypeScript and PHP engines.
    /// </summary>
    public sealed class SeededRng
    {
        private byte[] _buf = new byte[0];
        private int _off;
        private int _counter;
        private readonly byte[] _key;
        private readonly string _clientSeed;

        public SeededRng(string serverSeed, string clientSeed)
        {
            _key = Encoding.UTF8.GetBytes(serverSeed);
            _clientSeed = clientSeed;
        }

        private void Refill()
        {
            using (var h = new HMACSHA256(_key)) _buf = h.ComputeHash(Encoding.UTF8.GetBytes(_clientSeed + ":" + _counter++));
            _off = 0;
        }

        public uint NextUint32()
        {
            if (_off + 4 > _buf.Length) Refill();
            uint n = unchecked((uint)(_buf[_off] << 24 | _buf[_off + 1] << 16 | _buf[_off + 2] << 8 | _buf[_off + 3]));
            _off += 4;
            return n;
        }

        /// <summary>float in [0,1) — bot timing / choices, so replays stay deterministic.</summary>
        public double NextFloat() => NextUint32() / 4294967296.0;

        /// <summary>Unbiased integer in [0, max) via rejection sampling.</summary>
        public int NextInt(int max)
        {
            ulong limit = (4294967296UL / (ulong)max) * (ulong)max;
            uint n;
            do { n = NextUint32(); } while (n >= limit);
            return (int)(n % (uint)max);
        }

        public static string Sha256Hex(string s)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
        }

        public static string HmacSha256Hex(string key, string msg)
        {
            using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(key))) return Hex(h.ComputeHash(Encoding.UTF8.GetBytes(msg)));
        }

        public static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
    }
}
