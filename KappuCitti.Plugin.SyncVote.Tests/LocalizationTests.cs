using System.Collections.Generic;
using KappuCitti.Plugin.SyncVote.Infrastructure;
using Xunit;

namespace KappuCitti.Plugin.SyncVote.Tests;

public class LocalizationTests
{
    private static readonly string[] Available = { "en", "it" };

    [Theory]
    [InlineData("it-IT", "it")]
    [InlineData("it_IT", "it")]
    [InlineData("IT", "it")]
    [InlineData("en-US", "en")]
    [InlineData("en-GB", "en")]
    public void TheServerLanguageIsUsedWhenThePluginIsOnAuto(string serverCulture, string expected)
    {
        Assert.Equal(expected, Localization.Pick("Auto", serverCulture, Available));
        Assert.Equal(expected, Localization.Pick(null, serverCulture, Available));
        Assert.Equal(expected, Localization.Pick("auto", serverCulture, Available));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("zh-CN")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownServerLanguageFallsBackToEnglish(string? serverCulture)
    {
        Assert.Equal("en", Localization.Pick("Auto", serverCulture, Available));
    }

    [Fact]
    public void TheLanguageSetInThePluginWinsOverTheServer()
    {
        Assert.Equal("it", Localization.Pick("it", "en-US", Available));
        Assert.Equal("en", Localization.Pick("EN", "it-IT", Available));
    }

    [Fact]
    public void AnUnknownLanguageInThePluginFollowsTheServer()
    {
        Assert.Equal("it", Localization.Pick("xx", "it-IT", Available));
        Assert.Equal("en", Localization.Pick("xx", "fr-FR", Available));
    }

    [Fact]
    public void LaterLayersOverrideEarlierOnesAndKeepTheRest()
    {
        var english = new Dictionary<string, string> { ["a"] = "A", ["b"] = "B" };
        var italian = new Dictionary<string, string> { ["b"] = "Bi" };

        var merged = Localization.Merge(new IReadOnlyDictionary<string, string>[] { english, italian });

        Assert.Equal("A", merged["a"]);
        Assert.Equal("Bi", merged["b"]);
    }
}
