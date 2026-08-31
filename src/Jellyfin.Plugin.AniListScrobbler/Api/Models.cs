namespace Jellyfin.Plugin.AniListScrobbler.Api;

/// <summary>
/// Request body for validating an AniList access token.
/// </summary>
public class ValidateTokenRequest
{
    /// <summary>
    /// Gets or sets the AniList access token to check.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;
}

/// <summary>
/// The AniList account behind a validated token.
/// </summary>
public class ValidateTokenResponse
{
    /// <summary>
    /// Gets or sets the AniList account name.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the numeric AniList user id.
    /// </summary>
    public int UserId { get; set; }
}

/// <summary>
/// Request body for exchanging an authorization code for an access token.
/// </summary>
public class ExchangeCodeRequest
{
    /// <summary>
    /// Gets or sets the authorization code copied from AniList.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the redirect URI registered on the AniList client.
    /// </summary>
    public string RedirectUri { get; set; } = "https://anilist.co/api/v2/oauth/pin";
}

/// <summary>
/// The token returned by AniList.
/// </summary>
public class ExchangeCodeResponse
{
    /// <summary>
    /// Gets or sets the access token.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;
}
