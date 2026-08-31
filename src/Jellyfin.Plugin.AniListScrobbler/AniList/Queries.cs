namespace Jellyfin.Plugin.AniListScrobbler.AniList;

/// <summary>
/// The GraphQL documents sent to AniList.
/// </summary>
internal static class Queries
{
    /// <summary>The fields selected for every media lookup.</summary>
    private const string MediaFields = @"
    id
    idMal
    format
    episodes
    seasonYear
    synonyms
    title { romaji english native }
    mediaListEntry { id progress status repeat }";

    /// <summary>Resolves the account behind an access token.</summary>
    public const string Viewer = @"query { Viewer { id name } }";

    /// <summary>Looks a media up by its AniList id.</summary>
    public const string MediaById = @"query ($id: Int) {
  Media(id: $id, type: ANIME) {" + MediaFields + @"
  }
}";

    /// <summary>Looks a media up by its MyAnimeList id.</summary>
    public const string MediaByMalId = @"query ($idMal: Int) {
  Media(idMal: $idMal, type: ANIME) {" + MediaFields + @"
  }
}";

    /// <summary>Looks a media up by AniList id, including its relation graph.</summary>
    public const string MediaWithRelations = @"query ($id: Int) {
  Media(id: $id, type: ANIME) {" + MediaFields + @"
    relations {
      edges {
        relationType
        node { id idMal format episodes seasonYear title { romaji english native } }
      }
    }
  }
}";

    /// <summary>Searches anime by title.</summary>
    public const string SearchMedia = @"query ($search: String, $perPage: Int) {
  Page(page: 1, perPage: $perPage) {
    media(search: $search, type: ANIME, sort: SEARCH_MATCH) {" + MediaFields + @"
    }
  }
}";

    /// <summary>Creates or updates the viewer's list entry for a media.</summary>
    public const string SaveMediaListEntry = @"mutation ($mediaId: Int, $progress: Int, $status: MediaListStatus, $repeat: Int) {
  SaveMediaListEntry(mediaId: $mediaId, progress: $progress, status: $status, repeat: $repeat) {
    id
    progress
    status
  }
}";
}
