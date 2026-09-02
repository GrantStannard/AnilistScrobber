using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AniListScrobbler.AniList.Models;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniListScrobbler.AniList;

/// <summary>
/// A typed client over the AniList GraphQL API, rate limited and with 429 back-off.
/// </summary>
public sealed class AniListClient : IAniListClient, IDisposable
{
    private const string Endpoint = "https://graphql.anilist.co";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AniListClient> _logger;
    private readonly RateLimiter _rateLimiter;

    /// <summary>
    /// Initializes a new instance of the <see cref="AniListClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public AniListClient(IHttpClientFactory httpClientFactory, ILogger<AniListClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _rateLimiter = new RateLimiter(Plugin.Instance?.Configuration.RequestsPerMinute ?? 30);
    }

    /// <inheritdoc />
    public async Task<Viewer?> GetViewerAsync(string accessToken, CancellationToken cancellationToken)
    {
        var data = await SendAsync(Queries.Viewer, null, accessToken, cancellationToken).ConfigureAwait(false);
        return Deserialize<Viewer>(data, "Viewer");
    }

    /// <inheritdoc />
    public async Task<Media?> GetMediaAsync(int mediaId, string accessToken, bool includeRelations, CancellationToken cancellationToken)
    {
        var query = includeRelations ? Queries.MediaWithRelations : Queries.MediaById;
        var data = await SendAsync(query, new { id = mediaId }, accessToken, cancellationToken).ConfigureAwait(false);
        return Deserialize<Media>(data, "Media");
    }

    /// <inheritdoc />
    public async Task<Media?> GetMediaByMalIdAsync(int malId, string accessToken, CancellationToken cancellationToken)
    {
        var data = await SendAsync(Queries.MediaByMalId, new { idMal = malId }, accessToken, cancellationToken).ConfigureAwait(false);
        return Deserialize<Media>(data, "Media");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Media>> SearchAsync(string search, string accessToken, CancellationToken cancellationToken)
    {
        var data = await SendAsync(Queries.SearchMedia, new { search, perPage = 10 }, accessToken, cancellationToken).ConfigureAwait(false);

        if (data is null
            || !data.Value.TryGetProperty("Page", out var page)
            || !page.TryGetProperty("media", out var media)
            || media.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<Media>();
        }

        return media.Deserialize<List<Media>>(_jsonOptions) ?? (IReadOnlyList<Media>)Array.Empty<Media>();
    }

    /// <inheritdoc />
    public async Task<SaveMediaListEntryResult?> SaveEntryAsync(
        int mediaId,
        int progress,
        MediaListStatus status,
        int? repeat,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var variables = new
        {
            mediaId,
            progress,
            status = status.ToString(),
            repeat,
        };

        var data = await SendAsync(Queries.SaveMediaListEntry, variables, accessToken, cancellationToken).ConfigureAwait(false);
        return Deserialize<SaveMediaListEntryResult>(data, "SaveMediaListEntry");
    }

    /// <inheritdoc />
    public void Dispose() => _rateLimiter.Dispose();

    /// <summary>
    /// Reads the Retry-After header, which AniList sends either as a delay or as a date,
    /// falling back to a full window when it is missing or already in the past.
    /// </summary>
    private static TimeSpan GetRetryAfter(HttpResponseMessage response)
    {
        var fallback = TimeSpan.FromSeconds(60);
        var header = response.Headers.RetryAfter;

        if (header is null)
        {
            return fallback;
        }

        var delay = header.Delta ?? (header.Date.HasValue ? header.Date.Value - DateTimeOffset.UtcNow : (TimeSpan?)null);

        return delay is { } value && value > TimeSpan.Zero ? value : fallback;
    }

    /// <summary>
    /// Whether a status code describes a condition that may clear on its own. Forbidden is
    /// included because AniList uses it to report the whole API being disabled, not just
    /// permission problems; an actual bad token is recognised from the error payload instead.
    /// </summary>
    /// <param name="status">The response status.</param>
    /// <returns><c>true</c> when a later retry may succeed.</returns>
    private static bool IsTransientStatus(HttpStatusCode status)
        => (int)status >= 500
            || status == HttpStatusCode.Forbidden
            || status == HttpStatusCode.RequestTimeout;

    private static T? Deserialize<T>(JsonElement? data, string property)
        where T : class
    {
        if (data is null
            || !data.Value.TryGetProperty(property, out var element)
            || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return element.Deserialize<T>(_jsonOptions);
    }

    private async Task<JsonElement?> SendAsync(
        string query,
        object? variables,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var configured = Plugin.Instance?.Configuration.RequestsPerMinute;
        if (configured.HasValue)
        {
            _rateLimiter.PermitsPerWindow = configured.Value;
        }

        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { query, variables }, options: _jsonOptions),
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AniListException("Could not reach AniList.", ex) { IsTransient = true };
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = GetRetryAfter(response);

                _rateLimiter.PauseFor(retryAfter);
                _logger.LogWarning("AniList rate limit hit, backing off for {Seconds}s", retryAfter.TotalSeconds);

                throw new AniListException(
                    string.Create(CultureInfo.InvariantCulture, $"Rate limited by AniList; retry in {retryAfter.TotalSeconds:F0}s."))
                {
                    IsTransient = true,
                };
            }

            // 401 is unambiguous. 403 is not: AniList answers with one, plus an explanatory
            // errors payload, when the API is disabled during an outage. Deciding that from
            // the status alone would report a perfectly good token as rejected and, because
            // an authentication failure is not retried, give up for the whole outage.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new AniListException("AniList rejected the access token.") { IsAuthenticationFailure = true };
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(body))
            {
                throw new AniListException(
                    string.Create(CultureInfo.InvariantCulture, $"AniList returned {(int)response.StatusCode}."))
                {
                    IsTransient = IsTransientStatus(response.StatusCode),
                };
            }

            GraphQlResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<GraphQlResponse>(body, _jsonOptions);
            }
            catch (JsonException ex)
            {
                throw new AniListException("AniList returned a response that could not be parsed.", ex)
                {
                    IsTransient = true,
                };
            }

            if (parsed?.Errors is { Count: > 0 } errors)
            {
                var message = string.Join("; ", errors.Select(error => error.Message));

                // AniList answers 404-with-errors for "not found" style queries; that is a
                // legitimate empty result rather than a failure.
                if ((int)response.StatusCode == 404)
                {
                    _logger.LogDebug("AniList reported not found: {Message}", message);
                    return null;
                }

                var isAuthenticationFailure =
                    message.Contains("Invalid token", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase);

                throw new AniListException($"AniList error: {message}")
                {
                    IsTransient = !isAuthenticationFailure && IsTransientStatus(response.StatusCode),
                    IsAuthenticationFailure = isAuthenticationFailure,
                };
            }

            return parsed?.Data;
        }
    }

    private sealed class GraphQlResponse
    {
        [JsonPropertyName("data")]
        public JsonElement? Data { get; set; }

        [JsonPropertyName("errors")]
        public IReadOnlyList<GraphQlError>? Errors { get; set; }
    }

    private sealed class GraphQlError
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }
}
