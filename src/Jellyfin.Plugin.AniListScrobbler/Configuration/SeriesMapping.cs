using System;

namespace Jellyfin.Plugin.AniListScrobbler.Configuration;

/// <summary>
/// A manual override pinning a Jellyfin series or season to a specific AniList entry.
/// Useful when automatic matching picks the wrong season of a long-running show.
/// </summary>
public class SeriesMapping
{
    /// <summary>
    /// Gets or sets the Jellyfin series or season id this mapping applies to.
    /// </summary>
    public Guid JellyfinItemId { get; set; }

    /// <summary>
    /// Gets or sets a human readable label, shown on the configuration page.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the AniList media id to scrobble against.
    /// </summary>
    public int AniListMediaId { get; set; }

    /// <summary>
    /// Gets or sets the number subtracted from the Jellyfin episode number to produce the
    /// AniList progress value. Use this for libraries numbered absolutely across seasons:
    /// a season starting at episode 26 that is episode 1 on AniList needs an offset of 25.
    /// </summary>
    public int EpisodeOffset { get; set; }
}
