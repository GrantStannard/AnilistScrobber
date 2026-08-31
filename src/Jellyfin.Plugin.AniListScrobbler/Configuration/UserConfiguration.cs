using System;

namespace Jellyfin.Plugin.AniListScrobbler.Configuration;

/// <summary>
/// Per-Jellyfin-user AniList link and scrobbling preferences.
/// </summary>
public class UserConfiguration
{
    /// <summary>
    /// Gets or sets the Jellyfin user this configuration belongs to.
    /// </summary>
    public Guid JellyfinUserId { get; set; }

    /// <summary>
    /// Gets or sets the AniList OAuth access token.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the AniList account name, resolved when the token is validated.
    /// </summary>
    public string AniListUserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the numeric AniList user id, resolved when the token is validated.
    /// </summary>
    public int AniListUserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether playback by this user is scrobbled.
    /// </summary>
    public bool ScrobbleEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the percentage of an episode that must be played before it counts as
    /// watched.
    /// </summary>
    public int MinimumPlayedPercentage { get; set; } = 85;

    /// <summary>
    /// Gets or sets a value indicating whether marking an item played in the Jellyfin UI
    /// scrobbles it, in addition to actual playback.
    /// </summary>
    public bool ScrobbleOnManualMarkPlayed { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether anime films are scrobbled.
    /// </summary>
    public bool ScrobbleMovies { get; set; } = true;

    /// <summary>
    /// Gets or sets the library ids that are scrobbled. Empty means every library.
    /// </summary>
    public Guid[] LibraryIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// Gets a value indicating whether this user has a usable AniList link.
    /// </summary>
    public bool IsLinked => !string.IsNullOrWhiteSpace(AccessToken);
}
