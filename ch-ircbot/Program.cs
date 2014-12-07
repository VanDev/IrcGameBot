using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NetIrc2;

namespace ch_ircbot
{
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
            private readonly DateTime _started;

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
                    return "HAHA, YOU FUNNY!";
                }
            }

            private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(10);
            public bool TryResolve()
            {
                PlayerState p1;
                PlayerState p2;
                Players.TryGetValue(Player1, out p1);
                Players.TryGetValue(Player2, out p2);

                if (p1 == null)
                {
                    if (p2 == null)
                    {
                        return true;
                    }
                    p2.Wins++;
                    return true;
                }
                if (p2 == null)
                {
                    p1.Wins++;
                    return true;
                }

                if (_player1Move != null && _player2Move != null)
                {

                    if (_player1Move == _player2Move)
                    {
                        p1.Ties++;
                        p2.Ties++;
                    }
                    else
                    {
                        if ((_player1Move == "ROCK" && _player2Move == "SCISSORS")
                            || (_player1Move == "SCISSORS" && _player2Move == "PAPER")
                            || (_player1Move == "PAPER" && _player2Move == "ROCK"))
                        {
                            p1.Wins++;
                            p2.Losses++;
                        }
                        else
                        {
                            p2.Wins++;
                            p1.Losses++;
                        }
                    }
                    p1.RemoveMatch(this);
                    p2.RemoveMatch(this);
                    return true;
                }
                var timeout = DateTime.UtcNow - MatchTimeout;
                if (_started > timeout)
                {
                    if (_player1Move != null)
                    {
                        p2.Wins++;
                        return true;
                    }
                    p1.Wins++;
                    return true;
                }
                return false;
            }
        }

        sealed class PlayerState
        {
            private static readonly TimeSpan LivelynessTimeSpan = TimeSpan.FromSeconds(30);

            public int Wins { get; set; }
            public int Losses { get; set; }
            public int Ties { get; set; }
            public DateTime LastCommunication { get; set; }
            public bool Alive { get { return (DateTime.UtcNow - LastCommunication).Duration() < LivelynessTimeSpan; } }

            private int _matchCount;

            public void AddMatch(MatchState ms)
            {
                Interlocked.Increment(ref _matchCount);
            }
            public void RemoveMatch(MatchState ms)
            {
                Interlocked.Decrement(ref _matchCount);
            }

            public bool Free { get { return _matchCount == 0; } }

            public PlayerState(string name)
            {
                Name = name;
                LastCommunication = GetTime();
                Wins = 0;
                Losses = 0;
            }

            public string Name { get; private set; }

            public bool PingPending { get; set; }

            public int Played()
            {
                return Wins + Losses + Ties;
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
                var player = UpdateLastCommunicationTime(bits[1]);
                if(player == null)
                    return "WHO ARE YOU?!";
                player.PingPending = false;
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
                if (UpdateLastCommunicationTime(bits[1]) == null)
                    return "WHO ARE YOU?!!";

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

        private static PlayerState UpdateLastCommunicationTime(string player)
        {
            PlayerState ps;
            if (Players.TryGetValue(player, out ps))
            {
                ps.LastCommunication = GetTime();
                return ps;
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
                            if (newMatch != null)
                            {
                                Matches[newMatch.Id] = newMatch;
                                x.Message(newMatch.Player1,
                                    Record(sw, newMatch.Player1,
                                        "NEWMATCH " + newMatch.Mode + " " + newMatch.Id + " " + newMatch.Player2));
                                x.Message(newMatch.Player2,
                                    Record(sw, newMatch.Player2,
                                        "NEWMATCH " + newMatch.Mode + " " + newMatch.Id + " " + newMatch.Player1));
                            }

                            var pingTime = GetTime().Add(TimeSpan.FromSeconds(-10));
                            foreach (var ps in Players.Values.Where(p=>!p.PingPending))
                            {
                                if (ps.LastCommunication < pingTime)
                                {
                                    ps.PingPending = true;
                                    x.Message(ps.Name, "PING");
                                }
                            }

                            foreach (var id in Matches.Values.Where(match => match.TryResolve()).Select(match => match.Id))
                            {
                                MatchState ms;
                                Matches.TryRemove(id, out ms);
                            }
                        }
                    }
                }
            }
        }

        private static DateTime GetTime()
        {
            return DateTime.UtcNow;
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

        static readonly Random Rand = new Random();

        private static MatchState CreateMatch()
        {
            var possibles = Players.Values.Where(x => x.Alive && x.Free).ToArray();
            if (possibles.Length < 2)
            {
                possibles = Players.Values.Where(x => x.Alive).ToArray();
            }
            if (possibles.Length < 2)
                return null;

            Swap(ref possibles, Rand.Next(0, possibles.Length), 0);
            Swap(ref possibles, Rand.Next(1, possibles.Length), 1);

            var p1 = possibles[0];
            var p2 = possibles[1];
            var ms = new MatchState(p1.Name, p2.Name);
            p1.AddMatch(ms);
            p2.AddMatch(ms);
            return ms;
        }

        private static void Swap<T>(ref T[] arr, int i, int j)
        {
            if (i == j) return;
            var temp = arr[i];
            arr[i] = arr[j];
            arr[j] = temp;
        }
    }
}
