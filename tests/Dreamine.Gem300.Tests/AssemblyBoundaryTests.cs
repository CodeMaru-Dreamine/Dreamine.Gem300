using Xunit;

namespace Dreamine.Gem300.Tests;

public sealed class AssemblyBoundaryTests
{
    [Fact]
    public void MarkerBelongsToExpectedAssembly()
    {
        Assert.Equal("Dreamine.Gem300", typeof(Gem300AssemblyMarker).Assembly.GetName().Name);
        Assert.IsType<Gem300AssemblyMarker>(new Gem300AssemblyMarker());
    }
}
