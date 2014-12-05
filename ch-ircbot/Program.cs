using System;
using System.Collections.Concurrent;
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
using NetIrc2.Events;

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
                Assert.That(!Tuid.Valid("SNHdh3ZaaiWZbphyNv3twO6tMri2PgbHdB"));
            }
        }
    }

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
            var b = Convert.FromBase64String(tuid.Replace("Zd","=").Replace("Zc","/").Replace("Zb","+").Replace("Za","Z"));
            // 8 time, 8 guid, 8 sha
            if (b.Length != 24) return false;

            var gt = new Guid(b.Take(16).ToArray());
            var gs = gt.ToString("N");
            var gss = secretKey + "|" + gs + "|" + secretKey;
            var h = sha1Managed.ComputeHash(Encoding.Default.GetBytes(gss)).Take(8).ToArray();
            return !h.Where((t, i) => t != b[16 + i]).Any();
        }

        internal static DateTime When(string tuid)
        {
            // assumes Valid
            var b = Convert.FromBase64String(tuid.Replace("Zd", "=").Replace("Zc", "/").Replace("Zb", "+").Replace("Za", "Z"));
            var dt = b.Take(8).Reverse().ToArray();
            return DateTime.FromBinary(BitConverter.ToInt64(dt,0));
        }
    }

    internal static class Program
    {
        private const string Me = "user02ae4f8be6";
        private static readonly string[] LineSeparator = new[]{" | "};

        
        sealed class MatchState
        {
            private readonly object _lock = new object();

            public MatchState(string player1, string player2)
            {
                Player1 = player1;
                Player2 = player2;
                Id = EventId();
                _started = Tuid.When(Id);
            }

            private string _player1Move;
            private string _player2Move;

            private IList<string> _log = new List<string>();
            private DateTime _started;

            public string Player1 { get; private set; }
            public string Player2 { get; private set; }

            public string Mode { get { return "DEMO"; } }

            public string Id { get; private set; }

            public string Move(string player, string move)
            {
                lock (_lock)
                {
                    if (player == Player1)
                    {
                        if (_player1Move == null)
                        {
                            _player1Move = move;
                            return "OK!";
                        }
                        return "NICETRY!";
                    }
                    if (player == Player2)
                    {
                        if (_player2Move == null)
                        {
                            _player2Move = move;
                            return "OK!";
                        }
                        return "NICETRY!";
                    }
                    return "HAHA YOU FUNNY!";
                }
            }
        }

        sealed class PlayerState
        {
            private string _name;
            private int _wins;
            private int _losses;
            private DateTime _lastCommunication;

            public PlayerState(string name)
            {
                _name = name;
                _wins = 0;
                _losses = 0;
            }
        }

        private static string EventId()
        {
            return Tuid.New();
        }
        private static string ParseLine(String line)
        {
            var bits = line.Split(LineSeparator, StringSplitOptions.None);
            if (bits.Length != 3) return null;

            if (bits[2].Contains("HI"))
            {
                return "Hi!";
            }
            if (bits[2].Contains("PONG"))
            {
                return "ACK!";
            }
            if (bits[2].Contains("NEWMATCH"))
            {
                return bits[2].Replace("NEWMATCH", "MATCH");
            }

            if (bits[2].Contains("REGISTER"))
            {
                PlayerState ps;
                if (Players.TryGetValue(bits[1], out ps))
                {
                    return "OK!!";
                }
                if (Players.TryAdd(bits[1], new PlayerState(bits[1])))
                {
                    return "OK!";
                }
                return "OK?!";
            }

            if (bits[2].Contains("MATCH"))
            {
                var match = bits[2].Split(' ');
                if (match.Length != 3)
                    return "NO";
                if (match[0] != "MATCH")
                    return "NO!";
                if (!Tuid.Valid(match[1]))
                    return "NOPE";
                if (!Moves.Contains(match[2]))
                    return "NOPE!";

                MatchState ms;
                if (!Matches.TryGetValue(match[1], out ms))
                {
                    return "INVALID!";
                }

                return ms.Move(bits[1], match[2]);
            }
            return null;
        }

        static readonly ConcurrentDictionary<string, PlayerState> Players = new ConcurrentDictionary<string, PlayerState>();
        static readonly ConcurrentDictionary<string, MatchState> Matches = new ConcurrentDictionary<string, MatchState>();
        static readonly ISet<string> Moves = new HashSet<string> { "ROCK", "PAPER", "SISSORS" };

        public static void Main(string[] args)
        {
            var connected = false;
            var notice = false;

            
            RestoreState();
            

            {
                var logFileName = DateTime.UtcNow.ToString("u").Replace(" ", "T").Replace(":", "_").Replace("-", "_") +
                    ".botlog.txt";
                using (var logFileStream = new FileStream(logFileName, FileMode.Append, FileAccess.Write))
                {
                    using (var sw = new StreamWriter(logFileStream))
                    {

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
                                    var message = eventArgs.Message.ToString();

                                    Console.WriteLine("GotMessage: " + eventArgs.Sender.Nickname + " > " +
                                        eventArgs.Recipient +
                                        " > " +
                                        message);

                                    if (eventArgs.Recipient != Me)
                                        return;

                                    var result = Record(sw, eventArgs.Sender.Nickname, message);
                                    if (result != null)
                                        x.Message(eventArgs.Sender.Nickname, result);
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
                        x.LogIn(Me, "none", Me, "chat.freenode.net");

                        var joined = false;
                        for (; ; )
                        {
                            if (connected && notice && !joined)
                            {
                                Console.WriteLine("Attempting to join");
                                x.Join("#02ae4f8be6");
                                x.Message("#02ae4f8be6", "test");
                                joined = true;
                            }

                            Thread.Sleep(100);

                            var newMatch = CreateMatch();
                            if (newMatch == null) 
                                continue;
                            x.Message(newMatch.Player1, Record(sw, newMatch.Player1, "NEWMATCH " + newMatch.Mode + " " + newMatch.Id + " " + newMatch.Player2));
                            x.Message(newMatch.Player2, Record(sw, newMatch.Player2, "NEWMATCH " + newMatch.Mode + " " + newMatch.Id + " " + newMatch.Player1));
                        }

                    }
                }
            }
        }

        private static void RestoreState()
        {
            var logFileNames = Directory.EnumerateFiles(".", "*.botlog.txt");
            foreach (var logFileName in logFileNames)
                using (var logFileStream = new FileStream(logFileName, FileMode.Open, FileAccess.Read))
                {
                    logFileStream.Seek(0, SeekOrigin.Begin);
                    using (var sr = new StreamReader(logFileStream))
                    {
                        while (!sr.EndOfStream)
                        {
                            var line = sr.ReadLine();
                            ParseLine(line);
                        }
                    }
                }
        }

        private static string Record(TextWriter sw, string nickname, string message )
        {
            var eventID = EventId();
            var line = string.Format("{0} | {1} | {2}", eventID, nickname, message);

            sw.WriteLine(line);
            sw.Flush();

            return ParseLine(line);
        }

        static Random _r = new Random();

        private static MatchState CreateMatch()
        {
            var possibles = Players.Where(x => x.Alive && x.Free).ToArray();
            if (possibles.Length < 2)
            {
                possibles = Players.Where(x => x.Alive);
            }
            if (possibles.Length < 2)
                return null;

            
            // find players that are 1) alive, and 2) not in a match
            // if at least two
                // pick two at random
                // start a match
            // else
            //  find players that are alive
            //  if at least two
            //    pick two at random
            //    start a match

            return null;
        }
    }
}
