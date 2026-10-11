using KappuCitti.Plugin.SyncVote.Infrastructure;
using Xunit;

namespace KappuCitti.Plugin.SyncVote.Tests;

public class TitleMatcherTests
{
    [Theory]
    [InlineData(null, "Anything", true)]
    [InlineData("", "Anything", true)]
    [InlineData("   ", "Anything", true)]
    [InlineData("alien", "Alien", true)]
    [InlineData("ALIEN", "Aliens", true)]
    [InlineData("amelie", "Amélie", true)]
    [InlineData("Amélie", "AMELIE", true)]
    [InlineData("star wars", "Star Wars: Episode IV", true)]
    [InlineData("starwars", "Star Wars", true)]
    [InlineData("godfather", "The Godfather Part II", true)]
    [InlineData("matrix", "Alien", false)]
    [InlineData("alien3", "Alien", false)]
    public void ComparesLoosely(string? filter, string title, bool expected)
    {
        Assert.Equal(expected, TitleMatcher.Matches(filter, title));
    }

    [Fact]
    public void TheOriginalTitleCountsToo()
    {
        Assert.True(TitleMatcher.Matches("spirited", "La città incantata", "Spirited Away"));
        Assert.False(TitleMatcher.Matches("spirited", "La città incantata", null));
        Assert.False(TitleMatcher.Matches("spirited", null, null));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  matrix ", "matrix")]
    public void NormalizeTrimsAndDropsEmpty(string? input, string? expected)
    {
        Assert.Equal(expected, TitleMatcher.Normalize(input));
    }

    [Fact]
    public void NormalizeCutsLongInput()
    {
        Assert.Equal(TitleMatcher.MaxLength, TitleMatcher.Normalize(new string('a', 500))!.Length);
    }
}
