namespace Pounce.Core.Tests;

/// <summary>Logging a repeated failure once.</summary>
public class OncePerCauseTests
{
    [Fact]
    public void Each_cause_is_first_once()
    {
        var once = new OncePerCause();

        Assert.True(once.IsFirst("GetImage 0x80004005"));
        Assert.False(once.IsFirst("GetImage 0x80004005"));
        Assert.True(once.IsFirst("GetImage 0x80070002"));
    }

    [Fact]
    public void Past_the_limit_nothing_is_first()
    {
        var once = new OncePerCause(limit: 2);

        Assert.True(once.IsFirst("a"));
        Assert.True(once.IsFirst("b"));
        Assert.False(once.IsFirst("c"));
        Assert.False(once.IsFirst("a"));
    }
}
