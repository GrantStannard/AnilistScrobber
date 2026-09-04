using System;
using Jellyfin.Plugin.AniListScrobbler.Matching;
using Xunit;

namespace Jellyfin.Plugin.AniListScrobbler.Tests;

/// <summary>
/// Libraries do not agree on whether an episode number restarts each season. All three shapes
/// here came from one real library.
/// </summary>
public class AbsoluteNumberingTests
{
    /// <summary>
    /// Reads an episode number as counting from the start of the series across a chain of
    /// entry lengths, mirroring TryReadAsAbsoluteAsync. A null length means the entry is
    /// still airing and unbounded, so everything remaining lands there.
    /// </summary>
    private static (int Index, int Progress)? ReadAsAbsolute(int episodeNumber, int?[] chain)
    {
        var progress = episodeNumber;

        for (var index = 0; index < chain.Length; index++)
        {
            if (chain[index] is not { } total || total <= 0 || progress <= total)
            {
                return progress >= 1 ? (index, progress) : null;
            }

            progress -= total;
        }

        return null;
    }

    [Fact]
    public void JujutsuKaisen_SeasonThreeNumberedAbsolutely()
    {
        // Seasons stored as 1..24, 1..23, then 48..59. Episode 59 is episode 12 of the third
        // entry, not progress 59 on a 12-episode entry.
        var result = ReadAsAbsolute(59, new int?[] { 24, 23, 12 });

        Assert.Equal((2, 12), result);
    }

    [Fact]
    public void ReZero_SeasonFourNumberedAbsolutely()
    {
        // 25 + 13 + 12 + 16 then into the 19-episode fourth season.
        var result = ReadAsAbsolute(81, new int?[] { 25, 13, 12, 16, 19 });

        Assert.Equal((4, 15), result);
    }

    [Fact]
    public void OnePiece_SingleUnboundedEntryTakesEveryEpisode()
    {
        // AniList holds the whole show as one still-airing entry with no episode count, and
        // the library keeps every episode in one folder numbered from 1 upward.
        var result = ReadAsAbsolute(1176, new int?[] { null });

        Assert.Equal((0, 1176), result);
    }

    [Fact]
    public void ANumberPastTheEndOfTheChain_DoesNotResolve()
    {
        Assert.Null(ReadAsAbsolute(500, new int?[] { 12, 13 }));
    }

    [Fact]
    public void FirstEpisode_StaysOnTheFirstEntry()
    {
        Assert.Equal((0, 1), ReadAsAbsolute(1, new int?[] { 12, 13 }));
    }

    [Fact]
    public void AnimeMatch_CarriesTheSeriesStartAndNumberingFlag()
    {
        var match = new AnimeMatch(999, 0, MatchSource.TvdbSeason) { BaseMediaId = 21, AbsoluteNumbering = true };

        Assert.Equal(21, match.BaseMediaId);
        Assert.True(match.AbsoluteNumbering);

        // The default stays conservative: no anchor, no absolute reading.
        var plain = new AnimeMatch(999, 0, MatchSource.AniDbId);
        Assert.Null(plain.BaseMediaId);
        Assert.False(plain.AbsoluteNumbering);
    }
}
