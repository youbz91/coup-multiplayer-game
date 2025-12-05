using System;
using System.Linq;
using System.Threading.Tasks;
using Coup.Shared;
using Microsoft.AspNetCore.SignalR.Client;

namespace Coup.Client.ConsoleApp
{
    class Program
    {
        static GameState _state = new();
        static string _me = "";
        static bool _myTurn = false;
        static List<Role> _myRoles = new();
        static bool _mustChooseCard = false;
        static string _chooseCardReason = "";


        static async Task Main(string[] args)
        {
            Console.WriteLine("Your name?");
            var name = Console.ReadLine();
            _me = string.IsNullOrWhiteSpace(name) ? $"You_{Guid.NewGuid().ToString("N")[..4]}" : name;

            Console.WriteLine("Game ID? (default: room1)");
            var gameId = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(gameId)) gameId = "room1";

            var hubUrl = "http://localhost:5076/couphub";
            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            connection.On<GameState>("GameStateUpdated", gs =>
            {
                _state = gs;

                // Recalcule si c'est mon tour à partir de l'état global
                var me = _state.Players.FirstOrDefault(p => p.Name == _me);
                if (me != null)
                {
                    var myIndex = _state.Players.IndexOf(me);
                    _myTurn = (myIndex == _state.CurrentPlayerIndex) && ! _state.GameEnded && _state.GameStarted;
                }
                else
                {
                    _myTurn = false;
                }

                // Check if influence loss is still pending for us
                if (_state.PendingInfluenceLoss == null || (_state.PendingInfluenceLoss.PlayerConnectionId != me?.ConnectionId))
                {
                    _mustChooseCard = false;
                    _chooseCardReason = "";
                }

                Render();
            });

            connection.On("YourTurn", () =>
            {
                _myTurn = true;
                Render(); // le Render affichera que c'est ton tour
            });

            connection.On("RematchReady", () =>
            {
                Console.WriteLine("\n[Rematch Ready] Game has been reset! Host can type 'start' to begin.");
                Render();
            });

            connection.On<string>("Error", msg =>
            {
                Console.WriteLine($"[Server Error] {msg}");
            });
            
            connection.On<List<Role>>("YourRoles", roles =>
            {
                _myRoles = roles;
            });

            connection.On<string>("ChooseInfluence", message =>
            {
                _mustChooseCard = true;
                _chooseCardReason = message;
                Console.WriteLine($"\n[!] {message}");
                Console.WriteLine($"Your roles: {string.Join(", ", _myRoles)}");
                Console.WriteLine("Type 'lose <role>' to choose which card to lose (e.g. 'lose duke')");
                Render();
            });


            await connection.StartAsync();
            await connection.InvokeAsync("JoinLobby", gameId, _me);

            Console.WriteLine("Type 'start' to start game (host) or press Enter to wait...");
            var cmd = Console.ReadLine();
            if (cmd?.Trim().Equals("start", StringComparison.OrdinalIgnoreCase) == true)
            {
                await connection.InvokeAsync("StartGame", gameId);
            }

            // Boucle input simple
            while (true)
            {
                var line = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.Equals("q", StringComparison.OrdinalIgnoreCase)) break;
                bool isHelp = line.Equals("help", StringComparison.OrdinalIgnoreCase);
                bool isStart = line.Equals("start", StringComparison.OrdinalIgnoreCase);
                bool isRematch = line.Equals("rematch", StringComparison.OrdinalIgnoreCase);
                bool isChallenge = line.Equals("challenge", StringComparison.OrdinalIgnoreCase);
                bool isPass = line.Equals("pass", StringComparison.OrdinalIgnoreCase);
                bool isBlockContessa = line.Equals("block contessa", StringComparison.OrdinalIgnoreCase);
                bool isBlockDuke = line.Equals("block duke", StringComparison.OrdinalIgnoreCase);
                bool isBlockCaptain = line.Equals("block captain", StringComparison.OrdinalIgnoreCase);
                bool isBlockAmbassador = line.Equals("block ambassador", StringComparison.OrdinalIgnoreCase);
                bool isLoseCard = line.StartsWith("lose ", StringComparison.OrdinalIgnoreCase);

