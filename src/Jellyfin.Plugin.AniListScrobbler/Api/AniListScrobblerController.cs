using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AniListScrobbler.AniList;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniListScrobbler.Api;

/// <summary>
/// Endpoints backing the plugin configuration page.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("AniListScrobbler")]
[Produces("application/json")]
public class AniListScrobblerController : ControllerBase
{
    private const string TokenEndpoint = "https://anilist.co/api/v2/oauth/token";

    private readonly IAniListClient _aniListClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AniListScrobblerController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AniListScrobblerController"/> class.
    /// </summary>
    /// <param name="aniListClient">Instance of the <see cref="IAniListClient"/> interface.</param>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public AniListScrobblerController(
        IAniListClient aniListClient,
        IHttpClientFactory httpClientFactory,
        ILogger<AniListScrobblerController> logger)
    {
        _aniListClient = aniListClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Checks an access token and returns the AniList account it belongs to.
    /// </summary>
    /// <param name="request">The token to check.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The AniList account.</returns>
    /// <response code="200">The token is valid.</response>
    /// <response code="400">The token was rejected by AniList.</response>
    [HttpPost("ValidateToken")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ValidateTokenResponse>> ValidateToken(
        [FromBody] ValidateTokenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return BadRequest("An access token is required.");
        }

        try
        {
            var viewer = await _aniListClient
                .GetViewerAsync(request.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            if (viewer is null)
            {
                return BadRequest("AniList did not recognise that token.");
            }

            return new ValidateTokenResponse { UserName = viewer.Name, UserId = viewer.Id };
        }
        catch (AniListException ex)
        {
            _logger.LogWarning(ex, "AniList token validation failed");
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Exchanges an OAuth authorization code for a long lived access token.
    /// </summary>
    /// <param name="request">The authorization code.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The access token.</returns>
    /// <response code="200">The code was exchanged.</response>
    /// <response code="400">The code or the configured client credentials were rejected.</response>
    [HttpPost("ExchangeCode")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ExchangeCodeResponse>> ExchangeCode(
        [FromBody] ExchangeCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null)
        {
            return BadRequest("The plugin is not loaded.");
        }

        if (string.IsNullOrWhiteSpace(configuration.ClientId)
            || string.IsNullOrWhiteSpace(configuration.ClientSecret))
        {
            return BadRequest("Set the AniList client id and secret before exchanging a code.");
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest("An authorization code is required.");
        }

        var payload = new
        {
            grant_type = "authorization_code",
            client_id = configuration.ClientId,
            client_secret = configuration.ClientSecret,
            redirect_uri = request.RedirectUri,
            code = request.Code,
        };

        var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);

        using var response = await httpClient
            .PostAsJsonAsync(TokenEndpoint, payload, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("AniList refused the authorization code: {Status} {Body}", response.StatusCode, body);
            return BadRequest("AniList refused the authorization code. Check the client credentials and redirect URI.");
        }

        var token = await response.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken)
            .ConfigureAwait(false);

        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            return BadRequest("AniList returned no access token.");
        }

        return new ExchangeCodeResponse { AccessToken = token.AccessToken };
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
    }
}
