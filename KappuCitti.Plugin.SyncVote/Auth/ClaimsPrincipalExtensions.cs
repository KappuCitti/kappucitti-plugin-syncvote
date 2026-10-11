using System;
using System.Security.Claims;

namespace KappuCitti.Plugin.SyncVote.Auth;

/// <summary>
/// Helpers to read the Jellyfin authentication claims.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    // Claim types used by Jellyfin's authentication handler.
    private const string JellyfinUserId = "Jellyfin-UserId";
    private const string JellyfinDeviceId = "Jellyfin-DeviceId";
    private const string AdministratorRole = "Administrator";

    /// <summary>
    /// Gets the ID of the authenticated user, or <see cref="Guid.Empty"/> if there is none.
    /// </summary>
    /// <param name="user">The principal.</param>
    /// <returns>The user ID.</returns>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        if (user is null)
        {
            return Guid.Empty;
        }

        var id = user.FindFirst(JellyfinUserId)?.Value
                 ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? user.FindFirst("sub")?.Value
                 ?? user.FindFirst("UserId")?.Value
                 ?? user.FindFirst("nameid")?.Value;

        return Guid.TryParse(id, out var guid) ? guid : Guid.Empty;
    }

    /// <summary>
    /// Gets the ID of the device of the current request, if known.
    /// </summary>
    /// <param name="user">The principal.</param>
    /// <returns>The device ID or null.</returns>
    public static string? GetDeviceId(this ClaimsPrincipal user) => user?.FindFirst(JellyfinDeviceId)?.Value;

    /// <summary>
    /// Gets whether the authenticated user is a server administrator.
    /// </summary>
    /// <param name="user">The principal.</param>
    /// <returns>True for administrators.</returns>
    public static bool IsAdministrator(this ClaimsPrincipal user) => user?.IsInRole(AdministratorRole) == true;
}
