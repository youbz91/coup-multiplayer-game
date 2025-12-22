using Coup.Shared;
using Microsoft.AspNetCore.SignalR;

namespace Coup.Server.Services;

/// <summary>
/// Core game logic service that can be used by both CoupHub and bot systems.
/// Contains all game action implementations without Hub dependencies.
/// </summary>
public class GameActionService
{
    private readonly GameStore _store;
    private readonly IHubContext<CoupHub> _hubContext;

    // Game constants (from CoupHub)
    private const int INCOME_GAIN = 1;
    private const int FOREIGN_AID_GAIN = 2;
    private const int TAX_GAIN = 3;
    private const int COUP_COST = 7;
    private const int ASSASSINATE_COST = 3;
    private const int MANDATORY_COUP_COINS = 10;
    private const int STARTING_COINS = 2;
    private const int STARTING_INFLUENCE = 2;

    public GameActionService(GameStore store, IHubContext<CoupHub> hubContext)
    {
        _store = store;
        _hubContext = hubContext;
    }

    public async Task PerformActionAsync(GameState game, PlayerState actor, ActionDto action, string gameId)
    {
        // Validate game state
        if (!game.GameStarted || game.GameEnded) return;

        // Check turn
        var current = game.Players[game.CurrentPlayerIndex];
        if (current.ConnectionId != actor.ConnectionId) return;

        // Check mandatory coup
        if (actor.Coins >= MANDATORY_COUP_COINS && action.Action != ActionType.Coup) return;

        // Process action
        switch (action.Action)
        {
            case ActionType.Income:
                actor.Coins += INCOME_GAIN;
                game.Log.Add($"{actor.Name} takes Income (+{INCOME_GAIN}).");
                AdvanceTurn(game);
                break;

            case ActionType.ForeignAid:
                // Create pending action for blocking
                game.Pending = new PendingAction
                {
                    Action = ActionType.ForeignAid,
                    ActorConnectionId = actor.ConnectionId,
                    Phase = PendingPhase.ActionClaim
                };
                game.PendingStartTime = DateTime.UtcNow;
                game.Log.Add($"{actor.Name} attempts Foreign Aid (+{FOREIGN_AID_GAIN} if not blocked).");
                break;

            case ActionType.Coup:
                if (actor.Coins < COUP_COST) return;
                actor.Coins -= COUP_COST;
                var coupTarget = game.Players.FirstOrDefault(p => p.ConnectionId == action.TargetConnectionId);
                if (coupTarget == null || !coupTarget.IsAlive) return;
                game.Log.Add($"{actor.Name} launches a Coup on {coupTarget.Name} (-{COUP_COST} coins).");
                RequestInfluenceLoss(game, coupTarget, $"Coup by {actor.Name}");
                break;

            case ActionType.Tax:
                game.Pending = new PendingAction
                {
                    Action = ActionType.Tax,
                    ActorConnectionId = actor.ConnectionId,
                    ClaimedRole = Role.Duke,
                    Phase = PendingPhase.ActionClaim
                };
                game.PendingStartTime = DateTime.UtcNow;
                game.Log.Add($"{actor.Name} claims Duke and attempts Tax (+{TAX_GAIN}).");
                break;

            case ActionType.Assassinate:
                if (actor.Coins < ASSASSINATE_COST) return;
                var assassinateTarget = game.Players.FirstOrDefault(p => p.ConnectionId == action.TargetConnectionId);
                if (assassinateTarget == null || !assassinateTarget.IsAlive) return;
                game.Pending = new PendingAction
                {
                    Action = ActionType.Assassinate,
                    ActorConnectionId = actor.ConnectionId,
                    TargetConnectionId = action.TargetConnectionId,
                    ClaimedRole = Role.Assassin,
                    Phase = PendingPhase.ActionClaim
                };
                game.PendingStartTime = DateTime.UtcNow;
                game.Log.Add($"{actor.Name} claims Assassin and attempts to assassinate {assassinateTarget.Name}.");
                break;

            case ActionType.Steal:
                var stealTarget = game.Players.FirstOrDefault(p => p.ConnectionId == action.TargetConnectionId);
                if (stealTarget == null || !stealTarget.IsAlive || stealTarget.Coins == 0) return;
                game.Pending = new PendingAction
                {
                    Action = ActionType.Steal,
                    ActorConnectionId = actor.ConnectionId,
                    TargetConnectionId = action.TargetConnectionId,
                    ClaimedRole = Role.Captain,
                    Phase = PendingPhase.ActionClaim
                };
                game.PendingStartTime = DateTime.UtcNow;
                game.Log.Add($"{actor.Name} claims Captain and attempts to steal from {stealTarget.Name}.");
                break;

            case ActionType.Exchange:
                game.Pending = new PendingAction
                {
                    Action = ActionType.Exchange,
                    ActorConnectionId = actor.ConnectionId,
                    ClaimedRole = Role.Ambassador,
                    Phase = PendingPhase.ActionClaim
                };
                game.PendingStartTime = DateTime.UtcNow;
                game.Log.Add($"{actor.Name} claims Ambassador and attempts Exchange.");
                break;
        }

        await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);

