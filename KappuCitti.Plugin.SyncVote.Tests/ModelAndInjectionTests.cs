using System;
using KappuCitti.Plugin.SyncVote.Infrastructure;
using KappuCitti.Plugin.SyncVote.Models;
using Xunit;

namespace KappuCitti.Plugin.SyncVote.Tests;

public class VotingRoomTests
{
    [Fact]
    public void ItemTypes_FallBackToMovie()
    {
        var room = new VotingRoom();
        room.SetItemTypes(new[] { " ", "" });
        Assert.Equal(new[] { "Movie" }, room.ItemTypes);

        room.SetItemTypes(new[] { "Series", "Series", "Movie" });
        Assert.Equal(new[] { "Series", "Movie" }, room.ItemTypes);
    }

    [Fact]
    public void Collections_SkipEmptyAndDuplicateIds()
    {
        var id = Guid.NewGuid();
        var room = new VotingRoom();
        room.SetSelectedCollections(new[] { id, Guid.Empty, id });
        Assert.Equal(new[] { id }, room.SelectedCollections);
    }

    [Fact]
    public void Genres_SkipBlankAndDuplicates()
    {
        var room = new VotingRoom();
        room.SetSelectedGenres(new[] { "Drama", " ", "Drama", "Comedy" });
        Assert.Equal(new[] { "Drama", "Comedy" }, room.SelectedGenres);
    }

    [Fact]
    public void Members_AreNotAddedTwice()
    {
        var room = new VotingRoom();
        var id = Guid.NewGuid();
        Assert.True(room.AddMember(id));
        Assert.False(room.AddMember(id));
        Assert.True(room.RemoveMember(id));
        Assert.False(room.RemoveMember(id));
    }

    [Fact]
    public void SecondsRemaining_OnlyWhileVotingRuns()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var room = new VotingRoom { TimeLimit = 2, VotingStartedAt = now, IsVotingActive = true };

        Assert.Equal(90, room.Snapshot(now.AddSeconds(30)).SecondsRemaining);
        Assert.Equal(0, room.Snapshot(now.AddMinutes(5)).SecondsRemaining);

        room.IsVotingActive = false;
        Assert.Null(room.Snapshot(now).SecondsRemaining);
    }
}

public class WebClientInjectionTests
{
    [Fact]
    public void Inject_AddsTheTagsJustBeforeTheEndOfTheBody()
    {
        var html = "<html><head></head><body><div id=\"app\"></div></body></html>";
        var result = WebClientInjectionMiddleware.Inject(html, string.Empty);

        Assert.Contains("<script defer src=\"/SyncVote/Client/syncvote.js\"></script></body>", result, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"stylesheet\" href=\"/SyncVote/Client/syncvote.css\">", result, StringComparison.Ordinal);
        Assert.StartsWith("<html><head></head><body><div id=\"app\"></div>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Inject_KeepsTheBasePathOfTheServer()
    {
        var result = WebClientInjectionMiddleware.Inject("<body></body>", "/jellyfin");
        Assert.Contains("src=\"/jellyfin/SyncVote/Client/syncvote.js\"", result, StringComparison.Ordinal);
        Assert.Contains("href=\"/jellyfin/SyncVote/Client/syncvote.css\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Inject_DoesNothingTwice()
    {
        var once = WebClientInjectionMiddleware.Inject("<body></body>", string.Empty);
        Assert.Equal(once, WebClientInjectionMiddleware.Inject(once, string.Empty));
    }

    [Fact]
    public void Inject_AppendsWhenThereIsNoBodyTag()
    {
        var result = WebClientInjectionMiddleware.Inject("<p>hello</p>", string.Empty);
        Assert.EndsWith("</script>", result, StringComparison.Ordinal);
        Assert.StartsWith("<p>hello</p>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Inject_UsesTheLastBodyTag()
    {
        var html = "<body><script>var s = '</body>';</script></body>";
        var result = WebClientInjectionMiddleware.Inject(html, string.Empty);
        Assert.Contains("syncvote.js\"></script></body>", result, StringComparison.Ordinal);
        Assert.Contains("var s = '</body>';</script><link", result, StringComparison.Ordinal);
    }
}
