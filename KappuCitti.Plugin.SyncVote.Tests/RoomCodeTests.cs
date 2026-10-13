using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using KappuCitti.Plugin.SyncVote.Api;
using KappuCitti.Plugin.SyncVote.Models;
using KappuCitti.Plugin.SyncVote.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace KappuCitti.Plugin.SyncVote.Tests;

public class RoomCodeTests
{
    private static readonly Guid Organizer = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid Library = Guid.NewGuid();
    private static readonly Guid MovieA = Guid.NewGuid();

    private readonly FakeSettings _settings = new();
    private readonly FakeItems _items = new();
    private readonly CapturingLogger _log = new();
    private readonly Queue<string> _codes = new(new[] { "042917", "555111", "777222", "888333", "999444" });
    private DateTime _now = new(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);
    private readonly VotingRoomService _service;

    public RoomCodeTests()
    {
        _items.Add(MovieA, "Alien", 1979);
        _service = new VotingRoomService(_log, _items, _settings) { Clock = () => _now, CodeGenerator = () => _codes.Dequeue() };
    }

    private static CreateRoomRequest Request(bool code = true, string? scope = null) => new()
    {
        Name = "Serata",
        TimeLimit = 5,
        SelectedCollections = new List<Guid> { Library },
        RequireCode = code,
        CodeScope = scope
    };

    private int Free => _settings.Configuration.WrongCodeAttempts;

    private VotingRoom NewRoom(string? group = "group-1", bool code = true, string? scope = null)
        => _service.CreateRoom(Organizer, group, Request(code, scope)).Value!;

    // ---- a room with and without a code

    [Fact]
    public void ARoomWithoutACodeNeedsNone()
    {
        var room = NewRoom(code: false);

        Assert.False(room.RequiresCode);
        Assert.Null(_service.GetCode(room.Id));
        Assert.True(_service.JoinRoom(room.Id, Alice).Succeeded);
    }

    [Fact]
    public void ARoomWithACodeHasTheCodeTheServerMade()
    {
        var room = NewRoom();

        Assert.True(room.RequiresCode);
        Assert.Equal(RoomCodeScope.Session, room.CodeScope);
        Assert.Equal("042917", _service.GetCode(room.Id));
    }

    [Fact]
    public void TheRealGeneratorMakesSixDigits()
    {
        var service = new VotingRoomService(_log, _items, _settings);
        var made = new HashSet<string>();
        for (var i = 0; i < 40; i++)
        {
            var room = service.CreateRoom(Organizer, null, Request()).Value!;
            var code = service.GetCode(room.Id);
            Assert.Matches(new Regex(@"^\d{6}$"), code);
            made.Add(code!);
        }

        Assert.True(made.Count > 30, "the codes should not repeat");
    }

