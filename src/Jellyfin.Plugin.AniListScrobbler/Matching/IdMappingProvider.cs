using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniListScrobbler.Matching;

/// <summary>
/// Maps AniDB and MyAnimeList ids onto AniList ids using the community maintained
/// Fribb/anime-lists dataset. Jellyfin's AniDB metadata provider and Shoko both stamp items
/// with AniDB ids, which AniList's API cannot resolve on its own, so without this table those
/// libraries cannot be scrobbled at all.
///
/// The dataset is downloaded once, cached on disk, and refreshed on the configured interval.
/// </summary>
public sealed class IdMappingProvider : IIdMappingProvider, IDisposable
{
    private const string DatasetUrl =
        "https://raw.githubusercontent.com/Fribb/anime-lists/master/anime-list-full.json";

    private const string CacheFileName = "anilist-scrobbler-id-map.json";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IdMappingProvider> _logger;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    private Dictionary<int, int>? _aniDbToAniList;
    private Dictionary<int, int>? _malToAniList;
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdMappingProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public IdMappingProvider(IHttpClientFactory httpClientFactory, ILogger<IdMappingProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int?> GetAniListIdFromAniDbAsync(int aniDbId, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return _aniDbToAniList is not null && _aniDbToAniList.TryGetValue(aniDbId, out var id) ? id : null;
    }

    /// <inheritdoc />
    public async Task<int?> GetAniListIdFromMalAsync(int malId, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return _malToAniList is not null && _malToAniList.TryGetValue(malId, out var id) ? id : null;
    }

    /// <inheritdoc />
    public void Dispose() => _loadGate.Dispose();

    private static string CachePath =>
        Path.Combine(Plugin.Instance?.DataFolderPath ?? Path.GetTempPath(), CacheFileName);

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableIdMappingDatabase)
        {
            return;
        }

        var maxAge = TimeSpan.FromDays(Math.Max(1, config.IdMappingRefreshDays));
        if (_aniDbToAniList is not null && DateTimeOffset.UtcNow - _loadedAt < maxAge)
        {
            return;
        }

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_aniDbToAniList is not null && DateTimeOffset.UtcNow - _loadedAt < maxAge)
            {
                return;
            }

            var path = CachePath;
            var cacheIsFresh = File.Exists(path)
                && DateTimeOffset.UtcNow - new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) < maxAge;

            if (!cacheIsFresh)
            {
                await DownloadAsync(path, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(path))
            {
                Parse(path);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not load the AniDB/MAL id mapping database");

            // Leave whatever was already loaded in place, and do not hammer the source on
            // every lookup after a failure.
            _loadedAt = DateTimeOffset.UtcNow;
            _aniDbToAniList ??= new Dictionary<int, int>();
            _malToAniList ??= new Dictionary<int, int>();
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task DownloadAsync(string path, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading the AniDB/MAL to AniList id mapping database");

        var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);
        using var response = await httpClient
            .GetAsync(DatasetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var destination = File.Create(temporaryPath))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private void Parse(string path)
    {
        var aniDb = new Dictionary<int, int>();
        var mal = new Dictionary<int, int>();

        using (var stream = File.OpenRead(path))
        {
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("The id mapping dataset was not a JSON array.");
            }

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (!TryReadId(entry, "anilist_id", out var aniListId))
                {
                    continue;
                }

                // The dataset holds one row per AniList entry, so first write wins and later
                // duplicates (alternate cuts of the same show) are ignored.
                if (TryReadId(entry, "anidb_id", out var aniDbId))
                {
                    aniDb.TryAdd(aniDbId, aniListId);
                }

                if (TryReadId(entry, "mal_id", out var malId))
                {
                    mal.TryAdd(malId, aniListId);
                }
            }
        }

        _aniDbToAniList = aniDb;
        _malToAniList = mal;
        _loadedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Loaded id mappings: {AniDbCount} AniDB and {MalCount} MyAnimeList entries",
            aniDb.Count,
            mal.Count);
    }

    private static bool TryReadId(JsonElement entry, string property, out int value)
    {
        value = 0;

        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty(property, out var element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(
                element.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value),
            _ => false,
        };
    }
}
