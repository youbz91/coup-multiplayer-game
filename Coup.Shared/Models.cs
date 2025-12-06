using System;
using System.Collections.Generic;
using System.Linq;

namespace Coup.Shared
{
    public enum Role { Duke, Assassin, Captain, Ambassador, Contessa }
    public enum ActionType { Income, ForeignAid, Coup, Tax, Assassinate, Steal, Exchange }
    public enum PendingPhase { ActionClaim, BlockClaim, ExchangeCardSelection }
    public class PlayerState
    {
        public string ConnectionId { get; set; } = "";
        public string Name { get; set; } = "";
        public int Coins { get; set; } = 2;
        public int InfluenceCount { get; set; } = 2; // caché: serveurs garde les vraies cartes
        public bool IsBot { get; set; }
        public bool IsConnected { get; set; } = true;
        public DateTime? DisconnectedTime { get; set; } = null;
        public bool IsAlive => InfluenceCount > 0;
    }

    public class GameState
    {
        public string GameId { get; set; } = Guid.NewGuid().ToString("N");
        public List<PlayerState> Players { get; set; } = new();
        public int CurrentPlayerIndex { get; set; } = 0;
        public List<string> Log { get; set; } = new();

        public PendingAction? Pending { get; set; }
        public PendingInfluenceLoss? PendingInfluenceLoss { get; set; }

        // NEW
        public string HostConnectionId { get; set; } = ""; // premier joueur à JoinLobby
        public bool GameStarted { get; set; } = false;
        public bool GameEnded { get; set; } = false;
        public string WinnerName { get; set; } = "";
        public DateTime? GameStartTime { get; set; } = null;
        public DateTime? PendingStartTime { get; set; } = null; // Pour le timeout
        public int TurnCount { get; set; } = 0;

        public PlayerState? CurrentPlayer =>
            (Players.Count == 0) ? null : Players.ElementAtOrDefault(CurrentPlayerIndex);

        public int AliveCount => Players.Count(p => p.IsAlive);
    }


    public class ActionDto
    {
        public ActionType Action { get; set; }
        public string? TargetConnectionId { get; set; }
        public Role? ClaimedRole { get; set; }
    }

        public class PendingAction
    {
        public ActionType Action { get; set; }
        public string ActorConnectionId { get; set; } = "";
        public string? TargetConnectionId { get; set; }
        public Role? ClaimedRole { get; set; }

        public HashSet<string> Responded { get; set; } = new();
        public PendingPhase Phase { get; set; } = PendingPhase.ActionClaim;
        public string? BlockerConnectionId { get; set; }
        public Role? BlockClaimedRole { get; set; }
        public HashSet<string> BlockResponded { get; set; } = new();

        // Exchange card selection
        public List<Role>? ExchangeAvailableCards { get; set; }
        public int ExchangeCardsToKeep { get; set; }
    }

    public class PendingInfluenceLoss
    {
        public string PlayerConnectionId { get; set; } = "";
        public string Reason { get; set; } = ""; // "Coup", "Assassinated", "Challenge Failed", etc.
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
    }

    public class GameEndStats
    {
        public string WinnerName { get; set; } = "";
        public List<PlayerFinalStats> PlayerStats { get; set; } = new();
        public int TotalTurns { get; set; }
        public string GameDuration { get; set; } = "";
    }

    public class PlayerFinalStats
    {
        public string Name { get; set; } = "";
        public int FinalCoins { get; set; }
        public int RemainingInfluence { get; set; }
        public bool IsWinner { get; set; }
        public bool WasConnected { get; set; }
    }

}
