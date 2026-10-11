using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using KappuCitti.Plugin.SyncVote.Api;
using KappuCitti.Plugin.SyncVote.Infrastructure;
using KappuCitti.Plugin.SyncVote.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Logging;

namespace KappuCitti.Plugin.SyncVote.Services;

/// <summary>
/// Reads the Jellyfin library on behalf of the voting rooms.
/// </summary>
public class LibraryService : IItemInfoProvider
{
    private static readonly TimeSpan CandidateCacheLifetime = TimeSpan.FromSeconds(60);
    private static readonly BaseItemKind[] VotableKinds = { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode };
    private const int MaxEpisodesToQueue = 200;

    private readonly ILibraryManager _libraryManager;
    private readonly ILocalizationManager _localization;
    private readonly ILogger<LibraryService> _logger;
    private readonly object _cacheLock = new();
    private readonly Dictionary<Guid, CachedCandidates> _candidateCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryService"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="localization">The localization manager.</param>
    /// <param name="logger">The logger.</param>
    public LibraryService(ILibraryManager libraryManager, ILocalizationManager localization, ILogger<LibraryService> logger)
    {
        _libraryManager = libraryManager;
        _localization = localization;
        _logger = logger;
    }

    /// <inheritdoc />
    public ItemInfo? GetItem(Guid itemId)
    {
        if (itemId == Guid.Empty)
        {
            return null;
        }

        var item = _libraryManager.GetItemById(itemId);
        return item == null ? null : new ItemInfo(item.Name, item.ProductionYear, item.GetType().Name);
    }

