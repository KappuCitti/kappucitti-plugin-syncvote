namespace KappuCitti.Plugin.SyncVote.Services;

/// <summary>
/// Outcome of a room operation.
/// </summary>
public enum RoomError
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>The room does not exist (or is closed).</summary>
    NotFound,

    /// <summary>The caller is not a member of the room.</summary>
    NotMember,

    /// <summary>Only the organizer can do this.</summary>
    NotOrganizer,

    /// <summary>The caller is not allowed to do this.</summary>
    Forbidden,

    /// <summary>The room reached the maximum number of members.</summary>
    RoomFull,

    /// <summary>Voting is not running.</summary>
    VotingNotActive,

    /// <summary>Voting is already running.</summary>
    VotingAlreadyActive,

    /// <summary>The item does not exist.</summary>
    UnknownItem,

    /// <summary>A room is already open for the SyncPlay group.</summary>
    GroupHasRoom,

    /// <summary>No library folder was selected.</summary>
    NoCollectionSelected,

    /// <summary>A runoff needs a finished round in which at least two titles were liked.</summary>
    NotEnoughLikedTitles,

    /// <summary>The request is not valid.</summary>
    Invalid
}

/// <summary>
/// Result of a service operation: a value or an error.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
public readonly struct OperationResult<T>
{
    private OperationResult(T? value, RoomError error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Gets the value, when the operation succeeded.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Gets the error, <see cref="RoomError.None"/> on success.
    /// </summary>
    public RoomError Error { get; }

    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool Succeeded => Error == RoomError.None;

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The result.</returns>
    public static OperationResult<T> Ok(T value) => new(value, RoomError.None);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>The result.</returns>
    public static OperationResult<T> Fail(RoomError error) => new(default, error);
}
