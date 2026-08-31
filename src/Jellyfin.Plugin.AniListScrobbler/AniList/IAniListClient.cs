using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AniListScrobbler.AniList.Models;

namespace Jellyfin.Plugin.AniListScrobbler.AniList;

/// <summary>
/// A thin typed client over the AniList GraphQL API.
/// </summary>
public interface IAniListClient
{
    /// <summary>
    /// Resolves the account behind an access token.
    /// </summary>
    /// <param name="accessToken">The AniList access token.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The account, or <c>null</c> when the token is not valid.</returns>
    Task<Viewer?> GetViewerAsync(string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Looks a media up by its AniList id.
    /// </summary>
    /// <param name="mediaId">The AniList media id.</param>
    /// <param name="accessToken">The access token, so the viewer's list entry is included.</param>
    /// <param name="includeRelations">Whether to fetch the relation graph as well.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The media, or <c>null</c> when it does not exist.</returns>
    Task<Media?> GetMediaAsync(int mediaId, string accessToken, bool includeRelations, CancellationToken cancellationToken);

    /// <summary>
    /// Looks a media up by its MyAnimeList id.
    /// </summary>
    /// <param name="malId">The MyAnimeList id.</param>
    /// <param name="accessToken">The access token, so the viewer's list entry is included.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The media, or <c>null</c> when AniList knows no such mapping.</returns>
    Task<Media?> GetMediaByMalIdAsync(int malId, string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Searches anime by title.
    /// </summary>
    /// <param name="search">The search term.</param>
    /// <param name="accessToken">The access token, so viewer list entries are included.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The candidate media, best match first.</returns>
    Task<IReadOnlyList<Media>> SearchAsync(string search, string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates the viewer's list entry for a media.
    /// </summary>
    /// <param name="mediaId">The AniList media id.</param>
    /// <param name="progress">The number of episodes watched.</param>
    /// <param name="status">The list status to set.</param>
    /// <param name="repeat">The rewatch count to set, when it changed.</param>
    /// <param name="accessToken">The access token.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The stored entry.</returns>
    Task<SaveMediaListEntryResult?> SaveEntryAsync(
        int mediaId,
        int progress,
        MediaListStatus status,
        int? repeat,
        string accessToken,
        CancellationToken cancellationToken);
}
