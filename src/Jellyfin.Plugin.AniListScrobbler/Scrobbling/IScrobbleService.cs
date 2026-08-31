using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.AniListScrobbler.Scrobbling;

/// <summary>
/// Pushes watch progress to AniList.
/// </summary>
public interface IScrobbleService
{
    /// <summary>
    /// Records an item as watched by a user on AniList.
    /// </summary>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <param name="item">The watched episode or film.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What happened.</returns>
    Task<ScrobbleOutcome> ScrobbleAsync(Guid userId, BaseItem item, CancellationToken cancellationToken);
}
