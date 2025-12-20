using Coup.Shared;
using Microsoft.AspNetCore.SignalR;

namespace Coup.Server.Services;

/// <summary>
/// Executes bot actions by using a wrapper hub instance.
/// This allows bots to call hub methods with their synthetic ConnectionId.
/// </summary>
public class BotActionExecutor
{
    private readonly CoupHub _hub;
    private readonly GameStore _store;

    public BotActionExecutor(CoupHub hub, GameStore store)
    {
        _hub = hub;
        _store = store;
    }

    /// <summary>
    /// Executes a bot action by calling the hub method with bot context
    /// </summary>
    public async Task ExecuteActionAsync(string gameId, string botConnectionId, ActionType action, Role? claimedRole, string? targetConnectionId)
    {
        var actionDto = new ActionDto
        {
            Action = action,
            ClaimedRole = claimedRole,
            TargetConnectionId = targetConnectionId
        };

        await _hub.PerformActionForBot(gameId, botConnectionId, actionDto);
    }

    /// <summary>
    /// Executes a challenge from a bot
    /// </summary>
    public async Task ExecuteChallengeAsync(string gameId, string botConnectionId)
    {
        await _hub.ChallengeForBot(gameId, botConnectionId);
    }

    /// <summary>
    /// Executes a pass from a bot
    /// </summary>
    public async Task ExecutePassAsync(string gameId, string botConnectionId)
    {
        await _hub.PassPendingForBot(gameId, botConnectionId);
    }

    /// <summary>
    /// Executes a Duke block (ForeignAid)
    /// </summary>
    public async Task ExecuteBlockDukeAsync(string gameId, string botConnectionId)
    {
        await _hub.BlockDukeForBot(gameId, botConnectionId);
    }

    /// <summary>
    /// Executes a Contessa block (Assassinate)
    /// </summary>
    public async Task ExecuteBlockContessaAsync(string gameId, string botConnectionId)
    {
        await _hub.BlockPendingContessaForBot(gameId, botConnectionId);
    }

    /// <summary>
    /// Executes a Captain/Ambassador block (Steal)
    /// </summary>
    public async Task ExecuteBlockCaptainAmbassadorAsync(string gameId, string botConnectionId, Role role)
    {
        await _hub.BlockCaptainAmbassadorForBot(gameId, botConnectionId, role);
    }

    /// <summary>
    /// Executes choosing a card to lose
    /// </summary>
    public async Task ExecuteChooseCardToLoseAsync(string gameId, string botConnectionId, Role role)
    {
        await _hub.ChooseCardToLoseForBot(gameId, botConnectionId, role);
    }

    /// <summary>
    /// Executes exchange card selection
    /// </summary>
    public async Task ExecuteExchangeCardsAsync(string gameId, string botConnectionId, List<Role> chosenCards)
    {
        await _hub.SubmitExchangeCardsForBot(gameId, botConnectionId, chosenCards);
    }
}
