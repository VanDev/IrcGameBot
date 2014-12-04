using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NUnit.Framework;
using NetIrc2;

namespace ch_ircbot
{

    [TestFixture]
    public class TF
    {
        [Test]
        public void TestTuid()
        {
            for (int i = 0; i < 16; ++i)
            {
                var tuid = Tuid.New();
                Debug.WriteLine(tuid);
                Assert.That(Tuid.Valid(tuid));
            }
        }
    }

    internal sealed class Tuid
    {
        private static SHA1Managed sha1Managed = new SHA1Managed();
        private static string secretKey = "asdf";

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
            var b = Convert.FromBase64String(tuid.Replace("Zd","=").Replace("Zc","/").Replace("Zb","+").Replace("Za","Z"));
            // 4 time, 12 guid, 8 sha
            if (b.Length != 24) return false;

            var gt = new Guid(b.Take(16).ToArray());
            var gs = gt.ToString("N");
            var gss = secretKey + "|" + gs + "|" + secretKey;
            var h = sha1Managed.ComputeHash(Encoding.Default.GetBytes(gss)).Take(8).ToArray();
            return !h.Where((t, i) => t != b[16 + i]).Any();
        }
    }

    internal static class Program
    {
        private static TV GetOrAdd<TK, TV>(this IDictionary<TK, TV> dict, TK key, Func<TK, TV> valueFactory)
        {
            TV value;
            if (dict.TryGetValue(key, out value)) return value;
            value = valueFactory(key);
            dict[key] = value;
            return value;
        }

        private static void Main(string[] args)
        {
            var connected = false;
            var notice = false;

            var logFileName = DateTime.UtcNow.ToString("u").Replace(" ","T").Replace(":","_").Replace("-","_") + ".txt";
            var matches = new Dictionary<string, IList<string>>();

            Func<string> eventId = Tuid.New;

            Action<string> parseLine = line => { };

            using (var logFileStream = new FileStream(logFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                logFileStream.Seek(0, SeekOrigin.Begin);
                using (var sr = new StreamReader(logFileStream))
                {
                    while (!sr.EndOfStream)
                    {
                        var line = sr.ReadLine();
                        parseLine(line);
                    }
                }

                using (var sw = new StreamWriter(logFileStream))
                {
                    var identServer = new IdentServer();
                    try
                    {
                        identServer.OperatingSystem = "Windows";
                        identServer.UserID = "02ae4f8be6";
                        identServer.Start();
                    }
                    catch (SocketException)
                    {
                        Console.WriteLine("Ident server failed to start.");
                    }

                    var x = new IrcClient();
                    x.GotChannelListBegin += (sender, eventArgs) => Console.WriteLine("GotChannelListBegin");
                    x.GotChannelListEnd += (sender, eventArgs) => Console.WriteLine("GotChannelListEnd");
                    x.GotChannelListEntry += (sender, eventArgs) => Console.WriteLine("GotChannelListEntry");
                    x.GotChannelTopicChange += (sender, eventArgs) => Console.WriteLine("GotChannelTopicChange");
                    x.GotChatAction += (sender, eventArgs) => Console.WriteLine("GotChatAction");
                    x.GotInvitation += (sender, eventArgs) => Console.WriteLine("GotInvitation");
                    x.GotIrcError += (sender, eventArgs) => Console.WriteLine("GotIrcError: " + eventArgs.Error);
                    x.GotJoinChannel += (sender, eventArgs) => Console.WriteLine("GotJoinChannel");
                    x.GotLeaveChannel += (sender, eventArgs) => Console.WriteLine("GotLeaveChannel");
                    x.GotMessage +=
                        (sender, eventArgs) =>
                            {
                                Console.WriteLine("GotMessage: " + eventArgs.Sender.Nickname + " > " +
                                    eventArgs.Recipient +
                                    " > " +
                                    eventArgs.Message);

                                var eventID = eventId();
                                var line = string.Format("{0} | {1} | {2}", eventID, eventArgs.Sender.Nickname, eventArgs.Message);
                                parseLine(line);
                                sw.WriteLine(line);

                                x.Message(eventArgs.Sender.Nickname, eventID + " " + eventArgs.Message);
                            };
                    x.GotMode += (sender, eventArgs) => Console.WriteLine("GotMode");
                    x.GotMotdBegin += (sender, eventArgs) => Console.WriteLine("GotMotdBegin");
                    x.GotMotdText += (sender, eventArgs) => Console.WriteLine("GotMotdText");
                    x.GotNameChange += (sender, eventArgs) => Console.WriteLine("GotNameChange");
                    x.GotNameListEnd += (sender, eventArgs) => Console.WriteLine("GotNameListEnd");
                    x.GotNameListReply += (sender, eventArgs) => Console.WriteLine("GotNameListReply");
                    x.GotNotice += (sender, eventArgs) =>
                        {
                            Console.WriteLine("GotNotice: " + eventArgs.Message);
                            notice = eventArgs.Message.ToString().ToUpperInvariant().Contains("NO IDENT");
                        };
                    x.GotPingReply += (sender, eventArgs) => Console.WriteLine("GotPingReply");
                    x.GotUserKicked += (sender, eventArgs) => Console.WriteLine("GotUserKicked");
                    x.GotUserQuit += (sender, eventArgs) => Console.WriteLine("GotUserQuit");
                    x.GotWelcomeMessage += (sender, eventArgs) => Console.WriteLine("GotWelcomeMessage");
                    x.Closed += (sender, eventArgs) =>
                        {
                            Console.WriteLine("Closed");
                            connected = false;
                            notice = false;
                        };
                    x.Connected += (sender, eventArgs) =>
                        {
                            Console.WriteLine("Connected");
                            connected = true;
                        };
                    x.Connect("chat.freenode.net");
                    x.LogIn("user02ae4f8be6", "none", "user02ae4f8be6", "chat.freenode.net");

                    var joined = false;
                    while (!Console.KeyAvailable)
                    {
                        if (connected && notice && !joined)
                        {
                            Console.WriteLine("Attempting to join");
                            x.Join("#02ae4f8be6");
                            x.Message("#02ae4f8be6", "test");
                            joined = true;
                        }
                        Thread.Sleep(100);
                    }

                    identServer.Stop();
                }
            }
        }
    }
}
