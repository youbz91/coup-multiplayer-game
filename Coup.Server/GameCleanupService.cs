using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Coup.Server;

/// <summary>
/// Background service that periodically cleans up inactive and ended games
/// to prevent memory leaks.
/// </summary>
public class GameCleanupService : BackgroundService
{
    private readonly GameStore _store;
    private readonly GameLockService _lockService;
    private readonly ILogger<GameCleanupService> _logger;
    private readonly IConfiguration _configuration;
    private readonly TimeSpan _cleanupInterval;
    private readonly TimeSpan _inactiveGameTimeout;

    public GameCleanupService(
        GameStore store,
        GameLockService lockService,
        ILogger<GameCleanupService> logger,
        IConfiguration configuration)
    {
        _store = store;
        _lockService = lockService;
        _logger = logger;
        _configuration = configuration;

        // Read from configuration or use defaults
        var intervalMinutes = _configuration.GetValue("GameSettings:GameCleanupIntervalMinutes", 30);
        var timeoutHours = _configuration.GetValue("GameSettings:InactiveGameTimeoutHours", 24);

        _cleanupInterval = TimeSpan.FromMinutes(intervalMinutes);
        _inactiveGameTimeout = TimeSpan.FromHours(timeoutHours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Game Cleanup Service started. Cleanup interval: {Interval} minutes, Timeout: {Timeout} hours",
            _cleanupInterval.TotalMinutes, _inactiveGameTimeout.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);

                CleanupGames();
            }
            catch (OperationCanceledException)
            {
                // Service is stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during game cleanup");
            }
        }

        _logger.LogInformation("Game Cleanup Service stopping");
    }

    private void CleanupGames()
    {
        var now = DateTime.UtcNow;
        var gamesToCleanup = new List<string>();

        foreach (var kvp in _store.Games)
        {
            var gameId = kvp.Key;
            var game = kvp.Value;

            bool shouldCleanup = false;
            string reason = "";

            // Cleanup ended games with no connected players
            if (game.GameEnded && game.Players.All(p => !p.IsConnected))
            {
                shouldCleanup = true;
                reason = "ended with no connected players";
            }
            // Cleanup games where all players have been disconnected for timeout period
            else if (game.Players.All(p => !p.IsConnected) &&
                     game.Players.Any(p => p.DisconnectedTime.HasValue &&
                                          (now - p.DisconnectedTime.Value) > _inactiveGameTimeout))
            {
                shouldCleanup = true;
                reason = $"inactive for {_inactiveGameTimeout.TotalHours} hours";
            }
            // Cleanup games that haven't started and have no players
            else if (!game.GameStarted && game.Players.Count == 0)
            {
                shouldCleanup = true;
                reason = "no players joined";
            }

            if (shouldCleanup)
            {
                gamesToCleanup.Add(gameId);
                _logger.LogInformation("Marking game {GameId} for cleanup: {Reason}", gameId, reason);
            }
        }

        // Perform cleanup
        foreach (var gameId in gamesToCleanup)
        {
            try
            {
                _store.CleanupGame(gameId);
                _lockService.RemoveLock(gameId);
                _logger.LogInformation("Successfully cleaned up game {GameId}", gameId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup game {GameId}", gameId);
            }
        }

        if (gamesToCleanup.Any())
        {
            _logger.LogInformation("Cleaned up {Count} games. Active games remaining: {ActiveCount}",
                gamesToCleanup.Count, _store.Games.Count);
        }
    }
}
