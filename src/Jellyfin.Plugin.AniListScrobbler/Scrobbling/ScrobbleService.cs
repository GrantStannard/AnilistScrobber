using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AniListScrobbler.AniList;
using Jellyfin.Plugin.AniListScrobbler.AniList.Models;
using Jellyfin.Plugin.AniListScrobbler.Configuration;
using Jellyfin.Plugin.AniListScrobbler.Matching;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniListScrobbler.Scrobbling;

/// <summary>
/// Turns a watched Jellyfin item into an AniList list update.
/// </summary>
public sealed class ScrobbleService : IScrobbleService
{
    /// <summary>
    /// Playback stopping and the user data being saved both signal the same watch, so an
    /// item scrobbled within this window is not scrobbled again.
    /// </summary>
    private static readonly TimeSpan _deduplicationWindow = TimeSpan.FromMinutes(1);

    private readonly IAniListClient _aniListClient;
    private readonly IAnimeMatcher _matcher;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ScrobbleService> _logger;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recent = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrobbleService"/> class.
    /// </summary>
    /// <param name="aniListClient">Instance of the <see cref="IAniListClient"/> interface.</param>
    /// <param name="matcher">Instance of the <see cref="IAnimeMatcher"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public ScrobbleService(
        IAniListClient aniListClient,
        IAnimeMatcher matcher,
        ILibraryManager libraryManager,
        ILogger<ScrobbleService> logger)
    {
        _aniListClient = aniListClient;
        _matcher = matcher;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ScrobbleOutcome> ScrobbleAsync(Guid userId, BaseItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null)
        {
            return ScrobbleOutcome.NotEnabled;
        }

        var user = configuration.GetUser(userId);
        if (user is null || !user.ScrobbleEnabled || !user.IsLinked)
        {
            return ScrobbleOutcome.NotEnabled;
        }

        if (!IsInAllowedLibrary(item, user))
        {
            Trace("{Item} is not in a library this user scrobbles", item.Name);
            return ScrobbleOutcome.NotApplicable;
        }

        if (!TryGetEpisodeNumber(item, user, configuration, out var episodeNumber))
        {
            return ScrobbleOutcome.NotApplicable;
        }

        if (!TryClaim(userId, item.Id))
        {
            Trace("Skipping duplicate scrobble of {Item}", item.Name);
            return ScrobbleOutcome.AlreadyUpToDate;
        }

        var attempts = Math.Max(0, configuration.MaxRetryAttempts) + 1;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return await ScrobbleCoreAsync(item, user, episodeNumber, configuration, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (AniListException ex)
            {
                if (ex.IsAuthenticationFailure)
                {
                    _logger.LogError(
                        "AniList rejected the token for Jellyfin user {UserId}; re-link the account in the plugin settings",
                        userId);

                    break;
                }

                if (!ex.IsTransient || attempt == attempts)
                {
                    _logger.LogError(ex, "Failed to scrobble {Item} to AniList", item.Name);
                    break;
                }

                var backoff = GetBackoff(attempt);
                _logger.LogWarning(
                    "Scrobbling {Item} failed ({Reason}); retrying in {Seconds}s (attempt {Attempt} of {Attempts})",
                    item.Name,
                    ex.Message,
                    backoff.TotalSeconds,
                    attempt,
                    attempts);

                try
                {
                    await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // A failed attempt must not block a later retry of the same episode.
        _recent.TryRemove(GetClaimKey(userId, item.Id), out _);
        return ScrobbleOutcome.Failed;
    }

    /// <summary>
    /// Exponential back-off, capped so a long outage does not park a thread for hours. The
    /// client already waits out an explicit Retry-After before its request returns, so this
    /// only spaces out the attempts around it.
    /// </summary>
    /// <param name="attempt">The 1-based attempt number that just failed.</param>
    /// <returns>How long to wait before the next attempt.</returns>
    internal static TimeSpan GetBackoff(int attempt)
    {
        var seconds = Math.Min(60, 5 * Math.Pow(2, Math.Max(0, attempt - 1)));
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task<ScrobbleOutcome> ScrobbleCoreAsync(
        BaseItem item,
        UserConfiguration user,
        int episodeNumber,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var match = await _matcher.ResolveAsync(item, user.AccessToken, cancellationToken).ConfigureAwait(false);
        if (match is null)
        {
            _logger.LogInformation("Could not match {Item} to an AniList entry", item.Name);
            return ScrobbleOutcome.NotMatched;
        }

        var progress = episodeNumber - match.EpisodeOffset;
        if (progress < 1)
        {
            _logger.LogWarning(
                "Episode {Episode} of {Item} maps to AniList progress {Progress}; check the episode offset",
                episodeNumber,
                item.Name,
                progress);

            return ScrobbleOutcome.NotApplicable;
        }

        var media = await _aniListClient
            .GetMediaAsync(match.MediaId, user.AccessToken, includeRelations: false, cancellationToken)
            .ConfigureAwait(false);

        if (media is null)
        {
            _logger.LogWarning("AniList has no media {MediaId}, matched from {Item}", match.MediaId, item.Name);
            return ScrobbleOutcome.NotMatched;
        }

        // Libraries numbered absolutely run past the end of one AniList entry and into its
        // sequel; follow the chain until the episode falls inside an entry.
        (media, progress) = await ResolveOverflowAsync(item, match, media, progress, user, cancellationToken)
            .ConfigureAwait(false);

        // After the sequel walk the episode must fit inside the entry. It can still overrun
        // when a library numbers one season absolutely while its siblings restart at 1, and
        // writing a progress the entry cannot hold would corrupt the list.
        if (media.Episodes is { } episodeCount && episodeCount > 0 && progress > episodeCount)
        {
            _logger.LogWarning(
                "Not scrobbling {Item}: episode {Episode} maps to progress {Progress} on \"{Title}\", "
                + "which has {Total} episodes. The season is most likely numbered absolutely; "
                + "add a manual mapping with an episode offset.",
                item.Name,
                episodeNumber,
                progress,
                media.DisplayTitle,
                episodeCount);

            return ScrobbleOutcome.NotApplicable;
        }

        var entry = media.MediaListEntry;
        var status = media.Episodes is { } total && total > 0 && progress >= total
            ? MediaListStatus.COMPLETED
            : MediaListStatus.CURRENT;

        if (entry is not null && entry.Progress >= progress)
        {
            Trace(
                "AniList already has {Title} at episode {Existing}, not lowering it to {Progress}",
                media.DisplayTitle,
                entry.Progress,
                progress);

            return ScrobbleOutcome.AlreadyUpToDate;
        }

        var result = await _aniListClient
            .SaveEntryAsync(media.Id, progress, status, repeat: null, user.AccessToken, cancellationToken)
            .ConfigureAwait(false);

        if (result is null)
        {
            _logger.LogWarning("AniList did not confirm the update for {Title}", media.DisplayTitle);
            return ScrobbleOutcome.Failed;
        }

        _logger.LogInformation(
            "Scrobbled {Title} episode {Progress}{Total} to AniList as {Status} (matched by {Source})",
            media.DisplayTitle,
            progress,
            media.Episodes is { } count ? "/" + count.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty,
            status,
            match.Source);

        return ScrobbleOutcome.Updated;
    }

    private async Task<(Media Media, int Progress)> ResolveOverflowAsync(
        BaseItem item,
        AnimeMatch match,
        Media media,
        int progress,
        UserConfiguration user,
        CancellationToken cancellationToken)
    {
        var current = media;
        var currentProgress = progress;
        var offset = match.EpisodeOffset;

        // Bounded so a cyclic or mis-tagged relation graph cannot spin here.
        for (var hop = 0; hop < 8; hop++)
        {
            if (current.Episodes is not { } total || total <= 0 || currentProgress <= total)
            {
                break;
            }

            var withRelations = await _aniListClient
                .GetMediaAsync(current.Id, user.AccessToken, includeRelations: true, cancellationToken)
                .ConfigureAwait(false);

            var sequelId = FindSequelId(withRelations);
            if (sequelId is null)
            {
                break;
            }

            var sequel = await _aniListClient
                .GetMediaAsync(sequelId.Value, user.AccessToken, includeRelations: false, cancellationToken)
                .ConfigureAwait(false);

            if (sequel is null)
            {
                break;
            }

            var previousTitle = current.DisplayTitle;
            currentProgress -= total;
            offset += total;
            current = sequel;

            _logger.LogInformation(
                "Episode ran past the end of \"{Previous}\", continuing into \"{Next}\" at episode {Progress}",
                previousTitle,
                current.DisplayTitle,
                currentProgress);
        }

        if (current.Id != media.Id)
        {
            // Remember the corrected entry so later episodes of this season skip the walk.
            _matcher.UpdateCache(item, match with { MediaId = current.Id, EpisodeOffset = offset });
        }

        return (current, currentProgress);
    }

    private static int? FindSequelId(Media? media)
    {
        var edges = media?.Relations?.Edges;
        if (edges is null)
        {
            return null;
        }

        foreach (var edge in edges)
        {
            if (edge.Node is not null
                && string.Equals(edge.RelationType, "SEQUEL", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(edge.Node.Format, "TV", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(edge.Node.Format, "TV_SHORT", StringComparison.OrdinalIgnoreCase)))
            {
                return edge.Node.Id;
            }
        }

        return null;
    }

    private bool TryGetEpisodeNumber(
        BaseItem item,
        UserConfiguration user,
        PluginConfiguration configuration,
        out int episodeNumber)
    {
        episodeNumber = 0;

        switch (item)
        {
            case Episode episode:
                if (episode.IndexNumber is not { } number)
                {
                    Trace("{Item} has no episode number", episode.Name);
                    return false;
                }

                var seasonNumber = episode.ParentIndexNumber ?? episode.AiredSeasonNumber ?? 1;
                if (seasonNumber == 0
                    && configuration.GetMapping(episode.SeasonId, episode.SeriesId) is null)
                {
                    // Specials do not map onto a numbered AniList run without an explicit
                    // mapping, and guessing would corrupt the user's progress.
                    Trace("{Item} is a special with no manual mapping", episode.Name);
                    return false;
                }

                episodeNumber = number;
                return true;

            case Movie:
                if (!user.ScrobbleMovies)
                {
                    return false;
                }

                episodeNumber = 1;
                return true;

            default:
                return false;
        }
    }

    private bool IsInAllowedLibrary(BaseItem item, UserConfiguration user)
    {
        if (user.LibraryIds.Length == 0)
        {
            return true;
        }

        var folders = _libraryManager.GetCollectionFolders(item);
        foreach (var folder in folders)
        {
            if (Array.IndexOf(user.LibraryIds, folder.Id) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetClaimKey(Guid userId, Guid itemId)
        => string.Concat(userId.ToString("N"), ":", itemId.ToString("N"));

    private bool TryClaim(Guid userId, Guid itemId)
    {
        var now = DateTimeOffset.UtcNow;
        var key = GetClaimKey(userId, itemId);

        foreach (var pair in _recent)
        {
            if (now - pair.Value > _deduplicationWindow)
            {
                _recent.TryRemove(pair.Key, out _);
            }
        }

        if (_recent.TryGetValue(key, out var last) && now - last <= _deduplicationWindow)
        {
            return false;
        }

        _recent[key] = now;
        return true;
    }

    private void Trace(string message, params object?[] arguments)
    {
        if (Plugin.Instance?.Configuration.VerboseLogging == true)
        {
            _logger.LogInformation(message, arguments);
        }
        else
        {
            _logger.LogDebug(message, arguments);
        }
    }
}
