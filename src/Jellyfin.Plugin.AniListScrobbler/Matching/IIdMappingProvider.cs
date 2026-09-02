using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AniListScrobbler.Matching;

/// <summary>
/// Translates ids from other anime databases into AniList media ids.
/// </summary>
public interface IIdMappingProvider
{
    /// <summary>
    /// Maps an AniDB anime id to an AniList media id.
    /// </summary>
    /// <param name="aniDbId">The AniDB id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The AniList media id, or <c>null</c> when unknown.</returns>
    Task<int?> GetAniListIdFromAniDbAsync(int aniDbId, CancellationToken cancellationToken);

    /// <summary>
    /// Maps a TheTVDB series id and season number onto the AniList entries covering that
    /// season, in broadcast order. A season split across two cours maps to two entries.
    /// </summary>
    /// <param name="tvdbId">The TheTVDB series id.</param>
    /// <param name="seasonNumber">The season number as TheTVDB numbers it.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The AniList media ids, or an empty list when unknown.</returns>
    Task<IReadOnlyList<int>> GetAniListIdsFromTvdbSeasonAsync(
        int tvdbId,
        int seasonNumber,
        CancellationToken cancellationToken);

    /// <summary>
    /// Maps a MyAnimeList id to an AniList media id.
    /// </summary>
    /// <param name="malId">The MyAnimeList id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The AniList media id, or <c>null</c> when unknown.</returns>
    Task<int?> GetAniListIdFromMalAsync(int malId, CancellationToken cancellationToken);
}
