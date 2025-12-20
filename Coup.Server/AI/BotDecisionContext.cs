using Coup.Shared;

namespace Coup.Server.AI;

/// <summary>
/// Contains all context information needed for bot AI decision-making.
/// Provides bot's visible information: own roles, game state, and public information only.
/// NEVER includes opponent's hidden cards (anti-cheat).
/// </summary>
public class BotDecisionContext
{
    /// <summary>
    /// Current game state (public information)
    /// </summary>
    public required GameState Game { get; init; }

    /// <summary>
    /// The bot player making the decision
    /// </summary>
    public required PlayerState BotPlayer { get; init; }

    /// <summary>
    /// Bot's current role cards (private to this bot only)
    /// </summary>
    public required List<Role> BotRoles { get; init; }

    /// <summary>
    /// Current pending action (if any)
    /// </summary>
    public PendingAction? Pending { get; init; }

    /// <summary>
    /// Bot difficulty level
    /// </summary>
    public required BotDifficulty Difficulty { get; init; }

    /// <summary>
    /// Bot personality type
    /// </summary>
    public required BotPersonality Personality { get; init; }

    /// <summary>
    /// Bot configuration (for modifiers and delays)
    /// </summary>
    public required BotConfig Config { get; init; }

    /// <summary>
    /// Cards that have been revealed during the game (from challenges, influence loss).
    /// Bots can track these to make better probability calculations.
    /// </summary>
    public List<RevealedCard> VisibleCards { get; init; } = new();

    /// <summary>
    /// Risk scores for each player (0-100). Higher = more dangerous opponent.
    /// Updated based on player actions, coins, and influence.
    /// </summary>
    public Dictionary<string, int> PlayerRiskScores { get; init; } = new();

    /// <summary>
    /// Checks if it's currently the bot's turn
    /// </summary>
    public bool IsMyTurn => Game.CurrentPlayer?.ConnectionId == BotPlayer.ConnectionId;

    /// <summary>
    /// Gets all alive opponents (not including the bot)
    /// </summary>
    public IEnumerable<PlayerState> Opponents =>
        Game.Players.Where(p => p.IsAlive && p.ConnectionId != BotPlayer.ConnectionId);

    /// <summary>
    /// Gets all alive opponents with coins (potential Steal targets)
    /// </summary>
    public IEnumerable<PlayerState> OpponentsWithCoins =>
        Opponents.Where(p => p.Coins > 0);

    /// <summary>
    /// Checks if bot can afford an action
    /// </summary>
    public bool CanAfford(ActionType action) => action switch
    {
        ActionType.Coup => BotPlayer.Coins >= 7,
        ActionType.Assassinate => BotPlayer.Coins >= 3,
        _ => true
    };

    /// <summary>
    /// Checks if bot must Coup (10+ coins)
    /// </summary>
    public bool MustCoup => BotPlayer.Coins >= 10;
}

/// <summary>
/// Represents a card that was revealed during the game (from challenge or death)
/// </summary>
public class RevealedCard
{
    public required string PlayerId { get; init; }
    public required string PlayerName { get; init; }
    public required Role Role { get; init; }
    public required string Reason { get; init; } // "Challenge", "Death", "Shuffle"
}
