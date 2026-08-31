using Jellyfin.Plugin.AniListScrobbler.Scrobbling;
using Xunit;

namespace Jellyfin.Plugin.AniListScrobbler.Tests;

public class PlaybackWatcherTests
{
    private const long Minute = 60L * 10_000_000L;

    [Fact]
    public void IsWatched_AtThreshold_IsWatched()
    {
        // 21 of 24 minutes is 87.5%.
        Assert.True(PlaybackWatcher.IsWatched(21 * Minute, 24 * Minute, false, 85));
    }

    [Fact]
    public void IsWatched_ExactlyThreshold_IsWatched()
    {
        Assert.True(PlaybackWatcher.IsWatched(12 * Minute, 24 * Minute, false, 50));
    }

    [Fact]
    public void IsWatched_BelowThreshold_IsNotWatched()
    {
        Assert.False(PlaybackWatcher.IsWatched(10 * Minute, 24 * Minute, false, 85));
    }

    [Fact]
    public void IsWatched_ThresholdStricterThanJellyfin_OverridesPlayedToCompletion()
    {
        // Jellyfin marks an item played past MaxResumePct, 90% by default. A user asking for
        // 95% must not be overruled by that flag, or the setting would do nothing above 90.
        Assert.False(PlaybackWatcher.IsWatched(22 * Minute, 24 * Minute, playedToCompletion: true, 95));
    }

    [Fact]
    public void IsWatched_ThresholdLooserThanJellyfin_StillScrobblesEarly()
    {
        // The reverse direction: Jellyfin has not marked it played at 62%, but the user asked
        // for 50%.
        Assert.True(PlaybackWatcher.IsWatched(15 * Minute, 24 * Minute, playedToCompletion: false, 50));
    }

    [Fact]
    public void IsWatched_PlayedToCompletionWithKnownTimings_StillRespectsTheThreshold()
    {
        // Stopping at the very start cannot count as watched just because the flag was set.
        Assert.False(PlaybackWatcher.IsWatched(0, 24 * Minute, playedToCompletion: true, 85));
    }

    [Theory]
    [InlineData(null, 24 * Minute)]
    [InlineData(21 * Minute, null)]
    [InlineData(21 * Minute, 0L)]
    [InlineData(null, null)]
    public void IsWatched_WithoutUsableTimings_DefersToJellyfin(long? position, long? runtime)
    {
        // Nothing to measure, so Jellyfin's own judgement decides. It marks these items
        // played, and disagreeing would leave AniList behind the Jellyfin library.
        Assert.True(PlaybackWatcher.IsWatched(position, runtime, playedToCompletion: true, 85));
        Assert.False(PlaybackWatcher.IsWatched(position, runtime, playedToCompletion: false, 85));
    }
}
