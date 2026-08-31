namespace Jellyfin.Plugin.AniListScrobbler.Scrobbling;

/// <summary>
/// Why a scrobble did or did not happen.
/// </summary>
public enum ScrobbleOutcome
{
    /// <summary>AniList was updated.</summary>
    Updated,

    /// <summary>The AniList list was already at or ahead of this episode.</summary>
    AlreadyUpToDate,

    /// <summary>The Jellyfin user has not linked an AniList account, or has scrobbling off.</summary>
    NotEnabled,

    /// <summary>The item is not something this plugin scrobbles.</summary>
    NotApplicable,

    /// <summary>The item could not be matched to an AniList entry.</summary>
    NotMatched,

    /// <summary>The update was attempted but failed.</summary>
    Failed,
}
