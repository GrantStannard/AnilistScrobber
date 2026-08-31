using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AniListScrobbler.AniList;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Jellyfin.Plugin.AniListScrobbler.Tests;

public class RateLimiterTests
{
    [Fact]
    public async Task WaitAsync_AllowsUpToTheBudgetWithoutDelay()
    {
        var time = new FakeTimeProvider();
        using var limiter = new RateLimiter(3, time);

        for (var i = 0; i < 3; i++)
        {
            await limiter.WaitAsync(CancellationToken.None);
        }

        // The budget is spent, so a fourth request must not complete synchronously.
        var pending = limiter.WaitAsync(CancellationToken.None);
        Assert.False(pending.IsCompleted);

        time.Advance(TimeSpan.FromMinutes(1));
        await pending;
    }

    [Fact]
    public async Task WaitAsync_ReleasesAsTheWindowSlides()
    {
        var time = new FakeTimeProvider();
        using var limiter = new RateLimiter(2, time);

        await limiter.WaitAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(30));
        await limiter.WaitAsync(CancellationToken.None);

        var pending = limiter.WaitAsync(CancellationToken.None);
        Assert.False(pending.IsCompleted);

        // 60s after the first request, its slot frees up even though the second is still held.
        time.Advance(TimeSpan.FromSeconds(30));
        await pending;
    }

    [Fact]
    public async Task PauseFor_BlocksEvenWhenBudgetRemains()
    {
        var time = new FakeTimeProvider();
        using var limiter = new RateLimiter(10, time);

        limiter.PauseFor(TimeSpan.FromSeconds(45));

        var pending = limiter.WaitAsync(CancellationToken.None);
        Assert.False(pending.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(45));
        await pending;
    }

    [Fact]
    public async Task WaitAsync_ObservesCancellation()
    {
        var time = new FakeTimeProvider();
        using var limiter = new RateLimiter(1, time);
        using var cts = new CancellationTokenSource();

        await limiter.WaitAsync(cts.Token);

        var pending = limiter.WaitAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public void PermitsPerWindow_IsClampedToAtLeastOne()
    {
        using var limiter = new RateLimiter(0);
        Assert.Equal(1, limiter.PermitsPerWindow);

        limiter.PermitsPerWindow = -5;
        Assert.Equal(1, limiter.PermitsPerWindow);
    }
}
