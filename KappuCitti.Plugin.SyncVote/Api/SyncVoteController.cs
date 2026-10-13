using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KappuCitti.Plugin.SyncVote.Auth;
using KappuCitti.Plugin.SyncVote.Compat;
using KappuCitti.Plugin.SyncVote.Models;
using KappuCitti.Plugin.SyncVote.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace KappuCitti.Plugin.SyncVote.Api;

/// <summary>
/// SyncVote API controller. Every endpoint requires a Jellyfin-authenticated user.
/// </summary>
[ApiController]
[Authorize]
[Route("SyncVote")]
public class SyncVoteController : ControllerBase
{
    private readonly VotingRoomService _rooms;
    private readonly LibraryService _library;
    private readonly SyncPlayService _syncPlay;
    private readonly IUserManager _userManager;
    private readonly IPluginSettings _settings;
    private SyncPlayGroup? _group;
    private bool _groupLoaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncVoteController"/> class.
    /// </summary>
    /// <param name="rooms">The voting room service.</param>
    /// <param name="library">The library service.</param>
    /// <param name="syncPlay">The SyncPlay service.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    public SyncVoteController(
        VotingRoomService rooms,
        LibraryService library,
        SyncPlayService syncPlay,
        IUserManager userManager,
        IPluginSettings settings)
    {
        _rooms = rooms;
        _library = library;
        _syncPlay = syncPlay;
        _userManager = userManager;
        _settings = settings;
    }

    private Guid CurrentUserId => User.GetUserId();

    // Looked up once per request: it asks SyncPlay, and several checks need it.
    private SyncPlayGroup? CurrentGroup
    {
        get
        {
            if (!_groupLoaded)
            {
                _group = _syncPlay.FindGroup(CurrentUserId, User.GetDeviceId());
                _groupLoaded = true;
            }

            return _group;
        }
    }

    /// <summary>
    /// Creates a new voting room for the SyncPlay group of the current user.
    /// </summary>
    /// <param name="request">The room creation request.</param>
    /// <returns>The created room.</returns>
    [HttpPost("Room")]
    public ActionResult<VotingRoom> CreateRoom([FromBody] CreateRoomRequest request)
    {
        var userId = CurrentUserId;
        if (!_rooms.GetUserPermissions(userId, User.IsAdministrator()).CanOrganize)
        {
            return Failure(RoomError.Forbidden);
        }

        // The group is always the one the server sees for the user: the client cannot claim another one.
        var result = _rooms.CreateRoom(userId, CurrentGroup?.GroupId, request);
        return result.Succeeded ? Ok(ForViewer(result.Value!)) : Failure(result.Error);
    }

    /// <summary>
    /// Gets the active voting rooms visible to the current user.
    /// </summary>
    /// <returns>The rooms.</returns>
    [HttpGet("Rooms")]
    public ActionResult<IEnumerable<VotingRoom>> GetRooms()
    {
        return Ok(_rooms.GetVisibleRooms(CurrentUserId, CurrentGroup?.GroupId, User.IsAdministrator()).Select(ForViewer).ToList());
    }

    /// <summary>
    /// Gets a voting room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The room.</returns>
    [HttpGet("Room/{roomId}")]
    public ActionResult<VotingRoom> GetRoom([FromRoute] Guid roomId)
    {
        var room = FindVisibleRoom(roomId);
        return room == null ? Failure(RoomError.NotFound) : Ok(ForViewer(room));
    }

    /// <summary>
    /// Joins a voting room (joining twice is fine). A room with a code needs it in the body; the administrators do not.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="request">The code of the room, if it has one (in the body, so that it stays out of the addresses in the logs).</param>
    /// <returns>The room.</returns>
    [HttpPost("Room/{roomId}/Join")]
    public ActionResult<VotingRoom> JoinRoom([FromRoute] Guid roomId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JoinRoomRequest? request = null)
    {
        var userId = CurrentUserId;
        var room = FindVisibleRoom(roomId);
        if (room == null)
        {
            return Failure(RoomError.NotFound);
        }

        var group = CurrentGroup;
        if (room.SyncPlayGroupId != null && group?.GroupId != room.SyncPlayGroupId && !User.IsAdministrator())
        {
            // A room belongs to its SyncPlay group: only people watching together vote together.
            return Failure(RoomError.Forbidden);
        }

        var joined = _rooms.JoinRoom(roomId, userId, request?.Code, User.IsAdministrator());
        if (joined.Error == RoomError.TooManyAttempts)
        {
            var wait = _rooms.GetRetryAfterSeconds(roomId, userId);
            Response.Headers["Retry-After"] = wait.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status429TooManyRequests, new { Error = nameof(RoomError.TooManyAttempts), RetryAfterSeconds = wait });
        }

        if (!joined.Succeeded)
        {
            return Failure(joined.Error);
        }

        var updated = joined.Value!;
        if (_settings.Configuration.AutoStartVoting
            && !updated.IsVotingActive
            && !updated.IsVotingFinished
            && group != null
            && group.MemberIds.All(updated.MemberIds.Contains))
        {
            var started = _rooms.StartVoting(roomId, updated.OrganizerId);
            if (started.Succeeded)
            {
                updated = started.Value!;
            }
        }

        return Ok(ForViewer(updated));
    }

    /// <summary>
    /// Starts a voting round. Organizer only.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="onlyLiked">Runoff: vote again only on the titles liked in the round that just ended (at least two).</param>
    /// <returns>The room.</returns>
    [HttpPost("Room/{roomId}/StartVoting")]
    public ActionResult<VotingRoom> StartVoting([FromRoute] Guid roomId, [FromQuery] bool onlyLiked = false)
    {
        var result = _rooms.StartVoting(roomId, CurrentUserId, onlyLiked);
        return result.Succeeded ? Ok(ForViewer(result.Value!)) : Failure(result.Error);
    }

    /// <summary>
    /// Ends the voting round before its time limit. Organizer only.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The room.</returns>
    [HttpPost("Room/{roomId}/EndVoting")]
    public ActionResult<VotingRoom> EndVoting([FromRoute] Guid roomId)
    {
        var result = _rooms.EndVoting(roomId, CurrentUserId);
        return result.Succeeded ? Ok(ForViewer(result.Value!)) : Failure(result.Error);
    }

    /// <summary>
    /// Closes a room. Organizer or administrator only.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>No content.</returns>
    [HttpDelete("Room/{roomId}")]
    public ActionResult CloseRoom([FromRoute] Guid roomId)
    {
        var result = _rooms.CloseRoom(roomId, CurrentUserId, User.IsAdministrator());
        return result.Succeeded ? NoContent() : Failure(result.Error);
    }

    /// <summary>
    /// Casts a vote for an item.
    /// </summary>
    /// <param name="request">The vote request.</param>
    /// <returns>No content.</returns>
    [HttpPost("Vote")]
    public ActionResult CastVote([FromBody] CastVoteRequest request)
    {
        if (request.RoomId == Guid.Empty || request.ItemId == Guid.Empty)
        {
            return Failure(RoomError.Invalid);
        }

        var userId = CurrentUserId;
        if (!_rooms.GetUserPermissions(userId, User.IsAdministrator()).CanVote)
        {
            return Failure(RoomError.Forbidden);
        }

        var user = _userManager.GetUserById(userId);
        if (user == null)
        {
            return Unauthorized();
        }

        if (!_library.IsVisible(request.ItemId, user))
        {
            return Failure(RoomError.UnknownItem);
        }

        var result = _rooms.CastVote(request.RoomId, userId, request.ItemId, request.IsLike);
        return result.Succeeded ? NoContent() : Failure(result.Error);
    }

    /// <summary>
    /// Gets the ranking of a room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The results.</returns>
    [HttpGet("Room/{roomId}/Results")]
    public ActionResult<VotingResults> GetResults([FromRoute] Guid roomId)
    {
        var room = FindVisibleRoom(roomId);
        var results = room == null ? null : _rooms.GetResults(roomId);
        if (room == null || results == null)
        {
            return Failure(RoomError.NotFound);
        }

        // The ranking of a room with a code is for the people who got in with it.
        if (room.RequiresCode && !room.MemberIds.Contains(CurrentUserId) && !User.IsAdministrator())
        {
            return Failure(RoomError.NotMember);
        }

        if (results.Winner != null)
        {
            results.MembersWithoutAccess = _library.CountUsersWithoutAccess(results.Winner.ItemId, ResolveUsers(room.MemberIds));
        }

        return Ok(results);
    }

    /// <summary>
    /// Queues the winner in the SyncPlay group of the room and starts it. Organizer only.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>No content.</returns>
    [HttpPost("Room/{roomId}/PlayWinner")]
    public async Task<ActionResult> PlayWinner([FromRoute] Guid roomId)
    {
        var userId = CurrentUserId;
        var room = _rooms.GetRoom(roomId);
        if (room == null)
        {
            return Failure(RoomError.NotFound);
        }

        if (room.OrganizerId != userId)
        {
            return Failure(RoomError.NotOrganizer);
        }

        var winner = _rooms.GetResults(roomId)?.Winner;
        var organizer = _userManager.GetUserById(userId);
        var group = CurrentGroup;
        if (winner == null || organizer == null || group == null || group.GroupId != room.SyncPlayGroupId)
        {
            return Failure(RoomError.Invalid);
        }

        var ids = _library.GetPlayableIds(winner.ItemId, organizer);
        return await _syncPlay.PlayAsync(userId, User.GetDeviceId(), ids).ConfigureAwait(false)
            ? NoContent()
            : Failure(RoomError.Invalid);
    }

    /// <summary>
    /// Gets the permissions of a user (the current one unless an administrator asks for someone else).
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>The permissions.</returns>
    [HttpGet("Permissions")]
    public ActionResult<UserPermissions> GetPermissions([FromQuery] Guid? userId = null)
    {
        var target = userId ?? CurrentUserId;
        if (target != CurrentUserId && !User.IsAdministrator())
        {
            return Failure(RoomError.Forbidden);
        }

        var isAdministrator = target == CurrentUserId ? User.IsAdministrator() : _userManager.GetUserById(target)?.IsAdministrator() == true;
        return Ok(_rooms.GetUserPermissions(target, isAdministrator));
    }

    /// <summary>
    /// Gets the collections and library folders accessible to the current user.
    /// </summary>
    /// <returns>The collections.</returns>
    [HttpGet("Library/Collections")]
    public ActionResult<IEnumerable<LibraryItemInfo>> GetCollections()
    {
        var user = _userManager.GetUserById(CurrentUserId);
        return user == null ? Unauthorized() : Ok(_library.GetCollections(user));
    }

    /// <summary>
    /// Gets the genres of the library for the current user.
    /// </summary>
    /// <returns>The genres.</returns>
    [HttpGet("Library/Genres")]
    public ActionResult<IEnumerable<string>> GetGenres()
    {
        var user = _userManager.GetUserById(CurrentUserId);
        return user == null ? Unauthorized() : Ok(_library.GetGenres(user));
    }

    /// <summary>
    /// Gets the parental ratings known to the server.
    /// </summary>
    /// <returns>The ratings.</returns>
    [HttpGet("Library/ParentalRatings")]
    public ActionResult<IEnumerable<ParentalRatingInfo>> GetParentalRatings()
    {
        return Ok(_library.GetParentalRatings());
    }

    /// <summary>
    /// Gets the SyncPlay group of the current user, if any.
    /// </summary>
    /// <returns>The group information; <c>GroupId</c> is null outside a group.</returns>
    [HttpGet("SyncPlayInfo")]
    public ActionResult<SyncPlayInfo> GetSyncPlayInfo()
    {
        var userId = CurrentUserId;
        var group = CurrentGroup;
        if (group == null)
        {
            return Ok(new SyncPlayInfo());
        }

        var room = _rooms.GetVisibleRooms(userId, group.GroupId, User.IsAdministrator()).FirstOrDefault(r => r.SyncPlayGroupId == group.GroupId);
        return Ok(new SyncPlayInfo
        {
            GroupId = group.GroupId,
            GroupName = group.Name,
            IsLeader = room?.OrganizerId == userId,
            RoomState = room == null ? null : room.IsVotingActive ? "voting" : room.IsVotingFinished ? "results" : "lobby",
            MemberCount = group.MemberIds.Count,
            MemberUserIds = group.MemberIds.ToList()
        });
    }

    /// <summary>
    /// Checks whether the other members of the SyncPlay group can see the given collections.
    /// Only tells whether there is a problem, never who or what.
    /// </summary>
    /// <param name="request">The collections to check.</param>
    /// <returns>The outcome.</returns>
    [HttpPost("CheckAccess")]
    public ActionResult<AccessCheckResult> CheckCollectionAccess([FromBody] CheckAccessRequest request)
    {
        var userId = CurrentUserId;
        var others = (CurrentGroup?.MemberIds ?? Array.Empty<Guid>()).Where(id => id != userId).ToList();
        if (others.Count == 0)
        {
            return Ok(new AccessCheckResult { HasAccessIssues = false, Message = "No other members in group" });
        }

        var users = ResolveUsers(others);
        var hasIssues = (request.CollectionIds ?? new List<Guid>()).Any(id => _library.CountUsersWithoutAccess(id, users) > 0);
        return Ok(new AccessCheckResult
        {
            HasAccessIssues = hasIssues,
            Message = hasIssues ? "Some group members may not have access to all selected content" : "All members have access"
        });
    }

    /// <summary>
    /// Gets the candidate items of a room: filtered, visible to every member, in the same order for everyone,
    /// without what the current user already voted on.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="skip">Items to skip.</param>
    /// <param name="limit">Maximum items to return.</param>
    /// <returns>The candidates.</returns>
    [HttpGet("Room/{roomId}/Candidates")]
    public ActionResult<CandidateItemsResult> GetCandidates(
        [FromRoute] Guid roomId,
        [FromQuery] int skip = 0,
        [FromQuery] int limit = 20)
    {
        var userId = CurrentUserId;
        var room = _rooms.GetRoom(roomId);
        if (room == null)
        {
            return Failure(RoomError.NotFound);
        }

        if (!room.MemberIds.Contains(userId))
        {
            return Failure(RoomError.NotMember);
        }

        var ordered = _library.GetCandidateIds(room, ResolveUsers(room.MemberIds).ToList());
        var voted = _rooms.GetVotedItemIds(roomId, userId);
        var remaining = ordered.Where(id => !voted.Contains(id)).ToList();

        var page = remaining.Skip(Math.Max(0, skip)).Take(Math.Clamp(limit, 1, 100)).ToList();
        return Ok(new CandidateItemsResult
        {
            Items = _library.GetCandidates(page).ToList(),
            TotalCount = remaining.Count,
            StartIndex = Math.Max(0, skip)
        });
    }

    // The code of a room goes only to the organizer and to the administrators: everybody else just gets to know that the room has one.
    // Every room that leaves the controller goes through here.
    private VotingRoom ForViewer(VotingRoom room)
    {
        if (room.RequiresCode && (room.OrganizerId == CurrentUserId || User.IsAdministrator()))
        {
            room.Code = _rooms.GetCode(room.Id);
        }

        return room;
    }

    private VotingRoom? FindVisibleRoom(Guid roomId)
    {
        var room = _rooms.GetRoom(roomId);
        if (room == null)
        {
            return null;
        }

        var visible = _rooms.IsVisibleTo(room, CurrentUserId, User.IsAdministrator(), CurrentGroup?.GroupId);
        return visible ? room : null;
    }

    private IEnumerable<JfUser> ResolveUsers(IEnumerable<Guid> ids)
    {
        return ids.Select(id => _userManager.GetUserById(id)).Where(u => u != null).Select(u => u!);
    }

    private ActionResult Failure(RoomError error)
    {
        var status = error switch
        {
            RoomError.NotFound => StatusCodes.Status404NotFound,
            RoomError.NotMember or RoomError.NotOrganizer or RoomError.Forbidden or RoomError.CodeRequired or RoomError.WrongCode => StatusCodes.Status403Forbidden,
            RoomError.TooManyAttempts => StatusCodes.Status429TooManyRequests,
            RoomError.VotingAlreadyActive or RoomError.GroupHasRoom or RoomError.RoomFull => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return StatusCode(status, new { Error = error.ToString() });
    }
}
