using Jellyfin.Plugin.AniListScrobbler.Matching;
using Xunit;

namespace Jellyfin.Plugin.AniListScrobbler.Tests;

public class TitleSimilarityTests
{
    [Theory]
    [InlineData("Fullmetal Alchemist: Brotherhood", "fullmetal alchemist brotherhood")]
    [InlineData("  Steins;Gate  ", "steins gate")]
    [InlineData("Re:ZERO -Starting Life in Another World-", "re zero starting life in another world")]
    [InlineData("Kaguya-sama: Love Is War?", "kaguya sama love is war")]
    [InlineData("Pokémon", "pokemon")]
    [InlineData(null, "")]
    public void Normalize_StripsPunctuationCaseAndAccents(string? input, string expected)
    {
        Assert.Equal(expected, TitleSimilarity.Normalize(input));
    }

    [Fact]
    public void Compare_IdenticalAfterNormalisation_IsExact()
    {
        Assert.Equal(1.0, TitleSimilarity.Compare("Steins;Gate", "Steins Gate"));
    }

    [Fact]
    public void Compare_PunctuationOnlyDifferences_ScoreHigh()
    {
        Assert.True(TitleSimilarity.Compare("Kaguya-sama: Love Is War", "Kaguya-sama wa Kokurasetai") < 0.85);
        Assert.True(TitleSimilarity.Compare("Attack on Titan", "Attack on Titan!") > 0.95);
    }

    [Fact]
    public void Compare_UnrelatedTitles_ScoreLow()
    {
        Assert.True(TitleSimilarity.Compare("Cowboy Bebop", "Neon Genesis Evangelion") < 0.4);
    }

    [Fact]
    public void Compare_EmptyInput_IsZero()
    {
        Assert.Equal(0.0, TitleSimilarity.Compare("", "Bleach"));
        Assert.Equal(0.0, TitleSimilarity.Compare("Bleach", null));
    }

    [Fact]
    public void Compare_IsSymmetric()
    {
        var forward = TitleSimilarity.Compare("Mushishi", "Mushi-Shi");
        var backward = TitleSimilarity.Compare("Mushi-Shi", "Mushishi");
        Assert.Equal(forward, backward, 10);
    }
}
