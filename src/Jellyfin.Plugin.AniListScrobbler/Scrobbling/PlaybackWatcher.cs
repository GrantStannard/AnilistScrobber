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
    /// </summary>
    /// <param name="positionTicks">Where playback stopped.</param>
    /// <param name="runTimeTicks">The item's runtime.</param>
    /// <param name="playedToCompletion">Whether the client reported the item as finished.</param>
    /// <param name="minimumPercentage">The configured watched threshold.</param>
    /// <returns><c>true</c> when the item should be scrobbled.</returns>
    public static bool IsWatched(
        long? positionTicks,
        long? runTimeTicks,
        bool playedToCompletion,
        int minimumPercentage)
    {
        if (playedToCompletion)
        {
            return true;
        }

        if (positionTicks is not { } position || runTimeTicks is not { } runtime || runtime <= 0)
        {
            return false;
        }

        var percentage = (double)position / runtime * 100.0;
        return percentage >= minimumPercentage;
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

        // PlaybackFinished duplicates the session event and is deduplicated downstream; the
        // manual toggle is the case that the session events never cover.
        if (e.SaveReason is not (UserDataSaveReason.TogglePlayed or UserDataSaveReason.PlaybackFinished))
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

        if (e.SaveReason == UserDataSaveReason.TogglePlayed && !userConfiguration.ScrobbleOnManualMarkPlayed)
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
