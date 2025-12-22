using Coup.Shared;

namespace Coup.Server.AI;

/// <summary>
/// Hard difficulty bot strategy - Advanced card counting, probability calculations, and adaptive behavior
/// </summary>
public class HardBotStrategy : IBotStrategy
{
    private const int TOTAL_CARDS_PER_ROLE = 3;

    public (ActionType Action, string? Target) DecideAction(BotDecisionContext ctx)
    {
        var bot = ctx.BotPlayer;
        var opponents = ctx.Game.Players.Where(p => p.IsAlive && p.ConnectionId != bot.ConnectionId).ToList();

        // MANDATORY COUP at 10+ coins
        if (ctx.MustCoup)
        {
            var target = ChooseStrategicTarget(opponents, ctx);
            return (ActionType.Coup, target.ConnectionId);
        }

        // Strategic COUP at 7+ coins based on game state
        if (bot.Coins >= 7)
        {
            var shouldCoup = ShouldStrategicCoup(ctx, opponents);
            if (shouldCoup)
            {
                var target = ChooseStrategicTarget(opponents, ctx);
                return (ActionType.Coup, target.ConnectionId);
            }
        }

        // Calculate card probabilities for strategic decisions
        var cardProbabilities = CalculateCardProbabilities(ctx);

        // Build weighted actions based on strategic analysis
        var actionScores = new Dictionary<(ActionType, Role?, string?), double>();

        // INCOME - Low priority but always available
        actionScores[(ActionType.Income, null, null)] = 5.0;

        // FOREIGN AID - Good value, but risky if Duke is likely
        var dukeBluffRisk = EstimateBluffRisk(Role.Duke, cardProbabilities, ctx);
        actionScores[(ActionType.ForeignAid, null, null)] = 25.0 * (1.0 - dukeBluffRisk);

        // TAX - Strategic if we have Duke or low Duke probability
        if (ctx.BotRoles.Contains(Role.Duke))
        {
            actionScores[(ActionType.Tax, Role.Duke, null)] = 60.0; // High value, we have it
        }
        else
        {
            var bluffSafety = CalculateBluffSafety(Role.Duke, cardProbabilities, ctx);
            if (bluffSafety > 0.4 && (ctx.Personality == BotPersonality.Bluffer || ctx.Personality == BotPersonality.Aggressive))
            {
                actionScores[(ActionType.Tax, Role.Duke, null)] = 30.0 * bluffSafety;
            }
        }

        // ASSASSINATE - High-value action if we have role or can bluff safely
        if (bot.Coins >= 3)
        {
            var target = ChooseStrategicTarget(opponents, ctx);
            if (ctx.BotRoles.Contains(Role.Assassin))
            {
                // Calculate assassination value based on target threat
                var assassinationValue = CalculateTargetThreat(target, ctx) * 50.0;
                actionScores[(ActionType.Assassinate, Role.Assassin, target.ConnectionId)] = assassinationValue;
            }
            else
            {
                var bluffSafety = CalculateBluffSafety(Role.Assassin, cardProbabilities, ctx);
                if (bluffSafety > 0.5 && ctx.Personality != BotPersonality.Honest)
                {
                    var assassinationValue = CalculateTargetThreat(target, ctx) * 25.0 * bluffSafety;
                    actionScores[(ActionType.Assassinate, Role.Assassin, target.ConnectionId)] = assassinationValue;
                }
            }
        }

        // STEAL - Target richest opponent
        var richOpponents = opponents.Where(p => p.Coins > 0).ToList();
        if (richOpponents.Any())
        {
            var stealTarget = richOpponents.OrderByDescending(p => p.Coins).First();
            var stealValue = Math.Min(stealTarget.Coins, 2); // Max 2 coins

            if (ctx.BotRoles.Contains(Role.Captain))
            {
                actionScores[(ActionType.Steal, Role.Captain, stealTarget.ConnectionId)] = stealValue * 15.0;
            }
            else
            {
                var bluffSafety = CalculateBluffSafety(Role.Captain, cardProbabilities, ctx);
                if (bluffSafety > 0.4 && ctx.Personality != BotPersonality.Honest)
                {
                    actionScores[(ActionType.Steal, Role.Captain, stealTarget.ConnectionId)] = stealValue * 8.0 * bluffSafety;
                }
            }
        }

        // EXCHANGE - Strategic card replacement
        if (ctx.BotRoles.Contains(Role.Ambassador))
        {
            var exchangeValue = CalculateExchangeValue(ctx);
            actionScores[(ActionType.Exchange, Role.Ambassador, null)] = exchangeValue;
        }
        else
        {
            var bluffSafety = CalculateBluffSafety(Role.Ambassador, cardProbabilities, ctx);
            if (bluffSafety > 0.6 && ctx.Personality == BotPersonality.Bluffer)
            {
                actionScores[(ActionType.Exchange, Role.Ambassador, null)] = 15.0 * bluffSafety;
            }
        }

        // Apply personality modifiers
        actionScores = ApplyPersonalityScores(actionScores, ctx.Personality);

        // Choose highest scoring action
        if (actionScores.Any())
        {
            var best = actionScores.OrderByDescending(kvp => kvp.Value).First();
            return (best.Key.Item1, best.Key.Item3);
        }

        return (ActionType.Income, null);
    }

