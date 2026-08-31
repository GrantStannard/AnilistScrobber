using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniListScrobbler.Scrobbling;

/// <summary>
/// Listens for finished playback and for items being marked played, and hands them to the
/// scrobbler.
/// </summary>
public sealed class PlaybackWatcher : IHostedService, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IScrobbleService _scrobbleService;
    private readonly ILogger<PlaybackWatcher> _logger;
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackWatcher"/> class.
    /// </summary>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="scrobbleService">Instance of the <see cref="IScrobbleService"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public PlaybackWatcher(
        ISessionManager sessionManager,
        IUserDataManager userDataManager,
        IScrobbleService scrobbleService,
        ILogger<PlaybackWatcher> logger)
    {
        _sessionManager = sessionManager;
        _userDataManager = userDataManager;
        _scrobbleService = scrobbleService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _userDataManager.UserDataSaved += OnUserDataSaved;

        _logger.LogInformation("AniList scrobbler is listening for playback");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _userDataManager.UserDataSaved -= OnUserDataSaved;

        if (!_stopping.IsCancellationRequested)
        {
            _stopping.Cancel();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Dispose();
    }

    /// <summary>
    /// Decides whether a stopped playback counts as watched.
    ///
    /// Whenever the timings are known the configured threshold is the only gate, so it can be
    /// set stricter than Jellyfin's own completion rule. Jellyfin marks an item played past
    /// MaxResumePct (90% by default); deferring to that flag first would silently cap the
    /// threshold at 90 and make any higher value do nothing.
    /// </summary>
    /// <param name="positionTicks">Where playback stopped.</param>
    /// <param name="runTimeTicks">The item's runtime.</param>
    /// <param name="playedToCompletion">Whether Jellyfin judged the item finished.</param>
    /// <param name="minimumPercentage">The configured watched threshold.</param>
    /// <returns><c>true</c> when the item should be scrobbled.</returns>
    public static bool IsWatched(
        long? positionTicks,
        long? runTimeTicks,
        bool playedToCompletion,
        int minimumPercentage)
    {
        if (positionTicks is { } position && runTimeTicks is { } runtime && runtime > 0)
        {
            var percentage = (double)position / runtime * 100.0;
            return percentage >= minimumPercentage;
        }

        // Nothing to measure: a client that reported no position, or an item with no known
        // runtime. Jellyfin treats both as finished and marks the item played, so agreeing
        // with it keeps AniList and the Jellyfin library from disagreeing.
        return playedToCompletion;
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        if (e.Item is null || e.Users is null)
        {
            return;
        }

        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null)
        {
            return;
        }

        foreach (var user in e.Users)
        {
            var userConfiguration = configuration.GetUser(user.Id);
            if (userConfiguration is null || !userConfiguration.ScrobbleEnabled)
            {
                continue;
            }

            if (!IsWatched(
                    e.PlaybackPositionTicks,
                    e.Item.RunTimeTicks,
                    e.PlayedToCompletion,
                    userConfiguration.MinimumPlayedPercentage))
            {
                continue;
            }

            Dispatch(user.Id, e.Item);
        }
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        if (e.Item is null || e.UserData is null)
        {
            return;
        }

        // Only the manual toggle is handled here. PlaybackFinished is raised by Jellyfin on
        // the same 90% rule as PlayedToCompletion, so acting on it would re-introduce the cap
        // that IsWatched exists to avoid; PlaybackStopped already covers real playback.
        if (e.SaveReason is not UserDataSaveReason.TogglePlayed)
        {
            return;
        }

        if (!e.UserData.Played)
        {
            return;
        }

        var userConfiguration = Plugin.Instance?.Configuration.GetUser(e.UserId);
        if (userConfiguration is null || !userConfiguration.ScrobbleEnabled)
        {
            return;
        }

        if (!userConfiguration.ScrobbleOnManualMarkPlayed)
        {
            return;
        }

        Dispatch(e.UserId, e.Item);
    }

    private void Dispatch(Guid userId, MediaBrowser.Controller.Entities.BaseItem item)
    {
        // Jellyfin raises these events synchronously on the playback path, so the network
        // work is handed to the thread pool and never blocks the caller.
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await _scrobbleService.ScrobbleAsync(userId, item, _stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The server is shutting down.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error while scrobbling {Item}", item.Name);
                }
            },
            CancellationToken.None);
    }
}
