using System.Collections.Generic;
using Jellyfin.Plugin.AniListScrobbler.Matching;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.AniListScrobbler.Tests;

public class ProviderIdTests
{
    private static readonly string[] _aniListKeys = { "AniList", "Anilist", "AniListId" };

    private static Series SeriesWith(params (string Key, string Value)[] providerIds)
    {
        var series = new Series { ProviderIds = new Dictionary<string, string>() };
        foreach (var (key, value) in providerIds)
        {
            series.ProviderIds[key] = value;
        }

        return series;
    }

    [Fact]
    public void GetProviderId_MatchesCaseInsensitively()
    {
        // Different metadata plugins spell the same provider differently.
        Assert.Equal(21, AnimeMatcher.GetProviderId(SeriesWith(("anilist", "21")), _aniListKeys));
        Assert.Equal(21, AnimeMatcher.GetProviderId(SeriesWith(("ANILIST", "21")), _aniListKeys));
        Assert.Equal(21, AnimeMatcher.GetProviderId(SeriesWith(("AniListId", "21")), _aniListKeys));
    }

    [Fact]
    public void GetProviderId_IgnoresOtherProviders()
    {
        Assert.Null(AnimeMatcher.GetProviderId(SeriesWith(("Tvdb", "81797")), _aniListKeys));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-3")]
    public void GetProviderId_RejectsUnusableValues(string value)
    {
        Assert.Null(AnimeMatcher.GetProviderId(SeriesWith(("AniList", value)), _aniListKeys));
    }

    [Fact]
    public void GetProviderId_WithNoItemOrNoIds_ReturnsNull()
    {
        Assert.Null(AnimeMatcher.GetProviderId(null, _aniListKeys));
        Assert.Null(AnimeMatcher.GetProviderId(SeriesWith(), _aniListKeys));
    }
}
