using Coup.Shared;
using Microsoft.AspNetCore.SignalR;

namespace Coup.Server.Services;

/// <summary>
/// Executes bot actions by using the game action service.
/// This allows bots to perform actions without needing SignalR connections.
/// </summary>
public class BotActionExecutor
{
    private readonly GameActionService _gameActions;
    private readonly GameStore _store;

    public BotActionExecutor(GameActionService gameActions, GameStore store)
    {
        _gameActions = gameActions;
        _store = store;
    }

    /// <summary>
    /// Executes a bot action by calling the game action service
    /// </summary>
    public async Task ExecuteActionAsync(string gameId, string botConnectionId, ActionType action, Role? claimedRole, string? targetConnectionId)
    {
        if (!_store.Games.TryGetValue(gameId, out var game)) return;
        var bot = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
        if (bot == null) return;

        var actionDto = new ActionDto
        {
            Action = action,
            ClaimedRole = claimedRole,
            TargetConnectionId = targetConnectionId
        };

        await _gameActions.PerformActionAsync(game, bot, actionDto, gameId);
    }

    /// <summary>
    /// Executes a pass from a bot
    /// </summary>
    public async Task ExecutePassAsync(string gameId, string botConnectionId)
    {
        if (!_store.Games.TryGetValue(gameId, out var game)) return;
        var bot = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
        if (bot == null) return;

        await _gameActions.PassPendingAsync(game, bot, gameId);
    }

    /// <summary>
    /// Executes a Duke block (ForeignAid)
    /// </summary>
    public async Task ExecuteBlockDukeAsync(string gameId, string botConnectionId)
    {
        if (!_store.Games.TryGetValue(gameId, out var game)) return;
        var bot = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
        if (bot == null) return;

        await _gameActions.BlockDukeAsync(game, bot, gameId);
    }

    /// <summary>
    /// Executes a Contessa block (Assassinate)
    /// </summary>
    public async Task ExecuteBlockContessaAsync(string gameId, string botConnectionId)
    {
        if (!_store.Games.TryGetValue(gameId, out var game)) return;
        var bot = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
        if (bot == null) return;

        await _gameActions.BlockContessaAsync(game, bot, gameId);
    }

    /// <summary>
    /// Executes a Captain/Ambassador block (Steal)
    /// </summary>
    public async Task ExecuteBlockCaptainAmbassadorAsync(string gameId, string botConnectionId, Role role)
    {
        if (!_store.Games.TryGetValue(gameId, out var game)) return;
        var bot = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
        if (bot == null) return;

        await _gameActions.BlockCaptainAmbassadorAsync(game, bot, role, gameId);
    }

    /// <summary>
    /// Executes choosing a card to lose
    /// </summary>
    public async Task ExecuteChooseCardToLoseAsync(string gameId, string botConnectionId, Role role)
    {
        if (!_store.Games.TryGetValue(gameId, out var game)) return;
        var bot = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
        if (bot == null) return;

        await _gameActions.ChooseCardToLoseAsync(game, bot, role, gameId);
    }

    /// <summary>
    /// Executes exchange card selection
    /// </summary>
    public async Task ExecuteExchangeCardsAsync(string gameId, string botConnectionId, List<Role> chosenCards)
    {
        if (!_store.Games.TryGetValue(gameId, out var game)) return;
        var bot = game.Players.FirstOrDefault(p => p.ConnectionId == botConnectionId);
        if (bot == null) return;

        await _gameActions.SubmitExchangeCardsAsync(game, bot, chosenCards, gameId);
    }
}
