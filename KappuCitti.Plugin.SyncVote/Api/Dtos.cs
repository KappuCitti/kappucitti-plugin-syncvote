using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace KappuCitti.Plugin.SyncVote.Api;

/// <summary>
/// Request model for creating a room.
/// </summary>
public class CreateRoomRequest
{
    /// <summary>
    /// Gets or sets the room name.
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SyncPlay group ID. When empty, the group of the current user is used.
    /// </summary>
    public string? SyncPlayGroupId { get; set; }

    /// <summary>
    /// Gets or sets the time limit in minutes (0 = plugin default).
    /// </summary>
    public int TimeLimit { get; set; }

    /// <summary>
    /// Gets or sets the sort order (empty = plugin default).
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>
    /// Gets or sets the selected collection IDs.
    /// </summary>
    public List<Guid>? SelectedCollections { get; set; } = new();

    /// <summary>
    /// Gets or sets the selected genres.
    /// </summary>
    public List<string>? SelectedGenres { get; set; } = new();

    /// <summary>
    /// Gets or sets the maximum parental rating level (null = no filter).
    /// </summary>
    public int? MaxParentalRating { get; set; }

    /// <summary>
    /// Gets or sets the text the titles must contain (empty = every title).
    /// </summary>
    public string? TitleFilter { get; set; }

    /// <summary>
    /// Gets or sets the item types to include (Movie, Series, Episode). Defaults to Movie only.
    /// </summary>
    public List<string>? ItemTypes { get; set; } = new() { "Movie" };
}

/// <summary>
/// Request model for casting a vote.
/// </summary>
public class CastVoteRequest
{
    /// <summary>
    /// Gets or sets the room ID.
    /// </summary>
    [Required]
    public Guid RoomId { get; set; }

    /// <summary>
    /// Gets or sets the item ID being voted on.
    /// </summary>
    [Required]
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a like vote.
    /// </summary>
    public bool IsLike { get; set; }
}

/// <summary>
/// Voting results model.
/// </summary>
public class VotingResults
{
    /// <summary>
    /// Gets or sets the room ID.
    /// </summary>
    public Guid RoomId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the voting round is over.
    /// </summary>
    public bool IsVotingFinished { get; set; }

    /// <summary>
    /// Gets or sets the liked items, best first.
    /// </summary>
    public List<VotedItem> LikedItems { get; set; } = new();

    /// <summary>
    /// Gets or sets the winning item.
    /// </summary>
    public VotedItem? Winner { get; set; }

    /// <summary>
    /// Gets or sets the number of room members that cannot access the winner (their identity is not disclosed).
    /// </summary>
    public int MembersWithoutAccess { get; set; }
}

/// <summary>
/// Represents an item with vote information.
/// </summary>
public class VotedItem
{
    /// <summary>
    /// Gets or sets the item ID.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the number of likes.
    /// </summary>
    public int VoteCount { get; set; }

    /// <summary>
    /// Gets or sets the number of dislikes.
    /// </summary>
    public int DislikeCount { get; set; }

    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item year.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the item type.
    /// </summary>
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// Information about a library item (collection/folder).
/// </summary>
public class LibraryItemInfo
{
    /// <summary>
    /// Gets or sets the item ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the collection type (movies, tvshows, boxset, etc).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of items in this collection.
    /// </summary>
    public int ItemCount { get; set; }
}

/// <summary>
/// Parental rating information.
/// </summary>
public class ParentalRatingInfo
{
    /// <summary>
    /// Gets or sets the numeric rating value.
    /// </summary>
    public int Value { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// SyncPlay group information.
/// </summary>
public class SyncPlayInfo
{
    /// <summary>
    /// Gets or sets the group ID (null when the user is not in a SyncPlay group).
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// Gets or sets the group name.
    /// </summary>
    public string? GroupName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the current user organizes the voting room of the group.
    /// </summary>
    public bool IsLeader { get; set; }

    /// <summary>
    /// Gets or sets the number of members in the group.
    /// </summary>
    public int MemberCount { get; set; }

    /// <summary>
    /// Gets or sets the member user IDs.
    /// </summary>
    public List<Guid> MemberUserIds { get; set; } = new();
}

/// <summary>
/// Request to check collection access for SyncPlay group members.
/// </summary>
public class CheckAccessRequest
{
    /// <summary>
    /// Gets or sets the collection IDs to check.
    /// </summary>
    public List<Guid>? CollectionIds { get; set; }
}

/// <summary>
/// Result of access check for collections.
/// </summary>
public class AccessCheckResult
{
    /// <summary>
    /// Gets or sets a value indicating whether some members cannot see some of the collections.
    /// </summary>
    public bool HasAccessIssues { get; set; }

    /// <summary>
    /// Gets or sets the message describing the result.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// A candidate item for voting.
/// </summary>
public class CandidateItem
{
    /// <summary>
    /// Gets or sets the item ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the production year.
    /// </summary>
    public int? ProductionYear { get; set; }

    /// <summary>
    /// Gets or sets the genres.
    /// </summary>
    public string[] Genres { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the community rating.
    /// </summary>
    public float? CommunityRating { get; set; }

    /// <summary>
    /// Gets or sets the official rating (e.g., PG-13).
    /// </summary>
    public string? OfficialRating { get; set; }

    /// <summary>
    /// Gets or sets the overview/description.
    /// </summary>
    public string? Overview { get; set; }

    /// <summary>
    /// Gets or sets the runtime in ticks.
    /// </summary>
    public long? RunTimeTicks { get; set; }
}

/// <summary>
/// Result containing candidate items for voting.
/// </summary>
public class CandidateItemsResult
{
    /// <summary>
    /// Gets or sets the candidate items.
    /// </summary>
    public List<CandidateItem> Items { get; set; } = new();

    /// <summary>
    /// Gets or sets the total number of matching items (voted ones excluded when requested).
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the start index.
    /// </summary>
    public int StartIndex { get; set; }
}
