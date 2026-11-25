using Pounce.Core;

namespace Pounce.Core.Tests;

public class PounceInfoTests
{
    [Fact]
    public void NameIsPounce()
    {
        Assert.Equal("Pounce", PounceInfo.Name);
    }
}
