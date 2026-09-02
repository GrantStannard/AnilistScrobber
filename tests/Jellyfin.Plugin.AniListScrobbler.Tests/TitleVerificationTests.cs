using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.AniListScrobbler.AniList.Models;
using Jellyfin.Plugin.AniListScrobbler.Matching;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.AniListScrobbler.Tests;

/// <summary>
/// Every case here was observed in a real 110-series anime library whose items are tagged with
/// AniDB ids. The default threshold is 0.80; the accepted cases sit at 0.85 and above and the
/// rejected ones at 0.72 and below, so the boundary has margin on both sides.
/// </summary>
public class TitleVerificationTests
{
    private const double Threshold = 0.80;

    private static Media Anime(string? romaji, string? english = null, params string[] synonyms)
        => new()
        {
            Id = 1,
            Title = new MediaTitle { Romaji = romaji, English = english },
            Synonyms = synonyms,
        };

    private static double Score(IEnumerable<string> local, Media media)
        => AnimeMatcher.ScoreTitles(local, media).Score;

    [Theory]
    // A bad AniDB id on a live-action show: watching Psych would have scrobbled Psychic Academy.
    [InlineData("Psych", "Psych", "Psychic Academy")]
    [InlineData("Ted Lasso", "Ted Lasso", "Animated Classics of Japanese Literature")]
    // A real anime whose id points at a special or film rather than the series.
    [InlineData("Black Clover", "Black Clover", "Black Clover: Jump Festa 2016 Special")]
    [InlineData("The Mentalist", "The 100 Girlfriends Who Really, Really, Really, Really, REALLY Love You", "Love Hina Spring Movie")]
    // The western show, whose id resolves to the unrelated anime of nearly the same name.
    [InlineData("Rick and Morty", "Rick and Morty", "Rick and Morty: The Anime")]
    public void Score_BelowThreshold_ForMismatchedEntries(string name, string folder, string aniListTitle)
    {
        Assert.True(Score(new[] { name, folder }, Anime(aniListTitle)) < Threshold);
    }

    [Fact]
    public void Score_RejectsAFilmEntryForASeries()
    {
        // KONOSUBA's id resolved to the film, which would complete the entry after one episode.
        var score = Score(
            new[] { "KONOSUBA - God's blessing on this wonderful world!", "KonoSuba – God’s blessing on this wonderful world!" },
            Anime("KONOSUBA -God's blessing on this wonderful world! Legend of Crimson"));

        Assert.True(score < Threshold);
    }

    [Fact]
    public void Score_AcceptsWhenOnlyTheFolderIsRight()
    {
        // The display name came from a bad metadata match, but the AniDB id and the folder are
        // both correct. Scoring the display name alone would reject a good match.
        var media = Anime("Kanojo mo Kanojo", "Girlfriend, Girlfriend");

        Assert.True(Score(new[] { "The Girlfriend Experience" }, media) < Threshold);
        Assert.Equal(1.0, Score(new[] { "The Girlfriend Experience", "Girlfriend, Girlfriend" }, media));
    }

    [Fact]
    public void Score_AcceptsDifferentPunctuationOfTheSameTitle()
    {
        // Folder "Ranma ½ (2024)" against AniList "Ranma1/2 (2024)". The fraction only folds
        // into "1/2" under compatibility normalisation; without it the character is dropped
        // outright and the score falls below the threshold.
        var score = Score(new[] { "Ranma1/2", "Ranma ½ (2024)" }, Anime("Ranma1/2 (2024)"));

        Assert.True(score >= Threshold, $"expected >= {Threshold}, got {score:F2}");
    }

