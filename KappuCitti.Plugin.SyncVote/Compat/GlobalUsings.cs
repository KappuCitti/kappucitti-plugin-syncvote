// Jellyfin moved its types between releases; the rest of the plugin only uses these aliases.
// Symbols JELLYFIN_10_10, JELLYFIN_10_11 and JELLYFIN_12 come from the csproj (see JellyfinVersion).
#if JELLYFIN_10_11 || JELLYFIN_12
global using JfUser = Jellyfin.Database.Implementations.Entities.User;
global using JfSortOrder = Jellyfin.Database.Implementations.Enums.SortOrder;
global using JfPermissionKind = Jellyfin.Database.Implementations.Enums.PermissionKind;
#else
global using JfUser = Jellyfin.Data.Entities.User;
global using JfSortOrder = Jellyfin.Data.Enums.SortOrder;
global using JfPermissionKind = Jellyfin.Data.Enums.PermissionKind;
#endif
