using System;
using Jellyfin.Plugin.AniListScrobbler.Configuration;
using Xunit;

namespace Jellyfin.Plugin.AniListScrobbler.Tests;

public class PluginConfigurationTests
{
    private static readonly Guid _seriesId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _seasonId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _userId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void GetUser_ReturnsTheMatchingUser()
    {
        var configuration = new PluginConfiguration
        {
            UserConfigurations = new[]
            {
                new UserConfiguration { JellyfinUserId = Guid.NewGuid(), AniListUserName = "other" },
                new UserConfiguration { JellyfinUserId = _userId, AniListUserName = "mine" },
            },
        };

        Assert.Equal("mine", configuration.GetUser(_userId)?.AniListUserName);
        Assert.Null(configuration.GetUser(Guid.NewGuid()));
    }

    [Fact]
    public void GetMapping_PrefersTheSeasonOverTheSeries()
    {
        var configuration = new PluginConfiguration
        {
            Mappings = new[]
            {
                new SeriesMapping { JellyfinItemId = _seriesId, AniListMediaId = 100 },
                new SeriesMapping { JellyfinItemId = _seasonId, AniListMediaId = 200 },
            },
        };

        Assert.Equal(200, configuration.GetMapping(_seasonId, _seriesId)?.AniListMediaId);
    }

    [Fact]
    public void GetMapping_FallsBackToTheSeries()
    {
        var configuration = new PluginConfiguration
        {
            Mappings = new[] { new SeriesMapping { JellyfinItemId = _seriesId, AniListMediaId = 100 } },
        };

        Assert.Equal(100, configuration.GetMapping(_seasonId, _seriesId)?.AniListMediaId);
        Assert.Equal(100, configuration.GetMapping(null, _seriesId)?.AniListMediaId);
        Assert.Equal(100, configuration.GetMapping(Guid.Empty, _seriesId)?.AniListMediaId);
    }

    [Fact]
    public void GetMapping_WithNoMatch_ReturnsNull()
    {
        Assert.Null(new PluginConfiguration().GetMapping(_seasonId, _seriesId));
    }

    [Fact]
    public void UserConfiguration_IsLinked_TracksTheToken()
    {
        Assert.False(new UserConfiguration().IsLinked);
        Assert.False(new UserConfiguration { AccessToken = "   " }.IsLinked);
        Assert.True(new UserConfiguration { AccessToken = "token" }.IsLinked);
    }
}
