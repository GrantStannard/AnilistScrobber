using System.IO;
using System.Text;
using Jellyfin.Plugin.AniListScrobbler.Matching;
using Xunit;

namespace Jellyfin.Plugin.AniListScrobbler.Tests;

/// <summary>
/// The field names and shapes here are taken verbatim from Fribb/anime-lists. A rename
/// upstream would otherwise show up only as every lookup silently missing.
/// </summary>
public class IdMappingTests
{
    private static IdMappingProvider.Indexes Build(string json)
        => IdMappingProvider.BuildIndexes(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    private const string Sample = """
    [
      { "type": "TV", "anidb_id": 1, "anilist_id": 290, "mal_id": 290,
        "tvdb_id": 72025, "season": { "tvdb": 1, "tmdb": 1 } },
      { "type": "TV", "anidb_id": 13871, "anilist_id": 101280, "mal_id": 37430,
        "tvdb_id": 352408, "season": { "tvdb": 1 } },
      { "type": "TV", "anidb_id": 14571, "anilist_id": 116742, "mal_id": 41487,
        "tvdb_id": 352408, "season": { "tvdb": 2 } },
      { "type": "TV", "anidb_id": 14570, "anilist_id": 108511, "mal_id": 39551,
        "tvdb_id": 352408, "season": { "tvdb": 2 } },
      { "type": "MOVIE", "anilist_id": 999, "tvdb_id": 352408, "season": { "tvdb": 0 } },
      { "type": "TV", "anidb_id": 77, "mal_id": 77 },
      { "type": "TV", "anidb_id": 88, "anilist_id": 555, "tvdb_id": 4242, "season": { "tvdb": null } }
    ]
    """;

    [Fact]
    public void BuildIndexes_MapsAniDbAndMal()
    {
        var indexes = Build(Sample);

        Assert.Equal(290, indexes.AniDbToAniList[1]);
        Assert.Equal(101280, indexes.AniDbToAniList[13871]);
        Assert.Equal(101280, indexes.MalToAniList[37430]);
    }

    [Fact]
    public void BuildIndexes_SkipsRowsWithNoAniListId()
    {
        var indexes = Build(Sample);

        Assert.False(indexes.AniDbToAniList.ContainsKey(77));
        Assert.False(indexes.MalToAniList.ContainsKey(77));
    }

    [Fact]
    public void BuildIndexes_MapsTvdbSeasonToEveryCour()
    {
        // A season split across two cours maps to two AniList entries, ordered so the first
        // half comes first: without that the overflow walk would start from the wrong half.
        var indexes = Build(Sample);
        var season2 = indexes.TvdbSeasonToAniList[(352408, 2)];

        Assert.Equal(new[] { 108511, 116742 }, season2);
        Assert.Equal(new[] { 101280 }, indexes.TvdbSeasonToAniList[(352408, 1)]);
    }

    [Fact]
    public void BuildIndexes_KeepsSpecialsSeasonSeparate()
    {
        // Season 0 is specials; it must not leak into season 1's list.
        var indexes = Build(Sample);

        Assert.Equal(new[] { 999 }, indexes.TvdbSeasonToAniList[(352408, 0)]);
    }

    [Fact]
    public void BuildIndexes_IgnoresARowWithNoSeasonNumber()
    {
        var indexes = Build(Sample);

        Assert.False(indexes.TvdbSeasonToAniList.ContainsKey((4242, 0)));
        Assert.Equal(555, indexes.AniDbToAniList[88]);
    }

    [Fact]
    public void BuildIndexes_AcceptsIdsWrittenAsStrings()
    {
        var indexes = Build("""[{ "anidb_id": "5", "anilist_id": "6", "tvdb_id": "7", "season": { "tvdb": "2" } }]""");

        Assert.Equal(6, indexes.AniDbToAniList[5]);
        Assert.Equal(new[] { 6 }, indexes.TvdbSeasonToAniList[(7, 2)]);
    }

    [Fact]
    public void BuildIndexes_RejectsANonArrayDataset()
    {
        Assert.Throws<InvalidDataException>(() => Build("""{ "not": "an array" }"""));
    }

    [Fact]
    public void BuildIndexes_OnAnEmptyDataset_IsEmpty()
    {
        var indexes = Build("[]");

        Assert.Empty(indexes.AniDbToAniList);
        Assert.Empty(indexes.TvdbSeasonToAniList);
    }
}
