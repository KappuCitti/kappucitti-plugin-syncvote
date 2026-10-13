using System;
using System.Collections.Generic;
using KappuCitti.Plugin.SyncVote.Configuration;
using KappuCitti.Plugin.SyncVote.Services;

namespace KappuCitti.Plugin.SyncVote.Tests;

/// <summary>The plugin settings, held in memory.</summary>
internal sealed class FakeSettings : IPluginSettings
{
    public PluginConfiguration Configuration { get; } = new();
}

/// <summary>The titles of a library, held in memory.</summary>
internal sealed class FakeItems : IItemInfoProvider
{
    private readonly Dictionary<Guid, ItemInfo> _items = new();

    public void Add(Guid id, string name, int year) => _items[id] = new ItemInfo(name, year, "Movie");

    public ItemInfo? GetItem(Guid itemId) => _items.TryGetValue(itemId, out var item) ? item : null;
}
