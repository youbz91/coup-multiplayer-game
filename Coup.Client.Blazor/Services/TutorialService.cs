using Coup.Shared;
using Microsoft.JSInterop;

namespace Coup.Client.Blazor.Services;

/// <summary>
/// Manages tutorial state and provides step-by-step guidance
/// </summary>
public class TutorialService
{
    private readonly IJSRuntime _jsRuntime;
    private TutorialProgress _progress;
    private List<TutorialStep> _tutorialSteps;

    public event Action? OnTutorialStateChanged;

    public TutorialService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
        _progress = new TutorialProgress();
        _tutorialSteps = InitializeTutorialSteps();
    }

    public TutorialProgress Progress => _progress;
    public bool IsActive => _progress.IsActive;
    public TutorialStep? CurrentStep => _progress.CurrentStep < _tutorialSteps.Count
        ? _tutorialSteps[_progress.CurrentStep]
        : null;

    /// <summary>
    /// Start the tutorial from the beginning
    /// </summary>
    public void StartTutorial()
    {
        _progress = new TutorialProgress
        {
            CurrentStep = 0,
            TotalSteps = _tutorialSteps.Count,
            IsActive = true,
            IsCompleted = false
        };
        OnTutorialStateChanged?.Invoke();
    }

    /// <summary>
    /// Advance to the next tutorial step
    /// </summary>
    public void NextStep()
    {
        if (!_progress.IsActive) return;

        if (CurrentStep != null)
        {
            CurrentStep.IsCompleted = true;
            _progress.CompletedSteps.Add(CurrentStep.StepNumber.ToString());
        }

        _progress.CurrentStep++;

        if (_progress.CurrentStep >= _progress.TotalSteps)
        {
            CompleteTutorial();
        }
        else
        {
            OnTutorialStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Go back to previous step
    /// </summary>
    public void PreviousStep()
    {
        if (!_progress.IsActive || _progress.CurrentStep <= 0) return;

        _progress.CurrentStep--;
        OnTutorialStateChanged?.Invoke();
    }

    /// <summary>
    /// Skip the tutorial
    /// </summary>
    public void SkipTutorial()
    {
        _progress.IsActive = false;
        _progress.IsCompleted = true;
        OnTutorialStateChanged?.Invoke();
    }

    /// <summary>
    /// Complete the tutorial
    /// </summary>
    private void CompleteTutorial()
    {
        _progress.IsActive = false;
        _progress.IsCompleted = true;
        _progress.CurrentStep = _progress.TotalSteps;
        OnTutorialStateChanged?.Invoke();
    }

    /// <summary>
    /// Track an action performed by the player
    /// </summary>
    public void TrackAction(string actionName)
    {
        if (!_progress.ActionCounts.ContainsKey(actionName))
        {
            _progress.ActionCounts[actionName] = 0;
        }
        _progress.ActionCounts[actionName]++;
    }

    /// <summary>
    /// Check if player has completed a specific action
    /// </summary>
    public bool HasCompletedAction(string actionName, int requiredCount = 1)
    {
        return _progress.ActionCounts.GetValueOrDefault(actionName, 0) >= requiredCount;
    }

    /// <summary>
    /// Initialize all tutorial steps
    /// </summary>
    private List<TutorialStep> InitializeTutorialSteps()
    {
        return new List<TutorialStep>
        {
            // Step 1: Introduction
            new TutorialStep
            {
                StepNumber = 1,
                Title = "Welcome to Coup!",
                Description = "Coup is a game of bluffing, deduction, and manipulation. Your goal is to be the last player standing by eliminating your opponents' influence.",
                ActionRequired = "Click 'Next' to continue",
                Type = TutorialStepType.Introduction,
                Tips = new List<string>
                {
                    "You start with 2 coins and 2 hidden influence cards (roles)",
                    "Each role has unique actions you can claim",
                    "You can bluff about which roles you have!"
                }
            },

            // Step 2: Understanding Influence
            new TutorialStep
            {
                StepNumber = 2,
                Title = "Understanding Influence",
                Description = "Your influence is represented by your role cards. You have 2 influences at the start. When you lose all influence, you're eliminated!",
                ActionRequired = "Click 'Next' to learn about roles",
                HighlightElement = ".player-card",
                Type = TutorialStepType.Explanation,
                Tips = new List<string>
                {
                    "Your role cards are kept secret from opponents",
                    "Losing a challenge or being Coup'd costs you influence",
                    "When you lose influence, you must reveal one of your cards"
                }
            },

            // Step 3: The Five Roles
            new TutorialStep
            {
                StepNumber = 3,
                Title = "The Five Roles",
                Description = "There are 5 roles in Coup, each with unique abilities:\n\n" +
                             "• Duke (Tax): Take 3 coins\n" +
                             "• Assassin: Pay 3 coins to assassinate a player\n" +
                             "• Captain (Steal): Steal 2 coins from another player\n" +
                             "• Ambassador (Exchange): Exchange cards with the deck\n" +
                             "• Contessa: Block assassination attempts",
                ActionRequired = "Click 'Next' to learn about basic actions",
                Type = TutorialStepType.Explanation,
                Tips = new List<string>
                {
                    "There are 3 copies of each role in the deck",
                    "You can claim to have any role, even if you don't!",
                    "Other players can challenge your claims"
                }
            },

            // Step 4: Basic Actions
            new TutorialStep
            {
                StepNumber = 4,
                Title = "Basic Actions (Always Available)",
                Description = "These actions don't require a specific role and cannot be challenged:\n\n" +
                             "• Income: Take 1 coin\n" +
                             "• Foreign Aid: Take 2 coins (can be blocked by Duke)\n" +
                             "• Coup: Pay 7 coins to eliminate an opponent's influence (cannot be blocked or challenged)",
                ActionRequired = "Remember these actions - they're crucial!",
                HighlightElement = ".action-buttons",
                Type = TutorialStepType.Explanation,
                Tips = new List<string>
                {
                    "Income is safe but slow",
                    "Foreign Aid is good value but risky",
                    "Coup is mandatory when you have 10+ coins"
                }
            },

            // Step 5: Role Actions
            new TutorialStep
            {
                StepNumber = 5,
                Title = "Role Actions (Can Be Challenged)",
                Description = "These actions require you to claim a specific role:\n\n" +
                             "• Tax (Duke): Take 3 coins\n" +
                             "• Assassinate (Assassin): Pay 3 coins to eliminate influence\n" +
                             "• Steal (Captain): Take up to 2 coins from a player\n" +
                             "• Exchange (Ambassador): Swap your cards with the deck",
                ActionRequired = "Click 'Next' to learn about challenges",
                Type = TutorialStepType.Explanation,
                Tips = new List<string>
                {
                    "You can claim these even if you don't have the role",
                    "If challenged and you lied, you lose influence",
                    "If challenged and you have the role, the challenger loses influence"
                }
            },

            // Step 6: Challenges
            new TutorialStep
            {
                StepNumber = 6,
                Title = "Challenges - Calling Out Bluffs",
                Description = "When someone claims a role action, you can challenge them if you think they're bluffing!\n\n" +
                             "If you're right → They lose influence\n" +
                             "If you're wrong → You lose influence",
                ActionRequired = "Click 'Next' to learn about blocking",
                Type = TutorialStepType.Challenge,
                HighlightElement = ".challenge-button",
                Tips = new List<string>
                {
                    "Challenge when you have evidence (you have 2 of their claimed role)",
                    "Challenging is risky but can catch bluffers",
                    "Failed challenges cost you dearly"
                }
            },

            // Step 7: Blocking
            new TutorialStep
            {
                StepNumber = 7,
                Title = "Blocking - Defensive Play",
                Description = "Some actions can be blocked by claiming specific roles:\n\n" +
                             "• Foreign Aid → Block with Duke\n" +
                             "• Assassination → Block with Contessa (if you're the target)\n" +
                             "• Steal → Block with Captain or Ambassador (if you're the target)",
                ActionRequired = "Click 'Next' to learn about winning",
                Type = TutorialStepType.Block,
                Tips = new List<string>
                {
                    "Blocks can also be challenged!",
                    "You can bluff blocks too",
                    "Blocking is key to surviving assassination attempts"
                }
            },

            // Step 8: Winning the Game
            new TutorialStep
            {
                StepNumber = 8,
                Title = "Winning the Game",
                Description = "To win Coup, you must be the last player with influence remaining.\n\n" +
                             "Strategies:\n" +
                             "• Build up coins for Coups (7 coins each)\n" +
                             "• Bluff to get extra coins (Tax, Steal)\n" +
                             "• Challenge obvious lies\n" +
                             "• Block dangerous attacks",
                ActionRequired = "Click 'Start Playing!' to begin",
                Type = TutorialStepType.Completion,
                Tips = new List<string>
                {
                    "Remember: You can lie about your roles!",
                    "Track what cards have been revealed",
                    "At 10 coins, you MUST Coup",
                    "Good luck, and may the best bluffer win!"
                }
            }
        };
    }

    /// <summary>
    /// Get tooltip data for a specific element
    /// </summary>
    public TooltipData? GetTooltipData(string element)
    {
        return element switch
        {
            "income" => new TooltipData
            {
                Element = "income",
                Title = "Income",
                Description = "Take 1 coin from the treasury. Safe and always available.",
                Example = "Use when you need coins but don't want to risk a bluff.",
                KeyPoints = new List<string> { "Always available", "Cannot be blocked", "Cannot be challenged" }
            },
            "foreign-aid" => new TooltipData
            {
                Element = "foreign-aid",
                Title = "Foreign Aid",
                Description = "Take 2 coins from the treasury. Can be blocked by Duke.",
                Example = "Good value but risky if opponents have Duke.",
                KeyPoints = new List<string> { "Always available", "Can be blocked by Duke", "Cannot be challenged" }
            },
            "coup" => new TooltipData
            {
                Element = "coup",
                Title = "Coup",
                Description = "Pay 7 coins to eliminate an opponent's influence. Mandatory at 10+ coins.",
                Example = "Use to eliminate threatening players.",
                KeyPoints = new List<string> { "Costs 7 coins", "Cannot be blocked", "Cannot be challenged", "Mandatory at 10 coins" }
            },
            "tax" => new TooltipData
            {
                Element = "tax",
                Title = "Tax (Duke)",
                Description = "Claim Duke to take 3 coins from the treasury.",
                Example = "Great for building coins quickly. Can be challenged.",
                KeyPoints = new List<string> { "Requires Duke claim", "Can be challenged", "High value action" }
            },
            "assassinate" => new TooltipData
            {
                Element = "assassinate",
                Title = "Assassinate (Assassin)",
                Description = "Claim Assassin and pay 3 coins to eliminate target's influence.",
                Example = "Powerful elimination tool. Can be blocked by Contessa.",
                KeyPoints = new List<string> { "Requires Assassin claim", "Costs 3 coins", "Can be challenged", "Can be blocked by Contessa" }
            },
            "steal" => new TooltipData
            {
                Element = "steal",
                Title = "Steal (Captain)",
                Description = "Claim Captain to steal up to 2 coins from another player.",
                Example = "Take coins from rich opponents.",
                KeyPoints = new List<string> { "Requires Captain claim", "Steals up to 2 coins", "Can be challenged", "Can be blocked by Captain/Ambassador" }
            },
            "exchange" => new TooltipData
            {
                Element = "exchange",
                Title = "Exchange (Ambassador)",
                Description = "Claim Ambassador to draw 2 cards from deck, then return 2 cards.",
                Example = "Use to get better roles for your strategy.",
                KeyPoints = new List<string> { "Requires Ambassador claim", "Can be challenged", "Great for role optimization" }
            },
            _ => null
        };
    }
}
