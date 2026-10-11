using KappuCitti.Plugin.SyncVote.Infrastructure;
using KappuCitti.Plugin.SyncVote.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KappuCitti.Plugin.SyncVote;

/// <summary>
/// Registers the plugin services in Jellyfin's dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IPluginSettings, PluginSettings>();
        serviceCollection.AddSingleton<LibraryService>();
        serviceCollection.AddSingleton<IItemInfoProvider>(sp => sp.GetRequiredService<LibraryService>());
        serviceCollection.AddSingleton<SyncPlayService>();
        serviceCollection.AddSingleton<VotingRoomService>();

        // Adds the SyncVote script to the Jellyfin web client.
        serviceCollection.AddTransient<IStartupFilter, WebClientStartupFilter>();
    }
}
