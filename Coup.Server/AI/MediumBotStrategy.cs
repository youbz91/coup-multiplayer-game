using Coup.Shared;

namespace Coup.Server.AI;

/// <summary>
/// Medium difficulty bot strategy - Uses basic heuristics and respects personality
/// </summary>
public class MediumBotStrategy : IBotStrategy
{
    public (ActionType Action, string? Target) DecideAction(BotDecisionContext ctx)
    {
        var bot = ctx.BotPlayer;
        var opponents = ctx.Game.Players.Where(p => p.IsAlive && p.ConnectionId != bot.ConnectionId).ToList();

        // MANDATORY COUP at 10+ coins
        if (ctx.MustCoup)
        {
            var target = ChooseTargetByPersonality(opponents, ctx.Personality);
            return (ActionType.Coup, target.ConnectionId);
        }

        // Consider COUP at 7+ coins (personality-based)
        if (bot.Coins >= 7)
        {
            bool shouldCoup = ctx.Personality switch
            {
                BotPersonality.Aggressive => Random.Shared.Next(100) < 70, // 70% chance
                BotPersonality.Balanced => Random.Shared.Next(100) < 40,   // 40% chance
                _ => Random.Shared.Next(100) < 20                          // 20% chance
            };

            if (shouldCoup)
            {
                var target = ChooseTargetByPersonality(opponents, ctx.Personality);
                return (ActionType.Coup, target.ConnectionId);
            }
        }

        // Build action weights based on bot's actual roles and personality
        var actionWeights = new List<(ActionType Action, Role? ClaimedRole, string? Target, int Weight)>();

        // INCOME - Always available (low priority for medium AI)
        actionWeights.Add((ActionType.Income, null, null, 5));

        // FOREIGN AID - Always available (good value)
        actionWeights.Add((ActionType.ForeignAid, null, null, 20));

        // TAX - Prefer if we have Duke, or bluff based on personality
        if (ctx.BotRoles.Contains(Role.Duke))
        {
            actionWeights.Add((ActionType.Tax, Role.Duke, null, 50)); // High weight - we have it!
        }
        else if (ctx.Personality == BotPersonality.Bluffer)
        {
            actionWeights.Add((ActionType.Tax, Role.Duke, null, 25)); // Bluff sometimes
        }
        else if (ctx.Personality == BotPersonality.Aggressive)
        {
            actionWeights.Add((ActionType.Tax, Role.Duke, null, 15)); // Risky bluff
        }

        // ASSASSINATE - If we have coins and Assassin role (or bluff)
        if (bot.Coins >= 3)
        {
            if (ctx.BotRoles.Contains(Role.Assassin))
            {
                var target = ChooseTargetByPersonality(opponents, ctx.Personality);
                actionWeights.Add((ActionType.Assassinate, Role.Assassin, target.ConnectionId, 45));
            }
            else if (ctx.Personality == BotPersonality.Aggressive || ctx.Personality == BotPersonality.Bluffer)
            {
                var target = ChooseTargetByPersonality(opponents, ctx.Personality);
                actionWeights.Add((ActionType.Assassinate, Role.Assassin, target.ConnectionId, 15)); // Bluff assassinate
            }
        }

        // STEAL - If we have Captain (or bluff)
        var richOpponents = opponents.Where(p => p.Coins > 0).ToList();
        if (richOpponents.Any())
        {
            if (ctx.BotRoles.Contains(Role.Captain))
            {
                var target = richOpponents.OrderByDescending(p => p.Coins).First();
                actionWeights.Add((ActionType.Steal, Role.Captain, target.ConnectionId, 40));
            }
            else if (ctx.Personality == BotPersonality.Bluffer || ctx.Personality == BotPersonality.Aggressive)
            {
                var target = richOpponents.OrderByDescending(p => p.Coins).First();
                actionWeights.Add((ActionType.Steal, Role.Captain, target.ConnectionId, 10)); // Bluff steal
            }
        }

        // EXCHANGE - If we have Ambassador (or rarely bluff)
        if (ctx.BotRoles.Contains(Role.Ambassador))
        {
            actionWeights.Add((ActionType.Exchange, Role.Ambassador, null, 35));
        }
        else if (ctx.Personality == BotPersonality.Bluffer && Random.Shared.Next(100) < 20)
        {
            actionWeights.Add((ActionType.Exchange, Role.Ambassador, null, 10)); // Rare bluff
        }

        // Apply personality modifiers
        actionWeights = ApplyPersonalityModifiers(actionWeights, ctx.Personality);

        // Choose weighted random action
        var totalWeight = actionWeights.Sum(a => a.Weight);
        var randomValue = Random.Shared.Next(totalWeight);
        var cumulative = 0;

        foreach (var (action, claimedRole, target, weight) in actionWeights)
        {
            cumulative += weight;
            if (randomValue < cumulative)
            {
                return (action, target);
            }
        }

        // Fallback to income
        return (ActionType.Income, null);
    }