    /// <summary>
    /// Gets the library folders and box sets visible to a user.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns>The collections.</returns>
    public IReadOnlyList<LibraryItemInfo> GetCollections(JfUser user)
    {
        var result = new List<LibraryItemInfo>();

        // A library of type "boxsets" only holds the Jellyfin collections: those are offered as collections, not as a folder.
        foreach (var folder in _libraryManager.GetVirtualFolders().Where(f => !string.IsNullOrEmpty(f.ItemId) && !IsBoxSetLibrary(f.CollectionType)))
        {
            if (!Guid.TryParse(folder.ItemId, out var folderId))
            {
                continue;
            }

            var item = _libraryManager.GetItemById(folderId);
            if (item == null || !item.IsVisible(user))
            {
                continue;
            }

            var count = _libraryManager.GetCount(new InternalItemsQuery(user)
            {
                AncestorIds = new[] { folderId },
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true,
                IsVirtualItem = false
            });

            result.Add(new LibraryItemInfo
            {
                Id = folderId,
                Name = folder.Name ?? item.Name,
                Type = folder.CollectionType?.ToString() ?? "mixed",
                ItemCount = count
            });
        }

        var boxSets = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
            Recursive = true
        });

        foreach (var boxSet in boxSets.OfType<Folder>())
        {
            var children = boxSet.GetItems(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true
            });

            result.Add(new LibraryItemInfo
            {
                Id = boxSet.Id,
                Name = boxSet.Name,
                Type = "boxset",
                ItemCount = children.Items.Count
            });
        }

        return result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Gets the genres of the movies and series visible to a user.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns>The sorted genre names.</returns>
    public IReadOnlyList<string> GetGenres(JfUser user)
    {
        return _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true,
                IsVirtualItem = false
            })
            .SelectMany(i => i.Genres ?? Array.Empty<string>())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets the parental ratings known to the server, as the values <see cref="VotingRoom.MaxParentalRating"/> expects.
    /// </summary>
    /// <returns>The ratings, from the most permissive to the most restrictive.</returns>
    public IReadOnlyList<ParentalRatingInfo> GetParentalRatings()
    {
        return _localization.GetParentalRatings()
            .Select(r => new { r.Name, Value = RatingValue(r) })
            .Where(r => r.Value.HasValue)
            .GroupBy(r => r.Value!.Value)
            .Select(g => new ParentalRatingInfo { Value = g.Key, Name = string.Join(" / ", g.Select(x => x.Name).Distinct().Take(3)) })
            .OrderBy(r => r.Value)
            .ToList();
    }

    /// <summary>
    /// Gets the ordered IDs of the items every given member can see and that match the filters of a room.
    /// The order is the same for every member.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="members">The members; at least one.</param>
    /// <returns>The candidate IDs.</returns>
    public IReadOnlyList<Guid> GetCandidateIds(VotingRoom room, IReadOnlyList<JfUser> members)
    {
        if (members.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var memberKey = string.Join(',', members.Select(m => m.Id).OrderBy(i => i));
        lock (_cacheLock)
        {
            if (_candidateCache.TryGetValue(room.Id, out var cached) && cached.MemberKey == memberKey && cached.ExpiresAt > DateTime.UtcNow)
            {
                return cached.Ids;
            }
        }

        var first = Fetch(members[0], room);
        var allowed = first.Select(i => i.Id).ToHashSet();
        foreach (var member in members.Skip(1))
        {
            allowed.IntersectWith(Fetch(member, room).Select(i => i.Id));
        }

        var items = first.Where(i => allowed.Contains(i.Id)).ToList();
        var ids = Order(items, room);

        lock (_cacheLock)
        {
            foreach (var expired in _candidateCache.Where(c => c.Value.ExpiresAt <= DateTime.UtcNow).Select(c => c.Key).ToList())
            {
                _candidateCache.Remove(expired);
            }

            _candidateCache[room.Id] = new CachedCandidates(memberKey, DateTime.UtcNow + CandidateCacheLifetime, ids);
        }

        return ids;
    }

    /// <summary>
    /// Converts items to their candidate representation.
    /// </summary>
    /// <param name="ids">The item IDs.</param>
    /// <returns>The candidates; items that vanished from the library are skipped.</returns>
    public IReadOnlyList<CandidateItem> GetCandidates(IEnumerable<Guid> ids)
    {
        var result = new List<CandidateItem>();
        foreach (var id in ids)
        {
            var item = _libraryManager.GetItemById(id);
            if (item == null)
            {
                continue;
            }

            result.Add(new CandidateItem
            {
                Id = item.Id,
                Name = item.Name,
                Type = item.GetType().Name,
                ProductionYear = item.ProductionYear,
                Genres = item.Genres ?? Array.Empty<string>(),
                CommunityRating = item.CommunityRating,
                OfficialRating = item.OfficialRating,
                Overview = item.Overview,
                RunTimeTicks = item.RunTimeTicks
            });
        }

        return result;
    }

    /// <summary>
    /// Counts the users who cannot see an item.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <param name="users">The users to check.</param>
    /// <returns>The number of users without access.</returns>
    public int CountUsersWithoutAccess(Guid itemId, IEnumerable<JfUser> users)
    {
        if (itemId == Guid.Empty)
        {
            return 0;
        }

        var item = _libraryManager.GetItemById(itemId);
        return item == null ? 0 : users.Count(u => !item.IsVisible(u));
    }

    /// <summary>
    /// Gets whether a user can see an item.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <param name="user">The user.</param>
    /// <returns>True when the item exists and is visible.</returns>
    public bool IsVisible(Guid itemId, JfUser user)
    {
        if (itemId == Guid.Empty)
        {
            return false;
        }

        var item = _libraryManager.GetItemById(itemId);
        return item != null && item.IsVisible(user);
    }

    /// <summary>
    /// Gets the playable items for a winner: a series becomes its episodes in order.
    /// </summary>
    /// <param name="itemId">The winning item ID.</param>
    /// <param name="user">The user whose view of the library is used.</param>
    /// <returns>The IDs to queue.</returns>
    public IReadOnlyList<Guid> GetPlayableIds(Guid itemId, JfUser user)
    {
        if (itemId == Guid.Empty)
        {
            return Array.Empty<Guid>();
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return Array.Empty<Guid>();
        }

        if (item is not MediaBrowser.Controller.Entities.TV.Series)
        {
            return new[] { itemId };
        }

        return _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                AncestorIds = new[] { itemId },
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                Recursive = true,
                IsVirtualItem = false,
                OrderBy = new[]
                {
                    (ItemSortBy.ParentIndexNumber, JfSortOrder.Ascending),
                    (ItemSortBy.IndexNumber, JfSortOrder.Ascending)
                }
            })
            .Take(MaxEpisodesToQueue)
            .Select(e => e.Id)
            .ToList();
    }

    private static bool IsBoxSetLibrary(object? collectionType)
        => string.Equals(collectionType?.ToString(), "boxsets", StringComparison.OrdinalIgnoreCase);

    private static int? RatingValue(MediaBrowser.Model.Entities.ParentalRating rating)
    {
#if JELLYFIN_10_11 || JELLYFIN_12
        return rating.RatingScore?.Score;
#else
        return rating.Value;
#endif
    }

    private static IReadOnlyList<Guid> Order(List<BaseItem> items, VotingRoom room)
    {
        switch (room.SortBy)
        {
            case SortBy.Title:
                return items.OrderBy(i => i.SortName ?? i.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.Id).Select(i => i.Id).ToList();
            case SortBy.CommunityRating:
                return items.OrderByDescending(i => i.CommunityRating ?? 0).ThenBy(i => i.Id).Select(i => i.Id).ToList();
            case SortBy.PremiereDate:
                return items.OrderByDescending(i => i.PremiereDate ?? DateTime.MinValue).ThenBy(i => i.Id).Select(i => i.Id).ToList();
            default:
                // Same shuffle for every member of the room: seeded by the room, starting from a stable order.
                var ids = items.Select(i => i.Id).OrderBy(i => i).ToList();
                var random = new Random(room.Id.GetHashCode());
                for (var i = ids.Count - 1; i > 0; i--)
                {
                    var j = random.Next(i + 1);
                    (ids[i], ids[j]) = (ids[j], ids[i]);
                }

                return ids;
        }
    }

    private List<BaseItem> Fetch(JfUser user, VotingRoom room)
    {
        var found = new Dictionary<Guid, BaseItem>();

        if (room.SelectedCollections.Count == 0)
        {
            AddAll(found, _libraryManager.GetItemList(BuildQuery(user, room)));
            return MatchingTitle(found.Values, room.TitleFilter);
        }

        foreach (var collectionId in room.SelectedCollections)
        {
            var scope = collectionId == Guid.Empty ? null : _libraryManager.GetItemById(collectionId);
            if (scope == null || !scope.IsVisible(user))
            {
                continue;
            }

            var query = BuildQuery(user, room);
            if (scope is MediaBrowser.Controller.Entities.Movies.BoxSet boxSet)
            {
                // Box set members are linked children, not descendants: they need the folder's own query.
                AddAll(found, boxSet.GetItems(query).Items);
            }
            else
            {
                query.AncestorIds = new[] { collectionId };
                AddAll(found, _libraryManager.GetItemList(query));
            }
        }

        return MatchingTitle(found.Values, room.TitleFilter);
    }

    private static List<BaseItem> MatchingTitle(IEnumerable<BaseItem> items, string? filter)
        => items.Where(i => TitleMatcher.Matches(filter, i.Name, i.OriginalTitle)).ToList();

    private static void AddAll(Dictionary<Guid, BaseItem> target, IEnumerable<BaseItem> items)
    {
        foreach (var item in items)
        {
            target.TryAdd(item.Id, item);
        }
    }

    private InternalItemsQuery BuildQuery(JfUser user, VotingRoom room)
    {
        var kinds = room.ItemTypes
            .Select(t => Enum.TryParse<BaseItemKind>(t, true, out var kind) ? kind : (BaseItemKind?)null)
            .Where(k => k.HasValue && VotableKinds.Contains(k.Value))
            .Select(k => k!.Value)
            .Distinct()
            .ToArray();

        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = kinds.Length > 0 ? kinds : new[] { BaseItemKind.Movie },
            Recursive = true,
            IsVirtualItem = false
        };

        if (room.SelectedGenres.Count > 0)
        {
            query.Genres = room.SelectedGenres.ToArray();
        }

        if (room.MaxParentalRating.HasValue)
        {
#if JELLYFIN_10_11 || JELLYFIN_12
            query.MaxParentalRating = new MediaBrowser.Model.Entities.ParentalRatingScore(room.MaxParentalRating.Value, null);
#else
            query.MaxParentalRating = room.MaxParentalRating.Value;
#endif
        }

        _logger.LogDebug("Candidate query for room {RoomId}: {Kinds}", room.Id, string.Join(',', query.IncludeItemTypes.Select(k => k.ToString())));
        return query;
    }

    private sealed record CachedCandidates(string MemberKey, DateTime ExpiresAt, IReadOnlyList<Guid> Ids);
}
