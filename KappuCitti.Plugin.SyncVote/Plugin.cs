using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.IO;
using KappuCitti.Plugin.SyncVote.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.Logging;

namespace KappuCitti.Plugin.SyncVote;

/// <summary>
/// The main plugin class for SyncVote.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger,
        IServerConfigurationManager configurationManager)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        // Inject SyncVote web assets (js/css) into Jellyfin's index.html, similar to johnpc/custom-javascript plugin
        try
        {
            if (!string.IsNullOrWhiteSpace(applicationPaths.WebPath))
            {
                var indexFile = Path.Combine(applicationPaths.WebPath, "index.html");
                if (File.Exists(indexFile))
                {
                    var indexContents = File.ReadAllText(indexFile);

                    // Base path (when Jellyfin is hosted under subpath)
                    var basePath = string.Empty;
                    try
                    {
                        var networkConfig = configurationManager.GetConfiguration("network");
                        var configType = networkConfig.GetType();
                        var baseUrlProp = configType.GetProperty("BaseUrl");
                        var confBasePath = baseUrlProp?.GetValue(networkConfig)?.ToString()?.Trim('/');
                        if (!string.IsNullOrEmpty(confBasePath))
                        {
                            basePath = "/" + confBasePath;
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "SyncVote: Unable to read BaseUrl from network configuration");
                    }

                    var pluginTag = "SyncVote";
                    var pluginIdNoDash = Id.ToString("N", CultureInfo.InvariantCulture);
                    string scriptElement = string.Empty;
                    try
                    {
                        var asm = GetType().Assembly;
                        var resName = string.Format(CultureInfo.InvariantCulture, "{0}.Web.syncvote.js", GetType().Namespace);
                        using var stream = asm.GetManifestResourceStream(resName);
                        if (stream != null)
                        {
                            using var reader = new StreamReader(stream);
                            var js = reader.ReadToEnd();
                            if (!string.IsNullOrWhiteSpace(js))
                            {
                                scriptElement = "<script plugin=\"" + pluginTag + "\" defer=\"defer\">" + js + "</script>";
                            }
                        }
                    }
                    catch (Exception jsEx)
                    {
                        logger.LogWarning(jsEx, "SyncVote: Failed to inline JS; continuing without script embed");
                    }

                    // Inline CSS from embedded resource to avoid 404s on some setups
                    string styleElement = string.Empty;
                    try
                    {
                        var asm = GetType().Assembly;
                        var resName = string.Format(CultureInfo.InvariantCulture, "{0}.Web.syncvote.css", GetType().Namespace);
                        using var stream = asm.GetManifestResourceStream(resName);
                        if (stream != null)
                        {
                            using var reader = new StreamReader(stream);
                            var css = reader.ReadToEnd();
                            if (!string.IsNullOrWhiteSpace(css))
                            {
                                styleElement = "<style plugin=\"" + pluginTag + "\">" + css + "</style>";
                            }
                        }
                    }
                    catch (Exception cssEx)
                    {
                        logger.LogWarning(cssEx, "SyncVote: Failed to inline CSS; continuing without style embed");
                    }

                    // Remove any previous SyncVote injected tags to avoid duplicates or stale URLs
                    var injectPattern = new Regex("<(script|link|style)\\s+[^>]*plugin=\\\"" + Regex.Escape(pluginTag) + "\\\"[^>]*>(?:</script>|</style>)?", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    indexContents = injectPattern.Replace(indexContents, string.Empty);

                    // Insert right before </body>
                    var bodyClosing = indexContents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                    if (bodyClosing != -1)
                    {
                        var injection = styleElement + scriptElement;
                        indexContents = indexContents.Insert(bodyClosing, injection);
                        File.WriteAllText(indexFile, indexContents);
                        logger.LogInformation("SyncVote: Injected web assets into {IndexFile}", indexFile);
                    }
                    else
                    {
                        logger.LogWarning("SyncVote: Could not find </body> in {IndexFile}; skipping injection", indexFile);
                    }
                }
                else
                {
                    logger.LogWarning("SyncVote: index.html not found at {Path}; skipping injection", indexFile);
                }
            }
        }
        catch (UnauthorizedAccessException uae)
        {
            logger.LogError(uae, "SyncVote: Unauthorized to modify index.html. Consider mapping writable index.html as in johnpc/custom-javascript README.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SyncVote: Error injecting web assets into index.html");
        }
    }

    /// <inheritdoc />
    public override string Name => "SyncVote";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("6bfde2dd-8211-4964-b86d-7812a9de160b");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        // Expose the configuration page, a main menu loader page, and web assets (js/css/logo) for client-side UI.
        var ns = GetType().Namespace;
        return new[]
        {
            new PluginPageInfo
            {
                Name = this.Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", ns)
            },
            new PluginPageInfo
            {
                Name = "SyncVoteLoader",
                DisplayName = "SyncVote",
                EnableInMainMenu = true,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.hook.html", ns)
            },
            new PluginPageInfo
            {
                Name = "syncvote.js",
                // EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.syncvote.js", ns)
                EmbeddedResourcePath = $"{GetType().Namespace}.Web.syncvote.js"
            },
            new PluginPageInfo
            {
                Name = "syncvote.css",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.syncvote.css", ns)
            },
            new PluginPageInfo
            {
                Name = "logo.png",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.images.logo.png", ns)
            },
            // i18n bundles for web pages
            new PluginPageInfo
            {
                Name = "i18n/en.json",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.i18n.en.json", ns)
            },
            new PluginPageInfo
            {
                Name = "i18n/it.json",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.i18n.it.json", ns)
            }
        };
    }
}
