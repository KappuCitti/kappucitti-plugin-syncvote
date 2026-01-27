using System;
using System.Collections.Generic;

namespace KappuCitti.Plugin.SyncVote.Models;

/// <summary>
/// Represents a voting room for SyncVote.
/// </summary>
public class VotingRoom
{
    /// <summary>
    /// Gets or sets the unique identifier for the room.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid(); // <- default

    /// <summary>
    /// Gets or sets the name of the room.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SyncPlay group ID associated with this room.
    /// </summary>
    public string? SyncPlayGroupId { get; set; }  // <- nullable

    /// <summary>
    /// Gets or sets the organizer user ID.
    /// </summary>
    public Guid OrganizerId { get; set; }

    /// <summary>
    /// Member user IDs (read-only surface + helpers).
    /// </summary>
    private readonly List<Guid> _memberIds = new();
    public IReadOnlyList<Guid> MemberIds => _memberIds.AsReadOnly();
    public bool AddMember(Guid id)
    {
        if (_memberIds.Contains(id)) return false;
        _memberIds.Add(id);
        return true;
    }
    public bool RemoveMember(Guid id) => _memberIds.Remove(id);

    /// <summary>
    /// Whether the room is currently active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether voting is in progress.
    /// </summary>
    public bool IsVotingActive { get; set; }

    /// <summary>
    /// Time limit for voting in minutes.
    /// </summary>
    private int _timeLimit = 5;
    public int TimeLimit
    {
        get => _timeLimit;
        set => _timeLimit = Math.Clamp(value, 1, 120);
    }

    /// <summary>
    /// Sort order for movies.
    /// </summary>
    public SortBy SortBy { get; set; } = SortBy.Random; // <- niente "Models."

    /// <summary>
    /// Selected collection IDs.
    /// </summary>
    private readonly List<Guid> _selectedCollections = new();
    public IReadOnlyList<Guid> SelectedCollections => _selectedCollections.AsReadOnly();
    public void SetSelectedCollections(IEnumerable<Guid> ids)
    {
        _selectedCollections.Clear();
        _selectedCollections.AddRange(ids);
    }

    /// <summary>
    /// Selected genres.
    /// </summary>
    private readonly List<string> _selectedGenres = new();
    public IReadOnlyList<string> SelectedGenres => _selectedGenres.AsReadOnly();
    public void SetSelectedGenres(IEnumerable<string> genres)
    {
        _selectedGenres.Clear();
        foreach (var g in genres)
            if (!string.IsNullOrWhiteSpace(g))
                _selectedGenres.Add(g);
    }

    /// <summary>
    /// Maximum parental rating level (null = no filter).
    /// </summary>
    public int? MaxParentalRating { get; set; }

    /// <summary>
    /// Item types to include in voting (e.g., Movie, Series).
    /// </summary>
    private readonly List<string> _itemTypes = new() { "Movie" };
    public IReadOnlyList<string> ItemTypes => _itemTypes.AsReadOnly();
    public void SetItemTypes(IEnumerable<string> types)
    {
        _itemTypes.Clear();
        foreach (var t in types)
            if (!string.IsNullOrWhiteSpace(t))
                _itemTypes.Add(t);
        if (_itemTypes.Count == 0)
            _itemTypes.Add("Movie");
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? VotingStartedAt { get; set; }
}
