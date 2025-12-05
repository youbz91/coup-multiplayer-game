using Coup.Shared;
using Microsoft.AspNetCore.SignalR.Client;

namespace Coup.Client.Blazor.Services;

public class GameService : IAsyncDisposable
{
    private HubConnection? _hubConnection;
    private readonly string _hubUrl = "http://localhost:5076/couphub";

    public GameState CurrentGame { get; private set; } = new();
    public List<Role> MyRoles { get; private set; } = new();
    public string MyName { get; set; } = "";
    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
    public bool MustChooseCard { get; private set; }
    public string ChooseCardReason { get; private set; } = "";

    // Events
    public event Action? OnGameStateChanged;
    public event Action? OnRolesReceived;
    public event Action<string>? OnError;
    public event Action<string>? OnChooseInfluence;

    public async Task ConnectAsync()
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .Build();

        // Register event handlers
        _hubConnection.On<GameState>("GameStateUpdated", state =>
        {
            CurrentGame = state;

            // Check if we need to choose a card
            var me = CurrentGame.Players.FirstOrDefault(p => p.Name == MyName);
            if (CurrentGame.PendingInfluenceLoss == null ||
                CurrentGame.PendingInfluenceLoss.PlayerConnectionId != me?.ConnectionId)
            {
                MustChooseCard = false;
                ChooseCardReason = "";
            }

            OnGameStateChanged?.Invoke();
        });

        _hubConnection.On<List<Role>>("YourRoles", roles =>
        {
            MyRoles = roles;
            OnRolesReceived?.Invoke();
        });

        _hubConnection.On<string>("Error", msg =>
        {
            OnError?.Invoke(msg);
        });

        _hubConnection.On<string>("ChooseInfluence", message =>
        {
            MustChooseCard = true;
            ChooseCardReason = message;
            OnChooseInfluence?.Invoke(message);
        });

        _hubConnection.On("YourTurn", () =>
        {
            OnGameStateChanged?.Invoke();
        });

        _hubConnection.On("RematchReady", () =>
        {
            OnGameStateChanged?.Invoke();
        });

        await _hubConnection.StartAsync();
    }

    public async Task JoinLobbyAsync(string gameId, string playerName)
    {
        if (_hubConnection == null) throw new InvalidOperationException("Not connected");

        MyName = playerName;
        await _hubConnection.InvokeAsync("JoinLobby", gameId, playerName);
    }

    public async Task StartGameAsync(string gameId)
    {
        if (_hubConnection == null) throw new InvalidOperationException("Not connected");
        await _hubConnection.InvokeAsync("StartGame", gameId);
    }

    public async Task RematchAsync(string gameId)
    {
        if (_hubConnection == null) throw new InvalidOperationException("Not connected");
        await _hubConnection.InvokeAsync("RematchGame", gameId);
    }

    public async Task PerformActionAsync(ActionDto action)
    {
        if (_hubConnection == null) throw new InvalidOperationException("Not connected");
        await _hubConnection.InvokeAsync("PerformAction", action);
    }

    public async Task ChallengeAsync()
    {
        if (_hubConnection == null) throw new InvalidOperationException("Not connected");
        await _hubConnection.InvokeAsync("Challenge");
    }

    public async Task PassPendingAsync()
    {
        if (_hubConnection == null) throw new InvalidOperationException("Not connected");
        await _hubConnection.InvokeAsync("PassPending");
    }

    public async Task BlockContessaAsync()
    {
        if (_hubConnection == null) throw new InvalidOperationException("Not connected");
        await _hubConnection.InvokeAsync("BlockPendingContessa");
    }

    public async Task BlockDukeAsync()
    {
        if (_hubConnection == null) throw new InvalidOperationException("Not connected");
        await _hubConnection.InvokeAsync("BlockDuke");
    }

    public async Task BlockWithRoleAsync(Role role)
    {
        if (_hubConnection == null) throw new InvalidOperationException("Not connected");
        await _hubConnection.InvokeAsync("BlockCaptainAmbassador", role);
    }

    public async Task ChooseCardToLoseAsync(Role role)
    {
        if (_hubConnection == null) throw new InvalidOperationException("Not connected");
        await _hubConnection.InvokeAsync("ChooseCardToLose", role);
        MustChooseCard = false;
        ChooseCardReason = "";
    }

    public bool IsMyTurn()
    {
        var me = CurrentGame.Players.FirstOrDefault(p => p.Name == MyName);
        if (me == null) return false;

        var myIndex = CurrentGame.Players.IndexOf(me);
        return myIndex == CurrentGame.CurrentPlayerIndex &&
               !CurrentGame.GameEnded &&
               CurrentGame.GameStarted;
    }

    public PlayerState? GetMe()
    {
        return CurrentGame.Players.FirstOrDefault(p => p.Name == MyName);
    }

    public bool IsHost()
    {
        var me = GetMe();
        return me != null && CurrentGame.HostConnectionId == me.ConnectionId;
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}
