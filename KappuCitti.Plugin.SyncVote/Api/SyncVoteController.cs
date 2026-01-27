using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using KappuCitti.Plugin.SyncVote.Models;
using KappuCitti.Plugin.SyncVote.Services;
using KappuCitti.Plugin.SyncVote.Auth;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace KappuCitti.Plugin.SyncVote.Api;

/// <summary>
/// SyncVote API controller.
/// </summary>
[ApiController]
[Authorize]
[Route("SyncVote")]
public class SyncVoteController : ControllerBase
{
    private readonly ILogger<SyncVoteController> _logger;
    private readonly VotingRoomService _votingRoomService;
    private readonly IUserManager _userManager;
    private readonly ISessionManager _sessionManager;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncVoteController"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{SyncVoteController}"/> interface.</param>
    /// <param name="votingRoomService">Instance of the <see cref="VotingRoomService"/> class.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    public SyncVoteController(
        ILogger<SyncVoteController> logger,
        VotingRoomService votingRoomService,
        IUserManager userManager,
        ISessionManager sessionManager,
        ILibraryManager libraryManager)
    {
        _logger = logger;
        _votingRoomService = votingRoomService;
        _userManager = userManager;
        _sessionManager = sessionManager;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Creates a new voting room.
    /// </summary>
    /// <param name="request">The room creation request.</param>
    /// <returns>The created room.</returns>
    [HttpPost("Room")]
    public async Task<ActionResult<VotingRoom>> CreateRoom([FromBody] CreateRoomRequest request)
    {
        var userId = User.GetUserId();

        // Check if user can organize
        var permissions = await _votingRoomService.GetUserPermissionsAsync(userId);
        if (!permissions.CanOrganize)
        {
            return Forbid("User does not have permission to organize voting rooms");
        }

        var room = await _votingRoomService.CreateRoomAsync(userId, request);
        return Ok(room);
    }

    /// <summary>
    /// Gets all active voting rooms.
    /// </summary>
    /// <returns>List of active rooms.</returns>
    [HttpGet("Rooms")]
    public async Task<ActionResult<IEnumerable<VotingRoom>>> GetRooms()
    {
        var rooms = await _votingRoomService.GetActiveRoomsAsync();
        return Ok(rooms);
    }

    /// <summary>
    /// Gets a specific voting room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The room details.</returns>
    [HttpGet("Room/{roomId}")]
    public async Task<ActionResult<VotingRoom>> GetRoom([FromRoute] Guid roomId)
    {
        var room = await _votingRoomService.GetRoomAsync(roomId);
        if (room == null)
        {
            return NotFound();
        }

        return Ok(room);
    }

    /// <summary>
    /// Joins a voting room.
    /// </summary>
    /// <param name="roomId">The room ID to join.</param>
    /// <returns>Success status.</returns>
    [HttpPost("Room/{roomId}/Join")]
    public async Task<ActionResult> JoinRoom([FromRoute] Guid roomId)
    {
        var userId = User.GetUserId();
        var success = await _votingRoomService.JoinRoomAsync(roomId, userId);

        if (!success)
        {
            return BadRequest("Unable to join room");
        }

        return Ok();
    }

    /// <summary>
    /// Starts voting in a room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>Success status.</returns>
    [HttpPost("Room/{roomId}/StartVoting")]
    public async Task<ActionResult> StartVoting([FromRoute] Guid roomId)
    {
        var userId = User.GetUserId();
        var success = await _votingRoomService.StartVotingAsync(roomId, userId);

        if (!success)
        {
            return BadRequest("Unable to start voting");
        }

        return Ok();
    }

    /// <summary>
    /// Casts a vote for an item.
    /// </summary>
    /// <param name="request">The vote request.</param>
    /// <returns>Success status.</returns>
    [HttpPost("Vote")]
    public async Task<ActionResult> CastVote([FromBody] CastVoteRequest request)
    {
        var userId = User.GetUserId();

        // Check if user can vote
        var permissions = await _votingRoomService.GetUserPermissionsAsync(userId);
        if (!permissions.CanVote)
        {
            return Forbid("User does not have permission to vote");
        }

        var success = await _votingRoomService.CastVoteAsync(request.RoomId, userId, request.ItemId, request.IsLike);

        if (!success)
        {
            return BadRequest("Unable to cast vote");
        }

        return Ok();
    }

    /// <summary>
    /// Gets voting results for a room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The voting results.</returns>
    [HttpGet("Room/{roomId}/Results")]
    public async Task<ActionResult<VotingResults>> GetResults([FromRoute] Guid roomId)
    {
        var results = await _votingRoomService.GetVotingResultsAsync(roomId);
        return Ok(results);
    }

    /// <summary>
    /// Gets user permissions.
    /// </summary>
    /// <param name="userId">The user ID (optional, defaults to current user).</param>
    /// <returns>User permissions.</returns>
    [HttpGet("Permissions")]
    public async Task<ActionResult<UserPermissions>> GetPermissions([FromQuery] Guid? userId = null)
    {
        var targetUserId = userId ?? User.GetUserId();
        var permissions = await _votingRoomService.GetUserPermissionsAsync(targetUserId);
        return Ok(permissions);
    }

    /// <summary>
    /// Gets collections/folders accessible to the current user.
    /// </summary>
    /// <returns>List of library folders the user can access.</returns>
    [HttpGet("Library/Collections")]
    public ActionResult<IEnumerable<LibraryItemInfo>> GetCollections()
    {
        var userId = User.GetUserId();
        var user = _userManager.GetUserById(userId);
        if (user == null)
        {
            return Unauthorized();
        }

        // Get user's accessible library folders (virtual folders)
        var folders = _libraryManager.GetVirtualFolders()
            .Where(f => !string.IsNullOrEmpty(f.ItemId))
            .Select(f =>
            {
                var item = _libraryManager.GetItemById(f.ItemId);
                if (item == null) return null;

                // Check if user has access
                var hasAccess = item.IsVisible(user);
                if (!hasAccess) return null;

                return new LibraryItemInfo
                {
                    Id = item.Id,
                    Name = f.Name ?? item.Name,
                    Type = f.CollectionType?.ToString() ?? "mixed",
                    ItemCount = GetFolderItemCount(item.Id, userId)
                };
            })
            .Where(x => x != null)
            .ToList();

        // Also get BoxSets (collections created by user)
        var boxSets = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
            Recursive = true
        })
        .Select(b => new LibraryItemInfo
        {
            Id = b.Id,
            Name = b.Name,
            Type = "boxset",
            ItemCount = GetBoxSetItemCount(b, user)
        });

        var result = folders.Concat(boxSets!).OrderBy(x => x!.Name).ToList();
        return Ok(result);
    }

    /// <summary>
    /// Gets all genres available in the library for the current user.
    /// </summary>
    /// <returns>List of genres.</returns>
    [HttpGet("Library/Genres")]
    public ActionResult<IEnumerable<string>> GetGenres()
    {
        var userId = User.GetUserId();
        var user = _userManager.GetUserById(userId);
        if (user == null)
        {
            return Unauthorized();
        }

        var genres = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode },
            Recursive = true,
            Limit = 10000
        })
        .Where(i => i.Genres != null && i.Genres.Length > 0)
        .SelectMany(i => i.Genres)
        .Distinct()
        .OrderBy(g => g)
        .ToList();

        return Ok(genres);
    }

    /// <summary>
    /// Gets available parental rating levels.
    /// </summary>
    /// <returns>List of parental ratings.</returns>
    [HttpGet("Library/ParentalRatings")]
    public ActionResult<IEnumerable<ParentalRatingInfo>> GetParentalRatings()
    {
        // Return common parental rating levels
        var ratings = new[]
        {
            new ParentalRatingInfo { Value = 0, Name = "Unrated" },
            new ParentalRatingInfo { Value = 1, Name = "G / All Ages" },
            new ParentalRatingInfo { Value = 6, Name = "PG / 6+" },
            new ParentalRatingInfo { Value = 12, Name = "PG-13 / 12+" },
            new ParentalRatingInfo { Value = 16, Name = "R / 16+" },
            new ParentalRatingInfo { Value = 18, Name = "NC-17 / 18+" }
        };

        return Ok(ratings);
    }

    /// <summary>
    /// Gets the current SyncPlay group info including member count and whether current user is the leader.
    /// </summary>
    /// <returns>SyncPlay group information.</returns>
    [HttpGet("SyncPlayInfo")]
    public ActionResult<SyncPlayInfo> GetSyncPlayInfo()
    {
        var userId = User.GetUserId();

        // Find user's active session with SyncPlay group via session info
        var sessions = _sessionManager.Sessions
            .Where(s => s.UserId == userId)
            .ToList();

        // Look for session with SyncPlayGroupId in NowPlayingItem or session properties
        foreach (var session in sessions)
        {
            // Check if session has a SyncPlayGroupId (stored in session when joining a group)
            var groupId = session.FullNowPlayingItem?.Id.ToString();

            // Alternative: look for all sessions sharing the same group
            // For now, we'll use a simpler approach based on VotingRoom
            var activeRooms = _votingRoomService.GetActiveRoomsAsync().Result;
            var roomWithUser = activeRooms.FirstOrDefault(r => r.MemberIds.Contains(userId));

            if (roomWithUser != null && !string.IsNullOrEmpty(roomWithUser.SyncPlayGroupId))
            {
                var memberCount = roomWithUser.MemberIds.Count;
                return Ok(new SyncPlayInfo
                {
                    GroupId = roomWithUser.SyncPlayGroupId,
                    IsLeader = roomWithUser.OrganizerId == userId,
                    MemberCount = memberCount,
                    MemberUserIds = roomWithUser.MemberIds.ToList()
                });
            }
        }

        // If not in a voting room, try to get SyncPlay info from sessions sharing the same content
        var userSession = sessions.FirstOrDefault(s => s.FullNowPlayingItem != null);
        if (userSession?.FullNowPlayingItem != null)
        {
            var sameContentSessions = _sessionManager.Sessions
                .Where(s => s.FullNowPlayingItem?.Id == userSession.FullNowPlayingItem.Id && s.UserId != Guid.Empty)
                .ToList();

            if (sameContentSessions.Count > 1)
            {
                return Ok(new SyncPlayInfo
                {
                    GroupId = userSession.FullNowPlayingItem.Id.ToString(),
                    IsLeader = true, // Assume leader if first to create
                    MemberCount = sameContentSessions.Select(s => s.UserId).Distinct().Count(),
                    MemberUserIds = sameContentSessions.Select(s => s.UserId).Distinct().ToList()
                });
            }
        }

        return Ok(new SyncPlayInfo { GroupId = null, IsLeader = false, MemberCount = 0, MemberUserIds = new List<Guid>() });
    }

    /// <summary>
    /// Checks if all SyncPlay group members have access to the specified collections.
    /// Returns true if there are access issues (some users can't see some collections).
    /// Does NOT reveal which specific users or which specific collections have issues.
    /// </summary>
    /// <param name="request">The collections to check.</param>
    /// <returns>Access check result indicating if there are any issues.</returns>
    [HttpPost("CheckAccess")]
    public ActionResult<AccessCheckResult> CheckCollectionAccess([FromBody] CheckAccessRequest request)
    {
        var userId = User.GetUserId();

        // Get member IDs from active voting room or sessions
        var memberUserIds = new List<Guid>();

        // Check if there's an active voting room for this user
        var activeRooms = _votingRoomService.GetActiveRoomsAsync().Result;
        var roomWithUser = activeRooms.FirstOrDefault(r => r.OrganizerId == userId);

        if (roomWithUser != null)
        {
            memberUserIds = roomWithUser.MemberIds
                .Where(id => id != userId)
                .ToList();
        }
        else
        {
            // Fallback: check sessions watching same content
            var userSession = _sessionManager.Sessions.FirstOrDefault(s => s.UserId == userId && s.FullNowPlayingItem != null);
            if (userSession?.FullNowPlayingItem != null)
            {
                memberUserIds = _sessionManager.Sessions
                    .Where(s => s.FullNowPlayingItem?.Id == userSession.FullNowPlayingItem.Id && s.UserId != userId && s.UserId != Guid.Empty)
                    .Select(s => s.UserId)
                    .Distinct()
                    .ToList();
            }
        }

        if (memberUserIds.Count == 0)
        {
            return Ok(new AccessCheckResult { HasAccessIssues = false, Message = "No other members in group" });
        }

        // Check each collection
        var hasIssues = false;
        foreach (var collectionId in request.CollectionIds ?? Enumerable.Empty<Guid>())
        {
            var item = _libraryManager.GetItemById(collectionId);
            if (item == null) continue;

            foreach (var memberId in memberUserIds)
            {
                var member = _userManager.GetUserById(memberId);
                if (member == null) continue;

                if (!item.IsVisible(member))
                {
                    hasIssues = true;
                    break;
                }
            }

            if (hasIssues) break;
        }

        return Ok(new AccessCheckResult
        {
            HasAccessIssues = hasIssues,
            Message = hasIssues
                ? "Some group members may not have access to all selected content"
                : "All members have access"
        });
    }

    /// <summary>
    /// Gets candidate items for voting based on room filters.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="limit">Maximum items to return.</param>
    /// <returns>List of candidate items.</returns>
    [HttpGet("Room/{roomId}/Candidates")]
    public async Task<ActionResult<CandidateItemsResult>> GetCandidates(
        [FromRoute] Guid roomId,
        [FromQuery] int skip = 0,
        [FromQuery] int limit = 20)
    {
        var userId = User.GetUserId();
        var user = _userManager.GetUserById(userId);
        if (user == null)
        {
            return Unauthorized();
        }

        var room = await _votingRoomService.GetRoomAsync(roomId);
        if (room == null)
        {
            return NotFound();
        }

        // Build query based on room filters
        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie },
            Recursive = true,
            StartIndex = skip,
            Limit = limit,
            IsVirtualItem = false
        };

        // Apply collection filter using AncestorIds (parent folders)
        if (room.SelectedCollections.Count > 0)
        {
            query.AncestorIds = room.SelectedCollections.ToArray();
        }

        // Apply genre filter
        if (room.SelectedGenres.Count > 0)
        {
            query.Genres = room.SelectedGenres.ToArray();
        }

        // Apply sorting
        query.OrderBy = room.SortBy switch
        {
            SortBy.Title => new[] { (ItemSortBy.SortName, SortOrder.Ascending) },
            SortBy.CommunityRating => new[] { (ItemSortBy.CommunityRating, SortOrder.Descending) },
            SortBy.PremiereDate => new[] { (ItemSortBy.PremiereDate, SortOrder.Descending) },
            _ => new[] { (ItemSortBy.Random, SortOrder.Ascending) }
        };

        var items = _libraryManager.GetItemList(query);
        var totalCount = _libraryManager.GetCount(query);

        var candidates = items.Select(item => new CandidateItem
        {
            Id = item.Id,
            Name = item.Name,
            Year = item.ProductionYear,
            Genres = item.Genres,
            CommunityRating = item.CommunityRating,
            OfficialRating = item.OfficialRating,
            Overview = item.Overview,
            RunTimeTicks = item.RunTimeTicks
        }).ToList();

        return Ok(new CandidateItemsResult
        {
            Items = candidates,
            TotalCount = totalCount,
            StartIndex = skip
        });
    }

    private int GetFolderItemCount(Guid folderId, Guid userId)
    {
        var user = _userManager.GetUserById(userId);
        if (user == null) return 0;

        var folder = _libraryManager.GetItemById(folderId);
        if (folder is not Folder f) return 0;

        return f.GetChildren(user, true).Count();
    }

    private int GetBoxSetItemCount(BaseItem boxSet, Jellyfin.Data.Entities.User user)
    {
        return _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            AncestorIds = new[] { boxSet.Id },
            Recursive = true,
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode }
        }).Count;
    }
}

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
    /// Gets or sets the SyncPlay group ID.
    /// </summary>
    [Required]
    public string SyncPlayGroupId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the time limit in minutes.
    /// </summary>
    public int TimeLimit { get; set; } = 5;

    /// <summary>
    /// Gets or sets the sort order.
    /// </summary>
    public string SortBy { get; set; } = "Random";

    /// <summary>
    /// Gets or sets the selected collection IDs.
    /// </summary>
    public List<Guid> SelectedCollections { get; set; } = new();

    /// <summary>
    /// Gets or sets the selected genres.
    /// </summary>
    public List<string> SelectedGenres { get; set; } = new();

    /// <summary>
    /// Gets or sets the maximum parental rating level (0 = no filter, higher = more restrictive filter).
    /// </summary>
    public int? MaxParentalRating { get; set; }

    /// <summary>
    /// Gets or sets the item types to include (Movie, Series, Episode). Defaults to Movie only.
    /// </summary>
    public List<string> ItemTypes { get; set; } = new() { "Movie" };
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
    /// Gets or sets the list of liked items with vote counts.
    /// </summary>
    public List<VotedItem> LikedItems { get; set; } = new();

    /// <summary>
    /// Gets or sets the winning item (most votes).
    /// </summary>
    public VotedItem? Winner { get; set; }
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
    /// Gets or sets the number of votes.
    /// </summary>
    public int VoteCount { get; set; }

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
    /// Gets or sets the group ID.
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// Gets or sets whether the current user is the group leader.
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
    /// Gets or sets whether there are access issues (some users can't see some collections).
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
    /// Gets or sets the production year.
    /// </summary>
    public int? Year { get; set; }

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
    /// Gets or sets the total count of matching items.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the start index.
    /// </summary>
    public int StartIndex { get; set; }
}
