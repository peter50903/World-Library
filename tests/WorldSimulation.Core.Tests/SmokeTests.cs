using WorldSimulation.Core;
using Xunit;

namespace WorldSimulation.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreInfo_Name_IsFixedMarker()
    {
        Assert.Equal("WorldSimulation.Core", CoreInfo.Name);
    }
}
