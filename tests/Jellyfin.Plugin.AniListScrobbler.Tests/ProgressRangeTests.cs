using Xunit;

namespace Jellyfin.Plugin.AniListScrobbler.Tests;

/// <summary>
/// Progress must always land inside the AniList entry it is written to. A library that
/// numbers one season absolutely while its siblings restart at 1 (observed: Jujutsu Kaisen
/// with seasons 1..24, 1..23, then 48..59) otherwise produces a progress far past the end of
/// a 12-episode entry.
/// </summary>
public class ProgressRangeTests
{
    /// <summary>
    /// Mirrors the overflow walk: while the progress runs past the current entry and a sequel
    /// exists, move into the sequel and subtract.
    /// </summary>
    private static (int Progress, int Index) Overflow(int progress, int[] chain)
    {
        var index = 0;
        while (index < chain.Length - 1 && progress > chain[index])
        {
            progress -= chain[index];
            index++;
        }

        return (progress, index);
    }

    [Fact]
    public void AbsoluteNumbering_WalksIntoTheRightEntry()
    {
        // Episode 59 of a 24 + 23 + 12 run is episode 12 of the third entry.
        var (progress, index) = Overflow(59, new[] { 24, 23, 12 });

        Assert.Equal(2, index);
        Assert.Equal(12, progress);
    }

    [Fact]
    public void SplitCour_WalksIntoThePartTwoEntry()
    {
        // A 25-episode TVDB season that AniList splits into 12 + 13.
        var (progress, index) = Overflow(13, new[] { 12, 13 });

        Assert.Equal(1, index);
        Assert.Equal(1, progress);
    }

    [Fact]
    public void ProgressInsideTheEntry_IsUntouched()
    {
        var (progress, index) = Overflow(7, new[] { 12, 13 });

        Assert.Equal(0, index);
        Assert.Equal(7, progress);
    }

    [Fact]
    public void ProgressPastTheEndOfTheChain_IsOutOfRange()
    {
        // Starting at the season-3 entry with an absolute episode number leaves nowhere to
        // walk, so the scrobble must be refused rather than written.
        var (progress, _) = Overflow(59, new[] { 12 });

        Assert.True(progress > 12, "an out-of-range progress must remain detectable");
    }
}

/// <summary>
/// Which related entries can continue a numbered run.
/// </summary>
public class ContinuationFormatTests
{
    [Theory]
    [InlineData("TV")]
    [InlineData("TV_SHORT")]
    // "SAKAMOTO DAYS Part 2" is the second half of a 22-episode TheTVDB season and is an ONA.
    // Excluding ONA stranded every episode past the first cour.
    [InlineData("ONA")]
    [InlineData("ona")]
    public void ContinuationFormats_AreAccepted(string format)
    {
        Assert.True(Jellyfin.Plugin.AniListScrobbler.Matching.AnimeMatcher.IsContinuationFormat(format));
    }

    [Theory]
    [InlineData("MOVIE")]
    [InlineData("SPECIAL")]
    [InlineData("OVA")]
    [InlineData("MUSIC")]
    [InlineData(null)]
    public void SideContentFormats_AreRejected(string? format)
    {
        Assert.False(Jellyfin.Plugin.AniListScrobbler.Matching.AnimeMatcher.IsContinuationFormat(format));
    }
}
