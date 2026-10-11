namespace KappuCitti.Plugin.SyncVote.Models;

/// <summary>
/// How the titles are ordered before the vote.
/// </summary>
public enum SortBy
{
    /// <summary>A random order, the same for every member of the room.</summary>
    Random,

    /// <summary>By title, A to Z (Jellyfin's sort name, so leading articles are ignored).</summary>
    Title,

    /// <summary>By community rating, best first.</summary>
    CommunityRating,

    /// <summary>By premiere date, newest first.</summary>
    PremiereDate
}
