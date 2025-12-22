using Coup.Server.AI;
using Coup.Shared;

namespace Coup.Server.Services;

/// <summary>
/// Core bot AI decision engine. Delegates to strategy implementations based on difficulty.
/// Handles all 6 decision points: actions, challenges, blocks, card selection.
/// </summary>
public class BotDecisionEngine
{
    private readonly GameStore _store;
    private readonly BotActionExecutor _executor;
    private readonly EasyBotStrategy _easyStrategy;
    private readonly MediumBotStrategy _mediumStrategy;
    private readonly HardBotStrategy _hardStrategy;
    private readonly Dictionary<string, List<RevealedCard>> _gameRevealedCards = new();

    public BotDecisionEngine(
        GameStore store,
        BotActionExecutor executor,
        EasyBotStrategy easyStrategy,
        MediumBotStrategy mediumStrategy,
        HardBotStrategy hardStrategy)
    {
        _store = store;
        _executor = executor;
        _easyStrategy = easyStrategy;
        _mediumStrategy = mediumStrategy;
        _hardStrategy = hardStrategy;
    }

    /// <summary>
    /// Gets the appropriate strategy based on bot difficulty
    /// </summary>
    private IBotStrategy GetStrategy(BotDifficulty difficulty)
    {
        return difficulty switch
        {
            BotDifficulty.Easy => _easyStrategy,
            BotDifficulty.Medium => _mediumStrategy,
            BotDifficulty.Hard => _hardStrategy,
            _ => _easyStrategy
        };
    }

    /// <summary>
    /// Makes a decision for a bot based on the current game state
    /// </summary>
    public async Task MakeDecisionAsync(string gameId, string botConnectionId)
    {
        if (!_store.Games.TryGetValue(gameId, out var game)) return;
        if (!_store.BotConfigs.TryGetValue(botConnectionId, out var config)) return;

        var bot = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
        if (bot == null || !bot.IsBot) return;

        // Build decision context
        var ctx = BuildContext(game, bot, config);

        // Determine which decision point we're at
        if (game.PendingInfluenceLoss != null &&
            game.PendingInfluenceLoss.PlayerConnectionId == botConnectionId)
        {
            // Decision Point 5: Choose card to lose
            await HandleChooseCardToLose(gameId, botConnectionId, ctx);
        }
        else if (game.Pending != null &&
                 game.Pending.Phase == PendingPhase.ExchangeCardSelection &&
                 game.Pending.ActorConnectionId == botConnectionId)
        {
            // Decision Point 6: Exchange card selection
            await HandleExchangeCardSelection(gameId, botConnectionId, ctx);
        }
        else if (game.Pending != null)
        {
            // Decision Points 2-4: Challenge, Block, or Pass
            await HandlePendingResponse(gameId, botConnectionId, ctx);
        }
        else if (game.CurrentPlayer?.ConnectionId == botConnectionId)
        {
            // Decision Point 1: Take action
            await HandleTakeAction(gameId, botConnectionId, ctx);
        }
    }

    private async Task HandleTakeAction(string gameId, string botConnectionId, BotDecisionContext ctx)
    {
        var strategy = GetStrategy(ctx.Difficulty);
        var (action, target) = strategy.DecideAction(ctx);

        // Determine claimed role based on action
        Role? claimedRole = action switch
        {
            ActionType.Tax => Role.Duke,
            ActionType.Assassinate => Role.Assassin,
            ActionType.Steal => Role.Captain,
            ActionType.Exchange => Role.Ambassador,
            _ => null
        };

        await _executor.ExecuteActionAsync(gameId, botConnectionId, action, claimedRole, target);
    }

