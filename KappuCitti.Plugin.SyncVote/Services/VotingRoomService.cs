using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KappuCitti.Plugin.SyncVote.Api;
using KappuCitti.Plugin.SyncVote.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace KappuCitti.Plugin.SyncVote.Services;

/// <summary>
/// Service for managing voting rooms and votes.
/// </summary>
public class VotingRoomService
{
    private readonly ILogger<VotingRoomService> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly List<VotingRoom> _rooms = new();
    private readonly List<Vote> _votes = new();
    private readonly Dictionary<Guid, UserPermissions> _userPermissions = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="VotingRoomService"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{VotingRoomService}"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    public VotingRoomService(ILogger<VotingRoomService> logger, ILibraryManager libraryManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Creates a new voting room.
    /// </summary>
    /// <param name="organizerId">The organizer user ID.</param>
    /// <param name="request">The room creation request.</param>
    /// <returns>The created room.</returns>
    public async Task<VotingRoom> CreateRoomAsync(Guid organizerId, CreateRoomRequest request)
    {
        // Parse SortBy from request (string -> enum), default to Random
        var sortBy = Enum.TryParse<SortBy>(request.SortBy, true, out var parsed)
            ? parsed
            : SortBy.Random;

        var room = new VotingRoom
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            SyncPlayGroupId = request.SyncPlayGroupId,
            OrganizerId = organizerId,
            IsActive = true,
            TimeLimit = request.TimeLimit,
            SortBy = sortBy
        };

        // Initialize members and filters via provided APIs
        room.AddMember(organizerId);
        room.SetSelectedCollections(request.SelectedCollections ?? new List<Guid>());
        room.SetSelectedGenres(request.SelectedGenres ?? new List<string>());

        _rooms.Add(room);
        _logger.LogInformation("Created voting room {RoomId} by user {UserId}", room.Id, organizerId);

        return await Task.FromResult(room);
    }

    /// <summary>
    /// Gets all active voting rooms.
    /// </summary>
    /// <returns>List of active rooms.</returns>
    public async Task<IEnumerable<VotingRoom>> GetActiveRoomsAsync()
    {
        return await Task.FromResult(_rooms.Where(r => r.IsActive));
    }

    /// <summary>
    /// Gets a specific voting room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The room or null if not found.</returns>
    public async Task<VotingRoom?> GetRoomAsync(Guid roomId)
    {
        return await Task.FromResult(_rooms.FirstOrDefault(r => r.Id == roomId));
    }

    /// <summary>
    /// Joins a user to a voting room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <returns>True if successful.</returns>
    public async Task<bool> JoinRoomAsync(Guid roomId, Guid userId)
    {
        var room = _rooms.FirstOrDefault(r => r.Id == roomId && r.IsActive);
        if (room == null || room.MemberIds.Contains(userId))
        {
            return false;
        }

        room.AddMember(userId);
        _logger.LogInformation("User {UserId} joined room {RoomId}", userId, roomId);

        return await Task.FromResult(true);
    }

    /// <summary>
    /// Starts voting in a room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user ID requesting to start voting.</param>
    /// <returns>True if successful.</returns>
    public async Task<bool> StartVotingAsync(Guid roomId, Guid userId)
    {
        var room = _rooms.FirstOrDefault(r => r.Id == roomId && r.IsActive);
        if (room == null || room.OrganizerId != userId || room.IsVotingActive)
        {
            return false;
        }

        room.IsVotingActive = true;
        room.VotingStartedAt = DateTime.UtcNow;
        _logger.LogInformation("Voting started in room {RoomId} by user {UserId}", roomId, userId);

        return await Task.FromResult(true);
    }

    /// <summary>
    /// Casts a vote for an item.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="itemId">The item ID.</param>
    /// <param name="isLike">Whether this is a like vote.</param>
    /// <returns>True if successful.</returns>
    public async Task<bool> CastVoteAsync(Guid roomId, Guid userId, Guid itemId, bool isLike)
    {
        var room = _rooms.FirstOrDefault(r => r.Id == roomId && r.IsActive && r.IsVotingActive);
        if (room == null || !room.MemberIds.Contains(userId))
        {
            return false;
        }

        // Remove any existing vote for this user/item combination
        _votes.RemoveAll(v => v.RoomId == roomId && v.UserId == userId && v.ItemId == itemId);

        var vote = new Vote
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            UserId = userId,
            ItemId = itemId,
            IsLike = isLike
        };

        _votes.Add(vote);
        _logger.LogInformation("User {UserId} voted {Vote} for item {ItemId} in room {RoomId}",
            userId, isLike ? "like" : "dislike", itemId, roomId);

        return await Task.FromResult(true);
    }

    /// <summary>
    /// Gets voting results for a room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The voting results.</returns>
    public async Task<VotingResults> GetVotingResultsAsync(Guid roomId)
    {
        var roomVotes = _votes.Where(v => v.RoomId == roomId && v.IsLike).ToList();

        var likedItems = roomVotes
            .GroupBy(v => v.ItemId)
            .Select(g => new VotedItem
            {
                ItemId = g.Key,
                VoteCount = g.Count(),
                Name = GetItemName(g.Key),
                Year = GetItemYear(g.Key),
                Type = GetItemType(g.Key)
            })
            .OrderByDescending(i => i.VoteCount)
            .ToList();

        var results = new VotingResults
        {
            RoomId = roomId,
            LikedItems = likedItems,
            Winner = likedItems.FirstOrDefault()
        };

        return await Task.FromResult(results);
    }

    /// <summary>
    /// Gets user permissions.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>User permissions.</returns>
    public async Task<UserPermissions> GetUserPermissionsAsync(Guid userId)
    {
        if (!_userPermissions.TryGetValue(userId, out var permissions))
        {
            // Default permissions - in real implementation, this would come from database
            permissions = new UserPermissions
            {
                UserId = userId,
                CanOrganize = true, // Default to true for demo
                CanVote = true
            };
            _userPermissions[userId] = permissions;
        }

        return await Task.FromResult(permissions);
    }

    private string GetItemName(Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        return item?.Name ?? "Unknown";
    }

    private int? GetItemYear(Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        return item?.ProductionYear;
    }

    private string GetItemType(Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        return item?.GetType().Name ?? "Unknown";
    }
}