    public bool ShouldChallenge(BotDecisionContext ctx)
    {
        if (ctx.Pending?.ClaimedRole == null) return false;

        var claimedRole = ctx.Pending.ClaimedRole.Value;
        var cardProbabilities = CalculateCardProbabilities(ctx);

        // Calculate probability opponent actually has the role
        var opponentHasProbability = cardProbabilities.GetValueOrDefault(claimedRole, 0.33);

        // If we have 2 of the role, opponent definitely doesn't have it
        if (ctx.BotRoles.Count(r => r == claimedRole) == 2)
        {
            return true; // 100% challenge - they're lying
        }

        // If we have 1 and saw 1+ in reveals, very likely lying
        var weHave = ctx.BotRoles.Count(r => r == claimedRole);
        var visible = ctx.VisibleCards.Count(c => c.Role == claimedRole);

        if (weHave + visible >= 2)
        {
            // Very high chance they're bluffing
            return Random.Shared.Next(100) < 90;
        }

        // Calculate expected value of challenging
        var challengeEV = CalculateChallengeExpectedValue(opponentHasProbability, ctx);

        // Personality modifies threshold
        var challengeThreshold = ctx.Personality switch
        {
            BotPersonality.Aggressive => -0.3,  // Challenge even at slight disadvantage
            BotPersonality.Bluffer => -0.2,      // Challenge when favorable
            BotPersonality.Balanced => 0.1,      // Challenge when clearly favorable
            BotPersonality.Defensive => 0.3,     // Only challenge when very confident
            BotPersonality.Honest => 0.2,        // Challenge when moderately confident
            _ => 0.1
        };

        return challengeEV > challengeThreshold;
    }

    public (bool ShouldBlock, Role? BlockRole) ShouldBlock(BotDecisionContext ctx)
    {
        if (ctx.Pending == null) return (false, null);

        var action = ctx.Pending.Action;
        var botId = ctx.BotPlayer.ConnectionId;

        // Calculate whether blocking is beneficial
        var cardProbabilities = CalculateCardProbabilities(ctx);

        // BLOCK FOREIGN AID with Duke
        if (action == ActionType.ForeignAid)
        {
            if (ctx.BotRoles.Contains(Role.Duke))
            {
                // Decide strategically whether to reveal Duke
                var blockChance = ctx.Personality switch
                {
                    BotPersonality.Defensive => 90,
                    BotPersonality.Balanced => 70,
                    BotPersonality.Honest => 95,
                    _ => 60
                };
                if (Random.Shared.Next(100) < blockChance)
                {
                    return (true, Role.Duke);
                }
            }
            else
            {
                // Consider bluffing Duke
                var bluffSafety = CalculateBluffSafety(Role.Duke, cardProbabilities, ctx);
                if (bluffSafety > 0.6 && ctx.Personality == BotPersonality.Bluffer)
                {
                    return (true, Role.Duke);
                }
            }
        }

        // BLOCK ASSASSINATE with Contessa (critical - we're dying!)
        if (action == ActionType.Assassinate && ctx.Pending.TargetConnectionId == botId)
        {
            if (ctx.BotRoles.Contains(Role.Contessa))
            {
                return (true, Role.Contessa); // Always block if we have it
            }
            else
            {
                // Bluff Contessa to survive (even risky)
                var bluffChance = ctx.Personality switch
                {
                    BotPersonality.Defensive => 85,  // High survival instinct
                    BotPersonality.Bluffer => 75,
                    BotPersonality.Balanced => 60,
                    BotPersonality.Aggressive => 50,
                    _ => 40
                };
                if (Random.Shared.Next(100) < bluffChance)
                {
                    return (true, Role.Contessa);
                }
            }
        }

        // BLOCK STEAL with Captain/Ambassador
        if (action == ActionType.Steal && ctx.Pending.TargetConnectionId == botId)
        {
            // Only block if we have significant coins to protect
            if (ctx.BotPlayer.Coins >= 3)
            {
                if (ctx.BotRoles.Contains(Role.Captain))
                {
                    return (true, Role.Captain);
                }
                if (ctx.BotRoles.Contains(Role.Ambassador))
                {
                    return (true, Role.Ambassador);
                }

                // Consider bluff if we have many coins
                if (ctx.BotPlayer.Coins >= 5 && ctx.Personality != BotPersonality.Honest)
                {
                    var bluffSafety = Math.Max(
                        CalculateBluffSafety(Role.Captain, cardProbabilities, ctx),
                        CalculateBluffSafety(Role.Ambassador, cardProbabilities, ctx)
                    );

                    if (bluffSafety > 0.5)
                    {
                        var blockRole = Random.Shared.Next(2) == 0 ? Role.Captain : Role.Ambassador;
                        return (true, blockRole);
                    }
                }
            }
        }

        return (false, null);
    }

