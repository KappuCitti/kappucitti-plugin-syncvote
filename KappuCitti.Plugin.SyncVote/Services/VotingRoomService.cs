using System;
using System.Collections.Generic;
using System.Linq;
using KappuCitti.Plugin.SyncVote.Api;
using KappuCitti.Plugin.SyncVote.Infrastructure;
using KappuCitti.Plugin.SyncVote.Models;
using Microsoft.Extensions.Logging;

namespace KappuCitti.Plugin.SyncVote.Services;

/// <summary>
/// Manages voting rooms and votes. Rooms are ephemeral: they live in memory for the lifetime of the server.
/// Every public method is thread-safe and hands out copies, never the live room.
/// </summary>
public class VotingRoomService
{
    private static readonly TimeSpan FinishedRoomLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan MaxRoomLifetime = TimeSpan.FromHours(24);

    private readonly object _lock = new();
    private readonly ILogger<VotingRoomService> _logger;
    private readonly IItemInfoProvider _items;
    private readonly IPluginSettings _settings;
    private readonly List<VotingRoom> _rooms = new();
    private readonly List<Vote> _votes = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="VotingRoomService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="items">The library item lookup.</param>
    /// <param name="settings">The plugin settings.</param>
    public VotingRoomService(ILogger<VotingRoomService> logger, IItemInfoProvider items, IPluginSettings settings)
    {
        _logger = logger;
        _items = items;
        _settings = settings;
    }

    /// <summary>
    /// Gets or sets the clock (UTC); replaceable for tests.
    /// </summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>
    /// Creates a new voting room. A user's previous room for the same SyncPlay group is closed;
    /// the group cannot have two rooms organized by different users at the same time.
    /// </summary>
    /// <param name="organizerId">The organizer.</param>
    /// <param name="syncPlayGroupId">The SyncPlay group of the room, if any.</param>
    /// <param name="request">The request.</param>
    /// <returns>The created room.</returns>
    public OperationResult<VotingRoom> CreateRoom(Guid organizerId, string? syncPlayGroupId, CreateRoomRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return OperationResult<VotingRoom>.Fail(RoomError.Invalid);
        }

        if (!(request.SelectedCollections ?? new List<Guid>()).Any(id => id != Guid.Empty))
        {
            return OperationResult<VotingRoom>.Fail(RoomError.NoCollectionSelected);
        }

        var config = _settings.Configuration;
        var sortByName = string.IsNullOrWhiteSpace(request.SortBy) ? config.DefaultSortBy : request.SortBy;
        var sortBy = Enum.TryParse<SortBy>(sortByName, true, out var parsed) ? parsed : SortBy.Random;

        lock (_lock)
        {
            Cleanup();

            if (!string.IsNullOrEmpty(syncPlayGroupId))
            {
                foreach (var existing in _rooms.Where(r => r.IsActive && r.SyncPlayGroupId == syncPlayGroupId).ToList())
                {
                    if (existing.OrganizerId != organizerId && !existing.IsVotingFinished)
                    {
                        return OperationResult<VotingRoom>.Fail(RoomError.GroupHasRoom);
                    }

                    CloseRoomInternal(existing);
                }
            }

            var room = new VotingRoom
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                SyncPlayGroupId = string.IsNullOrEmpty(syncPlayGroupId) ? null : syncPlayGroupId,
                OrganizerId = organizerId,
                IsActive = true,
                TimeLimit = request.TimeLimit > 0 ? request.TimeLimit : config.DefaultTimeLimit,
                SortBy = sortBy,
                MaxParentalRating = request.MaxParentalRating,
                TitleFilter = TitleMatcher.Normalize(request.TitleFilter),
                CreatedAt = Clock()
            };

            room.AddMember(organizerId);
            room.SetSelectedCollections(request.SelectedCollections ?? new List<Guid>());
            room.SetSelectedGenres(request.SelectedGenres ?? new List<string>());
            room.SetItemTypes(request.ItemTypes ?? new List<string>());

            _rooms.Add(room);
            _logger.LogInformation("Created voting room {RoomId} by user {UserId}", room.Id, organizerId);