                // Handle choosing which card to lose
                if (_mustChooseCard && isLoseCard)
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        Console.WriteLine("Usage: lose <role> (e.g., 'lose duke')");
                        continue;
                    }

                    var roleName = parts[1];
                    if (Enum.TryParse<Role>(roleName, true, out var role))
                    {
                        if (!_myRoles.Contains(role))
                        {
                            Console.WriteLine($"You don't have {role}. Your roles: {string.Join(", ", _myRoles)}");
                            continue;
                        }

                        await connection.InvokeAsync("ChooseCardToLose", role);
                        _mustChooseCard = false;
                        _chooseCardReason = "";
                        continue;
                    }
                    else
                    {
                        Console.WriteLine($"Invalid role. Valid roles: {string.Join(", ", Enum.GetValues<Role>())}");
                        continue;
                    }
                }

                if (!_myTurn && !isHelp && !isStart && !isRematch && !isChallenge && !isPass && !isBlockContessa && !isBlockDuke && !isBlockCaptain && !isBlockAmbassador && !isLoseCard)
                {
                    Console.WriteLine("It's not your turn.");
                    continue;
                }

                // Actions rapides
                if (line == "1")
                {
                    await connection.InvokeAsync("PerformAction", new ActionDto { Action = ActionType.Income });
                }
                else if (line == "2")
                {
                    await connection.InvokeAsync("PerformAction", new ActionDto { Action = ActionType.ForeignAid });
                }
                else if (line.StartsWith("3")) // Coup <targetIndex>
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) { Console.WriteLine("Usage: 3 <targetIndex>"); continue; }

                    if (!int.TryParse(parts[1], out var ti)) { Console.WriteLine("Bad index"); continue; }
                    var me = _state.Players.FirstOrDefault(p => p.Name == _me);
                    if (me == null) { Console.WriteLine("Not in game."); continue; }
                    var target = _state.Players.ElementAtOrDefault(ti);
                    if (target == null) { Console.WriteLine("Invalid target"); continue; }
                    if (target.ConnectionId == me.ConnectionId) { Console.WriteLine("You cannot coup yourself"); continue; }

                    await connection.InvokeAsync("PerformAction", new ActionDto
                    {
                        Action = ActionType.Coup,
                        TargetConnectionId = target.ConnectionId
                    });
                }
                else if (line == "4")
                {
                    await connection.InvokeAsync("PerformAction", new ActionDto { Action = ActionType.Tax });
                }
                else if (line.StartsWith("5"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        Console.WriteLine("Usage: 5 <targetIndex>");
                        continue;
                    }

                    if (!int.TryParse(parts[1], out var ti))
                    {
                        Console.WriteLine("Bad index");
                        continue;
                    }

                    var target = _state.Players.ElementAtOrDefault(ti);
                    if (target == null)
                    {
                        Console.WriteLine("Invalid target");
                        continue;
                    }

                    var meLocal = _state.Players.FirstOrDefault(p => p.Name == _me);
                    if (meLocal != null && target.ConnectionId == meLocal.ConnectionId)
                    {
                        Console.WriteLine("You cannot assassinate yourself");
                        continue;
                    }

                    await connection.InvokeAsync("PerformAction", new ActionDto
                    {
                        Action = ActionType.Assassinate,
                        TargetConnectionId = target.ConnectionId
                    });
                }
                else if (line.StartsWith("6"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        Console.WriteLine("Usage: 6 <targetIndex>");
                        continue;
                    }

                    if (!int.TryParse(parts[1], out var ti))
                    {
                        Console.WriteLine("Bad index");
                        continue;
                    }

                    var target = _state.Players.ElementAtOrDefault(ti);
                    if (target == null)
                    {
                        Console.WriteLine("Invalid target");
                        continue;
                    }

                    var meLocal = _state.Players.FirstOrDefault(p => p.Name == _me);
                    if (meLocal != null && target.ConnectionId == meLocal.ConnectionId)
                    {
                        Console.WriteLine("You cannot steal from yourself");
                        continue;
                    }

                    await connection.InvokeAsync("PerformAction", new ActionDto
                    {
                        Action = ActionType.Steal,
                        TargetConnectionId = target.ConnectionId
                    });
                }
                else if (line == "7")
                {
                    await connection.InvokeAsync("PerformAction", new ActionDto { Action = ActionType.Exchange });
                }
                else if (line.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    ShowMenu();
                }
                else if (line.Equals("pass", StringComparison.OrdinalIgnoreCase))
                {
                    if (_state.Pending == null)
                    {
                        Console.WriteLine("No pending action to pass on.");
                    }
                    else
                    {
                        await connection.InvokeAsync("PassPending");
                    }
                }
                else if (line.Equals("challenge", StringComparison.OrdinalIgnoreCase))
                {
                    if (_state.Pending == null)
                    {
                        Console.WriteLine("No pending action to challenge.");
                    }
                    else
                    {
                        await connection.InvokeAsync("Challenge");
                    }
                }
                else if (line.Equals("block contessa", StringComparison.OrdinalIgnoreCase))
                {
                    if (_state.Pending == null)
                    {
                        Console.WriteLine("No pending action to block.");
                    }
                    else
                    {
                        await connection.InvokeAsync("BlockPendingContessa");
                    }
                }
                else if (line.Equals("block duke", StringComparison.OrdinalIgnoreCase))
                {
                    if (_state.Pending == null)
                    {
                        Console.WriteLine("No pending action to block.");
                    }
                    else
                    {
                        await connection.InvokeAsync("BlockDuke");
                    }
                }
                else if (line.Equals("block captain", StringComparison.OrdinalIgnoreCase))
                {
                    if (_state.Pending == null)
                    {
                        Console.WriteLine("No pending action to block.");
                    }
                    else
                    {
                        await connection.InvokeAsync("BlockCaptainAmbassador", Role.Captain);
                    }
                }
                else if (line.Equals("block ambassador", StringComparison.OrdinalIgnoreCase))
                {
                    if (_state.Pending == null)
                    {
                        Console.WriteLine("No pending action to block.");
                    }
                    else
                    {
                        await connection.InvokeAsync("BlockCaptainAmbassador", Role.Ambassador);
                    }
                }
                else if (line.Equals("start", StringComparison.OrdinalIgnoreCase))
                {
                    var me = _state.Players.FirstOrDefault(p => p.Name == _me);
                    if (me == null) { Console.WriteLine("Not in game yet."); continue; }

                    if (_state.HostConnectionId != me.ConnectionId)
                    {
                        Console.WriteLine("Only host can start the game.");
                        continue;
                    }

                    await connection.InvokeAsync("StartGame", _state.GameId);
                }
                else if (line.Equals("rematch", StringComparison.OrdinalIgnoreCase))
                {
                    var me = _state.Players.FirstOrDefault(p => p.Name == _me);
                    if (me == null) { Console.WriteLine("Not in game yet."); continue; }

                    if (!_state.GameEnded)
                    {
                        Console.WriteLine("Cannot rematch - game is still in progress or hasn't started.");
                        continue;
                    }

                    if (_state.HostConnectionId != me.ConnectionId)
                    {
                        Console.WriteLine("Only host can initiate rematch.");
                        continue;
                    }

                    await connection.InvokeAsync("RematchGame", _state.GameId);
                    Console.WriteLine("Rematch requested. Type 'start' to begin when ready.");
                }

            }
        }

        static void Render()
        {
            Console.Clear();

            Console.WriteLine($"Game: {_state.GameId}    Started: {_state.GameStarted}    Ended: {_state.GameEnded}");
            if (!string.IsNullOrEmpty(_state.HostConnectionId))
            {
                var hostName = _state.Players
                    .FirstOrDefault(p => p.ConnectionId == _state.HostConnectionId)?.Name;
                Console.WriteLine($"Host: {hostName}");
            }

            // Players
            for (int i = 0; i < _state.Players.Count; i++)
            {
                var p = _state.Players[i];
                var mark = (i == _state.CurrentPlayerIndex) ? "▶" : " ";
                var dead = p.IsAlive ? "" : " (X)";
                Console.WriteLine($"{mark} [{i}] {p.Name}  Coins:{p.Coins}  Influence:{p.InfluenceCount}{dead}");
            }

            // Info joueur local
            var me = _state.Players.FirstOrDefault(p => p.Name == _me);
            if (me != null)
            {
                Console.WriteLine();
                Console.WriteLine($"Your roles: {string.Join(", ", _myRoles)}");
                Console.WriteLine($"Your coins: {me.Coins}, Your influence: {me.InfluenceCount}");
            }

            // Tour courant (clair pour tout le monde)
            var current = _state.CurrentPlayer;
            if (current != null && _state.GameStarted && !_state.GameEnded)
            {
                Console.WriteLine();
                if (_myTurn)
                    Console.WriteLine($"Current turn: {current.Name}  (YOUR TURN)");
                else
                    Console.WriteLine($"Current turn: {current.Name}  (wait for their move)");
            }
            if (_state.Pending != null)
            {
                var pa = _state.Pending;
                var actor = _state.Players.FirstOrDefault(p => p.ConnectionId == pa.ActorConnectionId);
                if (actor != null)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Pending: {actor.Name} claims {pa.ClaimedRole} for {pa.Action}.");

                    var others = _state.Players
                        .Where(p => p.IsAlive && p.ConnectionId != pa.ActorConnectionId)
                        .ToList();

                    var responded = pa.Responded ?? new HashSet<string>();
                    var waiting = others
                        .Where(p => !responded.Contains(p.ConnectionId))
                        .Select(p => p.Name)
                        .ToList();

                    if (waiting.Count > 0)
                    {
                        Console.WriteLine($"Waiting for: {string.Join(", ", waiting)} (they can 'challenge' or 'pass')");
                    }
                    else
                    {
                        Console.WriteLine($"All players responded. Action will auto-resolve.");
                    }
                }
            }
            Console.WriteLine();
            Console.WriteLine("Log (last 10):");
            foreach (var line in _state.Log.TakeLast(10))
            {
                Console.WriteLine(" - " + line);
            }

            Console.WriteLine();
            if (_mustChooseCard)
            {
                Console.WriteLine($"[!!!] YOU MUST CHOOSE A CARD TO LOSE: {_chooseCardReason}");
                Console.WriteLine($"Your roles: {string.Join(", ", _myRoles)}");
                Console.WriteLine("Type 'lose <role>' (e.g., 'lose duke', 'lose assassin')");
            }
            else if (_myTurn && !_state.GameEnded && _state.GameStarted)
            {
                Console.WriteLine("Your turn. Type 'help' for actions or 'q' to quit.");
            }
            else
            {
                Console.WriteLine("Not your turn. Type 'help' to see actions or 'q' to quit.");
            }
        }

        static void ShowMenu()
        {
            Console.WriteLine("=== ACTIONS ===");
            Console.WriteLine("[1] Income (+1 coin)");
            Console.WriteLine("[2] Foreign Aid (+2 coins, can be blocked by Duke)");
            Console.WriteLine("[3 <targetIndex>] Coup (cost 7, mandatory at 10+)");
            Console.WriteLine("[4] Tax (Duke) (+3 coins)");
            Console.WriteLine("[5 <targetIndex>] Assassinate (cost 3, Assassin)");
            Console.WriteLine("[6 <targetIndex>] Steal (Captain, up to 2 coins)");
            Console.WriteLine("[7] Exchange (Ambassador, swap roles)");
            Console.WriteLine();
            Console.WriteLine("=== RESPONSES ===");
            Console.WriteLine("'challenge' - Challenge a claim");
            Console.WriteLine("'pass' - Accept the action");
            Console.WriteLine("'block contessa' - Block assassination (Contessa)");
            Console.WriteLine("'block duke' - Block Foreign Aid (Duke)");
            Console.WriteLine("'block captain' - Block steal (Captain)");
            Console.WriteLine("'block ambassador' - Block steal (Ambassador)");
            Console.WriteLine();
            Console.WriteLine("=== INFLUENCE LOSS ===");
            Console.WriteLine("'lose <role>' - Choose which card to lose (e.g., 'lose duke')");
            Console.WriteLine();
            Console.WriteLine("=== GAME CONTROL ===");
            Console.WriteLine("'start' (host only) - Start the game");
            Console.WriteLine("'rematch' (host only) - Start a rematch after game ends");
            Console.WriteLine("'help' - Show this menu");
            Console.WriteLine("'q' - Quit");
        }
    }
}
