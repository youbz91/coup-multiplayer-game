namespace Coup.Shared;

public enum BotDifficulty
{
    Easy,
    Medium,
    Hard
}

public enum BotPersonality
{
    Balanced,
    Aggressive,
    Defensive,
    Bluffer,
    Honest
}

public class BotConfig
{
    public BotDifficulty Difficulty { get; set; }
    public BotPersonality Personality { get; set; }

    /// <summary>
    /// Gets the delay in milliseconds for bot actions to simulate human thinking time.
    /// Easy bots are impulsive (200-500ms), Medium bots think moderately (500-1200ms),
    /// Hard bots are calculating (800-2000ms).
    /// </summary>
    public int GetActionDelayMs()
    {
        return Difficulty switch
        {
            BotDifficulty.Easy => Random.Shared.Next(200, 500),
            BotDifficulty.Medium => Random.Shared.Next(500, 1200),
            BotDifficulty.Hard => Random.Shared.Next(800, 2000),
            _ => 1000
        };
    }

    /// <summary>
    /// Gets personality-based modifiers for challenge rate (0.0-1.0 multiplier).
    /// </summary>
    public double GetChallengeRateModifier()
    {
        return Personality switch
        {
            BotPersonality.Aggressive => 1.5,    // +50% challenge rate
            BotPersonality.Defensive => 0.5,     // -50% challenge rate
            BotPersonality.Balanced => 1.0,      // No modifier
            BotPersonality.Bluffer => 0.8,       // -20% (save challenges for own bluffs)
            BotPersonality.Honest => 0.7,        // -30% (trusting)
            _ => 1.0
        };
    }

    /// <summary>
    /// Gets personality-based modifiers for block rate (0.0-1.0 multiplier).
    /// </summary>
    public double GetBlockRateModifier()
    {
        return Personality switch
        {
            BotPersonality.Aggressive => 0.9,    // -10% (aggressive, takes hits)
            BotPersonality.Defensive => 1.4,     // +40% block rate
            BotPersonality.Balanced => 1.0,      // No modifier
            BotPersonality.Bluffer => 1.2,       // +20% (blocks often, some bluffs)
            BotPersonality.Honest => 1.1,        // +10% (blocks with real roles)
            _ => 1.0
        };
    }

    /// <summary>
    /// Gets personality-based bluff rate (probability of bluffing when don't have role).
    /// </summary>
    public double GetBluffRate()
    {
        return Personality switch
        {
            BotPersonality.Aggressive => 0.3,    // 30% bluff rate
            BotPersonality.Defensive => 0.1,     // 10% bluff rate (cautious)
            BotPersonality.Balanced => 0.2,      // 20% bluff rate
            BotPersonality.Bluffer => 0.5,       // 50% bluff rate!
            BotPersonality.Honest => 0.05,       // 5% bluff rate (rarely)
            _ => 0.2
        };
    }
}
