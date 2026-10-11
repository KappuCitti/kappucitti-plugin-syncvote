using System.Globalization;

namespace KappuCitti.Plugin.SyncVote.Infrastructure;

/// <summary>
/// The "title contains" filter of a room. Case, accents, spaces and punctuation do not matter: "amelie" finds "Amélie".
/// </summary>
public static class TitleMatcher
{
    /// <summary>
    /// The longest filter a room accepts.
    /// </summary>
    public const int MaxLength = 60;

    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreSymbols;

    /// <summary>
    /// Cleans up a filter typed by a user.
    /// </summary>
    /// <param name="filter">The text.</param>
    /// <returns>The trimmed and shortened text, or null when there is nothing to filter on.</returns>
    public static string? Normalize(string? filter)
    {
        var text = filter?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length > MaxLength ? text[..MaxLength] : text;
    }

    /// <summary>
    /// Tells whether any of the titles contains the filter.
    /// </summary>
    /// <param name="filter">The filter; null or empty matches everything.</param>
    /// <param name="titles">The titles of an item (name, original title).</param>
    /// <returns>True when the item passes the filter.</returns>
    public static bool Matches(string? filter, params string?[] titles)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var compare = CultureInfo.InvariantCulture.CompareInfo;
        foreach (var title in titles)
        {
            if (!string.IsNullOrEmpty(title) && compare.IndexOf(title, filter, Loose) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
