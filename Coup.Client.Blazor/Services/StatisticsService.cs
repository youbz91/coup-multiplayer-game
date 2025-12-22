using Coup.Shared;
using Microsoft.JSInterop;
using System.Text.Json;

namespace Coup.Client.Blazor.Services;

/// <summary>
/// Tracks player statistics and achievements
/// </summary>
public class StatisticsService
{
    private readonly IJSRuntime _jsRuntime;
    private PlayerStatistics _stats;
    private const string STATS_KEY = "coup_player_statistics";

    public StatisticsService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
        _stats = new PlayerStatistics();
    }

    public PlayerStatistics Stats => _stats;

    /// <summary>
    /// Load statistics from localStorage
    /// </summary>
    public async Task LoadStatisticsAsync()
    {
        try
        {
            var json = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", STATS_KEY);
            if (!string.IsNullOrEmpty(json))
            {
                _stats = JsonSerializer.Deserialize<PlayerStatistics>(json) ?? new PlayerStatistics();
            }
        }
        catch
        {
            _stats = new PlayerStatistics();
        }
    }

    /// <summary>
    /// Save statistics to localStorage
    /// </summary>
    public async Task SaveStatisticsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_stats);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", STATS_KEY, json);
        }
        catch
        {
            // Silently fail
        }
    }

    /// <summary>
    /// Record a game result
    /// </summary>
    public async Task RecordGameAsync(bool won, int turns, TimeSpan duration)
    {
        _stats.GamesPlayed++;
        if (won)
        {
            _stats.GamesWon++;
        }
        else
        {
            _stats.GamesLost++;
        }

        _stats.TotalPlayTime += duration;
        await SaveStatisticsAsync();
    }

    /// <summary>
    /// Record a challenge
    /// </summary>
    public async Task RecordChallengeAsync(bool successful)
    {
        if (successful)
        {
            _stats.SuccessfulChallenges++;
        }
        else
        {
            _stats.FailedChallenges++;
        }
        await SaveStatisticsAsync();
    }

    /// <summary>
    /// Record a bluff
    /// </summary>
    public async Task RecordBluffAsync(bool successful)
    {
        if (successful)
        {
            _stats.SuccessfulBluffs++;
        }
        else
        {
            _stats.CaughtBluffs++;
        }
        await SaveStatisticsAsync();
    }

    /// <summary>
    /// Record an action used
    /// </summary>
    public async Task RecordActionAsync(ActionType action)
    {
        if (!_stats.MostUsedActions.ContainsKey(action))
        {
            _stats.MostUsedActions[action] = 0;
        }
        _stats.MostUsedActions[action]++;
        await SaveStatisticsAsync();
    }

    /// <summary>
    /// Record a role used
    /// </summary>
    public async Task RecordRoleAsync(Role role)
    {
        if (!_stats.MostUsedRoles.ContainsKey(role))
        {
            _stats.MostUsedRoles[role] = 0;
        }
        _stats.MostUsedRoles[role]++;
        await SaveStatisticsAsync();
    }

    /// <summary>
    /// Reset all statistics
    /// </summary>
    public async Task ResetStatisticsAsync()
    {
        _stats = new PlayerStatistics();
        await SaveStatisticsAsync();
    }
}
