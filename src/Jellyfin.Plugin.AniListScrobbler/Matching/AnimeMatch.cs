namespace Jellyfin.Plugin.AniListScrobbler.Matching;

/// <summary>
/// How an AniList entry was identified, for logging and for deciding how much to trust it.
/// </summary>
public enum MatchSource
{
    /// <summary>A manual mapping configured by an administrator.</summary>
    ManualMapping,

    /// <summary>An AniList id stamped on the item by a metadata provider.</summary>
    AniListProviderId,

    /// <summary>A MyAnimeList id resolved through AniList.</summary>
    MyAnimeListId,

    /// <summary>An AniDB id resolved through the id mapping database.</summary>
    AniDbId,

    /// <summary>A title search against AniList.</summary>
    TitleSearch,
}

/// <summary>
/// The AniList entry a Jellyfin item was matched to.
/// </summary>
/// <param name="MediaId">The AniList media id.</param>
/// <param name="EpisodeOffset">
/// The number subtracted from the Jellyfin episode number to get the AniList progress value.
/// </param>
/// <param name="Source">How the match was made.</param>
public sealed record AnimeMatch(int MediaId, int EpisodeOffset, MatchSource Source);
