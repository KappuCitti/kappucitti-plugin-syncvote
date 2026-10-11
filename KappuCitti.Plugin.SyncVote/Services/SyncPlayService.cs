using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.SyncPlay;
using MediaBrowser.Controller.SyncPlay.PlaybackRequests;
using MediaBrowser.Controller.SyncPlay.Requests;
using Microsoft.Extensions.Logging;

namespace KappuCitti.Plugin.SyncVote.Services;

/// <summary>
/// A SyncPlay group as seen by the plugin.
/// </summary>
/// <param name="GroupId">The group ID.</param>
/// <param name="Name">The group name.</param>
/// <param name="MemberIds">The IDs of the users in the group.</param>
public sealed record SyncPlayGroup(string GroupId, string Name, IReadOnlyList<Guid> MemberIds);

/// <summary>
/// Talks to Jellyfin's SyncPlay: finds the group of a user and starts playback in it.
/// </summary>
public class SyncPlayService
{
    private readonly ISyncPlayManager _syncPlayManager;
    private readonly ISessionManager _sessionManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<SyncPlayService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncPlayService"/> class.
    /// </summary>
    /// <param name="syncPlayManager">The SyncPlay manager.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="logger">The logger.</param>
    public SyncPlayService(ISyncPlayManager syncPlayManager, ISessionManager sessionManager, IUserManager userManager, ILogger<SyncPlayService> logger)
    {
        _syncPlayManager = syncPlayManager;
        _sessionManager = sessionManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Finds the SyncPlay group a user is currently in.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="deviceId">The device of the current request, preferred when the user has several sessions.</param>
    /// <returns>The group, or null when the user is not in a group.</returns>
    public SyncPlayGroup? FindGroup(Guid userId, string? deviceId)
    {
        var user = _userManager.GetUserById(userId);
        var session = FindSession(userId, deviceId);
        if (user == null || session == null)
        {
            return null;
        }

        try
        {
            var group = _syncPlayManager.ListGroups(session, new ListGroupsRequest())
                .FirstOrDefault(g => g.Participants.Contains(user.Username, StringComparer.OrdinalIgnoreCase));
            if (group == null)
            {
                return null;
            }

            var members = group.Participants
                .Select(name => _userManager.GetUserByName(name)?.Id)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            return new SyncPlayGroup(group.GroupId.ToString("N"), group.GroupName, members);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to look up the SyncPlay group of user {UserId}", userId);
            return null;
        }
    }

    /// <summary>
    /// Replaces the play queue of the group with the given items and starts playing the first one.
    /// </summary>
    /// <param name="userId">The user asking (must be in the group).</param>
    /// <param name="deviceId">The device of the current request.</param>
    /// <param name="itemIds">The items to play.</param>
    /// <returns>True when the request was handed to SyncPlay.</returns>
    public async Task<bool> PlayAsync(Guid userId, string? deviceId, IReadOnlyList<Guid> itemIds)
    {
        var session = FindSession(userId, deviceId);
        if (session == null || itemIds.Count == 0)
        {
            return false;
        }

        try
        {
            await Task.Run(() => _syncPlayManager.HandleRequest(session, new PlayGroupRequest(itemIds, 0, 0), CancellationToken.None)).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SyncPlay playback request failed for user {UserId}", userId);
            return false;
        }
    }

    private SessionInfo? FindSession(Guid userId, string? deviceId)
    {
        var sessions = _sessionManager.Sessions.Where(s => s.UserId == userId).ToList();
        return sessions.FirstOrDefault(s => !string.IsNullOrEmpty(deviceId) && s.DeviceId == deviceId)
               ?? sessions.FirstOrDefault(s => s.IsActive)
               ?? sessions.FirstOrDefault();
    }
}
