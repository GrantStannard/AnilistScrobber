using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AniListScrobbler.Configuration;

/// <summary>
/// Plugin-wide configuration, persisted by Jellyfin as XML.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the AniList OAuth client id used to build the authorization link on the
    /// configuration page. Optional: users may paste a token obtained by any means.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the AniList OAuth client secret. Only required when exchanging an
    /// authorization code (PIN) for a token; not needed for the implicit grant flow.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the per-Jellyfin-user settings.
    /// </summary>
    public UserConfiguration[] UserConfigurations { get; set; } = Array.Empty<UserConfiguration>();

    /// <summary>
    /// Gets or sets manual overrides mapping a Jellyfin series or season to an AniList media id.
    /// </summary>
    public SeriesMapping[] Mappings { get; set; } = Array.Empty<SeriesMapping>();

    /// <summary>
    /// Gets or sets the maximum number of AniList requests per minute. AniList's documented
    /// budget is 90/min but it is frequently degraded to 30/min, which is the safe default.
    /// </summary>
    public int RequestsPerMinute { get; set; } = 30;

    /// <summary>
    /// Gets or sets the number of times a failed scrobble is retried before being dropped.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets a value indicating whether the AniDB/MAL to AniList id mapping database
    /// (Fribb/anime-lists) may be downloaded and cached. Required to scrobble libraries whose
    /// metadata comes from the AniDB provider or Shoko.
    /// </summary>
    public bool EnableIdMappingDatabase { get; set; } = true;

    /// <summary>
    /// Gets or sets how many days the cached id mapping database is used before being refreshed.
    /// </summary>
    public int IdMappingRefreshDays { get; set; } = 7;

    /// <summary>
    /// Gets or sets a value indicating whether a match found from a provider id is checked
    /// against the item's title before anything is written to AniList.
    ///
    /// Provider ids are not always trustworthy: a library can carry an AniDB id belonging to
    /// a completely different show, or one pointing at a special or film rather than the
    /// series. Without this check those ids scrobble silently onto the wrong AniList entry.
    /// Manual mappings are always trusted and never verified.
    /// </summary>
    public bool VerifyTitleMatch { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum similarity (0-1) between the item's title and the AniList
    /// entry's titles for a provider id match to be accepted.
    /// </summary>
    public double TitleVerificationMinimumSimilarity { get; set; } = 0.80;

    /// <summary>
    /// Gets or sets a value indicating whether titles may be searched on AniList when no
    /// provider id yields a match. Title matching is a heuristic and can mis-identify shows.
    /// </summary>
    public bool EnableTitleSearchFallback { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum title similarity (0-1) accepted by the title search fallback.
    /// </summary>
    public double TitleSearchMinimumSimilarity { get; set; } = 0.85;

    /// <summary>
    /// Gets or sets a value indicating whether every scrobble decision is logged at
    /// information level instead of debug.
    /// </summary>
    public bool VerboseLogging { get; set; }

    /// <summary>
    /// Returns the configuration for a Jellyfin user, or <c>null</c> when the user has not
    /// linked an AniList account.
    /// </summary>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <returns>The matching user configuration, or <c>null</c>.</returns>
    public UserConfiguration? GetUser(Guid userId)
        => Array.Find(UserConfigurations, u => u.JellyfinUserId.Equals(userId));

    /// <summary>
    /// Returns the manual mapping for a season id or series id, preferring the season.
    /// </summary>
    /// <param name="seasonId">The Jellyfin season id, if any.</param>
    /// <param name="seriesId">The Jellyfin series id.</param>
    /// <returns>The matching mapping, or <c>null</c>.</returns>
    public SeriesMapping? GetMapping(Guid? seasonId, Guid seriesId)
    {
        if (seasonId.HasValue && !seasonId.Value.Equals(Guid.Empty))
        {
            var bySeason = Array.Find(Mappings, m => m.JellyfinItemId.Equals(seasonId.Value));
            if (bySeason is not null)
            {
                return bySeason;
            }
        }

        return Array.Find(Mappings, m => m.JellyfinItemId.Equals(seriesId));
    }
}
