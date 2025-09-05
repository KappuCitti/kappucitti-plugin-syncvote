using MediaBrowser.Model.Plugins;

namespace KappuCitti.Plugin.SyncVote.Configuration;

/// <summary>
/// Plugin configuration for SyncVote.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the default time limit for voting sessions in minutes.
    /// </summary>
    public int DefaultTimeLimit { get; set; } = 5;

    /// <summary>
    /// Gets or sets a value indicating whether public rooms are allowed.
    /// </summary>
    public bool AllowPublicRooms { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of members per room.
    /// </summary>
    public int MaxRoomMembers { get; set; } = 10;

    /// <summary>
    /// Gets or sets a value indicating whether voting should auto-start when all members join.
    /// </summary>
    public bool AutoStartVoting { get; set; } = false;

    /// <summary>
    /// Gets or sets the default sort order for movies.
    /// </summary>
    public string DefaultSortBy { get; set; } = "Random";
}
