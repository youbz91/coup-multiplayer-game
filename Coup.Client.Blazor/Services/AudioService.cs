using Microsoft.JSInterop;

namespace Coup.Client.Blazor.Services;

/// <summary>
/// Service for playing sound effects in the game
/// </summary>
public class AudioService
{
    private readonly IJSRuntime _jsRuntime;
    private bool _soundEnabled = true;
    private double _volume = 0.5; // 50% volume by default

    public AudioService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public bool SoundEnabled
    {
        get => _soundEnabled;
        set => _soundEnabled = value;
    }

    public double Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0.0, 1.0);
    }

    public async Task PlaySoundAsync(string soundName)
    {
        if (!_soundEnabled) return;

        try
        {
            await _jsRuntime.InvokeVoidAsync("playSound", soundName, _volume);
        }
        catch (Exception)
        {
            // Silently fail if sound doesn't exist or JS not ready
        }
    }

    // Specific sound effects
    public Task PlayClickAsync() => PlaySoundAsync("click");
    public Task PlayCoinGainAsync() => PlaySoundAsync("coin-gain");
    public Task PlayCoinLossAsync() => PlaySoundAsync("coin-loss");
    public Task PlayCardFlipAsync() => PlaySoundAsync("card-flip");
    public Task PlayActionSuccessAsync() => PlaySoundAsync("action-success");
    public Task PlayActionBlockedAsync() => PlaySoundAsync("action-blocked");
    public Task PlayTimerWarningAsync() => PlaySoundAsync("timer-warning");
    public Task PlayInfluenceLossAsync() => PlaySoundAsync("influence-loss");
    public Task PlayChallengeAsync() => PlaySoundAsync("challenge");
}