        // Notify next player if turn advanced
        if (game.Pending == null)
        {
            var nextPlayer = game.Players[game.CurrentPlayerIndex];
            if (!nextPlayer.IsBot)
            {
                await _hubContext.Clients.Client(nextPlayer.ConnectionId).SendAsync("YourTurn");
            }
        }
    }

    public async Task PassPendingAsync(GameState game, PlayerState passer, string gameId)
    {
        if (game.Pending == null || !game.GameStarted || game.GameEnded) return;
        if (passer == null || !passer.IsAlive) return;

        var p = game.Pending;

        if (p.Phase == PendingPhase.ActionClaim)
        {
            if (passer.ConnectionId == p.ActorConnectionId) return; // Actor can't pass on own action

            if (p.Responded.Contains(passer.ConnectionId)) return;
            p.Responded.Add(passer.ConnectionId);

            // Check if all eligible players responded
            var eligibleResponders = game.Players
                .Where(x => x.IsAlive && x.ConnectionId != p.ActorConnectionId)
                .Select(x => x.ConnectionId)
                .ToHashSet();

            bool allResponded = eligibleResponders.All(id => p.Responded.Contains(id));

            if (allResponded)
            {
                // Execute the action
                await ExecutePendingActionAsync(game, gameId);
            }
        }
        else if (p.Phase == PendingPhase.BlockClaim)
        {
            if (passer.ConnectionId == p.BlockerConnectionId) return;

            if (p.BlockResponded.Contains(passer.ConnectionId)) return;
            p.BlockResponded.Add(passer.ConnectionId);

            var othersForBlock = game.Players
                .Where(x => x.IsAlive && x.ConnectionId != p.BlockerConnectionId)
                .Select(x => x.ConnectionId)
                .ToHashSet();

            bool allBlockRespondersAccepted = othersForBlock.All(id => p.BlockResponded.Contains(id));

            if (allBlockRespondersAccepted)
            {
                // Block stands
                game.Log.Add($"Block is accepted. {p.Action} is prevented.");
                game.Pending = null;
                game.PendingStartTime = null;
                AdvanceTurn(game);
            }
        }

        await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);
    }

    public async Task BlockDukeAsync(GameState game, PlayerState blocker, string gameId)
    {
        if (game.Pending == null || game.Pending.Action != ActionType.ForeignAid) return;

        game.Pending.Phase = PendingPhase.BlockClaim;
        game.Pending.BlockerConnectionId = blocker.ConnectionId;
        game.Pending.BlockClaimedRole = Role.Duke;
        game.Pending.BlockResponded.Clear();
        game.Pending.BlockResponded.Add(game.Pending.ActorConnectionId);

        game.Log.Add($"{blocker.Name} blocks Foreign Aid with Duke!");
        await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);
    }

    public async Task BlockContessaAsync(GameState game, PlayerState blocker, string gameId)
    {
        if (game.Pending == null || game.Pending.Action != ActionType.Assassinate) return;
        if (game.Pending.TargetConnectionId != blocker.ConnectionId) return;

        game.Pending.Phase = PendingPhase.BlockClaim;
        game.Pending.BlockerConnectionId = blocker.ConnectionId;
        game.Pending.BlockClaimedRole = Role.Contessa;
        game.Pending.BlockResponded.Clear();
        game.Pending.BlockResponded.Add(game.Pending.ActorConnectionId);

        game.Log.Add($"{blocker.Name} blocks Assassination with Contessa!");
        await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);
    }

    public async Task BlockCaptainAmbassadorAsync(GameState game, PlayerState blocker, Role role, string gameId)
    {
        if (game.Pending == null || game.Pending.Action != ActionType.Steal) return;
        if (game.Pending.TargetConnectionId != blocker.ConnectionId) return;

        game.Pending.Phase = PendingPhase.BlockClaim;
        game.Pending.BlockerConnectionId = blocker.ConnectionId;
        game.Pending.BlockClaimedRole = role;
        game.Pending.BlockResponded.Clear();
        game.Pending.BlockResponded.Add(game.Pending.ActorConnectionId);

        game.Log.Add($"{blocker.Name} blocks Steal with {role}!");
        await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);
    }

    public async Task ChooseCardToLoseAsync(GameState game, PlayerState player, Role role, string gameId)
    {
        var pending = game.PendingInfluenceLoss;
        if (pending == null || pending.PlayerConnectionId != player.ConnectionId) return;

        // Validate role is in player's hand
        if (!_store.PlayerRoles.TryGetValue(player.ConnectionId, out var roles) || !roles.Contains(role)) return;

        // Remove the chosen role
        RemoveSpecificInfluence(player, role);
        game.Log.Add($"{player.Name} loses {role} ({pending.Reason}).");

        // Send updated roles to player (skip if bot)
        if (!player.IsBot && _store.PlayerRoles.TryGetValue(player.ConnectionId, out var updatedRoles))
        {
            await _hubContext.Clients.Client(player.ConnectionId).SendAsync("YourRoles", updatedRoles);
        }

        // Clear pending
        game.PendingInfluenceLoss = null;

        // Check end game
        CheckEndGame(game);
        if (game.GameEnded)
        {
            await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);
            var stats = GenerateGameEndStats(game);
            await _hubContext.Clients.Group(gameId).SendAsync("GameEnded", stats);
            return;
        }

        // Continue game flow
        await ContinueAfterInfluenceLossAsync(gameId, game);
    }

    public async Task SubmitExchangeCardsAsync(GameState game, PlayerState player, List<Role> chosenCards, string gameId)
    {
        if (game.Pending == null || game.Pending.Phase != PendingPhase.ExchangeCardSelection) return;
        if (game.Pending.ActorConnectionId != player.ConnectionId) return;

        var cardsToKeep = game.Pending.ExchangeCardsToKeep;
        if (chosenCards.Count != cardsToKeep) return;

        // Update player roles
        _store.PlayerRoles[player.ConnectionId] = chosenCards;

        // Return unchosen cards to deck
        var available = game.Pending.ExchangeAvailableCards ?? new List<Role>();
        var returned = available.Except(chosenCards).ToList();
        if (_store.GameDecks.TryGetValue(gameId, out var deck))
        {
            deck.AddRange(returned);

            // Shuffle returned cards into deck
            var rng = Random.Shared;
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
        }

        game.Log.Add($"{player.Name} completes Exchange.");

        // Send updated roles to player (skip if bot)
        if (!player.IsBot)
        {
            await _hubContext.Clients.Client(player.ConnectionId).SendAsync("YourRoles", chosenCards);
        }

        // Clear pending and advance turn
        game.Pending = null;
        game.PendingStartTime = null;
        AdvanceTurn(game);

        await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);

        var nextPlayer = game.Players[game.CurrentPlayerIndex];
        if (!nextPlayer.IsBot)
        {
            await _hubContext.Clients.Client(nextPlayer.ConnectionId).SendAsync("YourTurn");
        }
    }

    // ==================== PRIVATE HELPER METHODS ====================

    private async Task ExecutePendingActionAsync(GameState game, string gameId)
    {
        var p = game.Pending;
        if (p == null) return;

        var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
        if (actor == null) return;

        switch (p.Action)
        {
            case ActionType.ForeignAid:
                actor.Coins += FOREIGN_AID_GAIN;
                game.Log.Add($"{actor.Name} gains {FOREIGN_AID_GAIN} coins from Foreign Aid.");
                game.Pending = null;
                game.PendingStartTime = null;
                AdvanceTurn(game);
                break;

            case ActionType.Tax:
                actor.Coins += TAX_GAIN;
                game.Log.Add($"{actor.Name} gains {TAX_GAIN} coins from Tax.");
                game.Pending = null;
                game.PendingStartTime = null;
                AdvanceTurn(game);
                break;

            case ActionType.Assassinate:
                actor.Coins -= ASSASSINATE_COST;
                p.Phase = PendingPhase.BlockClaim;
                p.BlockResponded.Clear();
                game.Log.Add($"{actor.Name} pays {ASSASSINATE_COST} coins. Target can block with Contessa.");
                break;

            case ActionType.Steal:
                p.Phase = PendingPhase.BlockClaim;
                p.BlockResponded.Clear();
                game.Log.Add($"{actor.Name} attempts to steal. Target can block with Captain or Ambassador.");
                break;

            case ActionType.Exchange:
                // Draw 2 cards
                if (_store.GameDecks.TryGetValue(gameId, out var deck) && _store.PlayerRoles.TryGetValue(actor.ConnectionId, out var currentRoles))
                {
                    var drawnCards = new List<Role>();
                    for (int i = 0; i < 2 && deck.Count > 0; i++)
                    {
                        var card = deck[^1];
                        deck.RemoveAt(deck.Count - 1);
                        drawnCards.Add(card);
                    }

                    var allCards = new List<Role>(currentRoles);
                    allCards.AddRange(drawnCards);

                    p.Phase = PendingPhase.ExchangeCardSelection;
                    p.ExchangeAvailableCards = allCards;
                    p.ExchangeCardsToKeep = actor.InfluenceCount;

                    game.Log.Add($"{actor.Name} draws cards for Exchange.");

                    // Notify player to choose cards (skip if bot)
                    if (!actor.IsBot)
                    {
                        await _hubContext.Clients.Client(actor.ConnectionId).SendAsync("ChooseExchangeCards", allCards, actor.InfluenceCount);
                    }
                }
                break;
        }

        await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);
    }

    private void AdvanceTurn(GameState game)
    {
        // Find next alive player
        int attempts = 0;
        do
        {
            game.CurrentPlayerIndex = (game.CurrentPlayerIndex + 1) % game.Players.Count;
            attempts++;
        } while (!game.Players[game.CurrentPlayerIndex].IsAlive && attempts < game.Players.Count);

        game.TurnCount++;
    }

    private void RequestInfluenceLoss(GameState game, PlayerState target, string reason)
    {
        game.PendingInfluenceLoss = new PendingInfluenceLoss
        {
            PlayerConnectionId = target.ConnectionId,
            Reason = reason
        };
    }

    private void RemoveSpecificInfluence(PlayerState player, Role role)
    {
        if (_store.PlayerRoles.TryGetValue(player.ConnectionId, out var roles))
        {
            roles.Remove(role);
            player.InfluenceCount = roles.Count;
        }
    }

    private void CheckEndGame(GameState game)
    {
        var alivePlayers = game.Players.Where(p => p.IsAlive).ToList();
        if (alivePlayers.Count <= 1)
        {
            game.GameEnded = true;
            if (alivePlayers.Count == 1)
            {
                game.WinnerName = alivePlayers[0].Name;
            }
        }
    }

    private GameEndStats GenerateGameEndStats(GameState game)
    {
        return new GameEndStats
        {
            WinnerName = game.WinnerName,
            TotalTurns = game.TurnCount,
            PlayerStats = game.Players.Select(p => new PlayerFinalStats
            {
                Name = p.Name,
                FinalCoins = p.Coins,
                RemainingInfluence = p.InfluenceCount,
                IsWinner = p.Name == game.WinnerName,
                WasConnected = p.IsConnected
            }).ToList()
        };
    }

    private async Task ContinueAfterInfluenceLossAsync(string gameId, GameState game)
    {
        // If there was a pending action that caused this influence loss, resolve it
        if (game.Pending != null)
        {
            var p = game.Pending;

            // Check if this was from a successful Assassinate block challenge
            if (p.Action == ActionType.Assassinate && p.Phase == PendingPhase.BlockClaim)
            {
                // Block failed, assassination succeeds
                var target = game.Players.FirstOrDefault(x => x.ConnectionId == p.TargetConnectionId);
                if (target != null && target.IsAlive)
                {
                    RequestInfluenceLoss(game, target, $"Assassinated by {game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId)?.Name}");
                    game.Pending = null;
                    game.PendingStartTime = null;
                    await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                    return;
                }
            }

            // Otherwise clear pending and advance turn
            game.Pending = null;
            game.PendingStartTime = null;
        }

        AdvanceTurn(game);
        await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", game);

        var nextPlayer = game.Players[game.CurrentPlayerIndex];
        if (!nextPlayer.IsBot)
        {
            await _hubContext.Clients.Client(nextPlayer.ConnectionId).SendAsync("YourTurn");
        }
    }
}