    public bool ShouldChallenge(BotDecisionContext ctx)
    {
        if (ctx.Pending == null) return false;

        var claimedRole = ctx.Pending.ClaimedRole;
        if (claimedRole == null) return false;

        // Check if we have evidence this might be a bluff
        // (We can't see opponent cards, so use heuristics)

        // If we have 2 of the claimed role, it's likely a bluff
        var weHaveCount = ctx.BotRoles.Count(r => r == claimedRole);
        if (weHaveCount == 2)
        {
            // High chance to challenge - they probably don't have it
            return Random.Shared.Next(100) < 80;
        }

        // Check visible cards (from previous reveals)
        var visibleCount = ctx.VisibleCards.Count(c => c.Role == claimedRole);
        if (weHaveCount + visibleCount >= 2)
        {
            // Good chance to challenge
            return Random.Shared.Next(100) < 60;
        }

        // Personality-based challenge rate
        return ctx.Personality switch
        {
            BotPersonality.Aggressive => Random.Shared.Next(100) < 35,  // 35% challenge
            BotPersonality.Bluffer => Random.Shared.Next(100) < 30,     // 30% challenge
            BotPersonality.Balanced => Random.Shared.Next(100) < 20,    // 20% challenge
            BotPersonality.Defensive => Random.Shared.Next(100) < 10,   // 10% challenge
            BotPersonality.Honest => Random.Shared.Next(100) < 15,      // 15% challenge
            _ => Random.Shared.Next(100) < 20
        };
    }

    public (bool ShouldBlock, Role? BlockRole) ShouldBlock(BotDecisionContext ctx)
    {
        if (ctx.Pending == null) return (false, null);

        var action = ctx.Pending.Action;
        var botId = ctx.BotPlayer.ConnectionId;

        // BLOCK FOREIGN AID with Duke
        if (action == ActionType.ForeignAid && ctx.BotRoles.Contains(Role.Duke))
        {
            // Block if we have Duke (honest play)
            var blockChance = ctx.Personality switch
            {
                BotPersonality.Defensive => 80,  // Block often
                BotPersonality.Balanced => 60,   // Block frequently
                BotPersonality.Honest => 90,     // Almost always block
                _ => 50
            };
            if (Random.Shared.Next(100) < blockChance)
            {
                return (true, Role.Duke);
            }
        }
        else if (action == ActionType.ForeignAid && ctx.Personality == BotPersonality.Bluffer)
        {
            // Bluff Duke block occasionally
            if (Random.Shared.Next(100) < 25)
            {
                return (true, Role.Duke);
            }
        }

        // BLOCK ASSASSINATE with Contessa (if we're the target)
        if (action == ActionType.Assassinate && ctx.Pending.TargetConnectionId == botId)
        {
            if (ctx.BotRoles.Contains(Role.Contessa))
            {
                // Always block if we have Contessa and we're targeted
                return (true, Role.Contessa);
            }
            else if (ctx.Personality == BotPersonality.Bluffer || ctx.Personality == BotPersonality.Defensive)
            {
                // Bluff Contessa to survive
                var bluffChance = ctx.Personality == BotPersonality.Defensive ? 70 : 50;
                if (Random.Shared.Next(100) < bluffChance)
                {
                    return (true, Role.Contessa);
                }
            }
        }

        // BLOCK STEAL with Captain or Ambassador (if we're the target)
        if (action == ActionType.Steal && ctx.Pending.TargetConnectionId == botId)
        {
            if (ctx.BotRoles.Contains(Role.Captain))
            {
                return (true, Role.Captain); // Block with Captain
            }
            if (ctx.BotRoles.Contains(Role.Ambassador))
            {
                return (true, Role.Ambassador); // Block with Ambassador
            }

            // Bluff block if defensive/bluffer
            if (ctx.Personality == BotPersonality.Bluffer || ctx.Personality == BotPersonality.Defensive)
            {
                var bluffChance = 40;
                if (Random.Shared.Next(100) < bluffChance)
                {
                    // Choose random block role
                    var blockRole = Random.Shared.Next(2) == 0 ? Role.Captain : Role.Ambassador;
                    return (true, blockRole);
                }
            }
        }

        return (false, null);
    }

    public bool ShouldChallengeBlock(BotDecisionContext ctx)
    {
        // Similar logic to ShouldChallenge but slightly lower rates
        if (ctx.Pending?.BlockClaimedRole == null) return false;

        var claimedRole = ctx.Pending.BlockClaimedRole.Value;

        // Check if we have evidence
        var weHaveCount = ctx.BotRoles.Count(r => r == claimedRole);
        if (weHaveCount == 2) return Random.Shared.Next(100) < 70; // High chance

        var visibleCount = ctx.VisibleCards.Count(c => c.Role == claimedRole);
        if (weHaveCount + visibleCount >= 2) return Random.Shared.Next(100) < 50;

        // Personality-based
        return ctx.Personality switch
        {
            BotPersonality.Aggressive => Random.Shared.Next(100) < 25,
            BotPersonality.Bluffer => Random.Shared.Next(100) < 20,
            BotPersonality.Balanced => Random.Shared.Next(100) < 15,
            _ => Random.Shared.Next(100) < 10
        };
    }

