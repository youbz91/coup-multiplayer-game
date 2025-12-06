using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Coup.Server;

/// <summary>
/// Provides thread-safe locking for game operations to prevent race conditions
/// </summary>
public class GameLockService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gameLocks = new();

    /// <summary>
    /// Executes an action with exclusive lock on the specified game
    /// </summary>
    public async Task<T> ExecuteWithLockAsync<T>(string gameId, Func<Task<T>> action)
    {
        var semaphore = _gameLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Executes an action with exclusive lock on the specified game
    /// </summary>
    public async Task ExecuteWithLockAsync(string gameId, Func<Task> action)
    {
        var semaphore = _gameLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Removes lock for a game (call when game is cleaned up)
    /// </summary>
    public void RemoveLock(string gameId)
    {
        if (_gameLocks.TryRemove(gameId, out var semaphore))
        {
            semaphore.Dispose();
        }
    }
}
