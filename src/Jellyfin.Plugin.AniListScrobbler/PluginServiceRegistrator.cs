using Jellyfin.Plugin.AniListScrobbler.AniList;
using Jellyfin.Plugin.AniListScrobbler.Matching;
using Jellyfin.Plugin.AniListScrobbler.Scrobbling;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.AniListScrobbler;

/// <summary>
/// Registers the plugin's services with the Jellyfin host.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IAniListClient, AniListClient>();
        serviceCollection.AddSingleton<IIdMappingProvider, IdMappingProvider>();
        serviceCollection.AddSingleton<IAnimeMatcher, AnimeMatcher>();
        serviceCollection.AddSingleton<IScrobbleService, ScrobbleService>();
        serviceCollection.AddHostedService<PlaybackWatcher>();
    }
}
