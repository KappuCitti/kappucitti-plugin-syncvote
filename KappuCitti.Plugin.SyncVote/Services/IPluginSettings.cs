using KappuCitti.Plugin.SyncVote.Configuration;

namespace KappuCitti.Plugin.SyncVote.Services;

/// <summary>
/// Gives access to the current plugin configuration.
/// </summary>
public interface IPluginSettings
{
    /// <summary>
    /// Gets the current configuration.
    /// </summary>
    PluginConfiguration Configuration { get; }
}
