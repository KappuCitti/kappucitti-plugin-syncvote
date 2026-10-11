using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using KappuCitti.Plugin.SyncVote.Infrastructure;
using KappuCitti.Plugin.SyncVote.Services;
using MediaBrowser.Controller.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace KappuCitti.Plugin.SyncVote.Api;

/// <summary>
/// Serves the web client files and the translations embedded in the plugin. They are public: the page that loads them is.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("SyncVote/Client")]
public class ClientAssetsController : ControllerBase
{
    // Only these files are served; the value is the name of the embedded resource.
    private static readonly Dictionary<string, (string Resource, string ContentType)> Assets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["syncvote.js"] = ("Web.syncvote.js", "application/javascript; charset=utf-8"),
        ["syncvote.css"] = ("Web.syncvote.css", "text/css; charset=utf-8")
    };

    // Translations are embedded as Web/i18n/<language>.json: adding a file adds a language.
    private static readonly Regex LanguageResource = new(@"\.Web\.i18n\.(?<lang>[A-Za-z]+)\.json$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();

    private readonly IServerConfigurationManager _serverConfiguration;
    private readonly IPluginSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientAssetsController"/> class.
    /// </summary>
    /// <param name="serverConfiguration">The server configuration (its display language picks the translation).</param>
    /// <param name="settings">The plugin settings.</param>
    public ClientAssetsController(IServerConfigurationManager serverConfiguration, IPluginSettings settings)
    {
        _serverConfiguration = serverConfiguration;
        _settings = settings;
    }

    /// <summary>
    /// Gets the texts of the plugin screens in the language of the server (or the one set in the plugin options).
    /// Strings missing in that language come from English.
    /// </summary>
    /// <returns>The language and its strings.</returns>
    [HttpGet("i18n")]
    public ActionResult GetTranslations()
    {
        var available = AvailableLanguages();
        var language = Localization.Pick(_settings.Configuration.Language, _serverConfiguration.Configuration.UICulture, available);

        var layers = new List<IReadOnlyDictionary<string, string>> { ReadStrings(Localization.Fallback) };
        if (!string.Equals(language, Localization.Fallback, StringComparison.OrdinalIgnoreCase))
        {
            layers.Add(ReadStrings(language));
        }

        Response.Headers[HeaderNames.CacheControl] = "no-cache";
        return Ok(new { Language = language, Available = available, Strings = Localization.Merge(layers) });
    }

    /// <summary>
    /// Gets a client file.
    /// </summary>
    /// <param name="path">The file path, e.g. <c>syncvote.js</c>.</param>
    /// <returns>The file.</returns>
    [HttpGet("{**path}")]
    public ActionResult GetAsset([FromRoute] string path)
    {
        if (!Assets.TryGetValue(path ?? string.Empty, out var asset))
        {
            return NotFound();
        }

        var content = Cache.GetOrAdd(asset.Resource, ReadResource);
        if (content.Length == 0)
        {
            return NotFound();
        }

        // The tag follows the content, so a new build is picked up by browsers that cached the previous one.
        var etag = new EntityTagHeaderValue($"\"{Convert.ToHexString(SHA256.HashData(content), 0, 8).ToLowerInvariant()}\"");
        Response.Headers[HeaderNames.CacheControl] = "no-cache";
        return File(content, asset.ContentType, lastModified: null, entityTag: etag);
    }

    private static List<string> AvailableLanguages()
    {
        return typeof(ClientAssetsController).Assembly.GetManifestResourceNames()
            .Select(name => LanguageResource.Match(name))
            .Where(m => m.Success)
            .Select(m => m.Groups["lang"].Value.ToLowerInvariant())
            .Distinct()
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, string> ReadStrings(string language)
    {
        var json = Cache.GetOrAdd($"Web.i18n.{language}.json", ReadResource);
        return json.Length == 0
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
    }

    private static byte[] ReadResource(string resource)
    {
        var assembly = typeof(ClientAssetsController).Assembly;
        using var stream = assembly.GetManifestResourceStream($"{typeof(Plugin).Namespace}.{resource}");
        if (stream == null)
        {
            return Array.Empty<byte>();
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
