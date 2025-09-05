// Auth/ClaimsPrincipalExtensions.cs
using System;
using System.Security.Claims;

namespace KappuCitti.Plugin.SyncVote.Auth
{
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// Estrae l'UserId (Guid) dai claim della richiesta Jellyfin.
        /// Prova NameIdentifier, poi "sub", poi "UserId".
        /// Ritorna Guid.Empty se non trova nulla.
        /// </summary>
        public static Guid GetUserId(this ClaimsPrincipal user)
        {
            if (user is null) return Guid.Empty;

            // ClaimTypes.NameIdentifier è quello standard
            var id =
                user.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                user.FindFirst("sub")?.Value ??
                user.FindFirst("UserId")?.Value ??
                user.FindFirst("nameid")?.Value;

            return Guid.TryParse(id, out var guid) ? guid : Guid.Empty;
        }
    }
}
