using System.Linq;
using System.Threading.Tasks;
using Coup.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Coup.Server
{
    public class CoupHub : Hub
    {
        private readonly GameStore _store;
        private readonly ILogger<CoupHub> _logger;

        // Game constants
        private const int INCOME_GAIN = 1;
        private const int FOREIGN_AID_GAIN = 2;
        private const int TAX_GAIN = 3;
        private const int COUP_COST = 7;
        private const int ASSASSINATE_COST = 3;
        private const int STEAL_MAX = 2;
        private const int MANDATORY_COUP_COINS = 10;
        private const int STARTING_COINS = 2;
        private const int STARTING_INFLUENCE = 2;
        private const int ROLES_PER_PLAYER = 2;
        private const int ROLES_PER_TYPE = 3;
        private const int MIN_PLAYERS = 2;
        private const int PENDING_ACTION_TIMEOUT_SECONDS = 30;

        public CoupHub(GameStore store, ILogger<CoupHub> logger)
        {
            _store = store;
            _logger = logger;
        }

        public async Task JoinLobby(string gameId, string playerName)
        {
            var game = _store.Games.GetOrAdd(gameId, id => new GameState { GameId = id });

            // Check if player already exists (by ConnectionId or by Name for reconnection)
            var existing = game.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);

            // If not found by ConnectionId, check by name for reconnection
            if (existing == null && !string.IsNullOrWhiteSpace(playerName))
            {
                var sanitizedName = SanitizePlayerName(playerName);
                existing = game.Players.FirstOrDefault(p => p.Name == sanitizedName && !p.IsConnected);
            }

            if (existing != null)
            {
                // Reconnection - retrieve roles with OLD connection ID before updating
                var oldConnectionId = existing.ConnectionId;
                List<Role>? existingRoles = null;
                if (!string.IsNullOrEmpty(oldConnectionId) && _store.PlayerRoles.TryGetValue(oldConnectionId, out var oldRoles))
                {
                    existingRoles = oldRoles;
                    // Remove old ConnectionId mapping
                    _store.PlayerRoles.TryRemove(oldConnectionId, out _);
                }

                // Update connection ID and mark as connected
                existing.ConnectionId = Context.ConnectionId;
                existing.IsConnected = true;
                existing.DisconnectedTime = null;

                // Re-map roles to new ConnectionId
                if (existingRoles != null)
                {
                    _store.PlayerRoles[Context.ConnectionId] = existingRoles;
                }

                game.Log.Add($"{existing.Name} reconnected.");
                _logger.LogInformation("Player {PlayerName} reconnected to game {GameId}", existing.Name, gameId);
            }
            else
            {
                // New player - sanitize player name
                var sanitizedName = SanitizePlayerName(playerName);
                if (string.IsNullOrWhiteSpace(sanitizedName))
                {
                    sanitizedName = $"Player_{game.Players.Count + 1}";
                }

                var player = new PlayerState
                {
                    ConnectionId = Context.ConnectionId,
                    Name = sanitizedName,
                    Coins = STARTING_COINS,
                    InfluenceCount = STARTING_INFLUENCE,
                    IsBot = false,
                    IsConnected = true
                };
                game.Players.Add(player);
                _logger.LogInformation("New player {PlayerName} joined game {GameId}", sanitizedName, gameId);
            }

            if (string.IsNullOrEmpty(game.HostConnectionId))
            {
                game.HostConnectionId = Context.ConnectionId; // first joiner is host
                var hostName = existing?.Name ?? game.Players.Last().Name;
                game.Log.Add($"{hostName} is host.");
            }

            _store.ConnectionToGame[Context.ConnectionId] = gameId;
            await Groups.AddToGroupAsync(Context.ConnectionId, gameId);

            // Send current roles to reconnecting player
            if (existing != null && _store.PlayerRoles.TryGetValue(Context.ConnectionId, out var roles))
            {
                await Clients.Client(Context.ConnectionId).SendAsync("YourRoles", roles);
            }

            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
        }

        private static string SanitizePlayerName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "";

            // Trim and limit length
            name = name.Trim();
            if (name.Length > 20)
                name = name.Substring(0, 20);

            // Remove potentially dangerous characters (keep alphanumeric, spaces, and basic punctuation)
            var sanitized = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_')
                {
                    sanitized.Append(c);
                }
            }

            return sanitized.ToString().Trim();
        }

        public async Task StartGame(string gameId)
        {
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            if (game.GameStarted)
                return;

            if (game.HostConnectionId != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Only host can start the game");
                return;
            }

            if (game.Players.Count(p => p.IsAlive && p.IsConnected) < MIN_PLAYERS)
            {
                await Clients.Caller.SendAsync("Error", "Need at least 2 connected players to start");
                return;
            }
            // Build deck (3 of each role)
            var deck = new List<Role>();
            foreach (Role r in Enum.GetValues(typeof(Role)))
            {
                for (int i = 0; i < ROLES_PER_TYPE; i++)
                    deck.Add(r);
            }

            // Shuffle
            var rng = Random.Shared;
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }

            // Deal 2 roles to each connected alive player, store them privately
            foreach (var p in game.Players.Where(p => p.IsAlive && p.IsConnected))
            {
                var roles = new List<Role>();
                for (int k = 0; k < ROLES_PER_PLAYER; k++)
                {
                    var top = deck[^1];
                    deck.RemoveAt(deck.Count - 1);
                    roles.Add(top);
                }
                _store.PlayerRoles[p.ConnectionId] = roles;
                p.InfluenceCount = roles.Count;

                 await Clients.Client(p.ConnectionId).SendAsync("YourRoles", roles);
            }

            // Store remaining deck for Exchange action
            _store.GameDecks[gameId] = deck;
            game.GameStarted = true;
            game.GameStartTime = DateTime.UtcNow;
            game.TurnCount = 0;
            game.Log.Add("Game started.");

            // positionner CurrentPlayerIndex sur le premier vivant ET connecté
            var first = game.Players.FirstOrDefault(p => p.IsAlive && p.IsConnected);
            if (first != null)
                game.CurrentPlayerIndex = game.Players.IndexOf(first);

            _logger.LogInformation("Game {GameId} started with {PlayerCount} players", gameId, game.Players.Count);

            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
            await Clients.Client(game.Players[game.CurrentPlayerIndex].ConnectionId).SendAsync("YourTurn");
        }

        public async Task RematchGame(string gameId)
        {
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            // Only host can start rematch
            if (game.HostConnectionId != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Only host can start rematch");
                return;
            }

            // Can only rematch after game has ended
            if (!game.GameEnded)
            {
                await Clients.Caller.SendAsync("Error", "Cannot rematch - game is still in progress");
                return;
            }

            // Need at least 2 connected players
            if (game.Players.Count(p => p.IsConnected) < MIN_PLAYERS)
            {
                await Clients.Caller.SendAsync("Error", "Need at least 2 connected players for rematch");
                return;
            }

            // Reset game state
            game.GameStarted = false;
            game.GameEnded = false;
            game.WinnerName = "";
            game.Pending = null;
            game.PendingInfluenceLoss = null;
            game.PendingStartTime = null;
            game.CurrentPlayerIndex = 0;

            // Keep previous game log and add separator
            game.Log.Add("=== REMATCH ===");

            // Reset all players
            foreach (var player in game.Players)
            {
                player.Coins = STARTING_COINS;
                player.InfluenceCount = STARTING_INFLUENCE;
                // Note: IsConnected and DisconnectedTime are preserved
            }

            // Clear old roles
            foreach (var player in game.Players)
            {
                _store.PlayerRoles.TryRemove(player.ConnectionId, out _);
            }

            game.Log.Add("Rematch initialized. Host can start the game when ready.");
            _logger.LogInformation("Rematch initialized for game {GameId} with {PlayerCount} connected players", gameId, game.Players.Count(p => p.IsConnected));

            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
            await Clients.Group(gameId).SendAsync("RematchReady");
        }


        public async Task PerformAction(ActionDto action)
        {
            try
            {
                if (!_store.ConnectionToGame.TryGetValue(Context.ConnectionId, out var gameId)) return;
                if (!_store.Games.TryGetValue(gameId, out var game)) return;

                // Vérifier timeout avant de continuer
                await CheckPendingTimeout(gameId);

                var actor = game.Players.First(p => p.ConnectionId == Context.ConnectionId);
            if (!game.GameStarted)
            {
                await Clients.Caller.SendAsync("Error", "Game not started");
                return;
            }
            if (game.GameEnded)
            {
                await Clients.Caller.SendAsync("Error", "Game already ended");
                return;
            }
            // Vérifie le tour
            var current = game.Players[game.CurrentPlayerIndex];
            if (current.ConnectionId != actor.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Not your turn");
                return;
            }
            if (actor.Coins >= MANDATORY_COUP_COINS && action.Action != ActionType.Coup)
            {
                await Clients.Caller.SendAsync("Error", "You have 10+ coins: you must Coup");
                return;
            }
            // Résolution actions V0 (pas de block/challenge encore)
            switch (action.Action)
            {
                case ActionType.Income:
                    actor.Coins += INCOME_GAIN;
                    game.Log.Add($"{actor.Name} takes Income (+{INCOME_GAIN}).");
                    break;
                case ActionType.ForeignAid:
                {
                    if (game.Pending != null)
                    {
                        await Clients.Caller.SendAsync("Error", "Another action is pending, wait for it to resolve");
                        return;
                    }

                    var pending = new PendingAction
                    {
                        Action = ActionType.ForeignAid,
                        ActorConnectionId = actor.ConnectionId,
                        ClaimedRole = null // Pas de rôle revendiqué pour Foreign Aid
                    };

                    pending.Responded.Add(actor.ConnectionId);
                    game.Pending = pending;
                    game.PendingStartTime = DateTime.UtcNow;

                    game.Log.Add($"{actor.Name} attempts Foreign Aid (+{FOREIGN_AID_GAIN}). Others can 'block duke', 'challenge', or 'pass'.");
                    await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                    return;
                }
                case ActionType.Coup:
                    if (actor.Coins < COUP_COST)
                    {
                        await Clients.Caller.SendAsync("Error", "Not enough coins for Coup");
                        return;
                    }
                    if (string.IsNullOrEmpty(action.TargetConnectionId))
                    {
                        await Clients.Caller.SendAsync("Error", "No target selected");
                        return;
                    }
                    var target = game.Players.FirstOrDefault(p => p.ConnectionId == action.TargetConnectionId);
                    if (target == null || !target.IsAlive)
                    {
                        await Clients.Caller.SendAsync("Error", "Invalid target");
                        return;
                    }
                    if (target.ConnectionId == actor.ConnectionId)
                    {
                        await Clients.Caller.SendAsync("Error", "You cannot coup yourself");
                        return;
                    }

                    actor.Coins -= COUP_COST;
                    game.Log.Add($"{actor.Name} coups {target.Name}. {target.Name} must choose a card to lose.");

                    // Request target to choose which card to lose
                    RequestInfluenceLoss(game, target, "Coup");

                    // Send updated game state and wait for target to choose
                    await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                    await Clients.Client(target.ConnectionId).SendAsync("ChooseInfluence", "You were coup'd. Choose a role to lose.");
                    return; // Don't advance turn yet - wait for choice
                case ActionType.Tax:
                {
                    if (game.Pending != null)
                    {
                        await Clients.Caller.SendAsync("Error", "Another action is pending, wait for it to resolve");
                        return;
                    }

                    var pending = new PendingAction
                    {
                        Action = ActionType.Tax,
                        ActorConnectionId = actor.ConnectionId,
                        ClaimedRole = Role.Duke
                    };

                    // L'acteur n'est pas censé "répondre" (il a déjà joué)
                    // mais on peut le marquer comme handled pour simplifier les checks
                    pending.Responded.Add(actor.ConnectionId);

                    game.Pending = pending;
                    game.PendingStartTime = DateTime.UtcNow;

                    game.Log.Add($"{actor.Name} claims Duke and attempts Tax (+{TAX_GAIN}). Others: 'challenge' or 'pass'.");
                    await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                    // On NE change PAS de tour ici. On attend les réponses.
                    return;
                }
                case ActionType.Assassinate:
                {
                    if (game.Pending != null)
                    {
                        await Clients.Caller.SendAsync("Error", "Another action is pending");
                        return;
                    }
                    // On n’empêche plus “par rôle” ici (bluff autorisé) :
                    // le vrai check se fait si quelqu’un challenge.
                    if (string.IsNullOrEmpty(action.TargetConnectionId))
                    {
                        await Clients.Caller.SendAsync("Error", "No target selected");
                        return;
                    }
                    var targetPlayer = game.Players.FirstOrDefault(p => p.ConnectionId == action.TargetConnectionId);
                    if (targetPlayer == null || !targetPlayer.IsAlive)
                    {
                        await Clients.Caller.SendAsync("Error", "Invalid target");
                        return;
                    }
                    if (targetPlayer.ConnectionId == actor.ConnectionId)
                    {
                        await Clients.Caller.SendAsync("Error", "You cannot assassinate yourself");
                        return;
                    }
                    if (actor.Coins < ASSASSINATE_COST)
                    {
                        await Clients.Caller.SendAsync("Error", "Not enough coins for Assassinate");
                        return;
                    }

                    // Création du pending (phase 1 : claim Assassin)
                    var pending = new PendingAction {
                        Action = ActionType.Assassinate,
                        ActorConnectionId = actor.ConnectionId,
                        TargetConnectionId = targetPlayer.ConnectionId,
                        ClaimedRole = Role.Assassin,
                        Phase = PendingPhase.ActionClaim
                    };
                    pending.Responded.Add(actor.ConnectionId); // l'acteur n'a rien à "répondre"

                    game.Pending = pending;
                    game.PendingStartTime = DateTime.UtcNow;
                    game.Log.Add($"{actor.Name} claims Assassin and attempts to assassinate {targetPlayer.Name} (cost {ASSASSINATE_COST}). Others: 'challenge' or the target can 'block contessa'. Others may also 'pass'.");

                    await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                    return; // attendre challenge/pass/block
                }
                case ActionType.Steal:
                {
                    if (game.Pending != null)
                    {
                        await Clients.Caller.SendAsync("Error", "Another action is pending");
                        return;
                    }

                    if (string.IsNullOrEmpty(action.TargetConnectionId))
                    {
                        await Clients.Caller.SendAsync("Error", "No target selected");
                        return;
                    }

                    var targetPlayer = game.Players.FirstOrDefault(p => p.ConnectionId == action.TargetConnectionId);
                    if (targetPlayer == null || !targetPlayer.IsAlive)
                    {
                        await Clients.Caller.SendAsync("Error", "Invalid target");
                        return;
                    }

                    if (targetPlayer.ConnectionId == actor.ConnectionId)
                    {
                        await Clients.Caller.SendAsync("Error", "You cannot steal from yourself");
                        return;
                    }

                    if (targetPlayer.Coins <= 0)
                    {
                        await Clients.Caller.SendAsync("Error", "Target has no coins to steal");
                        return;
                    }

                    // Création du pending (phase 1 : claim Captain pour steal)
                    var pending = new PendingAction {
                        Action = ActionType.Steal,
                        ActorConnectionId = actor.ConnectionId,
                        TargetConnectionId = targetPlayer.ConnectionId,
                        ClaimedRole = Role.Captain,
                        Phase = PendingPhase.ActionClaim
                    };
                    pending.Responded.Add(actor.ConnectionId);

                    game.Pending = pending;
                    game.PendingStartTime = DateTime.UtcNow;
                    game.Log.Add($"{actor.Name} claims Captain to steal from {targetPlayer.Name} (up to {STEAL_MAX}). Others: 'challenge' or target can 'block captain/ambassador'. Others may 'pass'.");

                    await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                    return;
                }
                case ActionType.Exchange:
                {
                    if (game.Pending != null)
                    {
                        await Clients.Caller.SendAsync("Error", "Another action is pending");
                        return;
                    }

                    // Création du pending (phase 1 : claim Ambassador pour Exchange)
                    var pending = new PendingAction {
                        Action = ActionType.Exchange,
                        ActorConnectionId = actor.ConnectionId,
                        ClaimedRole = Role.Ambassador,
                        Phase = PendingPhase.ActionClaim
                    };
                    pending.Responded.Add(actor.ConnectionId);

                    game.Pending = pending;
                    game.PendingStartTime = DateTime.UtcNow;
                    game.Log.Add($"{actor.Name} claims Ambassador to exchange roles. Others: 'challenge' or 'pass'.");

                    await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                    return;
                }
                default:
                    await Clients.Caller.SendAsync("Error", "Action not implemented yet");
                    return;
            }

                AdvanceTurn(game);
                await Clients.Group(gameId).SendAsync("GameStateUpdated", game);

                var next = game.Players[game.CurrentPlayerIndex];
                await Clients.Client(next.ConnectionId).SendAsync("YourTurn");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PerformAction for connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "An error occurred while performing the action. Please try again.");
            }
        }
        public async Task Challenge()
        {
            try
            {
                if (!_store.ConnectionToGame.TryGetValue(Context.ConnectionId, out var gameId)) return;
                if (!_store.Games.TryGetValue(gameId, out var game)) return;

                // Vérifier timeout avant de continuer
                if (await CheckPendingTimeout(gameId)) return;

                if (game.Pending == null) { await Clients.Caller.SendAsync("Error", "No pending action to challenge"); return; }
                if (!game.GameStarted || game.GameEnded) return;

            var p = game.Pending;
            var challenger = game.Players.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
            if (challenger == null || !challenger.IsAlive) { await Clients.Caller.SendAsync("Error", "You cannot challenge"); return; }

            // On ne peut pas se challenger soi-même
            if (p.Phase == PendingPhase.ActionClaim && challenger.ConnectionId == p.ActorConnectionId)
            { await Clients.Caller.SendAsync("Error", "You cannot challenge your own action"); return; }
            if (p.Phase == PendingPhase.BlockClaim && challenger.ConnectionId == p.BlockerConnectionId)
            { await Clients.Caller.SendAsync("Error", "You cannot challenge your own block"); return; }

            // Marquer le challenger comme "répondu" selon la phase
            if (p.Phase == PendingPhase.ActionClaim) p.Responded.Add(challenger.ConnectionId);
            else p.BlockResponded.Add(challenger.ConnectionId);

            // Cibles selon la phase
            string claimedBy = (p.Phase == PendingPhase.ActionClaim) ? p.ActorConnectionId : (p.BlockerConnectionId ?? "");
            var claimedPlayer = game.Players.FirstOrDefault(x => x.ConnectionId == claimedBy);
            if (claimedPlayer == null || !claimedPlayer.IsAlive)
            {
                // Si l’acteur/le bloqueur n’est plus valide, abandonner le pending
                game.Pending = null;
                await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                return;
            }

            // Rôle revendiqué selon la phase
            var role = (p.Phase == PendingPhase.ActionClaim) ? p.ClaimedRole : p.BlockClaimedRole;
            bool hasRole = false;
            if (role.HasValue && _store.PlayerRoles.TryGetValue(claimedBy, out var roles))
                hasRole = roles.Contains(role.Value);

            if (hasRole)
            {
                // Challenger perd 1 influence - must choose which card
                game.Log.Add($"{challenger.Name} challenges {claimedPlayer.Name}... and loses. Claim was TRUE. {challenger.Name} must choose a card to lose.");

                // Shuffle the proved card back into deck and draw a new one
                if (role.HasValue && _store.PlayerRoles.TryGetValue(claimedBy, out var claimerRoles) && _store.GameDecks.TryGetValue(gameId, out var deck))
                {
                    // Remove the proved role from player's hand
                    claimerRoles.Remove(role.Value);

                    // Add it back to the deck
                    deck.Add(role.Value);

                    // Shuffle the deck
                    var rng = Random.Shared;
                    for (int i = deck.Count - 1; i > 0; i--)
                    {
                        int j = rng.Next(i + 1);
                        (deck[i], deck[j]) = (deck[j], deck[i]);
                    }

                    // Draw a new card
                    if (deck.Count > 0)
                    {
                        var newCard = deck[^1];
                        deck.RemoveAt(deck.Count - 1);
                        claimerRoles.Add(newCard);

                        game.Log.Add($"{claimedPlayer.Name} shuffles their proved {role.Value} back and draws a new card.");
                        await Clients.Client(claimedBy).SendAsync("YourRoles", claimerRoles);
                    }
                }

                RequestInfluenceLoss(game, challenger, "Failed Challenge");

                await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                await Clients.Client(challenger.ConnectionId).SendAsync("ChooseInfluence", "Your challenge failed. Choose a role to lose.");
                return; // Wait for challenger to choose card - ContinueAfterInfluenceLoss will handle next steps
            }
            else
            {
                // Claim faux → le "claimant" perd 1 influence and must choose
                game.Log.Add($"{challenger.Name} successfully challenges {claimedPlayer.Name}! Claim was FALSE. {claimedPlayer.Name} must choose a card to lose.");

                RequestInfluenceLoss(game, claimedPlayer, "Failed Bluff");

                await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                await Clients.Client(claimedPlayer.ConnectionId).SendAsync("ChooseInfluence", "Your bluff was caught. Choose a role to lose.");
                return; // Wait for claimant to choose card
            }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Challenge for connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "An error occurred while processing the challenge. Please try again.");
            }
        }

        public async Task PassPending()
        {
            try
            {
                if (!_store.ConnectionToGame.TryGetValue(Context.ConnectionId, out var gameId)) return;
                if (!_store.Games.TryGetValue(gameId, out var game)) return;

                // Vérifier timeout avant de continuer
                if (await CheckPendingTimeout(gameId)) return;

                if (game.Pending == null) { await Clients.Caller.SendAsync("Error", "No pending action to pass on"); return; }
                if (!game.GameStarted || game.GameEnded) return;

            var p = game.Pending;
            var player = game.Players.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
            if (player == null || !player.IsAlive) { await Clients.Caller.SendAsync("Error", "You cannot pass"); return; }

            // L'acteur ne "passe" jamais son propre claim
            if (p.Phase == PendingPhase.ActionClaim && player.ConnectionId == p.ActorConnectionId)
            { await Clients.Caller.SendAsync("Error", "Actor cannot pass on their own action"); return; }

            if (p.Phase == PendingPhase.BlockClaim)
            {
                if (p.Action == ActionType.ForeignAid)
                {
                    // Pour Foreign Aid, tout le monde (sauf blocker) peut passer (accepter le block Duke)
                    if (player.ConnectionId == p.BlockerConnectionId)
                    { await Clients.Caller.SendAsync("Error", "You cannot pass on your own block"); return; }

                    if (p.BlockResponded.Contains(player.ConnectionId))
                    { await Clients.Caller.SendAsync("Error", "You already responded"); return; }

                    p.BlockResponded.Add(player.ConnectionId);
                    game.Log.Add($"{player.Name} accepts the Duke block.");

                    // Vérifier si tout le monde (sauf blocker) a répondu
                    var othersForBlock = game.Players
                        .Where(x => x.IsAlive && x.ConnectionId != p.BlockerConnectionId)
                        .Select(x => x.ConnectionId)
                        .ToHashSet();
                    bool allBlockRespondersAccepted = othersForBlock.All(id => p.BlockResponded.Contains(id));

                    if (allBlockRespondersAccepted)
                    {
                        // Tout le monde a accepté le block → Foreign Aid échoue
                        game.Log.Add($"Duke block stands. Foreign Aid is prevented.");
                        game.Pending = null;
                        game.PendingStartTime = null;
                        AdvanceTurn(game);
                        await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                        var nextPlayer = game.Players[game.CurrentPlayerIndex];
                        await Clients.Client(nextPlayer.ConnectionId).SendAsync("YourTurn");
                    }
                    else
                    {
                        await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                    }
                    return;
                }

                // Pour Assassinate et Steal
                if (p.Action == ActionType.Assassinate)
                {
                    // If Contessa block is claimed, others can pass to accept the block
                    if (p.BlockClaimedRole.HasValue)
                    {
                        // Similar to Foreign Aid Duke block - everyone except blocker can pass
                        if (player.ConnectionId == p.BlockerConnectionId)
                        { await Clients.Caller.SendAsync("Error", "You cannot pass on your own block"); return; }

                        if (p.BlockResponded.Contains(player.ConnectionId))
                        { await Clients.Caller.SendAsync("Error", "You already responded"); return; }

                        p.BlockResponded.Add(player.ConnectionId);
                        game.Log.Add($"{player.Name} accepts the Contessa block.");

                        // Check if everyone (except blocker) has responded
                        var othersForBlock = game.Players
                            .Where(x => x.IsAlive && x.ConnectionId != p.BlockerConnectionId)
                            .Select(x => x.ConnectionId)
                            .ToHashSet();
                        bool allBlockRespondersAccepted = othersForBlock.All(id => p.BlockResponded.Contains(id));

                        if (allBlockRespondersAccepted)
                        {
                            // Everyone accepted the block → assassination is prevented
                            game.Log.Add($"Contessa block stands. Assassination is prevented.");
                            game.Pending = null;
                            game.PendingStartTime = null;
                            AdvanceTurn(game);
                            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                            var nextPlayer = game.Players[game.CurrentPlayerIndex];
                            await Clients.Client(nextPlayer.ConnectionId).SendAsync("YourTurn");
                        }
                        else
                        {
                            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                        }
                        return;
                    }
                    else
                    {
                        // No block claimed yet - only target can pass (decline to block)
                        if (player.ConnectionId != p.TargetConnectionId)
                        { await Clients.Caller.SendAsync("Error", "Only the target can pass/block at block phase"); return; }

                        if (p.BlockResponded.Contains(player.ConnectionId))
                        { await Clients.Caller.SendAsync("Error", "You already responded"); return; }

                        p.BlockResponded.Add(player.ConnectionId);
                        game.Log.Add($"{player.Name} does not block.");

                        // Target declined to block ⇒ assassination succeeds
                        var target = game.Players.FirstOrDefault(x => x.ConnectionId == p.TargetConnectionId);
                        if (target != null && target.IsAlive)
                        {
                            game.Log.Add($"{target.Name} was assassinated and must choose a card to lose.");
                            game.Pending = null;
                            game.PendingStartTime = null;

                            RequestInfluenceLoss(game, target, "Assassinated");
                            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                            await Clients.Client(target.ConnectionId).SendAsync("ChooseInfluence", "You were assassinated. Choose a role to lose.");
                            return;
                        }
                    }
                }
                else if (p.Action == ActionType.Steal)
                {
                    // If Captain/Ambassador block is claimed, others can pass to accept the block
                    if (p.BlockClaimedRole.HasValue)
                    {
                        // Similar to Contessa block - everyone except blocker can pass
                        if (player.ConnectionId == p.BlockerConnectionId)
                        { await Clients.Caller.SendAsync("Error", "You cannot pass on your own block"); return; }

                        if (p.BlockResponded.Contains(player.ConnectionId))
                        { await Clients.Caller.SendAsync("Error", "You already responded"); return; }

                        p.BlockResponded.Add(player.ConnectionId);
                        game.Log.Add($"{player.Name} accepts the {p.BlockClaimedRole} block.");

                        // Check if everyone (except blocker) has responded
                        var othersForBlock = game.Players
                            .Where(x => x.IsAlive && x.ConnectionId != p.BlockerConnectionId)
                            .Select(x => x.ConnectionId)
                            .ToHashSet();
                        bool allBlockRespondersAccepted = othersForBlock.All(id => p.BlockResponded.Contains(id));

                        if (allBlockRespondersAccepted)
                        {
                            // Everyone accepted the block → steal is prevented
                            game.Log.Add($"{p.BlockClaimedRole} block stands. Steal is prevented.");
                            game.Pending = null;
                            game.PendingStartTime = null;
                            AdvanceTurn(game);
                            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                            var nextPlayer = game.Players[game.CurrentPlayerIndex];
                            await Clients.Client(nextPlayer.ConnectionId).SendAsync("YourTurn");
                        }
                        else
                        {
                            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                        }
                        return;
                    }
                    else
                    {
                        // No block claimed yet - only target can pass (decline to block)
                        if (player.ConnectionId != p.TargetConnectionId)
                        { await Clients.Caller.SendAsync("Error", "Only the target can pass/block at block phase"); return; }

                        if (p.BlockResponded.Contains(player.ConnectionId))
                        { await Clients.Caller.SendAsync("Error", "You already responded"); return; }

                        p.BlockResponded.Add(player.ConnectionId);
                        game.Log.Add($"{player.Name} does not block.");

                        // Target declined to block ⇒ steal succeeds
                        var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                        var target = game.Players.FirstOrDefault(x => x.ConnectionId == p.TargetConnectionId);

                        if (actor != null && actor.IsAlive && target != null && target.IsAlive)
                        {
                            var amount = Math.Min(STEAL_MAX, target.Coins);
                            if (amount > 0)
                            {
                                target.Coins -= amount;
                                actor.Coins += amount;
                                game.Log.Add($"{actor.Name} steals {amount} coin(s) from {target.Name}.");
                            }
                            else
                            {
                                game.Log.Add($"{actor.Name} tries to steal from {target.Name}, but there is nothing to take.");
                            }
                        }
                    }
                }

                game.Pending = null;
                game.PendingStartTime = null;
                AdvanceTurn(game);
                await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                var next = game.Players[game.CurrentPlayerIndex];
                await Clients.Client(next.ConnectionId).SendAsync("YourTurn");
                return;
            }

            // Phase ActionClaim : un "pass" d'un autre joueur
            if (p.Responded.Contains(player.ConnectionId))
            { await Clients.Caller.SendAsync("Error", "You already responded"); return; }

            p.Responded.Add(player.ConnectionId);
            game.Log.Add($"{player.Name} passes (no challenge).");

            // Tous les autres (hors acteur) ont répondu ?
            var others = game.Players.Where(x => x.IsAlive && x.ConnectionId != p.ActorConnectionId).Select(x => x.ConnectionId).ToHashSet();
            bool allOthers = others.All(id => p.Responded.Contains(id));

            if (!allOthers)
            {
                await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                return;
            }

            // Personne n'a challengé l'acteur ⇒ on valide l'action et on passe en phase block si besoin
            if (p.Action == ActionType.Tax)
            {
                var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                if (actor != null && actor.IsAlive)
                {
                    actor.Coins += TAX_GAIN;
                    game.Log.Add($"{actor.Name} takes Tax (+{TAX_GAIN}).");
                }
                game.Pending = null;
                game.PendingStartTime = null;
                AdvanceTurn(game);
                await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                var next = game.Players[game.CurrentPlayerIndex];
                await Clients.Client(next.ConnectionId).SendAsync("YourTurn");
                return;
            }

            if (p.Action == ActionType.ForeignAid)
            {
                // Personne n'a bloqué (block volontaire), donc Foreign Aid réussit
                var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                if (actor != null && actor.IsAlive)
                {
                    actor.Coins += FOREIGN_AID_GAIN;
                    game.Log.Add($"{actor.Name} takes Foreign Aid (+{FOREIGN_AID_GAIN}).");
                }
                game.Pending = null;
                game.PendingStartTime = null;
                AdvanceTurn(game);
                await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                var next = game.Players[game.CurrentPlayerIndex];
                await Clients.Client(next.ConnectionId).SendAsync("YourTurn");
                return;
            }

            if (p.Action == ActionType.Assassinate)
            {
                // Débiter maintenant les ASSASSINATE_COST à l'acteur (claim non challengé)
                var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                if (actor != null && actor.IsAlive)
                {
                    actor.Coins -= ASSASSINATE_COST;
                    game.Log.Add($"{actor.Name} pays {ASSASSINATE_COST} coins to attempt the assassination.");
                }

                // Phase block (seule la cible répond : 'block contessa' ou 'pass')
                p.Phase = PendingPhase.BlockClaim;
                p.BlockerConnectionId = p.TargetConnectionId;
                p.BlockClaimedRole = null; // pas encore revendiquée
                p.BlockResponded.Clear();

                // Marque l’acteur comme répondu dans la phase block
                if (actor != null) p.BlockResponded.Add(actor.ConnectionId);

                game.Log.Add($"Block phase: only the target may 'block contessa' or 'pass'. Others wait.");
                await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                return;
            }

            if (p.Action == ActionType.Steal)
            {
                // Phase block (seule la cible répond : 'block captain/ambassador' ou 'pass')
                p.Phase = PendingPhase.BlockClaim;
                p.BlockerConnectionId = p.TargetConnectionId;
                p.BlockClaimedRole = null;
                p.BlockResponded.Clear();

                game.Log.Add($"Block phase: only the target may 'block captain/ambassador' or 'pass'. Others wait.");
                await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                return;
            }

            if (p.Action == ActionType.Exchange)
            {
                // Claim validé, entrer en phase de sélection de cartes
                var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                if (actor != null && actor.IsAlive && _store.GameDecks.TryGetValue(gameId, out var deck))
                {
                    // Piocher 2 cartes du deck
                    var drawnCards = new List<Role>();
                    for (int i = 0; i < 2 && deck.Count > 0; i++)
                    {
                        var card = deck[^1];
                        deck.RemoveAt(deck.Count - 1);
                        drawnCards.Add(card);
                    }

                    // Combiner avec les cartes actuelles
                    if (_store.PlayerRoles.TryGetValue(actor.ConnectionId, out var currentRoles))
                    {
                        var allCards = new List<Role>(currentRoles);
                        allCards.AddRange(drawnCards);

                        // Entrer en phase de sélection de cartes
                        p.Phase = PendingPhase.ExchangeCardSelection;
                        p.ExchangeAvailableCards = allCards;
                        p.ExchangeCardsToKeep = actor.InfluenceCount;

                        game.Log.Add($"{actor.Name} is choosing cards to keep (Ambassador exchange)...");
                        await Clients.Group(gameId).SendAsync("GameStateUpdated", game);

                        // Envoyer les cartes disponibles au joueur pour qu'il choisisse
                        await Clients.Client(actor.ConnectionId).SendAsync("ChooseExchangeCards", allCards, actor.InfluenceCount);
                        return;
                    }
                }

                // Si erreur, annuler le pending
                game.Pending = null;
                game.PendingStartTime = null;
                AdvanceTurn(game);
                await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                var next = game.Players[game.CurrentPlayerIndex];
                await Clients.Client(next.ConnectionId).SendAsync("YourTurn");
                return;
            }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PassPending for connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "An error occurred while processing your pass. Please try again.");
            }
        }
        private async Task<bool> CheckPendingTimeout(string gameId)
        {
            if (!_store.Games.TryGetValue(gameId, out var game)) return false;
            if (game.Pending == null || game.PendingStartTime == null) return false;

            var elapsed = DateTime.UtcNow - game.PendingStartTime.Value;
            if (elapsed.TotalSeconds < PENDING_ACTION_TIMEOUT_SECONDS) return false;

            // Timeout expiré, résoudre automatiquement
            game.Log.Add($"Timeout: pending action auto-resolved (no response).");

            var p = game.Pending;

            if (p.Phase == PendingPhase.ActionClaim)
            {
                // Auto-pass pour tous les joueurs
                if (p.Action == ActionType.Tax)
                {
                    var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                    if (actor != null && actor.IsAlive)
                    {
                        actor.Coins += TAX_GAIN;
                        game.Log.Add($"{actor.Name} takes Tax (+{TAX_GAIN}).");
                    }
                    game.Pending = null;
                    game.PendingStartTime = null;
                    AdvanceTurn(game);
                }
                else if (p.Action == ActionType.ForeignAid)
                {
                    var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                    if (actor != null && actor.IsAlive)
                    {
                        actor.Coins += FOREIGN_AID_GAIN;
                        game.Log.Add($"{actor.Name} takes Foreign Aid (+{FOREIGN_AID_GAIN}).");
                    }
                    game.Pending = null;
                    game.PendingStartTime = null;
                    AdvanceTurn(game);
                }
                else if (p.Action == ActionType.Assassinate)
                {
                    // Passer en phase block
                    var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                    if (actor != null && actor.IsAlive)
                    {
                        actor.Coins -= ASSASSINATE_COST;
                    }
                    p.Phase = PendingPhase.BlockClaim;
                    p.BlockerConnectionId = p.TargetConnectionId;
                    p.BlockClaimedRole = null;
                    p.BlockResponded.Clear();
                    if (actor != null) p.BlockResponded.Add(actor.ConnectionId);
                    game.PendingStartTime = DateTime.UtcNow; // Restart timer for block phase
                }
                else if (p.Action == ActionType.Steal)
                {
                    // Passer en phase block
                    p.Phase = PendingPhase.BlockClaim;
                    p.BlockerConnectionId = p.TargetConnectionId;
                    p.BlockClaimedRole = null;
                    p.BlockResponded.Clear();
                    game.PendingStartTime = DateTime.UtcNow;
                }
                else if (p.Action == ActionType.Exchange)
                {
                    // Exécuter l'échange
                    var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                    if (actor != null && actor.IsAlive && _store.GameDecks.TryGetValue(gameId, out var deck))
                    {
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

                            // Mélanger toutes les cartes
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
                            await Clients.Client(actor.ConnectionId).SendAsync("YourRoles", keptCards);
                            game.Log.Add($"{actor.Name} exchanges roles (Ambassador).");
                        }
                    }
                    game.Pending = null;
                    game.PendingStartTime = null;
                    AdvanceTurn(game);
                }
            }
            else if (p.Phase == PendingPhase.BlockClaim)
            {
                // Timeout en phase BlockClaim
                if (p.Action == ActionType.ForeignAid)
                {
                    // Pour Foreign Aid, si timeout → le block Duke réussit (Foreign Aid échoue)
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

                            RequestInfluenceLoss(game, target, "Assassinated");
                            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                            await Clients.Client(target.ConnectionId).SendAsync("ChooseInfluence", "You were assassinated. Choose a role to lose.");
                            return true; // Don't continue - wait for card choice
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
                            var amount = Math.Min(STEAL_MAX, target.Coins);
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
                AdvanceTurn(game);
            }

            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
            if (!game.GameEnded)
            {
                var next = game.Players[game.CurrentPlayerIndex];
                await Clients.Client(next.ConnectionId).SendAsync("YourTurn");
            }
            else
            {
                var stats = GenerateGameEndStats(game);
                _logger.LogInformation("Game {GameId} ended. Winner: {WinnerName}, Duration: {Duration}, Turns: {Turns}",
                    gameId, stats.WinnerName, stats.GameDuration, stats.TotalTurns);
                await Clients.Group(gameId).SendAsync("GameEnded", stats);
                // Don't cleanup here - game needs to exist for rematch functionality
                // Cleanup will happen when all players disconnect or after timeout
            }

            return true;
        }

        private static void AdvanceTurn(GameState game)
        {
            AdvanceTurnStatic(game);
        }

        public static void AdvanceTurnStatic(GameState game)
        {
            game.TurnCount++; // Increment turn counter

            for (int step = 0; step < game.Players.Count; step++)
            {
                game.CurrentPlayerIndex = (game.CurrentPlayerIndex + 1) % game.Players.Count;
                var p = game.Players[game.CurrentPlayerIndex];
                if (p.IsAlive && p.IsConnected) return;
            }

            // If no connected alive players found, try to find any alive player (even if disconnected)
            for (int step = 0; step < game.Players.Count; step++)
            {
                game.CurrentPlayerIndex = (game.CurrentPlayerIndex + 1) % game.Players.Count;
                var p = game.Players[game.CurrentPlayerIndex];
                if (p.IsAlive) return;
            }
        }

        private static void CheckEndGame(GameState game)
        {
            CheckEndGameStatic(game);
        }

        public static void CheckEndGameStatic(GameState game)
        {
            if (game.AliveCount <= 1 && !game.GameEnded)
            {
                game.GameEnded = true;
                var winner = game.Players.FirstOrDefault(p => p.IsAlive);
                game.WinnerName = winner?.Name ?? "No one";
                game.Log.Add($"Game ended. Winner: {game.WinnerName}");
                // Note: Game end logging happens when stats are sent
            }
        }

        public static GameEndStats GenerateGameEndStats(GameState game)
        {
            var duration = game.GameStartTime.HasValue
                ? (DateTime.UtcNow - game.GameStartTime.Value).ToString(@"mm\:ss")
                : "N/A";

            var stats = new GameEndStats
            {
                WinnerName = game.WinnerName,
                TotalTurns = game.TurnCount,
                GameDuration = duration,
                PlayerStats = game.Players.Select(p => new PlayerFinalStats
                {
                    Name = p.Name,
                    FinalCoins = p.Coins,
                    RemainingInfluence = p.InfluenceCount,
                    IsWinner = p.Name == game.WinnerName,
                    WasConnected = p.IsConnected
                }).ToList()
            };

            return stats;
        }

        /// <summary>
        /// Sets up a pending influence loss that requires the player to choose which card to lose
        /// </summary>
        public static void RequestInfluenceLoss(GameState game, PlayerState player, string reason)
        {
            game.PendingInfluenceLoss = new PendingInfluenceLoss
            {
                PlayerConnectionId = player.ConnectionId,
                Reason = reason,
                StartTime = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Removes a specific role from a player (used after they choose which card to lose)
        /// </summary>
        public static void RemoveSpecificInfluence(PlayerState player, Role roleToRemove, GameStore store)
        {
            player.InfluenceCount = Math.Max(0, player.InfluenceCount - 1);

            if (store.PlayerRoles.TryGetValue(player.ConnectionId, out var roles) && roles.Count > 0)
            {
                roles.Remove(roleToRemove);
            }
        }

        /// <summary>
        /// Legacy method - removes a random role (only used for backwards compatibility or special cases)
        /// </summary>
        public static void RemoveInfluence(PlayerState player, GameStore store)
        {
            player.InfluenceCount = Math.Max(0, player.InfluenceCount - 1);

            if (store.PlayerRoles.TryGetValue(player.ConnectionId, out var roles) && roles.Count > 0)
            {
                var rng = Random.Shared;
                var indexToRemove = rng.Next(roles.Count);
                roles.RemoveAt(indexToRemove);
            }
        }
        public async Task ChooseCardToLose(Role role)
        {
            try
            {
                if (!_store.ConnectionToGame.TryGetValue(Context.ConnectionId, out var gameId)) return;
                if (!_store.Games.TryGetValue(gameId, out var game)) return;

                var pending = game.PendingInfluenceLoss;
                if (pending == null)
                {
                    await Clients.Caller.SendAsync("Error", "No pending influence loss");
                    return;
                }

            if (pending.PlayerConnectionId != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Not your turn to choose");
                return;
            }

            var player = game.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (player == null || !player.IsAlive)
            {
                await Clients.Caller.SendAsync("Error", "You are not in the game");
                return;
            }

            // Validate the role is in player's hand
            if (!_store.PlayerRoles.TryGetValue(player.ConnectionId, out var roles) || !roles.Contains(role))
            {
                await Clients.Caller.SendAsync("Error", "You don't have that role");
                return;
            }

            // Remove the chosen role
            RemoveSpecificInfluence(player, role, _store);
            game.Log.Add($"{player.Name} loses {role} ({pending.Reason}).");

            // Send updated roles to player
            if (_store.PlayerRoles.TryGetValue(player.ConnectionId, out var updatedRoles))
            {
                await Clients.Client(player.ConnectionId).SendAsync("YourRoles", updatedRoles);
            }

            // Clear pending
            game.PendingInfluenceLoss = null;

            // Check end game
            CheckEndGame(game);
            if (game.GameEnded)
            {
                await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                var stats = GenerateGameEndStats(game);
                await Clients.Group(gameId).SendAsync("GameEnded", stats);
                // Don't cleanup here - game needs to exist for rematch functionality
                return;
            }

                // Continue game flow
                await ContinueAfterInfluenceLoss(gameId, game);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ChooseCardToLose for connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "An error occurred while choosing a card. Please try again.");
            }
        }

        private async Task ContinueAfterInfluenceLoss(string gameId, GameState game)
        {
            var p = game.Pending;

            // If there's a pending action, handle continuation based on phase
            if (p != null)
            {
                if (p.Phase == PendingPhase.ActionClaim)
                {
                    // Challenge on action claim succeeded - action fails
                    if (p.Action == ActionType.Assassinate)
                    {
                        // For Assassinate during ActionClaim with successful challenge defense,
                        // we need to transition to BlockClaim phase
                        var claimedPlayer = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                        if (claimedPlayer != null && game.Log.Last().Contains("Claim was TRUE"))
                        {
                            // Challenger lost - continue with Assassinate to block phase
                            claimedPlayer.Coins -= ASSASSINATE_COST;
                            game.Log.Add($"{claimedPlayer.Name} pays {ASSASSINATE_COST} coins to attempt the assassination.");

                            p.Phase = PendingPhase.BlockClaim;
                            p.BlockerConnectionId = p.TargetConnectionId;
                            p.BlockClaimedRole = null;
                            p.BlockResponded.Clear();
                            p.BlockResponded.Add(claimedPlayer.ConnectionId);

                            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                            return;
                        }
                    }

                    // For all other cases in ActionClaim, challenge succeeded - action fails, advance turn
                    game.Pending = null;
                    game.PendingStartTime = null;
                    AdvanceTurn(game);
                    await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                    var next = game.Players[game.CurrentPlayerIndex];
                    await Clients.Client(next.ConnectionId).SendAsync("YourTurn");
                    return;
                }
                else if (p.Phase == PendingPhase.BlockClaim)
                {
                    // Challenge on block succeeded - execute the action
                    if (p.Action == ActionType.Assassinate)
                    {
                        var target = game.Players.FirstOrDefault(x => x.ConnectionId == p.TargetConnectionId);
                        if (target != null && target.IsAlive)
                        {
                            game.Log.Add($"Block fails. {target.Name} loses 1 influence (assassinated).");
                            RequestInfluenceLoss(game, target, "Assassinated");
                            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                            await Clients.Client(target.ConnectionId).SendAsync("ChooseInfluence", "You were assassinated. Choose a role to lose.");
                            return;
                        }
                    }
                    else if (p.Action == ActionType.ForeignAid)
                    {
                        var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                        if (actor != null && actor.IsAlive)
                        {
                            actor.Coins += FOREIGN_AID_GAIN;
                            game.Log.Add($"Block fails. {actor.Name} takes Foreign Aid (+{FOREIGN_AID_GAIN}).");
                        }
                    }
                    else if (p.Action == ActionType.Steal)
                    {
                        var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                        var target = game.Players.FirstOrDefault(x => x.ConnectionId == p.TargetConnectionId);
                        if (actor != null && actor.IsAlive && target != null && target.IsAlive)
                        {
                            var amount = Math.Min(STEAL_MAX, target.Coins);
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
                    AdvanceTurn(game);
                    await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                    var next = game.Players[game.CurrentPlayerIndex];
                    await Clients.Client(next.ConnectionId).SendAsync("YourTurn");
                    return;
                }
            }

            // No pending action - just advance turn
            AdvanceTurn(game);
            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
            var nextPlayer = game.Players[game.CurrentPlayerIndex];
            await Clients.Client(nextPlayer.ConnectionId).SendAsync("YourTurn");
        }

        public async Task SubmitExchangeCards(List<Role> chosenCards)
        {
            try
            {
                if (!_store.ConnectionToGame.TryGetValue(Context.ConnectionId, out var gameId)) return;
                if (!_store.Games.TryGetValue(gameId, out var game)) return;

                if (game.Pending == null || game.Pending.Phase != PendingPhase.ExchangeCardSelection)
                {
                    await Clients.Caller.SendAsync("Error", "No card selection in progress");
                    return;
                }

                var p = game.Pending;
                if (p.ActorConnectionId != Context.ConnectionId)
                {
                    await Clients.Caller.SendAsync("Error", "Only the actor can choose cards");
                    return;
                }

                var actor = game.Players.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
                if (actor == null || !actor.IsAlive)
                {
                    await Clients.Caller.SendAsync("Error", "Invalid player state");
                    return;
                }

                // Validate the chosen cards
                if (chosenCards == null || chosenCards.Count != p.ExchangeCardsToKeep)
                {
                    await Clients.Caller.SendAsync("Error", $"You must choose exactly {p.ExchangeCardsToKeep} cards");
                    return;
                }

                // Validate all chosen cards are from available cards
                if (p.ExchangeAvailableCards == null)
                {
                    await Clients.Caller.SendAsync("Error", "Invalid exchange state");
                    return;
                }

                foreach (var card in chosenCards)
                {
                    if (!p.ExchangeAvailableCards.Contains(card))
                    {
                        await Clients.Caller.SendAsync("Error", "Invalid card selection");
                        return;
                    }
                }

                // Process the exchange
                if (_store.GameDecks.TryGetValue(gameId, out var deck))
                {
                    // Determine which cards to return to deck
                    var returnedCards = new List<Role>(p.ExchangeAvailableCards);
                    foreach (var chosenCard in chosenCards)
                    {
                        returnedCards.Remove(chosenCard);
                    }

                    // Return unchosen cards to deck
                    deck.AddRange(returnedCards);

                    // Shuffle the deck
                    var rng = Random.Shared;
                    for (int i = deck.Count - 1; i > 0; i--)
                    {
                        int j = rng.Next(i + 1);
                        (deck[i], deck[j]) = (deck[j], deck[i]);
                    }

                    // Update player's roles
                    _store.PlayerRoles[Context.ConnectionId] = new List<Role>(chosenCards);
                    await Clients.Client(Context.ConnectionId).SendAsync("YourRoles", chosenCards);

                    game.Log.Add($"{actor.Name} completes exchange (Ambassador).");
                }

                // Clear pending and advance turn
                game.Pending = null;
                game.PendingStartTime = null;
                AdvanceTurn(game);
                await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                var next = game.Players[game.CurrentPlayerIndex];
                await Clients.Client(next.ConnectionId).SendAsync("YourTurn");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SubmitExchangeCards for connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "An error occurred while submitting your card choice. Please try again.");
            }
        }

        public async Task BlockPendingContessa()
        {
            if (!_store.ConnectionToGame.TryGetValue(Context.ConnectionId, out var gameId)) return;
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            // Vérifier timeout avant de continuer
            if (await CheckPendingTimeout(gameId)) return;

            if (game.Pending == null) { await Clients.Caller.SendAsync("Error", "No pending action to block"); return; }
            if (!game.GameStarted || game.GameEnded) return;

            var p = game.Pending;
            if (p.Action != ActionType.Assassinate)
            { await Clients.Caller.SendAsync("Error", "No blockable pending right now"); return; }

            // seul le target peut bloquer
            if (p.TargetConnectionId != Context.ConnectionId)
            { await Clients.Caller.SendAsync("Error", "Only the target can block assassination"); return; }

            // Si déjà block revendiqué, inutile
            if (p.BlockClaimedRole.HasValue)
            { await Clients.Caller.SendAsync("Error", "Block already claimed"); return; }

            var blocker = game.Players.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
            if (blocker == null) return;

            // If we're still in ActionClaim phase, transition to BlockClaim
            if (p.Phase == PendingPhase.ActionClaim)
            {
                // Pay the coins now and transition to block phase
                var actor = game.Players.FirstOrDefault(x => x.ConnectionId == p.ActorConnectionId);
                if (actor != null && actor.IsAlive)
                {
                    actor.Coins -= ASSASSINATE_COST;
                    game.Log.Add($"{actor.Name} pays {ASSASSINATE_COST} coins to attempt the assassination.");
                }

                p.Phase = PendingPhase.BlockClaim;
                p.BlockResponded.Clear();
            }

            p.BlockerConnectionId = Context.ConnectionId;
            p.BlockClaimedRole = Role.Contessa;
            p.BlockResponded.Add(Context.ConnectionId); // Only the blocker has responded

            game.Log.Add($"{blocker.Name} claims Contessa to block the assassination. Others may 'challenge' the block, or the actor waits.");

            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
        }

        public async Task BlockDuke()
        {
            if (!_store.ConnectionToGame.TryGetValue(Context.ConnectionId, out var gameId)) return;
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            // Vérifier timeout avant de continuer
            if (await CheckPendingTimeout(gameId)) return;

            if (game.Pending == null) { await Clients.Caller.SendAsync("Error", "No pending action to block"); return; }
            if (!game.GameStarted || game.GameEnded) return;

            var p = game.Pending;
            if (p.Action != ActionType.ForeignAid || p.Phase != PendingPhase.ActionClaim)
            { await Clients.Caller.SendAsync("Error", "No blockable Foreign Aid right now"); return; }

            // N'importe quel joueur (sauf l'acteur) peut bloquer avec Duke
            if (p.ActorConnectionId == Context.ConnectionId)
            { await Clients.Caller.SendAsync("Error", "You cannot block your own action"); return; }

            var blocker = game.Players.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
            if (blocker == null || !blocker.IsAlive)
            { await Clients.Caller.SendAsync("Error", "You cannot block"); return; }

            // Marquer comme répondu
            p.Responded.Add(Context.ConnectionId);

            // Créer un block claim
            p.Phase = PendingPhase.BlockClaim;
            p.BlockerConnectionId = Context.ConnectionId;
            p.BlockClaimedRole = Role.Duke;
            p.BlockResponded.Clear();
            p.BlockResponded.Add(Context.ConnectionId);

            game.Log.Add($"{blocker.Name} claims Duke to block Foreign Aid. Others may 'challenge' the block.");

            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
        }

        public async Task BlockCaptainAmbassador(Role role)
        {
            if (!_store.ConnectionToGame.TryGetValue(Context.ConnectionId, out var gameId)) return;
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            // Vérifier timeout avant de continuer
            if (await CheckPendingTimeout(gameId)) return;

            if (game.Pending == null) { await Clients.Caller.SendAsync("Error", "No pending action to block"); return; }
            if (!game.GameStarted || game.GameEnded) return;

            var p = game.Pending;
            if (p.Action != ActionType.Steal)
            { await Clients.Caller.SendAsync("Error", "No blockable Steal right now"); return; }

            // Seul le target peut bloquer le steal
            if (p.TargetConnectionId != Context.ConnectionId)
            { await Clients.Caller.SendAsync("Error", "Only the target can block steal"); return; }

            // Vérifier que le rôle est Captain ou Ambassador
            if (role != Role.Captain && role != Role.Ambassador)
            { await Clients.Caller.SendAsync("Error", "You can only block with Captain or Ambassador"); return; }

            // Si déjà block revendiqué, inutile
            if (p.BlockClaimedRole.HasValue)
            { await Clients.Caller.SendAsync("Error", "Block already claimed"); return; }

            var blocker = game.Players.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
            if (blocker == null) return;

            // If we're still in ActionClaim phase, transition to BlockClaim
            if (p.Phase == PendingPhase.ActionClaim)
            {
                p.Phase = PendingPhase.BlockClaim;
                p.BlockResponded.Clear();
            }

            p.BlockerConnectionId = Context.ConnectionId;
            p.BlockClaimedRole = role;
            p.BlockResponded.Add(Context.ConnectionId); // Only the blocker has responded

            game.Log.Add($"{blocker.Name} claims {role} to block the steal. Others may 'challenge' the block.");

            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
        }

        private static bool AllOthersResponded(GameState game)
        {
            var pending = game.Pending;
            if (pending == null) return false;

            var others = game.Players
                .Where(p => p.IsAlive && p.ConnectionId != pending.ActorConnectionId)
                .Select(p => p.ConnectionId)
                .ToHashSet();

            // Tous les autres vivants ont soit challenge, soit pass
            return others.All(id => pending.Responded.Contains(id));
        }

        public override async Task OnDisconnectedAsync(System.Exception? exception)
        {
            if (_store.ConnectionToGame.TryGetValue(Context.ConnectionId, out var gameId) &&
                _store.Games.TryGetValue(gameId, out var game))
            {
                var player = game.Players.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
                if (player != null)
                {
                    // Mark player as disconnected instead of removing them
                    player.IsConnected = false;
                    player.DisconnectedTime = DateTime.UtcNow;
                    game.Log.Add($"{player.Name} disconnected. They can reconnect with the same name.");
                    _logger.LogInformation("Player {PlayerName} disconnected from game {GameId}", player.Name, gameId);

                    // Remove from ConnectionToGame mapping
                    _store.ConnectionToGame.TryRemove(Context.ConnectionId, out _);

                    // If it was their turn and game is still active, advance to next connected player
                    var currentPlayer = game.Players[game.CurrentPlayerIndex];
                    if (currentPlayer.ConnectionId == Context.ConnectionId && game.GameStarted && !game.GameEnded)
                    {
                        // Skip to next connected player
                        AdvanceTurnToConnectedPlayer(game);
                    }

                    // Check if all connected players are eliminated
                    var connectedAlivePlayers = game.Players.Count(p => p.IsConnected && p.IsAlive);
                    if (connectedAlivePlayers <= 1 && !game.GameEnded)
                    {
                        CheckEndGame(game);
                    }

                    await Clients.Group(gameId).SendAsync("GameStateUpdated", game);

                    if (!game.GameEnded && game.GameStarted)
                    {
                        var next = game.Players[game.CurrentPlayerIndex];
                        if (next.IsConnected)
                        {
                            await Clients.Client(next.ConnectionId).SendAsync("YourTurn");
                        }
                    }
                    else if (game.GameEnded)
                    {
                        var stats = GenerateGameEndStats(game);
                        await Clients.Group(gameId).SendAsync("GameEnded", stats);
                        // Don't cleanup here - game needs to exist for rematch functionality
                    }
                }
            }
            await base.OnDisconnectedAsync(exception);
        }

        private static void AdvanceTurnToConnectedPlayer(GameState game)
        {
            // Advance turn until we find a connected and alive player
            int attempts = 0;
            int maxAttempts = game.Players.Count;

            do
            {
                game.CurrentPlayerIndex = (game.CurrentPlayerIndex + 1) % game.Players.Count;
                var player = game.Players[game.CurrentPlayerIndex];

                if (player.IsAlive && player.IsConnected)
                {
                    return; // Found a valid player
                }

                attempts++;
            } while (attempts < maxAttempts);

            // If no connected alive players found, just set to first alive player (disconnected)
            for (int i = 0; i < game.Players.Count; i++)
            {
                if (game.Players[i].IsAlive)
                {
                    game.CurrentPlayerIndex = i;
                    return;
                }
            }
        }

        // ==================== BOT MANAGEMENT METHODS ====================

        /// <summary>
        /// Adds a bot player to the lobby (host only)
        /// </summary>
        public async Task AddBot(string gameId, BotDifficulty difficulty, BotPersonality personality)
        {
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            // Validate: game not started, caller is host
            if (game.GameStarted)
            {
                await Clients.Caller.SendAsync("Error", "Cannot add bots after game has started");
                return;
            }

            if (game.HostConnectionId != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Only the host can add bots");
                return;
            }

            // Generate bot name
            var botNames = new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel" };
            var existingBotCount = game.Players.Count(p => p.IsBot);
            var botName = $"Bot_{botNames[existingBotCount % botNames.Length]}";

            // Create synthetic ConnectionId
            var botConnectionId = $"bot_{Guid.NewGuid():N}";

            // Create bot player
            var bot = new PlayerState
            {
                ConnectionId = botConnectionId,
                Name = botName,
                Coins = STARTING_COINS,
                InfluenceCount = STARTING_INFLUENCE,
                IsBot = true,
                IsConnected = true
            };

            game.Players.Add(bot);
            _store.ConnectionToGame[botConnectionId] = gameId;

            // Store bot config
            _store.BotConfigs[botConnectionId] = new BotConfig
            {
                Difficulty = difficulty,
                Personality = personality
            };

            game.Log.Add($"🤖 {botName} ({difficulty}, {personality}) joined the game.");
            _logger.LogInformation("Bot {BotName} added to game {GameId} with difficulty {Difficulty} and personality {Personality}",
                botName, gameId, difficulty, personality);

            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
        }

        /// <summary>
        /// Removes a bot player from the lobby (host only)
        /// </summary>
        public async Task RemoveBot(string gameId, string botConnectionId)
        {
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            // Validate: game not started, caller is host
            if (game.GameStarted)
            {
                await Clients.Caller.SendAsync("Error", "Cannot remove bots after game has started");
                return;
            }

            if (game.HostConnectionId != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Only the host can remove bots");
                return;
            }

            var bot = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId && p.IsBot);
            if (bot == null)
            {
                await Clients.Caller.SendAsync("Error", "Bot not found");
                return;
            }

            game.Players.Remove(bot);
            _store.ConnectionToGame.TryRemove(botConnectionId, out _);
            _store.BotConfigs.TryRemove(botConnectionId, out _);

            game.Log.Add($"🤖 {bot.Name} was removed from the game.");
            _logger.LogInformation("Bot {BotName} removed from game {GameId}", bot.Name, gameId);

            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
        }

        // ==================== BOT ACTION METHODS (ForBot variants) ====================

        /// <summary>
        /// Performs an action for a bot player
        /// </summary>
        public async Task PerformActionForBot(string gameId, string botConnectionId, ActionDto action)
        {
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            var actor = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
            if (actor == null || !actor.IsBot) return;

            // Reuse existing PerformAction logic by temporarily treating bot as current context
            // This is a simplified approach - in production, you'd extract the core logic
            await PerformActionInternal(game, actor, action, gameId);
        }

        /// <summary>
        /// Challenge for a bot player
        /// </summary>
        public async Task ChallengeForBot(string gameId, string botConnectionId)
        {
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            var challenger = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
            if (challenger == null || !challenger.IsBot) return;

            await ChallengeInternal(game, challenger, gameId);
        }

        /// <summary>
        /// Pass pending for a bot player
        /// </summary>
        public async Task PassPendingForBot(string gameId, string botConnectionId)
        {
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            var passer = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
            if (passer == null || !passer.IsBot) return;

            await PassPendingInternal(game, passer, gameId);
        }

        /// <summary>
        /// Block Duke for a bot player
        /// </summary>
        public async Task BlockDukeForBot(string gameId, string botConnectionId)
        {
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            var blocker = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
            if (blocker == null || !blocker.IsBot) return;

            await BlockDukeInternal(game, blocker, gameId);
        }

        /// <summary>
        /// Block Contessa for a bot player
        /// </summary>
        public async Task BlockPendingContessaForBot(string gameId, string botConnectionId)
        {
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            var blocker = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
            if (blocker == null || !blocker.IsBot) return;

            await BlockContessaInternal(game, blocker, gameId);
        }

        /// <summary>
        /// Block Captain/Ambassador for a bot player
        /// </summary>
        public async Task BlockCaptainAmbassadorForBot(string gameId, string botConnectionId, Role role)
        {
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            var blocker = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
            if (blocker == null || !blocker.IsBot) return;

            await BlockCaptainAmbassadorInternal(game, blocker, role, gameId);
        }

        /// <summary>
        /// Choose card to lose for a bot player
        /// </summary>
        public async Task ChooseCardToLoseForBot(string gameId, string botConnectionId, Role role)
        {
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            var player = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
            if (player == null || !player.IsBot) return;

            await ChooseCardToLoseInternal(game, player, role, gameId);
        }

        /// <summary>
        /// Submit exchange cards for a bot player
        /// </summary>
        public async Task SubmitExchangeCardsForBot(string gameId, string botConnectionId, List<Role> chosenCards)
        {
            if (!_store.Games.TryGetValue(gameId, out var game)) return;

            var player = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
            if (player == null || !player.IsBot) return;

            await SubmitExchangeCardsInternal(game, player, chosenCards, gameId);
        }

        // ==================== INTERNAL HELPER METHODS ====================
        // These methods contain the core logic and are called by both regular and bot methods

        private async Task PerformActionInternal(GameState game, PlayerState actor, ActionDto action, string gameId)
        {
            // Validate game state
            if (!game.GameStarted || game.GameEnded) return;

            // Check turn
            var current = game.Players[game.CurrentPlayerIndex];
            if (current.ConnectionId != actor.ConnectionId) return;

            // Check mandatory coup
            if (actor.Coins >= MANDATORY_COUP_COINS && action.Action != ActionType.Coup) return;

            // Process action (simplified version of PerformAction logic)
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

            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);

            // Notify next player if turn advanced
            if (game.Pending == null)
            {
                var nextPlayer = game.Players[game.CurrentPlayerIndex];
                if (!nextPlayer.IsBot)
                {
                    await Clients.Client(nextPlayer.ConnectionId).SendAsync("YourTurn");
                }
            }
        }

        private async Task PassPendingInternal(GameState game, PlayerState passer, string gameId)
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
                    await ExecutePendingAction(game, gameId);
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

            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
        }

        private async Task ChallengeInternal(GameState game, PlayerState challenger, string gameId)
        {
            // Simplified challenge implementation
            // Full implementation would match the existing Challenge method
            game.Log.Add($"{challenger.Name} challenges!");
            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
        }

        private async Task BlockDukeInternal(GameState game, PlayerState blocker, string gameId)
        {
            if (game.Pending == null || game.Pending.Action != ActionType.ForeignAid) return;

            game.Pending.Phase = PendingPhase.BlockClaim;
            game.Pending.BlockerConnectionId = blocker.ConnectionId;
            game.Pending.BlockClaimedRole = Role.Duke;
            game.Pending.BlockResponded.Clear();
            game.Pending.BlockResponded.Add(game.Pending.ActorConnectionId);

            game.Log.Add($"{blocker.Name} blocks Foreign Aid with Duke!");
            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
        }

        private async Task BlockContessaInternal(GameState game, PlayerState blocker, string gameId)
        {
            if (game.Pending == null || game.Pending.Action != ActionType.Assassinate) return;
            if (game.Pending.TargetConnectionId != blocker.ConnectionId) return;

            game.Pending.Phase = PendingPhase.BlockClaim;
            game.Pending.BlockerConnectionId = blocker.ConnectionId;
            game.Pending.BlockClaimedRole = Role.Contessa;
            game.Pending.BlockResponded.Clear();
            game.Pending.BlockResponded.Add(game.Pending.ActorConnectionId);

            game.Log.Add($"{blocker.Name} blocks Assassination with Contessa!");
            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
        }

        private async Task BlockCaptainAmbassadorInternal(GameState game, PlayerState blocker, Role role, string gameId)
        {
            if (game.Pending == null || game.Pending.Action != ActionType.Steal) return;
            if (game.Pending.TargetConnectionId != blocker.ConnectionId) return;

            game.Pending.Phase = PendingPhase.BlockClaim;
            game.Pending.BlockerConnectionId = blocker.ConnectionId;
            game.Pending.BlockClaimedRole = role;
            game.Pending.BlockResponded.Clear();
            game.Pending.BlockResponded.Add(game.Pending.ActorConnectionId);

            game.Log.Add($"{blocker.Name} blocks Steal with {role}!");
            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
        }

        private async Task ChooseCardToLoseInternal(GameState game, PlayerState player, Role role, string gameId)
        {
            var pending = game.PendingInfluenceLoss;
            if (pending == null || pending.PlayerConnectionId != player.ConnectionId) return;

            // Validate role is in player's hand
            if (!_store.PlayerRoles.TryGetValue(player.ConnectionId, out var roles) || !roles.Contains(role)) return;

            // Remove the chosen role
            RemoveSpecificInfluence(player, role, _store);
            game.Log.Add($"{player.Name} loses {role} ({pending.Reason}).");

            // Send updated roles to player (skip if bot)
            if (!player.IsBot && _store.PlayerRoles.TryGetValue(player.ConnectionId, out var updatedRoles))
            {
                await Clients.Client(player.ConnectionId).SendAsync("YourRoles", updatedRoles);
            }

            // Clear pending
            game.PendingInfluenceLoss = null;

            // Check end game
            CheckEndGame(game);
            if (game.GameEnded)
            {
                await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
                var stats = GenerateGameEndStats(game);
                await Clients.Group(gameId).SendAsync("GameEnded", stats);
                return;
            }

            // Continue game flow
            await ContinueAfterInfluenceLoss(gameId, game);
        }

        private async Task SubmitExchangeCardsInternal(GameState game, PlayerState player, List<Role> chosenCards, string gameId)
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
                await Clients.Client(player.ConnectionId).SendAsync("YourRoles", chosenCards);
            }

            // Clear pending and advance turn
            game.Pending = null;
            game.PendingStartTime = null;
            AdvanceTurn(game);

            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);

            var nextPlayer = game.Players[game.CurrentPlayerIndex];
            if (!nextPlayer.IsBot)
            {
                await Clients.Client(nextPlayer.ConnectionId).SendAsync("YourTurn");
            }
        }

        private async Task ExecutePendingAction(GameState game, string gameId)
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
                            await Clients.Client(actor.ConnectionId).SendAsync("ChooseExchangeCards", allCards, actor.InfluenceCount);
                        }
                    }
                    break;
            }

            await Clients.Group(gameId).SendAsync("GameStateUpdated", game);
        }

    }
}
