using ConsoleMatch;

namespace ConsoleApp1.Tests;

public class CardRenderTests
{
    [Fact]
    public void InterestedCandidatesGetTheBadge()
        => Assert.Equal("50% match ★ interested", DatingApp.Badge(50, interested: true));

    [Fact]
    public void UninterestedCandidatesShowScoreOnly()
        => Assert.Equal("0% match", DatingApp.Badge(0, interested: false));

    [Fact]
    public void BadgeStaysSingleWidthSoCardBordersLineUp()
    {
        // Any char above the BMP-ish range risks rendering two columns wide, which would
        // desync the border padding. ★ (U+2605) and ASCII are safe; emoji are not.
        var badge = DatingApp.Badge(100, interested: true);
        Assert.All(badge, ch => Assert.True(ch < 0x2700, $"'{ch}' may render double-width"));
    }
}
