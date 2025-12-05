using System.Collections.Concurrent;
using Coup.Shared;

namespace Coup.Server
{
    public class GameStore
    {
        public ConcurrentDictionary<string, GameState> Games { get; } = new();
        public ConcurrentDictionary<string, string> ConnectionToGame { get; } = new();
        public ConcurrentDictionary<string, List<Role>> PlayerRoles { get; } = new();
        public ConcurrentDictionary<string, List<Role>> GameDecks { get; } = new(); // Deck restant pour chaque partie (pour Exchange)

        /// <summary>
        /// Cleans up all game data from memory to prevent memory leaks
        /// </summary>
        public void CleanupGame(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return;

            // Remove the game itself
            if (Games.TryRemove(gameId, out var game))
            {
                // Remove all player data for this game
                foreach (var player in game.Players)
                {
                    if (!string.IsNullOrEmpty(player.ConnectionId))
                    {
                        ConnectionToGame.TryRemove(player.ConnectionId, out _);
                        PlayerRoles.TryRemove(player.ConnectionId, out _);
                    }
                }
            }

            // Remove the game deck
            GameDecks.TryRemove(gameId, out _);
        }
    }
}
