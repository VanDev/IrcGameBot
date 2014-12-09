using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ch_ircbot
{
    internal sealed class Tuid
    {
        private static SHA1Managed sha1Managed = new SHA1Managed();
        private static string secretKey = "asdf_this_will_change";

        internal static string New()
        {
            var g = Guid.NewGuid().ToByteArray();
            var t = BitConverter.GetBytes(DateTime.UtcNow.ToBinary()).Reverse().ToArray();
            Array.Copy(t,g,t.Length);
            var gt = new Guid(g);
            var gs = gt.ToString("N");
            var gss = secretKey + "|" + gs + "|" +secretKey;
            var h = sha1Managed.ComputeHash(Encoding.Default.GetBytes(gss)).Take(8);
            var b = g.Concat(h);
            return Convert.ToBase64String(b.ToArray())
                .Replace("Z","Za")
                .Replace("+","Zb")
                .Replace("/","Zc")
                .Replace("=","Zd");
        }

        internal static bool Valid(string tuid)
        {
            try
            {
                var b =
                    Convert.FromBase64String(tuid.Replace("Zd", "=")
                        .Replace("Zc", "/")
                        .Replace("Zb", "+")
                        .Replace("Za", "Z"));
                // 8 time, 8 guid, 8 sha
                if (b.Length != 24) return false;

                var gt = new Guid(b.Take(16).ToArray());
                var gs = gt.ToString("N");
                var gss = secretKey + "|" + gs + "|" + secretKey;
                var h = sha1Managed.ComputeHash(Encoding.Default.GetBytes(gss)).Take(8).ToArray();
                return !h.Where((t, i) => t != b[16 + i]).Any();
            }
            catch
            {
                return false;
            }
        }

        internal static DateTime When(string tuid)
        {
            // assumes Valid
            var b = Convert.FromBase64String(tuid.Replace("Zd", "=").Replace("Zc", "/").Replace("Zb", "+").Replace("Za", "Z"));
            var dt = b.Take(8).Reverse().ToArray();
            return DateTime.FromBinary(BitConverter.ToInt64(dt,0));
        }
    }
}