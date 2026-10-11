using KappuCitti.Plugin.SyncVote.Configuration;

namespace KappuCitti.Plugin.SyncVote.Services;

/// <summary>
/// <see cref="IPluginSettings"/> backed by the plugin instance.
/// </summary>
public class PluginSettings : IPluginSettings
{
    /// <inheritdoc />
    public PluginConfiguration Configuration => Plugin.Instance?.Configuration ?? new PluginConfiguration();
}
