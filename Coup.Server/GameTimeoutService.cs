using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coup.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coup.Server
{
    public class GameTimeoutService : BackgroundService
    {
        private readonly GameStore _store;
        private readonly IHubContext<CoupHub> _hubContext;
        private readonly ILogger<GameTimeoutService> _logger;
        private const int CHECK_INTERVAL_SECONDS = 5;
        private const int PENDING_ACTION_TIMEOUT_SECONDS = 30;

        public GameTimeoutService(GameStore store, IHubContext<CoupHub> hubContext, ILogger<GameTimeoutService> logger)
        {
            _store = store;
            _hubContext = hubContext;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("GameTimeoutService started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAllGamesForTimeout();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking game timeouts");
                }

                await Task.Delay(TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS), stoppingToken);
            }

            _logger.LogInformation("GameTimeoutService stopped");
        }

        private async Task CheckAllGamesForTimeout()
        {
            foreach (var kvp in _store.Games)
            {
                var gameId = kvp.Key;
                var game = kvp.Value;

                // Get timeout from variant (fallback to default if not set)
                var timeoutSeconds = game.Variant?.ActionTimeoutSeconds ?? PENDING_ACTION_TIMEOUT_SECONDS;

                // Check for PendingInfluenceLoss timeout
                if (game.PendingInfluenceLoss != null)
                {
                    var elapsed = DateTime.UtcNow - game.PendingInfluenceLoss.StartTime;
                    if (elapsed.TotalSeconds >= timeoutSeconds)
                    {
                        await ProcessInfluenceLossTimeout(gameId, game);
                        continue;
                    }
                }

                // Check for Pending action timeout
                if (game.Pending == null || game.PendingStartTime == null)
                    continue;

                var actionElapsed = DateTime.UtcNow - game.PendingStartTime.Value;
                if (actionElapsed.TotalSeconds < timeoutSeconds)
                    continue;

                // Timeout expired - process it
                await ProcessTimeout(gameId, game);
            }
        }

        private async Task ProcessInfluenceLossTimeout(string gameId, GameState game)
        {
            var pending = game.PendingInfluenceLoss;
            if (pending == null) return;

            _logger.LogInformation("Processing influence loss timeout for game {GameId}", gameId);

            var player = game.Players.FirstOrDefault(p => p.ConnectionId == pending.PlayerConnectionId);
            if (player == null || !player.IsAlive)
            {
                game.PendingInfluenceLoss = null;
                return;
            }

            // Get player's roles and randomly choose one
            if (_store.PlayerRoles.TryGetValue(player.ConnectionId, out var roles) && roles.Count > 0)
            {
                var rng = Random.Shared;
                var roleToLose = roles[rng.Next(roles.Count)];

                // Remove the chosen role
                CoupHub.RemoveSpecificInfluence(player, roleToLose, _store);
                game.Log.Add($"{player.Name} loses {roleToLose} ({pending.Reason}) - timeout auto-chose.");

                // Send updated roles
                if (_store.PlayerRoles.TryGetValue(player.ConnectionId, out var updatedRoles))
                {
                    await _hubContext.Clients.Client(player.ConnectionId).SendAsync("YourRoles", updatedRoles);
                }

                // Clear pending
                game.PendingInfluenceLoss = null;

                // Check end game
                CoupHub.CheckEndGameStatic(game);
                if (game.GameEnded)
                {
                    await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                    await _hubContext.Clients.Group(gameId).SendAsync("GameEnded", game.WinnerName);
                    return;
                }

                // Continue game flow after influence loss
                await ContinueAfterInfluenceLoss(gameId, game);
            }
        }

        private async Task ContinueAfterInfluenceLoss(string gameId, GameState game)
        {
            var p = game.Pending;

            // If there's a pending action in BlockClaim phase, process it
            if (p != null && p.Phase == PendingPhase.BlockClaim)
            {
                // Block challenge succeeded - execute the blocked action
                if (p.Action == ActionType.Assassinate)
                {
                    var target = game.Players.FirstOrDefault(x => x.ConnectionId == p.TargetConnectionId);
                    if (target != null && target.IsAlive)
                    {
                        game.Log.Add($"Block fails. {target.Name} loses 1 influence (assassinated).");
                        CoupHub.RequestInfluenceLoss(game, target, "Assassinated");
                        await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                        await _hubContext.Clients.Client(target.ConnectionId).SendAsync("ChooseInfluence", "You were assassinated. Choose a role to lose.");
                        return;
                    }
                }
                else if (p.Action == ActionType.ForeignAid)
                {
                    var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                    if (actor != null && actor.IsAlive)
                    {
                        actor.Coins += 2; // FOREIGN_AID_GAIN
                        game.Log.Add($"Block fails. {actor.Name} takes Foreign Aid (+2).");
                    }
                }
                else if (p.Action == ActionType.Steal)
                {
                    var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                    var target = game.Players.FirstOrDefault(x => x.ConnectionId == p.TargetConnectionId);
                    if (actor != null && actor.IsAlive && target != null && target.IsAlive)
                    {
                        var amount = Math.Min(2, target.Coins); // STEAL_MAX
                        if (amount > 0)
                        {
                            target.Coins -= amount;
                            actor.Coins += amount;
                            game.Log.Add($"Block fails. {actor.Name} steals {amount} coin(s) from {target.Name}.");
                        }
                    }
                }

                game.Pending = null;
                game.PendingStartTime = null;
                CoupHub.AdvanceTurnStatic(game);
                await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                var next = game.Players[game.CurrentPlayerIndex];
                await _hubContext.Clients.Client(next.ConnectionId).SendAsync("YourTurn");
                return;
            }

            // If there's a pending action in ActionClaim phase, handle it
            if (p != null && p.Phase == PendingPhase.ActionClaim)
            {
                // Challenge on action succeeded - action fails
                game.Pending = null;
                game.PendingStartTime = null;
                CoupHub.AdvanceTurnStatic(game);
                await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                var next = game.Players[game.CurrentPlayerIndex];
                await _hubContext.Clients.Client(next.ConnectionId).SendAsync("YourTurn");
                return;
            }

            // No pending action - just advance turn
            CoupHub.AdvanceTurnStatic(game);
            await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);
            var nextPlayer = game.Players[game.CurrentPlayerIndex];
            await _hubContext.Clients.Client(nextPlayer.ConnectionId).SendAsync("YourTurn");
        }

        private async Task ProcessTimeout(string gameId, GameState game)
        {
            _logger.LogInformation("Processing timeout for game {GameId}", gameId);

            game.Log.Add($"Timeout: pending action auto-resolved (no response).");

            var p = game.Pending;
            if (p == null) return;

            if (p.Phase == PendingPhase.ActionClaim)
            {
                await ProcessActionClaimTimeout(gameId, game, p);
            }
            else if (p.Phase == PendingPhase.BlockClaim)
            {
                await ProcessBlockClaimTimeout(gameId, game, p);
            }

            await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);

            if (!game.GameEnded && game.Players.Count > 0)
            {
                var next = game.Players[game.CurrentPlayerIndex];
                await _hubContext.Clients.Client(next.ConnectionId).SendAsync("YourTurn");
            }
            else if (game.GameEnded)
            {
                await _hubContext.Clients.Group(gameId).SendAsync("GameEnded", game.WinnerName);
            }
        }

        private async Task ProcessActionClaimTimeout(string gameId, GameState game, PendingAction p)
        {
            if (p.Action == ActionType.Tax)
            {
                var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                if (actor != null && actor.IsAlive)
                {
                    actor.Coins += 3; // TAX_GAIN
                    game.Log.Add($"{actor.Name} takes Tax (+3).");
                }
                game.Pending = null;
                game.PendingStartTime = null;
                CoupHub.AdvanceTurnStatic(game);
            }
            else if (p.Action == ActionType.ForeignAid)
            {
                var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                if (actor != null && actor.IsAlive)
                {
                    actor.Coins += 2; // FOREIGN_AID_GAIN
                    game.Log.Add($"{actor.Name} takes Foreign Aid (+2).");
                }
                game.Pending = null;
                game.PendingStartTime = null;
                CoupHub.AdvanceTurnStatic(game);
            }
            else if (p.Action == ActionType.Assassinate)
            {
                var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                if (actor != null && actor.IsAlive)
                {
                    actor.Coins -= 3; // ASSASSINATE_COST
                }
                p.Phase = PendingPhase.BlockClaim;
                p.BlockerConnectionId = p.TargetConnectionId;
                p.BlockClaimedRole = null;
                p.BlockResponded.Clear();
                if (actor != null) p.BlockResponded.Add(actor.ConnectionId);
                game.PendingStartTime = DateTime.UtcNow;
            }
            else if (p.Action == ActionType.Steal)
            {
                p.Phase = PendingPhase.BlockClaim;
                p.BlockerConnectionId = p.TargetConnectionId;
                p.BlockClaimedRole = null;
                p.BlockResponded.Clear();
                var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                if (actor != null) p.BlockResponded.Add(actor.ConnectionId);
                game.PendingStartTime = DateTime.UtcNow;
            }
            else if (p.Action == ActionType.Exchange)
            {
                await ProcessExchangeAction(gameId, game, p);
                game.Pending = null;
                game.PendingStartTime = null;
                CoupHub.AdvanceTurnStatic(game);
            }
        }

        private async Task ProcessBlockClaimTimeout(string gameId, GameState game, PendingAction p)
        {
            if (p.Action == ActionType.ForeignAid)
            {
                game.Log.Add($"Duke block stands (timeout). Foreign Aid is prevented.");
            }
            else if (p.Action == ActionType.Assassinate)
            {
                // Check if a block was claimed
                if (p.BlockClaimedRole.HasValue)
                {
                    // Block was claimed (Contessa) - timeout means everyone accepts the block
                    game.Log.Add($"Contessa block stands (timeout). Assassination is prevented.");
                }
                else
                {
                    // No block claimed - timeout means target didn't block, assassination succeeds
                    var target = game.Players.FirstOrDefault(x => x.ConnectionId == p.TargetConnectionId);
                    if (target != null && target.IsAlive)
                    {
                        game.Log.Add($"{target.Name} was assassinated (timeout) and must choose a card to lose.");
                        game.Pending = null;
                        game.PendingStartTime = null;

                        CoupHub.RequestInfluenceLoss(game, target, "Assassinated");
                        await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                        await _hubContext.Clients.Client(target.ConnectionId).SendAsync("ChooseInfluence", "You were assassinated. Choose a role to lose.");
                        return; // Don't continue - wait for card choice
                    }
                }
            }
            else if (p.Action == ActionType.Steal)
            {
                // Check if a block was claimed
                if (p.BlockClaimedRole.HasValue)
                {
                    // Block was claimed (Captain/Ambassador) - timeout means everyone accepts the block
                    game.Log.Add($"{p.BlockClaimedRole} block stands (timeout). Steal is prevented.");
                }
                else
                {
                    // No block claimed - timeout means target didn't block, steal succeeds
                    var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                    var target = game.Players.FirstOrDefault(x => x.ConnectionId == p.TargetConnectionId);
                    if (actor != null && actor.IsAlive && target != null && target.IsAlive)
                    {
                        var amount = Math.Min(2, target.Coins); // STEAL_MAX
                        if (amount > 0)
                        {
                            target.Coins -= amount;
                            actor.Coins += amount;
                            game.Log.Add($"{actor.Name} steals {amount} coin(s) from {target.Name}.");
                        }
                    }
                }
            }

            game.Pending = null;
            game.PendingStartTime = null;
            CoupHub.AdvanceTurnStatic(game);
        }

        private async Task ProcessExchangeAction(string gameId, GameState game, PendingAction p)
        {
            var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
            if (actor == null || !actor.IsAlive || !_store.GameDecks.TryGetValue(gameId, out var deck))
                return;

            var drawnCards = new List<Role>();
            for (int i = 0; i < 2 && deck.Count > 0; i++)
            {
                var card = deck[^1];
                deck.RemoveAt(deck.Count - 1);
                drawnCards.Add(card);
            }

            if (_store.PlayerRoles.TryGetValue(actor.ConnectionId, out var currentRoles))
            {
                var allCards = new List<Role>(currentRoles);
                allCards.AddRange(drawnCards);

                var rngShuffle = Random.Shared;
                for (int i = allCards.Count - 1; i > 0; i--)
                {
                    int j = rngShuffle.Next(i + 1);
                    (allCards[i], allCards[j]) = (allCards[j], allCards[i]);
                }

                // Garder autant de cartes que le joueur a d'influence
                int cardsToKeep = Math.Min(actor.InfluenceCount, allCards.Count);
                var keptCards = allCards.Take(cardsToKeep).ToList();
                var returnedCards = allCards.Skip(cardsToKeep).ToList();
                deck.AddRange(returnedCards);

                var rng = Random.Shared;
                for (int i = deck.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (deck[i], deck[j]) = (deck[j], deck[i]);
                }

                _store.PlayerRoles[actor.ConnectionId] = keptCards;
                await _hubContext.Clients.Client(actor.ConnectionId).SendAsync("YourRoles", keptCards);

                game.Log.Add($"{actor.Name} exchanges roles (Ambassador).");
            }
        }
    }
}