            return OperationResult<VotingRoom>.Ok(room.Snapshot(Clock()));
        }
    }

    /// <summary>
    /// Gets the active rooms a user is allowed to see.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="syncPlayGroupId">The SyncPlay group of the user, if any.</param>
    /// <param name="isAdministrator">Whether the user is a server administrator.</param>
    /// <returns>The visible rooms.</returns>
    public IReadOnlyList<VotingRoom> GetVisibleRooms(Guid userId, string? syncPlayGroupId, bool isAdministrator)
    {
        lock (_lock)
        {
            Cleanup();
            var publicRooms = _settings.Configuration.AllowPublicRooms;
            return _rooms
                .Where(r => r.IsActive)
                .Where(r => publicRooms || isAdministrator || CanSee(r, userId, syncPlayGroupId))
                .Select(r => r.Snapshot(Clock()))
                .ToList();
        }
    }

    /// <summary>
    /// Gets a room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>A copy of the room, or null.</returns>
    public VotingRoom? GetRoom(Guid roomId)
    {
        lock (_lock)
        {
            var room = FindActive(roomId);
            return room?.Snapshot(Clock());
        }
    }

    /// <summary>
    /// Gets whether a user may look at a room.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="userId">The user.</param>
    /// <param name="syncPlayGroupId">The SyncPlay group of the user, if any.</param>
    /// <param name="isAdministrator">Whether the user is a server administrator.</param>
    /// <returns>True when the room is visible to the user.</returns>
    public bool IsVisibleTo(VotingRoom room, Guid userId, bool isAdministrator, string? syncPlayGroupId)
        => isAdministrator || _settings.Configuration.AllowPublicRooms || CanSee(room, userId, syncPlayGroupId);

    /// <summary>
    /// Joins a user to a room. Joining a room twice is not an error.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <returns>The updated room.</returns>
    public OperationResult<VotingRoom> JoinRoom(Guid roomId, Guid userId)
    {
        lock (_lock)
        {
            var room = FindActive(roomId);
            if (room == null)
            {
                return OperationResult<VotingRoom>.Fail(RoomError.NotFound);
            }

            if (!room.MemberIds.Contains(userId))
            {
                if (room.MemberIds.Count >= Math.Max(1, _settings.Configuration.MaxRoomMembers))
                {
                    return OperationResult<VotingRoom>.Fail(RoomError.RoomFull);
                }

                room.AddMember(userId);
                _logger.LogInformation("User {UserId} joined room {RoomId}", userId, roomId);
            }

            return OperationResult<VotingRoom>.Ok(room.Snapshot(Clock()));
        }
    }

    /// <summary>
    /// Starts a voting round. Only the organizer can do it; the votes of a previous round are discarded.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user asking.</param>
    /// <returns>The updated room.</returns>
    public OperationResult<VotingRoom> StartVoting(Guid roomId, Guid userId)
    {
        lock (_lock)
        {
            var room = FindActive(roomId);
            if (room == null)
            {
                return OperationResult<VotingRoom>.Fail(RoomError.NotFound);
            }

            if (room.OrganizerId != userId)
            {
                return OperationResult<VotingRoom>.Fail(RoomError.NotOrganizer);
            }

            if (room.IsVotingActive)
            {
                return OperationResult<VotingRoom>.Fail(RoomError.VotingAlreadyActive);
            }

            _votes.RemoveAll(v => v.RoomId == roomId);
            room.IsVotingActive = true;
            room.IsVotingFinished = false;
            room.VotingStartedAt = Clock();
            _logger.LogInformation("Voting started in room {RoomId} by user {UserId}", roomId, userId);

            return OperationResult<VotingRoom>.Ok(room.Snapshot(Clock()));
        }
    }

    /// <summary>
    /// Ends the running voting round before its time limit. Only the organizer can do it.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user asking.</param>
    /// <returns>The updated room.</returns>
    public OperationResult<VotingRoom> EndVoting(Guid roomId, Guid userId)
    {
        lock (_lock)
        {
            var room = FindActive(roomId);
            if (room == null)
            {
                return OperationResult<VotingRoom>.Fail(RoomError.NotFound);
            }

            if (room.OrganizerId != userId)
            {
                return OperationResult<VotingRoom>.Fail(RoomError.NotOrganizer);
            }

            if (!room.IsVotingActive)
            {
                return OperationResult<VotingRoom>.Fail(RoomError.VotingNotActive);
            }

            FinishVoting(room);
            return OperationResult<VotingRoom>.Ok(room.Snapshot(Clock()));
        }
    }

    /// <summary>
    /// Closes a room. Only the organizer can do it.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user asking.</param>
    /// <param name="isAdministrator">Whether the user is a server administrator.</param>
    /// <returns>The outcome.</returns>
    public OperationResult<bool> CloseRoom(Guid roomId, Guid userId, bool isAdministrator)
    {
        lock (_lock)
        {
            var room = FindActive(roomId);
            if (room == null)
            {
                return OperationResult<bool>.Fail(RoomError.NotFound);
            }

            if (room.OrganizerId != userId && !isAdministrator)
            {
                return OperationResult<bool>.Fail(RoomError.NotOrganizer);
            }

            CloseRoomInternal(room);
            return OperationResult<bool>.Ok(true);
        }
    }

    /// <summary>
    /// Casts (or changes) a vote.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The voter.</param>
    /// <param name="itemId">The item ID.</param>
    /// <param name="isLike">Whether it is a like.</param>
    /// <returns>The outcome.</returns>
    public OperationResult<bool> CastVote(Guid roomId, Guid userId, Guid itemId, bool isLike)
    {
        lock (_lock)
        {
            var room = FindActive(roomId);
            if (room == null)
            {
                return OperationResult<bool>.Fail(RoomError.NotFound);
            }

            if (!room.MemberIds.Contains(userId))
            {
                return OperationResult<bool>.Fail(RoomError.NotMember);
            }

            if (!room.IsVotingActive)
            {
                return OperationResult<bool>.Fail(RoomError.VotingNotActive);
            }

            if (_items.GetItem(itemId) == null)
            {
                return OperationResult<bool>.Fail(RoomError.UnknownItem);
            }

            _votes.RemoveAll(v => v.RoomId == roomId && v.UserId == userId && v.ItemId == itemId);
            _votes.Add(new Vote
            {
                Id = Guid.NewGuid(),
                RoomId = roomId,
                UserId = userId,
                ItemId = itemId,
                IsLike = isLike,
                VotedAt = Clock()
            });

            _logger.LogDebug("User {UserId} voted {Vote} for item {ItemId} in room {RoomId}", userId, isLike ? "like" : "dislike", itemId, roomId);
            return OperationResult<bool>.Ok(true);
        }
    }

    /// <summary>
    /// Gets the items a user already voted on in a room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user.</param>
    /// <returns>The item IDs.</returns>
    public IReadOnlySet<Guid> GetVotedItemIds(Guid roomId, Guid userId)
    {
        lock (_lock)
        {
            return _votes.Where(v => v.RoomId == roomId && v.UserId == userId).Select(v => v.ItemId).ToHashSet();
        }
    }

    /// <summary>
    /// Gets the ranking of a room: most liked first, then fewest dislikes, then earliest like.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The results, or null if the room does not exist.</returns>
    public VotingResults? GetResults(Guid roomId)
    {
        lock (_lock)
        {
            var room = FindActive(roomId);
            if (room == null)
            {
                return null;
            }

            var ranked = _votes
                .Where(v => v.RoomId == roomId)
                .GroupBy(v => v.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    Likes = g.Count(v => v.IsLike),
                    Dislikes = g.Count(v => !v.IsLike),
                    FirstLike = g.Where(v => v.IsLike).Select(v => v.VotedAt).DefaultIfEmpty(DateTime.MaxValue).Min()
                })
                .Where(x => x.Likes > 0)
                .OrderByDescending(x => x.Likes)
                .ThenBy(x => x.Dislikes)
                .ThenBy(x => x.FirstLike)
                .ToList();

            var liked = ranked.Select(x =>
            {
                var info = _items.GetItem(x.ItemId);
                return new VotedItem
                {
                    ItemId = x.ItemId,
                    VoteCount = x.Likes,
                    DislikeCount = x.Dislikes,
                    Name = info?.Name ?? "Unknown",
                    Year = info?.Year,
                    Type = info?.Type ?? "Unknown"
                };
            }).ToList();

            return new VotingResults
            {
                RoomId = roomId,
                IsVotingFinished = room.IsVotingFinished,
                LikedItems = liked,
                Winner = liked.FirstOrDefault()
            };
        }
    }

    /// <summary>
    /// Gets the permissions of a user: server administrators can always do everything.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="isAdministrator">Whether the user is a server administrator.</param>
    /// <returns>The permissions.</returns>
    public UserPermissions GetUserPermissions(Guid userId, bool isAdministrator)
    {
        var config = _settings.Configuration;
        var overridden = config.UserPermissionOverrides.FirstOrDefault(p => p.UserId == userId);

        return new UserPermissions
        {
            UserId = userId,
            CanOrganize = isAdministrator || (overridden?.CanOrganize ?? config.DefaultCanOrganize),
            CanVote = isAdministrator || (overridden?.CanVote ?? config.DefaultCanVote)
        };
    }

    private static bool CanSee(VotingRoom room, Guid userId, string? syncPlayGroupId)
        => room.OrganizerId == userId
           || room.MemberIds.Contains(userId)
           || (!string.IsNullOrEmpty(syncPlayGroupId) && room.SyncPlayGroupId == syncPlayGroupId);

    // Callers hold _lock.
    private VotingRoom? FindActive(Guid roomId)
    {
        var room = _rooms.FirstOrDefault(r => r.Id == roomId && r.IsActive);
        if (room != null)
        {
            ExpireVoting(room);
        }

        return room;
    }

    private void ExpireVoting(VotingRoom room)
    {
        if (room.IsVotingActive && room.VotingEndsAt is { } end && Clock() >= end)
        {
            FinishVoting(room);
        }
    }

    private void FinishVoting(VotingRoom room)
    {
        room.IsVotingActive = false;
        room.IsVotingFinished = true;
        _logger.LogInformation("Voting finished in room {RoomId}", room.Id);
    }

    private void CloseRoomInternal(VotingRoom room)
    {
        room.IsActive = false;
        room.IsVotingActive = false;
        _votes.RemoveAll(v => v.RoomId == room.Id);
        _logger.LogInformation("Closed voting room {RoomId}", room.Id);
    }

    private void Cleanup()
    {
        var now = Clock();
        foreach (var room in _rooms.ToList())
        {
            ExpireVoting(room);
            var finishedLongAgo = room.IsVotingFinished && room.VotingEndsAt is { } end && now - end > FinishedRoomLifetime;
            var tooOld = now - room.CreatedAt > MaxRoomLifetime;
            if (!room.IsActive || finishedLongAgo || tooOld)
            {
                if (room.IsActive)
                {
                    CloseRoomInternal(room);
                }

                _rooms.Remove(room);
            }
        }
    }
}
