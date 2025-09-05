using System;

namespace KappuCitti.Plugin.SyncVote.Models;

/// <summary>
/// Represents user permissions for SyncVote.
/// </summary>
public class UserPermissions
{
    /// <summary>
    /// Gets or sets the user ID.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user can organize voting rooms.
    /// </summary>
    public bool CanOrganize { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the user can vote.
    /// </summary>
    public bool CanVote { get; set; } = true;
}
