using System;

namespace Jellyfin.Plugin.AniListScrobbler.AniList;

/// <summary>
/// Raised when AniList rejects a request or answers with an error payload.
/// </summary>
public class AniListException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AniListException"/> class.
    /// </summary>
    public AniListException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AniListException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AniListException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AniListException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The cause.</param>
    public AniListException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets or sets a value indicating whether retrying the request later may succeed.
    /// </summary>
    public bool IsTransient { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the access token was rejected.
    /// </summary>
    public bool IsAuthenticationFailure { get; set; }
}
