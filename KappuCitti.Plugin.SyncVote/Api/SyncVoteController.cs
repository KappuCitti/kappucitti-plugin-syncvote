using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using KappuCitti.Plugin.SyncVote.Models;
using KappuCitti.Plugin.SyncVote.Services;
using KappuCitti.Plugin.SyncVote.Auth;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncVoteController"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{SyncVoteController}"/> interface.</param>
    /// <param name="votingRoomService">Instance of the <see cref="VotingRoomService"/> class.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
    public SyncVoteController(
        ILogger<SyncVoteController> logger,
        VotingRoomService votingRoomService,
        IUserManager userManager,
        ISessionManager sessionManager)
    {
        _logger = logger;
        _votingRoomService = votingRoomService;
        _userManager = userManager;
        _sessionManager = sessionManager;
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
