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
        private const string Me = "VanDevBot7c34cb9";
        private static readonly string[] LineSeparator = new[] {" | "};

        private static readonly ConcurrentDictionary<string, PlayerState> Players =
            new ConcurrentDictionary<string, PlayerState>();

        private static readonly ConcurrentDictionary<string, MatchState> Matches =
            new ConcurrentDictionary<string, MatchState>();

        private static readonly ISet<string> Moves = new HashSet<string> {"ROCK", "PAPER", "SISSORS"};
        private static readonly Random Rand = new Random();

        private static DateTime _lastMatchCreated = DateTime.MinValue;
        private static readonly TimeSpan CreationDelay = TimeSpan.FromSeconds(3);
        private static readonly char[] SpaceSeparator = new[] {' '};
        private const int MaxSymoMatches = 2;

        private static string EventId()
        {
            return Tuid.New();
        }

        private static IEnumerable<Tuple<string, string>> ParseLine(String line)
        {
            Console.WriteLine(line);
            var bits = line.Split(LineSeparator, StringSplitOptions.None);
            if (bits.Length != 3) yield break;

            var eid = bits[0];
            var playerName = bits[1];
            var command = bits[2];
            if (command.StartsWith("HI"))
            {
                yield return Tuple.Create(playerName, "Hi!");
                yield break;
            }
            if (command.StartsWith("PONG"))
            {
                var player = UpdateLastCommunicationTime(playerName);
                if (player == null)
                {
                    yield return Tuple.Create(playerName, "WHO ARE YOU?!");
                    yield break;
                }
                player.PingPending = false;
                yield return Tuple.Create(playerName, "ACK!");
                yield break;
            }

            if (command.StartsWith("MATCHLOG"))
            {
                yield return Tuple.Create(playerName, "SORRY");
                yield break;
            }

            if (command.StartsWith("LISTMATCHES"))
            {
                var p = UpdateLastCommunicationTime(playerName);
                if (p != null)
                {
                    yield return
                        Tuple.Create(playerName,
                            string.Join(", ", p.Matches.Values.Where(m => !m.Resolved).Select(m => m.Id)));
                }
                yield break;
            }

            if (command.StartsWith("NEWMATCH"))
            {
                var newmatch = command.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);
                if (newmatch.Length != 4)
                {
                    yield break;
                }
                if (playerName != Me)
                {
                    yield break;
                }
                var com = newmatch[0];
                if (com != "NEWMATCH")
                {
                    yield break;
                }
                var mode = newmatch[1];
                var p1 = newmatch[2];
                var p2 = newmatch[3];

                PlayerState player1;
                Players.TryGetValue(p1, out player1);
                PlayerState player2;
                Players.TryGetValue(p2, out player2); 
                
                if (player1 == null || player2 == null)
                    yield break;

                var ms = new MatchState(mode, eid, p1, p2);

                player1.AddMatch(ms);
                player2.AddMatch(ms);
                Matches[ms.Id] = ms;
                yield return Tuple.Create(p1, "MATCH " + mode + " " + eid + " " + p2);
                yield return Tuple.Create(p2, "MATCH " + mode + " " + eid + " " + p1);
                yield break;
            }

            if (command.StartsWith("RESULTMATCH"))
            {
                var resultmatch = command.Split(SpaceSeparator, 3, StringSplitOptions.RemoveEmptyEntries);
                if (resultmatch.Length != 3)
                {
                    yield break;
                }
                if (playerName != Me)
                {
                    yield break;
                }
                var com = resultmatch[0];
                if (com != "RESULTMATCH")
                {
                    yield break;
                }
                var mid = resultmatch[1];
                if (!Tuid.Valid(mid))
                {
                    yield break;
                }
                var mresult = resultmatch[2];

                MatchState ms;
                if (Matches.TryGetValue(mid, out ms))
                {
                    ms.Resolved = true;
                    PlayerState player1;
                    Players.TryGetValue(ms.Player1, out player1);
                    PlayerState player2;
                    Players.TryGetValue(ms.Player2, out player2);

                    var mrbits = mresult.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);

                    if (mrbits.Length>1 && mrbits[1] == "WINS")
                    {
                        if (mrbits[0] == ms.Player1)
                        {
                            if (player1 != null)
                                ++player1.Wins;
                            if (player2 != null)
                                ++player2.Losses;
                        }
                        if (mrbits[0] == ms.Player2)
                        {
                            if (player1 != null)
                                ++player1.Losses;
                            if (player2 != null)
                                ++player2.Wins;
                        }
                    }
                    else
                    {
                        if (player1 != null) ++player1.Ties;
                        if (player2 != null) ++player2.Ties;
                    }
                    if (player1 != null)
                        yield return Tuple.Create(player1.Name, "MATCH " + mid + " " + mresult);
                    if (player2 != null)
                        yield return Tuple.Create(player2.Name, "MATCH " + mid + " " + mresult);
                }
                yield break;
            }

            if (command.StartsWith("STAT"))
            {
                var ps = UpdateLastCommunicationTime(playerName);

                if (ps != null)
                {
                    yield return
                        Tuple.Create(playerName,
                            string.Format("{0} WINS {1} LOSS {2} TIE {3} SCORE", ps.Wins, ps.Losses, ps.Ties, ps.Score()))
                        ;
                    yield break;
                }
                yield return Tuple.Create(playerName, "YOU DO NOT EXIST!");
                yield break;
            }
            if (command.StartsWith("REGISTER"))
            {
                PlayerState ps;
                if (Players.TryGetValue(playerName, out ps))
                {
                    ps.PingPending = false;
                    yield return Tuple.Create(playerName, "OK!!");
                    yield break;
                }
                if (Players.TryAdd(playerName, new PlayerState(playerName)))
                {
                    yield return Tuple.Create(playerName, "OK!");
                    yield break;
                }
                yield return Tuple.Create(playerName, "OK?!");
                yield break;
            }

            if (command.StartsWith("MATCH"))
            {
                if (UpdateLastCommunicationTime(playerName) == null)
                {
                    yield return Tuple.Create(playerName, "WHO ARE YOU?!!");
                    yield break;
                }

                var match = command.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);
                if (match.Length != 3)
                {
                    yield return Tuple.Create(playerName, "NO");
                    yield break;
                }
                if (match[0] != "MATCH")
                {
                    yield return Tuple.Create(playerName, "NO!");
                    yield break;
                }
                var matchID = match[1];
                if (!Tuid.Valid(matchID))
                {
                    yield return Tuple.Create(playerName, "NOPE");
                    yield break;
                }
                var matchMove = match[2];
                if (!Moves.Contains(matchMove))
                {
                    yield return Tuple.Create(playerName, "NOPE!");
                    yield break;
                }

                MatchState ms;
                if (!Matches.TryGetValue(matchID, out ms))
                {
                    yield return Tuple.Create(playerName, "INVALID!");
                    yield break;
                }

                if (ms.Resolved)
                {
                    yield return Tuple.Create(playerName, "MATCH IS ALREADY RESOLVED.");
                    yield break;
                }

                yield return Tuple.Create(playerName, ms.Move(playerName, matchMove));
                yield break;
            }

            if (command.StartsWith("LEADERBOARD"))
            {
                yield return Tuple.Create(playerName, string.Join("; ",
                    Players.Values.OrderByDescending(p => p.Score()).Take(10).Select(p => p.Name + " score=" + (p.Score().ToString("F3")) + " played=" + (p.Wins+p.Ties+p.Losses))));
                yield break;
            }
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

                                    if (eventArgs.Recipient.ToString() != Me)
                                        return;

                                    var results = Record(sw, eventArgs.Sender.Nickname, message);
                                    foreach (var r in results)
                                        x.Message(r.Item1, r.Item2);
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
                        for (;;)
                        {
                            if (connected && notice && !joined)
                            {
                                Console.WriteLine("Attempting to join");
                                x.Join("#02ae4f8be6");
                                x.Message("#02ae4f8be6", "test");
                                joined = true;
                            }

                            Thread.Sleep(100);
                            if (!joined)
                                continue;

                            var newMatch = PickTwoPlayersForNewMatch();
                            if (newMatch != null)
                            {
                                foreach (
                                    var r in
                                        Record(sw, Me, "NEWMATCH DEMO " + " " + newMatch.Item1 + " " + newMatch.Item2))
                                {
                                    x.Message(r.Item1, r.Item2);
                                }
                            }

                            var pingTime = GetTime().Add(TimeSpan.FromSeconds(-10));
                            foreach (var ps in Players.Values.Where(p => !p.PingPending))
                            {
                                if (ps.LastCommunication < pingTime)
                                {
                                    ps.PingPending = true;
                                    x.Message(ps.Name, "PING");
                                }
                            }

                            foreach (
                                var ms in
                                    Matches.Values.Where(match => match.TryResolve()))
                            {
                                foreach (var r in Record(sw, Me, "RESULTMATCH " + ms.Id + " " + ms.Result))
                                {
                                    x.Message(r.Item1, r.Item2);
                                }
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
                    using (var sr = new StreamReader(logFileStream))
                    {
                        while (!sr.EndOfStream)
                        {
                            var line = sr.ReadLine();
                            ParseLine(line).Seq();
                        }
                    }
                }
        }

        private static IEnumerable<T> Seq<T>(this IEnumerable<T> e)
        {
            return e.ToArray();
        }

        private static IEnumerable<Tuple<string, string>> Record(TextWriter sw, string nickname, string message)
        {
            var eventID = EventId();
            var line = string.Format("{0} | {1} | {2}", eventID, nickname, message);

            sw.WriteLine(line);
            sw.Flush();

            return ParseLine(line);
        }

        private static Tuple<string, string> PickTwoPlayersForNewMatch()
        {
            if (_lastMatchCreated.Add(CreationDelay) > GetTime()) return null;

            PlayerState[] possibles = null;
            for (var i = 0; i < MaxSymoMatches; ++i)
            {
                possibles = Players.Values.Where(x => x.Alive && x.AliveMatchCount == i).ToArray();
                if (possibles.Length >= 2)
                {
                    break;
                }
            }

            if (possibles == null || possibles.Length < 2)
            {
                return null;
            }
            Swap(ref possibles, Rand.Next(0, possibles.Length), 0);
            Swap(ref possibles, Rand.Next(1, possibles.Length), 1);

            _lastMatchCreated = GetTime();
            return Tuple.Create(possibles[0].Name, possibles[1].Name);
        }

        private static void Swap<T>(ref T[] arr, int i, int j)
        {
            if (i == j) return;
            var temp = arr[i];
            arr[i] = arr[j];
            arr[j] = temp;
        }

        private sealed class MatchState
        {
            private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(60);
            private readonly object _lock = new object();
            private readonly IList<string> _log = new List<string>();
            private readonly DateTime _started;

            private string _player1Move;
            private string _player2Move;

            public MatchState(string mode, string id, string player1, string player2)
            {
                Mode = mode;
                Player1 = player1;
                Player2 = player2;
                Id = id;
                _started = Tuid.When(Id);
            }

            public string Result { get; private set; }
            public string Player1 { get; private set; }
            public string Player2 { get; private set; }
            public string Mode { get; private set; }
            public string Id { get; private set; }

            public bool Resolved { get; set; }

            public string Move(string player, string move)
            {
                lock (_lock)
                {
                    _log.Add("MOVE " + player + " " + move);
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

            public bool TryResolve()
            {
                if (Resolved)
                    return false;

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
                    Result = p2.Name + " WINS FORFEIT";
                    _log.Add(Result);
                    return true;
                }
                if (p2 == null)
                {
                    Result = p1.Name + " WINS FORFEIT";
                    _log.Add(Result);
                    return true;
                }

                if (_player1Move != null && _player2Move != null)
                {
                    if (_player1Move == _player2Move)
                    {
                        Result = "TIE";
                        _log.Add(Result);
                    }
                    else
                    {
                        if ((_player1Move == "ROCK" && _player2Move == "SCISSORS")
                            || (_player1Move == "SCISSORS" && _player2Move == "PAPER")
                            || (_player1Move == "PAPER" && _player2Move == "ROCK"))
                        {
                            Result = p1.Name + " WINS " + _player1Move;
                            _log.Add(Result);
                        }
                        else
                        {
                            Result = p2.Name + " WINS " + _player2Move;
                            _log.Add(Result);
                        }
                    }
                    return true;
                }
                var now = GetTime();
                if (_started.Add(MatchTimeout) < now)
                {
                    _log.Add("TIMEOUT");
                    if (_player1Move == null && _player2Move != null)
                    {
                        Result = p1.Name + " WINS FORFEIT";
                        _log.Add(Result);
                        return true;
                    }
                    if (_player2Move != null && _player1Move != null)
                    {
                        Result = p2.Name + " WINS FORFEIT";
                        _log.Add(Result);
                        return true;
                    }
                    Result = "TIE";
                    _log.Add("TIE");
                    return true;
                }
                return false;
            }
        }

        private sealed class PlayerState
        {
            private static readonly TimeSpan LivelynessTimeSpan = TimeSpan.FromSeconds(20);

            public readonly ConcurrentDictionary<string, MatchState> Matches =
                new ConcurrentDictionary<string, MatchState>();

            public PlayerState(string name)
            {
                Name = name;
                LastCommunication = GetTime();
                Wins = 0;
                Losses = 0;
            }

            public int Wins { get; set; }
            public int Losses { get; set; }
            public int Ties { get; set; }
            public DateTime LastCommunication { get; set; }

            public bool Alive
            {
                get
                {
                    var expireAt = LastCommunication.Add(LivelynessTimeSpan);
                    var now = GetTime();
                    return (expireAt > now);
                }
            }

            public int AliveMatchCount
            {
                get { return Matches.Count(ms => !ms.Value.Resolved); }
            }

            public string Name { get; private set; }

            public bool PingPending { get; set; }

            public void AddMatch(MatchState ms)
            {
                Matches.TryAdd(ms.Id, ms);
            }

            public double Score()
            {
                int total = Wins + Losses;
                if (total == 0) return 0;
                return Wins/(double)total;
            }
        }
    }
}
