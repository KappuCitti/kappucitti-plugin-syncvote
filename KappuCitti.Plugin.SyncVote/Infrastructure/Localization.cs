using System;
using System.Collections.Generic;
using System.Linq;

namespace KappuCitti.Plugin.SyncVote.Infrastructure;

/// <summary>
/// Chooses the language of the plugin screens. The server decides, not the browser: everyone in a SyncPlay group sees the same language.
/// </summary>
public static class Localization
{
    /// <summary>
    /// The language used when nothing else matches, and the one every other language falls back to for missing strings.
    /// </summary>
    public const string Fallback = "en";

    /// <summary>
    /// Value of the language option that means "follow the server".
    /// </summary>
    public const string Auto = "Auto";

    /// <summary>
    /// Picks the language to use.
    /// </summary>
    /// <param name="configured">The language set in the plugin options: <see cref="Auto"/> or a language code.</param>
    /// <param name="serverCulture">The display language of the server, e.g. <c>it-IT</c> or <c>en_US</c>.</param>
    /// <param name="available">The languages the plugin has strings for.</param>
    /// <returns>A language code from <paramref name="available"/>, or <see cref="Fallback"/>.</returns>
    public static string Pick(string? configured, string? serverCulture, IReadOnlyCollection<string> available)
    {
        if (!string.IsNullOrWhiteSpace(configured) && !string.Equals(configured, Auto, StringComparison.OrdinalIgnoreCase))
        {
            var forced = Match(configured, available);
            if (forced != null)
            {
                return forced;
            }
        }

        return Match(serverCulture, available) ?? Fallback;
    }

    /// <summary>
    /// Puts the layers on top of each other: later layers win, so a language only has to define what differs from the fallback.
    /// </summary>
    /// <param name="layers">The dictionaries, the base one first.</param>
    /// <returns>The merged dictionary.</returns>
    public static Dictionary<string, string> Merge(IEnumerable<IReadOnlyDictionary<string, string>> layers)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            foreach (var pair in layer)
            {
                result[pair.Key] = pair.Value;
            }
        }

        return result;
    }

    // "it-IT", "it_IT" and "IT" all mean "it".
    private static string? Match(string? culture, IReadOnlyCollection<string> available)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return null;
        }

        var language = culture.Split('-', '_')[0].Trim().ToLowerInvariant();
        return available.FirstOrDefault(a => string.Equals(a, language, StringComparison.OrdinalIgnoreCase));
    }
}