    public bool ShouldChallengeBlock(BotDecisionContext ctx)
    {
        if (ctx.Pending?.BlockClaimedRole == null) return false;

        var claimedRole = ctx.Pending.BlockClaimedRole.Value;
        var cardProbabilities = CalculateCardProbabilities(ctx);

        // Similar logic to ShouldChallenge but for blocks
        var weHave = ctx.BotRoles.Count(r => r == claimedRole);
        var visible = ctx.VisibleCards.Count(c => c.Role == claimedRole);

        if (weHave == 2) return true; // They definitely don't have it

        if (weHave + visible >= 2)
        {
            return Random.Shared.Next(100) < 85; // Very likely bluff
        }

        var opponentHasProbability = cardProbabilities.GetValueOrDefault(claimedRole, 0.33);
        var challengeEV = CalculateChallengeExpectedValue(opponentHasProbability, ctx);

        var threshold = ctx.Personality switch
        {
            BotPersonality.Aggressive => -0.2,
            BotPersonality.Balanced => 0.15,
            _ => 0.25
        };

        return challengeEV > threshold;
    }

    public Role ChooseCardToLose(BotDecisionContext ctx)
    {
        var roles = ctx.BotRoles.ToList();
        if (roles.Count <= 1) return roles.FirstOrDefault();

        // Strategic card loss based on game state
        var cardValues = new Dictionary<Role, double>();

        foreach (var role in roles)
        {
            cardValues[role] = CalculateCardValue(role, ctx);
        }

        // Keep highest value card, lose lowest value
        var loseCard = cardValues.OrderBy(kvp => kvp.Value).First().Key;
        return loseCard;
    }

    public List<Role> ChooseExchangeCards(BotDecisionContext ctx)
    {
        var availableCards = ctx.Pending?.ExchangeAvailableCards ?? new List<Role>();
        var cardsToKeep = ctx.Pending?.ExchangeCardsToKeep ?? 2;

        if (availableCards.Count <= cardsToKeep)
        {
            return availableCards.ToList();
        }

        // Strategic exchange - keep highest value cards
        var cardValues = availableCards.Select(role => new
        {
            Role = role,
            Value = CalculateCardValue(role, ctx)
        }).OrderByDescending(c => c.Value);

        return cardValues.Take(cardsToKeep).Select(c => c.Role).ToList();
    }

    // ==================== ADVANCED HELPER METHODS ====================

    private Dictionary<Role, double> CalculateCardProbabilities(BotDecisionContext ctx)
    {
        var probabilities = new Dictionary<Role, double>();
        var allRoles = new[] { Role.Duke, Role.Assassin, Role.Captain, Role.Ambassador, Role.Contessa };

        foreach (var role in allRoles)
        {
            // Count cards: Total=3, We have X, Visible Y, Unknown = 3-X-Y
            var weHave = ctx.BotRoles.Count(r => r == role);
            var visible = ctx.VisibleCards.Count(c => c.Role == role);
            var unknown = TOTAL_CARDS_PER_ROLE - weHave - visible;

            // Total unknown cards in deck + opponents' hands
            var totalUnknown = 15 - ctx.VisibleCards.Count - ctx.BotRoles.Count;

            // Probability an opponent has this role
            probabilities[role] = totalUnknown > 0 ? (double)unknown / totalUnknown : 0;
        }

        return probabilities;
    }

    private double EstimateBluffRisk(Role role, Dictionary<Role, double> probabilities, BotDecisionContext ctx)
    {
        // Risk that someone will block/challenge because they have the role
        return probabilities.GetValueOrDefault(role, 0.33) * 0.5; // 50% chance they'll actually respond
    }

    private double CalculateBluffSafety(Role role, Dictionary<Role, double> probabilities, BotDecisionContext ctx)
    {
        // How safe is it to bluff this role?
        var opponentHasProbability = probabilities.GetValueOrDefault(role, 0.33);
        return 1.0 - opponentHasProbability;
    }

