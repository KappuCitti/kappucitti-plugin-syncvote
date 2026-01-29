using System;
using System.Collections.Generic;
using System.Globalization;
using KappuCitti.Plugin.SyncVote.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

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
        IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
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
        // Expose the configuration page and web assets (js/css) for client-side UI.
        // NOTE: To load the JS/CSS in Jellyfin Web, use the "File Transformation" plugin
        // or "Custom JavaScript" plugin to inject the script tag.
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
                Name = "syncvote.js",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.syncvote.js", ns)
            },
            new PluginPageInfo
            {
                Name = "syncvote.css",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.syncvote.css", ns)
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
