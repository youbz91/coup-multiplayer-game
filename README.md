# 🎴 Coup Multiplayer Game

A real-time multiplayer implementation of the Coup card game using ASP.NET Core SignalR and Blazor WebAssembly.

## 🚀 Quick Start

### Prerequisites
- .NET 8.0 or later
- Windows/Linux/Mac

### Running the Game

1. **Start the Server** (Terminal 1):
   ```bash
   cd Coup.Server
   dotnet run
   ```
   Server will start at: `http://localhost:5076`

2. **Start the Blazor Web Client** (Terminal 2):
   ```bash
   cd Coup.Client.Blazor
   dotnet run
   ```
   Web UI will open at: `http://localhost:5000` (or similar like 5126)

3. **Or use Console Client** (Alternative):
   ```bash
   cd Coup.Client.Console
   dotnet run
   ```

## 🎮 How to Play

### Web UI (Blazor)
1. Open browser to `http://localhost:5000`
2. Enter your name and game ID (default: room1)
3. Click "Join Game"
4. Host clicks "Start Game" when ready
5. Play the game!

### Console Client
1. Run the console client
2. Enter your name and game ID
3. Type `start` (host only) to start the game
4. Type `help` to see available actions

## 📦 Project Structure

```
complotsV2/
├── Coup.Server/                 # SignalR server
│   ├── CoupHub.cs              # Game logic hub
│   ├── GameStore.cs            # In-memory game storage
│   └── GameTimeoutService.cs    # Timeout handling
├── Coup.Shared/                 # Shared models (used by all clients)
│   └── Models.cs               # Game state, player state, etc.
├── Coup.Client.Blazor/          # ⭐ NEW Blazor WebAssembly UI
│   ├── Pages/
│   │   └── Game.razor          # Main game interface
│   ├── Services/
│   │   └── GameService.cs      # SignalR connection service
│   └── wwwroot/
│       └── css/game.css        # Styling
└── Coup.Client.Console/         # Console client
    └── Program.cs              # Terminal-based UI
```

## ✨ Features

### Completed Features
- ✅ **All Coup game mechanics**
  - Income, Foreign Aid, Coup
  - Tax (Duke), Assassinate (Assassin), Steal (Captain), Exchange (Ambassador)
  - Challenge system
  - Block system (Contessa, Duke, Captain, Ambassador)

- ✅ **Multiplayer**
  - Real-time SignalR communication
  - 2+ players per game
  - Multiple simultaneous games

- ✅ **Reconnection**
  - Players can disconnect and reconnect
  - Game state preserved
  - Roles restored

- ✅ **Rematch System**
  - Quick rematch after game ends
  - Keeps same players
  - Fresh game state

- ✅ **Two Client Options**
  - Beautiful web UI (Blazor)
  - Console client for terminal users

### Game Rules
See [Coup Rules](https://www.ultraboardgames.com/coup/game-rules.php) for full rules.

## 🎯 Actions

| Action | Cost | Effect | Blockable |
|--------|------|--------|-----------|
| Income | Free | +1 coin | No |
| Foreign Aid | Free | +2 coins | Yes (Duke) |
| Coup | 7 coins | Opponent loses influence | No |
| Tax (Duke) | Free | +3 coins | No (can challenge) |
| Assassinate (Assassin) | 3 coins | Target loses influence | Yes (Contessa) |
| Steal (Captain) | Free | Take 2 coins from target | Yes (Captain/Ambassador) |
| Exchange (Ambassador) | Free | Swap cards with deck | No (can challenge) |

## 🛠️ Development

### Building
```bash
# Build all projects
dotnet build

# Build specific project
cd Coup.Server
dotnet build
```

### Running Tests
```bash
dotnet test
```

### Hot Reload (Development)
```bash
# Server with hot reload
cd Coup.Server
dotnet watch run

# Blazor with hot reload
cd Coup.Client.Blazor
dotnet watch run
```

## 🌐 Deployment

### Deploy Server
1. Publish: `dotnet publish -c Release`
2. Deploy to Azure/AWS/your hosting
3. Update `hubUrl` in clients to production URL

### Deploy Blazor Client
1. Publish: `dotnet publish -c Release`
2. Host on Azure Static Web Apps, GitHub Pages, or any static hosting
3. Point to production SignalR server

## 📝 TODO / Future Enhancements

- [ ] Complete Exchange action (card selection UI)
- [ ] Add game statistics/leaderboard
- [ ] Add chat system
- [ ] Add game history/replays
- [ ] Add spectator mode
- [ ] Add AI/bot players
- [ ] Add animations to Blazor UI
- [ ] Add sound effects
- [ ] Mobile responsive improvements
- [ ] Add authentication
- [ ] Add lobby browser (see all active games)

## 🤝 Contributing

Feel free to submit issues and pull requests!

## 📄 License

MIT License - feel free to use for learning and fun!

## 🎮 Credits

Based on the card game Coup by Rikki Tahta.
