using System;
using System.Collections.Generic;

namespace KappuCitti.Plugin.SyncVote.Models;

/// <summary>
/// Represents a voting room for SyncVote.
/// </summary>
public class VotingRoom
{
    private readonly List<Guid> _memberIds = new();
    private readonly List<Guid> _selectedCollections = new();
    private readonly List<string> _selectedGenres = new();
    private readonly List<string> _itemTypes = new() { "Movie" };
    private int _timeLimit = 5;

    /// <summary>
    /// Gets or sets the unique identifier for the room.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the name of the room.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SyncPlay group ID associated with this room.
    /// </summary>
    public string? SyncPlayGroupId { get; set; }

    /// <summary>
    /// Gets or sets the organizer user ID.
    /// </summary>
    public Guid OrganizerId { get; set; }

    /// <summary>
    /// Gets the member user IDs.
    /// </summary>
    public IReadOnlyList<Guid> MemberIds => _memberIds.AsReadOnly();

    /// <summary>
    /// Gets or sets a value indicating whether the room is currently active (not closed).
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether voting is in progress.
    /// </summary>
    public bool IsVotingActive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a voting round has been completed (time elapsed or ended by the organizer).
    /// </summary>
    public bool IsVotingFinished { get; set; }

    /// <summary>
    /// Gets or sets the time limit for voting in minutes (clamped between 1 and 120).
    /// </summary>
    public int TimeLimit
    {
        get => _timeLimit;
        set => _timeLimit = Math.Clamp(value, 1, 120);
    }

    /// <summary>
    /// Gets or sets the sort order for the candidates.
    /// </summary>
    public SortBy SortBy { get; set; } = SortBy.Random;

    /// <summary>
    /// Gets the selected collection IDs.
    /// </summary>
    public IReadOnlyList<Guid> SelectedCollections => _selectedCollections.AsReadOnly();

    /// <summary>
    /// Gets the selected genres.
    /// </summary>
    public IReadOnlyList<string> SelectedGenres => _selectedGenres.AsReadOnly();

    /// <summary>
    /// Gets or sets the maximum parental rating level (null = no filter).
    /// </summary>
    public int? MaxParentalRating { get; set; }

    /// <summary>
    /// Gets or sets the text the titles must contain (null = every title). Case and accents are ignored.
    /// </summary>
    public string? TitleFilter { get; set; }

    /// <summary>
    /// Gets the item types to include in voting (Movie, Series, Episode).
    /// </summary>
    public IReadOnlyList<string> ItemTypes => _itemTypes.AsReadOnly();

    /// <summary>
    /// Gets or sets the creation time (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the time (UTC) at which the current voting round started.
    /// </summary>
    public DateTime? VotingStartedAt { get; set; }

    /// <summary>
    /// Gets the time (UTC) at which the current voting round ends, if one is running.
    /// </summary>
    public DateTime? VotingEndsAt => VotingStartedAt?.AddMinutes(TimeLimit);

    /// <summary>
    /// Gets the seconds left in the running voting round, computed by the server when the room is read.
    /// </summary>
    public int? SecondsRemaining { get; private set; }

    /// <summary>
    /// Adds a member to the room.
    /// </summary>
    /// <param name="id">The user ID.</param>
    /// <returns>True if the user was added, false if already a member.</returns>
    public bool AddMember(Guid id)
    {
        if (_memberIds.Contains(id))
        {
            return false;
        }

        _memberIds.Add(id);
        return true;
    }

    /// <summary>
    /// Removes a member from the room.
    /// </summary>
    /// <param name="id">The user ID.</param>
    /// <returns>True if the user was removed.</returns>
    public bool RemoveMember(Guid id) => _memberIds.Remove(id);

    /// <summary>
    /// Replaces the selected collections.
    /// </summary>
    /// <param name="ids">The collection IDs.</param>
    public void SetSelectedCollections(IEnumerable<Guid> ids)
    {
        _selectedCollections.Clear();
        foreach (var id in ids)
        {
            if (id != Guid.Empty && !_selectedCollections.Contains(id))
            {
                _selectedCollections.Add(id);
            }
        }
    }

    /// <summary>
    /// Replaces the selected genres.
    /// </summary>
    /// <param name="genres">The genres.</param>
    public void SetSelectedGenres(IEnumerable<string> genres)
    {
        _selectedGenres.Clear();
        foreach (var genre in genres)
        {
            if (!string.IsNullOrWhiteSpace(genre) && !_selectedGenres.Contains(genre))
            {
                _selectedGenres.Add(genre);
            }
        }
    }

    /// <summary>
    /// Replaces the item types, falling back to <c>Movie</c> when none is given.
    /// </summary>
    /// <param name="types">The item types.</param>
    public void SetItemTypes(IEnumerable<string> types)
    {
        _itemTypes.Clear();
        foreach (var type in types)
        {
            if (!string.IsNullOrWhiteSpace(type) && !_itemTypes.Contains(type))
            {
                _itemTypes.Add(type);
            }
        }

        if (_itemTypes.Count == 0)
        {
            _itemTypes.Add("Movie");
        }
    }

    /// <summary>
    /// Creates an independent copy, safe to hand out while the original keeps changing.
    /// </summary>
    /// <param name="now">The current time (UTC), used to compute <see cref="SecondsRemaining"/>.</param>
    /// <returns>The copy.</returns>
    public VotingRoom Snapshot(DateTime? now = null)
    {
        var copy = new VotingRoom
        {
            Id = Id,
            Name = Name,
            SyncPlayGroupId = SyncPlayGroupId,
            OrganizerId = OrganizerId,
            IsActive = IsActive,
            IsVotingActive = IsVotingActive,
            IsVotingFinished = IsVotingFinished,
            TimeLimit = TimeLimit,
            SortBy = SortBy,
            MaxParentalRating = MaxParentalRating,
            TitleFilter = TitleFilter,
            CreatedAt = CreatedAt,
            VotingStartedAt = VotingStartedAt
        };

        if (IsVotingActive && VotingEndsAt is { } end)
        {
            copy.SecondsRemaining = Math.Max(0, (int)Math.Ceiling((end - (now ?? DateTime.UtcNow)).TotalSeconds));
        }

        copy._memberIds.AddRange(_memberIds);
        copy._selectedCollections.AddRange(_selectedCollections);
        copy._selectedGenres.AddRange(_selectedGenres);
        copy._itemTypes.Clear();
        copy._itemTypes.AddRange(_itemTypes);
        return copy;
    }
}
