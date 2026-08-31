using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AniListScrobbler.AniList;

/// <summary>
/// A sliding-window rate limiter. AniList publishes a 90 requests/minute budget but in
/// practice serves a degraded 30/minute, and answers 429 with a Retry-After header when
/// exceeded. Requests are serialised through this gate so a burst of episode updates cannot
/// trip the limit.
/// </summary>
public sealed class RateLimiter : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<long> _timestamps = new();
    private readonly TimeSpan _window = TimeSpan.FromMinutes(1);
    private readonly TimeProvider _timeProvider;

    private int _permitsPerWindow;
    private long _pausedUntilTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimiter"/> class.
    /// </summary>
    /// <param name="permitsPerWindow">The number of requests allowed per minute.</param>
    /// <param name="timeProvider">The time source; defaults to the system clock.</param>
    public RateLimiter(int permitsPerWindow, TimeProvider? timeProvider = null)
    {
        _permitsPerWindow = Math.Max(1, permitsPerWindow);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Gets or sets the number of requests allowed per minute.
    /// </summary>
    public int PermitsPerWindow
    {
        get => Volatile.Read(ref _permitsPerWindow);
        set => Volatile.Write(ref _permitsPerWindow, Math.Max(1, value));
    }

    /// <summary>
    /// Waits until a request may be sent, then records it against the budget.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the caller may proceed.</returns>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = _timeProvider.GetUtcNow();
                var pausedUntil = Interlocked.Read(ref _pausedUntilTicks);

                if (now.UtcTicks < pausedUntil)
                {
                    wait = TimeSpan.FromTicks(pausedUntil - now.UtcTicks);
                }
                else
                {
                    var cutoff = now.UtcTicks - _window.Ticks;
                    while (_timestamps.Count > 0 && _timestamps.Peek() <= cutoff)
                    {
                        _timestamps.Dequeue();
                    }

                    if (_timestamps.Count < PermitsPerWindow)
                    {
                        _timestamps.Enqueue(now.UtcTicks);
                        return;
                    }

                    // The oldest request leaves the window at its timestamp + one window.
                    var remaining = _timestamps.Peek() + _window.Ticks - now.UtcTicks;
                    wait = TimeSpan.FromTicks(Math.Max(remaining, TimeSpan.TicksPerMillisecond));
                }
            }
            finally
            {
                _gate.Release();
            }

            await Task.Delay(wait, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Blocks all requests for the given duration, in response to a 429 from AniList.
    /// </summary>
    /// <param name="duration">How long to pause.</param>
    public void PauseFor(TimeSpan duration)
    {
        var until = _timeProvider.GetUtcNow().UtcTicks + Math.Max(duration.Ticks, 0);

        long current;
        do
        {
            current = Interlocked.Read(ref _pausedUntilTicks);
            if (until <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _pausedUntilTicks, until, current) != current);
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}
