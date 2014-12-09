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
        private const string VanDevBot = "VanDevBot7c34cb9";
        private const int MaxSymoMatches = 2;
        private static readonly string[] LineSeparator = new[] {" | "};

        private static readonly ConcurrentDictionary<string, PlayerState> Players =
            new ConcurrentDictionary<string, PlayerState>();

        private static readonly ConcurrentDictionary<string, MatchState> Matches =
            new ConcurrentDictionary<string, MatchState>();

        private static readonly ISet<string> Moves = new HashSet<string> {"ROCK", "PAPER", "SCISSORS"};
        private static readonly Random Rand = new Random();

        private static DateTime _lastMatchCreated = DateTime.MinValue;
        private static readonly TimeSpan CreationDelay = TimeSpan.FromSeconds(3);
        private static readonly char[] SpaceSeparator = new[] {' '};
        private static readonly IEnumerable<Tuple<string, string>> Nothing = Enumerable.Empty<Tuple<string, string>>();

        private static readonly IDictionary<string, Func<string, string, string[], IEnumerable<Tuple<string, string>>>> Commands = new Dictionary<string, Func<string, string, string[], IEnumerable<Tuple<string, string>>>>
            {
                    {"HI", HandleRegister},
                    {"REGISTER", HandleRegister},
                    {"PONG", HandlePong},
                    {"NEWMATCH", HandleNewMatch},
                    {"MATCHLOG", HandleMatchlog},
                    {"MATCH", HandleMatch},
                    {"LISTMATCHES", HandleListMatches},
                    {"LEADERBOARD", HandleLeaderboard},
                    {"RESULTMATCH", HandleResultsMatch},
                    {"STAT", HandleStat}
                };

        private static string EventId()
        {
            return Tuid.New();
        }

        private static IEnumerable<Tuple<string, string>> ParseLine(String line)
        {
            Console.WriteLine(line);
            var bits = line.Split(LineSeparator, StringSplitOptions.None);
            if (bits.Length != 3)
                return Nothing;

            var eid = bits[0];
            var playerName = bits[1];
            var command = bits[2];
            var commandbits = command.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);

            Func<string, string, string[], IEnumerable<Tuple<string, string>>> handler;
            if (Commands.TryGetValue(commandbits[0], out handler))
            {
                return handler(eid, playerName, commandbits);
            }
            else
            {
                return Nothing;
            }
        }

        private static IEnumerable<Tuple<string, string>> HandleLeaderboard(string eid, string playerName,
            string[] commandbits)
        {
            yield return Tuple.Create(playerName, String.Join("; ",
                Players.Values.OrderByDescending(p => p.Score())
                    .Take(10)
                    .Select(
                        p =>
                            p.Name + " score=" + (p.Score().ToString("F3")) + " played=" +
                                (p.Wins + p.Ties + p.Losses))));
            yield break;
        }

        private static IEnumerable<Tuple<string, string>> HandleMatch(string eid, string playerName,
            string[] commandbits)
        {
            if (UpdateLastCommunicationTime(playerName) == null)
            {
                yield return Tuple.Create(playerName, "WHO ARE YOU?!!");
                yield break;
            }

            if (commandbits.Length != 3)
            {
                yield return Tuple.Create(playerName, "NO");
                yield break;
            }
            if (commandbits[0] != "MATCH")
            {
                yield return Tuple.Create(playerName, "NO!");
                yield break;
            }
            var matchID = commandbits[1];
            if (!Tuid.Valid(matchID))
            {
                yield return Tuple.Create(playerName, "NOPE");
                yield break;
            }
            var matchMove = commandbits[2];
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

        private static IEnumerable<Tuple<string, string>> HandleStat(string eid, string playerName, string[] commandbits)
        {
            var ps = UpdateLastCommunicationTime(playerName);

            if (ps != null)
            {
                yield return
                    Tuple.Create(playerName,
                        String.Format("{0} WINS {1} LOSS {2} TIE {3} SCORE", ps.Wins, ps.Losses, ps.Ties, ps.Score()))
                    ;
                yield break;
            }
            yield return Tuple.Create(playerName, "YOU DO NOT EXIST!");
            yield break;
        }

        private static IEnumerable<Tuple<string, string>> HandleResultsMatch(string eid, string playerName,
            string[] commandbits)
        {
            if (commandbits.Length < 3)
            {
                yield break;
            }
            if (playerName != VanDevBot)
            {
                yield break;
            }
            var mid = commandbits[1];
            if (!Tuid.Valid(mid))
            {
                yield break;
            }
            string mresult = string.Empty;
            if (commandbits.Length==3)
                mresult = commandbits[2];
            if (commandbits.Length == 5)
                mresult = commandbits[3];

            MatchState ms;
            if (Matches.TryGetValue(mid, out ms))
            {
                ms.Resolved = true;
                PlayerState player1;
                Players.TryGetValue(ms.Player1, out player1);
                PlayerState player2;
                Players.TryGetValue(ms.Player2, out player2);

                if (mresult == "WINS")
                {
                    if (commandbits[2] == ms.Player1)
                    {
                        if (player1 != null)
                            ++player1.Wins;
                        if (player2 != null)
                            ++player2.Losses;
                    }
                    if (commandbits[2] == ms.Player2)
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
                var message = "MATCH " + string.Join(" ", commandbits.Skip(1));
                if (player1 != null)
                {
                    yield return Tuple.Create(player1.Name, message);
                }
                if (player2 != null)
                    yield return Tuple.Create(player2.Name, message);
            }
            yield break;
        }

        private static IEnumerable<Tuple<string, string>> HandleNewMatch(string eid, string playerName,
            string[] commandbits)
        {
            if (commandbits.Length != 4)
            {
                yield break;
            }
            if (playerName != VanDevBot)
            {
                yield break;
            }
            var com = commandbits[0];
            if (com != "NEWMATCH")
            {
                yield break;
            }
            var mode = commandbits[1];
            var p1 = commandbits[2];
            var p2 = commandbits[3];

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

        private static IEnumerable<Tuple<string, string>> HandleListMatches(string eid, string playerName,
            string[] commandbits)
        {
            var p = UpdateLastCommunicationTime(playerName);
            if (p != null)
            {
                yield return
                    Tuple.Create(playerName,
                        String.Join(", ", p.Matches.Values.Where(m => !m.Resolved).Select(m => m.Id)));
            }
            yield break;
        }

        private static IEnumerable<Tuple<string, string>> HandleMatchlog(string eid, string playerName,
            string[] commandbits)
        {
            yield return Tuple.Create(playerName, "SORRY");
            yield break;
        }

        private static IEnumerable<Tuple<string, string>> HandlePong(string eid, string playerName, string[] commandbits)
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

        private static IEnumerable<Tuple<string, string>> HandleRegister(string eid, string playerName,
            string[] commandbits)
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

        private static void RockBot()
        {
            SimpleBot("VanDevRockBot", " SCISSORS");
        }

        private static void ScissorsBot()
        {
            SimpleBot("VanDevScissBot", " PAPER");
        }

        private static void PaperBot()
        {
            SimpleBot("VanDevPaperBot", " ROCK");
        }

        private static void SimpleBot(string botname, string move)
        {
            var x = new IrcClient();

            var notice = false;
            x.GotNotice +=
                (sender, eventArgs) =>
                    { notice = true; };
            x.GotMotdBegin +=
                (sender, eventArgs) =>
                { notice = true; };
            x.GotMessage +=
                (sender, eventArgs) =>
                    {
                        var message = eventArgs.Message.ToString();

                        if (eventArgs.Recipient.ToString() != botname)
                            return;

                        if (eventArgs.Sender.Nickname != VanDevBot)
                            return;

                        var bits = message.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);

                        if (bits.Length == 0) return;
                        if (bits[0] == "PING")
                        {
                            x.Message(VanDevBot, "PONG");
                            return;
                        }
                        if (bits.Length < 3) return;

                        if (bits[0] == "MATCH")
                        {
                            Thread.Sleep(9*1000);
                            x.Message(VanDevBot, "MATCH " + bits[2] + move);
                        }
                    };
            var connected = false;

            x.Closed += (sender, eventArgs) =>
                {
                    connected = false;
                    notice = false;
                };
            x.Connected += (sender, eventArgs) => { connected = true; };
            var joined = false;

            x.Connect(IrcServer);
            x.LogIn(botname, "none", botname, IrcServer);

            for (;;)
            {
                if (connected && notice && !joined)
                {
                    x.Join("#02ae4f8be6");
                    x.Message("#02ae4f8be6", "test");
                    x.Message(VanDevBot, "REGISTER");
                    joined = true;
                }

                Thread.Sleep(100);
                if (!joined)
                    continue;
            }
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
                        x.GotIrcError +=
                            (sender, eventArgs) =>
                                Console.WriteLine("GotIrcError: " + eventArgs.Error + " " + eventArgs.Data.ToString());
                        x.GotMessage +=
                            (sender, eventArgs) =>
                                {
                                    var message = eventArgs.Message.ToString();

                                    Console.WriteLine("GotMessage: " + eventArgs.Sender.Nickname + " > " +
                                        eventArgs.Recipient +
                                        " > " +
                                        message);

                                    if (eventArgs.Recipient.ToString() != VanDevBot)
                                        return;

                                    var results = Record(sw, eventArgs.Sender.Nickname, message);
                                    foreach (var r in results)
                                        x.Message(r.Item1, r.Item2);
                                };
                        x.GotNotice +=
                            (sender, eventArgs) =>
                            { notice = true; };
                        x.GotMotdBegin +=
                            (sender, eventArgs) =>
                            { notice = true; };

                        var joined = false;
                        
                        x.Closed += (sender, eventArgs) =>
                            {
                                Console.WriteLine("Closed");
                                connected = false;
                                notice = false;
                                joined = false;
                                Thread.Sleep(5000);
                                x.Connect(IrcServer);
                                x.LogIn(VanDevBot, "none", VanDevBot, IrcServer);
                            };
                        x.Connected += (sender, eventArgs) =>
                            {
                                Console.WriteLine("Connected");
                                connected = true;
                            };
                        x.Connect(IrcServer);
                        x.LogIn(VanDevBot, "none", VanDevBot, IrcServer);

                        for (;;)
                        {
                            if (connected && notice && !joined)
                            {
                                Console.WriteLine("Attempting to join");
                                x.Join("#02ae4f8be6");
                                x.Message("#02ae4f8be6", "test");
                                joined = true;
                                new Thread(RockBot).Start();
                                new Thread(ScissorsBot).Start();
                                new Thread(PaperBot).Start();
                            }

                            Thread.Sleep(100);
                            if (!joined)
                                continue;

                            var newMatch = PickTwoPlayersForNewMatch();
                            if (newMatch != null)
                            {
                                foreach (
                                    var r in
                                        Record(sw, VanDevBot,
                                            "NEWMATCH DEMO " + " " + newMatch.Item1 + " " + newMatch.Item2))
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
                                    Matches.Values.Where(TryResolve))
                            {
                                foreach (var r in Record(sw, VanDevBot, "RESULTMATCH " + ms.Id + " " + ms.Result))
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
            var line = String.Format("{0} | {1} | {2}", eventID, nickname, message);

            sw.WriteLine(line);
            sw.Flush();

            return ParseLine(line);
        }

        private static T[] InplaceShuffle<T>(this T[] arr)
        {
            var length = arr.Length;
            for (var i = 0; i < length; ++i)
            {
                Swap(ref arr, i, Rand.Next(i, length));
            }
            return arr;
        }

        private static Tuple<string, string> PickTwoPlayersForNewMatch()
        {
            if (_lastMatchCreated.Add(CreationDelay) > GetTime()) return null;

            PlayerState[] possibles;
            possibles =
                Players.Values.ToArray()
                    .InplaceShuffle()
                    .Where(x => x.Alive && x.AliveMatchCount < MaxSymoMatches)
                    .ToArray();

            if (possibles.Length < 2)
            {
                return null;
            }

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
                var total = Wins + Losses;
                if (total == 0) return 0;
                return Wins/(double) total;
            }
        }

        private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(60);
        private const string IrcServer = "ec2-54-173-131-206.compute-1.amazonaws.com";

        static internal bool TryResolve(IMatchState ms)
        {
            if (ms.Resolved)
                return false;

            PlayerState p1;
            PlayerState p2;
            Players.TryGetValue(ms.Player1, out p1);
            Players.TryGetValue(ms.Player2, out p2);

            if (p1 == null)
            {
                if (p2 == null)
                {
                    return true;
                }
                ms.Result = p2.Name + " WINS FORFEIT";
                ms.Log.Add(ms.Result);
                return true;
            }
            if (p2 == null)
            {
                ms.Result = p1.Name + " WINS FORFEIT";
                ms.Log.Add(ms.Result);
                return true;
            }

            if (ms.Player1Move != null && ms.Player2Move != null)
            {
                if (ms.Player1Move == ms.Player2Move)
                {
                    ms.Result = "TIE";
                    ms.Log.Add(ms.Result);
                }
                else
                {
                    if ((ms.Player1Move == "ROCK" && ms.Player2Move == "SCISSORS")
                        || (ms.Player1Move == "SCISSORS" && ms.Player2Move == "PAPER")
                        || (ms.Player1Move == "PAPER" && ms.Player2Move == "ROCK"))
                    {
                        ms.Result = p1.Name + " WINS " + ms.Player1Move;
                        ms.Log.Add(ms.Result);
                    }
                    else
                    {
                        ms.Result = p2.Name + " WINS " + ms.Player2Move;
                        ms.Log.Add(ms.Result);
                    }
                }
                return true;
            }
            var now = GetTime();
            if (ms.Started.Add(MatchTimeout) < now)
            {
                ms.Log.Add("TIMEOUT");
                if (ms.Player1Move == null && ms.Player2Move != null)
                {
                    ms.Result = p1.Name + " WINS FORFEIT";
                    ms.Log.Add(ms.Result);
                    return true;
                }
                if (ms.Player2Move != null && ms.Player1Move != null)
                {
                    ms.Result = p2.Name + " WINS FORFEIT";
                    ms.Log.Add(ms.Result);
                    return true;
                }
                ms.Result = "TIE";
                ms.Log.Add("TIE");
                return true;
            }
            return false;
        }
    }

    internal class MatchState : IMatchState
    {
        private readonly object _lock = new object();
        public IList<string> Log { get; private set; }
        public DateTime Started { get; private set; }

        public MatchState(string mode, string id, string player1, string player2)
        {
            Log = new List<string>();
            Mode = mode;
            Player1 = player1;
            Player2 = player2;
            Id = id;
            Started = Tuid.When(Id);
        }

        public string Result { get; set; }
        public string Player1 { get; private set; }
        public string Player2 { get; private set; }
        public string Player1Move { get; private set; }
        public string Player2Move { get; private set; }
        public string Mode { get; private set; }
        public string Id { get; private set; }

        public bool Resolved { get; set; }

        public string Move(string player, string move)
        {
            lock (_lock)
            {
                Log.Add("MOVE " + player + " " + move);
                if (player == Player1)
                {
                    if (Player1Move == null)
                    {
                        Player1Move = move;
                        return "OK!";
                    }
                    return "NICETRY!";
                }
                if (player == Player2)
                {
                    if (Player2Move == null)
                    {
                        Player2Move = move;
                        return "OK!";
                    }
                    return "NICETRY!";
                }
                return "HAHA, YOU FUNNY!";
            }
        }
    }

    internal interface IMatchState
    {
        string Result { get; set; }
        string Player1 { get; }
        string Player2 { get; }
        string Player1Move { get; }
        string Player2Move { get; }
        string Mode { get; }
        string Id { get; }
        bool Resolved { get; set; }
        IList<string> Log { get; }
        DateTime Started { get; }
        string Move(string player, string move);
    }
}
