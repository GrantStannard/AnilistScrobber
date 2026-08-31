using System;
using Jellyfin.Plugin.AniListScrobbler.Scrobbling;
using Xunit;

namespace Jellyfin.Plugin.AniListScrobbler.Tests;

public class BackoffTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    public void GetBackoff_GrowsExponentially(int attempt, double expectedSeconds)
    {
        Assert.Equal(expectedSeconds, ScrobbleService.GetBackoff(attempt).TotalSeconds);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(100)]
    public void GetBackoff_IsCapped(int attempt)
    {
        Assert.Equal(60, ScrobbleService.GetBackoff(attempt).TotalSeconds);
    }

    [Fact]
    public void GetBackoff_HandlesNonPositiveAttempts()
    {
        Assert.Equal(5, ScrobbleService.GetBackoff(0).TotalSeconds);
        Assert.True(ScrobbleService.GetBackoff(-1) > TimeSpan.Zero);
    }
}
