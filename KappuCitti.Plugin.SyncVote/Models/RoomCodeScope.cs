namespace KappuCitti.Plugin.SyncVote.Models;

/// <summary>
/// How long the code of a room stays valid.
/// </summary>
public enum RoomCodeScope
{
    /// <summary>
    /// The code lasts for the whole SyncPlay session: the same code serves every room of the group, and whoever has entered it
    /// does not have to enter it again.
    /// </summary>
    Session = 0,

    /// <summary>
    /// The code changes with every new voting round; whoever wants to vote in the new round has to enter the new code.
    /// </summary>
    Round = 1
}
