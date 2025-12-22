namespace Coup.Shared;

/// <summary>
/// Game variant configuration for custom rules
/// </summary>
public class GameVariant
{
    // Starting conditions
    public int StartingCoins { get; set; } = 2;
    public int StartingInfluence { get; set; } = 2;

    // Timer settings
    public int ActionTimeoutSeconds { get; set; } = 30;

    // Game modes
    public GameMode Mode { get; set; } = GameMode.Standard;

    // Custom rules
    public bool AllowFastMode { get; set; } = false;  // Reduced timeouts
    public bool RichStart { get; set; } = false;       // Start with more coins
    public bool QuickDeath { get; set; } = false;      // Start with 1 influence

    public static GameVariant Standard => new GameVariant
    {
        StartingCoins = 2,
        StartingInfluence = 2,
        ActionTimeoutSeconds = 30,
        Mode = GameMode.Standard
    };

    public static GameVariant SpeedCoup => new GameVariant
    {
        StartingCoins = 2,
        StartingInfluence = 2,
        ActionTimeoutSeconds = 15,
        Mode = GameMode.Speed,
        AllowFastMode = true
    };

    public static GameVariant RichMode => new GameVariant
    {
        StartingCoins = 5,
        StartingInfluence = 2,
        ActionTimeoutSeconds = 30,
        Mode = GameMode.Rich,
        RichStart = true
    };

    public static GameVariant ChaosMode => new GameVariant
    {
        StartingCoins = 3,
        StartingInfluence = 1,
        ActionTimeoutSeconds = 20,
        Mode = GameMode.Chaos,
        QuickDeath = true
    };
}

public enum GameMode
{
    Standard,      // Normal Coup rules
    Speed,         // 15-second timer
    Rich,          // Start with 5 coins
    Chaos,         // 1 influence, 3 coins, 20s timer
    Custom         // Custom configuration
}
