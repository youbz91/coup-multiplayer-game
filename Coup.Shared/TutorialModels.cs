using System.Collections.Generic;

namespace Coup.Shared;

/// <summary>
/// Tutorial step with instructions and interactive elements
/// </summary>
public class TutorialStep
{
    public int StepNumber { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string ActionRequired { get; set; } = ""; // What the player needs to do
    public string HighlightElement { get; set; } = ""; // CSS selector to highlight
    public TutorialStepType Type { get; set; }
    public List<string> Tips { get; set; } = new();
    public bool IsCompleted { get; set; }
}

/// <summary>
/// Type of tutorial step
/// </summary>
public enum TutorialStepType
{
    Introduction,      // Welcome message
    Explanation,       // Explain concept
    Action,           // Player must perform action
    Choice,           // Player must make a decision
    Challenge,        // Explain challenge mechanic
    Block,            // Explain block mechanic
    Completion        // Tutorial complete
}

/// <summary>
/// Tutorial progress tracking
/// </summary>
public class TutorialProgress
{
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public bool IsActive { get; set; }
    public bool IsCompleted { get; set; }
    public HashSet<string> CompletedSteps { get; set; } = new();
    public Dictionary<string, int> ActionCounts { get; set; } = new(); // Track actions performed
}

/// <summary>
/// Tooltip data for UI elements
/// </summary>
public class TooltipData
{
    public string Element { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Example { get; set; } = "";
    public List<string> KeyPoints { get; set; } = new();
}

/// <summary>
/// Achievement definition
/// </summary>
public class Achievement
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "🏆";
    public AchievementCategory Category { get; set; }
    public int ProgressCurrent { get; set; }
    public int ProgressRequired { get; set; }
    public bool IsUnlocked { get; set; }
    public DateTime? UnlockedDate { get; set; }
}

public enum AchievementCategory
{
    Beginner,
    Strategic,
    Aggressive,
    Social,
    Master
}

/// <summary>
/// Player statistics
/// </summary>
public class PlayerStatistics
{
    public int GamesPlayed { get; set; }
    public int GamesWon { get; set; }
    public int GamesLost { get; set; }
    public double WinRate => GamesPlayed > 0 ? (double)GamesWon / GamesPlayed * 100 : 0;

    public int SuccessfulChallenges { get; set; }
    public int FailedChallenges { get; set; }
    public int TotalChallenges => SuccessfulChallenges + FailedChallenges;
    public double ChallengeSuccessRate => TotalChallenges > 0 ? (double)SuccessfulChallenges / TotalChallenges * 100 : 0;

    public int SuccessfulBluffs { get; set; }
    public int CaughtBluffs { get; set; }
    public int TotalBluffs => SuccessfulBluffs + CaughtBluffs;
    public double BluffSuccessRate => TotalBluffs > 0 ? (double)SuccessfulBluffs / TotalBluffs * 100 : 0;

    public Dictionary<Role, int> MostUsedRoles { get; set; } = new();
    public Dictionary<ActionType, int> MostUsedActions { get; set; } = new();

    public int TotalCoinsEarned { get; set; }
    public int TotalInfluenceLost { get; set; }
    public int TotalCoupsPerformed { get; set; }
    public int TotalAssassinations { get; set; }

    public TimeSpan TotalPlayTime { get; set; }
    public TimeSpan AverageGameDuration => GamesPlayed > 0 ? TimeSpan.FromTicks(TotalPlayTime.Ticks / GamesPlayed) : TimeSpan.Zero;

    public List<Achievement> Achievements { get; set; } = new();
}

/// <summary>
/// Game match history entry
/// </summary>
public class GameHistoryEntry
{
    public string GameId { get; set; } = "";
    public DateTime GameDate { get; set; }
    public TimeSpan GameDuration { get; set; }
    public bool PlayerWon { get; set; }
    public string PlayerName { get; set; } = "";
    public int FinalCoins { get; set; }
    public int FinalInfluence { get; set; }
    public List<string> OpponentNames { get; set; } = new();
    public string WinnerName { get; set; } = "";
    public int TotalTurns { get; set; }
    public Dictionary<string, object> KeyMoments { get; set; } = new(); // Special events
}