    private double CalculateChallengeExpectedValue(double opponentHasProbability, BotDecisionContext ctx)
    {
        // EV = P(success) * gain - P(failure) * loss
        // Success = they don't have it, they lose influence
        // Failure = they have it, we lose influence

        var successProbability = 1.0 - opponentHasProbability;
        var successValue = 1.0; // They lose influence
        var failureValue = -1.0; // We lose influence

        return successProbability * successValue + opponentHasProbability * failureValue;
    }

    private double CalculateCardValue(Role role, BotDecisionContext ctx)
    {
        // Calculate strategic value of keeping this card
        return role switch
        {
            Role.Contessa => 100.0, // Critical for survival against Assassin
            Role.Duke => 80.0 + (ctx.BotPlayer.Coins < 5 ? 20.0 : 0), // Great for income
            Role.Ambassador => 60.0, // Good for exchange flexibility
            Role.Captain => 50.0 + (ctx.Game.Players.Any(p => p.Coins > 3) ? 15.0 : 0), // Good if opponents have coins
            Role.Assassin => 40.0 + (ctx.BotPlayer.Coins >= 3 ? 20.0 : 0), // Good if we have coins
            _ => 50.0
        };
    }

    private double CalculateExchangeValue(BotDecisionContext ctx)
    {
        // Calculate how valuable an exchange would be based on current hand
        var currentHandValue = ctx.BotRoles.Sum(r => CalculateCardValue(r, ctx));
        var averageHandValue = 120.0; // Average value of 2 cards

        // More valuable if current hand is weak
        return Math.Max(0, averageHandValue - currentHandValue) * 0.5 + 20.0;
    }

    private bool ShouldStrategicCoup(BotDecisionContext ctx, List<PlayerState> opponents)
    {
        // Coup if an opponent is close to winning (7+ coins) or is a threat
        var dangerousOpponent = opponents.FirstOrDefault(p => p.Coins >= 7);
        if (dangerousOpponent != null) return true;

        // Coup based on personality
        var coupChance = ctx.Personality switch
        {
            BotPersonality.Aggressive => 60,
            BotPersonality.Balanced => 35,
            _ => 20
        };

        return Random.Shared.Next(100) < coupChance;
    }

    private PlayerState ChooseStrategicTarget(List<PlayerState> opponents, BotDecisionContext ctx)
    {
        if (!opponents.Any()) return opponents.First();

        // Calculate threat score for each opponent
        var targetScores = opponents.Select(p => new
        {
            Player = p,
            Threat = CalculateTargetThreat(p, ctx)
        }).OrderByDescending(t => t.Threat);

        return targetScores.First().Player;
    }

    private double CalculateTargetThreat(PlayerState opponent, BotDecisionContext ctx)
    {
        var threat = 0.0;

        // High coins = high threat (can Coup)
        threat += opponent.Coins * 5.0;

        // Multiple influences = more resilient
        threat += opponent.InfluenceCount * 20.0;

        // Personality modifier
        if (ctx.Personality == BotPersonality.Aggressive)
        {
            threat += opponent.Coins * 2.0; // Prioritize rich players
        }
        else if (ctx.Personality == BotPersonality.Defensive)
        {
            threat += opponent.InfluenceCount * 10.0; // Prioritize weak players
        }

        return threat;
    }

    private Dictionary<(ActionType, Role?, string?), double> ApplyPersonalityScores(
        Dictionary<(ActionType, Role?, string?), double> scores,
        BotPersonality personality)
    {
        var modified = new Dictionary<(ActionType, Role?, string?), double>();

        foreach (var kvp in scores)
        {
            var (action, role, target) = kvp.Key;
            var score = kvp.Value;

            switch (personality)
            {
                case BotPersonality.Aggressive:
                    if (action == ActionType.Assassinate || action == ActionType.Steal || action == ActionType.Coup)
                        score *= 1.5;
                    if (action == ActionType.Income)
                        score *= 0.4;
                    break;

                case BotPersonality.Defensive:
                    if (action == ActionType.Income || action == ActionType.ForeignAid || action == ActionType.Tax)
                        score *= 1.4;
                    if (action == ActionType.Assassinate)
                        score *= 0.5;
                    break;

                case BotPersonality.Bluffer:
                    // Bluffer already gets bonuses in bluff safety calculations
                    break;

                case BotPersonality.Honest:
                    // Honest bots have limited actions (only claim what they have)
                    break;
            }

            modified[kvp.Key] = score;
        }

        return modified;
    }
}
