using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AniListScrobbler.AniList;
using Jellyfin.Plugin.AniListScrobbler.AniList.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniListScrobbler.Matching;

/// <summary>
/// Resolves Jellyfin items to AniList entries.
///
/// AniList models each season, cour and film as a separate entry, while Jellyfin models a show
/// as one series with numbered seasons. The matcher therefore resolves against the season
/// first and only falls back to the series, walking the AniList sequel chain when a library
/// stores later seasons under a series that is only tagged with its first entry.
/// </summary>
public sealed class AnimeMatcher : IAnimeMatcher
{
    private static readonly string[] _aniListKeys = { "AniList", "Anilist", "AniListId" };
    private static readonly string[] _malKeys = { "MyAnimeList", "Mal", "MyAnimeListId" };
    private static readonly string[] _aniDbKeys = { "AniDB", "Anidb", "AniDB_Series", "AnidbId" };

    private readonly IAniListClient _aniListClient;
    private readonly IIdMappingProvider _idMappingProvider;
    private readonly ILogger<AnimeMatcher> _logger;
    private readonly ConcurrentDictionary<string, AnimeMatch> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimeMatcher"/> class.
    /// </summary>
    /// <param name="aniListClient">Instance of the <see cref="IAniListClient"/> interface.</param>
    /// <param name="idMappingProvider">Instance of the <see cref="IIdMappingProvider"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public AnimeMatcher(
        IAniListClient aniListClient,
        IIdMappingProvider idMappingProvider,
        ILogger<AnimeMatcher> logger)
    {
        _aniListClient = aniListClient;
        _idMappingProvider = idMappingProvider;
        _logger = logger;

        // Editing a manual mapping is how an administrator corrects a bad match, so the
        // cache must not outlive the configuration it was built from.
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged += OnConfigurationChanged;
        }
    }

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration configuration)
    {
        _cache.Clear();
        _logger.LogDebug("Cleared cached AniList matches after a configuration change");
    }

    /// <inheritdoc />
    public async Task<AnimeMatch?> ResolveAsync(BaseItem item, string accessToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var key = GetCacheKey(item);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var match = item is Episode episode
            ? await ResolveEpisodeAsync(episode, accessToken, cancellationToken).ConfigureAwait(false)
            : await ResolveStandaloneAsync(item, accessToken, cancellationToken).ConfigureAwait(false);

        if (match is not null)
        {
            _cache[key] = match;
        }

        return match;
    }

    /// <inheritdoc />
    public void UpdateCache(BaseItem item, AnimeMatch match)
    {
        ArgumentNullException.ThrowIfNull(item);
        _cache[GetCacheKey(item)] = match;
    }

    /// <summary>
    /// Reads a provider id from an item under any of the given key spellings.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="keys">The accepted provider key spellings.</param>
    /// <returns>The parsed id, or <c>null</c>.</returns>
    internal static int? GetProviderId(BaseItem? item, IReadOnlyList<string> keys)
    {
        if (item?.ProviderIds is not { Count: > 0 } providerIds)
        {
            return null;
        }

        foreach (var pair in providerIds)
        {
            foreach (var key in keys)
            {
                if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    && value > 0)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string GetCacheKey(BaseItem item)
    {
        if (item is Episode episode)
        {
            var seasonId = episode.SeasonId.Equals(default) ? episode.SeriesId : episode.SeasonId;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"ep:{seasonId:N}:{episode.ParentIndexNumber ?? episode.AiredSeasonNumber ?? 1}");
        }

        return string.Create(CultureInfo.InvariantCulture, $"item:{item.Id:N}");
    }

    private async Task<AnimeMatch?> ResolveEpisodeAsync(
        Episode episode,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var season = episode.Season;
        var series = episode.Series;
        var seasonNumber = episode.ParentIndexNumber ?? episode.AiredSeasonNumber ?? 1;

        var configuration = Plugin.Instance?.Configuration;
        var mapping = configuration?.GetMapping(episode.SeasonId, episode.SeriesId);
        if (mapping is not null && mapping.AniListMediaId > 0)
        {
            return new AnimeMatch(mapping.AniListMediaId, mapping.EpisodeOffset, MatchSource.ManualMapping);
        }

        // A season tagged directly is the most precise signal available.
        var direct = await ResolveFromProviderIdsAsync(season, accessToken, cancellationToken).ConfigureAwait(false);
        if (direct is not null)
        {
            return await VerifyAsync(episode, direct, accessToken, cancellationToken).ConfigureAwait(false)
                ? direct
                : null;
        }

        var fromSeries = await ResolveFromProviderIdsAsync(series, accessToken, cancellationToken).ConfigureAwait(false);
        if (fromSeries is not null)
        {
            // Verify the entry the id names, before following any sequel chain: later seasons
            // carry titles like "... 2nd Season" that would not compare cleanly.
            if (!await VerifyAsync(episode, fromSeries, accessToken, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            // The series id names the first entry; later seasons are separate AniList entries
            // reachable by following the sequel chain.
            if (seasonNumber <= 1)
            {
                return fromSeries;
            }

            var walked = await WalkSequelsAsync(
                fromSeries.MediaId,
                seasonNumber - 1,
                accessToken,
                cancellationToken).ConfigureAwait(false);

            if (walked is null)
            {
                // Scrobbling season N onto the season 1 entry would silently corrupt the
                // user's progress, so report no match and let them map it by hand.
                _logger.LogInformation(
                    "AniList {MediaId} has no sequel chain reaching season {Season} of \"{Series}\"; add a manual mapping to scrobble it",
                    fromSeries.MediaId,
                    seasonNumber,
                    series?.Name ?? episode.SeriesName);

                return null;
            }

            return fromSeries with { MediaId = walked.Value };
        }

        if (configuration?.EnableTitleSearchFallback == true)
        {
            var title = season?.SeriesName ?? series?.Name ?? episode.SeriesName;
            return await ResolveByTitleAsync(
                title,
                seasonNumber,
                series?.ProductionYear,
                accessToken,
                cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<AnimeMatch?> ResolveStandaloneAsync(
        BaseItem item,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration;
        var mapping = configuration?.GetMapping(null, item.Id);
        if (mapping is not null && mapping.AniListMediaId > 0)
        {
            return new AnimeMatch(mapping.AniListMediaId, mapping.EpisodeOffset, MatchSource.ManualMapping);
        }

        var direct = await ResolveFromProviderIdsAsync(item, accessToken, cancellationToken).ConfigureAwait(false);
        if (direct is not null)
        {
            return await VerifyAsync(item, direct, accessToken, cancellationToken).ConfigureAwait(false)
                ? direct
                : null;
        }

        if (configuration?.EnableTitleSearchFallback == true)
        {
            return await ResolveByTitleAsync(
                item.Name,
                seasonNumber: 1,
                item.ProductionYear,
                accessToken,
                cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<AnimeMatch?> ResolveFromProviderIdsAsync(
        BaseItem? item,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return null;
        }

        var aniListId = GetProviderId(item, _aniListKeys);
        if (aniListId.HasValue)
        {
            return new AnimeMatch(aniListId.Value, 0, MatchSource.AniListProviderId);
        }

        var malId = GetProviderId(item, _malKeys);
        if (malId.HasValue)
        {
            var media = await _aniListClient
                .GetMediaByMalIdAsync(malId.Value, accessToken, cancellationToken)
                .ConfigureAwait(false);

            if (media is not null)
            {
                return new AnimeMatch(media.Id, 0, MatchSource.MyAnimeListId);
            }

            var mapped = await _idMappingProvider
                .GetAniListIdFromMalAsync(malId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (mapped.HasValue)
            {
                return new AnimeMatch(mapped.Value, 0, MatchSource.MyAnimeListId);
            }
        }

        var aniDbId = GetProviderId(item, _aniDbKeys);
        if (aniDbId.HasValue)
        {
            var mapped = await _idMappingProvider
                .GetAniListIdFromAniDbAsync(aniDbId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (mapped.HasValue)
            {
                return new AnimeMatch(mapped.Value, 0, MatchSource.AniDbId);
            }

            _logger.LogDebug("No AniList id is known for AniDB {AniDbId} ({Name})", aniDbId.Value, item.Name);
        }

        return null;
    }

    /// <summary>
    /// Checks that the AniList entry an id resolved to actually looks like the item in hand.
    ///
    /// Both halves of the comparison can be wrong on their own: a library's display name may
    /// come from a bad metadata match while the folder is named correctly, or the reverse. The
    /// best score across both is used, so either one being right is enough.
    /// </summary>
    /// <param name="item">The Jellyfin item being scrobbled.</param>
    /// <param name="match">The candidate match.</param>
    /// <param name="accessToken">The AniList access token.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><c>true</c> when the match is trustworthy.</returns>
    private async Task<bool> VerifyAsync(
        BaseItem item,
        AnimeMatch match,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration;

        // A manual mapping is an explicit instruction and is never second-guessed.
        if (configuration is null
            || !configuration.VerifyTitleMatch
            || match.Source == MatchSource.ManualMapping)
        {
            return true;
        }

        var media = await _aniListClient
            .GetMediaAsync(match.MediaId, accessToken, includeRelations: false, cancellationToken)
            .ConfigureAwait(false);

        if (media is null)
        {
            _logger.LogWarning("AniList has no media {MediaId} for {Item}", match.MediaId, item.Name);
            return false;
        }

        var (best, bestTitle) = ScoreTitles(GetLocalTitles(item), media);

        if (best >= configuration.TitleVerificationMinimumSimilarity)
        {
            return true;
        }

        _logger.LogWarning(
            "Refusing to scrobble \"{Item}\": its {Source} points at AniList {MediaId} \"{Candidate}\", "
            + "which only matches at {Score:P0}. Add a manual mapping if this is correct.",
            item.Name,
            match.Source,
            match.MediaId,
            bestTitle ?? media.DisplayTitle,
            best);

        return false;
    }

    /// <summary>
    /// Scores every local name against every title AniList holds, keeping the best pair.
    /// </summary>
    /// <param name="localTitles">The names the item is known by locally.</param>
    /// <param name="media">The candidate AniList entry.</param>
    /// <returns>The best similarity and the AniList title that produced it.</returns>
    internal static (double Score, string? Title) ScoreTitles(IEnumerable<string> localTitles, Media media)
    {
        var best = 0.0;
        string? bestTitle = null;

        foreach (var local in localTitles)
        {
            foreach (var remote in EnumerateTitles(media))
            {
                var score = TitleSimilarity.Compare(local, remote);
                if (score > best)
                {
                    best = score;
                    bestTitle = remote;
                }
            }
        }

        return (best, bestTitle);
    }

    /// <summary>
    /// The names this item is known by locally: its display title and its folder name.
    /// </summary>
    /// <param name="item">The Jellyfin item.</param>
    /// <returns>The candidate local titles.</returns>
    internal static IEnumerable<string> GetLocalTitles(BaseItem item)
    {
        var owner = item is Episode episode ? (BaseItem?)episode.Series ?? episode : item;

        if (!string.IsNullOrWhiteSpace(owner.Name))
        {
            yield return owner.Name;
        }

        var path = owner.Path;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var folder = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
            if (!string.IsNullOrWhiteSpace(folder))
            {
                yield return folder;
            }
        }
    }

    private async Task<int?> WalkSequelsAsync(
        int mediaId,
        int steps,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var current = mediaId;

        for (var step = 0; step < steps; step++)
        {
            var media = await _aniListClient
                .GetMediaAsync(current, accessToken, includeRelations: true, cancellationToken)
                .ConfigureAwait(false);

            var sequel = FindSequel(media);
            if (sequel is null)
            {
                // A partial walk lands on the wrong season, which is worse than no match.
                _logger.LogDebug(
                    "AniList {MediaId} has no sequel, {Remaining} step(s) short of the target season",
                    current,
                    steps - step);

                return null;
            }

            current = sequel.Value;
        }

        return current;
    }

    private static int? FindSequel(Media? media)
    {
        var edges = media?.Relations?.Edges;
        if (edges is null)
        {
            return null;
        }

        foreach (var edge in edges)
        {
            if (edge.Node is null
                || !string.Equals(edge.RelationType, "SEQUEL", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Side stories and specials share the SEQUEL relation in some cases; only a TV
            // format entry continues the numbered run of a series.
            if (string.Equals(edge.Node.Format, "TV", StringComparison.OrdinalIgnoreCase)
                || string.Equals(edge.Node.Format, "TV_SHORT", StringComparison.OrdinalIgnoreCase))
            {
                return edge.Node.Id;
            }
        }

        return null;
    }

    private async Task<AnimeMatch?> ResolveByTitleAsync(
        string? title,
        int seasonNumber,
        int? year,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var configuration = Plugin.Instance?.Configuration;
        var minimumSimilarity = configuration?.TitleSearchMinimumSimilarity ?? 0.85;

        var query = seasonNumber > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{title} Season {seasonNumber}")
            : title;

        var candidates = await _aniListClient
            .SearchAsync(query, accessToken, cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates
            .Select(media => new
            {
                Media = media,
                Score = ScoreCandidate(media, title, seasonNumber, year),
            })
            .OrderByDescending(candidate => candidate.Score)
            .First();

        if (best.Score < minimumSimilarity)
        {
            _logger.LogInformation(
                "No confident AniList match for \"{Title}\": best was \"{Candidate}\" at {Score:P0}",
                title,
                best.Media.DisplayTitle,
                best.Score);

            return null;
        }

        _logger.LogInformation(
            "Matched \"{Title}\" to AniList {MediaId} \"{Candidate}\" by title at {Score:P0}",
            title,
            best.Media.Id,
            best.Media.DisplayTitle,
            best.Score);

        return new AnimeMatch(best.Media.Id, 0, MatchSource.TitleSearch);
    }

    private static double ScoreCandidate(Media media, string title, int seasonNumber, int? year)
    {
        var best = 0.0;

        foreach (var candidate in EnumerateTitles(media))
        {
            best = Math.Max(best, TitleSimilarity.Compare(title, candidate));

            if (seasonNumber > 1)
            {
                var seasonForm = string.Create(CultureInfo.InvariantCulture, $"{title} Season {seasonNumber}");
                best = Math.Max(best, TitleSimilarity.Compare(seasonForm, candidate));
            }
        }

        // A matching production year breaks ties between a show and its remake.
        if (year.HasValue && media.SeasonYear.HasValue && year.Value == media.SeasonYear.Value)
        {
            best = Math.Min(1.0, best + 0.05);
        }

        return best;
    }

    private static IEnumerable<string> EnumerateTitles(Media media)
    {
        if (media.Title?.Romaji is { Length: > 0 } romaji)
        {
            yield return romaji;
        }

        if (media.Title?.English is { Length: > 0 } english)
        {
            yield return english;
        }

        if (media.Title?.Native is { Length: > 0 } native)
        {
            yield return native;
        }

        foreach (var synonym in media.Synonyms)
        {
            if (!string.IsNullOrWhiteSpace(synonym))
            {
                yield return synonym;
            }
        }
    }
}
