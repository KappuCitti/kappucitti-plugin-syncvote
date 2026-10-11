#if JELLYFIN_10_11 || JELLYFIN_12
using System.Linq;
#endif

namespace KappuCitti.Plugin.SyncVote.Compat;

/// <summary>
/// Reads user data that is exposed differently by each Jellyfin generation.
/// </summary>
public static class UserCompat
{
    /// <summary>
    /// Gets whether the user is a server administrator.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns>True for administrators.</returns>
    public static bool IsAdministrator(this JfUser user)
    {
#if JELLYFIN_10_11 || JELLYFIN_12
        return user.Permissions.Any(p => p.Kind == JfPermissionKind.IsAdministrator && p.Value);
#else
        return user.HasPermission(JfPermissionKind.IsAdministrator);
#endif
    }
}
