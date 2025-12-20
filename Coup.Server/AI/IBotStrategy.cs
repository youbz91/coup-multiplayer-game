using Coup.Shared;

namespace Coup.Server.AI;

/// <summary>
/// Strategy interface for bot AI decision-making.
/// Implementations provide different difficulty levels (Easy, Medium, Hard).
/// </summary>
public interface IBotStrategy
{
    /// <summary>
    /// Decides which action the bot should take on its turn.
    /// Must respect game rules (e.g., Coup if 10+ coins).
    /// </summary>
    /// <param name="ctx">Current game context and bot state</param>
    /// <returns>The action type to perform and optional target</returns>
    (ActionType Action, string? Target) DecideAction(BotDecisionContext ctx);

    /// <summary>
    /// Decides whether the bot should challenge a pending action claim.
    /// Only called when bot is eligible to challenge (not the actor).
    /// </summary>
    /// <param name="ctx">Current game context with pending action</param>
    /// <returns>True to challenge, false to pass</returns>
    bool ShouldChallenge(BotDecisionContext ctx);

    /// <summary>
    /// Decides whether the bot should block a pending action.
    /// Only called for blockable actions (ForeignAid, Assassinate, Steal).
    /// </summary>
    /// <param name="ctx">Current game context with pending action</param>
    /// <returns>Block details (role claimed) if blocking, null otherwise</returns>
    (bool ShouldBlock, Role? BlockRole) ShouldBlock(BotDecisionContext ctx);

    /// <summary>
    /// Decides whether the bot should challenge a pending block claim.
    /// Only called when bot is eligible to challenge the blocker.
    /// </summary>
    /// <param name="ctx">Current game context with pending block</param>
    /// <returns>True to challenge the block, false to pass</returns>
    bool ShouldChallengeBlock(BotDecisionContext ctx);

    /// <summary>
    /// Chooses which influence card to lose when forced to lose one.
    /// Called after failed challenges, Coup, or successful Assassination.
    /// </summary>
    /// <param name="ctx">Current game context with bot's roles</param>
    /// <returns>The role to discard from hand</returns>
    Role ChooseCardToLose(BotDecisionContext ctx);

    /// <summary>
    /// Chooses which cards to keep after using Ambassador Exchange.
    /// Bot receives current cards + 2 drawn cards and must select which to keep.
    /// </summary>
    /// <param name="ctx">Current game context with available cards</param>
    /// <returns>List of roles to keep (length = bot's influence count)</returns>
    List<Role> ChooseExchangeCards(BotDecisionContext ctx);
}
