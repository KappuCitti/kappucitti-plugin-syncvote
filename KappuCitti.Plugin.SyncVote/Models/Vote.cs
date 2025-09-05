using System;

namespace KappuCitti.Plugin.SyncVote.Models;

/// <summary>
/// Represents a user's vote on a movie/show.
/// </summary>
public class Vote
{
    /// <summary>
    /// Gets or sets the unique identifier for the vote.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the room ID this vote belongs to.
    /// </summary>
    public Guid RoomId { get; set; }

    /// <summary>
    /// Gets or sets the user ID who cast the vote.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the item ID (movie/show) being voted on.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user liked the item.
    /// </summary>
    public bool IsLike { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the vote was cast.
    /// </summary>
    public DateTime VotedAt { get; set; } = DateTime.UtcNow;
}