    /// <summary>
    /// The whole corpus of awkward cases from the sample library, asserted as one separation
    /// rather than case by case: every entry that should be accepted must outrank every entry
    /// that should be rejected, with the default threshold falling in the gap between them.
    /// This is what stops a normalisation tweak from quietly closing that gap.
    /// </summary>
    [Fact]
    public void Threshold_SeparatesTheRealCorpus()
    {
        (string Name, string Folder, string AniList, bool Accept)[] corpus =
        {
            ("The Mentalist", "The 100 Girlfriends Who Really, Really, Really, Really, REALLY Love You", "Love Hina Spring Movie", false),
            ("Ted Lasso", "Ted Lasso", "Animated Classics of Japanese Literature", false),
            ("Psych", "Psych", "Psychic Academy", false),
            ("Black Clover", "Black Clover", "Black Clover: Jump Festa 2016 Special", false),
            ("Rick and Morty", "Rick and Morty", "Rick and Morty: The Anime", false),
            ("KONOSUBA - God's blessing on this wonderful world!", "KonoSuba - God's blessing on this wonderful world!", "KONOSUBA -God's blessing on this wonderful world! Legend of Crimson", false),
            ("Ranma1/2", "Ranma ½ (2024)", "Ranma1/2 (2024)", true),
            ("The Girlfriend Experience", "Girlfriend, Girlfriend", "Girlfriend, Girlfriend", true),
            ("The Apothecary Diaries", "The Apothecary Diaries", "The Apothecary Diaries", true),
            ("BOCCHI THE ROCK!", "Bocchi the Rock!", "Bocchi the Rock!", true),
            ("Ascendance of a Bookworm", "Ascendance of a Bookworm", "Ascendance of a Bookworm", true),
        };

        var worstAccept = 1.0;
        var bestReject = 0.0;

        foreach (var (name, folder, aniList, accept) in corpus)
        {
            var score = Score(new[] { name, folder }, Anime(aniList));

            if (accept)
            {
                worstAccept = Math.Min(worstAccept, score);
            }
            else
            {
                bestReject = Math.Max(bestReject, score);
            }
        }

        Assert.True(
            bestReject < Threshold && Threshold <= worstAccept,
            $"threshold {Threshold} does not separate the corpus: worst accepted {worstAccept:F2}, best rejected {bestReject:F2}");
    }

    [Theory]
    [InlineData("The Apothecary Diaries")]
    [InlineData("Ascendance of a Bookworm")]
    [InlineData("BOCCHI THE ROCK!")]
    [InlineData("Baccano!")]
    public void Score_IsExactForOrdinaryMatches(string title)
    {
        // 99 of the 109 resolvable series in the sample library scored exactly 1.0.
        Assert.Equal(1.0, Score(new[] { title, title }, Anime(title)));
    }

    [Fact]
    public void Score_MatchesAgainstSynonyms()
    {
        var media = Anime("Shingeki no Kyojin", "Attack on Titan", "SnK", "AoT");
        Assert.Equal(1.0, Score(new[] { "SnK" }, media));
    }

    [Fact]
    public void Score_WithNoTitles_IsZero()
    {
        Assert.Equal(0.0, Score(Array.Empty<string>(), Anime("Cowboy Bebop")));
        Assert.Equal(0.0, Score(new[] { "Cowboy Bebop" }, Anime(null)));
    }

    [Fact]
    public void GetLocalTitles_UsesTheSeriesNameAndFolder()
    {
        var series = new Series { Name = "The Girlfriend Experience", Path = "/media/tv/Girlfriend, Girlfriend" };
        var titles = AnimeMatcher.GetLocalTitles(series).ToList();

        Assert.Equal(new[] { "The Girlfriend Experience", "Girlfriend, Girlfriend" }, titles);
    }

    [Fact]
    public void GetLocalTitles_ToleratesATrailingSeparatorAndMissingPath()
    {
        Assert.Contains("Bleach", AnimeMatcher.GetLocalTitles(new Series { Name = "x", Path = "/media/tv/Bleach/" }));
        Assert.Equal(new[] { "Bleach" }, AnimeMatcher.GetLocalTitles(new Series { Name = "Bleach" }).ToList());
    }
}
