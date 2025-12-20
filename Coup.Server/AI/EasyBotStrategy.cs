using Coup.Shared;

namespace Coup.Server.AI;

/// <summary>
/// Easy difficulty bot strategy - makes completely random decisions.
/// Fast, impulsive gameplay (200-500ms delays).
/// Win rate target: ~20-30%
/// </summary>
public class EasyBotStrategy : IBotStrategy
{
    public (ActionType Action, string? Target) DecideAction(BotDecisionContext ctx)
    {
        var bot = ctx.BotPlayer;
        var opponents = ctx.Opponents.ToList();

        // Must Coup if 10+ coins (game rule)
        if (ctx.MustCoup)
        {
            return (ActionType.Coup, ChooseRandomTarget(opponents));
        }

        // Random weighted action selection
        var actions = new List<(ActionType Action, int Weight)>
        {
            (ActionType.Income, 30),
            (ActionType.ForeignAid, 30),
            (ActionType.Tax, 10),
            (ActionType.Steal, bot.Coins >= 2 && opponents.Any(p => p.Coins > 0) ? 15 : 0),
            (ActionType.Assassinate, bot.Coins >= 3 ? 10 : 0),
            (ActionType.Coup, bot.Coins >= 7 ? 20 : 0),
            (ActionType.Exchange, 10)
        };

        var action = WeightedRandom(actions.Where(a => a.Weight > 0).ToList());
        var target = NeedsTarget(action) ? ChooseRandomTarget(opponents) : null;

        return (action, target);
    }

    public bool ShouldChallenge(BotDecisionContext ctx)
    {
        // Never challenge if would die
        if (ctx.BotPlayer.InfluenceCount == 1)
            return false;

        // 20% random challenge rate
        return Random.Shared.NextDouble() < 0.2;
    }

    public (bool ShouldBlock, Role? BlockRole) ShouldBlock(BotDecisionContext ctx)
    {
        var pending = ctx.Pending;
        if (pending == null) return (false, null);

        // ForeignAid - anyone can block with Duke
        if (pending.Action == ActionType.ForeignAid)
        {
            // 30% chance to block (random)
            if (Random.Shared.NextDouble() < 0.3)
                return (true, Role.Duke);
        }

        // Assassinate - target can block with Contessa
        if (pending.Action == ActionType.Assassinate &&
            pending.TargetConnectionId == ctx.BotPlayer.ConnectionId)
        {
            // 40% chance to block (higher since being assassinated)
            if (Random.Shared.NextDouble() < 0.4)
                return (true, Role.Contessa);
        }

        // Steal - target can block with Captain or Ambassador
        if (pending.Action == ActionType.Steal &&
            pending.TargetConnectionId == ctx.BotPlayer.ConnectionId)
        {
            // 35% chance to block
            if (Random.Shared.NextDouble() < 0.35)
            {
                // Randomly choose Captain or Ambassador
                var role = Random.Shared.Next(2) == 0 ? Role.Captain : Role.Ambassador;
                return (true, role);
            }
        }

        return (false, null);
    }

    public bool ShouldChallengeBlock(BotDecisionContext ctx)
    {
        // Never challenge if would die
        if (ctx.BotPlayer.InfluenceCount == 1)
            return false;

        // 15% random challenge rate (lower than action challenges)
        return Random.Shared.NextDouble() < 0.15;
    }

    public Role ChooseCardToLose(BotDecisionContext ctx)
    {
        var roles = ctx.BotRoles;

        // Completely random selection
        return roles[Random.Shared.Next(roles.Count)];
    }

    public List<Role> ChooseExchangeCards(BotDecisionContext ctx)
    {
        var available = ctx.Pending?.ExchangeAvailableCards;
        var toKeep = ctx.Pending?.ExchangeCardsToKeep ?? ctx.BotPlayer.InfluenceCount;

        if (available == null || available.Count == 0)
            return ctx.BotRoles;

        // Randomly shuffle and take first N cards
        var shuffled = available.OrderBy(_ => Random.Shared.Next()).ToList();
        return shuffled.Take(toKeep).ToList();
    }

    // Helper methods

    private static string ChooseRandomTarget(List<PlayerState> opponents)
    {
        if (opponents.Count == 0)
            throw new InvalidOperationException("No valid targets available");

        return opponents[Random.Shared.Next(opponents.Count)].ConnectionId;
    }

    private static bool NeedsTarget(ActionType action)
    {
        return action is ActionType.Coup or ActionType.Assassinate or ActionType.Steal;
    }

    private static ActionType WeightedRandom(List<(ActionType Action, int Weight)> actions)
    {
        var totalWeight = actions.Sum(a => a.Weight);
        var random = Random.Shared.Next(totalWeight);

        var cumulative = 0;
        foreach (var (action, weight) in actions)
        {
            cumulative += weight;
            if (random < cumulative)
                return action;
        }

        return actions[^1].Action; // Fallback to last action
    }
}
