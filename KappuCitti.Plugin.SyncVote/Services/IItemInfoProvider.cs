using System;

namespace KappuCitti.Plugin.SyncVote.Services;

/// <summary>
/// Basic information about a library item.
/// </summary>
/// <param name="Name">The item name.</param>
/// <param name="Year">The production year.</param>
/// <param name="Type">The item type (Movie, Series, ...).</param>
public sealed record ItemInfo(string Name, int? Year, string Type);

/// <summary>
/// Looks up library items; keeps <see cref="VotingRoomService"/> independent from the Jellyfin library.
/// </summary>
public interface IItemInfoProvider
{
    /// <summary>
    /// Gets the information about an item.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <returns>The item information, or null if the item does not exist.</returns>
    ItemInfo? GetItem(Guid itemId);
}