    [Fact]
    public void TheCodeIsNeverPartOfARoomOrItsJson()
    {
        var room = NewRoom();
        _service.JoinRoom(room.Id, Alice, "042917");
        var views = new[] { room, _service.GetRoom(room.Id)!, _service.GetVisibleRooms(Alice, "group-1", false).Single(), _service.StartVoting(room.Id, Organizer).Value! };

        foreach (var view in views)
        {
            Assert.Null(view.Code);
            Assert.DoesNotContain("042917", JsonSerializer.Serialize(view), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheCodeShowsInTheJsonOnlyWhereTheCallerPutsIt()
    {
        var room = NewRoom();
        room.Code = _service.GetCode(room.Id);

        Assert.Contains("\"Code\":\"042917\"", JsonSerializer.Serialize(room), StringComparison.Ordinal);
        Assert.Contains("\"RequiresCode\":true", JsonSerializer.Serialize(room), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCodeIsNeverLogged()
    {
        var room = NewRoom();
        _service.JoinRoom(room.Id, Alice, "111111");
        _service.JoinRoom(room.Id, Alice, "042917");
        _service.StartVoting(room.Id, Organizer);
        _service.EndVoting(room.Id, Organizer);
        _service.CloseRoom(room.Id, Organizer, false);

        Assert.NotEmpty(_log.Messages);
        Assert.DoesNotContain(_log.Messages, m => m.Contains("042917", StringComparison.Ordinal) || m.Contains("111111", StringComparison.Ordinal));
    }

    // ---- joining

    [Fact]
    public void JoiningNeedsTheCode()
    {
        var room = NewRoom();

        Assert.Equal(RoomError.CodeRequired, _service.JoinRoom(room.Id, Alice).Error);
        Assert.Equal(RoomError.CodeRequired, _service.JoinRoom(room.Id, Alice, "").Error);
        Assert.Equal(RoomError.CodeRequired, _service.JoinRoom(room.Id, Alice, "  - ").Error);
        Assert.Equal(RoomError.WrongCode, _service.JoinRoom(room.Id, Alice, "042918").Error);
        Assert.Equal(RoomError.WrongCode, _service.JoinRoom(room.Id, Alice, "0429170").Error);
        Assert.DoesNotContain(Alice, _service.GetRoom(room.Id)!.MemberIds);

        var joined = _service.JoinRoom(room.Id, Alice, "042917");
        Assert.True(joined.Succeeded);
        Assert.Contains(Alice, joined.Value!.MemberIds);
    }

    [Theory]
    [InlineData("042917")]
    [InlineData(" 042 917 ")]
    [InlineData("042-917")]
    public void TheCodeMayBeTypedWithSpaces(string typed)
    {
        var room = NewRoom();
        Assert.True(_service.JoinRoom(room.Id, Alice, typed).Succeeded);
    }

    [Fact]
    public void AnEnormousCodeIsJustAWrongOne()
    {
        var room = NewRoom();
        var huge = "042917" + new string('0', 1_000_000);

        // The right six digits buried in something huge do not count, and the try is counted like any other.
        Assert.Equal(RoomError.WrongCode, _service.JoinRoom(room.Id, Alice, huge).Error);
        Assert.Equal(RoomError.WrongCode, _service.JoinRoom(room.Id, Alice, new string('7', 33)).Error);
        Assert.DoesNotContain(Alice, _service.GetRoom(room.Id)!.MemberIds);
    }

    [Fact]
    public void CodesWithLettersAndSymbolsOnlyCountTheirDigits()
    {
        var room = NewRoom();

        Assert.Equal(RoomError.CodeRequired, _service.JoinRoom(room.Id, Alice, "abc!?").Error);
        Assert.Equal(RoomError.WrongCode, _service.JoinRoom(room.Id, Alice, "04a2917b9").Error);
        Assert.True(_service.JoinRoom(room.Id, Bob, "04a2917b").Succeeded);
    }

    [Fact]
    public void AMemberDoesNotNeedTheCodeAgain()
    {
        var room = NewRoom();
        _service.JoinRoom(room.Id, Alice, "042917");

        Assert.True(_service.JoinRoom(room.Id, Alice).Succeeded);
        Assert.True(_service.JoinRoom(room.Id, Organizer).Succeeded);
    }

    [Fact]
    public void AnAdministratorGetsInWithoutTheCode()
    {
        var room = NewRoom();

        Assert.Equal(RoomError.CodeRequired, _service.JoinRoom(room.Id, Admin).Error);
        Assert.True(_service.JoinRoom(room.Id, Admin, null, isAdministrator: true).Succeeded);
    }

    [Fact]
    public void AWrongCodeDoesNotTellWhetherTheRoomIsFull()
    {
        _settings.Configuration.MaxRoomMembers = 2;
        var room = NewRoom();
        Assert.True(_service.JoinRoom(room.Id, Alice, "042917").Succeeded);

        Assert.Equal(RoomError.WrongCode, _service.JoinRoom(room.Id, Bob, "000000").Error);
        Assert.Equal(RoomError.RoomFull, _service.JoinRoom(room.Id, Bob, "042917").Error);
    }

    [Fact]
    public void OnlyMembersCanVote()
    {
        var room = NewRoom();
        _service.JoinRoom(room.Id, Alice, "042917");
        _service.StartVoting(room.Id, Organizer);

        Assert.True(_service.CastVote(room.Id, Alice, MovieA, true).Succeeded);
        Assert.Equal(RoomError.NotMember, _service.CastVote(room.Id, Bob, MovieA, true).Error);
    }

    // ---- wrong codes

    [Fact]
    public void TheWrongCodesOfTheSettingLockTheUserOutForAWhile()
    {
        var room = NewRoom();
        for (var i = 0; i < Free; i++)
        {
            Assert.Equal(RoomError.WrongCode, _service.JoinRoom(room.Id, Alice, "000000").Error);
        }

        // Even the right code has to wait.
        Assert.Equal(RoomError.TooManyAttempts, _service.JoinRoom(room.Id, Alice, "042917").Error);
        Assert.Equal(30, _service.GetRetryAfterSeconds(room.Id, Alice));

        _now = _now.AddSeconds(31);
        Assert.Equal(0, _service.GetRetryAfterSeconds(room.Id, Alice));
        Assert.True(_service.JoinRoom(room.Id, Alice, "042917").Succeeded);
    }

    [Fact]
    public void EachFurtherMistakeDoublesTheWait()
    {
        var room = NewRoom();
        for (var i = 0; i < Free; i++)
        {
            _service.JoinRoom(room.Id, Alice, "000000");
        }

        _now = _now.AddSeconds(31);
        Assert.Equal(RoomError.WrongCode, _service.JoinRoom(room.Id, Alice, "000000").Error);
        Assert.Equal(60, _service.GetRetryAfterSeconds(room.Id, Alice));

        _now = _now.AddSeconds(61);
        Assert.Equal(RoomError.WrongCode, _service.JoinRoom(room.Id, Alice, "000000").Error);
        Assert.Equal(120, _service.GetRetryAfterSeconds(room.Id, Alice));
    }

    [Fact]
    public void TheWaitNeverGoesBeyondHalfAnHour()
    {
        var room = NewRoom();
        for (var i = 0; i < 40; i++)
        {
            _now = _now.AddSeconds(MaxWait + 1);
            _service.JoinRoom(room.Id, Alice, "000000");
        }

        Assert.Equal(1800, _service.GetRetryAfterSeconds(room.Id, Alice));
    }

    private const int MaxWait = 1800;

    [Fact]
    public void ALockOutIsForOneUserAndOneRoom()
    {
        var room = NewRoom();
        var other = _service.CreateRoom(Alice, "group-2", Request()).Value!;
        for (var i = 0; i < Free; i++)
        {
            _service.JoinRoom(room.Id, Alice, "000000");
        }

        Assert.Equal(RoomError.TooManyAttempts, _service.JoinRoom(room.Id, Alice, "042917").Error);
        Assert.True(_service.JoinRoom(room.Id, Bob, "042917").Succeeded);
        Assert.Equal(RoomError.WrongCode, _service.JoinRoom(other.Id, Bob, "000000").Error);
    }

    [Fact]
    public void MistakesBelowTheLimitAreFreeAndTheRightCodeStillWorks()
    {
        var room = NewRoom();
        for (var i = 0; i < Free - 1; i++)
        {
            Assert.Equal(RoomError.WrongCode, _service.JoinRoom(room.Id, Alice, "000000").Error);
        }

        Assert.Equal(0, _service.GetRetryAfterSeconds(room.Id, Alice));
        Assert.True(_service.JoinRoom(room.Id, Alice, "042917").Succeeded);
        Assert.Equal(0, _service.GetRetryAfterSeconds(room.Id, Alice));
    }

    [Fact]
    public void MistakesAreForgottenAfterAnHour()
    {
        var room = NewRoom();
        for (var i = 0; i < Free - 1; i++)
        {
            _service.JoinRoom(room.Id, Alice, "000000");
        }

        _now = _now.AddHours(2);
        for (var i = 0; i < Free - 1; i++)
        {
            Assert.Equal(RoomError.WrongCode, _service.JoinRoom(room.Id, Alice, "000000").Error);
        }

        // The old mistakes are forgotten, so these are new ones: still under the limit, no lock.
        Assert.True(_service.JoinRoom(room.Id, Alice, "042917").Succeeded);
    }

    // ---- how long the code lasts

    [Fact]
    public void ACodeForTheSessionIsTheCodeOfTheSyncPlayGroup()
    {
        var first = NewRoom("group-1");
        _service.JoinRoom(first.Id, Alice, "042917");
        _service.CloseRoom(first.Id, Organizer, false);

        var second = NewRoom("group-1");

        Assert.Equal("042917", _service.GetCode(second.Id));
        // Whoever entered the code does not enter it again, for any room of the group.
        Assert.True(_service.JoinRoom(second.Id, Alice).Succeeded);
        Assert.Equal(RoomError.CodeRequired, _service.JoinRoom(second.Id, Bob).Error);
    }

    [Fact]
    public void AnotherGroupHasAnotherCode()
    {
        var one = NewRoom("group-1");
        var two = NewRoom("group-2");

        Assert.Equal("042917", _service.GetCode(one.Id));
        Assert.Equal("555111", _service.GetCode(two.Id));
        Assert.Equal(RoomError.WrongCode, _service.JoinRoom(two.Id, Alice, "042917").Error);
    }

    [Fact]
    public void ARoomOutsideAnySyncPlayGroupHasACodeOfItsOwn()
    {
        var one = NewRoom(null);
        var two = NewRoom(null);

        Assert.NotEqual(_service.GetCode(one.Id), _service.GetCode(two.Id));
    }

    [Fact]
    public void TheCodeOfAGroupIsForgottenADayAfterItWasLastUsed()
    {
        var first = NewRoom("group-1");
        _service.CloseRoom(first.Id, Organizer, false);

        _now = _now.AddHours(25);
        var second = NewRoom("group-1");

        Assert.Equal("555111", _service.GetCode(second.Id));
    }

    [Fact]
    public void ACodeForEachRoundKeepsThePeopleOfTheFirstRound()
    {
        var room = NewRoom(scope: "Round");
        _service.JoinRoom(room.Id, Alice, "042917");

        var started = _service.StartVoting(room.Id, Organizer);

        Assert.Equal(RoomCodeScope.Round, started.Value!.CodeScope);
        Assert.Equal("042917", _service.GetCode(room.Id));
        Assert.Contains(Alice, started.Value.MemberIds);
    }

    [Fact]
    public void ACodeForEachRoundChangesWithTheNextRound()
    {
        var room = NewRoom(scope: "round");
        _service.JoinRoom(room.Id, Alice, "042917");
        _service.StartVoting(room.Id, Organizer);
        _service.EndVoting(room.Id, Organizer);

        var next = _service.StartVoting(room.Id, Organizer).Value!;

        Assert.Equal("555111", _service.GetCode(room.Id));
        Assert.Equal(new[] { Organizer }, next.MemberIds);
        Assert.Equal(RoomError.CodeRequired, _service.JoinRoom(room.Id, Alice).Error);
        Assert.Equal(RoomError.WrongCode, _service.JoinRoom(room.Id, Alice, "042917").Error);
        Assert.True(_service.JoinRoom(room.Id, Alice, "555111").Succeeded);
        Assert.Equal(RoomError.NotMember, _service.CastVote(room.Id, Bob, MovieA, true).Error);
    }

    [Fact]
    public void ACodeForEachRoundAlsoChangesForARunoff()
    {
        var room = NewRoom(scope: "Round");
        var second = Guid.NewGuid();
        _items.Add(second, "Brazil", 1985);
        _service.JoinRoom(room.Id, Alice, "042917");
        _service.StartVoting(room.Id, Organizer);
        foreach (var movie in new[] { MovieA, second })
        {
            _service.CastVote(room.Id, Alice, movie, true);
        }

        _service.EndVoting(room.Id, Organizer);
        var runoff = _service.StartVoting(room.Id, Organizer, onlyLiked: true);

        Assert.True(runoff.Succeeded);
        Assert.Equal("555111", _service.GetCode(room.Id));
        Assert.DoesNotContain(Alice, runoff.Value!.MemberIds);
    }

    [Fact]
    public void ACodeForTheSessionDoesNotChangeWithTheRound()
    {
        var room = NewRoom(scope: "Session");
        _service.JoinRoom(room.Id, Alice, "042917");
        _service.StartVoting(room.Id, Organizer);
        _service.EndVoting(room.Id, Organizer);

        var next = _service.StartVoting(room.Id, Organizer).Value!;

        Assert.Equal("042917", _service.GetCode(room.Id));
        Assert.Contains(Alice, next.MemberIds);
    }

    [Theory]
    [InlineData("round", RoomCodeScope.Round)]
    [InlineData("ROUND", RoomCodeScope.Round)]
    [InlineData("session", RoomCodeScope.Session)]
    [InlineData(null, RoomCodeScope.Session)]
    [InlineData("", RoomCodeScope.Session)]
    [InlineData("forever", RoomCodeScope.Session)]
    [InlineData("7", RoomCodeScope.Session)]
    public void TheScopeDefaultsToTheWholeSession(string? asked, RoomCodeScope expected)
    {
        Assert.Equal(expected, NewRoom(scope: asked).CodeScope);
    }

    [Fact]
    public void SevenWrongCodesAreAllowedByDefault()
    {
        Assert.Equal(7, new Configuration.PluginConfiguration().WrongCodeAttempts);

        var room = NewRoom();
        for (var i = 0; i < 6; i++)
        {
            _service.JoinRoom(room.Id, Alice, "000000");
        }

        Assert.Equal(0, _service.GetRetryAfterSeconds(room.Id, Alice));
        _service.JoinRoom(room.Id, Alice, "000000");
        Assert.Equal(30, _service.GetRetryAfterSeconds(room.Id, Alice));
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(20, 20)]
    [InlineData(500, 20)]
    public void TheNumberOfWrongCodesCanBeSetAndStaysBetweenOneAndTwenty(int configured, int effective)
    {
        _settings.Configuration.WrongCodeAttempts = configured;
        var room = NewRoom();

        for (var i = 0; i < effective - 1; i++)
        {
            _service.JoinRoom(room.Id, Alice, "000000");
        }

        Assert.Equal(0, _service.GetRetryAfterSeconds(room.Id, Alice));
        _service.JoinRoom(room.Id, Alice, "000000");
        Assert.Equal(30, _service.GetRetryAfterSeconds(room.Id, Alice));
    }

    // ---- when

    [Fact]
    public void TheCodeWorksAtAnyTimeAndWhoComesInDuringTheVotingCanVote()
    {
        var room = NewRoom();
        _service.JoinRoom(room.Id, Alice, "042917");
        _service.StartVoting(room.Id, Organizer);
        Assert.True(_service.CastVote(room.Id, Alice, MovieA, true).Succeeded);

        // Bob comes in with the code while the round is running.
        Assert.Equal(RoomError.NotMember, _service.CastVote(room.Id, Bob, MovieA, true).Error);
        Assert.True(_service.JoinRoom(room.Id, Bob, "042917").Succeeded);
        Assert.True(_service.CastVote(room.Id, Bob, MovieA, false).Succeeded);

        // And after the round, too: he is in, whatever comes next.
        _service.EndVoting(room.Id, Organizer);
        Assert.True(_service.JoinRoom(room.Id, Guid.NewGuid(), "042917").Succeeded);
    }

    // ---- the codes themselves
    [Theory]
    [InlineData("000000")]
    [InlineData("111111")]
    [InlineData("123456")]
    [InlineData("654321")]
    [InlineData("012345")]
    [InlineData("121212")]
    [InlineData("123123")]
    [InlineData("123321")]
    [InlineData("450054")]
    public void TheCodesPeopleTryFirstAreEasyToGuess(string code) => Assert.True(VotingRoomService.IsEasyToGuess(code));

    [Theory]
    [InlineData("042917")]
    [InlineData("482913")]
    [InlineData("555111")]
    [InlineData("100200")]
    [InlineData("123457")]
    public void OtherCodesAreNot(string code) => Assert.False(VotingRoomService.IsEasyToGuess(code));

    [Fact]
    public void AnEasyCodeIsNeverGiven()
    {
        _codes.Clear();
        foreach (var code in new[] { "111111", "123456", "654321", "121212", "000000", "123321", "314159" })
        {
            _codes.Enqueue(code);
        }

        Assert.Equal("314159", _service.GetCode(NewRoom().Id));
    }

    [Fact]
    public void TwoRoomsAtTheSameTimeNeverHaveTheSameCode()
    {
        _codes.Clear();
        foreach (var code in new[] { "314159", "314159", "271828" })
        {
            _codes.Enqueue(code);
        }

        var one = NewRoom("group-1");
        var two = NewRoom("group-2");

        Assert.Equal("314159", _service.GetCode(one.Id));
        Assert.Equal("271828", _service.GetCode(two.Id));
    }

    [Fact]
    public void TheCodeOfARoomThatWasClosedCanComeBack()
    {
        _codes.Clear();
        foreach (var code in new[] { "314159", "314159" })
        {
            _codes.Enqueue(code);
        }

        var one = NewRoom(null);
        _service.CloseRoom(one.Id, Organizer, false);

        Assert.Equal("314159", _service.GetCode(NewRoom(null).Id));
    }

    [Fact]
    public void TheRealGeneratorNeverGivesAnEasyCode()
    {
        var service = new VotingRoomService(_log, _items, _settings);
        for (var i = 0; i < 300; i++)
        {
            var room = service.CreateRoom(Organizer, null, Request()).Value!;
            Assert.False(VotingRoomService.IsEasyToGuess(service.GetCode(room.Id)!));
        }
    }

    // ---- closing
    [Fact]
    public void AClosedRoomHasNoCodeAnymore()
    {
        var room = NewRoom();
        _service.CloseRoom(room.Id, Organizer, false);

        Assert.Null(_service.GetCode(room.Id));
        Assert.Equal(RoomError.NotFound, _service.JoinRoom(room.Id, Alice, "042917").Error);
    }

    private sealed class CapturingLogger : ILogger<VotingRoomService>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
