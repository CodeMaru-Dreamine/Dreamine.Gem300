using Dreamine.Gem300.Abstractions.Interfaces;
using Xunit;

namespace Dreamine.Gem300.Tests;

public sealed class RuntimeContractCoverageTests
{
    [Fact]
    public void RuntimeExposesEveryComposedServiceThroughPublicContract()
    {
        var concrete = new Gem300Runtime(new FakeGemRuntime(), new FakeProcessPrograms(), eventCapacity: 8);
        IGem300Runtime runtime = concrete;

        Assert.Same(concrete.Objects, runtime.Objects);
        Assert.Same(concrete.Carriers, runtime.Carriers);
        Assert.Same(concrete.Substrates, runtime.Substrates);
        Assert.Same(concrete.ProcessJobs, runtime.ProcessJobs);
        Assert.Same(concrete.ControlJobs, runtime.ControlJobs);
        Assert.Same(concrete.Events, runtime.Events);
        Assert.Equal(0, concrete.EventHealth.FailureCount);
        Assert.Same(concrete.Workflow, concrete.Workflow);
    }
}
