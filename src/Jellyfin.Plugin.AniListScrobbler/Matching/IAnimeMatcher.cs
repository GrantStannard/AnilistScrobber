using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.AniListScrobbler.Matching;

/// <summary>
/// Resolves a Jellyfin library item to an AniList entry.
/// </summary>
public interface IAnimeMatcher
{
    /// <summary>
    /// Resolves the AniList entry for an episode or film.
    /// </summary>
    /// <param name="item">The Jellyfin item being scrobbled.</param>
    /// <param name="accessToken">An AniList access token, used for the lookup requests.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The match, or <c>null</c> when the item could not be identified.</returns>
    Task<AnimeMatch?> ResolveAsync(BaseItem item, string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the cached match for an item, after the scrobbler followed a sequel chain.
    /// </summary>
    /// <param name="item">The Jellyfin item.</param>
    /// <param name="match">The corrected match.</param>
    void UpdateCache(BaseItem item, AnimeMatch match);
}