    private async Task HandlePendingResponse(string gameId, string botConnectionId, BotDecisionContext ctx)
    {
        var pending = ctx.Pending!;
        var strategy = GetStrategy(ctx.Difficulty);

        if (pending.Phase == PendingPhase.BlockClaim)
        {
            // Can challenge the block or pass
            if (pending.BlockerConnectionId != botConnectionId &&
                !pending.BlockResponded.Contains(botConnectionId))
            {
                // TODO: Implement challenge logic when needed
                // For now, bots always pass on block claims
                await _executor.ExecutePassAsync(gameId, botConnectionId);
            }
        }
        else if (pending.Phase == PendingPhase.ActionClaim)
        {
            var actor = ctx.Game.Players.First(p => p.ConnectionId == pending.ActorConnectionId);

            // Check if bot can block this action
            var canBlock = CanBlock(pending.Action, botConnectionId, pending.TargetConnectionId);

            if (canBlock)
            {
                var (shouldBlock, blockRole) = strategy.ShouldBlock(ctx);
                if (shouldBlock && blockRole.HasValue)
                {
                    // Execute block
                    await ExecuteBlock(gameId, botConnectionId, pending.Action, blockRole.Value);
                    return;
                }
            }

            // Can challenge or pass (if not the actor)
            if (pending.ActorConnectionId != botConnectionId &&
                !pending.Responded.Contains(botConnectionId))
            {
                // TODO: Implement challenge logic when needed
                // For now, bots always pass on action claims
                await _executor.ExecutePassAsync(gameId, botConnectionId);
            }
        }
    }

    private async Task HandleChooseCardToLose(string gameId, string botConnectionId, BotDecisionContext ctx)
    {
        var strategy = GetStrategy(ctx.Difficulty);
        var cardToLose = strategy.ChooseCardToLose(ctx);
        await _executor.ExecuteChooseCardToLoseAsync(gameId, botConnectionId, cardToLose);
    }

    private async Task HandleExchangeCardSelection(string gameId, string botConnectionId, BotDecisionContext ctx)
    {
        var strategy = GetStrategy(ctx.Difficulty);
        var cardsToKeep = strategy.ChooseExchangeCards(ctx);
        await _executor.ExecuteExchangeCardsAsync(gameId, botConnectionId, cardsToKeep);
    }

    private async Task ExecuteBlock(string gameId, string botConnectionId, ActionType action, Role blockRole)
    {
        switch (action)
        {
            case ActionType.ForeignAid:
                await _executor.ExecuteBlockDukeAsync(gameId, botConnectionId);
                break;
            case ActionType.Assassinate:
                await _executor.ExecuteBlockContessaAsync(gameId, botConnectionId);
                break;
            case ActionType.Steal:
                await _executor.ExecuteBlockCaptainAmbassadorAsync(gameId, botConnectionId, blockRole);
                break;
        }
    }

    private bool CanBlock(ActionType action, string botConnectionId, string? targetConnectionId)
    {
        return action switch
        {
            ActionType.ForeignAid => true, // Anyone can block
            ActionType.Assassinate => botConnectionId == targetConnectionId, // Only target
            ActionType.Steal => botConnectionId == targetConnectionId, // Only target
            _ => false
        };
    }

    private BotDecisionContext BuildContext(GameState game, PlayerState bot, BotConfig config)
    {
        // Get bot's roles
        var botRoles = _store.PlayerRoles.TryGetValue(bot.ConnectionId, out var roles)
            ? roles
            : new List<Role>();

        // Get revealed cards for this game
        if (!_gameRevealedCards.ContainsKey(game.GameId))
        {
            _gameRevealedCards[game.GameId] = new List<RevealedCard>();
        }

        // Calculate player risk scores
        var riskScores = new Dictionary<string, int>();
        foreach (var player in game.Players.Where(p => p.IsAlive && p.ConnectionId != bot.ConnectionId))
        {
            var risk = CalculateRiskScore(player);
            riskScores[player.ConnectionId] = risk;
        }

        return new BotDecisionContext
        {
            Game = game,
            BotPlayer = bot,
            BotRoles = botRoles,
            Pending = game.Pending,
            Difficulty = config.Difficulty,
            Personality = config.Personality,
            Config = config,
            VisibleCards = _gameRevealedCards[game.GameId],
            PlayerRiskScores = riskScores
        };
    }

    private int CalculateRiskScore(PlayerState player)
    {
        // Simple risk calculation: coins * 10 + influence * 20
        // Higher coins = can Coup/Assassinate
        // Higher influence = harder to eliminate
        return (player.Coins * 10) + (player.InfluenceCount * 20);
    }

    /// <summary>
    /// Tracks a card that was revealed (for probability calculations)
    /// </summary>
    public void TrackRevealedCard(string gameId, string playerId, string playerName, Role role, string reason)
    {
        if (!_gameRevealedCards.ContainsKey(gameId))
        {
            _gameRevealedCards[gameId] = new List<RevealedCard>();
        }

        _gameRevealedCards[gameId].Add(new RevealedCard
        {
            PlayerId = playerId,
            PlayerName = playerName,
            Role = role,
            Reason = reason
        });
    }
}
