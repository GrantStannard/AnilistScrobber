using Jellyfin.Plugin.AniListScrobbler.Scrobbling;
using Xunit;

namespace Jellyfin.Plugin.AniListScrobbler.Tests;

public class PlaybackWatcherTests
{
    private const long Minute = 60L * 10_000_000L;

    [Fact]
    public void IsWatched_PlayedToCompletion_IsAlwaysWatched()
    {
        Assert.True(PlaybackWatcher.IsWatched(0, 24 * Minute, playedToCompletion: true, minimumPercentage: 90));
    }

    [Fact]
    public void IsWatched_AtThreshold_IsWatched()
    {
        // 21 of 24 minutes is 87.5%.
        Assert.True(PlaybackWatcher.IsWatched(21 * Minute, 24 * Minute, false, 85));
    }

    [Fact]
    public void IsWatched_BelowThreshold_IsNotWatched()
    {
        Assert.False(PlaybackWatcher.IsWatched(10 * Minute, 24 * Minute, false, 85));
    }

    [Fact]
    public void IsWatched_ExactlyThreshold_IsWatched()
    {
        Assert.True(PlaybackWatcher.IsWatched(12 * Minute, 24 * Minute, false, 50));
    }

    [Theory]
    [InlineData(null, 24 * Minute)]
    [InlineData(21 * Minute, null)]
    [InlineData(21 * Minute, 0L)]
    public void IsWatched_WithoutUsableTimings_IsNotWatched(long? position, long? runtime)
    {
        // Live TV and some remote streams report no runtime; guessing there would scrobble
        // things the user never finished.
        Assert.False(PlaybackWatcher.IsWatched(position, runtime, false, 85));
    }
}