    public Role ChooseCardToLose(BotDecisionContext ctx)
    {
        var roles = ctx.BotRoles.ToList();
        if (roles.Count == 0) return Role.Duke; // Shouldn't happen

        if (roles.Count == 1) return roles[0];

        // Smart card loss - keep useful roles
        // Priority: Contessa (survival) > Duke (income) > Captain/Ambassador (utility) > Assassin

        // If we have Contessa, try to keep it
        if (roles.Contains(Role.Contessa) && roles.Count > 1)
        {
            var other = roles.FirstOrDefault(r => r != Role.Contessa);
            if (other != default) return other;
        }

        // If we have Duke, try to keep it for income
        if (roles.Contains(Role.Duke) && roles.Count > 1)
        {
            var nonDuke = roles.Where(r => r != Role.Duke).ToList();
            if (nonDuke.Contains(Role.Assassin)) return Role.Assassin; // Lose Assassin before Duke
        }

        // Random from available
        return roles[Random.Shared.Next(roles.Count)];
    }

    public List<Role> ChooseExchangeCards(BotDecisionContext ctx)
    {
        var availableCards = ctx.Pending?.ExchangeAvailableCards ?? new List<Role>();
        var cardsToKeep = ctx.Pending?.ExchangeCardsToKeep ?? 2;

        if (availableCards.Count <= cardsToKeep)
        {
            return availableCards.ToList();
        }

        // Smart exchange - prioritize useful roles
        var priorityOrder = new List<Role>
        {
            Role.Contessa,    // Best for survival
            Role.Duke,        // Best for income
            Role.Ambassador,  // Good utility
            Role.Captain,     // Good utility
            Role.Assassin     // Situational
        };

        var chosen = new List<Role>();

        // Pick cards by priority
        foreach (var role in priorityOrder)
        {
            var count = availableCards.Count(c => c == role);
            var alreadyChosen = chosen.Count(c => c == role);

            while (count > alreadyChosen && chosen.Count < cardsToKeep)
            {
                chosen.Add(role);
                alreadyChosen++;
            }

            if (chosen.Count >= cardsToKeep) break;
        }

        // If still need more, take remaining
        while (chosen.Count < cardsToKeep)
        {
            var remaining = availableCards.Except(chosen).FirstOrDefault();
            if (remaining != default)
            {
                chosen.Add(remaining);
            }
            else break;
        }

        return chosen;
    }

    // ==================== HELPER METHODS ====================

    private PlayerState ChooseTargetByPersonality(List<PlayerState> opponents, BotPersonality personality)
    {
        if (!opponents.Any()) return opponents.First();

        return personality switch
        {
            BotPersonality.Aggressive => opponents.OrderByDescending(p => p.Coins).First(), // Target richest
            BotPersonality.Defensive => opponents.OrderBy(p => p.InfluenceCount).First(),   // Target weakest
            BotPersonality.Balanced => opponents[Random.Shared.Next(opponents.Count)],      // Random
            _ => opponents[Random.Shared.Next(opponents.Count)]
        };
    }

    private List<(ActionType Action, Role? ClaimedRole, string? Target, int Weight)> ApplyPersonalityModifiers(
        List<(ActionType Action, Role? ClaimedRole, string? Target, int Weight)> weights,
        BotPersonality personality)
    {
        var modified = new List<(ActionType Action, Role? ClaimedRole, string? Target, int Weight)>();

        foreach (var (action, role, target, weight) in weights)
        {
            var newWeight = weight;

            switch (personality)
            {
                case BotPersonality.Aggressive:
                    if (action == ActionType.Assassinate || action == ActionType.Steal)
                        newWeight = (int)(weight * 1.5); // Boost aggressive actions
                    if (action == ActionType.Income)
                        newWeight = (int)(weight * 0.5); // Reduce passive actions
                    break;

                case BotPersonality.Defensive:
                    if (action == ActionType.Income || action == ActionType.ForeignAid)
                        newWeight = (int)(weight * 1.3); // Boost safe actions
                    if (action == ActionType.Assassinate)
                        newWeight = (int)(weight * 0.6); // Reduce aggressive actions
                    break;

                case BotPersonality.Bluffer:
                    // Already boosted in action selection
                    break;

                case BotPersonality.Honest:
                    // Only claim roles we have (already filtered above)
                    break;
            }

            modified.Add((action, role, target, newWeight));
        }

        return modified;
    }
}
