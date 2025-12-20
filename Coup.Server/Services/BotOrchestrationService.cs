using Coup.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coup.Server.Services;

/// <summary>
/// Background service that polls games and triggers bot decisions.
/// Runs continuously, checking every 500ms for bots that need to act.
/// </summary>
public class BotOrchestrationService : BackgroundService
{
    private readonly GameStore _store;
    private readonly BotDecisionEngine _decisionEngine;
    private readonly ILogger<BotOrchestrationService> _logger;
    private readonly HashSet<string> _processingBots = new();
    private readonly object _lock = new();

    public BotOrchestrationService(
        GameStore store,
        BotDecisionEngine decisionEngine,
        ILogger<BotOrchestrationService> logger)
    {
        _store = store;
        _decisionEngine = decisionEngine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BotOrchestrationService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAllGamesForBotActions();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BotOrchestrationService");
            }

            // Poll every 500ms
            await Task.Delay(500, stoppingToken);
        }

        _logger.LogInformation("BotOrchestrationService stopped");
    }

    private async Task CheckAllGamesForBotActions()
    {
        foreach (var game in _store.Games.Values)
        {
            // Skip games that haven't started or have ended
            if (!game.GameStarted || game.GameEnded)
                continue;

            // Check if current player is a bot that needs to take their turn
            var currentPlayer = game.Players.ElementAtOrDefault(game.CurrentPlayerIndex);
            if (currentPlayer != null && currentPlayer.IsBot && currentPlayer.IsAlive)
            {
                if (!IsProcessing(currentPlayer.ConnectionId))
                {
                    _ = Task.Run(async () => await HandleBotTurn(game, currentPlayer));
                }
            }

            // Check for bots that need to respond to pending actions
            if (game.Pending != null)
            {
                await CheckBotsNeedingPendingResponse(game);
            }

            // Check for bots that need to choose a card to lose
            if (game.PendingInfluenceLoss != null)
            {
                var bot = game.Players.FirstOrDefault(p =>
                    p.ConnectionId == game.PendingInfluenceLoss.PlayerConnectionId && p.IsBot);

                if (bot != null && !IsProcessing(bot.ConnectionId))
                {
                    _ = Task.Run(async () => await HandleBotInfluenceLoss(game, bot));
                }
            }
        }
    }

    private async Task CheckBotsNeedingPendingResponse(GameState game)
    {
        var pending = game.Pending!;

        if (pending.Phase == PendingPhase.ExchangeCardSelection)
        {
            // Bot needs to choose exchange cards
            var bot = game.Players.FirstOrDefault(p =>
                p.ConnectionId == pending.ActorConnectionId && p.IsBot);

            if (bot != null && !IsProcessing(bot.ConnectionId))
            {
                _ = Task.Run(async () => await HandleBotExchange(game, bot));
            }
        }
        else if (pending.Phase == PendingPhase.ActionClaim)
        {
            // Check all bots that haven't responded yet
            var botsNeedingResponse = game.Players.Where(p =>
                p.IsBot &&
                p.IsAlive &&
                p.ConnectionId != pending.ActorConnectionId &&
                !pending.Responded.Contains(p.ConnectionId)).ToList();

            foreach (var bot in botsNeedingResponse)
            {
                if (!IsProcessing(bot.ConnectionId))
                {
                    _ = Task.Run(async () => await HandleBotPendingResponse(game, bot));
                }
            }
        }
        else if (pending.Phase == PendingPhase.BlockClaim)
        {
            // Check all bots that haven't responded to the block yet
            var botsNeedingResponse = game.Players.Where(p =>
                p.IsBot &&
                p.IsAlive &&
                p.ConnectionId != pending.BlockerConnectionId &&
                !pending.BlockResponded.Contains(p.ConnectionId)).ToList();

            foreach (var bot in botsNeedingResponse)
            {
                if (!IsProcessing(bot.ConnectionId))
                {
                    _ = Task.Run(async () => await HandleBotBlockResponse(game, bot));
                }
            }
        }
    }

    private async Task HandleBotTurn(GameState game, PlayerState bot)
    {
        try
        {
            MarkProcessing(bot.ConnectionId);

            if (!_store.BotConfigs.TryGetValue(bot.ConnectionId, out var config))
                return;

            // Add thinking delay
            var delay = config.GetActionDelayMs();
            _logger.LogDebug("Bot {BotName} thinking for {Delay}ms", bot.Name, delay);
            await Task.Delay(delay);

            // Make decision
            await _decisionEngine.MakeDecisionAsync(game.GameId, bot.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling bot turn for {BotName}", bot.Name);
        }
        finally
        {
            UnmarkProcessing(bot.ConnectionId);
        }
    }

    private async Task HandleBotPendingResponse(GameState game, PlayerState bot)
    {
        try
        {
            MarkProcessing(bot.ConnectionId);

            if (!_store.BotConfigs.TryGetValue(bot.ConnectionId, out var config))
                return;

            // Shorter delay for responses (50-70% of action delay)
            var delay = (int)(config.GetActionDelayMs() * 0.6);
            await Task.Delay(delay);

            await _decisionEngine.MakeDecisionAsync(game.GameId, bot.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling bot pending response for {BotName}", bot.Name);
        }
        finally
        {
            UnmarkProcessing(bot.ConnectionId);
        }
    }

    private async Task HandleBotBlockResponse(GameState game, PlayerState bot)
    {
        try
        {
            MarkProcessing(bot.ConnectionId);

            if (!_store.BotConfigs.TryGetValue(bot.ConnectionId, out var config))
                return;

            var delay = (int)(config.GetActionDelayMs() * 0.6);
            await Task.Delay(delay);

            await _decisionEngine.MakeDecisionAsync(game.GameId, bot.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling bot block response for {BotName}", bot.Name);
        }
        finally
        {
            UnmarkProcessing(bot.ConnectionId);
        }
    }

    private async Task HandleBotInfluenceLoss(GameState game, PlayerState bot)
    {
        try
        {
            MarkProcessing(bot.ConnectionId);

            if (!_store.BotConfigs.TryGetValue(bot.ConnectionId, out var config))
                return;

            // Quick decision for losing a card (30% of normal delay)
            var delay = (int)(config.GetActionDelayMs() * 0.3);
            await Task.Delay(delay);

            await _decisionEngine.MakeDecisionAsync(game.GameId, bot.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling bot influence loss for {BotName}", bot.Name);
        }
        finally
        {
            UnmarkProcessing(bot.ConnectionId);
        }
    }

    private async Task HandleBotExchange(GameState game, PlayerState bot)
    {
        try
        {
            MarkProcessing(bot.ConnectionId);

            if (!_store.BotConfigs.TryGetValue(bot.ConnectionId, out var config))
                return;

            // Longer delay for exchange (bots need to "think" about cards)
            var delay = (int)(config.GetActionDelayMs() * 1.2);
            await Task.Delay(delay);

            await _decisionEngine.MakeDecisionAsync(game.GameId, bot.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling bot exchange for {BotName}", bot.Name);
        }
        finally
        {
            UnmarkProcessing(bot.ConnectionId);
        }
    }

    private bool IsProcessing(string botConnectionId)
    {
        lock (_lock)
        {
            return _processingBots.Contains(botConnectionId);
        }
    }

    private void MarkProcessing(string botConnectionId)
    {
        lock (_lock)
        {
            _processingBots.Add(botConnectionId);
        }
    }

    private void UnmarkProcessing(string botConnectionId)
    {
        lock (_lock)
        {
            _processingBots.Remove(botConnectionId);
        }
    }
}
