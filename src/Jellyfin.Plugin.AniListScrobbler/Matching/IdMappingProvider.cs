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
    private Dictionary<(int TvdbId, int Season), List<int>>? _tvdbSeasonToAniList;
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
    public async Task<IReadOnlyList<int>> GetAniListIdsFromTvdbSeasonAsync(
        int tvdbId,
        int seasonNumber,
        CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        return _tvdbSeasonToAniList is not null
            && _tvdbSeasonToAniList.TryGetValue((tvdbId, seasonNumber), out var ids)
                ? ids
                : Array.Empty<int>();
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
            _tvdbSeasonToAniList ??= new Dictionary<(int TvdbId, int Season), List<int>>();
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

    /// <summary>
    /// The three indexes built from the dataset.
    /// </summary>
    /// <param name="AniDbToAniList">AniDB id to AniList id.</param>
    /// <param name="MalToAniList">MyAnimeList id to AniList id.</param>
    /// <param name="TvdbSeasonToAniList">TheTVDB series id and season to AniList ids.</param>
    internal sealed record Indexes(
        Dictionary<int, int> AniDbToAniList,
        Dictionary<int, int> MalToAniList,
        Dictionary<(int TvdbId, int Season), List<int>> TvdbSeasonToAniList);

    /// <summary>
    /// Builds the lookup indexes from the raw dataset.
    /// </summary>
    /// <param name="stream">The dataset JSON.</param>
    /// <returns>The indexes.</returns>
    internal static Indexes BuildIndexes(Stream stream)
    {
        var aniDb = new Dictionary<int, int>();
        var mal = new Dictionary<int, int>();
        var tvdbSeason = new Dictionary<(int TvdbId, int Season), List<int>>();

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

            // The dataset also pins each entry to a TheTVDB series and season, which is the
            // only signal that survives a library whose seasons carry no anime ids.
            if (TryReadId(entry, "tvdb_id", out var tvdbId)
                && entry.TryGetProperty("season", out var season)
                && TryReadId(season, "tvdb", out var seasonNumber))
            {
                if (!tvdbSeason.TryGetValue((tvdbId, seasonNumber), out var list))
                {
                    list = new List<int>();
                    tvdbSeason[(tvdbId, seasonNumber)] = list;
                }

                if (!list.Contains(aniListId))
                {
                    list.Add(aniListId);
                }
            }
        }

        // Ascending AniList id tracks broadcast order closely enough to put a split cour's
        // first half before its second, which is what the overflow walk expects.
        foreach (var list in tvdbSeason.Values)
        {
            list.Sort();
        }

        return new Indexes(aniDb, mal, tvdbSeason);
    }

    private void Parse(string path)
    {
        Indexes indexes;
        using (var stream = File.OpenRead(path))
        {
            indexes = BuildIndexes(stream);
        }

        _aniDbToAniList = indexes.AniDbToAniList;
        _malToAniList = indexes.MalToAniList;
        _tvdbSeasonToAniList = indexes.TvdbSeasonToAniList;
        _loadedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Loaded id mappings: {AniDbCount} AniDB, {MalCount} MyAnimeList and {TvdbCount} TheTVDB season entries",
            indexes.AniDbToAniList.Count,
            indexes.MalToAniList.Count,
            indexes.TvdbSeasonToAniList.Count);
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
